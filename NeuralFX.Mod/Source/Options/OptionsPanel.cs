using System;
using System.Diagnostics;
using System.IO;
using ICities;
using NeuralFX.Config;
using UnityEngine;

namespace NeuralFX.Options
{
    internal static class OptionsPanel
    {
        internal static void Build(UIHelperBase helper)
        {
            BuildStatusSection(helper);
            BuildActionsSection(helper);
            BuildPreferencesSection(helper);
            BuildToolsSection(helper);
        }

        private static void BuildStatusSection(UIHelperBase helper)
        {
            var group = helper.AddGroup("Diagnóstico y Estado de NeuralFX");

            bool dxgi = NativeInterop.IsModuleLoaded("dxgi.dll");
            bool feeder = NativeInterop.IsModuleLoaded("dlss5-feed.addon64");
            bool pipelineActive = dxgi && feeder;

            bool cameraReady = false;
            var cam = Camera.main;
            if (cam != null)
            {
                var wanted = DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
                cameraReady = (cam.depthTextureMode & wanted) == wanted;
            }

            // Status 1: Pipeline
            OptionUI.AddStatusRow(
                group,
                "Pipeline Gráfico (DLSS / ReShade):",
                pipelineActive ? "ACTIVO / ENLACE ESTABLE" : (dxgi ? "PARCIAL (Solo DXGI)" : "NO INYECTADO EN ESTA SESIÓN"),
                pipelineActive,
                pipelineActive
                    ? "Los ganchos DX11 están transfiriendo los buffers de render al runtime de DLSS correctamente."
                    : "Cierra el juego y usa NeuralFX Hub para inyectar el pipeline antes de iniciar Cities: Skylines.");

            // Status 2: Motion Vectors
            OptionUI.AddStatusRow(
                group,
                "Vectores de Movimiento:",
                cameraReady ? "GENERÁNDOSE CORRECTAMENTE" : "EN ESPERA (Cargar mapa)",
                cameraReady,
                "La cámara principal tiene activos DepthTextureMode.Depth y MotionVectors para alimentar el escalador.");

            // Status 3: Temporal Anti-Aliasing check
            bool hasConflict = false;
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
                        break;
                    }
                }
            }

            OptionUI.AddStatusRow(
                group,
                "Compatibilidad Temporal:",
                !hasConflict ? "ÓPTIMA (Sin interferencias)" : "ADVERTENCIA (TAA/SMAA detectado)",
                !hasConflict,
                !hasConflict
                    ? "No hay filtros de suavizado temporal ajenos compitiendo con la reconstrucción de DLSS."
                    : "Hay un mod de suavizado temporal activo en la cámara. Desactívalo para prevenir artefactos de ghosting.");
        }

        private static void BuildActionsSection(UIHelperBase helper)
        {
            var group = helper.AddGroup("Acciones Rápidas");

            group.AddButton("Abrir HUD de Telemetría In-Game", () =>
            {
                NeuralFXManager.ToggleWindow();
            });
            OptionUI.AddHint(group, "Abre el monitor flotante. Atajo: Ctrl + Alt + N, o pulsa el icono de NeuralFX en la barra Unified UI.");

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
            OptionUI.AddHint(group, "Fuerza la activación de Depth & Motion Vectors si otro mod gráfico reseteó la cámara.");
        }

        private static void BuildPreferencesSection(UIHelperBase helper)
        {
            var group = helper.AddGroup("Preferencias");

            group.AddCheckbox("Mostrar botón en la barra flotante Unified UI (UUI)", ModSettings.EnableUui, sel =>
            {
                ModSettings.EnableUui = sel;
                ModSettings.Save();
            });

            group.AddCheckbox("Permitir atajo global de teclado (Ctrl + Alt + N)", ModSettings.EnableHotkey, sel =>
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

        private static void BuildToolsSection(UIHelperBase helper)
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
                        UnityEngine.Debug.LogWarning("[NeuralFX] No se encontró NeuralFX.Hub.exe en: " + hubPath);
                    }
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[NeuralFX] Error iniciando Hub: " + ex.Message);
                }
            });
            OptionUI.AddHint(group, "Abre el panel de diagnóstico de hardware, gestor de binarios NVIDIA y rollback limpio.");

            group.AddButton("Abrir Carpeta de Instalación de Cities: Skylines", () =>
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
            OptionUI.AddHint(group, "Ubicación de Cities.exe, dxgi.dll y los archivos de registro en vivo (ReShade.log y dlss5-feed.log).");

            OptionUI.AddLabel(group, "Atajo adicional: Pulsa la tecla [Home] durante el juego para calibrar el filtro de nitidez AMD CAS en ReShade.", new Color32(78, 201, 176, 255), 0.80f);
        }
    }
}
