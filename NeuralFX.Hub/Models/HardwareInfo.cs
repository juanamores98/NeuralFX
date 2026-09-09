using System;

namespace NeuralFX.Hub.Models
{
    public enum GpuArchitecture
    {
        Unknown,
        Unsupported,
        AmpereTuring,   // Inventory only; does not select a runtime variant
        AdaLovelace,    // Inventory only
        Blackwell       // Inventory only
    }

    public enum FeatureSupport { Unknown, Supported, Unsupported }

    public class HardwareInfo
    {
        public string GpuName { get; set; } = "Desconocido";
        public bool IsNvidia { get; set; }
        public string Architecture { get; set; } = "Desconocida";
        public GpuArchitecture DetectedArchitecture { get; set; } = GpuArchitecture.Unknown;
        public bool SupportsDLSS { get; set; } // preliminary family hint, never authoritative
        public FeatureSupport SuperResolution { get; set; } = FeatureSupport.Unknown;
        public FeatureSupport Dlaa { get; set; } = FeatureSupport.Unknown;
        public FeatureSupport NeuralRendering { get; set; } = FeatureSupport.Unknown;
        public string AdapterSource { get; set; } = "Registro (preliminar; puede no ser la GPU del juego)";
        public ulong VramBytes { get; set; }
        public double VramMB => VramBytes / (1024.0 * 1024.0);
        public double VramGB => VramBytes / (1024.0 * 1024.0 * 1024.0);
        public string VramDisplay => $"{VramGB:F1} GB ({VramMB:N0} MB)";
        public string RawDriverVersion { get; set; } = "N/A";
        public string ParsedDriverVersion { get; set; } = "N/A";
        public string GameExePath { get; set; } = string.Empty;
        public bool GameFound { get; set; }
        public bool CanWriteGameDir { get; set; }
        public bool IsGameRunning { get; set; }

    }
}
