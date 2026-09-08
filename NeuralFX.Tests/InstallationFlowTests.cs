using System.Text.Json;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;

namespace NeuralFX.Tests;

public sealed class InstallationFlowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NeuralFX_Flow_" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly DependencyManagerService _manager;
    public InstallationFlowTests()
    {
        _game = Path.Combine(_root, "game"); Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "Cities.exe"), "game");
        _manager = new(Path.Combine(_root, "storage"));
    }
    private async Task<DependencyItem> Payload()
    {
        string source = Path.Combine(_root, "input.fx"); File.WriteAllText(source, "new");
        var item = new DependencyItem { Id = "fixture", TargetRelativePath = "fixture.fx", SourceType = "UserProvided" };
        Assert.True(await _manager.ImportFileAsync(item, source)); return item;
    }
    private IntegrityReport Refresh(DependencyItem item)
    {
        var paths = _manager.GetPackageFiles(item).Values;
        var report = IntegrityMonitor.Scan(_game, paths);
        InstallationStatus.Apply(item, paths, report); return report;
    }
    [Fact]
    public async Task PreparedCopyIsNotReportedAsInstalledAndGameBlocksMutation()
    {
        var item = await Payload(); var report = Refresh(item);
        Assert.True(item.IsReady); Assert.Equal(ComponentGameState.Missing, item.GameState);
        var ready = InstallationGuidance.Create(true, true, false, report, new[] { item });
        Assert.Equal(0, ready.Installed); Assert.Equal(1, ready.Ready); Assert.True(ready.CanInstall); Assert.False(ready.CanRestore);
        var running = InstallationGuidance.Create(true, true, true, report, new[] { item });
        Assert.False(running.CanInstall); Assert.False(running.CanRestore); Assert.Contains("abierto", running.Next);
    }
    [Fact]
    public async Task UnregisteredFilesAreNotClaimedAsVerifiedOrRestorable()
    {
        var item = await Payload(); File.WriteAllText(Path.Combine(_game, item.TargetRelativePath), "some other installation");
        var report = Refresh(item);
        Assert.Equal(ComponentGameState.Unmanaged, item.GameState);
        Assert.False(report.Managed); Assert.Null(report.Restoration);
        Assert.Equal(0, InstallationGuidance.Create(true, true, false, report, new[] { item }).Installed);
    }
    [Fact]
    public async Task LiveScanReportsModifiedAndMissingFilesWithoutRestartingDiscovery()
    {
        var item = await Payload(); Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { item }));
        Refresh(item); Assert.Equal(ComponentGameState.Installed, item.GameState);
        File.WriteAllText(Path.Combine(_game, item.TargetRelativePath), "edited");
        Assert.True(Refresh(item).NeedsRepair); Assert.Equal(ComponentGameState.Modified, item.GameState);
        File.Delete(Path.Combine(_game, item.TargetRelativePath));
        var missing = Refresh(item);
        Assert.True(missing.NeedsRepair); Assert.Equal(ComponentGameState.Missing, item.GameState);
        Assert.DoesNotContain(item.TargetRelativePath, missing.Restoration!.Remove);
        Assert.Contains(item.TargetRelativePath, missing.Restoration.AlreadyAbsent);
    }
    [Fact]
    public async Task ConfigurationGroupIncludesAllGeneratedFilesAndPreservesCustomState()
    {
        var config = _manager.GetInitialDependencies().Single(x => x.IsEmbedded);
        Assert.Equal(4, _manager.GetPackageFiles(config).Count);
        Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { config }));
        File.AppendAllText(Path.Combine(_game, "dlss5-feed.cfg"), "\ncustom=1\n");
        var report = Refresh(config); Assert.False(report.NeedsRepair); Assert.Equal(ComponentGameState.Configured, config.GameState);
        Assert.Contains("dlss5-feed.cfg", report.Restoration!.Preserve);
        File.Delete(Path.Combine(_game, "ReShade.ini"));
        Assert.True(Refresh(config).NeedsRepair); Assert.Equal(ComponentGameState.Incomplete, config.GameState);
        Assert.Contains("3 de 4", config.GameStatusDetail);
    }
    [Fact]
    public async Task PreviewMatchesRollbackAndReinstallKeepsTheOriginalBaseline()
    {
        File.WriteAllText(Path.Combine(_game, "fixture.fx"), "original");
        File.WriteAllText(Path.Combine(_game, "unrelated.fx"), "keep");
        var item = await Payload(); var engine = new InstallationEngineService(_manager, () => false);
        Assert.True(await engine.InstallAsync(_game, new() { item }));
        Assert.True(await engine.InstallAsync(_game, new() { item }, preset: PipelinePreset.Photography));
        File.WriteAllText(Path.Combine(_game, "fixture.fx"), "user edit");
        var preview = Refresh(item).Restoration!;
        Assert.False(preview.RestoresLegacy); Assert.Equal(new[] { "fixture.fx" }, preview.Restore);
        Assert.Equal(4, preview.Remove.Length); Assert.Equal(new[] { "fixture.fx" }, preview.Preserve);
        Assert.DoesNotContain("unrelated.fx", preview.Restore.Concat(preview.Remove));
        // Preview is read-only.
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(_game, "fixture.fx")));
        Assert.True(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal("original", File.ReadAllText(Path.Combine(_game, "fixture.fx")));
        Assert.All(preview.Remove, p => Assert.False(File.Exists(Path.Combine(_game, p))));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_game, "unrelated.fx")));
        string preserved = Assert.Single(Directory.GetFiles(Path.Combine(_game, ".neuralfx-preserved"), "fixture.fx", SearchOption.AllDirectories));
        Assert.Equal("user edit", File.ReadAllText(preserved));
        Assert.Null(IntegrityMonitor.Scan(_game).Restoration);
    }
    [Fact]
    public async Task LegacyMigrationAndRestoreAreExplicitAndDoNotClaimCleanGame()
    {
        var legacy = new InstallationManifest { SchemaVersion = 1, GameDirectory = _game };
        File.WriteAllText(Path.Combine(_game, "NeuralFX_Manifest.json"), JsonSerializer.Serialize(legacy));
        File.WriteAllText(Path.Combine(_game, "fixture.fx"), "old mod");
        var item = await Payload(); var before = Refresh(item);
        Assert.True(before.CanMigrate); Assert.Null(before.Restoration);
        Assert.True(InstallationGuidance.Create(true, true, false, before, new[] { item }).CanInstall);
        Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { item }));
        var preview = Refresh(item).Restoration!;
        Assert.True(preview.RestoresLegacy); Assert.Contains("instalación antigua", preview.Target);
        Assert.Contains("Hub, el mod NeuralFX", preview.Scope);
        Assert.True(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal("old mod", File.ReadAllText(Path.Combine(_game, "fixture.fx")));
        Assert.True(Refresh(item).CanMigrate); Assert.Equal(ComponentGameState.Unmanaged, item.GameState);
    }
    [Fact]
    public async Task DamagedBackupDisablesRestoreAndInstallInsteadOfShowingGreen()
    {
        File.WriteAllText(Path.Combine(_game, "fixture.fx"), "original");
        var item = await Payload(); Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { item }));
        var manifest = ManifestStore.Read(_game);
        File.WriteAllText(Path.Combine(_game, manifest.BackedUpFiles["fixture.fx"]), "corrupt");
        var report = Refresh(item); var guidance = InstallationGuidance.Create(true, true, false, report, new[] { item });
        Assert.False(report.Valid); Assert.Null(report.Restoration); Assert.Equal(ComponentGameState.Unknown, item.GameState);
        Assert.False(guidance.CanRestore); Assert.False(guidance.CanInstall);
        Assert.False(await new RollbackService(() => false).RollbackAsync(_game));
        Assert.Equal("new", File.ReadAllText(Path.Combine(_game, "fixture.fx")));
    }
    [Fact]
    public async Task ImportingAnotherCopyDoesNotClaimThatTheGameWasUpdated()
    {
        var item = await Payload(); Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { item }));
        File.WriteAllText(Path.Combine(_root, "input.fx"), "next version");
        Assert.True(await _manager.ImportFileAsync(item, Path.Combine(_root, "input.fx")));
        var report = Refresh(item); var guidance = InstallationGuidance.Create(true, true, false, report, new[] { item });
        Assert.Equal(ComponentGameState.DifferentCopy, item.GameState); Assert.Equal(1, guidance.Installed);
        Assert.Contains("Aplicar componentes preparados", guidance.InstallLabel); Assert.True(guidance.CanInstall);
        Assert.Equal("new", File.ReadAllText(Path.Combine(_game, "fixture.fx")));
        Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { item }));
        Refresh(item); Assert.Equal(ComponentGameState.Installed, item.GameState);
    }
    [Fact]
    public async Task InstalledFilesRemainInstalledWhenThePreparedCopyIsRemoved()
    {
        var item = await Payload(); Assert.True(await new InstallationEngineService(_manager, () => false).InstallAsync(_game, new() { item }));
        File.Delete(item.LocalCachedPath!);
        _manager.RefreshDependencyStatuses(new() { item }, _game);
        var report = Refresh(item); var guidance = InstallationGuidance.Create(true, true, false, report, new[] { item });
        Assert.False(item.IsReady); Assert.Equal(ComponentGameState.Installed, item.GameState);
        Assert.Equal(1, guidance.Installed); Assert.False(guidance.CanInstall); Assert.True(guidance.CanRestore);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
