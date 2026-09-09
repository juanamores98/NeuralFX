using ColossalFramework.UI;
using UnityEngine;
namespace NeuralFX.UI
{
    internal sealed class TelemetryPanel
    {
        private UIPanel _panel;
        private UIScrollablePanel _body;
        private UILabel _metrics, _status, _camera, _conflicts;
        private UIButton _toggle, _close, _reset;
        private UILabel _workLabel, _sharpLabel, _controlMessage;
        private readonly UIButton[] _workButtons = new UIButton[3], _sharpButtons = new UIButton[3];
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
            _panel.size = new Vector2(480, 480); _panel.backgroundSprite = "MenuPanel2"; _panel.isVisible = false;
            float x = Config.ModSettings.PanelX, y = Config.ModSettings.PanelY;
            _panel.relativePosition = new Vector3(float.IsNaN(x) || float.IsInfinity(x) ? 40 : x, float.IsNaN(y) || float.IsInfinity(y) ? 80 : y);
            var title = _panel.AddUIComponent<UILabel>(); title.text = "NeuralFX"; title.relativePosition = new Vector3(16, 14); title.isInteractive = false;
            var drag = _panel.AddUIComponent<UIDragHandle>(); drag.relativePosition = new Vector3(8, 4); drag.size = new Vector2(380, 38); drag.target = _panel;
            var close = Button(_panel, "X", 435, 8, 28); _close = close; close.eventClicked += (c,e) => { _panel.isVisible = false; SavePosition(); };
            close.tooltip = "Cerrar panel";
            _body = _panel.AddUIComponent<UIScrollablePanel>(); _body.relativePosition = new Vector3(12, 50);
            _body.size = new Vector2(450, 415); _body.clipChildren = true; _body.scrollWheelDirection = UIOrientation.Vertical;
            _toggle = Button(_body, "", 0, 0, 425);
            _toggle.eventClicked += (c,e) => { var manager = NeuralFXManager.Instance; if (manager != null) manager.SetPipelineEnabled(!Config.ModSettings.PipelineEnabled); };
            _metrics = Label(40, 35); _status = Label(80, 60); _camera = Label(146, 190); _conflicts = Label(342, 65);
            _workLabel = Label(414,28); _workLabel.text = "Trabajo NR (la escena Unity mantiene su tamaño)";
            for (int i=0;i<3;i++) { int percent = new[] {100,85,66}[i]; var choose = Button(_body,percent+"%",i*140,446,130);
                _workButtons[i] = choose; choose.eventClicked += (c,e)=> {if(NeuralFXManager.Instance!=null)NeuralFXManager.Instance.SetSessionControls(percent,-1);}; }
            _sharpLabel = Label(484,28); _sharpLabel.text = "Nitidez propia";
            for(int i=0;i<3;i++) {float value = new[] {0f,.15f,.3f}[i];var choose=Button(_body,i==0?"Apagada":value.ToString("0.00"),i*140,516,130);
                _sharpButtons[i] = choose; choose.eventClicked += (c,e)=>{if(NeuralFXManager.Instance!=null)NeuralFXManager.Instance.SetSessionControls(0,value);};}
            _controlMessage = Label(550,45);
            var reset = Button(_body, "Solicitar reset de historial", 0, 558, 425);
            _reset = reset; reset.eventClicked += (c,e) => { if (NeuralFXManager.Instance != null) NeuralFXManager.Instance.RequestHistoryReset(); };
            reset.tooltip = "El reset solo se confirma al completar una salida GPU de la historia correspondiente.";
            Fit();
        }
        private static UIButton Button(UIComponent parent, string text, float x, float y, float width)
        {
            var button = parent.AddUIComponent<UIButton>(); button.text = text; button.normalBgSprite = "ButtonMenu";
            button.hoveredBgSprite = "ButtonMenuHovered"; button.focusedBgSprite = "ButtonMenuFocused";
            button.relativePosition = new Vector3(x,y); button.size = new Vector2(width,30); return button;
        }
        private UILabel Label(float y, float height)
        {
            var label = _body.AddUIComponent<UILabel>(); label.autoSize = false; label.autoHeight = true; label.size = new Vector2(425,height);
            label.relativePosition = new Vector3(0,y); label.wordWrap = true; label.textScale = .82f; return label;
        }
        private void Fit()
        {
            var view = UIView.GetAView(); if (view == null || _panel == null) return;
            _panel.height = Mathf.Min(520, Mathf.Max(180, view.fixedHeight - 24));
            _panel.width = Mathf.Min(480, Mathf.Max(240, view.fixedWidth - 24));
            _body.width = _panel.width - 24; _body.height = _panel.height - 62;
            _close.relativePosition = new Vector3(_panel.width-40,8);
            _toggle.width = _body.width-8;
            _panel.relativePosition = new Vector3(Mathf.Clamp(_panel.relativePosition.x,0,Mathf.Max(0,view.fixedWidth-_panel.width)),Mathf.Clamp(_panel.relativePosition.y,0,Mathf.Max(0,view.fixedHeight-_panel.height)));
            float y = 40;
            foreach (var label in new[] {_metrics,_status,_camera,_conflicts,_controlMessage,_workLabel}) Place(label, ref y);
            PlaceButtons(_workButtons,ref y); Place(_sharpLabel,ref y); PlaceButtons(_sharpButtons,ref y);
            _reset.relativePosition = new Vector3(0,y); _reset.width = _body.width-8;
        }
        private void Place(UILabel label,ref float y)
        {
            if(label==null)return; label.width=_body.width-8;label.relativePosition=new Vector3(0,y);
            y+=Mathf.Max(28,label.height)+8;
        }
        private void PlaceButtons(UIButton[] buttons,ref float y)
        {
            float width=(_body.width-24)/3;
            for(int i=0;i<buttons.Length;i++){buttons[i].width=width;buttons[i].relativePosition=new Vector3(i*(width+8),y);} y+=38;
        }
        public void Update(string metrics,string status,string camera,string conflicts)
        {
            if (!Visible) return;
            Set(_metrics,metrics); Set(_status,status); Set(_camera,camera); Set(_conflicts,conflicts); Set(_controlMessage,NeuralFXManager.Instance!=null?NeuralFXManager.Instance.ControlMessage??"":"");
            _toggle.text = Config.ModSettings.PipelineEnabled ? "Desactivar NeuralFX" : "Activar NeuralFX"; Fit();
        }
        private static void Set(UILabel label,string text) { if (label.text != text) label.text = text; }
        private void SavePosition() { if (_panel == null) return; Config.ModSettings.PanelX=_panel.relativePosition.x; Config.ModSettings.PanelY=_panel.relativePosition.y; Config.ModSettings.Save(); }
        public void Destroy() { if (_panel != null) { SavePosition(); Object.Destroy(_panel.gameObject); _panel=null; } }
    }
}
