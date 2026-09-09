using System;
using System.Runtime.InteropServices;

namespace NeuralFX.Rendering
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeFrame
    {
        public uint Size, Version, Frame, ResetSerial, Width, Height;
        public float JitterX, JitterY;
        public uint Camera, Epoch, Flags, MotionHandle;
        public float MvScaleX, MvScaleY;
        public uint Magic, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeStatus { public int Size, Version, Capabilities, Frame, ResetSerial, Evaluations, Width, Height, Result; public uint AgeMs; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeCapabilities { public uint Size, Version, Build, Supported, MaxDimension, FrameBytes, ResultBytes, Reserved; }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeResult
    {
        public uint Size, Version, Frame, Camera, Epoch, Recorded, Submitted, Completed, OutputCommitted;
        public uint ResetSerial, MotionProvider, BypassReason, NrConfirmed, UiIsolated;
        public int Error;
        public uint WorkWidth, WorkHeight, AdapterLow;
        public int AdapterHigh;
        public uint Reserved;
    }
    internal sealed class NativeBridge
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SubmitDelegate(ref NativeFrame data, uint bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StatusDelegate(ref NativeStatus data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CapabilitiesDelegate(ref NativeCapabilities data, uint bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ResultDelegate(ref NativeResult data, uint bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr EventDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnableDelegate(uint enabled);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint RegisterDelegate(IntPtr texture, uint camera, uint epoch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReserveDelegate(uint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseDelegate(uint handle);
        private SubmitDelegate _submit; private StatusDelegate _status; private ResultDelegate _result;
        private EnableDelegate _enable; private RegisterDelegate _register; private ReserveDelegate _reserve; private ReleaseDelegate _release;
        public NativeCapabilities Capabilities { get; private set; }
        public string Reason { get; private set; }
        public IntPtr RenderEvent { get; private set; }
        public bool Connected { get { return _submit != null && _status != null && _enable != null && RenderEvent != IntPtr.Zero; } }
        private static Delegate Resolve(IntPtr module, string name, Type type)
        {
            IntPtr address = NativeInterop.GetProcAddress(module, name);
            return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(address, type);
        }
        public bool Connect()
        {
            IntPtr module = NativeInterop.GetModuleHandle("dlss5-feed.addon64");
            if (module == IntPtr.Zero) { Reason = "Puente sin cargar"; return false; }
            var probe = (CapabilitiesDelegate)Resolve(module, "NeuralFX_GetCapabilities", typeof(CapabilitiesDelegate));
            var caps = new NativeCapabilities();
            if (probe == null || probe(ref caps, 32) != 1 || caps.Version != 3 || caps.Build != 4 || caps.FrameBytes != 64 || caps.ResultBytes != 80 || (caps.Supported & 9) != 9)
            { Reason = "Puente incompatible; actualiza mod y Hub juntos y reinicia el juego"; return false; }
            var callback = (EventDelegate)Resolve(module, "NeuralFX_GetRenderEvent", typeof(EventDelegate));
            _submit = (SubmitDelegate)Resolve(module, "NeuralFX_SubmitFrameV3", typeof(SubmitDelegate));
            _status = (StatusDelegate)Resolve(module, "NeuralFX_GetStatus", typeof(StatusDelegate));
            _result = (ResultDelegate)Resolve(module, "NeuralFX_GetFrameResult", typeof(ResultDelegate));
            _enable = (EnableDelegate)Resolve(module, "NeuralFX_SetEnabled", typeof(EnableDelegate));
            _register = (RegisterDelegate)Resolve(module, "NeuralFX_RegisterMotion", typeof(RegisterDelegate));
            _reserve = (ReserveDelegate)Resolve(module, "NeuralFX_ReserveMotion", typeof(ReserveDelegate));
            _release = (ReleaseDelegate)Resolve(module, "NeuralFX_ReleaseMotion", typeof(ReleaseDelegate));
            RenderEvent = callback != null ? callback() : IntPtr.Zero;
            Capabilities = caps; Reason = Connected ? "Puente ABI 3 negociado; NR sin confirmar" : "Exports incompletos";
            return Connected;
        }
        public bool SetEnabled(bool enabled) { return Connected && _enable(enabled ? 1u : 0u) == 1; }
        public uint RegisterMotion(IntPtr texture, uint camera, uint epoch) { return Connected && _register != null ? _register(texture, camera, epoch) : 0; }
        public bool ReserveMotion(uint handle) { return Connected && _reserve != null && _reserve(handle) == 1; }
        public void ReleaseMotion(uint handle) { if (_release != null && handle != 0) _release(handle); }
        public bool Submit(ref NativeFrame frame) { return Connected && _submit(ref frame, 64) == 1; }
        public NativeStatus ReadStatus()
        {
            var data = new NativeStatus { Size = 40 };
            if (!Connected || _status(ref data) != 1 || data.Version != 1) return new NativeStatus();
            return data;
        }
        public NativeResult ReadResult()
        {
            var data = new NativeResult();
            return Connected && _result != null && _result(ref data, 80) == 1 && data.Version == 3 ? data : new NativeResult();
        }
    }
}
