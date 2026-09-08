using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services;

internal sealed class ReleaseUpdateService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    public async Task<string> CheckAsync(IEnumerable<DependencyItem> items)
    {
        var messages = new List<string>();
        foreach (var item in items.Where(x => !string.IsNullOrEmpty(x.ReleaseRepository)))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/" + item.ReleaseRepository + "/releases?per_page=100");
            request.Headers.UserAgent.ParseAdd("NeuralFX/2.0");
            using var response = await Client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string? tag = LatestTag(json.RootElement, item.ReleaseTagPrefix);
            if (tag == null) { messages.Add(item.DisplayName + ": sin publicaciones coincidentes en la consulta"); continue; }
            bool same = Normalize(tag) == Normalize(item.Version);
            messages.Add(item.DisplayName + ": " + (same ? "versión fijada al día" : "publicación disponible " + tag + " · catálogo fijado en " + item.Version));
        }
        messages.Add("Las novedades se consultan en GitHub. Las actualizaciones instalables requieren un catálogo compatible con hashes verificados.");
        return string.Join("\n", messages);
    }
    internal static string? LatestTag(JsonElement releases, string prefix) => releases.EnumerateArray()
        .Where(x => !x.GetProperty("draft").GetBoolean())
        .Select(x => x.GetProperty("tag_name").GetString())
        .FirstOrDefault(tag => tag != null && tag.StartsWith(prefix, StringComparison.Ordinal));
    private static string Normalize(string? value) => (value ?? "").TrimStart('v', 'V');
}
