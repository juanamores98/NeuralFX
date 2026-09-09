using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class InstallationEngineService
    {
        private readonly DependencyManagerService _dependencies;
        private readonly Func<bool> _isGameRunning;
        internal Action<int>? AfterWrite { get; set; }
        public InstallationEngineService(DependencyManagerService dependencyManager, Func<bool>? isGameRunning = null)
        { _dependencies = dependencyManager; _isGameRunning = isGameRunning ?? HardwareDiagnosticsService.IsCitiesSkylinesRunning; }

        public Task<bool> InstallAsync(string gameDirectory, List<DependencyItem> items, Action<string>? logAction = null, bool enforceProcessClosed = true, PipelinePreset preset = PipelinePreset.Native, InstallationPreview? preview = null)
            => Task.Run(() => Install(gameDirectory, items, logAction, enforceProcessClosed, preset, preview));

        private bool Install(string root, List<DependencyItem> items, Action<string>? log, bool enforceClosed, PipelinePreset preset, InstallationPreview? preview)
        {
            try
            {
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
                if (enforceClosed && _isGameRunning()) throw new IOException("Cierra Cities: Skylines antes de instalar.");
                using var lease = new InstallationLease(root);
                FileTransaction.Recover(root);
                preview?.ValidateUnchanged(root);
                string manifestPath = ManagedPaths.Resolve(root, "NeuralFX_Manifest.json");
                byte[]? previousManifest = null;
                var manifest = new InstallationManifest { SchemaVersion = 2, GameDirectory = Path.GetFullPath(root) };
                if (File.Exists(manifestPath))
                {
                    var existing = JsonSerializer.Deserialize<InstallationManifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException("Manifiesto vacío.");
                    if (existing.SchemaVersion == 2) manifest = ManifestStore.Read(root);
                    else
                    {
                        if (existing.SchemaVersion is not (0 or 1) || existing.ToolName != "NeuralFX" || !Path.GetFullPath(existing.GameDirectory).TrimEnd('\\').Equals(Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Manifiesto desconocido: no se modifica.");
                        // Legacy ownership/backups were incomplete. Snapshot the current installation as the upgrade baseline.
                        previousManifest = File.ReadAllBytes(manifestPath);
                        manifest.PreviousManifestBackup = ".neuralfx-backups/" + Guid.NewGuid().ToString("N") + ".manifest.json";
                        manifest.PreviousManifestSha256 = DependencyManagerService.Hash(previousManifest);
                        log?.Invoke("Migración: el rollback restaurará el estado actual de la instalación antigua, incluido su manifiesto. Sus backups originales se conservan.");
                    }
                }
                var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items.Where(x => !x.IsEmbedded))
                {
                    if (!item.IsRequired && !item.IsInCache) continue;
                    foreach (var file in _dependencies.ReadPayload(item)) payload.Add(file.Key, file.Value);
                }
                foreach (var file in PipelineConfiguration.Create(root, preset)) payload.Add(file.Key, file.Value);
                // Complete validation and payload preparation precedes all writes to the game.
                foreach (string relative in payload.Keys) ManagedPaths.Resolve(root, relative);
                using var transaction = new FileTransaction(root);
                // ReShade falls back to the shared system temp directory if its configured cache folder does not exist.
                const string runtimeCache = ".neuralfx-runtime/ReShade";
                transaction.EnsureDirectory(runtimeCache);
                foreach (string relative in new[] { runtimeCache }.Concat(PipelineFootprint.Parents(runtimeCache)))
                    if (!Directory.Exists(ManagedPaths.Resolve(root, relative)) && !manifest.InstalledDirectories.Contains(relative)) manifest.InstalledDirectories.Add(relative);
                if (previousManifest != null) transaction.Write(manifest.PreviousManifestBackup!, previousManifest);
                foreach (var file in payload)
                {
                    string destination = ManagedPaths.Resolve(root, file.Key);
                    if (!manifest.InstalledFiles.Contains(file.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        if (File.Exists(destination))
                        {
                            string backup = ".neuralfx-backups/" + Guid.NewGuid().ToString("N") + ".bin";
                            byte[] original = File.ReadAllBytes(destination);
                            transaction.Write(backup, original);
                            manifest.BackedUpFiles.Add(file.Key, backup);
                            manifest.BackupChecksums.Add(file.Key, DependencyManagerService.Hash(original));
                        }
                        manifest.InstalledFiles.Add(file.Key);
                    }
                    for (string? dir = Path.GetDirectoryName(destination); dir != null && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
                    {
                        string relative = Path.GetRelativePath(root, dir);
                        if (!manifest.InstalledDirectories.Contains(relative)) manifest.InstalledDirectories.Add(relative);
                    }
                    transaction.Write(file.Key, file.Value);
                    manifest.FileChecksums[file.Key] = DependencyManagerService.Hash(file.Value);
                }
                string gameExe = ManagedPaths.Resolve(root, "Cities.exe");
                manifest.InstalledAt = DateTime.UtcNow;
                manifest.GameExecutableSha256 = File.Exists(gameExe) ? DependencyManagerService.CalculateSha256(gameExe) : null;
                transaction.Write("NeuralFX_Manifest.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })));
                if (enforceClosed && _isGameRunning()) throw new IOException("El juego se abrió durante la preparación. Instalación cancelada.");
                preview?.ValidateUnchanged(root);
                transaction.Commit(AfterWrite);
                log?.Invoke("Instalación registrada. Comprueba en juego el efecto, los buffers y las evaluaciones; archivos presentes no significa inferencia activa.");
                return true;
            }
            catch (Exception ex) { log?.Invoke("Instalación cancelada/recuperada: " + ex.Message); return false; }
        }
    }

    internal static class ManifestStore
    {
        public static InstallationManifest Read(string root)
        {
            var manifest = JsonSerializer.Deserialize<InstallationManifest>(File.ReadAllText(ManagedPaths.Resolve(root, "NeuralFX_Manifest.json"))) ?? throw new InvalidDataException("Manifiesto vacío.");
            if (manifest.SchemaVersion != 2 || manifest.ToolName != "NeuralFX") throw new InvalidDataException("Manifiesto antiguo o desconocido: se conserva sin borrar recursos. Requiere revisar y migrar su instalación.");
            if (manifest.InstalledFiles == null || manifest.InstalledDirectories == null || manifest.FileChecksums == null || manifest.BackedUpFiles == null || manifest.BackupChecksums == null || string.IsNullOrEmpty(manifest.GameDirectory)) throw new InvalidDataException("Manifiesto incompleto.");
            if (!string.Equals(Path.GetFullPath(manifest.GameDirectory).TrimEnd('\\', '/'), Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("El manifiesto corresponde a otro directorio.");
            if (manifest.InstalledFiles.Select(p => ManagedPaths.Resolve(root, p)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.InstalledFiles.Count) throw new InvalidDataException("Archivos duplicados.");
            foreach (string path in manifest.InstalledFiles)
            {
                ManagedPaths.Resolve(root, path);
                if (!manifest.FileChecksums.ContainsKey(path) || path.StartsWith(".neuralfx", StringComparison.OrdinalIgnoreCase) || path.Equals("Cities.exe", StringComparison.OrdinalIgnoreCase) || path.Equals("NeuralFX_Manifest.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entrada de manifiesto inválida.");
            }
            foreach (string dir in manifest.InstalledDirectories) ManagedPaths.Resolve(root, dir);
            foreach (var backup in manifest.BackedUpFiles)
            {
                if (!manifest.InstalledFiles.Contains(backup.Key) || !backup.Value.StartsWith(".neuralfx-backups/", StringComparison.Ordinal) || !manifest.BackupChecksums.ContainsKey(backup.Key)) throw new InvalidDataException("Backup inválido.");
                string path = ManagedPaths.Resolve(root, backup.Value);
                if (!File.Exists(path) || DependencyManagerService.CalculateSha256(path) != manifest.BackupChecksums[backup.Key]) throw new InvalidDataException("Backup ausente o alterado: " + backup.Key);
            }
            if (manifest.PreviousManifestBackup != null)
            {
                if (!manifest.PreviousManifestBackup.StartsWith(".neuralfx-backups/", StringComparison.Ordinal)) throw new InvalidDataException("Backup de manifiesto inválido.");
                string path = ManagedPaths.Resolve(root, manifest.PreviousManifestBackup);
                if (!File.Exists(path) || DependencyManagerService.CalculateSha256(path) != manifest.PreviousManifestSha256) throw new InvalidDataException("Backup de manifiesto ausente o alterado.");
            }
            return manifest;
        }
    }
}
