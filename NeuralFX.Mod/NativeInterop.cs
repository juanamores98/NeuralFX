using System;
using System.Runtime.InteropServices;

namespace NeuralFX
{
    public static class NativeInterop
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        public static bool IsModuleLoaded(string moduleName)
        {
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
