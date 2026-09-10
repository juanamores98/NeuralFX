using ICities;
using NeuralFX.Config;
using NeuralFX.Options;
using NeuralFX.UI;

namespace NeuralFX
{
    public class ModInfo : IUserMod
    {
        public string Name => "NeuralFX - DLSS 5 & Telemetría";

        public string Description => "Soporte de Depth/Motion Vectors para DLSS 5, detección de módulos nativos, panel de telemetría y soporte UUI.";

        public void OnEnabled()
        {
            ModSettings.Load();
            if (ModSettings.EnableMainMenuButton) MainMenuButton.Install();
        }

        public void OnSettingsUI(UIHelperBase helper)
        {
            ModSettings.Load();
            OptionsPanel.Build(helper);
        }
    }
}
