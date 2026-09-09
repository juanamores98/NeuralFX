using UnityEngine;
namespace NeuralFX.Rendering
{
    // Release only our last write. A later writer keeps its complete value.
    internal sealed class CameraModeLease
    {
        private Camera _camera;
        private DepthTextureMode _before, _written;
        public bool Conflict { get; private set; }
        public void Acquire(Camera camera)
        {
            if (_camera == camera) return;
            Release(); if (camera == null) return;
            _camera = camera; _before = camera.depthTextureMode;
            _written = _before | DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            camera.depthTextureMode = _written; Conflict = false;
        }
        public void Release()
        {
            if (_camera == null) return;
            if (_camera.depthTextureMode == _written) _camera.depthTextureMode = _before;
            else Conflict = true;
            _camera = null;
        }
    }
}
