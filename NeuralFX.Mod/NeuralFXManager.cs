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
        private bool _quitRequested;
        private static int _resetSerial;
        private float _fps, _frameMs;
        private RuntimeFlags _moduleFlags;
        private string[] _conflicts = new string[0];
        private int _pid;
        private CommandResult _commandResult;
        private int _commandReason;
        private uint _commandReset, _controlRevision;
        private bool _commandIsControl;
        public string ControlMessage { get; private set; }
        public TelemetryFrame CurrentFrame { get; private set; }
        /// <summary>Ultima salud publicada por el puente. Size=0 si este puente no la trae.</summary>
        internal Rendering.NativeHealth Health { get; private set; }
        public string BridgeReason { get { return _bridge.Reason; } }
        /// <summary>Por qué la ruta de vectores de Unity entró o no. Null si no hay cámara.</summary>
        internal string MotionState { get { return _temporal != null ? _temporal.MotionState : null; } }
        public static void ToggleWindow() { if (_instance != null) _instance._panel.Toggle(); }
        public void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            ModSettings.Load();
            _pid = NativeInterop.GetCurrentProcessId();
            _channel = TelemetryChannel.Open(_pid, true);
            RenderStageProbe.Start();
        }
        public void Update()
        {
            float now = Time.unscaledTime;
            _elapsed += Time.unscaledDeltaTime; _frames++;
            if (_elapsed >= 0.5f) { _fps = _frames / _elapsed; _frameMs = _elapsed * 1000f / _frames; _elapsed = 0; _frames = 0; }
            if (now >= _discoverAt)
            {
                _discoverAt = now + 1;
                Camera discoveredCamera = Camera.main;
                if (_camera != discoveredCamera || (_camera != null && _temporal == null))
                {
                    if (_temporal != null) { _resetSerial = _temporal.ResetSerial; Destroy(_temporal); _temporal = null; }
                    _camera = discoveredCamera;
                    if (_camera != null) { _temporal = _camera.gameObject.AddComponent<TemporalCamera>(); _temporal.Bridge = _bridge; _temporal.ResetSerial = ++_resetSerial; }
                }
            }
            if (ModSettings.PipelineEnabled && ModSettings.ExperimentalOptIn && ModSettings.ForceMotionVectorsOnLoad && _camera != null) EnsureCameraModes();
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
                (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.N) && !UI.TelemetryPanel.HasTextFocus()) ToggleWindow();
            TelemetryCommand command;
            if (_channel != null && _channel.TryReadCommand(out command) && FramePolicy.Newer(unchecked((uint)command.Revision), unchecked((uint)_lastCommand)))
            {
                _commandResult = CommandResult.Accepted; _commandReason = 0; _commandIsControl = false;
                if (command.ExpiresUtcTicks < System.DateTime.UtcNow.Ticks) { _commandResult = CommandResult.Rejected; _commandReason = 1; }
                else if (command.Kind == CommandKind.TogglePanel) { ToggleWindow(); _commandResult = CommandResult.Applied; }
                else if (command.Kind == CommandKind.ReapplyBuffers && ModSettings.ExperimentalOptIn) { EnsureCameraModes(); _commandResult = CommandResult.Applied; }
                else if (command.Kind == CommandKind.ResetHistory && _temporal != null && ModSettings.PipelineEnabled) { RequestHistoryReset(); _commandReset = unchecked((uint)_temporal.ResetSerial); }
                else if (command.Kind == CommandKind.EnablePipeline || command.Kind == CommandKind.DisablePipeline) {
                    if (SetPipelineEnabled(command.Kind == CommandKind.EnablePipeline)) _commandResult = CommandResult.Applied;
                    else { _commandResult = CommandResult.Rejected; _commandReason = 3; }
                }
                else if (command.Kind == CommandKind.SetWorkResolution || command.Kind == CommandKind.SetSharpness) {
                    _commandIsControl = true;
                    if (!SetSessionControls(command.Kind == CommandKind.SetWorkResolution ? command.IntValue : 0, command.Kind == CommandKind.SetSharpness ? command.FloatValue : -1)) { _commandResult = CommandResult.Rejected; _commandReason = 4; }
                }
                else if (command.Kind == CommandKind.QuitGame) { _commandResult = CommandResult.Applied; _quitRequested = true; }
                else { _commandResult = CommandResult.Rejected; _commandReason = 2; }
                _lastCommand = command.Revision;
            }
            if (now < _publishAt) return;
            _publishAt = now + 0.1f;
            _bridge.SetEnabled(ModSettings.PipelineEnabled && _camera != null);
            var status = _bridge.ReadStatus();
            Health = _bridge.ReadHealth();
            var result = _bridge.ReadResult();
            RuntimeFlags flags = _moduleFlags;
            if (ModSettings.PipelineEnabled) flags |= RuntimeFlags.PipelineRequested;
            if (_camera != null)
            {
                flags |= RuntimeFlags.CameraPresent;
                if ((_camera.depthTextureMode & DepthTextureMode.Depth) != 0) flags |= RuntimeFlags.DepthRequested;
                if ((_camera.depthTextureMode & DepthTextureMode.MotionVectors) != 0) flags |= RuntimeFlags.MotionRequested;
            }
            if (_bridge.Connected) flags |= RuntimeFlags.NativeConnected;
            bool recent = status.Frame > 0 && status.AgeMs < 2000;
            if (recent && status.Result == 1 && ModSettings.PipelineEnabled) flags |= RuntimeFlags.EvaluationSucceeded;
            if (_temporal != null) _resetSerial = _temporal.ResetSerial;
            bool sameHistory = _temporal != null && result.Epoch == _temporal.Epoch && ModSettings.PipelineEnabled;
            bool current = sameHistory && recent;
            if (current && result.MotionProvider == 2) flags |= RuntimeFlags.NativeMotion;
            if (current && result.OutputCommitted > 0) flags |= RuntimeFlags.OutputCommitted;
            if (current && result.NrConfirmed != 0) flags |= RuntimeFlags.NrConfirmed;
            if (current && result.UiIsolated != 0) flags |= RuntimeFlags.UiIsolated;
            if (!_commandIsControl && _commandResult == CommandResult.Accepted && current && (result.ResetSerial == _commandReset || FramePolicy.Newer(result.ResetSerial,_commandReset))) _commandResult = CommandResult.Applied;
            var controls = new NativeControls(); int controlResult = _bridge.ReadControls(ref controls);
            if (_controlRevision != 0 && controls.Revision == _controlRevision && controlResult != 0) {
                ControlMessage = controlResult == 1 ? "Ajuste aplicado; comprueba dimensiones efectivas" : controlResult == -2 ? "Ajuste caducado sin consumidor; vuelve a intentarlo con una ciudad activa" : "Ajuste rechazado: desactiva Performance Mode en ReShade para editar CAS";
                if (_commandIsControl && _commandResult == CommandResult.Accepted) _commandResult = controlResult == 1 ? CommandResult.Applied : CommandResult.Rejected;
            }
            CurrentFrame = new TelemetryFrame {
                ProcessId = _pid, UtcTicks = System.DateTime.UtcNow.Ticks, Fps = _fps, FrameMs = _frameMs,
                DisplayWidth = Screen.width, DisplayHeight = Screen.height, RenderWidth = _camera != null ? _camera.pixelWidth : 0,
                RenderHeight = _camera != null ? _camera.pixelHeight : 0, Flags = flags, CameraCuts = _resetSerial,
                FrameIndex = Time.frameCount, LastCommand = _lastCommand, NativeStatus = recent ? status.Result : 0, ResetCount = status.ResetSerial,
                WorkWidth = status.Width, WorkHeight = status.Height, Evaluations = status.Evaluations,
                BridgeBuild = _bridge.Capabilities.Build, DeviceEpoch = result.Epoch, CameraId = result.Camera, RecordedFrame = result.Recorded, SubmittedFrame = result.Submitted,
                CompletedFrame = result.Completed, OutputFrame = result.OutputCommitted, MotionProvider = current ? result.MotionProvider : 0,
                AdapterLow = result.AdapterLow, AdapterHigh = result.AdapterHigh, BackendError = sameHistory ? result.Error : 0,
                CommandResult = _commandResult, CommandReason = _commandReason, ControlsRevision=controls.Revision, ControlsResult=controlResult, RequestedWork=controls.WorkPercent, RequestedSharpness=controls.Sharpness
            };
            if (_channel != null) _channel.Publish(CurrentFrame);
            Config.SessionLog.Tick(now, CurrentFrame, Health, _fps, _frameMs, ControlMessage,
                _conflicts.Length == 0 ? null : "AA adicional: " + string.Join(", ", _conflicts), MotionState);
            if (_panel.Visible) _panel.Update(CurrentFrame,
                !string.IsNullOrEmpty(ModSettings.LastSaveError) ? "Guardado pendiente: " + ModSettings.LastSaveError :
                _temporal != null && _temporal.RestoreConflict ? "Se conserva un cambio posterior de otro mod en la cámara." :
                _conflicts.Length == 0 ? "" : "Revisar AA: " + string.Join(", ", _conflicts));
            // Después de publicar, para que el Hub llegue a ver el comando como aplicado.
            // Es la salida normal del juego, no una terminación del proceso.
            if (_quitRequested) { _quitRequested = false; Application.Quit(); }
        }
        public bool SetSessionControls(int workPercent, float sharpness)
        {
            if (!ModSettings.PipelineEnabled || !_bridge.Connected) { ControlMessage = "Activa el pipeline y comprueba el puente"; return false; }
            var previous = new NativeControls(); _bridge.ReadControls(ref previous);
            var controls = new NativeControls { Size=24, Version=3, Revision=unchecked(previous.Revision+1), Mask=(workPercent!=0?1u:0u)|(sharpness>=0?2u:0u), WorkPercent=workPercent, Sharpness=sharpness };
            if (controls.Revision==0) controls.Revision=1;
            bool accepted = _bridge.SetControls(ref controls);
            ControlMessage = accepted ? "Ajuste pendiente del hilo de render" : "Ajuste rechazado o hay otro pendiente";
            if (accepted) _controlRevision=controls.Revision;
            return accepted;
        }
        public bool SetPipelineEnabled(bool enabled)
        {
            ModSettings.PipelineEnabled = enabled;
            bool saved = ModSettings.Save();
            bool applied = _bridge.SetEnabled(enabled && _camera != null);
            if (!enabled && _temporal != null) _temporal.OnDisable();
            if (enabled) RequestHistoryReset();
            return saved && applied;
        }

        public void EnsureCameraModes()
        {
            if (!ModSettings.PipelineEnabled || !ModSettings.ExperimentalOptIn || !_bridge.Connected) return;
            if (_temporal != null) _temporal.EnsureCameraModes();
        }
        public void RequestHistoryReset()
        {
            if (_temporal != null) _temporal.RequestReset();
            if (!_bridge.Connected) Debug.Log("[NeuralFX] Reset pendiente: se requiere el feeder con puente NeuralFX.");
        }
        public void OnDestroy()
        {
            if (_instance != this) return;
            _bridge.SetEnabled(false); RenderStageProbe.Stop();
            _panel.Destroy();
            if (_temporal != null) Destroy(_temporal);
            if (_channel != null) { _channel.Dispose(); _channel = null; }
            _instance = null;
        }
    }
}
