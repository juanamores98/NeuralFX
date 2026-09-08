using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NeuralFX.Hub.Services;

internal sealed record UninstallPlan(string GameDirectory, Dictionary<string, string> Files, string[] Directories)
{
    public bool HasWork => Files.Count != 0 || Directories.Length != 0;
    public string Summary => $"Retirar: {Files.Count} archivos · Eliminar: {Directories.Length} carpetas vacías";
    public string Details => "Desinstalar los componentes gráficos de NeuralFX de esta carpeta del juego. No se recuperará ninguna instalación antigua de ReShade o DLSS.\n\n" +
        "Incluye los archivos del catálogo actual, los destinos que utilizaba el Hub antiguo, registros y volcados del feeder, caché/capturas privadas y copias de restauración dentro del juego. Los archivos modificados o preexistentes de esta lista también se retiran; se guardará una copia verificada fuera del juego antes de eliminarlos.\n\n" +
        "El Hub, el mod administrado NeuralFX (en Addons/Mods), las descargas, las partidas y los archivos ajenos a esta lista permanecen.\n\n" +
        "Archivos que se retirarán:\n" + (Files.Count == 0 ? "Ninguno." : string.Join("\n", Files.Keys.Select(x => "• " + x))) +
        "\n\nCarpetas que se eliminarán solo si quedan vacías:\n" + (Directories.Length == 0 ? "Ninguna." : string.Join("\n", Directories.Select(x => "• " + x))) +
        "\n\nLa desinstalación solo se dará por completada si la comprobación final no encuentra residuos de este inventario. No se borran carpetas compartidas que contengan recursos ajenos.";
}
internal sealed record UninstallResult(bool Success, string Message, string? ArchiveDirectory = null);

