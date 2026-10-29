using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Nexo.Core
{
    // Independent bounded random-access readers. No external executable or extraction library.
    internal abstract class PkgData
    {
        internal long Length;
        internal byte[] Read(long at, int count)
        {
            if (at < 0 || count < 0 || at > Length || count > Length - at) throw new InvalidDataException("Lectura fuera de los límites del PKG/PFS.");
            return ReadCore(at, count);
        }
        protected abstract byte[] ReadCore(long at, int count);
        internal static long Long(byte[] b, int at) { var n = BitConverter.ToUInt64(b, at); if (n > long.MaxValue) throw new InvalidDataException("Tamaño PFS no válido."); return (long)n; }
    }
    internal sealed class PkgSlice : PkgData
    {
        private readonly PkgData parent; private readonly long offset;
        internal PkgSlice(PkgData parent, long offset, long size)
        {
            if (offset < 0 || size < 0 || offset > parent.Length || size > parent.Length - offset) throw new InvalidDataException("Imagen PKG incompleta.");
            this.parent = parent; this.offset = offset; Length = size;
        }
        protected override byte[] ReadCore(long at, int count) => parent.Read(offset + at, count);
    }
    internal sealed class PkgFile : PkgData, IDisposable
    {
        private readonly FileStream file;
        internal PkgFile(string path) { file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); Length = file.Length; }
        protected override byte[] ReadCore(long at, int count)
        {
            var bytes = new byte[count]; file.Position = at;
            for (int n = 0; n < count;) { int read = file.Read(bytes, n, count - n); if (read == 0) throw new EndOfStreamException(); n += read; }
            return bytes;
        }
        public void Dispose() => file.Dispose();
    }
    internal sealed class PkgXts : PkgData, IDisposable
    {
        private readonly PkgData parent; private readonly long start;
        private readonly Aes data, tweak; private readonly ICryptoTransform decrypt, encrypt;
        private long cached = -1; private byte[] page;
        internal PkgXts(PkgData parent, int blockSize, byte[] key)
        {
            this.parent = parent; Length = parent.Length; start = blockSize;
            data = Aes.Create(); tweak = Aes.Create();
            data.Mode = tweak.Mode = CipherMode.ECB; data.Padding = tweak.Padding = PaddingMode.None;
            data.Key = key.Skip(16).Take(16).ToArray(); tweak.Key = key.Take(16).ToArray();
            decrypt = data.CreateDecryptor(); encrypt = tweak.CreateEncryptor();
        }
        protected override byte[] ReadCore(long at, int count)
        {
            var result = new byte[count]; int done = 0;
            while (done < count)
            {
                long index = at / 65536; int offset = (int)(at % 65536);
                if (cached != index)
                {
                    long pos = index * 65536; page = parent.Read(pos, (int)Math.Min(65536, Length - pos));
                    for (int sector = 0; sector < page.Length; sector += 4096)
                    {
                        if (pos + sector < start) continue;
                        if (sector + 4096 > page.Length) throw new InvalidDataException("Sector XTS incompleto.");
                        var counter = new byte[16]; Array.Copy(BitConverter.GetBytes((pos + sector) / 4096), counter, 8);
                        var t = new byte[16]; encrypt.TransformBlock(counter, 0, 16, t, 0);
                        var masks = new byte[4096];
                        for (int b = 0; b < 4096; b += 16)
                        {
                            Buffer.BlockCopy(t, 0, masks, b, 16);
                            int carry = 0;
                            for (int j = 0; j < 16; j++) { int next = t[j] >> 7; t[j] = (byte)((t[j] << 1) | carry); carry = next; }
                            if (carry != 0) t[0] ^= 0x87;
                        }
                        for (int b = 0; b < 4096; b++) page[sector + b] ^= masks[b];
                        decrypt.TransformBlock(page, sector, 4096, page, sector);
                        for (int b = 0; b < 4096; b++) page[sector + b] ^= masks[b];
                    }
                    cached = index;
                }
                int n = Math.Min(count - done, page.Length - offset);
                Buffer.BlockCopy(page, offset, result, done, n); at += n; done += n;
            }
            return result;
        }
        public void Dispose() { decrypt.Dispose(); encrypt.Dispose(); data.Dispose(); tweak.Dispose(); }
    }
    internal sealed class PkgCompressed : PkgData
    {
        private readonly PkgData parent; private readonly int block; private readonly long[] offsets;
        private long cached = -1; private byte[] page;
        internal PkgCompressed(PkgData parent)
        {
            this.parent = parent; var h = parent.Read(0, 48);
            if (BitConverter.ToUInt32(h, 0) != 0x43534650) throw new InvalidDataException("Falta la cabecera PFSC.");
            block = BitConverter.ToInt32(h, 12); Length = Long(h, 40);
            if (block < 4096 || block > 1024 * 1024 || (block & (block - 1)) != 0 || Long(h, 16) != block || Length % block != 0 || Length / block > 16000000)
                throw new InvalidDataException("Tabla PFSC no válida.");
            int count = checked((int)(Length / block) + 1); long table = Long(h, 24);
            var raw = parent.Read(table, checked(count * 8)); offsets = new long[count];
            for (int i = 0; i < count; i++)
            {
                offsets[i] = Long(raw, i * 8);
                if (offsets[i] < table + raw.Length || offsets[i] > parent.Length || (i > 0 && (offsets[i] <= offsets[i - 1] || offsets[i] - offsets[i - 1] > block)))
                    throw new InvalidDataException("Bloques PFSC fuera de rango.");
            }
        }
        protected override byte[] ReadCore(long at, int count)
        {
            var result = new byte[count]; int done = 0;
            while (done < count)
            {
                int index = (int)(at / block), offset = (int)(at % block);
                if (cached != index)
                {
                    var stored = parent.Read(offsets[index], (int)(offsets[index + 1] - offsets[index]));
                    if (stored.Length == block) page = stored;
                    else
                    {
                        if (stored.Length < 6 || (stored[0] & 15) != 8 || ((stored[0] << 8) + stored[1]) % 31 != 0 || (stored[1] & 32) != 0)
                            throw new InvalidDataException("Bloque zlib no válido.");
                        page = new byte[block];
                        using (var stream = new MemoryStream(stored, 2, stored.Length - 6))
                        using (var deflate = new DeflateStream(stream, CompressionMode.Decompress))
                        {
                            int n = 0; while (n < block) { int got = deflate.Read(page, n, block - n); if (got == 0) throw new InvalidDataException("Bloque PFSC truncado."); n += got; }
                            if (deflate.ReadByte() != -1) throw new InvalidDataException("Bloque PFSC demasiado grande.");
                        }
                        uint a = 1, b = 0; foreach (byte value in page) { a = (a + value) % 65521; b = (b + a) % 65521; }
                        if (((b << 16) | a) != Ps4Package.Be32(stored, stored.Length - 4)) throw new InvalidDataException("La suma de comprobación PFSC no coincide.");
                    }
                    cached = index;
                }
                int take = Math.Min(block - offset, count - done); Buffer.BlockCopy(page, offset, result, done, take); done += take; at += take;
            }
            return result;
        }
    }
    internal sealed class PkgPfs
    {
        internal sealed class FileItem { internal string Name; internal PkgData Data; }
        private sealed class Inode { internal ushort Mode; internal uint Flags; internal long Size, Logical; internal uint Blocks; internal int[] Direct, Indirect; }
        private readonly PkgData source; private readonly int block, stride; private readonly bool signed;
        private readonly Inode[] inodes; private readonly CancellationToken ct;
        private readonly HashSet<int> visited = new HashSet<int>();
        internal readonly List<FileItem> Files = new List<FileItem>();
        internal readonly List<string> Directories = new List<string>();
        internal PkgPfs(PkgData source, CancellationToken ct)
        {
            this.source = source; this.ct = ct; var h = source.Read(0, 0x400);
            int mode = BitConverter.ToUInt16(h, 0x1c); signed = (mode & 1) != 0; stride = signed ? 36 : 4;
            if (Long(h, 0) != 1 || Long(h, 8) != 20130315 || (mode & 2) != 0) throw new InvalidDataException("Versión de PFS no admitida (se requiere PFS de 32 bits).");
            block = BitConverter.ToInt32(h, 0x20); long count = Long(h, 0x30), inodeBlocks = Long(h, 0x40);
            if (block < 4096 || block > 1048576 || (block & (block - 1)) != 0 || count < 1 || count > 200000 || inodeBlocks < 1)
                throw new InvalidDataException("Cabecera PFS no válida.");
            int size = signed ? 0x2c8 : 0xa8, perBlock = block / size;
            if ((count + perBlock - 1) / perBlock > inodeBlocks || inodeBlocks > source.Length / block - 1) throw new InvalidDataException("Tabla de inodos incompleta.");
            inodes = new Inode[(int)count]; byte[] page = null;
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % perBlock == 0) page = source.Read((1L + i / perBlock) * block, block);
                int p = i % perBlock * size;
                var node = new Inode { Mode = BitConverter.ToUInt16(page, p), Flags = BitConverter.ToUInt32(page, p + 4), Size = Long(page, p + 8), Logical = Long(page, p + 16), Blocks = BitConverter.ToUInt32(page, p + 0x60), Direct = new int[12], Indirect = new int[5] };
                for (int j = 0; j < 17; j++) { int ptr = BitConverter.ToInt32(page, p + 0x64 + (signed ? 32 : 0) + j * stride); if (j < 12) node.Direct[j] = ptr; else node.Indirect[j - 12] = ptr; }
                inodes[i] = node;
            }
            int root = checked((int)Long(h, 0x48));
            var entries = DirectoryEntries(root).Where(e => e.Type == 3 && e.Name == "uroot").ToArray();
            if (entries.Length != 1) throw new InvalidDataException("PFS sin directorio uroot.");
            Walk(entries[0].Id, "", 0);
        }
        private static long Long(byte[] b, int o) => PkgData.Long(b, o);
        private Inode Node(int id) { if (id < 0 || id >= inodes.Length) throw new InvalidDataException("Inodo fuera de rango."); return inodes[id]; }
        private PkgData Data(int id)
        {
            var n = Node(id);
            if (n.Size == 0) return new PkgSlice(source, 0, 0);
            if (n.Blocks < (n.Size + block - 1) / block || n.Blocks > source.Length / block) throw new InvalidDataException("Tamaño de inodo no válido.");
            PkgData result;
            if (n.Blocks <= 1 || n.Direct[1] == -1)
                result = new PkgSlice(source, checked((long)n.Direct[0] * block), n.Size);
            else result = new Mapped(this, n);
            if ((n.Flags & 1) != 0)
            {
                result = new PkgCompressed(result);
                if (n.Logical != result.Length) throw new InvalidDataException("Tamaño comprimido PFS inconsistente.");
            }
            return result;
        }
        private sealed class Mapped : PkgData
        {
            private readonly PkgPfs pfs; private readonly Inode node;
            private readonly Dictionary<int, byte[]> pointers = new Dictionary<int, byte[]>();
            internal Mapped(PkgPfs pfs, Inode node) { this.pfs = pfs; this.node = node; Length = node.Size; }
            private int Indirect(int root, long index, int depth)
            {
                if (root <= 0 || (long)root * pfs.block > pfs.source.Length - pfs.block) throw new InvalidDataException("Puntero PFS no válido.");
                if (!pointers.TryGetValue(root, out var bytes)) { if (pointers.Count >= 64) pointers.Clear(); bytes = pfs.source.Read((long)root * pfs.block, pfs.block); pointers[root] = bytes; }
                int capacity = pfs.block / pfs.stride; long span = 1; for (int i = 1; i < depth; i++) span *= capacity;
                int next = BitConverter.ToInt32(bytes, checked((int)(index / span) * pfs.stride + (pfs.signed ? 32 : 0)));
                return depth == 1 ? next : Indirect(next, index % span, depth - 1);
            }
            protected override byte[] ReadCore(long at, int count)
            {
                var result = new byte[count]; int done = 0;
                while (done < count)
                {
                    long index = at / pfs.block; int offset = (int)(at % pfs.block), ptr;
                    if (index < 12) ptr = node.Direct[index];
                    else
                    {
                        index -= 12; long capacity = pfs.block / pfs.stride; int depth = 1;
                        while (depth < 5 && index >= capacity) { index -= capacity; capacity = checked(capacity * (pfs.block / pfs.stride)); depth++; }
                        if (index >= capacity) throw new InvalidDataException("Archivo PFS demasiado grande.");
                        ptr = Indirect(node.Indirect[depth - 1], index, depth);
                    }
                    if (ptr <= 0) throw new InvalidDataException("Bloque PFS no válido.");
                    int n = Math.Min(pfs.block - offset, count - done); var bytes = pfs.source.Read((long)ptr * pfs.block + offset, n);
                    Buffer.BlockCopy(bytes, 0, result, done, n); done += n; at += n;
                }
                return result;
            }
        }
        private sealed class Entry { internal int Id, Type; internal string Name; }
        private IEnumerable<Entry> DirectoryEntries(int id)
        {
            if ((Node(id).Mode & 0xf000) != 0x4000) throw new InvalidDataException("No es un directorio PFS.");
            var data = Data(id);
            if (data.Length > 32 * 1024 * 1024) throw new InvalidDataException("Directorio PFS demasiado grande.");
            for (long at = 0; at < data.Length; at += block)
            {
                ct.ThrowIfCancellationRequested(); var bytes = data.Read(at, (int)Math.Min(block, data.Length - at));
                for (int p = 0; p + 16 <= bytes.Length;)
                {
                    int size = BitConverter.ToInt32(bytes, p + 12); if (size == 0) break;
                    int nameSize = BitConverter.ToInt32(bytes, p + 8), type = BitConverter.ToInt32(bytes, p + 4);
                    if (size < 17 || size > bytes.Length - p || nameSize < 1 || nameSize > 255 || nameSize >= size - 16) throw new InvalidDataException("Entrada de directorio PFS no válida.");
                    string name = new UTF8Encoding(false, true).GetString(bytes, p + 16, nameSize);
                    if (type == 4 || type == 5) { p += size; continue; }
                    if (type != 2 && type != 3) throw new InvalidDataException("Enlaces/tipos especiales PFS no admitidos.");
                    ArchiveExtractor.ValidateName(name);
                    if (name.Contains("/") || name.Contains("\\")) throw new InvalidDataException("Nombre PFS con separadores.");
                    yield return new Entry { Id = BitConverter.ToInt32(bytes, p), Type = type, Name = name }; p += size;
                }
            }
        }
        private void Walk(int id, string parent, int depth)
        {
            if (depth > 32 || !visited.Add(id)) throw new InvalidDataException("Ciclo o profundidad excesiva en PFS.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in DirectoryEntries(id))
            {
                ct.ThrowIfCancellationRequested(); if (!names.Add(entry.Name)) throw new InvalidDataException("Nombres PFS duplicados en Windows.");
                var name = parent + entry.Name;
                if (name.Length > 220) throw new InvalidDataException("Ruta PFS demasiado larga.");
                if (entry.Type == 3) { Directories.Add(name); Walk(entry.Id, name + "/", depth + 1); }
                else
                {
                    if ((Node(entry.Id).Mode & 0xf000) != 0x8000 || Files.Count >= 50000) throw new InvalidDataException("Archivo o cantidad PFS no admitido.");
                    Files.Add(new FileItem { Name = name, Data = Data(entry.Id) });
                }
            }
        }
    }
}
