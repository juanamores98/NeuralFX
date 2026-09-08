using System;
using System.Collections.Generic;
using System.Linq;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services;

internal static class InstallationStatus
{
    public static void Apply(DependencyItem item, IEnumerable<string> paths, IntegrityReport? report)
    {
        string[] names = paths.Select(x => x.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var states = names.Select(x => report?.Files.GetValueOrDefault(x, InstalledFileState.Unverified) ?? InstalledFileState.Unverified).ToArray();
        int present = states.Count(x => x != InstalledFileState.Missing);
        item.IsInGame = present == states.Length && report != null;
        item.GameState = report == null ? ComponentGameState.Unknown
            : present == 0 ? ComponentGameState.Missing
            : present != states.Length ? ComponentGameState.Incomplete
            : states.Contains(InstalledFileState.Unverified) ? ComponentGameState.Unknown
            : states.Contains(InstalledFileState.Unmanaged) ? ComponentGameState.Unmanaged
            : states.Contains(InstalledFileState.Modified) ? ComponentGameState.Modified
            : states.Contains(InstalledFileState.ConfigurationChanged) ? ComponentGameState.Configured
            : item.IsReady && !item.IsEmbedded && names.Any(x => item.AvailableChecksums.TryGetValue(x, out var hash) && report!.RecordedChecksums.GetValueOrDefault(x) != hash) ? ComponentGameState.DifferentCopy
            : ComponentGameState.Installed;
        item.GameStatusDetail = item.GameState switch
        {
            ComponentGameState.Unknown => "Pendiente de verificación",
            ComponentGameState.Missing => $"0 de {states.Length} archivos",
            ComponentGameState.Incomplete => $"{present} de {states.Length} archivos · reparar",
            ComponentGameState.Unmanaged => "Presente, sin registro verificable",
            ComponentGameState.Modified => "Difiere de lo instalado · reparar",
            ComponentGameState.Configured => "Configuración cambiada después de instalar",
            ComponentGameState.DifferentCopy => "La copia preparada difiere del juego",
            _ => $"{states.Length} de {states.Length} archivos verificados"
        };
        item.GameFilesDetail = string.Join("\n", names.Select((name, i) => name + " · " + Describe(states[i])));
    }
    private static string Describe(InstalledFileState state) => state switch
    {
        InstalledFileState.Verified => "verificado",
        InstalledFileState.Missing => "ausente",
        InstalledFileState.Unmanaged => "fuera del registro",
        InstalledFileState.Modified => "modificado",
        InstalledFileState.ConfigurationChanged => "configuración modificada",
        _ => "sin verificar"
    };
}
