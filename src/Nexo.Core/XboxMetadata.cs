using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Nexo.Core
{
    // Bounded metadata reads only. No mounting, extraction or executable loading.
    internal static class XboxMetadata
    {
        internal static uint Be32(byte[] b, int p) => (uint)b[p] << 24 | (uint)b[p + 1] << 16 | (uint)b[p + 2] << 8 | b[p + 3];
        private static uint Le32(byte[] b, int p) => BitConverter.ToUInt32(b, p);
        private static byte[] Read(Stream f, long at, int count)
        {
            if (at < 0 || count < 0 || at > f.Length - count) throw new InvalidDataException("Metadatos Xbox incompletos.");
            var b = new byte[count]; f.Position = at; int n = 0;
            while (n < count) { int r = f.Read(b, n, count - n); if (r == 0) throw new EndOfStreamException(); n += r; }
            return b;
        }
        internal static string XboxTitleId(string path, CancellationToken ct)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    ct.ThrowIfCancellationRequested();
                    var volume = Read(f, 32 * 2048, 32);
                    if (Encoding.ASCII.GetString(volume, 0, 20) != "MICROSOFT*XBOX*MEDIA") return null;
                    long sector = Le32(volume, 20); uint length = Le32(volume, 24);
                    if (length < 14 || length > 4 * 1024 * 1024) return null;
                    var table = Read(f, sector * 2048, (int)length);
                    var pending = new Stack<int>(); var seen = new HashSet<int>(); pending.Push(0);
                    while (pending.Count > 0 && seen.Count < 10000)
                    {
                        ct.ThrowIfCancellationRequested(); int p = pending.Pop();
                        if (!seen.Add(p) || p < 0 || p > table.Length - 14) continue;
                        int left = BitConverter.ToUInt16(table, p) * 4, right = BitConverter.ToUInt16(table, p + 2) * 4;
                        if (left != 0) pending.Push(left); if (right != 0) pending.Push(right);
                        int n = table[p + 13]; if (n > table.Length - p - 14 || (table[p + 12] & 16) != 0) continue;
                        if (!Encoding.ASCII.GetString(table, p + 14, n).Equals("default.xbe", StringComparison.OrdinalIgnoreCase)) continue;
                        long start = Le32(table, p + 4) * 2048L; uint size = Le32(table, p + 8);
                        if (size < 0x120 || start > f.Length - size) return null;
                        var header = Read(f, start, 0x120);
                        if (Encoding.ASCII.GetString(header, 0, 4) != "XBEH") return null;
                        long cert = (long)Le32(header, 0x118) - Le32(header, 0x104);
                        if (cert < 0x120 || cert > size - 12 || cert > 1024 * 1024) return null;
                        uint id = Le32(Read(f, start + cert, 12), 8);
                        return id == 0 ? null : id.ToString("X8");
                    }
                }
            }
            catch (Exception e) when (ArtworkMetadata.OptionalFailure(e) || e is InvalidDataException) { }
            return null;
        }
        internal static Tuple<string, string> Xbox360Container(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var h = Read(f, 0, 0x171a);
                    string magic = Encoding.ASCII.GetString(h, 0, 4);
                    if (magic != "CON " && magic != "LIVE" && magic != "PIRS") return null;
                    uint type = Be32(h, 0x344);
                    if (type != 0x4000 && type != 0x7000 && type != 0xd0000) return null; // disc/GOD/Arcade, not saves/DLC
                    uint header = Be32(h, 0x340), id = Be32(h, 0x360);
                    if (header < 0x171a || header > f.Length || id == 0) return null;
                    string title = Encoding.BigEndianUnicode.GetString(h, 0x411, 256).Split('\0')[0].Trim();
                    foreach (char c in title) if (char.IsControl(c)) return null;
                    return Tuple.Create(id.ToString("X8"), title.Length == 0 ? null : title);
                }
            }
            catch (Exception e) when (ArtworkMetadata.OptionalFailure(e) || e is InvalidDataException) { return null; }
        }
        internal static string XexTitleId(string path)
        {
            try
            {
                using (var f = File.OpenRead(path))
                {
                    var header = Read(f, 0, 24);
                    if (Encoding.ASCII.GetString(header, 0, 4) != "XEX2") return null;
                    uint count = Be32(header, 20); if (count > 256) return null;
                    var entries = Read(f, 24, (int)count * 8);
                    for (int i = 0; i < entries.Length; i += 8)
                        if (Be32(entries, i) == 0x40006)
                        {
                            uint offset = Be32(entries, i + 4); if (offset < 24 + entries.Length || offset > 1024 * 1024) return null;
                            uint id = Be32(Read(f, offset, 24), 12); return id == 0 ? null : id.ToString("X8");
                        }
                }
            }
            catch (Exception e) when (ArtworkMetadata.OptionalFailure(e) || e is InvalidDataException) { }
            return null;
        }
    }
}
