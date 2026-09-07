using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NeuralFX
{
    public static class NativeInterop
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint GetModuleFileName(IntPtr hModule, [Out] StringBuilder lpFilename, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        public static string GetGameDirectory()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir) && File.Exists(Path.Combine(baseDir, "Cities.exe")))
                {
                    return baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                string currentDir = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(currentDir) && File.Exists(Path.Combine(currentDir, "Cities.exe")))
                {
                    return currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }

                return baseDir ?? currentDir ?? string.Empty;
            }
            catch
            {
                return Directory.GetCurrentDirectory();
            }
        }

        public static bool IsReShadeHooked()
        {
            try
            {
                string gameDir = GetGameDirectory();
                if (string.IsNullOrEmpty(gameDir)) return false;

                string localDxgi = Path.Combine(gameDir, "dxgi.dll");
                // 1. Si dxgi.dll NO existe físicamente en la carpeta del juego, ReShade no está instalado
                if (!File.Exists(localDxgi))
                {
                    return false;
                }

                IntPtr handle = GetModuleHandle("dxgi.dll");
                if (handle == IntPtr.Zero)
                {
                    return false;
                }

                // 2. Verificar que el módulo cargado provenga de la carpeta del juego y no de C:\Windows\System32
                StringBuilder sb = new StringBuilder(512);
                uint len = GetModuleFileName(handle, sb, sb.Capacity);
                if (len > 0)
                {
                    string loadedPath = sb.ToString();
                    if (!loadedPath.StartsWith(gameDir, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                // 3. Verificar export de ReShade Addon
                if (GetProcAddress(handle, "ReShadeRegisterAddon") != IntPtr.Zero ||
                    GetProcAddress(handle, "ReShadeUnregisterAddon") != IntPtr.Zero)
                {
                    return true;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsFeederAddonLoaded()
        {
            try
            {
                string gameDir = GetGameDirectory();
                if (string.IsNullOrEmpty(gameDir)) return false;

                string localFeeder = Path.Combine(gameDir, "dlss5-feed.addon64");
                if (!File.Exists(localFeeder))
                {
                    return false;
                }

                IntPtr handle = GetModuleHandle("dlss5-feed.addon64");
                return handle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsModuleLoaded(string moduleName)
        {
            if (string.Equals(moduleName, "dxgi.dll", StringComparison.OrdinalIgnoreCase))
            {
                return IsReShadeHooked();
            }
            if (string.Equals(moduleName, "dlss5-feed.addon64", StringComparison.OrdinalIgnoreCase))
            {
                return IsFeederAddonLoaded();
            }

            try
            {
                IntPtr handle = GetModuleHandle(moduleName);
                return handle != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }
    }
}
