using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Nexo.Core
{
    // Bounded ISO9660 directory lookup: no subprocess, payload scan or decryption while
    // browsing. Extraction still validates libarchive's complete listing independently.
    internal static class Ps3IsoInspector
    {
        private const int Sector = 2048;
        private sealed class Entry { internal long Offset, Length; internal bool Directory; }
        // Used for PS2 SYSTEM.CNF: read just PVD, bounded root directory and the tiny file.
        // No PS3 signature is required here; raw BIN/CHD/UDF-only images are unsupported.
        internal static byte[] ReadRootFile(string path, string name, int maxBytes, CancellationToken ct)
            => ReadFile(path, new[] { name }, maxBytes, ct);

        internal static byte[] ReadFile(string path, string[] names, int maxBytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (names == null || names.Length == 0 || names.Length > 8 || maxBytes < 0 || maxBytes > 8 * 1024 * 1024) return null;
            if (!SafePaths.IsRegularFile(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length < 17 * Sector) return null;
                var pvd = Read(stream, 16 * Sector, Sector);
                if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001" || pvd[6] != 1 ||
                    pvd[128] != 0 || pvd[129] != 8 || pvd[130] != 8 || pvd[131] != 0) return null;
                long volume = Number(pvd, 80) * Sector;
                if (volume > stream.Length || volume < 17 * Sector) return null;
                var current = Record(pvd, 156, volume);
                int budget = 50000;
                foreach (string name in names)
                {
                if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0) return null;
                if (!current.Directory || current.Length > 4 * 1024 * 1024) return null;
                var directory = Read(stream, current.Offset, (int)current.Length);
                Entry found = null;
                for (int pos = 0; pos < directory.Length;)
                {
                    ct.ThrowIfCancellationRequested();
                    if (--budget < 0) return null;
                    int length = directory[pos];
                    if (length == 0) { pos = (pos / Sector + 1) * Sector; continue; }
                    if (length < 34 || pos + length > directory.Length || pos % Sector + length > Sector) return null;
                    int namesize = directory[pos + 32];
                    if (namesize == 0 || 33 + namesize > length) return null;
                    string entryName = Encoding.ASCII.GetString(directory, pos + 33, namesize).Split(';')[0];
                    if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (found != null) return null;
                        found = Record(directory, pos, volume);
                    }
                    pos += length;
                }
                if (found == null) return null;
                current = found;
                }
                ct.ThrowIfCancellationRequested();
                if (current.Directory || current.Length > maxBytes) return null;
                return Read(stream, current.Offset, (int)current.Length);
            }
        }
        internal static ArchiveEntry Inspect(string path, CancellationToken ct)
        {
            long size = 0;
            ArchiveEntry Result(ArchiveState state, long? expected, string message) => new ArchiveEntry("PS3", path,
                GameScanner.CleanTitle(Path.GetFileNameWithoutExtension(path)), size, expected, state, message);
            ArchiveEntry Short(long expected) => Result(ArchiveState.Incomplete, expected,
                string.Format("Descarga incompleta: {0:0} MB de {1:0} MB. Vuelve a descargarlo.", size / 1048576.0, expected / 1048576.0));
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!SafePaths.IsRegularFile(path)) throw new IOException();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    size = stream.Length;
                    if (size < 0x80c || Encoding.ASCII.GetString(Read(stream, 0x800, 12)) != "PlayStation3")
                        return Result(ArchiveState.Unreadable, null, "La ISO no contiene la firma PlayStation3 esperada.");
                    if (size < 17 * Sector) return Short(17 * Sector);
                    var pvd = Read(stream, 16 * Sector, Sector);
                    if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001" || pvd[6] != 1 ||
                        pvd[128] != 0 || pvd[129] != 8 || pvd[130] != 8 || pvd[131] != 0) throw new IOException();
                    long volume = Number(pvd, 80) * Sector;
                    if (volume < 17 * Sector) throw new IOException();
                    if (size < volume) return Short(volume);
                    var current = Record(pvd, 156, volume);
                    int budget = 50000;
                    foreach (string name in new[] { "PS3_GAME", "USRDIR", "EBOOT.BIN" })
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!current.Directory || current.Length > 4 * 1024 * 1024) throw new IOException();
                        var directory = Read(stream, current.Offset, (int)current.Length);
                        Entry found = null;
                        for (int pos = 0; pos < directory.Length;)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (--budget < 0) throw new IOException();
                            int length = directory[pos];
                            if (length == 0) { pos = (pos / Sector + 1) * Sector; continue; }
                            if (length < 34 || pos + length > directory.Length || pos % Sector + length > Sector) throw new IOException();
                            int namesize = directory[pos + 32];
                            if (namesize == 0 || 33 + namesize > length) throw new IOException();
                            string entryName = Encoding.ASCII.GetString(directory, pos + 33, namesize);
                            int version = entryName.IndexOf(';');
                            if (version >= 0) entryName = entryName.Substring(0, version);
                            if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                            {
                                if (found != null) throw new IOException();
                                found = Record(directory, pos, volume);
                            }
                            pos += length;
                        }
                        if (found == null) return Result(ArchiveState.Unreadable, null, "No se encuentra PS3_GAME/USRDIR/EBOOT.BIN en la ISO.");
                        current = found;
                    }
                    if (current.Directory || current.Length < 4) throw new IOException();
                    var magic = Read(stream, current.Offset, 4);
                    if (magic[0] != 83 || magic[1] != 67 || magic[2] != 69 || magic[3] != 0)
                        return Result(ArchiveState.Unreadable, null, "ISO cifrada: RPCS3 necesita una ISO descifrada (o la clave del disco)");
                    return Result(ArchiveState.Ready, volume, "ISO de PS3 pendiente. Pulsa Descomprimir para extraerla; después se enviará la ISO a la Papelera. La cabecera EBOOT es legible; no se ha comprobado todo el disco.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException)
            { return Result(ArchiveState.Unreadable, null, "No se puede validar la estructura ISO9660 de este disco de PS3."); }
        }
        private static Entry Record(byte[] data, int at, long volume)
        {
            if (at + 34 > data.Length || data[at] < 34 || at + data[at] > data.Length ||
                data[at + 1] != 0 || data[at + 26] != 0 || data[at + 27] != 0 || (data[at + 25] & 0x80) != 0) throw new IOException();
            long offset = Number(data, at + 2) * Sector, length = Number(data, at + 10);
            if (offset < 17 * Sector || length == 0 || offset + length > volume) throw new IOException();
            return new Entry { Offset = offset, Length = length, Directory = (data[at + 25] & 2) != 0 };
        }
        private static long Number(byte[] data, int at)
        {
            uint little = BitConverter.ToUInt32(data, at);
            uint big = (uint)data[at + 4] << 24 | (uint)data[at + 5] << 16 | (uint)data[at + 6] << 8 | data[at + 7];
            if (little != big) throw new IOException();
            return little;
        }
        private static byte[] Read(Stream stream, long offset, int count)
        {
            var bytes = new byte[count]; stream.Position = offset; int used = 0;
            while (used < count) { int read = stream.Read(bytes, used, count - used); if (read == 0) throw new EndOfStreamException(); used += read; }
            return bytes;
        }
    }
}
