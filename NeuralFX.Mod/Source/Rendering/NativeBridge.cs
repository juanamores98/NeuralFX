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
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeControls { public uint Size, Version, Revision, Mask; public int WorkPercent; public float Sharpness; }
    // Salud del pipeline: lo que el feeder mide cada 600 frames. Opcional a propósito —
    // un puente que no la exporte sigue siendo válido y el panel simplemente no la muestra.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeHealth
    {
        public uint Size, Version, ProbeFrame;
        public float MvMeanPx, MvMaxPx;
        public uint MvNonZeroPct;
        public float DepthMin, DepthMax, DepthMean, DepthVariance;
        public uint DepthFinitePct, DepthFlatMoving;
        public float FeedCpuMs, FeedGpuMs, FrameIntervalMs;
        public uint Stalls;
    }
    // Informe del ultimo intento de registro de movimiento. Opcional: un puente anterior no lo
    // exporta y el mod sigue funcionando sin el, solo que sin poder decir por que fallo.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct NativeRegistrationReport
    {
        public uint Size, Version, RequestSerial, Stage, Reason;
        public int HResult;
        public uint Camera, Epoch, Width, Height, Format;
        public uint MipLevels, ArraySize, SampleCount, SampleQuality;
        public uint BindFlags, MiscFlags, Usage, CpuAccess, FromView;
        public uint SlotsUsed, SlotsTotal, Accepted, Rejected;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public uint[] Counts;
        /// <summary>El motivo en palabras. Es una observacion del recurso, no una causa aguas arriba.</summary>
        public string Describe()
        {
            string reason;
            switch (Reason)
            {
                case 0: reason = "aceptado"; break;
                case 1: reason = "puntero nulo"; break;
                case 2: reason = "camara o epoca invalidas"; break;
                case 3: reason = "el recurso no expone ID3D11Texture2D"; break;
                case 4: reason = "formato " + Format + "; se admite R16G16_FLOAT (34) o R16G16_TYPELESS (33)"; break;
                case 5: reason = "multisample x" + SampleCount; break;
                case 6: reason = MipLevels + " niveles de mip"; break;
                case 7: reason = "array de " + ArraySize; break;
                case 8: reason = "sin permiso de lectura (bind 0x" + BindFlags.ToString("X") + ")"; break;
                case 9: reason = "no se pudo crear la vista de lectura (HRESULT 0x" + HResult.ToString("X8") + ")"; break;
                case 10: reason = "huecos agotados (" + SlotsUsed + "/" + SlotsTotal + ")"; break;
                case 11: reason = "extension " + Width + "x" + Height; break;
                default: reason = "motivo " + Reason; break;
            }
            string family = Format == 33 ? " tipeless" : Format == 34 ? " float" : "";
            string shape = Width == 0 && Height == 0 ? "" :
                " · recurso " + Width + "x" + Height + " fmt=" + Format + " mips=" + MipLevels +
                " array=" + ArraySize + " aa=" + SampleCount + family + " bind=0x" + BindFlags.ToString("X") +
                " uso=" + Usage + (FromView != 0 ? " (desde vista)" : "");
            return reason + " [etapa " + Stage + ", intento " + RequestSerial + "]" + shape;
        }
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
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ControlsDelegate(ref NativeControls data, uint bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int HealthDelegate(ref NativeHealth data, uint bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RegistrationDelegate(ref NativeRegistrationReport data, uint bytes);
        private ControlsDelegate _setControls, _getControls;
        private SubmitDelegate _submit; private StatusDelegate _status; private ResultDelegate _result;
        private EnableDelegate _enable; private RegisterDelegate _register; private ReserveDelegate _reserve; private ReleaseDelegate _release, _cancel;
        private HealthDelegate _health;
        private RegistrationDelegate _registration;
        public NativeCapabilities Capabilities { get; private set; }
        public string Reason { get; private set; }
        public IntPtr RenderEvent { get; private set; }
        public bool Connected { get { return _submit != null && _status != null && _result != null && _enable != null && _setControls != null && _getControls != null && _register != null && _reserve != null && _release != null && _cancel != null && RenderEvent != IntPtr.Zero; } }
        private static Delegate Resolve(IntPtr module, string name, Type type)
        {
            IntPtr address = NativeInterop.GetProcAddress(module, name);
            return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(address, type);
        }
        public bool Connect()
        {
            if (UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Direct3D11) { Reason = "Esta integración requiere DirectX 11; no se registran recursos de otra API"; return false; }
            IntPtr module = NativeInterop.GetModuleHandle("dlss5-feed.addon64");
            if (module == IntPtr.Zero) { Reason = "Puente sin cargar"; return false; }
            var probe = (CapabilitiesDelegate)Resolve(module, "NeuralFX_GetCapabilities", typeof(CapabilitiesDelegate));
            var caps = new NativeCapabilities();
            if (probe == null || probe(ref caps, 32) != 1 || caps.Size != 32 || caps.Version != 3 || caps.Build != 5 || caps.FrameBytes != 64 || caps.ResultBytes != 80 || (caps.Supported & 9) != 9)
            { Reason = "Puente incompatible; actualiza mod y Hub juntos y reinicia el juego"; return false; }
            var callback = (EventDelegate)Resolve(module, "NeuralFX_GetRenderEvent", typeof(EventDelegate));
            _submit = (SubmitDelegate)Resolve(module, "NeuralFX_SubmitFrameV3", typeof(SubmitDelegate));
            _status = (StatusDelegate)Resolve(module, "NeuralFX_GetStatus", typeof(StatusDelegate));
            _result = (ResultDelegate)Resolve(module, "NeuralFX_GetFrameResult", typeof(ResultDelegate));
            _enable = (EnableDelegate)Resolve(module, "NeuralFX_SetEnabled", typeof(EnableDelegate));
            _register = (RegisterDelegate)Resolve(module, "NeuralFX_RegisterMotion", typeof(RegisterDelegate));
            _reserve = (ReserveDelegate)Resolve(module, "NeuralFX_ReserveMotion", typeof(ReserveDelegate));
            _release = (ReleaseDelegate)Resolve(module, "NeuralFX_ReleaseMotion", typeof(ReleaseDelegate));
            _cancel = (ReleaseDelegate)Resolve(module, "NeuralFX_CancelMotion", typeof(ReleaseDelegate));
            _setControls = (ControlsDelegate)Resolve(module, "NeuralFX_SetControls", typeof(ControlsDelegate));
            _getControls = (ControlsDelegate)Resolve(module, "NeuralFX_GetControls", typeof(ControlsDelegate));
            _health = (HealthDelegate)Resolve(module, "NeuralFX_GetHealth", typeof(HealthDelegate));
            _registration = (RegistrationDelegate)Resolve(module, "NeuralFX_GetRegistrationReport", typeof(RegistrationDelegate));
            RenderEvent = callback != null ? callback() : IntPtr.Zero;
            Capabilities = caps; Reason = Connected ? (_health != null ? "Puente ABI 3 build 5 con salida de salud; NR sin confirmar" : "Puente ABI 3 build 5; NR sin confirmar") : "Exports incompletos";
            return Connected;
        }
        public int ReadControls(ref NativeControls controls) { return Connected && _getControls != null ? _getControls(ref controls,24) : -1; }
        public bool SetControls(ref NativeControls controls) { return Connected && _setControls != null && _setControls(ref controls,24) == 1; }
        public bool SetEnabled(bool enabled) { return Connected && _enable(enabled ? 1u : 0u) == 1; }
        public uint RegisterMotion(IntPtr texture, uint camera, uint epoch) { return Connected && _register != null ? _register(texture, camera, epoch) : 0; }
        public bool ReserveMotion(uint handle) { return Connected && _reserve != null && _reserve(handle) == 1; }
        public void CancelMotion(uint handle) { if (_cancel != null && handle != 0) _cancel(handle); }
        public void ReleaseMotion(uint handle) { if (_release != null && handle != 0) _release(handle); }
        public bool Submit(ref NativeFrame frame) { return Connected && _submit(ref frame, 64) == 1; }
        public NativeStatus ReadStatus()
        {
            var data = new NativeStatus { Size = 40 };
            if (!Connected || _status(ref data) != 1 || data.Version != 1) return new NativeStatus();
            return data;
        }
        /// <summary>Salud del pipeline, o Size=0 si este puente no la publica.</summary>
        public NativeHealth ReadHealth()
        {
            var data = new NativeHealth();
            return Connected && _health != null && _health(ref data, 64) == 1 && data.Version == 1 ? data : new NativeHealth();
        }
        /// <summary>Ultimo intento de registro, o Size=0 si este puente no lo publica.</summary>
        public NativeRegistrationReport ReadRegistrationReport()
        {
            // El tamano sale del propio tipo, no de una constante escrita a mano: si alguien
            // anade un campo aqui y no en el header, el puente rechaza la consulta y el mod dice
            // que no hay informe, en vez de leer memoria con otra forma.
            var data = new NativeRegistrationReport();
            uint bytes = (uint)Marshal.SizeOf(typeof(NativeRegistrationReport));
            return _registration != null && _registration(ref data, bytes) == 1 && data.Version == 1 ? data : new NativeRegistrationReport();
        }
        public NativeResult ReadResult()
        {
            var data = new NativeResult();
            return Connected && _result != null && _result(ref data, 80) == 1 && data.Version == 3 ? data : new NativeResult();
        }
    }
}
