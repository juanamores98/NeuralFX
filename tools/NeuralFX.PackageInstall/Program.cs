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
foreach (var file in payload)
{
    string relative = file.Key.StartsWith("Hub/", StringComparison.Ordinal) ? hubRelative + file.Key.Substring(3) : modRelative + "/" + file.Key;
    string path = ManagedPaths.Resolve(local, relative);
    if (File.Exists(path)) transaction.Write(backup + "/" + file.Key, File.ReadAllBytes(path));
    transaction.Write(relative, file.Value);
}
// Older packages put desktop assemblies and backups where CS1's mod scanner loads every DLL.
// Move those known directories into the private backup area in the same transaction.
var legacyDirectories = new[] { Path.Combine(target, "Hub"), Path.Combine(target, ".neuralfx-previous") };
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
