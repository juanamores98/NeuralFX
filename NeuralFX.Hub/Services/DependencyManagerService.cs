using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class DependencyManagerService
    {
        public string CacheDirectory { get; }
        public string BackupsDirectory { get; }

        public DependencyManagerService()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            CacheDirectory = Path.Combine(localAppData, "NeuralFX", "Cache");
            BackupsDirectory = Path.Combine(localAppData, "NeuralFX", "Backups");

            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(BackupsDirectory);
        }

        public List<DependencyItem> GetInitialDependencies(string? gameDirectory = null)
        {
            var list = new List<DependencyItem>
            {
                new DependencyItem
                {
                    Id = "reshade_addon",
                    DisplayName = "ReShade Add-on Runtime (dxgi.dll)",
                    Description = "ReShade 6.8+ con Add-ons desbloqueado para inyección de profundidad y motion vectors.",
                    Category = DependencyCategory.Runtime,
                    TargetRelativePath = "dxgi.dll",
                    SourceType = "PublicDownload",
                    DownloadUrl = "https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe",
                    OfficialWebUrl = "https://reshade.me/#download",
                    CanAutoDownload = true,
                    DownloadType = "ReShadeExeExtract",
                    ArchiveExtractFileName = "ReShade64.dll",
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "dlss5_feeder",
                    DisplayName = "DLSS5-Feeder Addon (dlss5-feed.addon64)",
                    Description = "Puente nativo C++ DX11 que interrumpe el pipeline y suministra motion vectors y depth a NGX.",
                    Category = DependencyCategory.Addon,
                    TargetRelativePath = "dlss5-feed.addon64",
                    SourceType = "PublicDownload",
                    DownloadUrl = "https://github.com/jlrouzies-fr/DLSS5-Feeder/releases/download/v0.14.0-beta.4/DLSS5-Feeder-0.14.0-beta.4.zip",
                    OfficialWebUrl = "https://github.com/jlrouzies-fr/DLSS5-Feeder/releases",
                    CanAutoDownload = true,
                    DownloadType = "ZipExtract",
                    ArchiveExtractFileName = "dlss5-feed.addon64",
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "renodx_dlss5",
                    DisplayName = "RenoDX DLSS 5 Neural Addon (renodx-dlss5.addon64)",
                    Description = "Consumidor neural que ejecuta la inferencia de DLSS 5 sobre el contrato sintético de DLSS5-Feeder.",
                    Category = DependencyCategory.Addon,
                    TargetRelativePath = "renodx-dlss5.addon64",
                    SourceType = "PublicDownload",
                    DownloadUrl = "https://github.com/RankFTW/rhi-repo/releases/download/renodx-dlss5-4.70/renodx-dlss5_4.70.zip",
                    OfficialWebUrl = "https://github.com/RankFTW/rhi-repo/releases",
                    CanAutoDownload = true,
                    DownloadType = "ZipExtract",
                    ArchiveExtractFileName = "renodx-dlss5.addon64",
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "nvngx_dlss",
                    DisplayName = "NVIDIA DLSS Super Resolution (nvngx_dlss.dll)",
                    Description = "Runtime oficial propietario de NVIDIA para reconstrucción y escalado DLSS.",
                    Category = DependencyCategory.NvidiaProprietary,
                    TargetRelativePath = "nvngx_dlss.dll",
                    SourceType = "UserProvided",
                    OfficialWebUrl = "https://www.techpowerup.com/download/nvidia-dlss-dll/",
                    CanAutoDownload = false,
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "nvngx_dlssd",
                    DisplayName = "NVIDIA DLSS Ray Reconstruction / Denoiser (nvngx_dlssd.dll)",
                    Description = "Runtime neural oficial de NVIDIA para denoiser y reconstrucción DLSS 5 en RTX Serie 50/40.",
                    Category = DependencyCategory.NvidiaProprietary,
                    TargetRelativePath = "nvngx_dlssd.dll",
                    Aliases = new[] { "nvngx_dlssd.dll", "nvngx_dlssnr.dll" },
                    SourceType = "UserProvided",
                    OfficialWebUrl = "https://www.techpowerup.com/download/nvidia-dlss-3-ray-reconstruction-dll/",
                    CanAutoDownload = false,
                    IsRequired = false // Opcional si solo se usa DLSS SR estándar
                },
                new DependencyItem
                {
                    Id = "reshade_shaders",
                    DisplayName = "Suite de Shaders (LumeniteFX & AMD CAS)",
                    Description = "Filtro de nitidez adaptativa AMD FidelityFX CAS y shaders de temporal flow.",
                    Category = DependencyCategory.Shaders,
                    TargetRelativePath = "reshade-shaders",
                    SourceType = "Embedded",
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "feeder_config",
                    DisplayName = "Configuración DLSS5 Feeder (dlss5-feed.cfg)",
                    Description = "Configuración precalibrada para Unity 5.6 DX11 (inverted depth, jittering compensation).",
                    Category = DependencyCategory.Config,
                    TargetRelativePath = "dlss5-feed.cfg",
                    SourceType = "Embedded",
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "reshade_preset",
                    DisplayName = "Presets de ReShade (ReShade.ini / ReShadePreset.ini)",
                    Description = "Enrutamiento automático de texturas y shaders sin interferir con la UI de Cities: Skylines.",
                    Category = DependencyCategory.Config,
                    TargetRelativePath = "ReShade.ini",
                    SourceType = "Embedded",
                    IsRequired = true
                }
            };

            RefreshDependencyStatuses(list, gameDirectory);
            return list;
        }

        public void RefreshDependencyStatuses(List<DependencyItem> items, string? gameDirectory)
        {
            // Auto-detect from downloads first
            AutoDetectAndImportFromDownloads(items, null);

            foreach (var item in items)
            {
                // 1. Check if physically installed in game directory
                bool installedInGame = false;
                if (!string.IsNullOrEmpty(gameDirectory) && Directory.Exists(gameDirectory))
                {
                    string inGamePath = Path.Combine(gameDirectory, item.TargetRelativePath);
                    installedInGame = File.Exists(inGamePath) || Directory.Exists(inGamePath);

                    if (!installedInGame && item.Aliases != null)
                    {
                        foreach (var alias in item.Aliases)
                        {
                            string aliasGamePath = Path.Combine(gameDirectory, alias);
                            if (File.Exists(aliasGamePath))
                            {
                                installedInGame = true;
                                break;
                            }
                        }
                    }
                }
                item.IsInGame = installedInGame;

                // 2. Check in Local Cache
                string cachedPath = Path.Combine(CacheDirectory, Path.GetFileName(item.TargetRelativePath));
                string? effectiveCachedPath = null;

                if (File.Exists(cachedPath))
                {
                    effectiveCachedPath = cachedPath;
                }
                else if (item.Aliases != null)
                {
                    foreach (var alias in item.Aliases)
                    {
                        string aliasPath = Path.Combine(CacheDirectory, alias);
                        if (File.Exists(aliasPath))
                        {
                            effectiveCachedPath = aliasPath;
                            break;
                        }
                    }
                }

                if (item.Category == DependencyCategory.Config || item.Category == DependencyCategory.Shaders)
                {
                    item.IsInCache = true;
                    item.Status = installedInGame ? DependencyStatus.InstalledInGame : DependencyStatus.InCache;
                    item.StatusMessage = installedInGame ? "Inyectado en juego" : "Generado dinámicamente";
                    continue;
                }

                if (effectiveCachedPath != null)
                {
                    var fileInfo = new FileInfo(effectiveCachedPath);
                    item.LocalCachedPath = effectiveCachedPath;
                    item.FileSize = fileInfo.Length;
                    item.IsInCache = true;
                    item.Status = installedInGame ? DependencyStatus.InstalledInGame : DependencyStatus.InCache;
                    item.StatusMessage = installedInGame ? "Inyectado en juego" : $"En caché ({fileInfo.Length / (1024.0 * 1024.0):F1} MB)";
                    continue;
                }

                // Default missing from cache
                item.IsInCache = false;
                item.Status = installedInGame ? DependencyStatus.InstalledInGame : DependencyStatus.Missing;
                item.StatusMessage = installedInGame
                    ? "Inyectado en juego"
                    : (item.CanAutoDownload
                        ? "Disponible para descarga directa"
                        : (item.Category == DependencyCategory.NvidiaProprietary ? "Requiere DLL oficial (TechPowerUp)" : "No descargado"));
            }
        }

        public void AutoDetectAndImportFromDownloads(List<DependencyItem> items, Action<string>? log = null)
        {
            string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(userDownloads))
                return;

            void LogMsg(string msg) => log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

            foreach (var item in items)
            {
                if (item.IsInCache)
                    continue;

                string cachedPath = Path.Combine(CacheDirectory, Path.GetFileName(item.TargetRelativePath));

                // 1. Direct DLL / File match in Downloads
                string directFile = Path.Combine(userDownloads, item.TargetRelativePath);
                if (File.Exists(directFile))
                {
                    try
                    {
                        File.Copy(directFile, cachedPath, true);
                        var fi = new FileInfo(cachedPath);
                        item.LocalCachedPath = cachedPath;
                        item.FileSize = fi.Length;
                        item.IsInCache = true;
                        item.Status = DependencyStatus.InCache;
                        item.StatusMessage = $"Importado desde Descargas ({fi.Length / (1024.0 * 1024.0):F1} MB)";
                        LogMsg($"Auto-detectado e importado desde Descargas: {Path.GetFileName(directFile)}");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        LogMsg($"Error copiando {directFile}: {ex.Message}");
                    }
                }

                // 2. Check DLL pattern / copy variants (e.g. "nvngx_dlss (1).dll")
                try
                {
                    string baseNameNoExt = Path.GetFileNameWithoutExtension(item.TargetRelativePath);
                    var matchingDlls = Directory.GetFiles(userDownloads, "*.dll");
                    bool matchedPattern = false;

                    foreach (var candidateDll in matchingDlls)
                    {
                        string fname = Path.GetFileName(candidateDll).ToLowerInvariant();
                        bool matches = false;

                        if (item.Id == "nvngx_dlss" && fname.StartsWith("nvngx_dlss") && !fname.Contains("dlssd") && !fname.Contains("dlssnr") && !fname.Contains("dlssg"))
                            matches = true;
                        else if (item.Id == "nvngx_dlssd" && (fname.StartsWith("nvngx_dlssd") || fname.StartsWith("nvngx_dlssnr")))
                            matches = true;

                        if (matches)
                        {
                            File.Copy(candidateDll, cachedPath, true);
                            var fi = new FileInfo(cachedPath);
                            item.LocalCachedPath = cachedPath;
                            item.FileSize = fi.Length;
                            item.IsInCache = true;
                            item.Status = DependencyStatus.InCache;
                            item.StatusMessage = $"Importado desde Descargas ({fi.Length / (1024.0 * 1024.0):F1} MB)";
                            LogMsg($"Auto-detectado e importado variante DLL: {Path.GetFileName(candidateDll)}");
                            matchedPattern = true;
                            break;
                        }
                    }

                    if (matchedPattern) continue;
                }
                catch
                {
                    // Ignore search errors
                }

                // 3. Check Aliases in Downloads
                if (item.Aliases != null)
                {
                    bool importedAlias = false;
                    foreach (var alias in item.Aliases)
                    {
                        string aliasFile = Path.Combine(userDownloads, alias);
                        if (File.Exists(aliasFile))
                        {
                            try
                            {
                                File.Copy(aliasFile, cachedPath, true);
                                var fi = new FileInfo(cachedPath);
                                item.LocalCachedPath = cachedPath;
                                item.FileSize = fi.Length;
                                item.IsInCache = true;
                                item.Status = DependencyStatus.InCache;
                                item.StatusMessage = $"Importado desde Descargas ({fi.Length / (1024.0 * 1024.0):F1} MB)";
                                LogMsg($"Auto-detectado alias e importado: {alias}");
                                importedAlias = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                LogMsg($"Error copiando alias {alias}: {ex.Message}");
                            }
                        }
                    }
                    if (importedAlias) continue;
                }

                // 4. Check ReShade installer executable in Downloads
                if (item.Id == "reshade_addon")
                {
                    try
                    {
                        var exeFiles = Directory.GetFiles(userDownloads, "*ReShade*.exe");
                        foreach (var exePath in exeFiles)
                        {
                            if (ExtractReShade64FromSetup(exePath, cachedPath, LogMsg))
                            {
                                var fi = new FileInfo(cachedPath);
                                item.LocalCachedPath = cachedPath;
                                item.FileSize = fi.Length;
                                item.IsInCache = true;
                                item.Status = DependencyStatus.InCache;
                                item.StatusMessage = $"Extraído de ReShade Setup ({fi.Length / (1024.0 * 1024.0):F1} MB)";
                                LogMsg($"Auto-detectado instalador ReShade en Descargas y extraído dxgi.dll exitosamente.");
                                break;
                            }
                        }
                        if (item.IsInCache) continue;
                    }
                    catch
                    {
                        // Ignore exe scan errors
                    }
                }

                // 5. Check ZIP archives in Downloads (e.g. nvidia-dlss-*.zip, nvngx_dlss_*.zip)
                try
                {
                    var zipFiles = Directory.GetFiles(userDownloads, "*.zip");
                    foreach (var zipPath in zipFiles)
                    {
                        string zipName = Path.GetFileName(zipPath).ToLowerInvariant();
                        bool matchZip = false;

                        if (item.Id == "nvngx_dlss")
                        {
                            bool isDlssG = zipName.Contains("dlssg") || zipName.Contains("dlss_g") || zipName.Contains("framegen") || zipName.Contains("frame-generation");
                            bool isDlssD = zipName.Contains("dlssd") || zipName.Contains("dlss_d") || zipName.Contains("dlssnr") || zipName.Contains("ray") || zipName.Contains("reconstruction");
                            bool isFeeder = zipName.Contains("feeder");
                            if ((zipName.Contains("dlss") || zipName.Contains("nvngx")) && !isDlssG && !isDlssD && !isFeeder)
                            {
                                matchZip = true;
                            }
                        }
                        else if (item.Id == "nvngx_dlssd" && (zipName.Contains("ray") || zipName.Contains("dlssd") || zipName.Contains("dlssnr") || zipName.Contains("reconstruction")))
                        {
                            matchZip = true;
                        }
                        else if (item.Id == "dlss5_feeder" && zipName.Contains("feeder"))
                        {
                            matchZip = true;
                        }
                        else if (item.Id == "renodx_dlss5" && (zipName.Contains("renodx") || zipName.Contains("rhi")))
                        {
                            matchZip = true;
                        }

                        if (matchZip)
                        {
                            bool extracted = ExtractFromZip(zipPath, item, cachedPath);
                            if (extracted)
                            {
                                var fi = new FileInfo(cachedPath);
                                item.LocalCachedPath = cachedPath;
                                item.FileSize = fi.Length;
                                item.IsInCache = true;
                                item.Status = DependencyStatus.InCache;
                                item.StatusMessage = $"Extraído de {Path.GetFileName(zipPath)} ({fi.Length / (1024.0 * 1024.0):F1} MB)";
                                LogMsg($"Extraído automáticamente desde archivo comprimido: {Path.GetFileName(zipPath)} -> {Path.GetFileName(cachedPath)}");
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore directory read errors
                }
            }
        }

        public async Task<bool> DownloadDependencyAsync(DependencyItem item, Action<string>? log = null, IProgress<double>? progress = null)
        {
            if (string.IsNullOrEmpty(item.DownloadUrl))
                return false;

            void LogMsg(string msg) => log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

            item.IsDownloading = true;
            item.StatusMessage = "Descargando...";
            LogMsg($"Iniciando descarga de {item.DisplayName}...");

            try
            {
                using var handler = new HttpClientHandler { AllowAutoRedirect = true };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                string tempFile = Path.Combine(Path.GetTempPath(), $"NeuralFX_Dl_{Guid.NewGuid():N}.tmp");

                using (var response = await client.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using var sourceStream = await response.Content.ReadAsStreamAsync();
                    using var targetStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int read;

                    while ((read = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await targetStream.WriteAsync(buffer, 0, read);
                        totalRead += read;
                        if (totalBytes > 0)
                        {
                            double pct = (double)totalRead / totalBytes;
                            progress?.Report(pct);
                        }
                    }
                }

                LogMsg($"Descarga finalizada ({new FileInfo(tempFile).Length / 1024} KB). Procesando contenido...");

                string targetCacheFile = Path.Combine(CacheDirectory, Path.GetFileName(item.TargetRelativePath));

                if (item.DownloadType == "ZipExtract")
                {
                    bool extracted = ExtractFromZip(tempFile, item, targetCacheFile);
                    if (!extracted)
                    {
                        throw new InvalidOperationException($"No se encontró {item.ArchiveExtractFileName ?? item.TargetRelativePath} dentro del archivo descargado.");
                    }
                }
                else if (item.DownloadType == "ReShadeExeExtract")
                {
                    bool extracted = ExtractReShade64FromSetup(tempFile, targetCacheFile, LogMsg);
                    if (!extracted)
                    {
                        throw new InvalidOperationException("No se pudo extraer ReShade64.dll desde el instalador oficial de ReShade.");
                    }
                }
                else
                {
                    File.Copy(tempFile, targetCacheFile, true);
                }

                if (File.Exists(tempFile))
                    File.Delete(tempFile);

                var fileInfo = new FileInfo(targetCacheFile);
                item.LocalCachedPath = targetCacheFile;
                item.FileSize = fileInfo.Length;
                item.Status = DependencyStatus.InCache;
                item.StatusMessage = $"En caché ({fileInfo.Length / (1024 * 1024):N1} MB)";
                item.IsDownloading = false;

                LogMsg($">> Éxito: {item.DisplayName} listo en caché ({fileInfo.Length / (1024 * 1024):N1} MB).");
                return true;
            }
            catch (Exception ex)
            {
                item.Status = DependencyStatus.Error;
                item.StatusMessage = $"Fallo de descarga: {ex.Message}";
                item.IsDownloading = false;
                LogMsg($"ERROR descargando {item.DisplayName}: {ex.Message}");
                return false;
            }
        }

        public async Task<int> DownloadAllPublicMissingAsync(List<DependencyItem> items, Action<string>? log = null)
        {
            int successCount = 0;
            var missingPublic = items.Where(i => i.CanAutoDownload && i.Status == DependencyStatus.Missing).ToList();

            foreach (var item in missingPublic)
            {
                bool ok = await DownloadDependencyAsync(item, log);
                if (ok) successCount++;
            }

            return successCount;
        }

        public async Task<bool> ImportFileAsync(DependencyItem item, string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath))
                return false;

            try
            {
                string destFileName = Path.GetFileName(item.TargetRelativePath);
                string destPath = Path.Combine(CacheDirectory, destFileName);
                string ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

                if (ext == ".zip")
                {
                    bool extracted = await Task.Run(() => ExtractFromZip(sourceFilePath, item, destPath));
                    if (!extracted)
                        return false;
                }
                else if (ext == ".exe" && sourceFilePath.Contains("ReShade", StringComparison.OrdinalIgnoreCase))
                {
                    bool extracted = await Task.Run(() => ExtractReShade64FromSetup(sourceFilePath, destPath, null));
                    if (!extracted)
                        return false;
                }
                else
                {
                    await Task.Run(() => File.Copy(sourceFilePath, destPath, true));
                }

                var fileInfo = new FileInfo(destPath);
                item.LocalCachedPath = destPath;
                item.FileSize = fileInfo.Length;
                item.Status = DependencyStatus.InCache;
                item.StatusMessage = $"En caché ({fileInfo.Length / (1024 * 1024):N1} MB)";
                return true;
            }
            catch (Exception ex)
            {
                item.Status = DependencyStatus.Error;
                item.StatusMessage = $"Error importando: {ex.Message}";
                return false;
            }
        }

        private static bool ExtractFromZip(string zipFilePath, DependencyItem item, string destinationFilePath)
        {
            using var archive = ZipFile.OpenRead(zipFilePath);

            string targetName = item.ArchiveExtractFileName ?? Path.GetFileName(item.TargetRelativePath);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

            // If not found by primary target name, check aliases
            if (entry == null && item.Aliases != null)
            {
                foreach (var alias in item.Aliases)
                {
                    entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(alias, StringComparison.OrdinalIgnoreCase));
                    if (entry != null) break;
                }
            }

            // Fallback: if there is only 1 .dll entry in the archive and item is a dll
            if (entry == null && targetName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var dllEntries = archive.Entries.Where(e => e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
                if (dllEntries.Count == 1)
                {
                    entry = dllEntries[0];
                }
            }

            if (entry != null)
            {
                string? dir = Path.GetDirectoryName(destinationFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                entry.ExtractToFile(destinationFilePath, overwrite: true);
                return true;
            }

            return false;
        }

        private static bool ExtractReShade64FromSetup(string setupExePath, string destinationDxgiPath, Action<string>? log)
        {
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"ReShade_Ext_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractDir);

            try
            {
                // Method 1: Use Windows native tar.exe (System32)
                string tarPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe");
                if (File.Exists(tarPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = tarPath,
                        Arguments = $"-xf \"{setupExePath}\" ReShade64.dll",
                        WorkingDirectory = tempExtractDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var p = Process.Start(psi);
                    p?.WaitForExit(10000);

                    string extracted = Path.Combine(tempExtractDir, "ReShade64.dll");
                    if (File.Exists(extracted))
                    {
                        File.Copy(extracted, destinationDxgiPath, true);
                        return true;
                    }
                }

                // Method 2: Try 7z if tar didn't work
                try
                {
                    var psi7z = new ProcessStartInfo
                    {
                        FileName = "7z",
                        Arguments = $"e \"{setupExePath}\" \"ReShade64.dll\" -o\"{tempExtractDir}\" -y",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p7z = Process.Start(psi7z);
                    p7z?.WaitForExit(10000);

                    string extracted7z = Path.Combine(tempExtractDir, "ReShade64.dll");
                    if (File.Exists(extracted7z))
                    {
                        File.Copy(extracted7z, destinationDxgiPath, true);
                        return true;
                    }
                }
                catch
                {
                    // 7z not in PATH
                }

                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtractDir))
                        Directory.Delete(tempExtractDir, true);
                }
                catch { }
            }
        }

        public static string CalculateSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
