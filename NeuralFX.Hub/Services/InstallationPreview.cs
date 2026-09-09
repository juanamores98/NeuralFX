using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeuralFX.Hub.Models;
namespace NeuralFX.Hub.Services;
public sealed class InstallationPreview
{
    public Dictionary<string,string?> ExpectedBefore { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Description { get; private set; } = "";
    public static InstallationPreview Create(string root, List<DependencyItem> items, DependencyManagerService dependencies, PipelinePreset preset)
    {
        var preview = new InstallationPreview(); var text = new StringBuilder();
        var payload = new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(x=>!x.IsEmbedded && (x.IsRequired || x.IsInCache)))
            foreach (var file in dependencies.ReadPayload(item)) payload.Add(file.Key,file.Value);
        foreach (var file in PipelineConfiguration.Create(root,preset)) payload.Add(file.Key,file.Value);
        foreach (var file in payload)
        {
            string path=ManagedPaths.Resolve(root,file.Key);
            string? before=File.Exists(path)?DependencyManagerService.CalculateSha256(path):null;
            preview.ExpectedBefore[file.Key]=before;
            string after=DependencyManagerService.Hash(file.Value);
            text.AppendLine((before==null?"Añadir":before==after?"Conservar":"Actualizar")+": "+file.Key);
            if ((file.Key.EndsWith(".ini")||file.Key.EndsWith(".cfg")) && before!=after) {
                var oldLines=(File.Exists(path)?File.ReadAllText(path):"").Replace("\r","").Split('\n');
                var newLines=Encoding.UTF8.GetString(file.Value).Replace("\r","").Split('\n');
                foreach(string line in oldLines.Except(newLines).Where(x=>x.Contains('='))) text.AppendLine("  - "+line);
                foreach(string line in newLines.Except(oldLines).Where(x=>x.Contains('='))) text.AppendLine("  + "+line);
            }
        }
        text.AppendLine("\nSe guardarán backups antes de aplicar. La resolución de trabajo NR no reduce por sí sola el render 3D de Unity. Requiere reiniciar el juego.");
        preview.Description=text.ToString();return preview;
    }
    public void ValidateUnchanged(string root)
    {
        foreach(var pair in ExpectedBefore) {
            string path=ManagedPaths.Resolve(root,pair.Key);string? actual=File.Exists(path)?DependencyManagerService.CalculateSha256(path):null;
            if(actual!=pair.Value)throw new IOException("Cambió un archivo después de revisar el plan: "+pair.Key+". Vuelve a generar la vista previa.");
        }
    }
}
