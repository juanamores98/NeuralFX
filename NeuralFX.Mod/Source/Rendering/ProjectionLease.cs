using UnityEngine;
namespace NeuralFX.Rendering
{
    // Restores exact custom matrices only while our last write still owns them.
    internal sealed class ProjectionLease
    {
        private Camera _camera;
        private Matrix4x4 _projection, _nonJittered, _written;
        public bool Conflict { get; private set; }
        public void Apply(Camera camera, Matrix4x4 jittered)
        {
            Restore(); _camera = camera; _projection = camera.projectionMatrix;
            _nonJittered = camera.nonJitteredProjectionMatrix; _written = jittered;
            camera.nonJitteredProjectionMatrix = _projection; camera.projectionMatrix = jittered;
        }
        public void Restore()
        {
            if (_camera == null) return;
            if (Equal(_camera.projectionMatrix, _written)) _camera.projectionMatrix = _projection; else Conflict = true;
            if (Equal(_camera.nonJitteredProjectionMatrix, _projection)) _camera.nonJitteredProjectionMatrix = _nonJittered; else Conflict = true;
            _camera = null;
        }
        private static bool Equal(Matrix4x4 a, Matrix4x4 b)
        { for (int i = 0; i < 16; ++i) if (a[i] != b[i]) return false; return true; }
    }
}
