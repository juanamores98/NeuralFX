using System.Collections.Generic;
using UnityEngine;
namespace NeuralFX.Rendering
{
    // Bounded evidence for selecting the pre-UI hook on the actual CS1 rendering path.
    internal static class RenderStageProbe
    {
        private static readonly Queue<string> Events = new Queue<string>();
        private static int _lastFrame = -1, _frames;
        public static void Start() { Camera.onPreRender -= Before; Camera.onPostRender -= After; Camera.onPreRender += Before; Camera.onPostRender += After; }
        public static void Stop() { Camera.onPreRender -= Before; Camera.onPostRender -= After; }
        private static void Before(Camera camera) { Record(camera, "pre"); }
        private static void After(Camera camera) { Record(camera, "post"); }
        private static void Record(Camera camera, string stage)
        {
            if (!Config.ModSettings.EnableRenderTrace || camera == null) return;
            if (_lastFrame != Time.frameCount) { _lastFrame = Time.frameCount; ++_frames; }
            if (_frames > 120) return;
            if (Events.Count >= 512) Events.Dequeue();
            Events.Enqueue(Time.frameCount + " " + stage + " camera=" + camera.GetInstanceID() + " name=" + camera.name + " order=" + camera.depth + " path=" + camera.actualRenderingPath + " target=" + (camera.targetTexture == null ? "screen" : camera.targetTexture.name) + " " + camera.pixelWidth + "x" + camera.pixelHeight);
        }
        public static string Export() { return string.Join("\n", Events.ToArray()); }
        public static void Restart() { Events.Clear(); _frames = 0; _lastFrame = -1; }
    }
}
