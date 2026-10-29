using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nexo.Core
{
    internal sealed class Ps4Package : IDisposable
    {
        private sealed class Meta { internal uint Id, Name, Flags; internal long Offset; internal int Size; internal byte[] Raw; }
        private readonly PkgFile file;
        private readonly Dictionary<uint, Meta> entries = new Dictionary<uint, Meta>();
        private PkgXts xts; private byte[] derivedKey; private bool unverifiedKey;
        private readonly long pfsOffset, pfsSize; private readonly ulong pfsFlags;
        internal string ContentId { get; private set; }
        internal string TitleId { get; private set; }
        internal string Category { get; private set; }
        internal string Title { get; private set; }
        internal string Path { get; private set; }
        internal bool Base => Category == "gd";
        internal bool Patch => Category == "gp";
        internal bool Addon => Category == "ac" || Category == "ac-nodata";
        internal long ExpectedSize { get; private set; }
        internal List<PkgPfs.FileItem> Files { get; private set; }
        internal IReadOnlyList<string> Directories { get; private set; } = new string[0];
        internal static bool IsPkg(string systemId, string path) => systemId == "PS4" && string.Equals(System.IO.Path.GetExtension(path), ".pkg", StringComparison.OrdinalIgnoreCase);
        internal static uint Be32(byte[] b, int p) => ((uint)b[p] << 24) | ((uint)b[p + 1] << 16) | ((uint)b[p + 2] << 8) | b[p + 3];
        private static ulong Be64(byte[] b, int p) => ((ulong)Be32(b, p) << 32) | Be32(b, p + 4);
        internal Ps4Package(string path)
        {
            Path = path; file = new PkgFile(path);
            try
            {
                var h = file.Read(0, 0x5a0);
                if (Be32(h, 0) != 0x7f434e54) throw new InvalidDataException("No es un PKG de PS4 (falta CNT).");
                ExpectedSize = checked((long)Be64(h, 0x430));
                if (ExpectedSize < 0x5a0) throw new InvalidDataException("Tamaño PKG no válido.");
                if (ExpectedSize > file.Length) throw new EndOfStreamException("Descarga de PKG incompleta.");
                ContentId = Encoding.ASCII.GetString(h, 0x40, 48).TrimEnd('\0');
                if (!Regex.IsMatch(ContentId, @"^[A-Z]{2}[0-9]{4}-(CUSA[0-9]{5})_[0-9]{2}-[A-Z0-9_]{16}$")) throw new InvalidDataException("Content ID de PKG no válido.");
                TitleId = ContentId.Substring(7, 9);
                uint count = Be32(h, 0x10), table = Be32(h, 0x18);
                if (count == 0 || count > 4096 || table < 0x5a0 || (long)table + count * 32L > ExpectedSize) throw new InvalidDataException("Tabla PKG no válida.");
                for (int i = 0; i < count; i++)
                {
                    var raw = file.Read(table + i * 32L, 32);
                    var e = new Meta { Id = Be32(raw, 0), Name = Be32(raw, 4), Flags = Be32(raw, 8), Offset = Be32(raw, 16), Size = checked((int)Be32(raw, 20)), Raw = raw };
                    if (e.Size > 64 * 1024 * 1024 || e.Offset > ExpectedSize - e.Size || entries.ContainsKey(e.Id)) throw new InvalidDataException("Entrada PKG fuera de rango o duplicada.");
                    entries.Add(e.Id, e);
                }
                var sfo = MetaBytes(0x1000);
                if (Metadata.ReadSfoString(sfo, "TITLE_ID") != TitleId || Metadata.ReadSfoString(sfo, "CONTENT_ID") != ContentId) throw new InvalidDataException("Title ID/Content ID inconsistente entre cabecera y PARAM.SFO.");
                Category = Metadata.ReadSfoString(sfo, "CATEGORY"); Title = Metadata.ReadSfoString(sfo, "TITLE");
                if ((!Base && !Patch && !Addon) || string.IsNullOrWhiteSpace(Title)) throw new InvalidDataException("El PKG no es un juego base, parche completo o DLC admitido.");
                pfsOffset = checked((long)Be64(h, 0x410)); pfsSize = checked((long)Be64(h, 0x418)); pfsFlags = Be64(h, 0x408);
                if (pfsOffset > ExpectedSize || pfsSize > ExpectedSize - pfsOffset || (pfsSize == 0 && !Addon)) throw new InvalidDataException("Imagen PFS no válida.");
            }
            catch { Dispose(); throw; }
        }
        private byte[] MetaBytes(uint id)
        {
            if (!entries.TryGetValue(id, out var e)) throw new InvalidDataException("Falta una entrada necesaria en el PKG.");
            if (id == 0x1000 && e.Size > 1024 * 1024) throw new InvalidDataException("PARAM.SFO demasiado grande.");
            if ((e.Flags & 0x80000000) != 0) throw new InvalidDataException("Metadatos de PKG cifrados no admitidos.");
            return file.Read(e.Offset, e.Size);
        }
        private byte[] ImageKey()
        {
            try
            {
                var keys = MetaBytes(0x10);
                if (keys.Length != 0x800 || !entries.TryGetValue(0x20, out var image) || image.Size != 256 ||
                    ((Be32(image.Raw, 12) >> 12) & 15) != 3) throw new CryptographicException();
                derivedKey = Ps4PkgCrypto.Unwrap(keys.Skip(256 + 3 * 256).Take(256).ToArray(), false);
                var ekpfs = Ps4PkgCrypto.Unwrap(Ps4PkgCrypto.Cbc(file.Read(image.Offset, image.Size), Ps4PkgCrypto.Sha(image.Raw.Concat(derivedKey).ToArray())), true);
                // Repacked fake PKGs can retain a stale, NONZERO derived-key-1 digest.
                // RSA padding must still verify, followed by all PFS structural checks.
                var digest = Ps4PkgCrypto.Sha(ekpfs).Select((b, i) => (byte)(b ^ ekpfs[i])).ToArray();
                unverifiedKey = !digest.SequenceEqual(keys.Skip(64).Take(32));
                return ekpfs;
            }
            catch (CryptographicException ex) { throw new NexoException("No se puede descifrar este PKG. Nexo solo admite paquetes fake/de desarrollo con las claves públicas compatibles; el original se conserva.", ex); }
        }
        internal void LoadFiles(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (Files != null) return;
            var files = new Dictionary<string, PkgPfs.FileItem>(StringComparer.OrdinalIgnoreCase);
            if (pfsSize > 0)
            {
                PkgData data = new PkgSlice(file, pfsOffset, pfsSize); var h = data.Read(0, 0x400);
                bool encrypted = (BitConverter.ToUInt16(h, 0x1c) & 4) != 0;
                var key = encrypted ? ImageKey() : null;
                PkgPfs outer;
                try
                {
                    bool newCrypt = (pfsFlags & 0x2000000000000000) != 0;
                    try { outer = ReadImage(data, h, key, newCrypt, ct); }
                    catch (InvalidDataException) when (encrypted && newCrypt)
                    {
                        // Some fake repacks retain bit 61 although their image uses legacy XTS
                        // derivation. Retry only after a structural failure, using the same
                        // successfully unwrapped fake image key. No passcode guesses or writes.
                        ct.ThrowIfCancellationRequested();
                        outer = ReadImage(data, h, key, false, ct);
                    }
                }
                catch (InvalidDataException ex) when (unverifiedKey)
                {
                    throw new NexoException("No se puede descifrar este PKG. Nexo solo admite paquetes fake/de desarrollo con las claves públicas compatibles; el original se conserva.", ex);
                }
                foreach (var f in outer.Files) files.Add(f.Name, f);
                Directories = outer.Directories.AsReadOnly();
            }
            byte[] names = entries.ContainsKey(0x200) ? MetaBytes(0x200) : new byte[0];
            foreach (var e in entries.Values.Where(e => e.Id >= 0x1000))
            {
                ct.ThrowIfCancellationRequested();
                string name;
                if (e.Id == 0x1000) name = "param.sfo";
                else
                {
                    if (e.Name == 0 || e.Name >= names.Length) continue;
                    int end = Array.IndexOf(names, (byte)0, (int)e.Name);
                    if (end < 0) throw new InvalidDataException("Nombre de metadato PKG sin terminador.");
                    name = new UTF8Encoding(false, true).GetString(names, (int)e.Name, end - (int)e.Name);
                }
                ArchiveExtractor.ValidateName(name);
                if ((e.Flags & 0x80000000) != 0) throw new InvalidDataException("Metadato cifrado no admitido: " + name);
                var destination = "sce_sys/" + name;
                // Public PKG metadata is authoritative for updates. Never mutate user files.
                files[destination] = new PkgPfs.FileItem { Name = destination, Data = new PkgSlice(file, e.Offset, e.Size) };
            }
            if (!files.ContainsKey("sce_sys/param.sfo") || (!Addon && (!files.TryGetValue("eboot.bin", out var boot) || boot.Data.Length < 4)))
                throw new InvalidDataException("El PKG no contiene un juego completo (eboot.bin y PARAM.SFO).");
            // Reject file/directory collisions before writing anything.
            foreach (var name in files.Keys)
            {
                int slash = name.IndexOf('/');
                while (slash >= 0) { if (files.ContainsKey(name.Substring(0, slash))) throw new InvalidDataException("Conflicto entre archivo y carpeta PFS."); slash = name.IndexOf('/', slash + 1); }
            }
            if (Directories.Any(files.ContainsKey)) throw new InvalidDataException("Conflicto entre carpeta PFS y metadato PKG.");
            Files = files.Values.OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        }
        private PkgPfs ReadImage(PkgData data, byte[] header, byte[] key, bool newCrypt, CancellationToken ct)
        {
            xts?.Dispose(); xts = null;
            try
            {
                if (key != null)
                {
                    xts = new PkgXts(data, BitConverter.ToInt32(header, 0x20),
                        Ps4PkgCrypto.PfsKeys(key, header.Skip(0x370).Take(16).ToArray(), newCrypt));
                    data = xts;
                }
                var pfs = new PkgPfs(data, ct);
                if ((pfsFlags & 0x8000000000000000) != 0)
                {
                    var nested = pfs.Files.SingleOrDefault(f => f.Name == "pfs_image.dat");
                    if (nested == null) throw new InvalidDataException("PFS sin imagen interior.");
                    var inner = nested.Data;
                    if (BitConverter.ToUInt32(inner.Read(0, 4), 0) == 0x43534650) inner = new PkgCompressed(inner);
                    pfs = new PkgPfs(inner, ct);
                }
                return pfs;
            }
            catch { xts?.Dispose(); xts = null; throw; }
        }
        internal static ArchiveEntry Inspect(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); long size = 0, expected = 0;
            string title = GameScanner.CleanTitle(System.IO.Path.GetFileNameWithoutExtension(path));
            string titleId = null;
            ArchiveEntry Result(ArchiveState state, string message) => new ArchiveEntry("PS4", path, title, size, expected > 0 ? (long?)expected : null, state, message, titleId: titleId);
            try
            {
                if (!SafePaths.IsRegularFile(path)) throw new InvalidDataException("El PKG no es un archivo regular.");
                using (var f = new PkgFile(path))
                {
                    size = f.Length;
                    if (size < 0x5a0) return Result(ArchiveState.Incomplete, "Descarga incompleta: falta la cabecera del PKG de PS4.");
                    var h = f.Read(0, 0x438);
                    if (Be32(h, 0) != 0x7f434e54) throw new InvalidDataException("No es un PKG de PS4.");
                    expected = checked((long)Be64(h, 0x430));
                    if (expected > size) return Result(ArchiveState.Incomplete, string.Format("Descarga incompleta: {0:0} MB de {1:0} MB. Vuelve a descargarlo.", size / 1048576.0, expected / 1048576.0));
                }
                using (var pkg = new Ps4Package(path))
                {
                    title = pkg.Title;
                    titleId = pkg.TitleId;
                    // Patch/DLC packages are installed as part of the base's transaction, never as games.
                    if (!pkg.Base) return null;
                    return Result(ArchiveState.Ready, "PKG de PS4 pendiente. Instalar incluye el parche y los DLC del mismo Title ID en esta carpeta y su subcarpeta DLC. Se comprobará que sea un PKG fake/de desarrollo compatible.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException || ex is NexoException)
            { return Result(ArchiveState.Unreadable, "No se puede instalar el PKG: " + ex.Message); }
        }
        public void Dispose() { xts?.Dispose(); file?.Dispose(); }
    }
}
