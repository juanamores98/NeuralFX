using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class RollbackService
    {
        public async Task<bool> RollbackAsync(string gameDirectory, Action<string>? logAction = null, bool enforceProcessClosed = true)
        {
            void Log(string msg) => logAction?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

            Log("Iniciando procedimiento de Rollback Atómico (Zero-Trace)...");

            if (!Directory.Exists(gameDirectory))
            {
                Log($"ERROR: Directorio del juego no existe: {gameDirectory}");
                return false;
            }

            if (enforceProcessClosed && HardwareDiagnosticsService.IsCitiesSkylinesRunning())
            {
                Log("ERROR BLOQUEANTE: Cities: Skylines (Cities.exe) está en ejecución.");
                Log("Debes cerrar el juego antes del rollback para liberar los archivos nativos (dxgi.dll y addons).");
                return false;
            }

            string manifestPath = Path.Combine(gameDirectory, "NeuralFX_Manifest.json");
            InstallationManifest? manifest = null;

            if (File.Exists(manifestPath))
            {
                try
                {
                    Log("Leyendo manifiesto oficial NeuralFX_Manifest.json...");
                    string json = await File.ReadAllTextAsync(manifestPath);
                    manifest = JsonSerializer.Deserialize<InstallationManifest>(json);

                    if (manifest != null)
                    {
                        // 1. Eliminar archivos instalados
                        foreach (var relFile in manifest.InstalledFiles)
                        {
                            string targetPath = Path.Combine(gameDirectory, relFile);
                            if (File.Exists(targetPath))
                            {
                                File.Delete(targetPath);
                                Log($"[ELIMINADO] {relFile}");
                            }
                        }

                        // 2. Restaurar backups si existían
                        foreach (var kvp in manifest.BackedUpFiles)
                        {
                            string relFile = kvp.Key;
                            string backupPath = kvp.Value;
                            string targetPath = Path.Combine(gameDirectory, relFile);

                            if (File.Exists(backupPath))
                            {
                                File.Copy(backupPath, targetPath, true);
                                Log($"[RESTAURADO] {relFile} restaurado desde {backupPath}");
                            }
                        }

                        // 3. Eliminar carpetas creadas si quedaron vacías o pertenecían al mod
                        foreach (var relDir in manifest.InstalledDirectories)
                        {
                            string targetDir = Path.Combine(gameDirectory, relDir);
                            if (Directory.Exists(targetDir))
                            {
                                Directory.Delete(targetDir, true);
                                Log($"[ELIMINADA CARPETA] {relDir}/");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"ADVERTENCIA leyendo manifiesto: {ex.Message}. Procediendo con barrido heurístico seguro.");
                }
            }
            else
            {
                Log("No se encontró NeuralFX_Manifest.json. Ejecutando barrido de seguridad heurístico...");
            }

            // 4. Purgar logs en caliente y archivos temporales generados por la inyección
            string[] transientFiles =
            {
                "ReShade.log",
                "dlss5-feed.log",
                "dlss5-feed.cfg",
                "dlss5-feed.addon64",
                "renodx-dlss5.addon64",
                "ReShadePreset.ini",
                "ReShade.ini",
                "nvngx_dlss.dll",
                "nvngx_dlssnr.dll",
                "nvngx_dlssd.dll",
                "nvngx_dlssg.dll",
                "Verify-DLSS5Feeder.ps1"
            };

            foreach (var trans in transientFiles)
            {
                string p = Path.Combine(gameDirectory, trans);
                if (File.Exists(p))
                {
                    try
                    {
                        File.Delete(p);
                        Log($"[PURGADO RESIDUO] {trans}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[AVISO] No se pudo borrar {trans}: {ex.Message}");
                    }
                }
            }

            // Si no había backup legítimo previo de dxgi.dll y dxgi.dll sigue ahí, eliminarlo
            string dxgi = Path.Combine(gameDirectory, "dxgi.dll");
            bool isRestoredBackup = manifest != null && manifest.BackedUpFiles.ContainsKey("dxgi.dll");
            if (File.Exists(dxgi) && !isRestoredBackup)
            {
                try
                {
                    File.Delete(dxgi);
                    Log("[ELIMINADO] dxgi.dll (ReShade inyector)");
                }
                catch (Exception ex)
                {
                    Log($"[AVISO] No se pudo borrar dxgi.dll: {ex.Message}");
                }
            }

            // Purgar carpeta reshade-shaders si persiste
            string shaders = Path.Combine(gameDirectory, "reshade-shaders");
            if (Directory.Exists(shaders))
            {
                try
                {
                    Directory.Delete(shaders, true);
                    Log("[PURGADO RESIDUO] reshade-shaders/");
                }
                catch (Exception ex)
                {
                    Log($"[AVISO] No se pudo borrar reshade-shaders/: {ex.Message}");
                }
            }

            // Eliminar manifiesto al final
            if (File.Exists(manifestPath))
            {
                try
                {
                    File.Delete(manifestPath);
                    Log("[ELIMINADO] NeuralFX_Manifest.json");
                }
                catch (Exception ex)
                {
                    Log($"[AVISO] No se pudo borrar NeuralFX_Manifest.json: {ex.Message}");
                }
            }

            // Verificación exhaustiva final de estado vanilla
            bool remainingDxgi = File.Exists(Path.Combine(gameDirectory, "dxgi.dll")) && !isRestoredBackup;
            bool remainingFeeder = File.Exists(Path.Combine(gameDirectory, "dlss5-feed.addon64"));
            bool remainingReno = File.Exists(Path.Combine(gameDirectory, "renodx-dlss5.addon64"));
            bool remainingDlss = File.Exists(Path.Combine(gameDirectory, "nvngx_dlss.dll"));
            bool remainingDlssd = File.Exists(Path.Combine(gameDirectory, "nvngx_dlssd.dll"));
            bool remainingDlssnr = File.Exists(Path.Combine(gameDirectory, "nvngx_dlssnr.dll"));
            bool remainingCfg = File.Exists(Path.Combine(gameDirectory, "dlss5-feed.cfg"));
            bool remainingIni = File.Exists(Path.Combine(gameDirectory, "ReShade.ini"));
            bool remainingPreset = File.Exists(Path.Combine(gameDirectory, "ReShadePreset.ini"));
            bool remainingManifest = File.Exists(Path.Combine(gameDirectory, "NeuralFX_Manifest.json"));
            bool remainingShaders = Directory.Exists(Path.Combine(gameDirectory, "reshade-shaders"));

            bool isVanilla = !remainingDxgi
                          && !remainingFeeder
                          && !remainingReno
                          && !remainingDlss
                          && !remainingDlssd
                          && !remainingDlssnr
                          && !remainingCfg
                          && !remainingIni
                          && !remainingPreset
                          && !remainingManifest
                          && !remainingShaders;

            if (isVanilla)
            {
                Log(">> VERIFICACIÓN EXITOSA: La carpeta del juego está 100% LIMPIA (Estado Vanilla Zero-Trace).");
            }
            else
            {
                Log(">> ADVERTENCIA: Quedan archivos o carpetas detectadas en el juego:");
                if (remainingDxgi) Log("   - dxgi.dll");
                if (remainingFeeder) Log("   - dlss5-feed.addon64");
                if (remainingReno) Log("   - renodx-dlss5.addon64");
                if (remainingDlss) Log("   - nvngx_dlss.dll");
                if (remainingShaders) Log("   - reshade-shaders/");
            }

            return isVanilla;
        }

        public bool IsInjectionActive(string gameDirectory)
        {
            if (string.IsNullOrEmpty(gameDirectory) || !Directory.Exists(gameDirectory))
                return false;

            return File.Exists(Path.Combine(gameDirectory, "NeuralFX_Manifest.json")) ||
                   File.Exists(Path.Combine(gameDirectory, "dlss5-feed.addon64")) ||
                   File.Exists(Path.Combine(gameDirectory, "dxgi.dll"));
        }
    }
}
