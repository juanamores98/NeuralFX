using System;
using System.Diagnostics;
using System.IO;
using ColossalFramework.UI;
using ICities;
using NeuralFX.Config;
using UnityEngine;

namespace NeuralFX.Options
{
    internal static class OptionsPanel
    {
        public static void Build(UIHelperBase helper)
        {
            BuildStatusGroup(helper);
            BuildActionsGroup(helper);
            BuildPreferencesGroup(helper);
            BuildToolsGroup(helper);
        }

        private static void BuildStatusGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Estado del Pipeline DLSS 5");

            bool dxgi = NativeInterop.IsModuleLoaded("dxgi.dll");
            bool feeder = NativeInterop.IsModuleLoaded("dlss5-feed.addon64");
            bool pipelineActive = dxgi && feeder;

            // Pipeline Status
            if (pipelineActive)
            {
                AddStatusLabel(group, "✔ Pipeline Gráfico: ACTIVO (ReShade y DLSS5-Feeder conectados)", new Color32(78, 201, 176, 255));
                AddNoteLabel(group, "DirectX 11 está comunicando los buffers de render al runtime neural correctamente.");
            }
            else if (dxgi)
            {
                AddStatusLabel(group, "⚠ Pipeline Gráfico: PARCIAL (Solo ReShade dxgi.dll detectado)", new Color32(245, 166, 35, 255));
                AddNoteLabel(group, "El addon DLSS5-Feeder no está activo. Abre NeuralFX Hub para inyectar el pipeline completo.");
            }
            else
            {
                AddStatusLabel(group, "○ Pipeline Gráfico: NO INYECTADO (Modo Vanilla Limpio)", new Color32(180, 190, 200, 255));
                AddNoteLabel(group, "El juego se encuentra en estado original vanilla sin hooks inyectados. Abre NeuralFX Hub si deseas instalar el pipeline DLSS 5.");
            }

            AddSpacing(group, 6f);

            // Camera / Motion Vectors Status
            var cam = Camera.main;
            if (cam != null)
            {
                var wanted = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                bool cameraReady = (cam.depthTextureMode & wanted) == wanted;

                if (cameraReady)
                {
                    AddStatusLabel(group, "✔ Vectores de Movimiento: GENERÁNDOSE CORRECTAMENTE", new Color32(78, 201, 176, 255));
                    AddNoteLabel(group, "La cámara principal tiene activos DepthTextureMode.Depth y MotionVectors.");
                }
                else
                {
                    AddStatusLabel(group, "⚠ Vectores de Movimiento: PENDIENTE EN CÁMARA", new Color32(245, 166, 35, 255));
                    AddNoteLabel(group, "Pulsa 'Reaplicar Configuración de Cámara' o carga una partida para forzar la activación.");
                }
            }
            else
            {
                AddStatusLabel(group, "ℹ Vectores de Movimiento: EN ESPERA", new Color32(180, 190, 200, 255));
                AddNoteLabel(group, "Se activarán automáticamente en la cámara principal en cuanto cargues un mapa.");
            }

            AddSpacing(group, 6f);

