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
            string activePreset = Ini.Get(ini, "GENERAL", "PresetPath").Replace('\\', '/');
            if (activePreset.Length > 0 && activePreset != "./ReShadePreset.ini" && activePreset != "ReShadePreset.ini")
                throw new InvalidDataException("Preset activo distinto de ReShadePreset.ini: " + activePreset + ". Selecciona explícitamente el preset de NeuralFX antes de instalar; se conserva el actual.");
            ini = Ini.Set(ini, "GENERAL", "PresetPath", @".\ReShadePreset.ini");
            ini = Ini.Set(ini, "GENERAL", "StartupPresetPath", @".\ReShadePreset.ini");
            ini = Ini.Set(ini, "GENERAL", "NoReloadOnInit", "0");
            ini = Ini.Set(ini, "GENERAL", "NoDebugInfo", "1");
            ini = Ini.Set(ini, "GENERAL", "NoEffectCache", "0");
            ini = Ini.Set(ini, "DEPTH", "DrawStatsHeuristic", "2");
            // La cámara de CS1 no ocupa el backbuffer: 1920x967 sobre 1920x1080. La heurística de
            // aspecto de ReShade 6.8 (generic_depth_addon.cpp) exige |1.7778 - 1.9855| <= 0.1 y
            // descarta ese buffer, así que DLSS recibía profundidad plana y, con VALIDATE_DEPTH,
            // ningún vector de movimiento. 0 = sin comprobación de aspecto; la elección la decide
            // la estadística de dibujado, que es la que sabe cuál es el buffer de la escena.
            ini = Ini.Set(ini, "DEPTH", "UseAspectRatioHeuristics", "0");
            ini = Ini.Set(ini, "GENERAL", "IntermediateCachePath", @".\.neuralfx-runtime\ReShade");
            ini = Ini.Set(ini, "SCREENSHOT", "SavePath", @".\.neuralfx-runtime\Capturas");
            string definitions = Ini.Get(ini, "GENERAL", "PreprocessorDefinitions");
            definitions = string.Join(",", definitions.Split(',').Where(x => x.Length > 0 && !x.StartsWith("DLSS5_MV_PROVIDER=", StringComparison.OrdinalIgnoreCase)));
            ini = Ini.Set(ini, "GENERAL", "PreprocessorDefinitions", MergePaths(definitions, "DLSS5_MV_PROVIDER=3"));
            files["ReShade.ini"] = Encoding.UTF8.GetBytes(ini);
            string reshadePreset = Read("ReShadePreset.ini");
            string techniques = Ini.Get(reshadePreset, "", "Techniques");
            string[] ours = { "Lumenite_Kernel@lumenite_Kernel.fx", "DLSS5_Feed@DLSS5_Feed.fx", "NeuralFX_CAS@NeuralFX_CAS.fx" };
            reshadePreset = Ini.Set(reshadePreset, "", "Techniques", InsertOwned(techniques, ours));
            string sorting = Ini.Get(reshadePreset, "", "TechniqueSorting");
            reshadePreset = Ini.Set(reshadePreset, "", "TechniqueSorting", InsertOwned(string.IsNullOrWhiteSpace(sorting) ? techniques : sorting, ours));
            reshadePreset = Ini.Set(reshadePreset, "DLSS5_Feed.fx", "PreprocessorDefinitions", "DLSS5_MV_PROVIDER=3");
            reshadePreset = Ini.Set(reshadePreset, "NeuralFX_CAS.fx", "Sharpening", sharpness.ToString(CultureInfo.InvariantCulture));
            files["ReShadePreset.ini"] = Encoding.UTF8.GetBytes(reshadePreset);
            using var stream = typeof(PipelineConfiguration).Assembly.GetManifestResourceStream("NeuralFX.Hub.Assets.NeuralFX_CAS.fx")!;
            using var output = new MemoryStream(); stream.CopyTo(output);
            files["reshade-shaders/Shaders/NeuralFX_CAS.fx"] = output.ToArray();
            return files;
        }
        internal static string InsertOwned(string text, string[] owned)
        {
            var original = text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            int anchor = original.FindIndex(x => owned.Contains(x, StringComparer.OrdinalIgnoreCase));
            if (anchor < 0) anchor = original.Count;
            int before = original.Take(anchor).Count(x => !owned.Contains(x, StringComparer.OrdinalIgnoreCase));
            var foreign = original.Where(x => !owned.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
            foreign.InsertRange(before, owned);
            return string.Join(",", foreign);
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
            string current = ""; string? found = null;
            foreach (string line in text.Replace("\r", "").Split('\n')) {
                string trim = line.Trim();
                if (trim.StartsWith('[') && trim.EndsWith(']')) current = trim[1..^1];
                int index = line.IndexOf('=');
                if (index > 0 && current.Equals(section,StringComparison.OrdinalIgnoreCase) && line[..index].Trim().Equals(key,StringComparison.OrdinalIgnoreCase)) {
                    string value = line[(index+1)..].Trim();
                    if (found != null && found != value) throw new InvalidDataException("Clave INI duplicada con valores distintos: [" + section + "] " + key);
                    found = value;
                }
            }
            return found ?? "";
        }
        public static string Set(string text, string section, string key, string value)
        {
            Get(text,section,key); // reject ambiguous input before changing anything
            var lines = new List<string>(); string scan = "";
            foreach (string line in text.Replace("\r", "").Split('\n')) {
                string trim = line.Trim(); if (trim.StartsWith('[') && trim.EndsWith(']')) scan = trim[1..^1];
                int equals = line.IndexOf('=');
                if (equals > 0 && scan.Equals(section,StringComparison.OrdinalIgnoreCase) && line[..equals].Trim().Equals(key,StringComparison.OrdinalIgnoreCase)) continue;
                lines.Add(line);
            }
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
