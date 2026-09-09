using System;
using NeuralFX.Protocol;
namespace NeuralFX.Hub.Services;
internal static class TelemetryStatus
{
    public static bool IsFresh(TelemetryFrame frame, DateTime now) => frame.UtcTicks <= now.Ticks && now.Ticks - frame.UtcTicks < TimeSpan.FromSeconds(2).Ticks;
    public static string Describe(TelemetryFrame frame) =>
        $"{frame.Fps:F1} FPS · intervalo del juego {frame.FrameMs:F2} ms\n" + SessionViewState.Summary(frame) + "\n\n" + SessionViewState.Details(frame) +
        $"\nAdaptador de render (LUID): {frame.AdapterHigh:X8}:{frame.AdapterLow:X8}\nComando #{frame.LastCommand}: {SessionViewState.CommandStatus(frame)}";
}
