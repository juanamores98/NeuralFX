using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NeuralFX.Protocol
{
    [Flags]
    public enum RuntimeFlags { None = 0, ReShadeLoaded = 1, FeederLoaded = 2, ConsumerLoaded = 4, DepthRequested = 8, MotionRequested = 16, CameraPresent = 32, NativeConnected = 64, EvaluationSucceeded = 128, NativeMotion = 256, JitterActive = 512, PipelineRequested = 1024, OutputCommitted = 2048, NrConfirmed = 4096, UiIsolated = 8192 }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TelemetryFrame
    {
        public int Magic, Version, ProcessId, Size;
        public long UtcTicks;
        public float Fps, FrameMs;
        public int DisplayWidth, DisplayHeight, RenderWidth, RenderHeight;
        public RuntimeFlags Flags;
        public int CameraCuts, FrameIndex, LastCommand;
        public float JitterX, JitterY;
        public int NativeStatus, ResetCount;
        public int WorkWidth, WorkHeight, Evaluations;
        public long SessionId;
        public uint DeviceEpoch, CameraId, RecordedFrame, SubmittedFrame, CompletedFrame, OutputFrame, MotionProvider;
        public uint AdapterLow; public int AdapterHigh;
        public CommandResult CommandResult;
        public int CommandReason, BackendError;
        public uint BridgeBuild;
        public uint ControlsRevision; public int ControlsResult, RequestedWork; public float RequestedSharpness;
    }
    public enum CommandKind { None = 0, TogglePanel = 1, ReapplyBuffers = 2, ResetHistory = 3, EnablePipeline = 4, DisablePipeline = 5, SetWorkResolution = 6, SetSharpness = 7 }
    public enum CommandResult { None = 0, Accepted = 1, Applied = 2, Rejected = 3 }
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TelemetryCommand { public int Magic, Version, Revision; public CommandKind Kind; public long SessionId; public long ExpiresUtcTicks; public int IntValue; public float FloatValue; }

    // Two separately sequenced regions, each with exactly one writer. No GPU resources cross into the Hub.
    public sealed unsafe class TelemetryChannel : IDisposable
    {
        public const int Magic = 0x4E465832;
        public const int Version = 4;
        private IntPtr _mapping, _view;
        private readonly bool _owner;
        private readonly long _session;
        private static long _lastSession;
        private TelemetryChannel(IntPtr mapping, IntPtr view, bool owner)
        {
            _mapping = mapping; _view = view; _owner = owner;
            _session = owner ? NewSession() : 0;
            if (owner) Publish(new TelemetryFrame());
        }
        private static long NewSession()
        {
            long previous, next;
            do { previous = Interlocked.Read(ref _lastSession); next = Math.Max(DateTime.UtcNow.Ticks, previous + 1); }
            while (Interlocked.CompareExchange(ref _lastSession, next, previous) != previous);
            return next;
        }
#if NET8_0_OR_GREATER
        public static TelemetryChannel? Open(int pid, bool create)
#else
        public static TelemetryChannel Open(int pid, bool create)
#endif
        {
            string name = "Local\\NeuralFX_CS1_" + pid;
            IntPtr mapping = create ? CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 4, 0, 4096, name) : OpenFileMapping(0x000F001F, false, name);
            if (mapping == IntPtr.Zero) return null;
            IntPtr view = MapViewOfFile(mapping, 0x000F001F, 0, 0, new UIntPtr(4096));
            if (view == IntPtr.Zero) { CloseHandle(mapping); return null; }
            return new TelemetryChannel(mapping, view, create);
        }
        public void Publish(TelemetryFrame frame)
        {
            if (!_owner || _view == IntPtr.Zero) return;
            frame.Magic = Magic; frame.Version = Version; frame.Size = sizeof(TelemetryFrame);
            frame.SessionId = _session;
            byte* memory = (byte*)_view;
            BeginWrite((int*)memory);
            *(TelemetryFrame*)(memory + 8) = frame;
            Thread.MemoryBarrier();
            Interlocked.Increment(ref *(int*)memory);
        }
        public bool TryRead(out TelemetryFrame frame)
        {
            frame = new TelemetryFrame();
            if (_view == IntPtr.Zero) return false;
            byte* memory = (byte*)_view;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int before = VolatileRead((int*)memory);
                if ((before & 1) != 0) continue;
                frame = *(TelemetryFrame*)(memory + 8);
                Thread.MemoryBarrier();
                if (before == VolatileRead((int*)memory)) return frame.Magic == Magic && frame.Version == Version && frame.Size == sizeof(TelemetryFrame);
            }
            return false;
        }
        public void Send(TelemetryCommand command)
        {
            if (_owner || _view == IntPtr.Zero) return;
            command.Magic = Magic; command.Version = Version;
            if (command.ExpiresUtcTicks == 0) command.ExpiresUtcTicks = DateTime.UtcNow.AddSeconds(5).Ticks;
            byte* memory = (byte*)_view + 512;
            BeginWrite((int*)memory);
            *(TelemetryCommand*)(memory + 8) = command;
            Thread.MemoryBarrier(); Interlocked.Increment(ref *(int*)memory);
        }
        public bool TryReadCommand(out TelemetryCommand command)
        {
            command = new TelemetryCommand();
            if (!_owner || _view == IntPtr.Zero) return false;
            byte* memory = (byte*)_view + 512;
            int before = VolatileRead((int*)memory);
            if ((before & 1) != 0) return false;
            command = *(TelemetryCommand*)(memory + 8);
            Thread.MemoryBarrier();
            return before == VolatileRead((int*)memory) && command.Magic == Magic && command.Version == Version && command.SessionId == _session;
        }
        // A replacement writer also recovers a sequence left odd by an interrupted writer.
        private static void BeginWrite(int* pointer) { Interlocked.Exchange(ref *pointer, unchecked((VolatileRead(pointer) + 1) | 1)); }
        private static int VolatileRead(int* pointer) { int value = *pointer; Thread.MemoryBarrier(); return value; }
        public void Dispose()
        {
            if (_owner) Publish(new TelemetryFrame());
            if (_view != IntPtr.Zero) { UnmapViewOfFile(_view); _view = IntPtr.Zero; }
            if (_mapping != IntPtr.Zero) { CloseHandle(_mapping); _mapping = IntPtr.Zero; }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr security, uint protect, uint high, uint low, string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, UIntPtr count);
        [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr view);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
