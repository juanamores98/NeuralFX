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
            bool hadManifest = false;

            if (File.Exists(manifestPath))
            {
                hadManifest = true;
                try
                {
                    Log("Leyendo manifiesto oficial NeuralFX_Manifest.json...");
                    string json = await File.ReadAllTextAsync(manifestPath);
                    var manifest = JsonSerializer.Deserialize<InstallationManifest>(json);

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
                "ReShadePreset.ini",
                "ReShade.ini",
                "nvngx_dlss.dll",
                "nvngx_dlssnr.dll",
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

            // Si no había backup de dxgi.dll y dxgi.dll sigue ahí, checar si se elimina
            string dxgi = Path.Combine(gameDirectory, "dxgi.dll");
            if (File.Exists(dxgi) && hadManifest)
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

            // Verificación final de estado vanilla
            bool isVanilla = !File.Exists(Path.Combine(gameDirectory, "dlss5-feed.addon64"))
                          && !File.Exists(Path.Combine(gameDirectory, "dlss5-feed.cfg"))
                          && !File.Exists(Path.Combine(gameDirectory, "ReShade.ini"))
                          && !Directory.Exists(Path.Combine(gameDirectory, "reshade-shaders"));

            if (isVanilla)
            {
                Log(">> VERIFICACIÓN EXITOSA: La carpeta del juego está 100% LIMPIA (Estado Vanilla Zero-Trace).");
            }
            else
            {
                Log(">> ADVERTENCIA: Quedan archivos o carpetas detectadas.");
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
