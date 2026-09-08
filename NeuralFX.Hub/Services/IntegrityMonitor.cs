using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services;

internal enum InstalledFileState { Missing, Unmanaged, Unverified, Verified, Modified, ConfigurationChanged }
internal sealed record IntegrityReport(bool Managed, bool Valid, bool NeedsRepair, string Summary, string[] Details)
{
    public Dictionary<string, InstalledFileState> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RecordedChecksums { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public RollbackPreview? Restoration { get; init; }
    public bool CanMigrate { get; init; }
}

internal sealed class IntegrityMonitor : IDisposable
{
    private FileSystemWatcher? _watcher;
    private HashSet<string> _tracked = new(StringComparer.OrdinalIgnoreCase);
    private long _revision = 1;
    public long Revision => Interlocked.Read(ref _revision);
    public void Invalidate() => Interlocked.Increment(ref _revision);
    public void Watch(string root, IEnumerable<string> files)
    {
        Dispose();
        _tracked = new(files.Select(x => Path.GetFullPath(Path.Combine(root, x))), StringComparer.OrdinalIgnoreCase);
        _tracked.Add(Path.Combine(root, "NeuralFX_Manifest.json")); _tracked.Add(Path.Combine(root, "Cities.exe"));
        _watcher = new(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.DirectoryName };
        _watcher.Changed += Changed; _watcher.Created += Changed; _watcher.Deleted += Changed;
        _watcher.Renamed += (_, e) => { if (_tracked.Contains(e.OldFullPath) || _tracked.Contains(e.FullPath) || !Path.HasExtension(e.FullPath)) Invalidate(); };
        _watcher.Error += (_, _) => Invalidate();
        _watcher.EnableRaisingEvents = true;
        Invalidate();
    }
    private void Changed(object sender, FileSystemEventArgs e) { if (_tracked.Contains(e.FullPath) || !Path.HasExtension(e.FullPath)) Invalidate(); }
    public static IntegrityReport Scan(string root, IEnumerable<string>? expectedFiles = null)
    {
        var files = new Dictionary<string, InstalledFileState>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string relative in expectedFiles ?? Array.Empty<string>())
                files[relative.Replace('\\', '/')] = File.Exists(ManagedPaths.Resolve(root, relative)) ? InstalledFileState.Unmanaged : InstalledFileState.Missing;
            if (!File.Exists(ManagedPaths.Resolve(root, "NeuralFX_Manifest.json")))
                return new(false, true, false, "SIN INSTALACIÓN REGISTRADA", Array.Empty<string>()) { Files = files };
            var existing = System.Text.Json.JsonSerializer.Deserialize<InstallationManifest>(File.ReadAllText(ManagedPaths.Resolve(root, "NeuralFX_Manifest.json")));
            if (existing != null && existing.SchemaVersion is 0 or 1 && existing.ToolName == "NeuralFX" &&
                string.Equals(Path.GetFullPath(existing.GameDirectory).TrimEnd('\\', '/'), Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return new(true, false, false, "INSTALACIÓN ANTIGUA", new[] { "Al actualizar se guardará el estado actual de la instalación antigua como punto de restauración." }) { Files = files, CanMigrate = true };
            var manifest = ManifestStore.Read(root);
            var details = new List<string>(); bool repair = false;
            foreach (string relative in manifest.InstalledFiles)
            {
                string path = ManagedPaths.Resolve(root, relative);
                string key = relative.Replace('\\', '/');
                files[key] = InstalledFileState.Verified;
                if (!File.Exists(path)) { files[key] = InstalledFileState.Missing; details.Add("Ausente: " + relative); repair = true; }
                else if (DependencyManagerService.CalculateSha256(path) != manifest.FileChecksums[relative])
                {
                    bool config = Path.GetExtension(path) is ".ini" or ".cfg";
                    files[key] = config ? InstalledFileState.ConfigurationChanged : InstalledFileState.Modified;
                    details.Add((config ? "Configuración modificada: " : "Archivo modificado: ") + relative);
                    repair |= !config;
                }
            }
            repair |= files.Values.Any(x => x is InstalledFileState.Missing or InstalledFileState.Unmanaged);
            string exe = ManagedPaths.Resolve(root, "Cities.exe");
            if (manifest.GameExecutableSha256 != null && File.Exists(exe) && DependencyManagerService.CalculateSha256(exe) != manifest.GameExecutableSha256)
                details.Add("Cities.exe cambió desde la instalación; vuelve a comprobar compatibilidad en juego.");
            return new(true, true, repair, repair ? "INSTALACIÓN A REVISAR" : "PIPELINE INSTALADO", details.ToArray())
            { Files = files, RecordedChecksums = manifest.FileChecksums.ToDictionary(x => x.Key.Replace('\\', '/'), x => x.Value, StringComparer.OrdinalIgnoreCase), Restoration = RollbackPreview.Create(root, manifest, files) };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            foreach (string key in files.Keys.ToArray())
                if (files[key] != InstalledFileState.Missing) files[key] = InstalledFileState.Unverified;
            return new(true, false, false, "MANIFIESTO A REVISAR", new[] { ex.Message }) { Files = files };
        }
    }
    public void Dispose() { _watcher?.Dispose(); _watcher = null; }
}
