using System;
using System.Diagnostics;
using System.IO;
using ColossalFramework.UI;
using ICities;
using NeuralFX.Config;
using NeuralFX.Protocol;
using UnityEngine;
namespace NeuralFX.Options
{
    // Cuatro grupos: estado, procesado, acceso y lo experimental detrás de un único permiso.
    // Nada que hoy no haga algo aparece aquí como interruptor.
    internal static class OptionsPanel
    {
        public static void Build(UIHelperBase helper)
        {
            var status = helper.AddGroup("Estado");
            var label = Note(status, "Esperando estado...");
            if (label != null) label.gameObject.AddComponent<OptionsStatus>().Label = label;
            status.AddCheckbox("Registrar telemetría de la sesión en disco", ModSettings.EnableSessionLog, value => { ModSettings.EnableSessionLog = value; ModSettings.Save(); });
            Note(status, "Una entrada cada cinco segundos, y una más en cada cambio de estado, en %LOCALAPPDATA%\\NeuralFX\\Diagnostics. Déjala encendida mientras se depura.");
            status.AddCheckbox("Anotar las cámaras de la escena en ese registro", ModSettings.EnableRenderTrace, value => { ModSettings.EnableRenderTrace = value; Rendering.RenderStageProbe.Restart(); ModSettings.Save(); });
            Note(status, "Cada configuración de cámara distinta —nombre, orden, geometría, rect y destino— se anota una sola vez, la primera vez que aparece. Es lo que dice por qué la cámara mide 3840x1933 sobre un backbuffer de 3840x2160. Solo lee.");
            status.AddButton("Abrir la carpeta de diagnósticos", () => {
                try
                {
                    string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuralFX/Diagnostics");
                    System.IO.Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
                catch (Exception ex) { if (label != null) label.text = ex.Message; }
            });
            status.AddButton("Reintentar guardado de preferencias", () => { ModSettings.Save(); if (label != null) label.text = ModSettings.LastSaveError ?? "Preferencias guardadas"; });
            Note(status, ModSettings.MigrationNotice);

            var pipeline = helper.AddGroup("Procesado");
            pipeline.AddCheckbox("Activar NeuralFX al cargar una ciudad", ModSettings.PipelineEnabled, value => {
                ModSettings.PipelineEnabled = value;
                if (NeuralFXManager.Instance != null) NeuralFXManager.Instance.SetPipelineEnabled(value); else ModSettings.Save();
            });
            pipeline.AddCheckbox("Avisar si otro mod aplica antialiasing", ModSettings.WarnOnAaConflict, value => { ModSettings.WarnOnAaConflict = value; ModSettings.Save(); });
            Note(pipeline, "Apagarlo detiene el feeder y el CAS propios. ReShade conserva el resto de efectos y el menú nunca ejecuta inferencia.");

            var access = helper.AddGroup("Acceso");
            access.AddCheckbox("Atajo Ctrl + Alt + N", ModSettings.EnableHotkey, value => { ModSettings.EnableHotkey = value; ModSettings.Save(); });
            access.AddCheckbox("Botón en Unified UI (se aplica al cargar ciudad)", ModSettings.EnableUui, value => { ModSettings.EnableUui = value; ModSettings.Save(); });
            access.AddCheckbox("Botón NEURALFX en el menú principal (se aplica al reiniciar)", ModSettings.EnableMainMenuButton, value => { ModSettings.EnableMainMenuButton = value; ModSettings.Save(); });
            var action = Note(access, "");
            access.AddButton("Abrir el panel en ciudad", () => NeuralFXManager.ToggleWindow());
            access.AddButton("Abrir NeuralFX Hub", () => { if (action != null) action.text = UI.HubLauncher.Open(); else UI.HubLauncher.Open(); });
            access.AddButton("Guardar informe de sesión", () => Run(action, () => {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuralFX/Diagnostics"); Directory.CreateDirectory(root);
                string file = Path.Combine(root, "session-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".txt");
                var manager = NeuralFXManager.Instance;
                File.WriteAllText(file, "NeuralFX bridge build 4 / ABI 3 / IPC 4\n" + (manager != null ? SessionViewState.Summary(manager.CurrentFrame) + "\n" + SessionViewState.Details(manager.CurrentFrame) : "Sin ciudad") + "\n\n" + Rendering.RenderStageProbe.Export());
                return "Diagnóstico guardado: " + file;
            }));

            var experimental = helper.AddGroup("Pruebas experimentales");
            UIComponent motion = null, invX = null, invY = null, terrain = null;
            experimental.AddCheckbox("Permitir capacidades sin validar", ModSettings.ExperimentalOptIn, value => {
                ModSettings.ExperimentalOptIn = value; ModSettings.Save();
                if (motion != null) motion.isEnabled = value;
                if (invX != null) invX.isEnabled = value && ModSettings.EnableNativeMotionVectors;
                if (invY != null) invY.isEnabled = value && ModSettings.EnableNativeMotionVectors;
                if (terrain != null) terrain.isEnabled = value && ModSettings.EnableNativeMotionVectors;
            });
            motion = experimental.AddCheckbox("Preferir vectores de movimiento de Unity", ModSettings.EnableNativeMotionVectors, value => {
                ModSettings.EnableNativeMotionVectors = value; ModSettings.Save();
                if (invX != null) invX.isEnabled = ModSettings.ExperimentalOptIn && value;
                if (invY != null) invY.isEnabled = ModSettings.ExperimentalOptIn && value;
                if (terrain != null) terrain.isEnabled = ModSettings.ExperimentalOptIn && value;
            }) as UIComponent;
            if (motion != null) motion.isEnabled = ModSettings.ExperimentalOptIn;
            invY = experimental.AddCheckbox("Invertir signo vertical (Y) de vectores", ModSettings.InvertMotionY, value => { ModSettings.InvertMotionY = value; ModSettings.Save(); }) as UIComponent;
            if (invY != null) invY.isEnabled = ModSettings.ExperimentalOptIn && ModSettings.EnableNativeMotionVectors;
            invX = experimental.AddCheckbox("Invertir signo horizontal (X) de vectores", ModSettings.InvertMotionX, value => { ModSettings.InvertMotionX = value; ModSettings.Save(); }) as UIComponent;
            if (invX != null) invX.isEnabled = ModSettings.ExperimentalOptIn && ModSettings.EnableNativeMotionVectors;
            Note(experimental, "Por defecto ambos signos van invertidos (-ancho, -alto) según el estándar DLSS; estas casillas permiten invertir la orientación de cada eje para pruebas.");
            terrain = experimental.AddCheckbox("Candidato: corregir profundidad del terreno", ModSettings.EnableTerrainDepthCandidate, value => {
                ModSettings.EnableTerrainDepthCandidate = value; ModSettings.Save();
            }) as UIComponent;
            if (terrain != null) terrain.isEnabled = ModSettings.ExperimentalOptIn && ModSettings.EnableNativeMotionVectors;
            Note(experimental, "Puedes activarlo y desactivarlo durante la partida. Requiere vectores de Unity y trabajo al 100%. Ajusta la profundidad cuando su tamaño coincide con el de la cámara; cada cambio reinicia el historial. Su mejora visual está pendiente de confirmar.");
            Note(experimental, "Jitter de cámara, SR interno y aislamiento de la UI siguen sin implementar. No aparecen aquí porque no harían nada.");
        }
        private static void Run(UILabel label, Func<string> action) { try { if (label != null) label.text = action(); else action(); } catch (Exception ex) { if (label != null) label.text = ex.Message; UnityEngine.Debug.LogWarning("[NeuralFX] " + ex.Message); } }
        private static UILabel Note(UIHelperBase group, string text)
        {
            var helper = group as UIHelper; var panel = helper != null ? helper.self as UIPanel : null; if (panel == null) return null;
            var label = panel.AddUIComponent<UILabel>(); label.autoSize = false; label.autoHeight = true; label.width = Mathf.Max(200, panel.width - 24);
            label.wordWrap = true; label.textScale = .8f; label.textColor = new Color32(147, 162, 174, 255); label.text = text; return label;
        }
    }
    internal sealed class OptionsStatus : MonoBehaviour
    {
        public UILabel Label;
        private float _next;
        public void Update()
        {
            if (Label == null || Time.unscaledTime < _next) return; _next = Time.unscaledTime + .5f;
            var manager = NeuralFXManager.Instance;
            string value = manager != null ? SessionViewState.Summary(manager.CurrentFrame) + "\n" + manager.BridgeReason : "Sin ciudad. La instalación y la compatibilidad se revisan en el Hub.";
            if (!string.IsNullOrEmpty(ModSettings.LastSaveError)) value += "\nGuardado pendiente: " + ModSettings.LastSaveError;
            if (Label.text != value) Label.text = value;
        }
    }
}
