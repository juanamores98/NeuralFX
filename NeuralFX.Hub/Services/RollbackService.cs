using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NeuralFX.Hub.Services
{
    public class RollbackService
    {
        private readonly Func<bool> _isGameRunning;
        public RollbackService(Func<bool>? isGameRunning = null) => _isGameRunning = isGameRunning ?? HardwareDiagnosticsService.IsCitiesSkylinesRunning;
        public Task<bool> RollbackAsync(string gameDirectory, Action<string>? logAction = null, bool enforceProcessClosed = true) => Task.Run(() =>
        {
            try
            {
                if (enforceProcessClosed && _isGameRunning()) throw new IOException("Cierra Cities: Skylines antes de desinstalar.");
                using var lease = new InstallationLease(gameDirectory);
                FileTransaction.Recover(gameDirectory);
                if (!File.Exists(ManagedPaths.Resolve(gameDirectory, "NeuralFX_Manifest.json")))
                { logAction?.Invoke("Sin manifiesto de NeuralFX: no se elimina ningún archivo."); return true; }
                var manifest = ManifestStore.Read(gameDirectory);
                using var transaction = new FileTransaction(gameDirectory);
                string preserved = ".neuralfx-preserved/" + Guid.NewGuid().ToString("N");
                foreach (string relative in manifest.InstalledFiles)
                {
                    string path = ManagedPaths.Resolve(gameDirectory, relative);
                    if (File.Exists(path) && DependencyManagerService.CalculateSha256(path) != manifest.FileChecksums[relative])
                    {
                        transaction.Write(preserved + "/" + relative, File.ReadAllBytes(path));
                        logAction?.Invoke("Se conserva el archivo modificado en " + preserved + "/" + relative);
                    }
                    byte[]? original = manifest.BackedUpFiles.TryGetValue(relative, out var backup) ? File.ReadAllBytes(ManagedPaths.Resolve(gameDirectory, backup)) : null;
                    transaction.Write(relative, original);
                }
                foreach (string backup in manifest.BackedUpFiles.Values) transaction.Write(backup, null);
                byte[]? previousManifest = manifest.PreviousManifestBackup == null ? null : File.ReadAllBytes(ManagedPaths.Resolve(gameDirectory, manifest.PreviousManifestBackup));
                if (manifest.PreviousManifestBackup != null) transaction.Write(manifest.PreviousManifestBackup, null);
                transaction.Write("NeuralFX_Manifest.json", previousManifest);
                if (enforceProcessClosed && _isGameRunning()) throw new IOException("El juego se abrió durante la preparación.");
                transaction.Commit();
                // Only remove directories this installation created, and only if empty.
                foreach (string relative in manifest.InstalledDirectories.Concat(new[] { ".neuralfx-backups" }).OrderByDescending(x => x.Length))
                {
                    string dir = ManagedPaths.Resolve(gameDirectory, relative);
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                logAction?.Invoke("Estado previo restaurado. Se conservan logs y archivos ajenos o modificados.");
                return true;
            }
            catch (Exception ex) { logAction?.Invoke("No se pudo completar el rollback: " + ex.Message); return false; }
        });
        public bool IsInjectionActive(string gameDirectory) => Directory.Exists(gameDirectory) && File.Exists(ManagedPaths.Resolve(gameDirectory, "NeuralFX_Manifest.json"));
    }
}
