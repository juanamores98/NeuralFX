using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class HardwareDiagnosticsService
    {
        public HardwareInfo RunDiagnostics()
        {
            var info = new HardwareInfo();

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
                                if (double.TryParse(info.ParsedDriverVersion, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedVal))
                                {
                                    info.DriverMeetsRequirement = parsedVal >= 570.0;
                                }
                            }

                            // Architecture detection
                            AnalyzeGpuArchitecture(info);

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
                    string combined = parts[2] + parts[3];
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

        private static void AnalyzeGpuArchitecture(HardwareInfo info)
        {
            string name = info.GpuName.ToUpperInvariant();

            if (name.Contains("RTX 50") || name.Contains("RTX 5"))
            {
                info.Architecture = "Blackwell (RTX Serie 50xx)";
                info.SupportsDLSS = true;
                info.SupportsDLSS5 = true; // Native DLSS 5 Neural Reconstruction
            }
            else if (name.Contains("RTX 40"))
            {
                info.Architecture = "Ada Lovelace (RTX Serie 40xx)";
                info.SupportsDLSS = true;
                info.SupportsDLSS5 = true; // DLSS 3.5+ / DFC Neural Denoiser compatible
            }
            else if (name.Contains("RTX 30"))
            {
                info.Architecture = "Ampere (RTX Serie 30xx)";
                info.SupportsDLSS = true;
                info.SupportsDLSS5 = false; // DLSS 2/3.5 standard
            }
            else if (name.Contains("RTX 20") || name.Contains("TITAN RTX"))
            {
                info.Architecture = "Turing (RTX Serie 20xx)";
                info.SupportsDLSS = true;
                info.SupportsDLSS5 = false;
            }
            else if (name.Contains("GTX"))
            {
                info.Architecture = "Pascal / Turing GTX (Sin Tensor Cores)";
                info.SupportsDLSS = false;
                info.SupportsDLSS5 = false;
            }
            else
            {
                info.Architecture = "NVIDIA Desconocida";
                info.SupportsDLSS = info.IsNvidia;
            }
        }

        public void DetectCitiesSkylines(HardwareInfo info)
        {
            string[] potentialPaths =
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines\Cities.exe",
                @"D:\SteamLibrary\steamapps\common\Cities_Skylines\Cities.exe",
                @"E:\SteamLibrary\steamapps\common\Cities_Skylines\Cities.exe"
            };

            foreach (var path in potentialPaths)
            {
                if (File.Exists(path))
                {
                    info.GameExePath = path;
                    info.GameFound = true;
                    break;
                }
            }

            // If not found, try reading Steam installation path from Registry
            if (!info.GameFound)
            {
                try
                {
                    using var steamKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam") 
                                         ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    if (steamKey != null)
                    {
                        var steamPath = steamKey.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            var candidate = Path.Combine(steamPath, @"steamapps\common\Cities_Skylines\Cities.exe");
                            if (File.Exists(candidate))
                            {
                                info.GameExePath = candidate;
                                info.GameFound = true;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore registry search failure
                }
            }

            // Test write permissions
            if (info.GameFound)
            {
                info.CanWriteGameDir = TestDirectoryWritable(Path.GetDirectoryName(info.GameExePath)!);
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
