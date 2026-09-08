using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace NeuralFX.Hub.Services
{
    public enum PipelinePreset { Native, BalancedCost, LowCost, Photography }

    internal static class PipelineConfiguration
    {
        public static Dictionary<string, byte[]> Create(string root, PipelinePreset preset)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string Read(string path) { string full = ManagedPaths.Resolve(root, path); return File.Exists(full) ? File.ReadAllText(full) : ""; }
            int work = preset == PipelinePreset.LowCost ? 66 : preset == PipelinePreset.BalancedCost ? 85 : 100;
            float sharpness = preset == PipelinePreset.Photography ? 0.15f : 0.30f;
            string feeder = Read("dlss5-feed.cfg");
            foreach (var pair in new Dictionary<string, string> { ["enabled"] = "1", ["mode"] = "2", ["depth_inverted"] = "-1", ["hdr"] = "-1", ["work_resolution"] = work.ToString(), ["work_upscale"] = work < 100 ? "1" : "0", ["work_sharpness"] = "0", ["mv_scale_x"] = "1", ["mv_scale_y"] = "1", ["reset_every"] = "0" })
                feeder = Ini.SetFlat(feeder, pair.Key, pair.Value);
            files["dlss5-feed.cfg"] = Encoding.UTF8.GetBytes(feeder);
            string ini = Read("ReShade.ini");
            ini = Ini.Set(ini, "GENERAL", "EffectSearchPaths", MergePaths(Ini.Get(ini, "GENERAL", "EffectSearchPaths"), @".\reshade-shaders\Shaders"));
            ini = Ini.Set(ini, "GENERAL", "TextureSearchPaths", MergePaths(Ini.Get(ini, "GENERAL", "TextureSearchPaths"), @".\reshade-shaders\Textures"));
            ini = Ini.Set(ini, "GENERAL", "PresetPath", @".\ReShadePreset.ini");
            ini = Ini.Set(ini, "GENERAL", "StartupPresetPath", @".\ReShadePreset.ini");
            ini = Ini.Set(ini, "GENERAL", "NoReloadOnInit", "0");
            ini = Ini.Set(ini, "GENERAL", "PerformanceMode", "1");
            ini = Ini.Set(ini, "GENERAL", "NoDebugInfo", "1");
            ini = Ini.Set(ini, "GENERAL", "NoEffectCache", "0");
            ini = Ini.Set(ini, "DEPTH", "DrawStatsHeuristic", "2");
            ini = Ini.Set(ini, "GENERAL", "IntermediateCachePath", @".\.neuralfx-runtime\ReShade");
            ini = Ini.Set(ini, "SCREENSHOT", "SavePath", @".\.neuralfx-runtime\Capturas");
            string definitions = Ini.Get(ini, "GENERAL", "PreprocessorDefinitions");
            definitions = string.Join(",", definitions.Split(',').Where(x => x.Length > 0 && !x.StartsWith("DLSS5_MV_PROVIDER=", StringComparison.OrdinalIgnoreCase)));
            ini = Ini.Set(ini, "GENERAL", "PreprocessorDefinitions", MergePaths(definitions, "DLSS5_MV_PROVIDER=3"));
            files["ReShade.ini"] = Encoding.UTF8.GetBytes(ini);
            string reshadePreset = Read("ReShadePreset.ini");
            string techniques = Ini.Get(reshadePreset, "", "Techniques");
            string[] ours = { "Lumenite_Kernel@lumenite_Kernel.fx", "DLSS5_Feed@DLSS5_Feed.fx", "NeuralFX_CAS@NeuralFX_CAS.fx" };
            var otherTechs = techniques.Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && !ours.Contains(x, StringComparer.OrdinalIgnoreCase) && !x.StartsWith("CAS@", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("DLSS5_Feed_Debug@", StringComparison.OrdinalIgnoreCase))
                .ToList();
            string finalTechs = string.Join(",", otherTechs.Concat(ours));
            reshadePreset = Ini.Set(reshadePreset, "", "Techniques", finalTechs);
            reshadePreset = Ini.Set(reshadePreset, "", "TechniqueSorting", finalTechs);
            reshadePreset = Ini.Set(reshadePreset, "DLSS5_Feed.fx", "PreprocessorDefinitions", "DLSS5_MV_PROVIDER=3");
            reshadePreset = Ini.Set(reshadePreset, "NeuralFX_CAS.fx", "Sharpening", sharpness.ToString(CultureInfo.InvariantCulture));
            files["ReShadePreset.ini"] = Encoding.UTF8.GetBytes(reshadePreset);
            using var stream = typeof(PipelineConfiguration).Assembly.GetManifestResourceStream("NeuralFX.Hub.Assets.NeuralFX_CAS.fx")!;
            using var output = new MemoryStream(); stream.CopyTo(output);
            files["reshade-shaders/Shaders/NeuralFX_CAS.fx"] = output.ToArray();
            return files;
        }
        private static string MergePaths(string existing, string addition) => string.Join(",", (existing + "," + addition).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    internal static class Ini
    {
        // The feeder ignores INI sections and the last duplicate wins, including legacy [General].
        public static string SetFlat(string text, string key, string value)
        {
            var lines = text.Replace("\r", "").Split('\n').Where(line =>
            {
                int equals = line.IndexOf('=');
                return equals < 0 || !line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase);
            });
            return key + "=" + value + "\n" + string.Join("\n", lines);
        }
        public static string Get(string text, string section, string key)
        {
            string current = "";
            foreach (string line in text.Replace("\r", "").Split('\n'))
            {
                string trim = line.Trim();
                if (trim.StartsWith('[') && trim.EndsWith(']')) current = trim[1..^1];
                int index = line.IndexOf('=');
                if (index > 0 && current.Equals(section, StringComparison.OrdinalIgnoreCase) && line[..index].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(index + 1)..].Trim();
            }
            return "";
        }
        public static string Set(string text, string section, string key, string value)
        {
            var lines = text.Replace("\r", "").Split('\n').ToList();
            string current = ""; int insert = section == "" ? 0 : -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string trim = lines[i].Trim();
                if (trim.StartsWith('[') && trim.EndsWith(']')) { current = trim[1..^1]; if (current.Equals(section, StringComparison.OrdinalIgnoreCase)) insert = i + 1; }
                int index = lines[i].IndexOf('=');
                if (index > 0 && current.Equals(section, StringComparison.OrdinalIgnoreCase) && lines[i][..index].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                { lines[i] = key + "=" + value; return string.Join("\n", lines); }
            }
            if (insert < 0) { lines.Add("[" + section + "]"); lines.Add(key + "=" + value); }
            else lines.Insert(insert, key + "=" + value);
            return string.Join("\n", lines);
        }
    }
}
