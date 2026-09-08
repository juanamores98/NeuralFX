using System;
using NeuralFX.Protocol;

namespace NeuralFX.Hub.Services;

internal static class TelemetryStatus
{
    public static bool IsFresh(TelemetryFrame frame, DateTime now) => frame.UtcTicks <= now.Ticks && now.Ticks - frame.UtcTicks < TimeSpan.FromSeconds(2).Ticks;
    public static string Describe(TelemetryFrame frame)
    {
        bool Flag(RuntimeFlags flag) => (frame.Flags & flag) != 0;
        string inference = Flag(RuntimeFlags.EvaluationSucceeded) ? "Evaluación NGX confirmada por el puente" : "Evaluación NGX sin confirmar";
        return $"{frame.Fps:F1} FPS    {frame.FrameMs:F2} ms\nSalida: {frame.DisplayWidth} × {frame.DisplayHeight}    Cámara: {frame.RenderWidth} × {frame.RenderHeight}\n\n" +
            $"ReShade: {(Flag(RuntimeFlags.ReShadeLoaded) ? "cargado" : "sin detectar")}    Feeder: {(Flag(RuntimeFlags.FeederLoaded) ? "cargado" : "sin detectar")}\n" +
            $"Profundidad solicitada: {Flag(RuntimeFlags.DepthRequested)}    MV solicitados: {Flag(RuntimeFlags.MotionRequested)}\n" +
            $"{inference} · {frame.Evaluations} evaluaciones\nTrabajo del feeder: {frame.WorkWidth} × {frame.WorkHeight}\nResets solicitados / confirmados: {frame.CameraCuts} / {frame.ResetCount}\nComando confirmado: #{frame.LastCommand}\n\n" +
            "La ruta actual utiliza flujo óptico. Solicitar MV a Unity no los conecta con NGX.\nLa calidad neural y el tratamiento del HUD requieren inspección visual.";
    }
}
