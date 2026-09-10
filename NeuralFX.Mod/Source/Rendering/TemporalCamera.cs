using UnityEngine;
using UnityEngine.Rendering;
using NeuralFX.Config;
namespace NeuralFX.Rendering
{
    internal sealed class TemporalCamera : MonoBehaviour
    {
        public NativeBridge Bridge;
        public int ResetSerial { get; set; }
        /// <summary>Por qué la ruta de vectores de Unity entró o no en este frame.</summary>
        public string MotionState { get; private set; }
        private Camera _camera;
        private CommandBuffer _event;
        private readonly FrameCoordinator _frames = new FrameCoordinator();
        private readonly CameraModeLease _modes = new CameraModeLease();
        public bool RestoreConflict { get { return _modes.Conflict || _projection.Conflict; } }
        public void EnsureCameraModes() { if (_camera != null) _modes.Acquire(_camera); }
        private readonly ProjectionLease _projection = new ProjectionLease();
        private readonly EngineInputProvider _inputs = new EngineInputProvider();
        private Vector3 _position; private Quaternion _rotation;
        private float _fov, _scale; private int _width, _height, _presentWidth, _presentHeight; private bool _previous;
        public uint Epoch { get { return _frames.Epoch; } }
        public void Awake() { _camera = GetComponent<Camera>(); }
        public void RequestReset() { unchecked { ResetSerial++; } }
        public void OnPreCull()
        {
            if (_event != null) _event.Clear();
            _projection.Restore();
            if (_camera == null || Bridge == null || !Bridge.Connected || !ModSettings.PipelineEnabled) { _modes.Release(); return; }
            if (!ModSettings.ExperimentalOptIn || (!ModSettings.EnableNativeMotionVectors && !ModSettings.ForceMotionVectorsOnLoad)) { _modes.Release(); _inputs.Release(); }
            // La cámara de CS1 no ocupa todo el backbuffer: su rect es (0, 0.105, 1, 0.895), de
            // modo que mide 3840x1933 sobre una pantalla de 3840x2160 y la franja de abajo queda
            // para la barra de herramientas. El frame anunciado describe lo que ReShade verá en
            // Present, así que va con el tamaño de pantalla.
            int width = _camera.pixelWidth, height = _camera.pixelHeight;
            int presentWidth = Screen.width, presentHeight = Screen.height;
            if (width <= 0 || height <= 0 || width > Bridge.Capabilities.MaxDimension || height > Bridge.Capabilities.MaxDimension) return;
            if (presentWidth <= 0 || presentHeight <= 0 || presentWidth > Bridge.Capabilities.MaxDimension || presentHeight > Bridge.Capabilities.MaxDimension) return;
            bool resize = width != _width || height != _height || presentWidth != _presentWidth || presentHeight != _presentHeight;
            bool cut = !_previous || resize || Vector3.Distance(_camera.transform.position, _position) > Mathf.Max(40f, Mathf.Abs(_camera.transform.position.y) * .8f) ||
                Quaternion.Angle(_camera.transform.rotation, _rotation) > 35f || Mathf.Abs(_camera.fieldOfView - _fov) > 10f || _scale != Time.timeScale;
            if (cut) RequestReset();
            if (resize) _frames.Recreate();
            _position = _camera.transform.position; _rotation = _camera.transform.rotation; _fov = _camera.fieldOfView; _scale = Time.timeScale;
            _width = width; _height = height; _presentWidth = presentWidth; _presentHeight = presentHeight; _previous = true;
            if (_event == null) { _event = new CommandBuffer { name = "NeuralFX current-camera capture and token" }; _camera.AddCommandBuffer(CameraEvent.AfterEverything, _event); }
            uint cameraId = unchecked((uint)_camera.GetInstanceID());
            uint motion = 0;
            if (!ModSettings.ExperimentalOptIn || !ModSettings.EnableNativeMotionVectors) MotionState = "no solicitado";
            else if ((Bridge.Capabilities.Supported & 16) == 0) MotionState = "sin capacidad del puente";
            else
            {
                EnsureCameraModes();
                // Los vectores se reservan al tamaño del frame anunciado y se copian al hueco
                // que la cámara ocupa dentro de él, que es lo que dice pixelRect. Un blit
                // directo los estiraría un 11,7% en vertical y cada píxel leería el movimiento
                // de otra fila.
                Rect view = _camera.pixelRect;
                int x = Mathf.RoundToInt(view.x), y = Mathf.RoundToInt(view.y);
                if (!_inputs.Prepare(Bridge, cameraId, _frames.Epoch, presentWidth, presentHeight)) MotionState = "reserva rechazada: " + _inputs.Failure;
                else
                {
                    motion = _inputs.Record(_event, width, height, x, y);
                    MotionState = motion != 0 ? "enviado " + width + "x" + height + " en " + x + "," + y : "sin turno libre";
                }
            }
            var frame = new NativeFrame {
                Size = 64, Version = 3, Magic = 0x4e465833, Frame = _frames.NextFrame(), ResetSerial = unchecked((uint)ResetSerial),
                Camera = cameraId, Epoch = _frames.Epoch, Width = (uint)presentWidth, Height = (uint)presentHeight,
                MotionHandle = motion, MvScaleX = motion != 0 ? width : 1, MvScaleY = motion != 0 ? height : 1
            };
            // Jitter intentionally stays zero: feeder has no prepared-frame/fallback contract.
            if (Bridge.Submit(ref frame)) _event.IssuePluginEvent(Bridge.RenderEvent, unchecked((int)frame.Frame));
            else { _event.Clear(); Bridge.CancelMotion(motion); _inputs.Release(); }
        }
        public void OnPostRender() { _projection.Restore(); }
        public void OnDisable()
        {
            _projection.Restore();
            if (_event != null) { if (_camera != null) _camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _event); _event.Release(); _event = null; }
            _inputs.Release(); _modes.Release(); _previous = false;
        }
    }
}
