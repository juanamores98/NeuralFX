using System;
using System.Collections.Generic;
using System.Linq;
using NeuralFX.Hub.Models;

namespace NeuralFX.Hub.Services;

internal sealed record RollbackPreview(string GameDirectory, bool RestoresLegacy, string[] Restore, string[] Remove, string[] Preserve)
{
    public string[] AlreadyAbsent { get; init; } = Array.Empty<string>();
    public string Target => RestoresLegacy
        ? "Volver a la instalación antigua, tal como estaba antes de migrarla a este Hub."
        : "Volver al estado de los archivos anterior a la primera instalación registrada por este Hub.";
    public string Scope => "Solo afecta a los archivos registrados del pipeline en esta carpeta del juego. El Hub, el mod NeuralFX, las descargas, las partidas, los logs y los archivos ajenos se conservan.";
    public string Summary => $"Restaurar: {Restore.Length} archivos · Retirar: {Remove.Length} · Guardar modificaciones: {Preserve.Length}";
    public string Details => Target + "\n\n" +
        "No equivale a dejar el juego como recién instalado: si ya había ReShade u otros componentes, volverán a ese estado.\n\n" + Scope + "\n\n" +
        List("Restaurar desde copias verificadas", Restore) + "\n\n" + List("Retirar archivos añadidos por NeuralFX", Remove) + "\n\n" +
        List("Guardar modificaciones actuales en .neuralfx-preserved antes de restaurar", Preserve) +
        (AlreadyAbsent.Length == 0 ? "" : "\n\n" + List("Ya ausentes: solo se retiran del registro", AlreadyAbsent)) +
        (RestoresLegacy ? "\n\nTambién se recuperará el manifiesto antiguo. Sus componentes pueden seguir presentes tras restaurar." : "\n\nSe retirará el registro de esta instalación.");
    private static string List(string title, string[] files) => title + $" ({files.Length})\n" + (files.Length == 0 ? "Ninguno." : string.Join("\n", files.Select(x => "• " + x)));
    public static RollbackPreview Create(string root, InstallationManifest manifest, IReadOnlyDictionary<string, InstalledFileState> files) => new(root,
        manifest.PreviousManifestBackup != null,
        manifest.InstalledFiles.Where(manifest.BackedUpFiles.ContainsKey).ToArray(),
        manifest.InstalledFiles.Where(x => !manifest.BackedUpFiles.ContainsKey(x) && files.GetValueOrDefault(x.Replace('\\', '/')) != InstalledFileState.Missing).ToArray(),
        manifest.InstalledFiles.Where(x => files.GetValueOrDefault(x.Replace('\\', '/')) is InstalledFileState.Modified or InstalledFileState.ConfigurationChanged).ToArray())
        { AlreadyAbsent = manifest.InstalledFiles.Where(x => !manifest.BackedUpFiles.ContainsKey(x) && files.GetValueOrDefault(x.Replace('\\', '/')) == InstalledFileState.Missing).ToArray() };
}
