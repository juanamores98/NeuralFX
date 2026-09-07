using System;
using System.Collections.Generic;
using System.IO;
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
                    Description = "ReShade con soporte de Add-ons desbloqueado para inyección de profundidad y motion vectors.",
                    Category = DependencyCategory.Runtime,
                    TargetRelativePath = "dxgi.dll",
                    SourceType = "PublicDownload",
                    DownloadUrl = "https://reshade.me/downloads", // Upstream reference
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
                    IsRequired = true
                },
                new DependencyItem
                {
                    Id = "nvngx_dlssnr",
                    DisplayName = "NVIDIA DLSS Neural Denoiser (nvngx_dlssnr.dll)",
                    Description = "Runtime neural oficial de NVIDIA para denoiser y reconstrucción DLSS 5 en RTX Serie 50/40.",
                    Category = DependencyCategory.NvidiaProprietary,
                    TargetRelativePath = "nvngx_dlssnr.dll",
                    SourceType = "UserProvided",
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
            string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

            foreach (var item in items)
            {
                // 1. Check if installed in game
                if (!string.IsNullOrEmpty(gameDirectory))
                {
                    string inGamePath = Path.Combine(gameDirectory, item.TargetRelativePath);
                    if (File.Exists(inGamePath) || Directory.Exists(inGamePath))
                    {
                        item.Status = DependencyStatus.InstalledInGame;
                        item.StatusMessage = "Inyectado en juego";
                        continue;
                    }
                }

                // 2. Check in Local Cache
                string cachedPath = Path.Combine(CacheDirectory, Path.GetFileName(item.TargetRelativePath));
                if (File.Exists(cachedPath))
                {
                    var fileInfo = new FileInfo(cachedPath);
                    item.LocalCachedPath = cachedPath;
                    item.FileSize = fileInfo.Length;
                    item.Status = DependencyStatus.InCache;
                    item.StatusMessage = $"En caché ({fileInfo.Length / (1024 * 1024):N1} MB)";
                    continue;
                }

                // 3. For NVIDIA proprietary items, check user Downloads automatically
                if (item.Category == DependencyCategory.NvidiaProprietary)
                {
                    string candidate = Path.Combine(userDownloads, item.TargetRelativePath);
                    if (File.Exists(candidate))
                    {
                        // Auto-cache
                        try
                        {
                            File.Copy(candidate, cachedPath, true);
                            var fileInfo = new FileInfo(cachedPath);
                            item.LocalCachedPath = cachedPath;
                            item.FileSize = fileInfo.Length;
                            item.Status = DependencyStatus.InCache;
                            item.StatusMessage = $"Importado desde Descargas ({fileInfo.Length / (1024 * 1024):N1} MB)";
                            continue;
                        }
                        catch
                        {
                            // Ignore copy error
                        }
                    }
                }

                // 4. Embedded or generated configs are always ready
                if (item.Category == DependencyCategory.Config || item.Category == DependencyCategory.Shaders)
                {
                    item.Status = DependencyStatus.InCache;
                    item.StatusMessage = "Generado dinámicamente";
                    continue;
                }

                // Default missing
                item.Status = DependencyStatus.Missing;
                item.StatusMessage = item.Category == DependencyCategory.NvidiaProprietary
                    ? "Requiere importación del usuario"
                    : "No descargado";
            }
        }

        public async Task<bool> ImportFileAsync(DependencyItem item, string sourceFilePath)
        {
            if (!File.Exists(sourceFilePath))
                return false;

            try
            {
                string destFileName = Path.GetFileName(item.TargetRelativePath);
                string destPath = Path.Combine(CacheDirectory, destFileName);

                await Task.Run(() => File.Copy(sourceFilePath, destPath, true));

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

        public static string CalculateSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
