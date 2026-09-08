using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class HardwareDiagnosticsService
    {
        public HardwareInfo RunDiagnostics(string? preferredExecutable = null)
        {
            var info = new HardwareInfo();
            if (!string.IsNullOrEmpty(preferredExecutable)) info.GameExePath = preferredExecutable;

            DetectGpuFromRegistry(info);
            DetectCitiesSkylines(info);

            return info;
        }

        private void DetectGpuFromRegistry(HardwareInfo info)
        {
            try
            {
                const string videoClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using var baseKey = Registry.LocalMachine.OpenSubKey(videoClassKey);

                if (baseKey != null)
                {
                    foreach (var subName in baseKey.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(subName, @"^\d{4}$"))
                            continue;

                        using var subKey = baseKey.OpenSubKey(subName);
                        if (subKey == null) continue;

                        var driverDesc = subKey.GetValue("DriverDesc") as string;
                        if (string.IsNullOrEmpty(driverDesc)) continue;

                        // Check if this is an NVIDIA adapter
                        if (driverDesc.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            info.GpuName = driverDesc;
                            info.IsNvidia = true;

                            // VRAM detection: check 64-bit qwMemorySize first, then 32-bit MemorySize
                            var qwMem = subKey.GetValue("HardwareInformation.qwMemorySize");
                            if (qwMem != null && ulong.TryParse(qwMem.ToString(), out ulong bytes64) && bytes64 > 0)
                            {
                                info.VramBytes = bytes64;
                            }
                            else
                            {
                                var mem = subKey.GetValue("HardwareInformation.MemorySize");
                                if (mem != null)
                                {
                                    if (mem is byte[] byteArr && byteArr.Length >= 4)
                                    {
                                        info.VramBytes = BitConverter.ToUInt32(byteArr, 0);
                                    }
                                    else if (ulong.TryParse(mem.ToString(), out ulong bytes32))
                                    {
                                        info.VramBytes = bytes32;
                                    }
                                }
                            }

                            // Driver Version detection
                            var driverVer = subKey.GetValue("DriverVersion") as string;
                            if (!string.IsNullOrEmpty(driverVer))
                            {
                                info.RawDriverVersion = driverVer;
                                info.ParsedDriverVersion = ParseNvidiaDriverVersion(driverVer);
                            }

                            // Architecture detection
                            if (Regex.IsMatch(info.GpuName, @"\bRTX\s*50\d\d\b", RegexOptions.IgnoreCase))
                            {
                                info.DetectedArchitecture = GpuArchitecture.Blackwell;
                                info.Architecture = "NVIDIA Blackwell (RTX 50xx · SM 120 · FP8)";
                                info.SupportsDLSS = true;
                            }
                            else if (Regex.IsMatch(info.GpuName, @"\bRTX\s*40\d\d\b", RegexOptions.IgnoreCase))
                            {
                                info.DetectedArchitecture = GpuArchitecture.AdaLovelace;
                                info.Architecture = "NVIDIA Ada Lovelace (RTX 40xx · SM 89 · FP16/FP8)";
                                info.SupportsDLSS = true;
                            }
                            else if (Regex.IsMatch(info.GpuName, @"\bRTX\s*(20|30)\d\d\b", RegexOptions.IgnoreCase) ||
                                     Regex.IsMatch(info.GpuName, @"\b(TITAN\s*RTX|RTX\s*A\d{4})\b", RegexOptions.IgnoreCase))
                            {
                                info.DetectedArchitecture = GpuArchitecture.AmpereTuring;
                                info.Architecture = "NVIDIA Ampere / Turing (RTX 20xx/30xx · SM 75/86 · FP16)";
                                info.SupportsDLSS = true;
                            }
                            else if (Regex.IsMatch(info.GpuName, @"\bRTX\b", RegexOptions.IgnoreCase))
                            {
                                info.DetectedArchitecture = GpuArchitecture.Blackwell;
                                info.Architecture = "NVIDIA RTX (Arquitectura moderna · DLSS compatible)";
                                info.SupportsDLSS = true;
                            }
                            else
                            {
                                info.DetectedArchitecture = GpuArchitecture.Unsupported;
                                info.Architecture = "No compatible con Tensor Cores (Se requiere NVIDIA RTX)";
                                info.SupportsDLSS = false;
                            }

                            // Prefer discrete NVIDIA GPU and break
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                info.GpuName = $"Error detectando GPU: {ex.Message}";
            }
        }

        private static string ParseNvidiaDriverVersion(string rawVersion)
        {
            // Windows format: A.B.C.D, e.g. 32.0.16.1686 or 31.0.15.5176
            // NVIDIA driver version is the last 5 digits of C + D: "161686" -> "616.86", "155176" -> "551.76"
            try
            {
                var parts = rawVersion.Split('.');
                if (parts.Length == 4)
                {
                    string combined = parts[2] + parts[3].PadLeft(4, '0');
                    if (combined.Length >= 5)
                    {
                        string last5 = combined.Substring(combined.Length - 5);
                        string major = last5.Substring(0, 3);
                        string minor = last5.Substring(3);
                        return $"{major}.{minor}";
                    }
                }
            }
            catch
            {
                // Fallback
            }

            return rawVersion;
        }

        public void DetectCitiesSkylines(HardwareInfo info)
        {
            // An explicit selection stays selected even if it later becomes unavailable.
            try
            {
                if (string.IsNullOrEmpty(info.GameExePath)) info.GameExePath = SteamLibraryLocator.Find() ?? string.Empty;
                info.GameFound = Path.GetFileName(info.GameExePath).Equals("Cities.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(info.GameExePath);
                info.CanWriteGameDir = info.GameFound && TestDirectoryWritable(Path.GetDirectoryName(info.GameExePath)!);
                info.IsGameRunning = IsCitiesSkylinesRunning();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { info.GameFound = false; }
        }
        public static bool IsCitiesSkylinesRunning()
        {
            try
            {
                var processes = System.Diagnostics.Process.GetProcessesByName("Cities");
                bool running = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                return running;
            }
            catch
            {
                return true; // Fail closed if process inspection is unavailable.
            }
        }

        public static bool TestDirectoryWritable(string directoryPath)
        {
            try
            {
                string testFile = Path.Combine(directoryPath, $".neuralfx_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
