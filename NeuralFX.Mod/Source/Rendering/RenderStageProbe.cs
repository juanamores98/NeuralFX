using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
namespace NeuralFX.Rendering
{
    // Evidencia acotada sobre la ruta real de render de CS1: qué cámaras dibujan, en qué orden,
    // a qué destino y con qué geometría. Sirvió para elegir el enganche pre-UI; ahora sirve
    // además para lo que sigue abierto — la cámara mide 3840x1933 sobre un backbuffer de
    // 3840x2160, de forma constante en ciudad, y falta saber si eso es su rect, su textura de
    // destino, o que Camera.main no sea la cámara que produce la imagen final.
    //
    // Se anota cada configuración distinta una sola vez. Repetir la misma línea mil veces no
    // añade nada y llenaría el registro; lo que importa es cuándo aparece una nueva.
    internal static class RenderStageProbe
    {
        private const int Burst = 120;       // los primeros fotogramas, uno a uno
        private const int Interval = 30;     // después, dos muestras por segundo
        private const int MaxDistinct = 512;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Queue<string> Events = new Queue<string>();
        private static readonly Queue<string> Pending = new Queue<string>();
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static int _lastFrame = -1, _frames;
        public static void Start() { Camera.onPreRender -= Before; Camera.onPostRender -= After; Camera.onPreRender += Before; Camera.onPostRender += After; }
        public static void Stop() { Camera.onPreRender -= Before; Camera.onPostRender -= After; }
        private static void Before(Camera camera) { Record(camera, "pre "); }
        private static void After(Camera camera) { Record(camera, "post"); }
        private static void Record(Camera camera, string stage)
        {
            if (!Config.ModSettings.EnableRenderTrace || camera == null) return;
            if (_lastFrame != Time.frameCount) { _lastFrame = Time.frameCount; ++_frames; }
            // La ráfaga inicial coge el arranque de la ciudad entero; después basta con mirar de
            // vez en cuando, porque solo se guarda lo que no se había visto ya.
            if (_frames > Burst && _frames % Interval != 0) return;
            if (Seen.Count >= MaxDistinct) return;
            string signature = Describe(camera, stage);
            if (!Seen.Add(signature)) return;
            string line = "f" + _frames.ToString(Inv) + " " + signature;
            Events.Enqueue(line); Pending.Enqueue(line);
            if (Events.Count > MaxDistinct) Events.Dequeue();
        }
        private static string Describe(Camera camera, string stage)
        {
            Rect rect = camera.rect;
            RenderTexture target = camera.targetTexture;
            var text = new StringBuilder();
            text.Append(stage).Append(' ').Append(camera.name)
                .Append(" id=").Append(camera.GetInstanceID().ToString(Inv))
                .Append(" orden=").Append(N(camera.depth))
                .Append(' ').Append(camera.pixelWidth.ToString(Inv)).Append('x').Append(camera.pixelHeight.ToString(Inv))
                .Append(" rect=").Append(N(rect.x)).Append(',').Append(N(rect.y))
                .Append(' ').Append(N(rect.width)).Append('x').Append(N(rect.height))
                .Append(" destino=").Append(target == null
                    ? "pantalla " + Screen.width.ToString(Inv) + "x" + Screen.height.ToString(Inv)
                    : (string.IsNullOrEmpty(target.name) ? "rt" : target.name) + " " + target.width.ToString(Inv) + "x" + target.height.ToString(Inv))
                .Append(" ruta=").Append(camera.actualRenderingPath);
            if (camera == Camera.main) text.Append(" [Camera.main]");
            return text.ToString();
        }
        private static string N(float value) { return value.ToString("0.####", Inv); }
        /// <summary>Lo anotado desde la última llamada. Lo consume el registro de sesión.</summary>
        public static string Drain()
        {
            if (Pending.Count == 0) return null;
            var text = new StringBuilder();
            while (Pending.Count > 0) text.Append("    ").Append(Pending.Dequeue()).Append('\n');
            return text.ToString();
        }
        public static string Export() { return string.Join("\n", Events.ToArray()); }
        public static void Restart() { Events.Clear(); Pending.Clear(); Seen.Clear(); _frames = 0; _lastFrame = -1; }
    }
}
