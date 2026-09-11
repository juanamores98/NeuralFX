using System.Security.Cryptography;
using System.Text.Json;
using NeuralFX.Hub.Services;

// Refresca en el juego los componentes del pipeline recién construidos.
//
// <b>Por qué existe.</b> `tools/package.ps1 -Deploy` instala el mod y el Hub, pero el addon
// nativo de la carpeta del juego lo escribía únicamente el botón del Hub. Resultado: cada vez
// que cambiaba el addon había que pedirle al usuario que cerrase el juego, abriera el Hub y
// pulsara «Aplicar preset / reinstalar». Tres entregas seguidas terminaron con esa frase, y es
// trabajo que la herramienta puede hacer sola.
//
// <b>No es una copia a mano.</b> Usa el mismo `InstallationEngineService` que el botón, con su
// diario transaccional, sus copias de seguridad y su manifiesto. Un `Copy-Item` dejaría el
// manifiesto mintiendo y el Hub marcaría el archivo como modificado.
//
// <b>Respeta lo que el usuario eligió.</b> El ejecutable y el preset salen de
// `hub-settings.json`, no de valores por defecto: reinstalar no debe cambiarle el preset.

// Se recorre una sola vez y en orden: el valor que sigue a una opción es suyo, nunca la ruta
// posicional. Buscar «el primer argumento que no empieza por guion» tomaba el valor de --addon
// como carpeta del juego.
string? requestedRoot = null, expectedAddon = null;
bool onlyIfStale = false, cleanReinstall = false;
for (int i = 0; i < args.Length; i++)
{
    if (string.Equals(args[i], "--si-hace-falta", StringComparison.OrdinalIgnoreCase)) onlyIfStale = true;
    else if (string.Equals(args[i], "--reinstalar", StringComparison.OrdinalIgnoreCase)) cleanReinstall = true;
    else if (string.Equals(args[i], "--addon", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) expectedAddon = args[++i];
    else if (args[i].StartsWith('-')) { Console.Error.WriteLine("Opción desconocida: " + args[i]); return 1; }
    else if (requestedRoot is null) requestedRoot = args[i];
    else { Console.Error.WriteLine("Sobra un argumento: " + args[i]); return 1; }
}

string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
string settingsPath = Path.Combine(local, "NeuralFX", "hub-settings.json");
string? preferredExe = null;
int presetIndex = 0;
if (File.Exists(settingsPath))
{
    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (document.RootElement.TryGetProperty("GameExecutable", out var exe) && exe.ValueKind == JsonValueKind.String)
            preferredExe = exe.GetString();
        if (document.RootElement.TryGetProperty("Preset", out var stored) && stored.TryGetInt32(out int value))
            presetIndex = value;
    }
    catch (JsonException) { /* Unas preferencias ilegibles no deben impedir instalar. */ }
}

string root;
if (!string.IsNullOrWhiteSpace(requestedRoot)) root = Path.GetFullPath(requestedRoot);
else
{
    var info = new HardwareDiagnosticsService().RunDiagnostics(preferredExe);
    if (!info.GameFound)
    {
        Console.Error.WriteLine("No se encontró Cities.exe. Elígelo en el Hub o pasa la carpeta del juego como argumento.");
        return 2;
    }
    root = Path.GetDirectoryName(info.GameExePath)!;
}

if (HardwareDiagnosticsService.IsCitiesSkylinesRunning())
{
    Console.Error.WriteLine("Cities: Skylines está abierto. Ciérralo antes de refrescar el pipeline.");
    return 3;
}

static string? Digest(string path)
{
    if (!File.Exists(path)) return null;
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

string installedAddon = Path.Combine(root, "dlss5-feed.addon64");
string? before = Digest(installedAddon);
string? wanted = expectedAddon is null ? null : Digest(expectedAddon);

// El modo opcional de refresco evita reescrituras; --reinstalar siempre hace el ciclo completo.
if (!cleanReinstall && onlyIfStale && wanted is not null && before == wanted)
{
    Console.WriteLine("El pipeline del juego ya tiene estos componentes; no se reinstala.");
    return 0;
}

var manager = new DependencyManagerService();
var items = manager.GetInitialDependencies();
var preset = Enum.IsDefined(typeof(PipelinePreset), presetIndex) ? (PipelinePreset)presetIndex : PipelinePreset.Native;
Dictionary<string, byte[]>? configuration = null;
if (cleanReinstall)
{
    // Verify every required payload and preserve configuration before removing anything.
    await manager.DownloadAllPublicMissingAsync(items, Console.WriteLine);
    foreach (var item in items.Where(x => !x.IsEmbedded && (x.IsRequired || x.IsInCache)))
        manager.ReadPayload(item);
    configuration = PipelineConfiguration.Create(root, preset);
    var plan = UninstallService.Inspect(root);
    Console.WriteLine("Desinstalación completa mediante NeuralFX Hub: " + plan.Summary);
    var removal = await new UninstallService().UninstallAsync(plan, Console.WriteLine);
    if (!removal.Success) { Console.Error.WriteLine(removal.Message); return 7; }
}
Console.WriteLine($"Refrescando el pipeline en {root} · preset {preset}");
if (!await new InstallationEngineService(manager).InstallAsync(root, items, Console.WriteLine, preset: preset, preparedConfiguration: configuration))
{
    Console.Error.WriteLine("La instalación no se completó. El estado anterior queda recuperable desde el Hub.");
    return 4;
}

string? after = Digest(installedAddon);
var installedManifest = ManifestStore.Read(root);
foreach (var file in installedManifest.FileChecksums)
    if (Digest(ManagedPaths.Resolve(root, file.Key)) != file.Value)
        throw new IOException("El archivo instalado no coincide con su manifiesto: " + file.Key);
foreach (var item in items.Where(x => x.SourceType == "Bundled"))
    foreach (var file in manager.ReadPayload(item))
        if (Digest(ManagedPaths.Resolve(root, file.Key)) != DependencyManagerService.Hash(file.Value))
            throw new IOException("El componente instalado no coincide con esta build: " + file.Key);
if (after is null)
{
    Console.Error.WriteLine("La instalación terminó pero no hay addon nativo en la carpeta del juego.");
    return 5;
}
// Comprobar lo que quedó escrito, no lo que la instalación dijo que haría.
if (wanted is not null && after != wanted)
{
    Console.Error.WriteLine($"El addon instalado no coincide con el construido: {after[..16]} frente a {wanted[..16]}.");
    return 6;
}
Console.WriteLine(before == after
    ? $"Pipeline verificado, sin cambios en el addon nativo ({after[..16]}…)."
    : $"Addon nativo actualizado: {(before ?? "ausente")[..Math.Min(16, (before ?? "ausente").Length)]}… → {after[..16]}…");
return 0;
