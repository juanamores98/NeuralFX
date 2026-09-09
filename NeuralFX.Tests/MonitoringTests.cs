using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NeuralFX.Hub.Services;
using NeuralFX.Protocol;

namespace NeuralFX.Tests;

public sealed class MonitoringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NeuralFX_Monitor_" + Guid.NewGuid().ToString("N"));
    public MonitoringTests() => Directory.CreateDirectory(_root);
    [Fact]
    public void ReleaseLookupIgnoresOtherProductsAndDrafts()
    {
        using var json = JsonDocument.Parse("""
            [{"tag_name":"other-addon-5","draft":false},{"tag_name":"renodx-dlss5-5","draft":true},
             {"tag_name":"renodx-dlss5-4.70","draft":false},{"tag_name":"renodx-dlss5-3.3.4","draft":false}]
            """);
        Assert.Equal("renodx-dlss5-4.70", ReleaseUpdateService.LatestTag(json.RootElement, "renodx-dlss5-"));
        Assert.Null(ReleaseUpdateService.LatestTag(json.RootElement, "absent-"));
    }
    [Fact]
    public void TransactionRejectsSlashAliasesAndReservedTargets()
    {
        using var transaction = new FileTransaction(_root);
        transaction.Write("folder/file.dll", new byte[] { 1 });
        Assert.Throws<InvalidDataException>(() => transaction.Write("folder\\file.dll", new byte[] { 2 }));
        Assert.Throws<InvalidDataException>(() => transaction.Write("folder/../.neuralfx-transaction/journal.json", new byte[] { 2 }));
    }
    [Theory]
    [InlineData("nested/../Cities.exe")]
    [InlineData("Cities.exe.")]
    [InlineData("Cities.exe ")]
    [InlineData(".neuralfx-backups/../Cities.exe")]
    public void AmbiguousManifestPathsAreRejected(string path) => Assert.Throws<InvalidDataException>(() => ManagedPaths.Resolve(_root, path));
    [Fact]
    public void LogHandlesSplitUtf8TruncationAndReplacement()
    {
        string path = Path.Combine(_root, "log.txt");
        var reader = new IncrementalLogReader();
        byte[] bytes = Encoding.UTF8.GetBytes("A€B");
        File.WriteAllBytes(path, bytes[..2]);
        Assert.Equal("A", reader.Read(path));
        using (var file = new FileStream(path, FileMode.Append)) file.Write(bytes[2..]);
        Assert.Equal("A€B", reader.Read(path));
        File.WriteAllText(path, "C"); Assert.Equal("C", reader.Read(path));
        File.Move(path, path + ".old"); File.WriteAllText(path, "replacement");
        Assert.Equal("replacement", reader.Read(path));
        File.WriteAllText(path, "rewritten longer than replacement");
        Assert.Equal("rewritten longer than replacement", reader.Read(path));
    }
    [Fact]
    public void LargeLogRetainsBoundedTail()
    {
        string path = Path.Combine(_root, "log.txt");
        File.WriteAllText(path, new string('x', 15 * 1024 * 1024) + "END");
        var reader = new IncrementalLogReader();
        string text = reader.Read(path);
        Assert.EndsWith("END", text); Assert.True(text.Length <= IncrementalLogReader.Limit);
        Assert.Equal(text, reader.Read(path));
    }
    [Fact]
    public void SteamDiscoveryHonorsAdditionalLibrariesAndAppManifest()
    {
        string secondary = Path.Combine(_root, "Library with spaces");
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        string game = Path.Combine(secondary, "steamapps", "common", "CustomCities"); Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(game, "Cities.exe"), "fixture");
        File.WriteAllText(Path.Combine(secondary, "steamapps", "appmanifest_255710.acf"), "\"AppState\" { \"appid\" \"255710\" \"installdir\" \"CustomCities\" }");
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + secondary.Replace("\\", "\\\\") + "\" \"apps\" { \"255710\" \"1234\" } } }");
        Assert.Equal(Path.Combine(game, "Cities.exe"), SteamLibraryLocator.FindInLibraries(SteamLibraryLocator.Libraries(_root)));
    }
    [Fact]
    public async Task SeqlockKeepsCorrelatedFieldsCoherentUnderConcurrentReads()
    {
        int pid = Random.Shared.Next(100000, int.MaxValue);
        using var writer = TelemetryChannel.Open(pid, true)!;
        using var reader = TelemetryChannel.Open(pid, false)!;
        Assert.NotNull(writer); Assert.NotNull(reader);
        int reads = 0;
        var publish = Task.Run(() => { for (int i = 1; i <= 50000; i++) writer.Publish(new TelemetryFrame { ProcessId = pid, FrameIndex = i, RenderWidth = i, RenderHeight = -i, UtcTicks = DateTime.UtcNow.Ticks }); });
        while (!publish.IsCompleted)
        {
            if (!reader.TryRead(out var frame)) continue;
            Assert.Equal(frame.FrameIndex, frame.RenderWidth); Assert.Equal(-frame.FrameIndex, frame.RenderHeight); reads++;
        }
        await publish;
        Assert.True(reader.TryRead(out var final)); Assert.Equal(50000, final.FrameIndex);
        Assert.True(reads > 0);
        reader.Send(new TelemetryCommand { Revision = 7, Kind = CommandKind.ResetHistory, SessionId = final.SessionId });
        Assert.True(writer.TryReadCommand(out var command)); Assert.Equal(7, command.Revision); Assert.Equal(CommandKind.ResetHistory, command.Kind);
        Assert.True(Marshal.SizeOf<TelemetryFrame>() < 504);
    }
    [Fact]
    public void ReloadedOwnerInvalidatesOldFramesAndCommands()
    {
        int pid = Random.Shared.Next(100000, int.MaxValue);
        using var reader = TelemetryChannel.Open(pid, true)!;
        using var client = TelemetryChannel.Open(pid, false)!;
        reader.Publish(new TelemetryFrame { UtcTicks = DateTime.UtcNow.Ticks });
        Assert.True(client.TryRead(out var before));
        client.Send(new TelemetryCommand { Kind = CommandKind.TogglePanel, Revision = 4, SessionId = before.SessionId });
        Assert.True(reader.TryReadCommand(out _));
        reader.Dispose();
        Assert.True(client.TryRead(out var offline)); Assert.Equal(0, offline.UtcTicks);
        using var next = TelemetryChannel.Open(pid, true)!;
        Assert.False(next.TryReadCommand(out _));
        Assert.True(client.TryRead(out var after)); Assert.NotEqual(before.SessionId, after.SessionId);
        client.Send(new TelemetryCommand { Kind = CommandKind.ResetHistory, Revision = 5, SessionId = after.SessionId });
        Assert.True(next.TryReadCommand(out var command)); Assert.Equal(5, command.Revision);
    }
    [Fact]
    public void LoadedModulesDoNotImplyInferenceAndStaleTelemetryIsRejected()
    {
        var now = DateTime.UtcNow;
        var frame = new TelemetryFrame { UtcTicks = now.AddSeconds(-3).Ticks, Flags = RuntimeFlags.FeederLoaded | RuntimeFlags.ReShadeLoaded };
        Assert.False(TelemetryStatus.IsFresh(frame, now));
        Assert.DoesNotContain("NR confirmado", TelemetryStatus.Describe(frame));
        frame.UtcTicks = now.AddMilliseconds(-100).Ticks; Assert.True(TelemetryStatus.IsFresh(frame, now));
        frame.UtcTicks = now.AddSeconds(1).Ticks; Assert.False(TelemetryStatus.IsFresh(frame, now));
    }
    [Fact]
    public void RecoveryReplaysAnInterruptedJournalIdempotently()
    {
        // Represents a terminated writer: no Dispose/finally is called before recovery.
        string folder = Path.Combine(_root, ".neuralfx-transaction"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "0"), "original"); File.WriteAllText(Path.Combine(_root, "owned.dll"), "partial new");
        File.WriteAllText(Path.Combine(_root, "new.dll"), "partial new");
        var journal = new FileTransaction.Journal { Files = new() { new() { Path = "owned.dll", Snapshot = "0", Existed = true }, new() { Path = "new.dll", Snapshot = "1", Existed = false } } };
        File.WriteAllText(Path.Combine(folder, "journal.json"), JsonSerializer.Serialize(journal));
        FileTransaction.Recover(_root); FileTransaction.Recover(_root);
        Assert.Equal("original", File.ReadAllText(Path.Combine(_root, "owned.dll"))); Assert.False(File.Exists(Path.Combine(_root, "new.dll")));
        Assert.False(Directory.Exists(folder));
    }
    [Fact]
    public void LeaseRejectsConcurrentWriterOnAnotherThread()
    {
        using var lease = new InstallationLease(_root);
        // Keep the acquiring thread synchronous while the other thread attempts the mutex.
        Exception? failure = null;
        var thread = new Thread(() => { try { using var other = new InstallationLease(_root); } catch (Exception ex) { failure = ex; } });
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.IsType<IOException>(failure);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
