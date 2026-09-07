using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeuralFX
{
    public class NeuralFXManager : MonoBehaviour
    {
        private static NeuralFXManager _instance;
        public static NeuralFXManager Instance => _instance;

        private bool _isPanelVisible = false;
        private Rect _windowRect = new Rect(40, 80, 420, 480);

        private bool _isDxgiHooked = false;
        private bool _isFeederHooked = false;
        private float _lastHookCheck = 0f;

        private float _fps = 0f;
        private float _frameTime = 0f;
        private float _fpsAccumulator = 0f;
        private int _fpsFrames = 0;
        private float _fpsTimer = 0f;

        private readonly List<string> _conflictingAAComponents = new List<string>();
        private float _lastConflictCheck = 0f;

        public static void ToggleWindow()
        {
            if (_instance != null)
            {
                _instance._isPanelVisible = !_instance._isPanelVisible;
            }
        }

        public void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            Config.ModSettings.Load();
            Debug.Log("[NeuralFX] Gestor in-game inicializado correctamente.");
        }

        public void Start()
        {
            CheckHooks();
            EnsureCameraModes();
        }

        public void Update()
        {
            // FPS Counter
            _fpsAccumulator += Time.unscaledDeltaTime;
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;

            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / _fpsAccumulator;
                _frameTime = (_fpsAccumulator / _fpsFrames) * 1000f;
                _fpsAccumulator = 0f;
                _fpsFrames = 0;
                _fpsTimer = 0f;
            }

            // Hook check every 3 seconds
            if (Time.unscaledTime - _lastHookCheck > 3f)
            {
                _lastHookCheck = Time.unscaledTime;
                CheckHooks();
            }

            // Ensure Camera Modes every second
            if (Time.frameCount % 60 == 0)
            {
                EnsureCameraModes();
            }

            // Check AA Conflicts every 5 seconds
            if (Config.ModSettings.WarnOnAaConflict && Time.unscaledTime - _lastConflictCheck > 5f)
            {
                _lastConflictCheck = Time.unscaledTime;
                ScanForConflictingAA();
            }

            // Hotkey Ctrl + Alt + N
            if (Config.ModSettings.EnableHotkey)
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

                if (ctrl && alt && Input.GetKeyDown(KeyCode.N))
                {
                    ToggleWindow();
                }
            }
        }

        private void CheckHooks()
        {
            _isDxgiHooked = NativeInterop.IsModuleLoaded("dxgi.dll");
            _isFeederHooked = NativeInterop.IsModuleLoaded("dlss5-feed.addon64");
        }

        public void EnsureCameraModes()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var targetMode = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                if ((cam.depthTextureMode & targetMode) != targetMode)
                {
                    cam.depthTextureMode |= targetMode;
                    Debug.Log("[NeuralFX] DepthTextureMode configurado: " + cam.depthTextureMode);
                }
            }
        }

        private void ScanForConflictingAA()
        {
            _conflictingAAComponents.Clear();
            var cam = Camera.main;
            if (cam == null) return;

            var components = cam.GetComponents<MonoBehaviour>();
            foreach (var comp in components)
            {
                if (comp == null || !comp.enabled) continue;

                string typeName = comp.GetType().Name;
                if (typeName.IndexOf("Antialiasing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("SMAA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("TAA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("Temporal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeName.IndexOf("FXAA", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _conflictingAAComponents.Add(typeName);
                }
            }
        }

        public void OnGUI()
        {
            if (!_isPanelVisible) return;

            GUI.skin = null; // Use standard styling
            _windowRect = GUI.Window(98721, _windowRect, DrawTelemetryWindow, "NeuralFX - Telemetría DLSS 5 & ReShade");
        }

        private void DrawTelemetryWindow(int windowId)
        {
            GUILayout.BeginVertical();

            // Header info
            GUILayout.Space(4);
            GUILayout.Label("Estado del Pipeline Gráfico Nativo:", UnityEditorStylesLikeBold());

            // Hooks status
            string dxgiStatus = _isDxgiHooked ? "<color=#4EC9B0>[ACTIVO] ReShade DXGI Hook</color>" : "<color=#F44747>[NO DETECTADO] dxgi.dll</color>";
            GUILayout.Label(dxgiStatus, RichTextLabel());

            string feederStatus = _isFeederHooked ? "<color=#4EC9B0>[ACTIVO] DLSS5-Feeder Addon</color>" : "<color=#CE9178>[PENDIENTE] dlss5-feed.addon64</color>";
            GUILayout.Label(feederStatus, RichTextLabel());

            GUILayout.Space(8);
            GUILayout.Label("Estado de la Cámara Principal:", UnityEditorStylesLikeBold());

            var cam = Camera.main;
            if (cam != null)
            {
                bool hasDepth = (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
                bool hasMv = (cam.depthTextureMode & DepthTextureMode.MotionVectors) != 0;

                string depthStr = hasDepth ? "<color=#4EC9B0>[OK] Depth Buffer</color>" : "<color=#F44747>[FALTA] Depth Buffer</color>";
                string mvStr = hasMv ? "<color=#4EC9B0>[OK] Motion Vectors</color>" : "<color=#F44747>[FALTA] Motion Vectors</color>";

                GUILayout.Label(depthStr + " | " + mvStr, RichTextLabel());
                GUILayout.Label("Resolución de Salida: " + Screen.width + "x" + Screen.height + " @ " + Screen.currentResolution.refreshRate + "Hz");
            }
            else
            {
                GUILayout.Label("<color=#CE9178>Cámara principal no encontrada en este estado.</color>", RichTextLabel());
            }

            GUILayout.Space(8);
            GUILayout.Label("Rendimiento In-Game:", UnityEditorStylesLikeBold());
            GUILayout.Label(string.Format("FPS: {0:F1}  ({1:F2} ms)", _fps, _frameTime));

            // Temporal Collision Warning
            GUILayout.Space(8);
            if (_conflictingAAComponents.Count > 0)
            {
                GUILayout.Label("<color=#F44747><b>ALERTA DE COLISIÓN TEMPORAL:</b></color>", RichTextLabel());
                GUILayout.Label("Se detectaron módulos de AA legacy activos en la cámara:", RichTextLabel());
                foreach (var c in _conflictingAAComponents)
                {
                    GUILayout.Label(" - " + c);
                }
                GUILayout.Label("<i>Desactiva el TAA legacy del mod de origen para permitir que DLSS 5 gestione el jittering y la reconstrucción sin ghosting.</i>", RichTextLabel());
            }
            else
            {
                GUILayout.Label("<color=#4EC9B0>Colisión temporal: Ninguna (Óptimo para DLSS)</color>", RichTextLabel());
            }

            GUILayout.Space(12);
            GUILayout.Label("Atajos de Teclado:", UnityEditorStylesLikeBold());
            GUILayout.Label(" - [Home]: Abrir overlay nativo de ReShade");
            GUILayout.Label(" - [Ctrl + Alt + N]: Abrir/Cerrar este panel");

            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reaplicar Depth/Motion"))
            {
                EnsureCameraModes();
                ScanForConflictingAA();
            }
            if (GUILayout.Button("Cerrar Panel"))
            {
                _isPanelVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private GUIStyle UnityEditorStylesLikeBold()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            return style;
        }

        private GUIStyle RichTextLabel()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.richText = true;
            return style;
        }
    }
}
