using System.Text;
using NeuralFX.Hub.Services;

namespace NeuralFX.Tests;

public sealed class TerrainCandidateTests
{
    [Fact]
    public void HubShipsTheMatchingShaderWithoutNeedingADownload()
    {
        var manager = new DependencyManagerService();
        var item = manager.GetInitialDependencies().Single(x => x.Id == "dlss5_feeder");
        Assert.Equal("Bundled", item.SourceType);
        Assert.True(item.IsInCache);
        Assert.False(item.CanAutoDownload);
        var bytes = manager.ReadPayload(item)[item.TargetRelativePath];
        var shader = Encoding.UTF8.GetString(bytes);
        Assert.Contains("NFX_DepthRectEnabled < hidden = true; > = false", shader);
        Assert.Contains("float RawDepthLegacy(float2 uv)", shader);
        Assert.Contains("NFX_DepthSceneRect.zw", shader);
        Assert.Equal(DependencyManagerService.Hash(bytes), item.AvailableChecksums[item.TargetRelativePath]);
    }

    [Fact]
    public async Task FullReinstallPreservesSettingsAndRegistersBundledShader()
    {
        string root = Path.Combine(Path.GetTempPath(), "NeuralFX_Terrain_" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        try
        {
            File.WriteAllText(Path.Combine(game, "Cities.exe"), "fixture");
            File.WriteAllText(Path.Combine(game, "ReShade.ini"), "[RenoDX.DLSS5]\nNeuralUplift=0\nNRIntensity=1.58\n");
            File.WriteAllText(Path.Combine(game, "unrelated.txt"), "preserve");
            var manager = new DependencyManagerService(Path.Combine(root, "storage"));
            var items = manager.GetInitialDependencies().Where(x => x.Id == "dlss5_feeder" || x.Id == "neuralfx_bridge").ToList();
            var config = PipelineConfiguration.Create(game, PipelinePreset.Native);
            var removal = await new UninstallService(Path.Combine(root, "storage"), () => false).UninstallAsync(UninstallService.Inspect(game));
            Assert.True(removal.Success, removal.Message);
            Assert.False(UninstallService.Inspect(game).HasWork);
            Assert.True(await new InstallationEngineService(manager, () => false).InstallAsync(game, items, preparedConfiguration: config));
            Assert.Equal("0", Ini.Get(File.ReadAllText(Path.Combine(game, "ReShade.ini")), "RenoDX.DLSS5", "NeuralUplift"));
            Assert.Equal("1.58", Ini.Get(File.ReadAllText(Path.Combine(game, "ReShade.ini")), "RenoDX.DLSS5", "NRIntensity"));
            Assert.Equal("preserve", File.ReadAllText(Path.Combine(game, "unrelated.txt")));
            foreach (var entry in ManifestStore.Read(game).FileChecksums)
                Assert.Equal(entry.Value, DependencyManagerService.CalculateSha256(ManagedPaths.Resolve(game, entry.Key)));
            var shader = items.Single(x => x.Id == "dlss5_feeder");
            Assert.Equal(manager.ReadPayload(shader)[shader.TargetRelativePath], File.ReadAllBytes(ManagedPaths.Resolve(game, shader.TargetRelativePath)));
        }
        finally { Directory.Delete(root, true); }
    }
}
