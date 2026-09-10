using System;
using ColossalFramework.UI;
using UnityEngine;

namespace NeuralFX.UI
{
    // Entrada al Hub desde el menú principal, como hace Skyve. Es la única forma de abrirlo
    // con el juego cerrado sin dejar el ejecutable dentro de Addons/Mods, donde CS1 lo cargaría
    // como ensamblado del juego.
    //
    // Se inyecta clonando un botón real del menú, así que hereda su tipografía y sus sprites.
    // Si la estructura del menú no es la esperada no se inyecta nada y no se rompe nada: el
    // Hub sigue accesible desde las opciones del mod.
    internal sealed class MainMenuButton : MonoBehaviour
    {
        private const string ButtonName = "NeuralFXMainMenu";
        private static GameObject _host;
        private float _next;
        private UILabel _feedback;

        public static void Install()
        {
            if (_host != null) return;
            _host = new GameObject("NeuralFX_MainMenuButton");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<MainMenuButton>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            try { Inject(); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning("[NeuralFX] menú principal: " + ex.Message); enabled = false; }
        }

        private void Inject()
        {
            var view = UIView.GetAView();
            if (view == null) return;

            UIPanel menu = FindMainMenu(view);
            if (menu == null || menu.Find<UIButton>(ButtonName) != null) return;

            UIButton template = Pick(menu, "ContentManager") ?? Pick(menu, "Options") ?? Pick(menu, "Exit");
            if (template == null) return;

            var button = menu.AddUIComponent<UIButton>();
            button.name = ButtonName;
            button.text = "NEURALFX";
            button.tooltip = "Abrir NeuralFX Hub: instalación, componentes y diagnóstico";
            button.size = template.size;
            button.font = template.font;
            button.textScale = template.textScale;
            button.textColor = template.textColor;
            button.hoveredTextColor = template.hoveredTextColor;
            button.pressedTextColor = template.pressedTextColor;
            button.disabledTextColor = template.disabledTextColor;
            button.normalBgSprite = template.normalBgSprite;
            button.hoveredBgSprite = template.hoveredBgSprite;
            button.pressedBgSprite = template.pressedBgSprite;
            button.disabledBgSprite = template.disabledBgSprite;
            button.textHorizontalAlignment = template.textHorizontalAlignment;
            button.textVerticalAlignment = template.textVerticalAlignment;
            button.wordWrap = template.wordWrap;
            button.zOrder = template.zOrder;   // queda justo encima del botón que clonamos
            button.eventClicked += (component, parameter) => Report(menu, template, HubLauncher.Open());
        }

        // El menú principal es el único panel que contiene NewGame: el menú de pausa dentro de
        // una ciudad no lo tiene, y así no se inyecta el botón donde no toca.
        private static UIPanel FindMainMenu(UIView view)
        {
            foreach (var panel in view.GetComponentsInChildren<UIPanel>(true))
            {
                if (!panel.isVisible) continue;
                if (Pick(panel, "NewGame") == null) continue;
                if (Pick(panel, "LoadGame") == null && Pick(panel, "ContentManager") == null) continue;
                return panel;
            }
            return null;
        }

        private static UIButton Pick(UIComponent parent, string name)
        {
            var button = parent.Find<UIButton>(name);
            return button != null && button.parent == parent ? button : null;
        }

        private void Report(UIPanel menu, UIButton anchor, string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_feedback == null || _feedback.parent != menu)
            {
                _feedback = menu.AddUIComponent<UILabel>();
                _feedback.autoSize = false; _feedback.autoHeight = true; _feedback.wordWrap = true;
                _feedback.textScale = .75f; _feedback.isInteractive = false;
            }
            _feedback.width = Mathf.Max(200, menu.width - 24);
            _feedback.zOrder = anchor.zOrder + 1;
            _feedback.text = message;
        }
    }
}
