using System;
using UnityEngine;
using UnityEngine.Rendering;
using NeuralFX.Config;

namespace NeuralFX.Rendering
{
    internal sealed class TemporalCamera : MonoBehaviour
    {
        public NativeBridge Bridge;
        public int ResetSerial { get; set; }

        private static readonly Vector2[] Halton16 = new Vector2[16]
        {
            new Vector2(0.000000f, -0.166667f),
            new Vector2(-0.250000f, 0.166667f),
            new Vector2(0.250000f, -0.388889f),
            new Vector2(-0.375000f, -0.055556f),
            new Vector2(0.125000f, 0.277778f),
            new Vector2(-0.125000f, -0.277778f),
            new Vector2(0.375000f, 0.055556f),
            new Vector2(-0.437500f, 0.388889f),
            new Vector2(0.062500f, -0.462963f),
            new Vector2(-0.187500f, -0.129630f),
            new Vector2(0.312500f, 0.203704f),
            new Vector2(-0.312500f, -0.351852f),
            new Vector2(0.187500f, -0.018519f),
            new Vector2(-0.062500f, 0.314815f),
            new Vector2(0.437500f, -0.240741f),
            new Vector2(-0.468750f, 0.092593f)
        };

        private Camera _camera;
        private CommandBuffer _event;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private float _previousFov, _previousScale;
        private int _width, _height;
        private bool _hasPrevious;
        private Matrix4x4 _originalProjection;
        private bool _jitterApplied;

        public void Awake() { _camera = GetComponent<Camera>(); }
        public void RequestReset() { unchecked { ResetSerial++; } }

        public void OnPreCull()
        {
            if (_camera == null) return;
            if (ModSettings.ForceMotionVectorsOnLoad)
            {
                const DepthTextureMode desired = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                if ((_camera.depthTextureMode & desired) != desired) _camera.depthTextureMode |= desired;
            }

            Vector3 position = _camera.transform.position;
            Quaternion rotation = _camera.transform.rotation;
            bool cut = !_hasPrevious || Vector3.Distance(position, _previousPosition) > Mathf.Max(40f, Mathf.Abs(position.y) * 0.8f) ||
                Quaternion.Angle(rotation, _previousRotation) > 35f || Mathf.Abs(_camera.fieldOfView - _previousFov) > 10f ||
                Time.timeScale != _previousScale || _camera.pixelWidth != _width || _camera.pixelHeight != _height;
            if (cut) RequestReset();

            _previousPosition = position; _previousRotation = rotation; _previousFov = _camera.fieldOfView; _previousScale = Time.timeScale;
            _width = _camera.pixelWidth; _height = _camera.pixelHeight; _hasPrevious = true;

            // Conservar la matriz de proyección original limpia para vectores de movimiento y UI
            _originalProjection = _camera.projectionMatrix;
            _camera.nonJitteredProjectionMatrix = _originalProjection;

            // Optimización de nitidez de texturas y LOD geométrico si está habilitado
            if (ModSettings.EnhanceTextureClarity)
            {
                if (QualitySettings.anisotropicFiltering != AnisotropicFiltering.ForceEnable)
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                if (QualitySettings.lodBias < 2.0f)
                    QualitySettings.lodBias = 2.0f;
            }

            float jx = 0f, jy = 0f;
            if (!cut && ModSettings.EnableCameraJitter && _width > 0 && _height > 0)
            {
                int phaseIndex = Time.frameCount & 15;
                Vector2 phase = Halton16[phaseIndex];
                jx = phase.x;
                jy = phase.y;

                // Modificar el sesgo de la matriz de proyección en espacio NDC (-1 a 1)
                Matrix4x4 jittered = _originalProjection;
                jittered.m02 += 2.0f * jx / _width;
                jittered.m12 += 2.0f * jy / _height;
                _camera.projectionMatrix = jittered;
                _jitterApplied = true;
            }

            // Obtener puntero nativo D3D11 a los vectores de movimiento de Unity si están disponibles
            ulong mvPtr = 0;
            if (ModSettings.EnableNativeMotionVectors)
            {
                try
                {
                    Texture mvTex = Shader.GetGlobalTexture("_CameraMotionVectorsTexture");
                    if (mvTex != null)
                    {
                        IntPtr p = mvTex.GetNativeTexturePtr();
                        mvPtr = (ulong)p.ToInt64();
                    }
                }
                catch { }
            }

            if (Bridge == null || !Bridge.Connected) return;
            if (_event == null) { _event = new CommandBuffer { name = "NeuralFX frame metadata" }; _camera.AddCommandBuffer(CameraEvent.AfterEverything, _event); }
            _event.Clear();
            var frame = new NativeFrame
            {
                Size = 48,
                Version = 2,
                Frame = Time.frameCount + 1,
                ResetSerial = ResetSerial,
                Width = _width,
                Height = _height,
                JitterX = jx,
                JitterY = jy,
                MotionVectorsPtr = mvPtr,
                MvScaleX = _width,
                MvScaleY = _height
            };
            if (Bridge.Submit(ref frame)) _event.IssuePluginEvent(Bridge.RenderEvent, frame.Frame);
        }

        public void OnPostRender()
        {
            if (_jitterApplied && _camera != null)
            {
                _camera.ResetProjectionMatrix();
                _camera.projectionMatrix = _originalProjection;
                _jitterApplied = false;
            }
        }

        public void OnDisable()
        {
            if (_jitterApplied && _camera != null)
            {
                _camera.ResetProjectionMatrix();
                _jitterApplied = false;
            }
            if (_event != null) { if (_camera != null) _camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _event); _event.Release(); _event = null; }
            _hasPrevious = false;
        }
    }
}
