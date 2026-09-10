using System;
using System.Diagnostics;
using System.IO;

namespace NeuralFX.UI
{
    // El Hub vive fuera de Addons/Mods a propósito: CS1 escanea esa carpeta y trata de cargar
    // cada DLL como ensamblado del juego. Abrirlo desde aquí evita que el usuario tenga que
    // buscar el ejecutable en AppData.
    internal static class HubLauncher
    {
        public static string ExecutablePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NeuralFX/Hub/NeuralFX.Hub.exe");
            }
        }

        public static bool Installed { get { return File.Exists(ExecutablePath); } }

        /// <summary>Abre el Hub y devuelve el mensaje a mostrar. Nunca lanza.</summary>
        public static string Open()
        {
            try
            {
                string exe = ExecutablePath;
                if (!File.Exists(exe))
                    return "Hub no instalado. Ejecuta Install-NeuralFX.ps1 desde el paquete extraído.";
                foreach (var running in Process.GetProcessesByName("NeuralFX.Hub"))
                    using (running) return "El Hub ya está abierto.";
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                return "Hub abierto.";
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[NeuralFX] Hub: " + ex.Message);
                return "No se pudo abrir el Hub: " + ex.Message;
            }
        }
    }
}