            // Anti-Aliasing Collision Status
            bool hasConflict = false;
            string conflictName = null;
            if (cam != null)
            {
                foreach (var comp in cam.GetComponents<MonoBehaviour>())
                {
                    if (comp == null || !comp.enabled) continue;
                    string name = comp.GetType().Name;
                    if (name.IndexOf("Antialiasing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("SMAA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("TAA", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasConflict = true;
                        conflictName = name;
                        break;
                    }
                }
            }

            if (hasConflict)
            {
                AddStatusLabel(group, "▲ Anti-Aliasing: CONFLICTO DETECTADO (" + conflictName + ")", new Color32(244, 71, 71, 255));
                AddNoteLabel(group, "Desactiva el suavizado temporal del otro mod para evitar efecto de ghosting con DLSS.");
            }
            else
            {
                AddStatusLabel(group, "✔ Anti-Aliasing: COMPATIBILIDAD ÓPTIMA", new Color32(78, 201, 176, 255));
                AddNoteLabel(group, "No se detectaron filtros temporales ajenos interfiriendo con la reconstrucción.");
            }
        }

        private static void BuildActionsGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Acciones Rápidas");

            group.AddButton("Abrir Panel de Telemetría In-Game", () =>
            {
                NeuralFXManager.ToggleWindow();
            });
            AddNoteLabel(group, "Abre la ventana flotante en el juego (Atajo: Ctrl + Alt + N, o pulsa el icono de NeuralFX en Unified UI).");

            AddSpacing(group, 4f);

            group.AddButton("Reaplicar Configuración de Cámara", () =>
            {
                if (NeuralFXManager.Instance != null)
                {
                    NeuralFXManager.Instance.EnsureCameraModes();
                }
                else
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        cam.depthTextureMode |= DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                    }
                }
            });
            AddNoteLabel(group, "Vuelve a forzar los modos de profundidad y vectores si otro mod restableció la cámara.");
        }

        private static void BuildPreferencesGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Preferencias");

            group.AddCheckbox("Mostrar botón de NeuralFX en la barra Unified UI (UUI)", ModSettings.EnableUui, sel =>
            {
                ModSettings.EnableUui = sel;
                ModSettings.Save();
            });

            group.AddCheckbox("Habilitar atajo global de teclado (Ctrl + Alt + N)", ModSettings.EnableHotkey, sel =>
            {
                ModSettings.EnableHotkey = sel;
                ModSettings.Save();
            });

            group.AddCheckbox("Forzar Depth & Motion Vectors automáticamente al cargar partidas", ModSettings.ForceMotionVectorsOnLoad, sel =>
            {
                ModSettings.ForceMotionVectorsOnLoad = sel;
                ModSettings.Save();
            });

            group.AddCheckbox("Avisar en pantalla si otro mod causa conflicto de Anti-Aliasing", ModSettings.WarnOnAaConflict, sel =>
            {
                ModSettings.WarnOnAaConflict = sel;
                ModSettings.Save();
            });
        }

        private static void BuildToolsGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Herramientas y Mantenimiento");

            group.AddButton("Lanzar NeuralFX Hub (Escritorio)", () =>
            {
                try
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string hubPath = Path.Combine(localAppData, Path.Combine("Colossal Order", Path.Combine("Cities_Skylines", Path.Combine("Addons", Path.Combine("Mods", Path.Combine("NeuralFX", Path.Combine("Hub", "NeuralFX.Hub.exe")))))));

                    if (File.Exists(hubPath))
                    {
                        Process.Start(new ProcessStartInfo(hubPath) { UseShellExecute = true });
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("[NeuralFX] No se encontro NeuralFX.Hub.exe en: " + hubPath);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[NeuralFX] Error iniciando Hub: " + ex.Message);
                }
            });
            AddNoteLabel(group, "Abre la herramienta para diagnósticos de hardware, VRAM de 64 bits y rollback limpio a vanilla.");

            AddSpacing(group, 4f);

            group.AddButton("Abrir Carpeta de Cities: Skylines", () =>
            {
                try
                {
                    string dir = Directory.GetCurrentDirectory();
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[NeuralFX] Error abriendo carpeta: " + ex.Message);
                }
            });
            AddNoteLabel(group, "Ubicación de Cities.exe, dxgi.dll y registros de depuración (ReShade.log y dlss5-feed.log).");

            AddSpacing(group, 6f);
            AddStatusLabel(group, "ℹ Atajo nativo: Presiona [Home] en el juego para calibrar ReShade y nitidez AMD CAS.", new Color32(78, 201, 176, 255));
        }

        private static void AddStatusLabel(UIHelperBase group, string text, Color32 color)
        {
            if (group is UIHelper helper && helper.self is UIPanel panel)
            {
                var label = panel.AddUIComponent<UILabel>();
                label.autoSize = false;
                label.autoHeight = true;
                label.width = 700f;
                label.wordWrap = true;
                label.textScale = 0.88f;
                label.textColor = color;
                label.text = text;
            }
        }

        private static void AddNoteLabel(UIHelperBase group, string text)
        {
            if (group is UIHelper helper && helper.self is UIPanel panel)
            {
                var label = panel.AddUIComponent<UILabel>();
                label.autoSize = false;
                label.autoHeight = true;
                label.width = 700f;
                label.wordWrap = true;
                label.textScale = 0.78f;
                label.textColor = new Color32(160, 160, 160, 255);
                label.text = text;
            }
        }

        private static void AddSpacing(UIHelperBase group, float height)
        {
            if (group is UIHelper helper && helper.self is UIPanel panel)
            {
                var spacer = panel.AddUIComponent<UIPanel>();
                spacer.size = new Vector2(700f, height);
                spacer.autoLayout = false;
            }
        }
    }
}
