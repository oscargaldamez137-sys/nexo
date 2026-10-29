using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace Nexo.Core
{
    internal static class ArchiveInspector
    {
        internal static bool IsArchive(string path) => new[] { ".7z", ".zip", ".rar" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        internal static bool IsArchive(string systemId, string path) => IsArchive(path) || IsPs3Iso(systemId, path) || Ps4Package.IsPkg(systemId, path);
        internal static bool IsPs3Iso(string systemId, string path) => systemId == "PS3" && string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase);

        internal static ArchiveEntry Inspect(string systemId, string path, CancellationToken ct = default(CancellationToken))
        {
            ct.ThrowIfCancellationRequested();
            if (Ps4Package.IsPkg(systemId, path)) return Ps4Package.Inspect(path, ct);
            if (IsPs3Iso(systemId, path)) return Ps3IsoInspector.Inspect(path, ct);
            long size = 0;
            ArchiveEntry Result(ArchiveState state, long? expected, string message) => new ArchiveEntry(systemId, path,
                GameScanner.CleanTitle(Path.GetFileNameWithoutExtension(path)), size, expected, state, message);
            ArchiveEntry Bad() => Result(ArchiveState.Unreadable, null, "No se puede leer o reconocer este archivo comprimido.");
            ArchiveEntry Short(long? expected = null) => Result(ArchiveState.Incomplete, expected,
                expected.HasValue ? string.Format("Descarga incompleta: {0:0} MB de {1:0} MB. Vuelve a descargarlo.", size / 1048576.0, expected.Value / 1048576.0)
                : "Descarga incompleta: falta el final del archivo. Vuelve a descargarlo.");
            try
            {
                if (!IsArchive(path) || !SafePaths.IsRegularFile(path)) return Bad();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream))
                {
                    size = stream.Length;
                    var header = reader.ReadBytes(32);
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension == ".7z")
                    {
                        if (!Starts(header, new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })) return Bad();
                        if (header.Length < 32) return Short(32);
                        if (Crc32(header, 12, 20) != BitConverter.ToUInt32(header, 8)) return Bad();
                        ulong total = checked(32UL + BitConverter.ToUInt64(header, 12) + BitConverter.ToUInt64(header, 20));
                        if (total > long.MaxValue) return Bad();
                        if (size < (long)total) return Short((long)total);
                    }
                    else if (extension == ".zip")
                    {
                        if (!Starts(header, new byte[] { 0x50, 0x4b, 3, 4 }) && !Starts(header, new byte[] { 0x50, 0x4b, 5, 6 })) return Bad();
                        int count = (int)Math.Min(size, 65557);
                        stream.Position = size - count;
                        var tail = reader.ReadBytes(count);
                        int end = -1;
                        for (int i = tail.Length - 22; i >= 0; i--)
                            if (BitConverter.ToUInt32(tail, i) == 0x06054b50 && i + 22 + BitConverter.ToUInt16(tail, i + 20) == tail.Length) { end = i; break; }
                        if (end < 0) return Short();
                        if (BitConverter.ToUInt16(tail, end + 4) != 0 || BitConverter.ToUInt16(tail, end + 6) != 0)
                            return Result(ArchiveState.Unreadable, null, "No se admiten ZIP divididos en varias partes.");
                        uint centralSize = BitConverter.ToUInt32(tail, end + 12), centralOffset = BitConverter.ToUInt32(tail, end + 16);
                        // ZIP64 sentinel fields are checked fully by libarchive before extraction.
                        if (centralSize != uint.MaxValue && centralOffset != uint.MaxValue &&
                            (ulong)centralSize + centralOffset > (ulong)(size - count + end)) return Bad();
                    }
                    else if (!Starts(header, new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 0 }) &&
                             !Starts(header, new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0 })) return Bad();
                }
                return Result(ArchiveState.Ready, null, "Archivo comprimido pendiente. Pulsa Descomprimir; se comprobará su contenido durante la extracción.");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OverflowException || ex is ArgumentException) { return Bad(); }
        }

        private static bool Starts(byte[] bytes, byte[] signature) => bytes.Length >= signature.Length && signature.Where((b, i) => b != bytes[i]).Any() == false;
        internal static uint Crc32(byte[] bytes, int offset, int count)
        {
            uint crc = uint.MaxValue;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= bytes[i];
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            return ~crc;
        }
    }
}
