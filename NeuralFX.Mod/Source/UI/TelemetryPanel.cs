using ColossalFramework.UI;
using NeuralFX.Protocol;
using UnityEngine;
namespace NeuralFX.UI
{
    // Panel minimalista: una línea de estado, tres métricas y dos segmentados.
    // El detalle técnico completo está plegado; su lugar natural es el Hub.
    internal sealed class TelemetryPanel
    {
        private const float PanelWidth = 352f, TitleHeight = 42f, Gap = 10f;
        private static readonly Color32 TextColor = new Color32(220, 228, 234, 255);
        // 4,5:1 sobre el panel Colossal; el gris anterior (147,162,174) se quedaba corto.
        private static readonly Color32 DimColor = new Color32(170, 184, 196, 255);
        private static readonly Color32 AccentColor = new Color32(127, 216, 192, 255);
        private static readonly Color32 WarnColor = new Color32(224, 180, 140, 255);
        private static readonly int[] WorkSteps = { 100, 85, 66 };
        private static readonly float[] SharpSteps = { 0f, .15f, .3f };

        private UIPanel _panel;
        private UIScrollablePanel _body;
        private UIPanel _dot;
        private UILabel _summary, _sub, _controlMessage, _details, _resetInfo, _workLabel, _sharpLabel, _health;
        private readonly UILabel[] _metricCaptions = new UILabel[3], _metricValues = new UILabel[3];
        private UIButton _toggle, _close, _reset, _detailToggle;
        private readonly UIButton[] _workButtons = new UIButton[3], _sharpButtons = new UIButton[3];
        private bool _showDetails;

        public bool Visible { get { return _panel != null && _panel.isVisible; } }
        public static bool HasTextFocus()
        {
            var view = UIView.GetAView(); if (view == null) return false;
            foreach (var field in view.GetComponentsInChildren<UITextField>()) if (field.containsFocus) return true;
            return false;
        }
        public void Toggle()
        {
            if (_panel == null) Create();
            if (_panel != null) { Fit(); _panel.isVisible = !_panel.isVisible; if (!_panel.isVisible) SavePosition(); }
        }

