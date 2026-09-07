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
            BuildStatusGroup(helper);
            BuildPreferencesGroup(helper);
            BuildExternalToolsGroup(helper);
        }

        private static void BuildStatusGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Estado del Pipeline & Diagnóstico In-Game");

            bool dxgi = NativeInterop.IsModuleLoaded("dxgi.dll");
            bool feeder = NativeInterop.IsModuleLoaded("dlss5-feed.addon64");

            string statusText = string.Format(
                "DXGI Hook (dxgi.dll): {0}  |  DLSS5-Feeder (dlss5-feed.addon64): {1}",
                dxgi ? "ACTIVO" : "NO DETECTADO",
                feeder ? "ACTIVO" : "NO DETECTADO");

            group.AddButton(statusText, () => { });

            group.AddButton("Reaplicar Hooks de Cámara (Depth & Motion Vectors)", () =>
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

            group.AddButton("Abrir / Cerrar HUD de Telemetría (Ctrl + Alt + N)", () =>
            {
                NeuralFXManager.ToggleWindow();
            });
        }

        private static void BuildPreferencesGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Preferencias de NeuralFX");

            group.AddCheckbox("Forzar Depth & Motion Vectors al cargar partidas", ModSettings.ForceMotionVectorsOnLoad, sel =>
            {
                ModSettings.ForceMotionVectorsOnLoad = sel;
                ModSettings.Save();
                if (sel && NeuralFXManager.Instance != null)
                {
                    NeuralFXManager.Instance.EnsureCameraModes();
                }
            });

            group.AddCheckbox("Alerta en pantalla de colisiones con TAA / SMAA legacy", ModSettings.WarnOnAaConflict, sel =>
            {
                ModSettings.WarnOnAaConflict = sel;
                ModSettings.Save();
            });

            group.AddCheckbox("Habilitar atajo global de teclado (Ctrl + Alt + N)", ModSettings.EnableHotkey, sel =>
            {
                ModSettings.EnableHotkey = sel;
                ModSettings.Save();
            });

            group.AddCheckbox("Integrar botón en barra Unified UI (UUI)", ModSettings.EnableUui, sel =>
            {
                ModSettings.EnableUui = sel;
                ModSettings.Save();
            });
        }

        private static void BuildExternalToolsGroup(UIHelperBase helper)
        {
            var group = helper.AddGroup("Herramientas de Inyección & ReShade");

            group.AddButton("Abrir Carpeta de Cities: Skylines (Logs y Config)", () =>
            {
                try
                {
                    string dir = Directory.GetCurrentDirectory();
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[NeuralFX] No se pudo abrir la carpeta del juego: " + ex.Message);
                }
            });

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

            group.AddButton("Atajos: [Ctrl+Alt+N] HUD Telemetría | [Home] ReShade Overlay", () => { });
        }
    }
}
