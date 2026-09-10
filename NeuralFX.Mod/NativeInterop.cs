using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NeuralFX
{
    public static class NativeInterop
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] public static extern IntPtr GetModuleHandleW(string moduleName);
        public static IntPtr GetModuleHandle(string name) { return GetModuleHandleW(name); }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder path, int count);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] public static extern int GetCurrentProcessId();
        // La carpeta del juego se toma del propio ejecutable en ejecucion: BaseDirectory del
        // AppDomain no siempre apunta ahi bajo Mono, y con eso ReShade, el feeder y el consumidor
        // se daban por no cargados aunque los tres estuvieran dentro del proceso.
        public static string GetGameDirectory()
        {
            try
            {
                var path = new StringBuilder(32768);
                uint length = GetModuleFileNameW(IntPtr.Zero, path, path.Capacity);
                if (length != 0 && length < path.Capacity)
                {
                    string directory = Path.GetDirectoryName(Path.GetFullPath(path.ToString()));
                    if (!string.IsNullOrEmpty(directory)) return Normalize(directory);
                }
            }
            catch { }
            return Normalize(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory));
        }
        private static string Normalize(string path) { return path.Replace('/', '\\').TrimEnd('\\'); }
        public static bool IsReShadeHooked()
        {
            IntPtr handle = LocalModule("dxgi.dll");
            return handle != IntPtr.Zero && GetProcAddress(handle, "ReShadeRegisterAddon") != IntPtr.Zero;
        }
        public static bool IsFeederAddonLoaded() { return LocalModule("dlss5-feed.addon64") != IntPtr.Zero; }
        public static bool IsModuleLoaded(string name) { return name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase) ? IsReShadeHooked() : LocalModule(name) != IntPtr.Zero; }
        private static IntPtr LocalModule(string name)
        {
            try
            {
                IntPtr handle = GetModuleHandle(name);
                if (handle == IntPtr.Zero) return IntPtr.Zero;
                var path = new StringBuilder(32768);
                uint length = GetModuleFileNameW(handle, path, path.Capacity);
                if (length == 0 || length >= path.Capacity) return IntPtr.Zero;
                string actual = Normalize(Path.GetFullPath(path.ToString()));
                string expected = Normalize(Path.Combine(GetGameDirectory(), name));
                return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase) ? handle : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }
    }
}