        private void Create()
        {
            UIView view = UIView.GetAView(); if (view == null) return;
            _panel = (UIPanel)view.AddUIComponent(typeof(UIPanel)); _panel.name = "NeuralFXControls";
            _panel.size = new Vector2(PanelWidth, 460); _panel.backgroundSprite = "MenuPanel2"; _panel.isVisible = false;
            float x = Config.ModSettings.PanelX, y = Config.ModSettings.PanelY;
            _panel.relativePosition = new Vector3(float.IsNaN(x) || float.IsInfinity(x) ? 40 : x, float.IsNaN(y) || float.IsInfinity(y) ? 80 : y);

            var title = _panel.AddUIComponent<UILabel>(); title.text = "NeuralFX"; title.textScale = .95f;
            title.relativePosition = new Vector3(14, 13); title.isInteractive = false;
            var drag = _panel.AddUIComponent<UIDragHandle>(); drag.relativePosition = new Vector3(6, 4);
            drag.size = new Vector2(PanelWidth - 46, TitleHeight - 6); drag.target = _panel;
            _close = Button(_panel, "X", PanelWidth - 34, 9, 24); _close.tooltip = "Cerrar panel";
            _close.eventClicked += (c, e) => { _panel.isVisible = false; SavePosition(); };

            _body = _panel.AddUIComponent<UIScrollablePanel>(); _body.relativePosition = new Vector3(12, TitleHeight);
            _body.clipChildren = true; _body.scrollWheelDirection = UIOrientation.Vertical;

            _dot = _body.AddUIComponent<UIPanel>(); _dot.backgroundSprite = "GenericPanel"; _dot.size = new Vector2(8, 8);
            _dot.color = DimColor; _dot.isInteractive = false;
            _summary = Label(.95f, TextColor); _sub = Label(.72f, DimColor);

            for (int i = 0; i < 3; i++) { _metricCaptions[i] = Label(.62f, DimColor); _metricValues[i] = Label(1.05f, TextColor); }
            _metricCaptions[0].text = "FPS"; _metricCaptions[1].text = "FRAME"; _metricCaptions[2].text = "SALIDA";

            _toggle = Button(_body, "", 0, 0, 300);
            _toggle.eventClicked += (c, e) => { var manager = NeuralFXManager.Instance; if (manager != null) manager.SetPipelineEnabled(!Config.ModSettings.PipelineEnabled); };

            _workLabel = Label(.72f, DimColor); _workLabel.text = "Trabajo NR";
            for (int i = 0; i < 3; i++)
            {
                int percent = WorkSteps[i];
                _workButtons[i] = Button(_body, percent + "%", 0, 0, 100);
                _workButtons[i].eventClicked += (c, e) => { if (NeuralFXManager.Instance != null) NeuralFXManager.Instance.SetSessionControls(percent, -1); };
            }
            _sharpLabel = Label(.72f, DimColor); _sharpLabel.text = "Nitidez";
            for (int i = 0; i < 3; i++)
            {
                float value = SharpSteps[i];
                _sharpButtons[i] = Button(_body, i == 0 ? "Apagada" : value.ToString("0.00"), 0, 0, 100);
                _sharpButtons[i].eventClicked += (c, e) => { if (NeuralFXManager.Instance != null) NeuralFXManager.Instance.SetSessionControls(0, value); };
            }

            _reset = Button(_body, "Reset de historial", 0, 0, 300);
            _reset.tooltip = "El reset solo se confirma al completar una salida GPU de la historia correspondiente.";
            _reset.eventClicked += (c, e) => { if (NeuralFXManager.Instance != null) NeuralFXManager.Instance.RequestHistoryReset(); };
            _resetInfo = Label(.68f, DimColor);
            _health = Label(.68f, DimColor);
            _controlMessage = Label(.7f, WarnColor);

            _detailToggle = Button(_body, "Ver detalle técnico", 0, 0, 300);
            _detailToggle.eventClicked += (c, e) => { _showDetails = !_showDetails; Fit(); };
            _details = Label(.68f, DimColor); _details.isVisible = false;
            Fit();
        }

        private static UIButton Button(UIComponent parent, string text, float x, float y, float width)
        {
            var button = parent.AddUIComponent<UIButton>(); button.text = text; button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered"; button.pressedBgSprite = "ButtonMenuPressed";
            button.textScale = .82f; button.relativePosition = new Vector3(x, y); button.size = new Vector2(width, 30);
            return button;
        }
        private UILabel Label(float scale, Color32 color)
        {
            var label = _body.AddUIComponent<UILabel>(); label.autoSize = false; label.autoHeight = true;
            label.size = new Vector2(300, 20); label.wordWrap = true; label.textScale = scale; label.textColor = color;
            return label;
        }

