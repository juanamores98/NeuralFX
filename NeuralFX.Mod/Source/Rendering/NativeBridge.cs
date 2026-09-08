using System;
using System.Runtime.InteropServices;

namespace NeuralFX.Rendering
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeFrame
    {
        public int Size, Version, Frame, ResetSerial;
        public int Width, Height;
        public float JitterX, JitterY;
        public ulong MotionVectorsPtr;
        public float MvScaleX, MvScaleY;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeStatus { public int Size, Version, Capabilities, Frame, ResetSerial, Evaluations, Width, Height, Result; public uint AgeMs; }
    internal sealed class NativeBridge
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SubmitDelegate(ref NativeFrame data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatusDelegate(ref NativeStatus data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EventDelegate();
        private SubmitDelegate _submit;
        private StatusDelegate _status;
        public IntPtr RenderEvent { get; private set; }
        public bool Connected { get { return _submit != null && _status != null && RenderEvent != IntPtr.Zero; } }
        public bool Connect()
        {
            IntPtr module = NativeInterop.GetModuleHandle("dlss5-feed.addon64");
            if (module == IntPtr.Zero) return false;
            IntPtr submit = NativeInterop.GetProcAddress(module, "NeuralFX_SubmitFrame"), status = NativeInterop.GetProcAddress(module, "NeuralFX_GetStatus"), renderEvent = NativeInterop.GetProcAddress(module, "NeuralFX_GetRenderEvent");
            if (submit == IntPtr.Zero || status == IntPtr.Zero || renderEvent == IntPtr.Zero) return false;
            _submit = (SubmitDelegate)Marshal.GetDelegateForFunctionPointer(submit, typeof(SubmitDelegate));
            _status = (StatusDelegate)Marshal.GetDelegateForFunctionPointer(status, typeof(StatusDelegate));
            RenderEvent = ((EventDelegate)Marshal.GetDelegateForFunctionPointer(renderEvent, typeof(EventDelegate)))();
            return Connected;
        }
        public bool Submit(ref NativeFrame frame) { return Connected && _submit(ref frame) == 1; }
        public NativeStatus ReadStatus()
        {
            var data = new NativeStatus { Size = 40 };
            if (!Connected || _status(ref data) != 1 || data.Version != 1) return new NativeStatus();
            return data;
        }
    }
}
