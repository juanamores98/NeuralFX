using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeuralFX.Rendering
{
    internal static class AaConflictScanner
    {
        private static readonly string[] Markers = { "Antialiasing", "SMAA", "TAA", "Temporal", "FXAA" };
        public static string[] Scan(Camera camera)
        {
            var conflicts = new List<string>();
            if (camera == null) return conflicts.ToArray();
            foreach (MonoBehaviour component in camera.GetComponents<MonoBehaviour>())
            {
                if (component == null || !component.enabled || component is TemporalCamera) continue;
                string name = component.GetType().Name;
                foreach (string marker in Markers)
                    if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) { conflicts.Add(name); break; }
            }
            return conflicts.ToArray();
        }
    }
}
