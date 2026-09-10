using System;
using System.IO;
using System.Text;
using NeuralFX.Protocol;

namespace NeuralFX.Config
{
    // Telemetría continua a disco: una línea de estado cada pocos segundos, para poder revisar
    // después qué hacía el puente sin tener que estar mirando el panel. Se apaga desde las
    // opciones del mod; mientras se desarrolla conviene dejarla encendida.
    //
    // Nunca puede tumbar la partida: cualquier fallo de escritura la desactiva en el acto.
    internal static class SessionLog
    {
        private const int MaxFailures = 3;
        private const float IntervalSeconds = 5f;
        private static string _path;
        private static float _next;
        private static int _failures;
        private static string _lastSummary;

        public static string Path { get { return _path; } }

        public static void Reset()
        {
            _path = null; _next = 0; _failures = 0; _lastSummary = null;
        }

        /// <summary>Escribe si toca. Devuelve true si acaba de escribir una entrada.</summary>
        public static bool Tick(float now, TelemetryFrame frame, float fps, float frameMs, string controlMessage, string conflicts)
        {
            if (!ModSettings.EnableSessionLog || _failures >= MaxFailures) return false;
            string summary = SessionViewState.Summary(frame);
            // Un cambio de estado se registra en cuanto ocurre; lo demás, a intervalos.
            if (now < _next && summary == _lastSummary) return false;
            _next = now + IntervalSeconds;
            _lastSummary = summary;
            try
            {
                if (_path == null)
                {
                    string root = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuralFX/Diagnostics");
                    Directory.CreateDirectory(root);
                    _path = System.IO.Path.Combine(root, "sesion-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                    File.AppendAllText(_path, "NeuralFX · telemetría de sesión · " + DateTime.Now + "\n" +
                        "puente build " + frame.BridgeBuild + " / ABI 3 / IPC 4\n\n", Encoding.UTF8);
                }
                var line = new StringBuilder();
                line.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").Append(summary).Append('\n');
                line.Append("  ").Append(fps.ToString("F1")).Append(" fps · ").Append(frameMs.ToString("F1")).Append(" ms · ")
                    .Append("escena ").Append(frame.RenderWidth).Append('x').Append(frame.RenderHeight)
                    .Append(" · presentado ").Append(frame.DisplayWidth).Append('x').Append(frame.DisplayHeight)
                    .Append(" · trabajo ").Append(frame.WorkWidth).Append('x').Append(frame.WorkHeight).Append('\n');
                line.Append("  movimiento=").Append(frame.MotionProvider)
                    .Append(" flags=").Append((int)frame.Flags)
                    .Append(" grabado/enviado/completado/incorporado=").Append(frame.RecordedFrame).Append('/').Append(frame.SubmittedFrame)
                    .Append('/').Append(frame.CompletedFrame).Append('/').Append(frame.OutputFrame)
                    .Append(" reset=").Append(frame.ResetCount).Append('/').Append(frame.CameraCuts)
                    .Append(" evaluaciones=").Append(frame.Evaluations)
                    .Append(" backend=0x").Append(frame.BackendError.ToString("X8")).Append('\n');
                if (!string.IsNullOrEmpty(controlMessage)) line.Append("  ajuste: ").Append(controlMessage).Append('\n');
                if (!string.IsNullOrEmpty(conflicts)) line.Append("  aviso: ").Append(conflicts).Append('\n');
                File.AppendAllText(_path, line.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                if (++_failures >= MaxFailures)
                {
                    _path = null;
                    UnityEngine.Debug.LogWarning("[NeuralFX] telemetría de sesión desactivada tras " + MaxFailures + " fallos: " + ex.Message);
                }
                return false;
            }
        }
    }
}
