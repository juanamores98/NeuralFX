using System;

namespace NeuralFX.Hub.Models
{
    public class HardwareInfo
    {
        public string GpuName { get; set; } = "Desconocido";
        public bool IsNvidia { get; set; }
        public string Architecture { get; set; } = "Desconocida";
        public bool SupportsDLSS { get; set; }
        public bool SupportsDLSS5 { get; set; }
        public ulong VramBytes { get; set; }
        public double VramMB => VramBytes / (1024.0 * 1024.0);
        public double VramGB => VramBytes / (1024.0 * 1024.0 * 1024.0);
        public string VramDisplay => $"{VramGB:F1} GB ({VramMB:N0} MB)";
        public string RawDriverVersion { get; set; } = "N/A";
        public string ParsedDriverVersion { get; set; } = "N/A";
        public bool DriverMeetsRequirement { get; set; }
        public string GameExePath { get; set; } = string.Empty;
        public bool GameFound { get; set; }
        public bool CanWriteGameDir { get; set; }

        public bool IsSystemReadyForDLSS5 =>
            IsNvidia && SupportsDLSS5 && DriverMeetsRequirement && VramGB >= 6.0 && GameFound && CanWriteGameDir;
    }
}
