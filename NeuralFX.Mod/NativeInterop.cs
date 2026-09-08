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
        public static string GetGameDirectory() { return Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/'); }
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
                string expected = Path.GetFullPath(Path.Combine(GetGameDirectory(), name));
                return string.Equals(expected, Path.GetFullPath(path.ToString()), StringComparison.OrdinalIgnoreCase) ? handle : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }
    }
}
