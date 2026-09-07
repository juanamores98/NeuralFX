using ICities;
using UnityEngine;

namespace NeuralFX
{
    public class LoadingExtension : LoadingExtensionBase
    {
        private GameObject _managerObject;

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            if (_managerObject == null)
            {
                _managerObject = new GameObject("NeuralFX_Manager");
                _managerObject.AddComponent<NeuralFXManager>();
                Object.DontDestroyOnLoad(_managerObject);
            }
            else
            {
                var mgr = _managerObject.GetComponent<NeuralFXManager>();
                if (mgr != null)
                {
                    mgr.EnsureCameraModes();
                }
            }
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();
        }
    }
}