internal sealed class UninstallService
{
    private readonly Func<bool> _isGameRunning;
    private readonly string _storageRoot;
    public string ArchiveRoot { get; }
    internal Action<int>? AfterWrite { get; set; }
    public UninstallService(string? storageRoot = null, Func<bool>? isGameRunning = null)
    {
        _storageRoot = storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuralFX");
        ArchiveRoot = Path.Combine(_storageRoot, "Backups", "Uninstall");
        _isGameRunning = isGameRunning ?? HardwareDiagnosticsService.IsCitiesSkylinesRunning;
    }
    public static UninstallPlan Inspect(string gameDirectory)
    {
        string root = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(ManagedPaths.Resolve(root, "Cities.exe"))) throw new IOException("No se encontró Cities.exe en la carpeta seleccionada.");
        // Recovery must finish before offering a new destructive plan. Do not silently discard its journal.
        if (Directory.Exists(ManagedPaths.Resolve(root, ".neuralfx-transaction")))
            throw new IOException("Hay una operación interrumpida. Recupera esa operación antes de volver a revisar la desinstalación.");
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddFile(string relative)
        {
            string path = ManagedPaths.Resolve(root, relative);
            if (Directory.Exists(path)) throw new IOException("Se esperaba un archivo, pero existe una carpeta: " + relative);
            if (File.Exists(path)) files[PipelineFootprint.Normalize(relative)] = DependencyManagerService.CalculateSha256(path);
        }
        void Visit(string relative)
        {
            string path = ManagedPaths.Resolve(root, relative);
            if (File.Exists(path)) throw new IOException("Se esperaba una carpeta privada: " + relative);
            if (!Directory.Exists(path)) return;
            directories.Add(PipelineFootprint.Normalize(relative));
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                string child = PipelineFootprint.Normalize(Path.GetRelativePath(root, entry));
                ManagedPaths.Resolve(root, child); // Reject links before considering recursion.
                if (Directory.Exists(entry)) Visit(child); else AddFile(child);
            }
        }
        foreach (string relative in PipelineFootprint.Files) AddFile(relative);
        foreach (string relative in PipelineFootprint.PrivateDirectories) Visit(relative);
        foreach (string relative in PipelineFootprint.SharedDirectories.OrderByDescending(x => x.Length))
        {
            string path = ManagedPaths.Resolve(root, relative);
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).All(x =>
            {
                string child = PipelineFootprint.Normalize(Path.GetRelativePath(root, x));
                return files.ContainsKey(child) || directories.Contains(child);
            })) directories.Add(relative);
        }
        return new(root, files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
            directories.OrderByDescending(x => x.Length).ThenBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
    }
    internal static void RequireSamePlan(UninstallPlan approved, UninstallPlan current)
    {
        if (!approved.GameDirectory.Equals(current.GameDirectory, StringComparison.OrdinalIgnoreCase) ||
            approved.Files.Count != current.Files.Count || approved.Files.Any(x => !current.Files.TryGetValue(x.Key, out var hash) || hash != x.Value) ||
            !approved.Directories.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(current.Directories))
            throw new IOException("Los archivos cambiaron desde la vista previa. Vuelve a revisar la desinstalación antes de confirmar.");
    }
    public Task<UninstallResult> UninstallAsync(UninstallPlan approved, Action<string>? log = null) => Task.Run(() =>
    {
        string? archive = null;
        try
        {
            if (_isGameRunning()) throw new IOException("Cierra Cities: Skylines antes de desinstalar.");
            using var lease = new InstallationLease(approved.GameDirectory);
            FileTransaction.Recover(approved.GameDirectory);
            var plan = Inspect(approved.GameDirectory);
            RequireSamePlan(approved, plan);
            if (!plan.HasWork) return new UninstallResult(true, "No quedan componentes ni residuos del inventario NeuralFX en la carpeta del juego.");
            string archiveRoot = ManagedPaths.Resolve(_storageRoot, "Backups/Uninstall").TrimEnd(Path.DirectorySeparatorChar);
            if ((archiveRoot + Path.DirectorySeparatorChar).StartsWith(plan.GameDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Las copias de desinstalación deben quedar fuera de la carpeta del juego.");
            archive = ManagedPaths.Resolve(archiveRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(archive);
            foreach (var file in plan.Files)
            {
                string target = ManagedPaths.Resolve(archive, "files/" + file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(ManagedPaths.Resolve(plan.GameDirectory, file.Key), target);
                if (DependencyManagerService.CalculateSha256(target) != file.Value) throw new IOException("La copia de seguridad no coincide: " + file.Key);
            }
            ManagedPaths.WriteAtomic(ManagedPaths.Resolve(archive, "uninstall.json"), JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
            RequireSamePlan(plan, Inspect(plan.GameDirectory));
            if (_isGameRunning()) throw new IOException("El juego se abrió durante la preparación; no se desinstala.");
            // Fail before deleting anything when a runtime is still locked or read-only.
            foreach (string relative in plan.Files.Keys)
                using (var probe = new FileStream(ManagedPaths.Resolve(plan.GameDirectory, relative), FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
            using (var transaction = new FileTransaction(plan.GameDirectory))
            {
                foreach (string file in plan.Files.Keys) transaction.Write(file, null);
                transaction.Commit(AfterWrite);
            }
            foreach (string relative in plan.Directories)
            {
                string path = ManagedPaths.Resolve(plan.GameDirectory, relative);
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            var remaining = Inspect(plan.GameDirectory);
            if (remaining.HasWork) throw new IOException("La comprobación final detectó residuos: " + string.Join(", ", remaining.Files.Keys.Concat(remaining.Directories)));
            string message = "Desinstalación verificada: no quedan componentes ni residuos del inventario NeuralFX en la carpeta del juego. Copias de recuperación: " + archive;
            log?.Invoke(message);
            return new UninstallResult(true, message, archive);
        }
        catch (Exception ex)
        {
            string message = "No se pudo completar y verificar la desinstalación: " + ex.Message;
            if (archive != null) message += " Copias conservadas: " + archive;
            log?.Invoke(message);
            return new UninstallResult(false, message, archive);
        }
    });
    public Task RecoverAsync(string root) => Task.Run(() =>
    {
        if (_isGameRunning()) throw new IOException("Cierra Cities: Skylines antes de recuperar la operación.");
        using var lease = new InstallationLease(root);
        FileTransaction.Recover(root);
    });
}
