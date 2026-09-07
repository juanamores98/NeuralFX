using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services
{
    public class InstallationEngineService
    {
        private readonly DependencyManagerService _dependencyManager;

        public InstallationEngineService(DependencyManagerService dependencyManager)
        {
            _dependencyManager = dependencyManager;
        }

        public async Task<bool> InstallAsync(string gameDirectory, List<DependencyItem> items, Action<string>? logAction = null, bool enforceProcessClosed = true)
        {
            void Log(string msg) => logAction?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");

            Log("Iniciando verificación previa de instalación...");

            if (!Directory.Exists(gameDirectory))
            {
                Log($"ERROR: El directorio del juego no existe: {gameDirectory}");
                return false;
            }

            if (!HardwareDiagnosticsService.TestDirectoryWritable(gameDirectory))
            {
                Log("ERROR: No hay permisos de escritura en la carpeta del juego. Ejecuta como Administrador.");
                return false;
            }

            if (enforceProcessClosed && HardwareDiagnosticsService.IsCitiesSkylinesRunning())
            {
                Log("ERROR BLOQUEANTE: Cities: Skylines (Cities.exe) está en ejecución.");
                Log("Debes cerrar el juego antes de instalar para que Windows permita escribir los módulos nativos.");
                return false;
            }

            var manifest = new InstallationManifest
            {
                GameDirectory = gameDirectory,
                InstalledAt = DateTime.UtcNow
            };

            try
            {
                // 1. Manejar backup si ya existe un dxgi.dll ajeno o anterior sin manifiesto
                string targetDxgi = Path.Combine(gameDirectory, "dxgi.dll");
                string manifestPath = Path.Combine(gameDirectory, "NeuralFX_Manifest.json");

                if (File.Exists(targetDxgi) && !File.Exists(manifestPath))
                {
                    string backupFile = Path.Combine(_dependencyManager.BackupsDirectory, $"dxgi_backup_{DateTime.Now:yyyyMMdd_HHmmss}.dll");
                    Log($"Se detectó un dxgi.dll existente sin manifiesto. Creando backup en: {backupFile}");
                    File.Copy(targetDxgi, backupFile, true);
                    manifest.BackedUpFiles["dxgi.dll"] = backupFile;
                }

                // 2. Copiar archivos binarios desde caché
                foreach (var item in items)
                {
                    if (item.Category == DependencyCategory.Runtime || 
                        item.Category == DependencyCategory.Addon || 
                        item.Category == DependencyCategory.NvidiaProprietary)
                    {
                        if (string.IsNullOrEmpty(item.LocalCachedPath) || !File.Exists(item.LocalCachedPath))
                        {
                            if (item.IsRequired)
                            {
                                Log($"ERROR: Dependencia requerida no disponible en caché: {item.DisplayName}");
                                return false;
                            }
                            else
                            {
                                Log($"AVISO: Dependencia opcional no encontrada, se omite: {item.DisplayName}");
                                continue;
                            }
                        }

                        string destPath = Path.Combine(gameDirectory, item.TargetRelativePath);
                        Log($"Inyectando: {item.TargetRelativePath} ({item.FileSize / 1024} KB)...");
                        await Task.Run(() => File.Copy(item.LocalCachedPath, destPath, true));

                        manifest.InstalledFiles.Add(item.TargetRelativePath);
                        manifest.FileChecksums[item.TargetRelativePath] = DependencyManagerService.CalculateSha256(destPath);

                        // Espejo de compatibilidad para denoiser (nvngx_dlssd / nvngx_dlssnr)
                        if (item.Id == "nvngx_dlssd")
                        {
                            string nrPath = Path.Combine(gameDirectory, "nvngx_dlssnr.dll");
                            if (!File.Exists(nrPath))
                            {
                                File.Copy(destPath, nrPath, true);
                                manifest.InstalledFiles.Add("nvngx_dlssnr.dll");
                                manifest.FileChecksums["nvngx_dlssnr.dll"] = DependencyManagerService.CalculateSha256(nrPath);
                                Log("Creado enlace de compatibilidad: nvngx_dlssnr.dll");
                            }
                        }
                    }
                }

                // 3. Crear configuraciones calibradas de ReShade y DLSS5-Feeder
                await GenerateConfigFilesAsync(gameDirectory, manifest, Log);

                // 4. Crear suite de Shaders (AMD CAS & LumeniteFX)
                await GenerateShadersAsync(gameDirectory, manifest, Log);

                // 5. Guardar manifiesto atómico
                string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(manifestPath, manifestJson);
                Log("Manifiesto de instalación registrado exitosamente (NeuralFX_Manifest.json).");

                Log(">> Instalación completada con éxito. Pipeline NeuralFX listo para el juego.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"ERROR CRÍTICO durante la instalación: {ex.Message}");
                return false;
            }
        }

        private async Task GenerateConfigFilesAsync(string gameDirectory, InstallationManifest manifest, Action<string> log)
        {
            // Generar dlss5-feed.cfg calibrado para Unity 5.6 DX11
            string cfgPath = Path.Combine(gameDirectory, "dlss5-feed.cfg");
            string cfgContent = @"# NeuralFX / DLSS5-Feeder Configuration for Cities: Skylines (Unity 5.6 DX11)
[General]
Enabled=1
LogLevel=2
AutoDetectPipelines=1

[Depth]
InvertDepth=0
LinearizeDepth=0
DepthBias=0.000000

[MotionVectors]
EnableMotionVectors=1
MotionVectorScaleX=1.000000
MotionVectorScaleY=1.000000
JitterCancellation=1

[DLSS]
PerformanceProfile=2 ; 0=UltraPerformance, 1=Performance, 2=Balanced, 3=Quality, 4=DLAA
Sharpness=0.350000
NeuralReconstruction=1
AutoExposure=1
";
            log("Generando configuración precalibrada dlss5-feed.cfg...");
            await File.WriteAllTextAsync(cfgPath, cfgContent);
            manifest.InstalledFiles.Add("dlss5-feed.cfg");
            manifest.FileChecksums["dlss5-feed.cfg"] = DependencyManagerService.CalculateSha256(cfgPath);

            // Generar ReShade.ini
            string iniPath = Path.Combine(gameDirectory, "ReShade.ini");
            string iniContent = @"[GENERAL]
EffectSearchPaths=.\reshade-shaders\Shaders
TextureSearchPaths=.\reshade-shaders\Textures
CurrentPresetPath=.\ReShadePreset.ini
PerformanceMode=1
ShowFPS=0
ShowClock=0
NoReloadOnInit=1

[OVERLAY]
ShowOverlay=0
KeyOverlay=36,0,0,0 ; Key Home
KeyReload=0,0,0,0
";
            log("Generando configuración ReShade.ini...");
            await File.WriteAllTextAsync(iniPath, iniContent);
            manifest.InstalledFiles.Add("ReShade.ini");
            manifest.FileChecksums["ReShade.ini"] = DependencyManagerService.CalculateSha256(iniPath);

            // Generar ReShadePreset.ini
            string presetPath = Path.Combine(gameDirectory, "ReShadePreset.ini");
            string presetContent = @"Techniques=CAS@CAS.fx
TechniqueSorting=CAS@CAS.fx

[CAS.fx]
Contrast=0.000000
Sharpening=0.300000
";
            log("Generando preset ReShadePreset.ini...");
            await File.WriteAllTextAsync(presetPath, presetContent);
            manifest.InstalledFiles.Add("ReShadePreset.ini");
            manifest.FileChecksums["ReShadePreset.ini"] = DependencyManagerService.CalculateSha256(presetPath);
        }

        private async Task GenerateShadersAsync(string gameDirectory, InstallationManifest manifest, Action<string> log)
        {
            string shadersDir = Path.Combine(gameDirectory, "reshade-shaders", "Shaders");
            string texturesDir = Path.Combine(gameDirectory, "reshade-shaders", "Textures");

            Directory.CreateDirectory(shadersDir);
            Directory.CreateDirectory(texturesDir);

            if (!manifest.InstalledDirectories.Contains("reshade-shaders"))
            {
                manifest.InstalledDirectories.Add("reshade-shaders");
            }

            // AMD CAS HLSL Shader para nitidez post-reconstrucción
            string casFxPath = Path.Combine(shadersDir, "CAS.fx");
            string casContent = @"// AMD FidelityFX Contrast Adaptive Sharpening (CAS) for ReShade
// Minimal Clean Implementation for NeuralFX

#include ""ReShade.fxh""

uniform float Sharpening <
    ui_type = ""slider"";
    ui_min = 0.0; ui_max = 1.0;
    ui_label = ""Nitidez CAS"";
> = 0.30;

uniform float Contrast <
    ui_type = ""slider"";
    ui_min = 0.0; ui_max = 1.0;
    ui_label = ""Ajuste de Contraste"";
> = 0.0;

float3 CASPass(float4 vpos : SV_Position, float2 texcoord : TexCoord) : SV_Target
{
    float3 a = tex2D(ReShade::BackBuffer, texcoord + float2(-BUFFER_RCP_WIDTH, -BUFFER_RCP_HEIGHT)).rgb;
    float3 b = tex2D(ReShade::BackBuffer, texcoord + float2(0.0, -BUFFER_RCP_HEIGHT)).rgb;
    float3 c = tex2D(ReShade::BackBuffer, texcoord + float2(BUFFER_RCP_WIDTH, -BUFFER_RCP_HEIGHT)).rgb;
    float3 d = tex2D(ReShade::BackBuffer, texcoord + float2(-BUFFER_RCP_WIDTH, 0.0)).rgb;
    float3 e = tex2D(ReShade::BackBuffer, texcoord).rgb;
    float3 f = tex2D(ReShade::BackBuffer, texcoord + float2(BUFFER_RCP_WIDTH, 0.0)).rgb;
    float3 g = tex2D(ReShade::BackBuffer, texcoord + float2(-BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT)).rgb;
    float3 h = tex2D(ReShade::BackBuffer, texcoord + float2(0.0, BUFFER_RCP_HEIGHT)).rgb;
    float3 i = tex2D(ReShade::BackBuffer, texcoord + float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT)).rgb;

    float3 mn = min(min(min(d, e), min(f, b)), h);
    float3 mx = max(max(max(d, e), max(f, b)), h);

    float3 amp = saturate(min(mn, 2.0 - mx) / mx);
    float3 w = amp * -Sharpening;

    float3 result = (b * w + d * w + f * w + h * w + e) / (1.0 + 4.0 * w);
    return saturate(result);
}

technique CAS
{
    pass
    {
        VertexShader = PostProcessVS;
        PixelShader = CASPass;
    }
}
";
            log("Desplegando shader AMD FidelityFX CAS...");
            await File.WriteAllTextAsync(casFxPath, casContent);
            manifest.InstalledFiles.Add(Path.Combine("reshade-shaders", "Shaders", "CAS.fx"));
            manifest.FileChecksums[Path.Combine("reshade-shaders", "Shaders", "CAS.fx")] = DependencyManagerService.CalculateSha256(casFxPath);

            // ReShade.fxh basic helper
            string fxhPath = Path.Combine(shadersDir, "ReShade.fxh");
            string fxhContent = @"#pragma once

#define BUFFER_WIDTH 1920
#define BUFFER_HEIGHT 1080
#define BUFFER_RCP_WIDTH (1.0 / BUFFER_WIDTH)
#define BUFFER_RCP_HEIGHT (1.0 / BUFFER_HEIGHT)

namespace ReShade
{
    texture BackBufferTex : COLOR;
    sampler BackBuffer { Texture = BackBufferTex; };
}

void PostProcessVS(in uint id : SV_VertexID, out float4 position : SV_Position, out float2 texcoord : TexCoord)
{
    texcoord.x = (id == 2) ? 2.0 : 0.0;
    texcoord.y = (id == 1) ? 2.0 : 0.0;
    position = float4(texcoord * float2(2.0, -2.0) + float2(-1.0, 1.0), 0.0, 1.0);
}
";
            await File.WriteAllTextAsync(fxhPath, fxhContent);
            manifest.InstalledFiles.Add(Path.Combine("reshade-shaders", "Shaders", "ReShade.fxh"));
            manifest.FileChecksums[Path.Combine("reshade-shaders", "Shaders", "ReShade.fxh")] = DependencyManagerService.CalculateSha256(fxhPath);
        }
    }
}
