using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class DependencyManagerService
    {
        private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromMinutes(3) };
        private readonly HttpClient _client;
        private readonly string _downloads;
        public string CacheDirectory { get; }
        public string BackupsDirectory { get; }
        public DependencyManagerService(string? storageRoot = null, string? downloadsDirectory = null, HttpClient? client = null)
        {
            storageRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuralFX");
            CacheDirectory = Path.Combine(storageRoot, "Cache", "v2");
            BackupsDirectory = Path.Combine(storageRoot, "Backups");
            _downloads = downloadsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            _client = client ?? SharedClient;
        }
        public List<DependencyItem> GetInitialDependencies(string? gameDirectory = null, HardwareInfo? hardware = null)
        {
            var items = ReadCatalog();
            ConfigureForHardware(items, hardware);
            RefreshDependencyStatuses(items, gameDirectory);
            return items;
        }
        public void ConfigureForHardware(List<DependencyItem> items, HardwareInfo? info)
        {
            if (info == null) return;
            var nr = items.FirstOrDefault(x => x.Id == "nvngx_dlssnr");
            if (nr == null) return;

            if (info.DetectedArchitecture == GpuArchitecture.AdaLovelace)
            {
                nr.DownloadUrl = "https://github.com/RankFTW/rhi-repo/releases/download/dlssnr-310.8.0-RTX40/nvngx_dlssnr_310.8.0-RTX40.zip";
                nr.ExpectedSha256 = "46124cfaef532ad5f6da07494772ea8c1b3e719f934e254385697f38d1289e3f";
                nr.ExpectedFileSha256["nvngx_dlssnr.dll"] = "4b8d19bc3eff58a084f5eca7489c921501c203450169fb82ff4f649a4482ba05";
                nr.Version = "310.8.0-RTX40";
                nr.Description = "Runtime DLSSNR adaptado para arquitecturas Ada Lovelace (RTX 40xx · SM 89).";
            }
            else if (info.DetectedArchitecture == GpuArchitecture.AmpereTuring)
            {
                nr.DownloadUrl = "https://github.com/RankFTW/rhi-repo/releases/download/dlssnr-310.8.SF-v2/nvngx_dlssnr_310.8.SF-v2.zip";
                nr.ExpectedSha256 = "1da35941894994eb087e017577829e492454e9bae3a6a9397027069ceb74955c";
                nr.ExpectedFileSha256["nvngx_dlssnr.dll"] = "6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927";
                nr.Version = "310.8.SF-v2";
                nr.Description = "Runtime DLSSNR adaptado para arquitecturas Ampere/Turing (RTX 30xx/20xx · FP16).";
            }
            else
            {
                nr.DownloadUrl = "https://github.com/RankFTW/rhi-repo/releases/download/dlssnr-310.8.0/nvngx_dlssnr_310.8.0.zip";
                nr.ExpectedSha256 = "388c0a7912e15ec911b9c9e11a692142b11fe387ddf2b637d8c358138fffb3ac";
                nr.ExpectedFileSha256["nvngx_dlssnr.dll"] = "e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e";
                nr.Version = "310.8.0";
                nr.Description = "Runtime DLSSNR oficial para arquitecturas Blackwell (RTX 50xx · FP8 nativo).";
            }
        }
        internal static List<DependencyItem> ReadCatalog()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeuralFX.Hub.Assets.components.json")!;
            return JsonSerializer.Deserialize<List<DependencyItem>>(stream)!;
        }
        public Dictionary<string, string> GetPackageFiles(DependencyItem item) => item.PackageFiles.Count > 0
            ? item.PackageFiles : new() { [item.ArchiveExtractFileName ?? Path.GetFileName(item.TargetRelativePath)] = item.TargetRelativePath };
        private string PackageDirectory(DependencyItem item) => ManagedPaths.Resolve(CacheDirectory, item.Id);
        public Dictionary<string, byte[]> ReadPayload(DependencyItem item)
        {
            if (item.SourceType == "Bundled")
            {
                using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeuralFX.Native.Feeder") ?? throw new FileNotFoundException("Compila Native/build.ps1 y después el Hub para incluir el puente.");
                using var output = new MemoryStream(); input.CopyTo(output);
                return new() { [item.TargetRelativePath] = output.ToArray() };
            }
            return ValidateCache(item).ToDictionary(x => x.Key, x => File.ReadAllBytes(x.Value), StringComparer.OrdinalIgnoreCase);
        }
        private Dictionary<string, string> ValidateCache(DependencyItem item)
        {
            item.AvailableChecksums.Clear();
            string root = PackageDirectory(item);
            var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllText(ManagedPaths.Resolve(root, "receipt.json"))) ?? throw new InvalidDataException("Caché sin recibo.");
            if (receipt.Files == null) throw new InvalidDataException("Recibo de caché incompleto.");
            if (receipt.SourceSha256 != item.ExpectedSha256) throw new InvalidDataException("La caché pertenece a otra versión: " + item.DisplayName);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in GetPackageFiles(item).Values)
            {
                string path = ManagedPaths.Resolve(root, relative);
                string expected = item.ExpectedFileSha256.TryGetValue(relative, out var pinned) ? pinned : receipt.Files.GetValueOrDefault(relative, "");
                if (CalculateSha256(path) != expected) throw new InvalidDataException("Integridad incorrecta: " + relative);
                if (item.Category == DependencyCategory.NvidiaProprietary) BinaryIdentity.ValidateNvidia(path, Path.GetFileName(item.TargetRelativePath));
                result.Add(relative, path);
                item.AvailableChecksums[relative.Replace('\\', '/')] = expected;
            }
            return result;
        }
        public void RefreshDependencyStatuses(List<DependencyItem> items, string? gameDirectory)
        {
            foreach (var item in items)
            {
                item.IsInGame = gameDirectory != null && GetPackageFiles(item).Values.All(p => File.Exists(ManagedPaths.Resolve(gameDirectory, p)));
                item.IsInCache = false;
                item.LocalCachedPath = null;
                if (item.IsEmbedded) item.IsInCache = true;
                else if (item.SourceType == "Bundled")
                {
                    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NeuralFX.Native.Feeder");
                    item.IsInCache = stream != null; item.FileSize = stream?.Length ?? 0;
                    if (stream != null) item.AvailableChecksums[item.TargetRelativePath] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (stream == null) item.StatusMessage = "Compila Native/build.ps1 antes de publicar el Hub.";
                }
                else
                {
                    try
                    {
                        var payload = ValidateCache(item);
                        item.IsInCache = true;
                        item.FileSize = payload.Sum(x => new FileInfo(x.Value).Length);
                        item.LocalCachedPath = ManagedPaths.Resolve(PackageDirectory(item), item.TargetRelativePath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException || ex is CryptographicException || ex is BadImageFormatException)
                    { item.StatusMessage = ex is FileNotFoundException || ex is DirectoryNotFoundException ? "Pendiente de descarga/importación" : ex.Message; }
                }
                item.Status = item.IsInCache ? DependencyStatus.InCache : DependencyStatus.Missing;
                if (item.IsInCache) item.StatusMessage = item.IsEmbedded ? "Integrado" : "Caché verificada";
            }
        }
        public async Task<bool> DownloadDependencyAsync(DependencyItem item, Action<string>? log = null, IProgress<double>? progress = null)
        {
            if (!item.CanAutoDownload || string.IsNullOrEmpty(item.DownloadUrl)) return false;
            item.IsDownloading = true;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, item.DownloadUrl);
                request.Headers.UserAgent.ParseAdd("NeuralFX/2.0");
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                byte[] data = await response.Content.ReadAsByteArrayAsync();
                await Task.Run(() => ImportBytes(item, data, item.DownloadType));
                progress?.Report(1);
                log?.Invoke("Verificado y guardado: " + item.DisplayName);
                return true;
            }
            catch (Exception ex) { item.Status = DependencyStatus.Error; item.StatusMessage = ex.Message; log?.Invoke(ex.Message); return false; }
            finally { item.IsDownloading = false; }
        }
        public async Task<int> DownloadAllPublicMissingAsync(List<DependencyItem> items, Action<string>? log = null)
        {
            int count = 0;
            foreach (var item in items.Where(x => x.CanAutoDownload && !x.IsInCache))
                if (await DownloadDependencyAsync(item, log)) count++;
            return count;
        }
        public async Task<bool> ImportFileAsync(DependencyItem item, string sourceFilePath)
        {
            try
            {
                string extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
                bool archive = extension == ".zip";
                if (item.Category == DependencyCategory.NvidiaProprietary && !archive && !IsRuntimeFileName(sourceFilePath, item.TargetRelativePath))
                    throw new InvalidDataException("Se requiere " + item.TargetRelativePath + "; no se aceptan otros runtimes como aliases.");
                string kind = archive ? "ZipExtract" : extension == ".exe" && item.Id == "reshade_addon" ? "ReShadeExeExtract" : "Direct";
                byte[] bytes = await File.ReadAllBytesAsync(sourceFilePath);
                await Task.Run(() => ImportBytes(item, bytes, kind));
                return true;
            }
            catch (Exception ex) { item.Status = DependencyStatus.Error; item.StatusMessage = ex.Message; return false; }
        }
        internal static bool IsRuntimeFileName(string path, string target)
        {
            string stem = Path.GetFileNameWithoutExtension(target);
            return System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"(?: \(\d+\))?\.dll$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        public void AutoDetectAndImportFromDownloads(List<DependencyItem> items, Action<string>? log = null)
        {
            if (!Directory.Exists(_downloads)) return;
            string[] candidates = Directory.EnumerateFiles(_downloads, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToArray();
            foreach (var item in items.Where(x => !x.IsEmbedded && !x.IsInCache))
                foreach (string candidate in candidates.Where(x => Path.GetExtension(x).Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
                    IsRuntimeFileName(x, item.TargetRelativePath) || (item.Id == "reshade_addon" && Path.GetFileName(x).StartsWith("ReShade", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(x).Equals(".exe", StringComparison.OrdinalIgnoreCase))))
                    if (ImportFileAsync(item, candidate).GetAwaiter().GetResult()) { log?.Invoke("Importado: " + Path.GetFileName(candidate)); break; }
        }
        private void ImportBytes(DependencyItem item, byte[] source, string kind)
        {
            if (item.CanAutoDownload && (string.IsNullOrEmpty(item.ExpectedSha256) || Hash(source) != item.ExpectedSha256))
                throw new InvalidDataException("El paquete no coincide con la versión y SHA-256 del catálogo.");
            string root = PackageDirectory(item);
            Directory.CreateDirectory(root);
            var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (kind == "ZipExtract")
            {
                using var archive = new ZipArchive(new MemoryStream(source), ZipArchiveMode.Read);
                foreach (var pair in GetPackageFiles(item))
                {
                    var matches = archive.Entries.Where(e => e.FullName.Replace('\\', '/').Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                        e.FullName.Replace('\\', '/').EndsWith("/" + pair.Key, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1 || matches[0].Length > 512L * 1024 * 1024) throw new InvalidDataException("Recurso ausente, ambiguo o demasiado grande: " + pair.Key);
                    using var input = matches[0].Open(); using var output = new MemoryStream();
                    input.CopyTo(output); payload.Add(pair.Value, output.ToArray());
                }
            }
            else if (kind == "ReShadeExeExtract") payload.Add(item.TargetRelativePath, ExtractReShade(source));
            else
            {
                if (GetPackageFiles(item).Count != 1) throw new InvalidDataException("Importa el paquete ZIP completo.");
                payload.Add(item.TargetRelativePath, source);
            }
            var receipt = new Receipt { SourceSha256 = item.ExpectedSha256 };
            using var lease = new InstallationLease(root);
            using var transaction = new FileTransaction(root);
            foreach (var pair in payload)
            {
                string hash = Hash(pair.Value);
                if (item.ExpectedFileSha256.TryGetValue(pair.Key, out string? expected) && hash != expected) throw new InvalidDataException("Recurso incorrecto: " + pair.Key);
                if (item.Category == DependencyCategory.NvidiaProprietary)
                {
                    string probe = Path.Combine(root, Guid.NewGuid().ToString("N") + ".dll");
                    try { File.WriteAllBytes(probe, pair.Value); BinaryIdentity.ValidateNvidia(probe, Path.GetFileName(pair.Key)); }
                    finally { if (File.Exists(probe)) File.Delete(probe); }
                }
                transaction.Write(pair.Key, pair.Value); receipt.Files.Add(pair.Key, hash);
            }
            transaction.Write("receipt.json", System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt)));
            transaction.Commit();
            item.AvailableChecksums.Clear();
            foreach (var file in receipt.Files) item.AvailableChecksums[file.Key.Replace('\\', '/')] = file.Value;
            item.LocalCachedPath = ManagedPaths.Resolve(root, item.TargetRelativePath);
            item.FileSize = payload.Sum(x => (long)x.Value.Length);
            item.IsInCache = true; item.Status = DependencyStatus.InCache; item.StatusMessage = "Importación verificada";
        }
        private static byte[] ExtractReShade(byte[] setup)
        {
            string directory = Path.Combine(Path.GetTempPath(), "NeuralFX_ReShade_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "setup.exe"); File.WriteAllBytes(path, setup);
                var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "tar.exe")) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in new[] { "-xf", path, "ReShade64.dll" }) info.ArgumentList.Add(arg);
                using var process = Process.Start(info) ?? throw new IOException("No se pudo ejecutar tar.exe.");
                if (!process.WaitForExit(15000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Tiempo agotado extrayendo ReShade."); }
                if (process.ExitCode != 0) throw new IOException("tar.exe no pudo extraer ReShade64.dll.");
                return File.ReadAllBytes(ManagedPaths.Resolve(directory, "ReShade64.dll"));
            }
            finally { Directory.Delete(directory, true); }
        }
        internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        public static string CalculateSha256(string filePath) { using var stream = File.OpenRead(filePath); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
        private sealed class Receipt
        {
            public string? SourceSha256 { get; set; }
            public Dictionary<string, string> Files { get; set; } = new();
        }
    }
}
