using System.Diagnostics;
using NeuralFX.Hub.Services;
using NeuralFX.Protocol;

namespace NeuralFX.Tests;

public sealed class ProcessBoundaryTests
{
    private static Process Start(params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ProcessFixture", "NeuralFX.ProcessFixture.dll"));
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        return Process.Start(info)!;
    }
    [Fact]
    public async Task TerminatedInstallerRecoversOriginalAndRemovesPartialFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "NeuralFX_Crash_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "owned.dll"), "original");
        using var process = Start("transaction", root);
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("changed", File.ReadAllText(Path.Combine(root, "owned.dll")));
            Assert.True(File.Exists(Path.Combine(root, "new.dll")));
            process.Kill(); await process.WaitForExitAsync();
            using var lease = new InstallationLease(root); // Also recovers an abandoned mutex.
            FileTransaction.Recover(root);
            Assert.Equal("original", File.ReadAllText(Path.Combine(root, "owned.dll")));
            Assert.False(File.Exists(Path.Combine(root, "new.dll")));
            Assert.False(Directory.Exists(Path.Combine(root, ".neuralfx-transaction")));
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } Directory.Delete(root, true); }
    }
    [Fact]
    public async Task TerminatedUninstallerRecoversAndCanCompleteOnRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), "NeuralFX_UninstallCrash_" + Guid.NewGuid().ToString("N"));
        string game = Path.Combine(root, "game"), storage = Path.Combine(root, "storage");
        Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "Cities.exe"), "game");
        File.WriteAllText(Path.Combine(game, "dxgi.dll"), "injector"); File.WriteAllText(Path.Combine(game, "ReShade.ini"), "config");
        using var process = Start("uninstall", game, storage);
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            process.Kill(); await process.WaitForExitAsync();
            Assert.Throws<IOException>(() => UninstallService.Inspect(game));
            var service = new UninstallService(storage, () => false); await service.RecoverAsync(game);
            Assert.Equal("injector", File.ReadAllText(Path.Combine(game, "dxgi.dll")));
            Assert.Equal("config", File.ReadAllText(Path.Combine(game, "ReShade.ini")));
            var result = await service.UninstallAsync(UninstallService.Inspect(game));
            Assert.True(result.Success, result.Message); Assert.False(UninstallService.Inspect(game).HasWork);
            Assert.Equal("game", File.ReadAllText(Path.Combine(game, "Cities.exe")));
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } Directory.Delete(root, true); }
    }
    [Fact]
    public async Task SeparateProcessPublishesCoherentFramesAndAcknowledgesCommand()
    {
        using var process = Start("telemetry");
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            using var reader = TelemetryChannel.Open(process.Id, false);
            Assert.NotNull(reader);
            bool acknowledged = false; int read = 0;
            var timeout = Stopwatch.StartNew();
            while (!process.HasExited && timeout.Elapsed < TimeSpan.FromSeconds(8))
            {
                if (reader.TryRead(out var frame) && frame.ProcessId == process.Id)
                {
                    Assert.Equal(frame.FrameIndex, frame.RenderWidth); Assert.Equal(-frame.FrameIndex, frame.RenderHeight); read++;
                    if (frame.LastCommand == 19) acknowledged = true;
                    else reader.Send(new TelemetryCommand { SessionId = frame.SessionId, Revision = 19, Kind = CommandKind.ResetHistory });
                }
                await Task.Delay(1);
            }
            Assert.True(acknowledged, "The owner did not acknowledge the command.");
            Assert.True(read > 10, "Too few coherent frames received.");
            Assert.True(process.HasExited, "The publishing process did not finish in time.");
            Assert.True(reader.TryRead(out var offline)); Assert.Equal(0, offline.UtcTicks);
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
    }
}
