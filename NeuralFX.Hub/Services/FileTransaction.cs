using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralFX.Hub.Services
{
    // One journal on the target volume. Recovery is idempotent, including a crash during recovery.
    internal sealed class FileTransaction : IDisposable
    {
        private const string Folder = ".neuralfx-transaction";
        private readonly string _root;
        private readonly string _directory;
        private readonly Journal _journal = new();
        private readonly List<(string Path, byte[]? Data)> _writes = new();
        private readonly List<string> _directories = new();

        public FileTransaction(string root)
        {
            _root = Path.GetFullPath(root);
            Recover(root);
            _directory = ManagedPaths.Resolve(root, Folder);
            Directory.CreateDirectory(_directory);
            Save();
        }

        public void Write(string relative, byte[]? data)
        {
            relative = Path.GetRelativePath(_root, ManagedPaths.Resolve(_root, relative));
            if (relative.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta reservada.");
            if (_writes.Any(x => string.Equals(x.Path, relative, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Destino duplicado: " + relative);
            _writes.Add((relative, data));
        }

        public void Commit(Action<int>? afterWrite = null)
        {
            foreach (string relative in _directories)
            {
                string path = ManagedPaths.Resolve(_root, relative);
                if (File.Exists(path)) throw new IOException("Se esperaba una carpeta: " + relative);
                TrackMissingDirectories(path);
            }
            foreach (var write in _writes)
            {
                string target = ManagedPaths.Resolve(_root, write.Path);
                if (Directory.Exists(target)) throw new IOException("El destino es una carpeta: " + target);
                string snapshot = _journal.Files.Count.ToString();
                bool exists = File.Exists(target);
                if (exists) CopyDurable(target, Path.Combine(_directory, snapshot));
                _journal.Files.Add(new Entry { Path = write.Path, Snapshot = snapshot, Existed = exists });
                TrackMissingDirectories(Path.GetDirectoryName(target));
            }
            Save(); // All recovery data is durable before the first mutation.
            foreach (string relative in _directories) Directory.CreateDirectory(ManagedPaths.Resolve(_root, relative));
            for (int i = 0; i < _writes.Count; i++)
            {
                var write = _writes[i];
                string target = ManagedPaths.Resolve(_root, write.Path);
                if (write.Data == null) { if (File.Exists(target)) File.Delete(target); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string stage = Path.Combine(_directory, "stage");
                    using (var file = new FileStream(stage, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                    { file.Write(write.Data); file.Flush(true); }
                    File.Move(stage, target, true);
                }
                afterWrite?.Invoke(i);
            }
            _journal.Committed = true;
            Save();
        }
        public void EnsureDirectory(string relative)
        {
            ManagedPaths.Resolve(_root, relative);
            if (relative.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ruta reservada.");
            _directories.Add(relative);
        }
        private void TrackMissingDirectories(string? directory)
        {
            for (string? dir = directory; dir != null && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
            {
                string relative = Path.GetRelativePath(_root, dir);
                ManagedPaths.Resolve(_root, relative);
                if (!_journal.CreatedDirectories.Contains(relative)) _journal.CreatedDirectories.Add(relative);
            }
        }

        private void Save() => ManagedPaths.WriteAtomic(Path.Combine(_directory, "journal.json"), JsonSerializer.Serialize(_journal));
        private static void CopyDurable(string source, string destination)
        {
            using var input = File.OpenRead(source);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            input.CopyTo(output); output.Flush(true);
        }

        public static void Recover(string root)
        {
            string directory = ManagedPaths.Resolve(root, Folder);
            if (!Directory.Exists(directory)) return;
            string journalPath = ManagedPaths.Resolve(root, Folder + "/journal.json");
            if (!File.Exists(journalPath))
                throw new IOException("Preparación interrumpida sin diario: conservar y revisar " + directory);
            var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? throw new InvalidDataException("Diario vacío.");
            if (journal.Files == null || journal.CreatedDirectories == null || journal.Files.Any(x => x == null)) throw new InvalidDataException("Diario incompleto.");
            if (journal.Files.Select(x => ManagedPaths.Resolve(root, x.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Files.Count ||
                journal.Files.Select(x => x.Snapshot).Distinct(StringComparer.Ordinal).Count() != journal.Files.Count) throw new InvalidDataException("Entradas de recuperación duplicadas.");
            // Validate the complete journal before modifying anything.
            foreach (var entry in journal.Files)
            {
                ManagedPaths.Resolve(root, entry.Path);
                if (entry.Path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Destino de recuperación reservado.");
                if (!int.TryParse(entry.Snapshot, out int index) || index < 0) throw new InvalidDataException("Snapshot inválido.");
                string snapshot = ManagedPaths.Resolve(directory, entry.Snapshot);
                if (!journal.Committed && entry.Existed && !File.Exists(snapshot)) throw new IOException("Falta un snapshot de recuperación.");
            }
            foreach (string dir in journal.CreatedDirectories) ManagedPaths.Resolve(root, dir);
            if (!journal.Committed)
            {
                foreach (var entry in journal.Files.AsEnumerable().Reverse())
                {
                    string path = ManagedPaths.Resolve(root, entry.Path);
                    if (entry.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        string stage = Path.Combine(directory, "restore");
                        CopyDurable(Path.Combine(directory, entry.Snapshot), stage);
                        File.Move(stage, path, true);
                    }
                    else if (File.Exists(path)) File.Delete(path);
                }
                foreach (string relative in journal.CreatedDirectories.OrderByDescending(x => x.Length))
                {
                    string dir = ManagedPaths.Resolve(root, relative);
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
            }
            // Do not follow unexpected links when cleaning the private journal directory.
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                ManagedPaths.Resolve(directory, Path.GetFileName(path));
                if (Directory.Exists(path)) throw new IOException("Carpeta inesperada en el diario.");
            }
            Directory.Delete(directory, true);
        }

        public void Dispose()
        {
            Recover(_root);
        }

        internal sealed class Journal
        {
            public bool Committed { get; set; }
            public List<Entry> Files { get; set; } = new();
            public List<string> CreatedDirectories { get; set; } = new();
        }
        internal sealed class Entry
        {
            public string Path { get; set; } = "";
            public string Snapshot { get; set; } = "";
            public bool Existed { get; set; }
        }
    }
}
