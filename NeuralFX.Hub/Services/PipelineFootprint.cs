using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NeuralFX.Hub.Services;

// Exact destinations used by the current catalog, the old Hub, and the bundled runtimes.
// Never use recursive DLL/shader wildcards or follow paths supplied by a legacy manifest.
internal static class PipelineFootprint
{
    public static readonly string[] Files = DependencyManagerService.ReadCatalog()
        .SelectMany(x => x.PackageFiles.Count > 0 ? x.PackageFiles.Values.AsEnumerable() : new[] { x.TargetRelativePath })
        .Concat(new[] { "nvngx_dlssd.dll", "reshade-shaders/Shaders/CAS.fx", "NeuralFX_Manifest.json",
            "ReShade.log", "ReShadeGUI.ini", "ReShadePreset.ini", "dxgi.log", "dlss5-feed.log", "dlss5-feed-crash.dmp" })
        .Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static readonly string[] PrivateDirectories = { ".neuralfx-backups", ".neuralfx-preserved", ".neuralfx-runtime", "reshade-shaders/Cache" };
    public static readonly string[] SharedDirectories = Files.SelectMany(Parents).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string Normalize(string relative) => relative.Replace('\\', '/');
    public static IEnumerable<string> Parents(string relative)
    {
        for (string? path = Path.GetDirectoryName(relative); !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path)) yield return Normalize(path);
    }
    public static bool HasArtifacts(string root)
    {
        try
        {
            if (Files.Concat(PrivateDirectories).Any(x =>
            {
                string path = ManagedPaths.Resolve(root, x);
                return File.Exists(path) || Directory.Exists(path);
            })) return true;
            // A shared shader folder containing only other resources is not a NeuralFX residue.
            return SharedDirectories.Any(x =>
            {
                string path = ManagedPaths.Resolve(root, x);
                return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return true; } // Offer review; Inspect reports the inaccessible path and prevents removal.
    }
}
