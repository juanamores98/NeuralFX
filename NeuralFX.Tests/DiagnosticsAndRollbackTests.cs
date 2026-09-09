using System.IO.Compression;
using System.Text.Json;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;

namespace NeuralFX.Tests;

public sealed class DiagnosticsAndRollbackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NeuralFX_Tests_" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly DependencyManagerService _dependencies;
    public DiagnosticsAndRollbackTests()
    {
        _game = Path.Combine(_root, "game");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "Cities.exe"), "game");
        _dependencies = new(Path.Combine(_root, "storage"), Path.Combine(_root, "downloads"));
    }
    private async Task<DependencyItem> Payload(string target = "reshade-shaders/Shaders/test.fx", string content = "new")
    {
        string source = Path.Combine(_root, "input.fx"); File.WriteAllText(source, content);
        var item = new DependencyItem { Id = "test", TargetRelativePath = target, Category = DependencyCategory.Shaders, SourceType = "UserProvided" };
        Assert.True(await _dependencies.ImportFileAsync(item, source), item.StatusMessage);
        return item;
    }
    [Fact]
    public async Task CacheFromPreviousCatalogCanBeReplacedWithoutBreakingDiscovery()
    {
        var item = await Payload();
        item.ExpectedSha256 = new string('a', 64);
        _dependencies.RefreshDependencyStatuses(new() { item }, _game);
        Assert.False(item.IsInCache);
        Assert.Contains("otra versión", item.StatusMessage);
        item.ExpectedSha256 = null;
        File.WriteAllText(item.LocalCachedPath ?? Path.Combine(_dependencies.CacheDirectory, item.Id, item.TargetRelativePath), "tampered");
        _dependencies.RefreshDependencyStatuses(new() { item }, _game);
        Assert.False(item.IsInCache);
        Assert.Contains("Integridad incorrecta", item.StatusMessage);
    }
    [Fact]
    public void UnsupportedManifestIsReportedWithoutCrashingMonitor()
    {
        File.WriteAllText(Path.Combine(_game, "NeuralFX_Manifest.json"), "{\"SchemaVersion\":99}");
        var report = IntegrityMonitor.Scan(_game);
        Assert.False(report.Valid);
        Assert.Equal("MANIFIESTO A REVISAR", report.Summary);
    }
    [Fact]
    public void PresetCannotBeOverriddenByLegacySectionDuplicates()
    {
        File.WriteAllText(Path.Combine(_game, "dlss5-feed.cfg"), "enabled=0\nwork_resolution=50\n[General]\nEnabled=0\nwork_resolution=75\ncustom=17\n");
        string text = System.Text.Encoding.UTF8.GetString(PipelineConfiguration.Create(_game, PipelinePreset.BalancedCost)["dlss5-feed.cfg"]);
        Assert.Single(text.Split('\n').Where(x => x.StartsWith("enabled=", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("enabled=1", text);
        Assert.Single(text.Split('\n').Where(x => x.StartsWith("work_resolution=", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("work_resolution=85", text); Assert.Contains("custom=17", text);
    }
    [Fact]
    public void CatalogRequiresActualPipelineAndSeparateNeuralRuntime()
    {
        var items = _dependencies.GetInitialDependencies();
        Assert.Contains(items, i => i.Id == "lumenite" && i.PackageFiles.Count >= 6);
        Assert.Contains(items, i => i.Id == "dlss5_feeder" && i.PackageFiles.ContainsKey("DLSS5_Feed.fx"));
        Assert.Contains(items, i => i.Id == "nvngx_dlssnr" && i.IsRequired);
        Assert.DoesNotContain(items, i => i.TargetRelativePath == "nvngx_dlssd.dll" || i.Aliases != null);
        Assert.All(items.Where(i => i.CanAutoDownload), i => Assert.Equal(64, i.ExpectedSha256!.Length));
        Assert.False(Directory.Exists(_dependencies.CacheDirectory)); // discovery has no import side effects
    }
    [Fact]
    public async Task ReinstallThenRollbackRestoresEveryOriginalAndPreservesUnrelatedFiles()
    {
        Directory.CreateDirectory(Path.Combine(_game, "reshade-shaders", "Shaders"));
        string target = Path.Combine(_game, "reshade-shaders", "Shaders", "test.fx");
        File.WriteAllText(target, "original");
        File.WriteAllText(Path.Combine(_game, "ReShade.ini"), "[GENERAL]\nSomeUserSetting=42\n");
        string unrelated = Path.Combine(_game, "reshade-shaders", "Shaders", "other.fx"); File.WriteAllText(unrelated, "other");
        var item = await Payload(); var install = new InstallationEngineService(_dependencies, () => false);
        Assert.True(await install.InstallAsync(_game, new() { item }));
        Assert.True(await install.InstallAsync(_game, new() { item }, preset: PipelinePreset.Photography));
        Assert.True(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal("original", File.ReadAllText(target));
        Assert.Equal("[GENERAL]\nSomeUserSetting=42\n", File.ReadAllText(Path.Combine(_game, "ReShade.ini")));
        Assert.Equal("other", File.ReadAllText(unrelated));
        Assert.Equal("game", File.ReadAllText(Path.Combine(_game, "Cities.exe")));
        Assert.False(File.Exists(Path.Combine(_game, "NeuralFX_Manifest.json")));
    }
    [Fact]
    public async Task FailureAfterEachMutationRestoresPreinstallState()
    {
        var item = await Payload();
        File.WriteAllText(Path.Combine(_game, "ReShade.ini"), "before");
        for (int failure = 0; failure < 7; failure++)
        {
            int at = failure;
            var install = new InstallationEngineService(_dependencies, () => false) { AfterWrite = i => { if (i == at) throw new IOException("simulated failure"); } };
            Assert.False(await install.InstallAsync(_game, new() { item }), "Failure index: " + failure);
            Assert.Equal("before", File.ReadAllText(Path.Combine(_game, "ReShade.ini")));
            Assert.False(File.Exists(Path.Combine(_game, "NeuralFX_Manifest.json")));
            Assert.False(File.Exists(Path.Combine(_game, item.TargetRelativePath)));
            Assert.False(Directory.Exists(Path.Combine(_game, ".neuralfx-transaction")));
        }
    }
    [Fact]
    public async Task MissingDependencyDoesNotLeaveAnyInstalledFiles()
    {
        var good = await Payload(); var missing = new DependencyItem { Id = "missing", TargetRelativePath = "missing.dll" };
        Assert.False(await new InstallationEngineService(_dependencies, () => false).InstallAsync(_game, new() { good, missing }));
        Assert.Single(Directory.GetFiles(_game));
    }
    [Fact]
    public async Task GameRunningBlocksBeforeWrites()
    {
        var item = await Payload();
        Assert.False(await new InstallationEngineService(_dependencies, () => true).InstallAsync(_game, new() { item }));
        Assert.Single(Directory.GetFiles(_game));
    }
    [Fact]
    public async Task NoManifestNeverPurgesOtherInstallations()
    {
        File.WriteAllText(Path.Combine(_game, "dxgi.dll"), "other injector");
        Directory.CreateDirectory(Path.Combine(_game, "reshade-shaders"));
        Assert.True(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal("other injector", File.ReadAllText(Path.Combine(_game, "dxgi.dll")));
        Assert.True(Directory.Exists(Path.Combine(_game, "reshade-shaders")));
    }
    [Fact]
    public async Task MalformedManifestFailsClosed()
    {
        File.WriteAllText(Path.Combine(_game, "NeuralFX_Manifest.json"), "{broken");
        File.WriteAllText(Path.Combine(_game, "dxgi.dll"), "keep");
        Assert.False(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_game, "dxgi.dll")));
    }
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("test:stream")]
    public async Task ManifestCannotEscapeGameDirectory(string path)
    {
        var manifest = new InstallationManifest { SchemaVersion = 2, GameDirectory = _game, InstalledFiles = new() { path }, FileChecksums = new() { [path] = "hash" } };
        File.WriteAllText(Path.Combine(_game, "NeuralFX_Manifest.json"), JsonSerializer.Serialize(manifest));
        Assert.False(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.True(File.Exists(Path.Combine(_game, "Cities.exe")));
    }
    [Fact]
    public async Task EditedFileIsPreservedBeforeRestoringOriginal()
    {
        var item = await Payload();
        Assert.True(await new InstallationEngineService(_dependencies, () => false).InstallAsync(_game, new() { item }));
        File.WriteAllText(Path.Combine(_game, item.TargetRelativePath), "user edit");
        Assert.True(await new RollbackService(() => false).RollbackAsync(_game));
        string preserved = Assert.Single(Directory.GetFiles(Path.Combine(_game, ".neuralfx-preserved"), "test.fx", SearchOption.AllDirectories));
        Assert.Equal("user edit", File.ReadAllText(preserved));
    }
    [Fact]
    public async Task CorruptedCacheIsRejected()
    {
        var item = await Payload(); File.WriteAllText(item.LocalCachedPath!, "corrupt");
        Assert.False(await new InstallationEngineService(_dependencies, () => false).InstallAsync(_game, new() { item }));
        Assert.Single(Directory.GetFiles(_game));
    }
    [Fact]
    public async Task ZipMustContainExactlyTheRequestedResource()
    {
        string archive = Path.Combine(_root, "wrong.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("nvngx_dlssd.dll").Open()); writer.Write("wrong runtime"); }
        var item = new DependencyItem { Id = "nr", TargetRelativePath = "nvngx_dlssnr.dll", Category = DependencyCategory.NvidiaProprietary, SourceType = "UserProvided" };
        Assert.False(await _dependencies.ImportFileAsync(item, archive));
        Assert.Null(item.LocalCachedPath);
    }
    [Theory]
    [InlineData("nvngx_dlss (1).dll", "nvngx_dlss.dll", true)]
    [InlineData("nvngx_dlssd.dll", "nvngx_dlssnr.dll", false)]
    [InlineData("nvngx_dlssnr.dll", "nvngx_dlss.dll", false)]
    public void RuntimeNamesAreNotInterchangeable(string candidate, string expected, bool accepted) => Assert.Equal(accepted, DependencyManagerService.IsRuntimeFileName(candidate, expected));
    [Fact]
    public async Task LegacyUpgradeRestoresExactPreviousInstallationInsteadOfInventingOwnership()
    {
        var item = await Payload();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_game, item.TargetRelativePath))!);
        File.WriteAllText(Path.Combine(_game, item.TargetRelativePath), "legacy payload");
        string legacy = JsonSerializer.Serialize(new InstallationManifest { GameDirectory = _game, Version = "1.0.0", InstalledFiles = new() { item.TargetRelativePath }, BackedUpFiles = new() { [item.TargetRelativePath] = "C:/old-backup-untouched" } });
        File.WriteAllText(Path.Combine(_game, "NeuralFX_Manifest.json"), legacy);
        var installer = new InstallationEngineService(_dependencies, () => false);
        Assert.True(await installer.InstallAsync(_game, new() { item }));
        Assert.NotNull(ManifestStore.Read(_game).PreviousManifestBackup);
        Assert.True(await installer.InstallAsync(_game, new() { item }));
        Assert.True(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal(legacy, File.ReadAllText(Path.Combine(_game, "NeuralFX_Manifest.json")));
        Assert.Equal("legacy payload", File.ReadAllText(Path.Combine(_game, item.TargetRelativePath)));
    }
    [Fact]
    public async Task IntegrityDistinguishesUserConfigurationFromMissingBinaries()
    {
        var item = await Payload();
        Assert.True(await new InstallationEngineService(_dependencies, () => false).InstallAsync(_game, new() { item }));
        Assert.False(IntegrityMonitor.Scan(_game).NeedsRepair);
        File.AppendAllText(Path.Combine(_game, "ReShadePreset.ini"), "\nUserSetting=123\n");
        var custom = IntegrityMonitor.Scan(_game); Assert.False(custom.NeedsRepair); Assert.Single(custom.Details);
        File.Delete(Path.Combine(_game, item.TargetRelativePath));
        Assert.True(IntegrityMonitor.Scan(_game).NeedsRepair);
    }
    [Fact]
    public void SignedRuntimeProductMustMatchRequestedRole()
    {
        Assert.Throws<InvalidDataException>(() => BinaryIdentity.ValidateProductName("NVIDIA DLSS Ray Reconstruction", "nvngx_dlssnr.dll"));
        Assert.Throws<InvalidDataException>(() => BinaryIdentity.ValidateProductName("NVIDIA DLSSNR", "nvngx_dlss.dll"));
        BinaryIdentity.ValidateProductName("NVIDIA DLSSNR", "nvngx_dlssnr.dll");
        BinaryIdentity.ValidateProductName("NVIDIA Deep Learning SuperSampling", "nvngx_dlss.dll");
    }
    [Fact]
    public void StartupConfigurationEnablesFastStartupAndCachedPerformance()
    {
        File.WriteAllText(Path.Combine(_game, "ReShade.ini"), "[GENERAL]\nStartupPresetPath=old.ini\nOther=42\n");
        var files = PipelineConfiguration.Create(_game, PipelinePreset.Native);
        string ini = System.Text.Encoding.UTF8.GetString(files["ReShade.ini"]);
        Assert.Equal("0", Ini.Get(ini, "GENERAL", "NoReloadOnInit"));
        Assert.Equal("1", Ini.Get(ini, "GENERAL", "PerformanceMode"));
        Assert.Equal("1", Ini.Get(ini, "GENERAL", "NoDebugInfo"));
        Assert.Equal("0", Ini.Get(ini, "GENERAL", "NoEffectCache"));
        Assert.Equal("2", Ini.Get(ini, "DEPTH", "DrawStatsHeuristic"));
        Assert.Equal(@".\ReShadePreset.ini", Ini.Get(ini, "GENERAL", "StartupPresetPath"));
        Assert.Equal("42", Ini.Get(ini, "GENERAL", "Other"));
    }
    [Fact]
    public void HardwareConfigurationNeverSilentlySelectsPatchedRuntimes()
    {
        var items = _dependencies.GetInitialDependencies();
        var nr = items.First(x => x.Id == "nvngx_dlssnr");
        string version = nr.Version; string? url = nr.DownloadUrl;
        foreach (var architecture in Enum.GetValues<GpuArchitecture>()) {
            _dependencies.ConfigureForHardware(items,new HardwareInfo {DetectedArchitecture=architecture});
            Assert.Equal(version,nr.Version); Assert.Equal(url,nr.DownloadUrl);
            Assert.False(nr.CanAutoDownload);
        }
    }
    [Fact]
    public void PipelineFootprintIncludesReShadeGeneratedFiles()
    {
        Assert.Contains("ReShadeGUI.ini", PipelineFootprint.Files);
        Assert.Contains("dxgi.log", PipelineFootprint.Files);
        Assert.Contains("ReShadePreset.ini", PipelineFootprint.Files);
    }
    [Fact]
    public void ConfigurationUsesSupportedKeysAndRetainsUserEffects()
    {
        File.WriteAllText(Path.Combine(_game, "ReShadePreset.ini"), "Techniques=UserEffect@user.fx\n[user.fx]\nValue=42");
        var files = PipelineConfiguration.Create(_game, PipelinePreset.BalancedCost);
        string cfg = System.Text.Encoding.UTF8.GetString(files["dlss5-feed.cfg"]);
        Assert.Contains("work_resolution=85", cfg); Assert.DoesNotContain("PerformanceProfile", cfg); Assert.DoesNotContain("JitterCancellation", cfg);
        string preset = System.Text.Encoding.UTF8.GetString(files["ReShadePreset.ini"]);
        Assert.Contains("UserEffect@user.fx,Lumenite_Kernel", preset); Assert.Contains("Value=42", preset);
        string shader = System.Text.Encoding.UTF8.GetString(files["reshade-shaders/Shaders/NeuralFX_CAS.fx"]);
        Assert.DoesNotContain("#define BUFFER_WIDTH", shader);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
