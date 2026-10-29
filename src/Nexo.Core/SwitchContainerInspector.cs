using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Nexo.Core
{
    // Structural checks only: no decryption, keys, NCA payload reads or subprocesses.
    // null means the bounded check passed, NOT that the content is a bootable base game.
    internal static class SwitchContainerInspector
    {
        private const int MaxEntries = 50000, MaxStrings = 4 * 1024 * 1024;
        internal static bool IsContainer(string path) => new[] { ".nsp", ".xci", ".nsz", ".ncz" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        internal static ArchiveEntry Inspect(string path, CancellationToken ct = default(CancellationToken))
        {
            long size = 0;
            ArchiveEntry Result(ArchiveState state, long? expected, string message) => new ArchiveEntry("Switch", path,
                GameScanner.CleanTitle(Path.GetFileNameWithoutExtension(path)), size, expected, state, message);
            ArchiveEntry Bad(string reason) => Result(ArchiveState.Unreadable, null, "Contenedor de Switch inválido o no reconocido: " + reason);
            ArchiveEntry Short(long expected, bool minimum = false) => Result(ArchiveState.Incomplete, expected,
                string.Format("Descarga incompleta: {0:0.##} MB de {1}{2:0.##} MB. Vuelve a descargarlo.", size / 1048576.0, minimum ? "al menos " : "", expected / 1048576.0));
            ArchiveEntry Compressed() => Result(ArchiveState.Unreadable, null, "NSZ comprimido: no compatible con Eden 0.2.1. Contiene datos NCZ; Nexo no los descomprime. Usa un NSP/XCI sin compresión.");
            try
            {
                ct.ThrowIfCancellationRequested();
                if (!IsContainer(path) || !SafePaths.IsRegularFile(path)) return Bad("no se puede leer el archivo.");
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream))
                {
                    size = stream.Length;
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension == ".ncz") return Compressed();
                    if (extension == ".xci")
                    {
                        var header = reader.ReadBytes(0x200);
                        if (header.Length < 0x104 || Encoding.ASCII.GetString(header, 0x100, 4) != "HEAD") return Bad("falta HEAD en la cabecera XCI.");
                        if (header.Length < 0x200) return Short(0x200, true);
                        // 0x118 is the final valid media unit (inclusive), not cart_type capacity.
                        ulong end = checked((BitConverter.ToUInt64(header, 0x118) + 1UL) * 512UL);
                        ulong hfs = BitConverter.ToUInt64(header, 0x130), hfsSize = BitConverter.ToUInt64(header, 0x138);
                        if (end > long.MaxValue || end < 0x200 || hfs < 0x200 || hfsSize < 16 || checked(hfs + hfsSize) > end)
                            return Bad("los límites de datos de la cabecera XCI no son coherentes.");
                        return (ulong)size < end ? Short((long)end) : null;
                    }

                    var pfs = reader.ReadBytes(16);
                    if (pfs.Length < 4 || Encoding.ASCII.GetString(pfs, 0, 4) != "PFS0") return Bad("falta la firma PFS0 del NSP.");
                    if (pfs.Length < 16) return Short(16, true);
                    uint count = BitConverter.ToUInt32(pfs, 4), stringSize = BitConverter.ToUInt32(pfs, 8);
                    if (count == 0 || count > MaxEntries || stringSize == 0 || stringSize > MaxStrings)
                        return Bad("la tabla PFS0 está vacía o supera los límites de comprobación (50 000 entradas y 4 MiB de nombres).");
                    long dataStart = checked(16L + 24L * count + stringSize);
                    if (size < dataStart) return Short(dataStart, true);
                    byte[] entries = reader.ReadBytes(checked((int)count * 24));
                    byte[] strings = reader.ReadBytes((int)stringSize);
                    if (entries.Length != (int)count * 24 || strings.Length != stringSize) return Short(dataStart, true);
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var ranges = new List<Tuple<ulong, ulong>>();
                    bool compressed = extension == ".nsz";
                    ulong maximum = 0;
                    var utf8 = new UTF8Encoding(false, true);
                    for (int i = 0; i < count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        int at = i * 24;
                        ulong offset = BitConverter.ToUInt64(entries, at), length = BitConverter.ToUInt64(entries, at + 8);
                        uint nameOffset = BitConverter.ToUInt32(entries, at + 16);
                        if (nameOffset >= strings.Length) return Bad("un nombre apunta fuera de la tabla PFS0.");
                        int stop = Array.IndexOf(strings, (byte)0, (int)nameOffset, Math.Min(1025, strings.Length - (int)nameOffset));
                        if (stop <= nameOffset) return Bad("un nombre PFS0 está vacío, sin terminar o es demasiado largo.");
                        string name = utf8.GetString(strings, (int)nameOffset, stop - (int)nameOffset);
                        if (name.Any(c => char.IsControl(c) || c == '/' || c == '\\') || name == "." || name == ".." || !names.Add(name))
                            return Bad("hay nombres PFS0 inválidos o duplicados.");
                        compressed |= name.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase);
                        ulong end = checked(offset + length);
                        maximum = Math.Max(maximum, end);
                        if (length != 0) ranges.Add(Tuple.Create(offset, end));
                    }
                    ulong previousEnd = 0;
                    foreach (var range in ranges.OrderBy(r => r.Item1))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (range.Item1 < previousEnd) return Bad("hay entradas PFS0 que se solapan.");
                        previousEnd = range.Item2;
                    }
                    ulong expected = checked((ulong)dataStart + maximum);
                    if (expected > long.MaxValue) return Bad("el tamaño PFS0 declarado no es representable.");
                    if ((ulong)size < expected) return Short((long)expected);
                    return compressed ? Compressed() : null;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OverflowException || ex is ArgumentException)
            { return Bad("no se pudo validar su cabecera y sus tablas."); }
        }
    }
}
