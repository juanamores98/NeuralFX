using System;
namespace NeuralFX.Protocol
{
    // Shared wording for Hub and in-game UI. None of these stages implies NR by itself.
    public static class SessionViewState
    {
        public static bool Has(TelemetryFrame frame, RuntimeFlags flag) { return (frame.Flags & flag) != 0; }
        public static string Summary(TelemetryFrame frame)
        {
            if (!Has(frame, RuntimeFlags.CameraPresent)) return "Preparado; esperando ciudad";
            if (!Has(frame, RuntimeFlags.PipelineRequested)) return "Desactivado por el usuario";
            if (!Has(frame, RuntimeFlags.NativeConnected)) return "Bypass: puente ausente o incompatible";
            if (frame.BackendError != 0) return "Bypass por error del backend: 0x" + frame.BackendError.ToString("X8");
            if (Has(frame, RuntimeFlags.NrConfirmed)) return Has(frame, RuntimeFlags.UiIsolated) ? "NR confirmado; UI protegida" : "NR confirmado; UI no aislada";
            return Has(frame, RuntimeFlags.EvaluationSucceeded) ? "Portador NGX operativo; NR sin confirmar" : "Esperando evaluación del portador NGX";
        }
        public static string Details(TelemetryFrame frame)
        {
            string motion = frame.MotionProvider == 2 ? "Unity experimental (cobertura sin validar)" : frame.MotionProvider == 1 ? "Óptico" : "Sin entrada confirmada";
            return "Movimiento: " + motion + "\nUI protegida: " + (Has(frame, RuntimeFlags.UiIsolated) ? "sí" : "sin verificar") +
                "\nEscena / trabajo NR / salida: " + frame.RenderWidth + "x" + frame.RenderHeight + " / " + frame.WorkWidth + "x" + frame.WorkHeight + " / " + frame.DisplayWidth + "x" + frame.DisplayHeight +
                "\nFrame grabado / enviado / completado / incorporado: " + frame.RecordedFrame + " / " + frame.SubmittedFrame + " / " + frame.CompletedFrame + " / " + frame.OutputFrame +
                "\nReset solicitado / completado: " + frame.CameraCuts + " / " + frame.ResetCount +
                "\nTiempo GPU NR: no medido. SR interno: no disponible.";
        }
    }
}
