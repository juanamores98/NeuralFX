using System.Text;
using NeuralFX.Hub.Services;
using NeuralFX.Protocol;

if (args[0] == "transaction")
{
    using var lease = new InstallationLease(args[1]);
    using var transaction = new FileTransaction(args[1]);
    transaction.Write("owned.dll", Encoding.UTF8.GetBytes("changed"));
    transaction.Write("new.dll", Encoding.UTF8.GetBytes("created"));
    transaction.Commit(i => { if (i == 1) { Console.WriteLine("ready"); Thread.Sleep(Timeout.Infinite); } });
}
else if (args[0] == "uninstall")
{
    var uninstaller = new UninstallService(args[2], () => false)
    {
        AfterWrite = i => { if (i == 0) { Console.WriteLine("ready"); Thread.Sleep(Timeout.Infinite); } }
    };
    await uninstaller.UninstallAsync(UninstallService.Inspect(args[1]));
}
else if (args[0] == "telemetry")
{
    using var channel = TelemetryChannel.Open(Environment.ProcessId, true) ?? throw new Exception("MMF creation failed");
    Console.WriteLine("ready");
    int revision = 0;
    for (int frame = 1; frame <= 200; frame++)
    {
        if (channel.TryReadCommand(out var command)) revision = command.Revision;
        channel.Publish(new TelemetryFrame { ProcessId = Environment.ProcessId, UtcTicks = DateTime.UtcNow.Ticks, FrameIndex = frame, RenderWidth = frame, RenderHeight = -frame, LastCommand = revision });
        Thread.Sleep(2);
    }
}
