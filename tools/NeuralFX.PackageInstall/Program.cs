using System.Diagnostics;
using System.Text.Json;
using NeuralFX.Hub.Services;

if (args.Length != 2) throw new ArgumentException("Use: NeuralFX.PackageInstall <package directory> <NeuralFX mod directory>");
string source = Path.GetFullPath(args[0]), target = Path.GetFullPath(args[1]);
if (Path.GetFileName(target.TrimEnd('\\')) != "NeuralFX") throw new ArgumentException("The destination must be the NeuralFX mod directory.");
string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
string modRelative = Path.GetRelativePath(local, target);
ManagedPaths.Resolve(local, modRelative);
string hubRelative = "NeuralFX/Hub";
static void RequireClosed()
{
    if (HardwareDiagnosticsService.IsCitiesSkylinesRunning()) throw new IOException("Close Cities: Skylines before updating its mod.");
    var hubs = Process.GetProcessesByName("NeuralFX.Hub");
    try { if (hubs.Length > 0) throw new IOException("Close NeuralFX Hub before updating it."); }
    finally { foreach (var hub in hubs) hub.Dispose(); }
}
RequireClosed();
var files = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(source, "deployment-manifest.json"))) ?? throw new InvalidDataException("Missing package manifest.");
if (!files.ContainsKey("NeuralFX.dll") || !files.ContainsKey("Hub/NeuralFX.Hub.exe")) throw new InvalidDataException("Incomplete mod package.");
var payload = new Dictionary<string, byte[]>();
foreach (var file in files)
{
    string path = ManagedPaths.Resolve(source, file.Key);
    ManagedPaths.Resolve(local, file.Key.StartsWith("Hub/", StringComparison.Ordinal) ? hubRelative + file.Key.Substring(3) : modRelative + "/" + file.Key);
    if (DependencyManagerService.CalculateSha256(path) != file.Value) throw new InvalidDataException("Package hash mismatch: " + file.Key);
    payload.Add(file.Key, File.ReadAllBytes(path));
}
payload.Add("deployment-manifest.json", File.ReadAllBytes(Path.Combine(source, "deployment-manifest.json")));
Directory.CreateDirectory(target);
using var lease = new InstallationLease(local);
using var transaction = new FileTransaction(local);
string backup = "NeuralFX/Backups/Packages/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
// Junto al escáner de mods de CS1 solo vive lo que el juego necesita, más las licencias que
// acompañan al ensamblado y el registro de lo instalado. La documentación, el instalador, el
// lanzador y el catálogo se quedan en el paquete extraído: ahí es donde se usan, y de hecho
// Install-NeuralFX.ps1 se niega a ejecutarse desde la carpeta Mods.
static bool BelongsInModDirectory(string key) =>
    key is "NeuralFX.dll" or "LICENSE" or "NOTICE" or "deployment-manifest.json";
// El lanzador vive junto al Hub, que es lo que abre. Dentro de Mods no le sirve a nadie.
static bool BelongsBesideHub(string key) => key is "Iniciar-NeuralFX-Hub.bat";

foreach (var file in payload)
{
    bool hub = file.Key.StartsWith("Hub/", StringComparison.Ordinal);
    bool besideHub = !hub && BelongsBesideHub(file.Key);
    if (!hub && !besideHub && !BelongsInModDirectory(file.Key)) continue;
    string relative = hub ? hubRelative + file.Key.Substring(3)
        : besideHub ? hubRelative + "/" + file.Key
        : modRelative + "/" + file.Key;
    string path = ManagedPaths.Resolve(local, relative);
    if (File.Exists(path)) transaction.Write(backup + "/" + file.Key, File.ReadAllBytes(path));
    transaction.Write(relative, file.Value);
}
// Older packages put desktop assemblies and backups where CS1's mod scanner loads every DLL.
// Move those known directories into the private backup area in the same transaction.
var legacyDirectories = new[]
{
    Path.Combine(target, "Hub"), Path.Combine(target, ".neuralfx-previous"),
    // Paquetes anteriores copiaban aquí el ZIP entero. Se archivan y se retiran.
    Path.Combine(target, "docs"), Path.Combine(target, "licenses"),
};
foreach (string name in new[] { "README.md", "Iniciar-NeuralFX-Hub.bat", "Install-NeuralFX.ps1", "capabilities.json" })
{
    string path = Path.Combine(target, name);
    if (!File.Exists(path)) continue;
    string relative = Path.GetRelativePath(local, path);
    ManagedPaths.Resolve(local, relative);
    transaction.Write(backup + "/legacy/" + name, File.ReadAllBytes(path));
    transaction.Write(relative, null);
}
foreach (string directory in legacyDirectories.Where(Directory.Exists))
    foreach (string path in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
    {
        string relative = Path.GetRelativePath(local, path);
        ManagedPaths.Resolve(local, relative);
        transaction.Write(backup + "/legacy/" + Path.GetRelativePath(target, path), File.ReadAllBytes(path));
        transaction.Write(relative, null);
    }
RequireClosed();
transaction.Commit();
foreach (string directory in legacyDirectories.Where(Directory.Exists))
    foreach (string path in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories).Append(directory).OrderByDescending(x => x.Length))
    {
        ManagedPaths.Resolve(local, Path.GetRelativePath(local, path));
        if (!Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
    }
Console.WriteLine("Package installed: " + target);
Console.WriteLine("Hub installed outside the mod scanner: " + Path.Combine(local, hubRelative));
Console.WriteLine("Previous files retained: " + Path.Combine(local, backup));