        private void Fit()
        {
            var view = UIView.GetAView(); if (view == null || _panel == null) return;
            _panel.width = Mathf.Min(PanelWidth, Mathf.Max(260, view.fixedWidth - 24));
            _panel.height = Mathf.Min(_showDetails ? 560 : 420, Mathf.Max(200, view.fixedHeight - 24));
            _body.width = _panel.width - 24; _body.height = _panel.height - TitleHeight - 12;
            _close.relativePosition = new Vector3(_panel.width - 34, 9);
            _panel.relativePosition = new Vector3(
                Mathf.Clamp(_panel.relativePosition.x, 0, Mathf.Max(0, view.fixedWidth - _panel.width)),
                Mathf.Clamp(_panel.relativePosition.y, 0, Mathf.Max(0, view.fixedHeight - _panel.height)));

            float width = _body.width - 6, y = 4;
            _dot.relativePosition = new Vector3(0, y + 6);
            Place(_summary, 18, width - 18, ref y);
            Place(_sub, 18, width - 18, ref y);
            y += 4;

            float column = width / 3f;
            for (int i = 0; i < 3; i++)
            {
                _metricCaptions[i].width = column - 6; _metricCaptions[i].relativePosition = new Vector3(i * column, y);
                _metricValues[i].width = column - 6; _metricValues[i].relativePosition = new Vector3(i * column, y + 15);
            }
            y += 44;

            _toggle.width = width; _toggle.relativePosition = new Vector3(0, y); y += 38;
            Place(_workLabel, 0, width, ref y);
            PlaceSegments(_workButtons, width, ref y);
            Place(_sharpLabel, 0, width, ref y);
            PlaceSegments(_sharpButtons, width, ref y);
            _reset.width = width; _reset.relativePosition = new Vector3(0, y); y += 34;
            Place(_resetInfo, 0, width, ref y);
            Place(_health, 0, width, ref y);
            Place(_controlMessage, 0, width, ref y);
            _detailToggle.width = width; _detailToggle.relativePosition = new Vector3(0, y); y += 36;
            _details.isVisible = _showDetails;
            if (_showDetails) Place(_details, 0, width, ref y);
        }
        private static void Place(UILabel label, float x, float width, ref float y)
        {
            if (label == null) return;
            label.width = width; label.relativePosition = new Vector3(x, y);
            y += Mathf.Max(16, label.height) + Gap;
        }
        private static void PlaceSegments(UIButton[] buttons, float width, ref float y)
        {
            float each = (width - 12) / 3f;
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].width = each; buttons[i].relativePosition = new Vector3(i * (each + 6), y);
            }
            y += 38;
        }
        private static void Highlight(UIButton[] buttons, int selected)
        {
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].normalBgSprite = i == selected ? "ButtonMenuFocused" : "ButtonMenu";
        }
        private static int NearestWork(int percent)
        {
            if (percent <= 0) return 0;
            int best = 0;
            for (int i = 1; i < WorkSteps.Length; i++) if (Mathf.Abs(WorkSteps[i] - percent) < Mathf.Abs(WorkSteps[best] - percent)) best = i;
            return best;
        }
        private static int NearestSharp(float value)
        {
            if (value < 0) return 2;
            int best = 0;
            for (int i = 1; i < SharpSteps.Length; i++) if (Mathf.Abs(SharpSteps[i] - value) < Mathf.Abs(SharpSteps[best] - value)) best = i;
            return best;
        }

        public void Update(TelemetryFrame frame, string notice)
        {
            if (!Visible) return;
            Set(_summary, SessionViewState.Summary(frame));
            _dot.color = DotColor(frame);
            string motion = frame.MotionProvider == 2 ? "movimiento Unity (sin validar)" : frame.MotionProvider == 1 ? "movimiento óptico" : "sin movimiento confirmado";
            Set(_sub, "Puente build " + frame.BridgeBuild + " · " + motion + " · UI " +
                (SessionViewState.Has(frame, RuntimeFlags.UiIsolated) ? "protegida" : "no aislada") +
                // El consumidor fijado no publica confirmación por frame, así que "sin confirmar"
                // es el estado correcto incluso cuando NR se está ejecutando. Decirlo evita la duda.
                (SessionViewState.Has(frame, RuntimeFlags.EvaluationSucceeded) && !SessionViewState.Has(frame, RuntimeFlags.NrConfirmed)
                    ? "\nEl consumidor fijado no confirma NR por frame; comprueba las evaluaciones en ReShade.log."
                    : ""));

            Set(_metricValues[0], frame.Fps.ToString("F1"));
            Set(_metricValues[1], frame.FrameMs.ToString("F1") + " ms");
            Set(_metricValues[2], frame.DisplayHeight > 0 ? frame.DisplayHeight + "p" : "-");

            _toggle.text = Config.ModSettings.PipelineEnabled ? "Desactivar NeuralFX" : "Activar NeuralFX";
            Highlight(_workButtons, NearestWork(frame.RequestedWork));
            Highlight(_sharpButtons, NearestSharp(frame.RequestedSharpness));
            string command = frame.LastCommand != 0 ? " · " + SessionViewState.CommandStatus(frame).ToLower() : "";
            Set(_resetInfo, frame.ResetCount + " confirmados de " + frame.CameraCuts + command);

            var manager = NeuralFXManager.Instance;
            Set(_health, manager != null ? DescribeGeometry(frame) + DescribeHealth(manager.Health) : "");
            if (_health != null) _health.textColor = manager != null && manager.Health.DepthFlatMoving != 0 ? WarnColor : DimColor;
            string message = manager != null ? manager.ControlMessage : null;
            Set(_controlMessage, string.IsNullOrEmpty(message) ? notice ?? "" : message);
            _detailToggle.text = _showDetails ? "Ocultar detalle técnico" : "Ver detalle técnico";
            if (_showDetails) Set(_details, SessionViewState.Details(frame));
            Fit();
        }
        // La cámara de CS1 no ocupa todo el backbuffer: dibuja en una región de la pantalla.
        //
        // Esto SOLO informa de esa diferencia de tamaños. No dice que la imagen se estire ni que
        // las guías estén desalineadas: para afirmar eso haría falta observar el recurso de
        // origen y la operación de copia o muestreo, no restar dos alturas. Una versión anterior
        // de este panel anunciaba "Guías estiradas 10,5 %" y era una deducción, no una medida.
        private static string DescribeGeometry(TelemetryFrame frame)
        {
            if (frame.RenderHeight <= 0 || frame.DisplayHeight <= 0) return "";
            if (frame.RenderWidth == frame.DisplayWidth && frame.RenderHeight == frame.DisplayHeight) return "";
            return "Viewport de escena " + frame.RenderWidth + "x" + frame.RenderHeight +
                " dentro de una salida " + frame.DisplayWidth + "x" + frame.DisplayHeight +
                ". Correspondencia de guías: sin verificar.\n";
        }
        // Lo que costo tres sesiones de diagnostico averiguar, dicho donde se ve.
        private static string DescribeHealth(Rendering.NativeHealth health)
        {
            if (health.Size == 0) return "Este puente no publica salud del pipeline.";
            if (health.ProbeFrame == 0) return "Sondas: aun sin medir (la primera llega a los 600 frames).";
            // Una muestra plana no distingue entre buffer equivocado, etapa de captura, copia
            // incompleta, region leida o conversion. Se informa la observacion; la causa la
            // decide el diagnostico, no este texto.
            if (health.DepthFlatMoving != 0)
                return "Profundidad plana en la ultima muestra, con la escena en movimiento. Origen sin identificar.";
            string depth = health.DepthFinitePct == 0 || health.DepthMax - health.DepthMin < 1e-6f
                ? "profundidad plana"
                : "profundidad " + health.DepthMin.ToString("0.###") + "-" + health.DepthMax.ToString("0.###");
            string motion = health.MvNonZeroPct < 2
                ? "sin movimiento (normal con la camara quieta)"
                : "movimiento " + health.MvMeanPx.ToString("0.0") + " px en el " + health.MvNonZeroPct + "%";
            string cost = health.FeedGpuMs > 0 ? " · coste " + health.FeedGpuMs.ToString("0.0") + " ms de GPU" : "";
            return depth + " · " + motion + cost;
        }
        private static Color32 DotColor(TelemetryFrame frame)
        {
            if (!SessionViewState.Has(frame, RuntimeFlags.CameraPresent)) return DimColor;
            if (!SessionViewState.Has(frame, RuntimeFlags.PipelineRequested)) return DimColor;
            if (!SessionViewState.Has(frame, RuntimeFlags.NativeConnected) || frame.BackendError != 0) return WarnColor;
            if (SessionViewState.Has(frame, RuntimeFlags.NrConfirmed)) return AccentColor;
            return SessionViewState.Has(frame, RuntimeFlags.EvaluationSucceeded) ? AccentColor : WarnColor;
        }
        private static void Set(UILabel label, string text) { if (label != null && label.text != text) label.text = text; }
        private void SavePosition() { if (_panel == null) return; Config.ModSettings.PanelX = _panel.relativePosition.x; Config.ModSettings.PanelY = _panel.relativePosition.y; Config.ModSettings.Save(); }
        public void Destroy() { if (_panel != null) { SavePosition(); Object.Destroy(_panel.gameObject); _panel = null; } }
    }
}
