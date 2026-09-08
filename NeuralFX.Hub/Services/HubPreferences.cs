using System;
using System.IO;
using System.Text.Json;

namespace NeuralFX.Hub.Services;

internal sealed class HubPreferences
{
    public string? GameExecutable { get; set; }
    public PipelinePreset Preset { get; set; } = PipelinePreset.Native;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeuralFX", "hub-settings.json");
    public static HubPreferences Load()
    {
        try { return JsonSerializer.Deserialize<HubPreferences>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        ManagedPaths.WriteAtomic(FilePath, JsonSerializer.Serialize(this));
    }
}
