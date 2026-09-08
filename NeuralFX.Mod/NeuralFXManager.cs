using UnityEngine;
using NeuralFX.Config;
using NeuralFX.Protocol;
using NeuralFX.Rendering;

namespace NeuralFX
{
    public class NeuralFXManager : MonoBehaviour
    {
        private static NeuralFXManager _instance;
        public static NeuralFXManager Instance { get { return _instance; } }
        private Camera _camera;
        private TemporalCamera _temporal;
        private readonly NativeBridge _bridge = new NativeBridge();
        private readonly UI.TelemetryPanel _panel = new UI.TelemetryPanel();
        private TelemetryChannel _channel;
        private float _elapsed, _publishAt, _discoverAt, _hooksAt, _scanAt;
        private int _frames, _lastCommand;
        private static int _resetSerial;
        private float _fps, _frameMs;
        private RuntimeFlags _moduleFlags;
        private string[] _conflicts = new string[0];
        private int _pid;
        public static void ToggleWindow() { if (_instance != null) _instance._panel.Toggle(); }
        public void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            ModSettings.Load();
            _pid = NativeInterop.GetCurrentProcessId();
            _channel = TelemetryChannel.Open(_pid, true);
        }
        public void Update()
        {
            float now = Time.unscaledTime;
            _elapsed += Time.unscaledDeltaTime; _frames++;
            if (_elapsed >= 0.5f) { _fps = _frames / _elapsed; _frameMs = _elapsed * 1000f / _frames; _elapsed = 0; _frames = 0; }
            if (now >= _discoverAt)
            {
                _discoverAt = now + 1;
                Camera current = Camera.main;
                if (_camera != current || (_camera != null && _temporal == null))
                {
                    if (_temporal != null) { _resetSerial = _temporal.ResetSerial; Destroy(_temporal); _temporal = null; }
                    _camera = current;
                    if (_camera != null) { _temporal = _camera.gameObject.AddComponent<TemporalCamera>(); _temporal.Bridge = _bridge; _temporal.ResetSerial = ++_resetSerial; }
                }
            }
            if (ModSettings.ForceMotionVectorsOnLoad && _camera != null) EnsureCameraModes();
            if (now >= _hooksAt)
            {
                _hooksAt = now + 3;
                _moduleFlags = RuntimeFlags.None;
                if (NativeInterop.IsReShadeHooked()) _moduleFlags |= RuntimeFlags.ReShadeLoaded;
                if (NativeInterop.IsFeederAddonLoaded()) _moduleFlags |= RuntimeFlags.FeederLoaded;
                if (NativeInterop.IsModuleLoaded("renodx-dlss5.addon64")) _moduleFlags |= RuntimeFlags.ConsumerLoaded;
                if (!_bridge.Connected) _bridge.Connect();
            }
            if (now >= _scanAt) { _scanAt = now + 5; _conflicts = ModSettings.WarnOnAaConflict ? AaConflictScanner.Scan(_camera) : new string[0]; }
            if (ModSettings.EnableHotkey && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.N)) ToggleWindow();
            TelemetryCommand command;
            if (_channel != null && _channel.TryReadCommand(out command) && command.Revision != _lastCommand)
            {
                if (command.Kind == CommandKind.TogglePanel) ToggleWindow();
                else if (command.Kind == CommandKind.ReapplyBuffers) EnsureCameraModes();
                else if (command.Kind == CommandKind.ResetHistory) RequestHistoryReset();
                _lastCommand = command.Revision;
            }
            if (now < _publishAt) return;
            _publishAt = now + 0.1f;
            var status = _bridge.ReadStatus();
            RuntimeFlags flags = _moduleFlags;
            if (_camera != null)
            {
                flags |= RuntimeFlags.CameraPresent;
                if ((_camera.depthTextureMode & DepthTextureMode.Depth) != 0) flags |= RuntimeFlags.DepthRequested;
                if ((_camera.depthTextureMode & DepthTextureMode.MotionVectors) != 0) flags |= RuntimeFlags.MotionRequested;
            }
            if (_bridge.Connected) flags |= RuntimeFlags.NativeConnected;
            bool recent = status.Frame > 0 && status.AgeMs < 2000;
            if (recent && status.Result == 1) flags |= RuntimeFlags.EvaluationSucceeded;
            if (_temporal != null) _resetSerial = _temporal.ResetSerial;
            if (_channel != null) _channel.Publish(new TelemetryFrame {
                ProcessId = _pid, UtcTicks = System.DateTime.UtcNow.Ticks, Fps = _fps, FrameMs = _frameMs,
                DisplayWidth = Screen.width, DisplayHeight = Screen.height, RenderWidth = _camera != null ? _camera.pixelWidth : 0,
                RenderHeight = _camera != null ? _camera.pixelHeight : 0, Flags = flags, CameraCuts = _resetSerial,
                FrameIndex = Time.frameCount, LastCommand = _lastCommand, NativeStatus = recent ? status.Result : 0, ResetCount = status.ResetSerial,
                WorkWidth = status.Width, WorkHeight = status.Height, Evaluations = status.Evaluations
            });
            if (_panel.Visible) _panel.Update(string.Format("{0:F1} FPS · {1:F2} ms · {2} × {3}", _fps, _frameMs, Screen.width, Screen.height),
                (flags & RuntimeFlags.EvaluationSucceeded) != 0 ? "Evaluación NGX confirmada por el puente. La calidad del consumidor debe comprobarse visualmente." : "Módulos: " + _moduleFlags + "\nInferencia sin confirmar.",
                "Buffers solicitados: " + ((_camera != null) ? _camera.depthTextureMode.ToString() : "sin cámara") + "\nResets solicitados/confirmados: " + _resetSerial + "/" + status.ResetSerial,
                _conflicts.Length == 0 ? "Sin AA adicional detectado por el escáner." : "Revisar AA: " + string.Join(", ", _conflicts));
        }
        public void EnsureCameraModes()
        {
            Camera camera = _camera != null ? _camera : Camera.main;
            if (camera == null) return;
            const DepthTextureMode desired = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            if ((camera.depthTextureMode & desired) != desired) camera.depthTextureMode |= desired;
        }
        public void RequestHistoryReset()
        {
            if (_temporal != null) _temporal.RequestReset();
            if (!_bridge.Connected) Debug.Log("[NeuralFX] Reset pendiente: se requiere el feeder con puente NeuralFX.");
        }
        public void OnDestroy()
        {
            if (_instance != this) return;
            _panel.Destroy();
            if (_temporal != null) Destroy(_temporal);
            if (_channel != null) { _channel.Dispose(); _channel = null; }
            _instance = null;
        }
    }
}
