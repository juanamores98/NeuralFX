using System;
using System.IO;

namespace NeuralFX.Hub.Services
{
    internal static class ManagedPaths
    {
        public static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
                throw new InvalidDataException("Ruta relativa no válida: " + relative);
            foreach (string segment in relative.Replace('\\', '/').Split('/'))
                if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' '))
                    throw new InvalidDataException("La ruta contiene segmentos ambiguos: " + relative);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La ruta sale del directorio administrado: " + relative);
            for (string? p = path; p != null; p = Path.GetDirectoryName(p))
            {
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("No se modifican enlaces ni junctions: " + p);
                if (string.Equals(p.TrimEnd(Path.DirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) break;
            }
            return path;
        }

        public static void WriteAtomic(string path, string text)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(text);
                    writer.Flush();
                    stream.Flush(true);
                }
                File.Move(temporary, path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
