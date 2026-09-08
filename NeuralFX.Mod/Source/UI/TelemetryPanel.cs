using ColossalFramework.UI;
using UnityEngine;

namespace NeuralFX.UI
{
    internal sealed class TelemetryPanel
    {
        private UIPanel _panel;
        private UILabel _metrics, _status, _camera, _conflicts;
        public bool Visible { get { return _panel != null && _panel.isVisible; } }
        public void Toggle()
        {
            if (_panel == null) Create();
            if (_panel != null) { _panel.isVisible = !_panel.isVisible; if (!_panel.isVisible) SavePosition(); }
        }
        private void Create()
        {
            UIView view = UIView.GetAView();
            if (view == null) return;
            _panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
            _panel.name = "NeuralFXTelemetry";
            _panel.size = new Vector2(460, 390);
            float x = Config.ModSettings.PanelX, y = Config.ModSettings.PanelY;
            if (float.IsNaN(x) || float.IsInfinity(x)) x = 40;
            if (float.IsNaN(y) || float.IsInfinity(y)) y = 80;
            _panel.relativePosition = new Vector3(Mathf.Clamp(x, 0, Mathf.Max(0, Screen.width - 460)), Mathf.Clamp(y, 0, Mathf.Max(0, Screen.height - 390)));
            _panel.backgroundSprite = "MenuPanel2";
            _panel.isVisible = false;
            var drag = _panel.AddUIComponent<UIDragHandle>();
            drag.relativePosition = new Vector3(8, 4); drag.size = new Vector2(400, 38); drag.target = _panel;
            var title = Label("NeuralFX", 16, 14); title.textScale = 1.1f; title.isInteractive = false;
            var close = _panel.AddUIComponent<UIButton>(); close.text = "×"; close.relativePosition = new Vector3(420, 7); close.size = new Vector2(30, 30);
            close.eventClicked += (c, e) => { _panel.isVisible = false; SavePosition(); };
            _metrics = Label("", 16, 62); _metrics.height = 28;
            _status = Label("", 16, 98); _status.height = 85;
            _camera = Label("", 16, 190); _camera.height = 65;
            _conflicts = Label("", 16, 265); _conflicts.height = 55;
            var refresh = _panel.AddUIComponent<UIButton>(); refresh.text = "Reaplicar buffers"; refresh.normalBgSprite = "ButtonMenu";
            refresh.relativePosition = new Vector3(16, 344); refresh.size = new Vector2(190, 28);
            refresh.eventClicked += (c, e) => NeuralFXManager.Instance.EnsureCameraModes();
            var reset = _panel.AddUIComponent<UIButton>(); reset.text = "Reiniciar historial"; reset.normalBgSprite = "ButtonMenu";
            reset.relativePosition = new Vector3(225, 344); reset.size = new Vector2(210, 28);
            reset.eventClicked += (c, e) => NeuralFXManager.Instance.RequestHistoryReset();
        }
        private UILabel Label(string text, float x, float y)
        {
            var label = _panel.AddUIComponent<UILabel>(); label.text = text; label.autoSize = false;
            label.size = new Vector2(425, 55); label.relativePosition = new Vector3(x, y); label.wordWrap = true; label.textScale = 0.85f;
            return label;
        }
        public void Update(string metrics, string status, string camera, string conflicts)
        {
            if (!Visible) return;
            _metrics.text = metrics; _status.text = status; _camera.text = camera; _conflicts.text = conflicts;
        }
        private void SavePosition()
        {
            if (_panel == null) return;
            Config.ModSettings.PanelX = _panel.relativePosition.x; Config.ModSettings.PanelY = _panel.relativePosition.y; Config.ModSettings.Save();
        }
        public void Destroy() { if (_panel != null) { SavePosition(); Object.Destroy(_panel.gameObject); _panel = null; } }
    }
}
