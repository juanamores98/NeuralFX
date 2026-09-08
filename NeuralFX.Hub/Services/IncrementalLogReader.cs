using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NeuralFX.Hub.Services;

// Bounded reads and retained text. Keep the UTF-8 decoder between polls, including split code points.
internal sealed class IncrementalLogReader
{
    internal const int Limit = 64 * 1024;
    private readonly byte[] _bytes = new byte[Limit];
    private readonly char[] _chars = new char[Limit];
    private readonly StringBuilder _text = new();
    private Decoder _decoder = Encoding.UTF8.GetDecoder();
    private long _position;
    private ulong _identity;
    private uint _volume;
    private byte[] _anchor = Array.Empty<byte>();
    public string Read(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(file.SafeFileHandle, out var info)) throw new IOException("No se pudo identificar el log.");
            ulong identity = ((ulong)info.IndexHigh << 32) | info.IndexLow;
            bool reset = identity != _identity || info.Volume != _volume || file.Length < _position;
            if (!reset && _anchor.Length > 0)
            {
                file.Position = _position - _anchor.Length;
                var previous = new byte[_anchor.Length];
                reset = file.Read(previous) != previous.Length || !previous.AsSpan().SequenceEqual(_anchor);
            }
            if (reset)
            {
                _text.Clear(); _decoder.Reset(); _position = Math.Max(0, file.Length - Limit); _anchor = Array.Empty<byte>();
                _identity = identity; _volume = info.Volume;
            }
            file.Position = _position;
            int count = file.Read(_bytes, 0, _bytes.Length);
            int skip = 0;
            if (reset && _position > 0) while (skip < count && (_bytes[skip] & 0xC0) == 0x80) skip++;
            _position += count;
            int chars = _decoder.GetChars(_bytes, skip, count - skip, _chars, 0, false);
            _text.Append(_chars, 0, chars);
            if (_text.Length > Limit) _text.Remove(0, _text.Length - Limit);
            if (count > 0)
            {
                int size = (int)Math.Min(64, _position); _anchor = new byte[size];
                file.Position = _position - size; file.ReadExactly(_anchor);
            }
            return _text.ToString();
        }
        catch (FileNotFoundException) { return "[Log todavía no creado]"; }
        catch (DirectoryNotFoundException) { return "[Carpeta no disponible]"; }
        catch (IOException ex) { return "[Lectura pendiente: " + ex.Message + "]"; }
        catch (UnauthorizedAccessException ex) { return "[" + ex.Message + "]"; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
}
