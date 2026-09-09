namespace NeuralFX.Rendering
{
    internal sealed class FrameCoordinator
    {
        private static uint _nextEpoch;
        private uint _frame;
        public uint Epoch { get; private set; }
        public FrameCoordinator() { Recreate(); }
        public void Recreate() { unchecked { Epoch = ++_nextEpoch; if (Epoch == 0) Epoch = ++_nextEpoch; } }
        public uint NextFrame() { unchecked { if (++_frame == 0) ++_frame; return _frame; } }
    }
}
