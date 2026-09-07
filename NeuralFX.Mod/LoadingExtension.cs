using ICities;
using NeuralFX.Config;
using NeuralFX.UI;
using UnityEngine;

namespace NeuralFX
{
    public class LoadingExtension : LoadingExtensionBase
    {
        private GameObject _managerObject;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            ModSettings.Load();

            if (_managerObject == null)
            {
                _managerObject = new GameObject("NeuralFX_Manager");
                _managerObject.AddComponent<NeuralFXManager>();
                Object.DontDestroyOnLoad(_managerObject);
            }
            else
            {
                var mgr = _managerObject.GetComponent<NeuralFXManager>();
                if (mgr != null && ModSettings.ForceMotionVectorsOnLoad)
                {
                    mgr.EnsureCameraModes();
                }
            }

            if (ModSettings.EnableUui)
            {
                UuiButton.Register(
                    "NeuralFX",
                    "NeuralFX - DLSS 5 & Telemetría (Ctrl+Alt+N)",
                    TrayIcon.Make(),
                    show => NeuralFXManager.ToggleWindow());
            }
        }

        public override void OnLevelUnloading()
        {
            UuiButton.Unregister();
            base.OnLevelUnloading();
        }
    }
}
