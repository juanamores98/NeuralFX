using System;
using System.IO;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;
using Xunit;

namespace NeuralFX.Tests
{
    public class DiagnosticsAndRollbackTests
    {
        [Fact]
        public void TestHardwareDiagnostics_OnHostMachine()
        {
            var service = new HardwareDiagnosticsService();
            var info = service.RunDiagnostics();

            Assert.NotNull(info);
            Assert.True(info.IsNvidia, "Debe detectar una tarjeta NVIDIA");
            Assert.Contains("5080", info.GpuName);
            Assert.Equal("Blackwell (RTX Serie 50xx)", info.Architecture);
            Assert.True(info.SupportsDLSS5, "Debe soportar DLSS 5 en Blackwell");
            Assert.True(info.VramGB > 15.0, $"VRAM debe ser > 15 GB (detectado: {info.VramDisplay})");
            Assert.True(info.DriverMeetsRequirement, $"El driver debe ser >= 570.xx (detectado: {info.ParsedDriverVersion})");
            Assert.True(info.GameFound, "Debe localizar Cities: Skylines 1 en Steam");
            Assert.True(info.CanWriteGameDir, "Debe tener permisos de escritura en la carpeta del juego");
        }

        [Fact]
        public void TestDependencyManager_InitialDependencies()
        {
            var depService = new DependencyManagerService();
            var list = depService.GetInitialDependencies();

            Assert.NotNull(list);
            Assert.NotEmpty(list);
            Assert.Contains(list, d => d.Id == "reshade_addon");
            Assert.Contains(list, d => d.Id == "dlss5_feeder");
            Assert.Contains(list, d => d.Id == "nvngx_dlss");
        }

        [Fact]
        public async Task TestInstallationEngine_And_ZeroTraceRollback()
        {
            // Create mock game sandbox
            string tempGameDir = Path.Combine(Path.GetTempPath(), $"NeuralFX_Game_{Guid.NewGuid():N}");
            string tempCacheDir = Path.Combine(Path.GetTempPath(), $"NeuralFX_Cache_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempGameDir);
            Directory.CreateDirectory(tempCacheDir);

            try
            {
                // Simulate Cities.exe
                string exePath = Path.Combine(tempGameDir, "Cities.exe");
                await File.WriteAllTextAsync(exePath, "MOCK_EXE");

                // Simulate cached binaries
                string mockDxgi = Path.Combine(tempCacheDir, "dxgi.dll");
                string mockFeeder = Path.Combine(tempCacheDir, "dlss5-feed.addon64");
                string mockDlss = Path.Combine(tempCacheDir, "nvngx_dlss.dll");
                await File.WriteAllTextAsync(mockDxgi, "MOCK_DXGI");
                await File.WriteAllTextAsync(mockFeeder, "MOCK_FEEDER");
                await File.WriteAllTextAsync(mockDlss, "MOCK_DLSS");

                var depService = new DependencyManagerService();
                var installEngine = new InstallationEngineService(depService);
                var rollbackService = new RollbackService();

                var dependencies = depService.GetInitialDependencies(tempGameDir);
                foreach (var dep in dependencies)
                {
                    if (dep.Id == "reshade_addon") { dep.LocalCachedPath = mockDxgi; dep.Status = DependencyStatus.InCache; }
                    if (dep.Id == "dlss5_feeder") { dep.LocalCachedPath = mockFeeder; dep.Status = DependencyStatus.InCache; }
                    if (dep.Id == "nvngx_dlss") { dep.LocalCachedPath = mockDlss; dep.Status = DependencyStatus.InCache; }
                }

                // Install into mock directory
                bool installed = await installEngine.InstallAsync(tempGameDir, dependencies);
                Assert.True(installed, "La instalación debe completarse");

                // Verify installed files exist
                Assert.True(File.Exists(Path.Combine(tempGameDir, "NeuralFX_Manifest.json")), "Manifiesto debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "dxgi.dll")), "dxgi.dll debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "dlss5-feed.addon64")), "dlss5-feed.addon64 debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "nvngx_dlss.dll")), "nvngx_dlss.dll debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "dlss5-feed.cfg")), "dlss5-feed.cfg debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "ReShade.ini")), "ReShade.ini debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "ReShadePreset.ini")), "ReShadePreset.ini debe existir");
                Assert.True(File.Exists(Path.Combine(tempGameDir, "reshade-shaders", "Shaders", "CAS.fx")), "CAS.fx debe existir");

                // Simulate runtime execution logs
                await File.WriteAllTextAsync(Path.Combine(tempGameDir, "ReShade.log"), "ReShade initialized");
                await File.WriteAllTextAsync(Path.Combine(tempGameDir, "dlss5-feed.log"), "DLSS5-Feeder hooked");

                Assert.True(rollbackService.IsInjectionActive(tempGameDir), "Debe reportar inyección activa");

                // Execute Rollback
                bool cleanRollback = await rollbackService.RollbackAsync(tempGameDir);
                Assert.True(cleanRollback, "El rollback debe devolver true");

                // Verify Zero-Trace
                Assert.False(File.Exists(Path.Combine(tempGameDir, "NeuralFX_Manifest.json")), "Manifiesto debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "dxgi.dll")), "dxgi.dll debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "dlss5-feed.addon64")), "dlss5-feed.addon64 debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "nvngx_dlss.dll")), "nvngx_dlss.dll debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "dlss5-feed.cfg")), "Config debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "ReShade.ini")), "ReShade.ini debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "ReShadePreset.ini")), "ReShadePreset.ini debe borrarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "ReShade.log")), "ReShade.log debe purgarse");
                Assert.False(File.Exists(Path.Combine(tempGameDir, "dlss5-feed.log")), "dlss5-feed.log debe purgarse");
                Assert.False(Directory.Exists(Path.Combine(tempGameDir, "reshade-shaders")), "reshade-shaders debe borrarse");

                // Verify game exe remains intact
                Assert.True(File.Exists(exePath), "Cities.exe vanilla debe permanecer intacto");
                Assert.False(rollbackService.IsInjectionActive(tempGameDir), "No debe haber inyección activa");
            }
            finally
            {
                if (Directory.Exists(tempGameDir)) Directory.Delete(tempGameDir, true);
                if (Directory.Exists(tempCacheDir)) Directory.Delete(tempCacheDir, true);
            }
        }
    }
}
