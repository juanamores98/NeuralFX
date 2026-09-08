using System.Diagnostics;
using System.Text.Json;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;

namespace NeuralFX.Tests;

public sealed class UninstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NeuralFX_Uninstall_" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly DependencyManagerService _manager;
    private readonly UninstallService _uninstaller;
    public UninstallTests()
    {
        _game = Path.Combine(_root, "game"); Directory.CreateDirectory(_game);
        Put("Cities.exe", "game executable"); Put("Cities_Data/unchanged.dat", "game data");
        Put("steam_api64.dll", "unrelated library"); Put("another-mod.xml", "user settings");
        _manager = new(Path.Combine(_root, "storage"));
        _uninstaller = new(Path.Combine(_root, "storage"), () => false);
    }
    private void Put(string path, string text)
    { string file = Path.Combine(_game, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
    private Dictionary<string, string> Snapshot() => Directory.GetFiles(_game, "*", SearchOption.AllDirectories)
        .ToDictionary(x => Path.GetRelativePath(_game, x), DependencyManagerService.CalculateSha256);
    private string[] Directories() => Directory.GetDirectories(_game, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(_game, x)).Order().ToArray();
    private async Task<List<DependencyItem>> InstallCatalog()
    {
        var items = new List<DependencyItem>();
        // Stand-in bytes at every real destination; NVIDIA identity is independently covered by importer tests.
        foreach (var component in DependencyManagerService.ReadCatalog().Where(x => !x.IsEmbedded))
            foreach (string target in _manager.GetPackageFiles(component).Values)
            {
                string source = Path.Combine(_root, "source.fx"); File.WriteAllText(source, "fixture bytes for " + target);
                var item = new DependencyItem { Id = "item" + items.Count, TargetRelativePath = target, SourceType = "UserProvided", Category = DependencyCategory.Shaders };
                Assert.True(await _manager.ImportFileAsync(item, source)); items.Add(item);
            }
        Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, items));
        return items;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EntireCatalogInstallReinstallUninstallRestoresCleanFileAndDirectoryInventory(bool runtimeWasUsed)
    {
        var before = Snapshot(); var directories = Directories();
        var items = await InstallCatalog();
        var manifest = ManifestStore.Read(_game);
        Assert.All(manifest.InstalledFiles, path => Assert.Contains(PipelineFootprint.Normalize(path), PipelineFootprint.Files));
        Assert.Equal(20, manifest.InstalledFiles.Count);
        Assert.True(Directory.Exists(Path.Combine(_game, ".neuralfx-runtime", "ReShade")));
        Assert.Contains(@"IntermediateCachePath=.\.neuralfx-runtime\ReShade", File.ReadAllText(Path.Combine(_game, "ReShade.ini")));
        Assert.Contains(@"SavePath=.\.neuralfx-runtime\Capturas", File.ReadAllText(Path.Combine(_game, "ReShade.ini")));
        Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, items, preset: PipelinePreset.Photography));
        if (runtimeWasUsed)
        {
            Put("ReShade.log", "runtime log"); Put("dlss5-feed.log", "feeder log"); Put("dlss5-feed-crash.dmp", "crash dump");
            Put(".neuralfx-runtime/ReShade/compiled.cso", "cache"); Put(".neuralfx-runtime/Capturas/city.png", "user screenshot");
            Put(".neuralfx-preserved/previous/ReShade.ini", "previous edits");
            File.AppendAllText(Path.Combine(_game, "ReShade.ini"), "\nuser-change=1");
        }
        var preview = UninstallService.Inspect(_game);
        var result = await _uninstaller.UninstallAsync(preview);
        Assert.True(result.Success, result.Message);
        Assert.Equal(before.OrderBy(x => x.Key), Snapshot().OrderBy(x => x.Key)); Assert.Equal(directories, Directories());
        Assert.False(UninstallService.Inspect(_game).HasWork);
        Assert.False(PipelineFootprint.HasArtifacts(_game));
        Assert.NotNull(result.ArchiveDirectory);
        Assert.All(preview.Files, file => Assert.Equal(file.Value, DependencyManagerService.CalculateSha256(Path.Combine(result.ArchiveDirectory!, "files", file.Key))));
    }
    [Fact]
    public async Task UninstallDoesNotRestoreOldInjectorOrOldManifestAndKeepsUnrelatedShader()
    {
        Put("dxgi.dll", "old injector"); Put("nvngx_dlssd.dll", "legacy RR"); Put("reshade-shaders/Shaders/CAS.fx", "legacy CAS");
        Put("reshade-shaders/Shaders/unrelated.fx", "another shader");
        Put("NeuralFX_Manifest.json", JsonSerializer.Serialize(new InstallationManifest { SchemaVersion = 1, GameDirectory = _game }));
        await InstallCatalog();
        Assert.NotNull(ManifestStore.Read(_game).PreviousManifestBackup);
        var plan = UninstallService.Inspect(_game); var result = await _uninstaller.UninstallAsync(plan);
        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(Path.Combine(_game, "dxgi.dll"))); Assert.False(File.Exists(Path.Combine(_game, "NeuralFX_Manifest.json")));
        Assert.False(File.Exists(Path.Combine(_game, "nvngx_dlssd.dll"))); Assert.False(File.Exists(Path.Combine(_game, "reshade-shaders/Shaders/CAS.fx")));
        Assert.Equal("another shader", File.ReadAllText(Path.Combine(_game, "reshade-shaders/Shaders/unrelated.fx")));
        Assert.False(UninstallService.Inspect(_game).HasWork);
        Assert.False(PipelineFootprint.HasArtifacts(_game));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("{\"InstalledFiles\":[\"../outside\",\"Cities.exe\",\"another-mod.xml\"],\"BackedUpFiles\":{\"dxgi.dll\":\"C:\\outside.dll\"}}")]
    public async Task MissingOrDamagedManifestCanBeRemovedWithoutTrustingItsPaths(string? manifest)
    {
        Put("dxgi.dll", "injector"); Put("ReShade.log", "residual log"); Put(".neuralfx-backups/stale.bin", "stale backup");
        if (manifest != null) Put("NeuralFX_Manifest.json", manifest);
        var result = await _uninstaller.UninstallAsync(UninstallService.Inspect(_game));
        Assert.True(result.Success, result.Message); Assert.False(UninstallService.Inspect(_game).HasWork);
        Assert.Equal("game executable", File.ReadAllText(Path.Combine(_game, "Cities.exe")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(_game, "another-mod.xml")));
    }
    [Fact]
    public async Task GameRunningBlocksBeforeArchivingOrRemovingFiles()
    {
        Put("dxgi.dll", "keep"); var before = Snapshot();
        var blocked = new UninstallService(Path.Combine(_root, "blocked"), () => true);
        var result = await blocked.UninstallAsync(UninstallService.Inspect(_game));
        Assert.False(result.Success); Assert.Equal(before.OrderBy(x => x.Key), Snapshot().OrderBy(x => x.Key));
        Assert.False(Directory.Exists(blocked.ArchiveRoot));
    }
    [Fact]
    public async Task ChangedPreviewCannotDeleteChangedOrNewFiles()
    {
        Put("dxgi.dll", "before"); var plan = UninstallService.Inspect(_game);
        Put("dxgi.dll", "after"); Put("dlss5-feed.log", "created after review");
        var result = await _uninstaller.UninstallAsync(plan);
        Assert.False(result.Success); Assert.Contains("vista previa", result.Message);
        Assert.Equal("after", File.ReadAllText(Path.Combine(_game, "dxgi.dll")));
        Assert.False(Directory.Exists(_uninstaller.ArchiveRoot));
    }
    [Fact]
    public async Task LockedFileFailsBeforeTheFirstDeletionAndCanBeRetried()
    {
        Put("dxgi.dll", "keep"); Put("ReShade.ini", "config"); var before = Snapshot();
        var plan = UninstallService.Inspect(_game);
        using (var locked = new FileStream(Path.Combine(_game, "dxgi.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await _uninstaller.UninstallAsync(plan);
            Assert.False(result.Success); Assert.Equal(before.OrderBy(x => x.Key), Snapshot().OrderBy(x => x.Key));
        }
        Assert.True((await _uninstaller.UninstallAsync(UninstallService.Inspect(_game))).Success);
    }
    [Fact]
    public async Task FailureAfterEveryDeletionRecoversAllOriginalBytes()
    {
        Put("dxgi.dll", "injector"); Put("dlss5-feed.log", "log"); Put(".neuralfx-runtime/ReShade/test.cso", "cache");
        var before = Snapshot(); var directories = Directories(); var plan = UninstallService.Inspect(_game);
        for (int failedAt = 0; failedAt < plan.Files.Count; failedAt++)
        {
            int index = failedAt;
            _uninstaller.AfterWrite = current => { if (current == index) throw new IOException("injected failure"); };
            var result = await _uninstaller.UninstallAsync(UninstallService.Inspect(_game));
            Assert.False(result.Success); Assert.Equal(before.OrderBy(x => x.Key), Snapshot().OrderBy(x => x.Key)); Assert.Equal(directories, Directories());
        }
    }
    [Fact]
    public async Task CleanOrAlreadyUninstalledGameIsAnIdempotentNoOp()
    {
        var before = Snapshot(); var result = await _uninstaller.UninstallAsync(UninstallService.Inspect(_game));
        Assert.True(result.Success); Assert.Null(result.ArchiveDirectory);
        Assert.Equal(before.OrderBy(x => x.Key), Snapshot().OrderBy(x => x.Key)); Assert.False(Directory.Exists(_uninstaller.ArchiveRoot));
    }
    [Fact]
    public async Task ArchiveCannotBePlacedInsideTheGame()
    {
        Put("dxgi.dll", "keep"); var unsafeService = new UninstallService(Path.Combine(_game, "copies"), () => false);
        Assert.False((await unsafeService.UninstallAsync(UninstallService.Inspect(_game))).Success);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_game, "dxgi.dll")));
    }
    [Fact]
    public void PrivateDirectoryCannotRedirectRemovalOutsideTheGame()
    {
        string outside = Path.Combine(_root, "outside"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.dat"), "outside");
        string link = Path.Combine(_game, ".neuralfx-runtime");
        CreateJunction(link, outside);
        try { Assert.Throws<IOException>(() => UninstallService.Inspect(_game)); Assert.Equal("outside", File.ReadAllText(Path.Combine(outside, "keep.dat"))); }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public async Task ArchiveJunctionCannotRedirectBackupsIntoTheGame()
    {
        Put("dxgi.dll", "keep");
        string link = Path.Combine(_root, "redirected-storage"); CreateJunction(link, _game);
        try
        {
            var redirected = new UninstallService(link, () => false);
            Assert.False((await redirected.UninstallAsync(UninstallService.Inspect(_game))).Success);
            Assert.Equal("keep", File.ReadAllText(Path.Combine(_game, "dxgi.dll")));
            Assert.False(Directory.Exists(Path.Combine(_game, "Backups")));
        }
        finally { Directory.Delete(link); }
    }
    private static void CreateJunction(string link, string target)
    {
        // Directory junctions exercise the same reparse-point guard without requiring symlink privileges.
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + target.Replace("'", "''") + "' | Out-Null" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        if (!process.WaitForExit(10000)) { process.Kill(); throw new TimeoutException("Junction creation did not finish."); }
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }
    public void Dispose() => Directory.Delete(_root, true);
}
