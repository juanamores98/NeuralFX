namespace NeuralFX.Protocol
{
    // Pure policy shared by runtime and contract tests. A loaded DLL is insufficient.
    public static class FramePolicy
    {
        public static bool CanJitter(bool enabled, bool experimental, uint capabilities, bool prepared, bool resourcesValid)
        { return enabled && experimental && prepared && resourcesValid && (capabilities & (2u | 32u | 64u)) == (2u | 32u | 64u); }
        public static bool Newer(uint value, uint previous)
        { return value != previous && unchecked(value - previous) < 0x80000000u; }
    }
}
