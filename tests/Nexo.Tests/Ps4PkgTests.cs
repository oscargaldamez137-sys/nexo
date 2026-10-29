using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Nexo.Core;

internal static class Ps4PkgTests
{
    private const string Id = "CUSA09072", Content = "UP0700-CUSA09072_00-DBPROJECTZGOKU18";
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            var root = Path.Combine(workspace, ".nexo-test-pkg-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally { if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-pkg-")) throw new Exception("Limpieza insegura"); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        Test("PS4 PKG: base, parche y DLC se instalan juntos y solo se reciclan tras verificar", (root, svc) =>
        {
            var folder = svc.GetStatus("PS4").GamesFolder;
            string main = Make(folder, "base.pkg"), patch = Make(folder, "update.pkg", "gp"), dlc = Make(Path.Combine(folder, "DLC"), "addon.pkg", "ac-nodata", "UP0700-CUSA09072_00-ADDON00000000001");
            var scan = svc.ScanGamesDetailedAsync("PS4", CancellationToken.None).GetAwaiter().GetResult();
            Check(scan.Games.Count == 0 && scan.Archives.Count == 1 && scan.Archives[0].State == ArchiveState.Ready);
            var recycled = new List<string>(); long last = -1;
            var output = Ps4PkgInstaller.InstallAsync(scan.Archives[0], folder, new ProgressValue(n => { Check(n >= last); last = n; }), CancellationToken.None, p =>
            {
                string target = Directory.GetDirectories(folder, "* - instalado").Single();
                Check(File.Exists(Path.Combine(target, Id, "eboot.bin")) && File.Exists(Path.Combine(target, Id + "-UPDATE", "eboot.bin")));
                Check(Metadata.ReadSfoString(Path.Combine(target, Id, "sce_sys", "param.sfo"), "TITLE_ID") == Id);
                recycled.Add(p); File.Delete(p);
            }).GetAwaiter().GetResult();
            Check(recycled.Count == 3 && last > 0 && !File.Exists(main) && !File.Exists(patch) && !File.Exists(dlc));
            var games = svc.ScanGamesDetailedAsync("PS4", CancellationToken.None).GetAwaiter().GetResult();
            Check(games.Games.Count == 1 && games.Archives.Count == 0 && games.Games[0].Path == Path.Combine(output, Id, "eboot.bin"));
            Check(Ps4PkgInstaller.AddonFolder(games.Games[0]) == Path.Combine(output, "addons"));
            Ps4PkgInstaller.PrepareAddons(games.Games[0], Path.Combine(root, "shadPS4.exe"), info =>
                Check(info.Arguments.Contains("--set-addon-folder") && !info.Arguments.Contains("\"-g\"") && info.CreateNoWindow));
        });
        Test("PS4 PKG: truncado muestra tamaño esperado y no se instala", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"); long size = new FileInfo(path).Length;
            using (var f = File.OpenWrite(path)) f.SetLength(size - 10);
            var item = Ps4Package.Inspect(path, CancellationToken.None); Check(item.State == ArchiveState.Incomplete && item.ExpectedSize == size);
            Fail(() => Install(item, folder)); Check(File.Exists(path)); NoStage(folder);
        });
        Test("PS4 PKG: Title ID de cabecera y SFO incoherente aborta todo el lote", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"), patch = Make(folder, "patch.pkg", "gp", Content, "CUSA00000");
            Check(Ps4Package.Inspect(patch, CancellationToken.None).State == ArchiveState.Unreadable);
            Fail(() => Install(Ps4Package.Inspect(path, CancellationToken.None), folder));
            Check(File.Exists(path) && File.Exists(patch) && Directory.GetDirectories(folder).Length == 0);
        });
        Test("PS4 PKG: parche de otro Title ID permanece intacto y no se mezcla", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg");
            string foreign = Make(folder, "other.pkg", "gp", "UP0700-CUSA00001_00-DBPROJECTZGOKU18", "CUSA00001");
            var before = File.ReadAllBytes(foreign); int count = 0;
            Install(Ps4Package.Inspect(path, CancellationToken.None), folder, p => { Check(p == path); count++; });
            Check(count == 1 && before.SequenceEqual(File.ReadAllBytes(foreign)));
        });
        Test("PS4 PKG: cancelación durante escritura limpia temporal y conserva originales", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"); var cts = new CancellationTokenSource();
            try { Ps4PkgInstaller.InstallAsync(Ps4Package.Inspect(path, CancellationToken.None), folder, new ProgressValue(n => { if (n > 0) { Check(Directory.GetDirectories(folder, ".nexo-extract-*").Length == 1); cts.Cancel(); } }), cts.Token, p => { throw new Exception("No reciclar"); }).GetAwaiter().GetResult(); throw new Exception("No canceló"); }
            catch (OperationCanceledException) { }
            Check(File.Exists(path)); NoStage(folder); Check(Directory.GetDirectories(folder).Length == 0);
        });
        Test("PS4 PKG: falta de espacio y fallo de reciclaje no destruyen el original", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"); var item = Ps4Package.Inspect(path, CancellationToken.None);
            Fail(() => Ps4PkgInstaller.InstallAsync(item, folder, null, CancellationToken.None, p => { throw new Exception("No reciclar"); }, p => 0).GetAwaiter().GetResult());
            Check(Directory.GetDirectories(folder).Length == 0);
            Fail(() => Install(item, folder, p => { throw new IOException("Papelera no disponible"); }));
            Check(File.Exists(path) && File.Exists(Path.Combine(Directory.GetDirectories(folder).Single(), Id, "eboot.bin"))); NoStage(folder);
        });
        Test("PS4 PKG: destino repetido conserva instalación anterior", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"); var item = Ps4Package.Inspect(path, CancellationToken.None);
            string first = Install(item, folder), second = Install(item, folder);
            Check(first != second && second.EndsWith(" (2)") && File.Exists(Path.Combine(first, Id, "eboot.bin")));
        });
        Test("PS4 PKG: API rechaza escapes de Juegos/PS4 y archivos temporales", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder;
            Make(svc.GetStatus("PS2").GamesFolder, "other.pkg");
            var escape = Path.Combine(folder, "..", "PS2", "other.pkg");
            var pending = new ArchiveEntry("PS4", escape, "escape", 1, null, ArchiveState.Ready, "");
            Fail(() => Install(pending, folder));
            string temp = Make(Path.Combine(folder, ".nexo-extract-fake"), "base.pkg");
            Fail(() => Install(Ps4Package.Inspect(temp, CancellationToken.None), folder));
            Check(File.Exists(temp) && File.Exists(escape));
        });
        Test("PS4 PFS: directorios vacíos conservados y enlaces simbólicos rechazados", (root, svc) =>
        {
            byte[] bytes = Pfs("empty", new byte[4096]);
            bytes[4096 + 2 * 168 + 1] = 0x40; Put(bytes, 3 * 4096 + 4, 3);
            string path = Path.Combine(root, "pfs.bin"); File.WriteAllBytes(path, bytes);
            using (var f = new PkgFile(path)) { var p = new PkgPfs(f, CancellationToken.None); Check(p.Files.Count == 0 && p.Directories.SequenceEqual(new[] { "empty" })); }
            bytes[4096 + 2 * 168 + 1] = 0xa0; Put(bytes, 3 * 4096 + 4, 2); File.WriteAllBytes(path, bytes);
            using (var f = new PkgFile(path)) { try { new PkgPfs(f, CancellationToken.None); throw new Exception("Enlace aceptado"); } catch (InvalidDataException) { } }
        });
        Test("PS4 PKG: archivo añadido al temporal impide publicación y reciclaje", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg");
            var progress = new ProgressValue(n => { if (n == 0) File.WriteAllText(Path.Combine(Directory.GetDirectories(folder, ".nexo-extract-*").Single(), "extra.txt"), "inesperado"); });
            Fail(() => Ps4PkgInstaller.InstallAsync(Ps4Package.Inspect(path, CancellationToken.None), folder, progress, CancellationToken.None, p => { throw new Exception("No reciclar"); }).GetAwaiter().GetResult());
            Check(File.Exists(path)); NoStage(folder); Check(Directory.GetDirectories(folder).Length == 0);
        });
        Test("PS4 PFS: recorrido, enlaces, ciclos y colisiones de nombres rechazados", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder;
            foreach (string bad in new[] { "..", "../salida", "C:salida", "CON", "foo.", "a/b" })
            {
                string path = Make(folder, "base.pkg", bootName: bad);
                Fail(() => Install(Ps4Package.Inspect(path, CancellationToken.None), folder)); Check(File.Exists(path)); NoStage(folder);
            }
            string good = Make(folder, "base.pkg"); byte[] bytes = File.ReadAllBytes(good);
            // uroot's file entry becomes a directory pointing at uroot itself.
            Put(bytes, 4096 + 3 * 4096, 1); Put(bytes, 4096 + 3 * 4096 + 4, 3); File.WriteAllBytes(good, bytes);
            Fail(() => Install(Ps4Package.Inspect(good, CancellationToken.None), folder)); NoStage(folder);
        });
        Test("PS4 PFSC: bloques zlib, lectura cruzada y checksum alterado", (root, svc) =>
        {
            var original = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray(); var compressed = Compress(original);
            string path = Path.Combine(root, "pfsc.bin"); File.WriteAllBytes(path, compressed);
            using (var f = new PkgFile(path))
            { var reader = new PkgCompressed(f); Check(reader.Read(4000, 1000).SequenceEqual(original.Skip(4000).Take(1000))); }
            compressed[compressed.Length - 1] ^= 1; File.WriteAllBytes(path, compressed);
            using (var f = new PkgFile(path)) { try { new PkgCompressed(f).Read(4096, 4096); throw new Exception("Checksum ignorado"); } catch (InvalidDataException) { } }
        });
        Test("PS4 PKG: claves fake públicas RSA y rechazo de padding inválido", (root, svc) =>
        {
            var key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            foreach (bool image in new[] { false, true })
            {
                string hex = (string)typeof(Ps4PkgCrypto).GetField(image ? "FakeN" : "DerivedN", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
                var modulus = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
                using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.ImportParameters(new System.Security.Cryptography.RSAParameters { Modulus = modulus, Exponent = new byte[] { 1, 0, 1 } });
                    Check(Ps4PkgCrypto.Unwrap(rsa.Encrypt(key, false), image).SequenceEqual(key));
                }
                try { Ps4PkgCrypto.Unwrap(new byte[256], image); throw new Exception("Padding RSA aceptado"); }
                catch (System.Security.Cryptography.CryptographicException) { }
            }
        });
        Test("PS4 XTS: sectores y lecturas cruzadas con cabecera sin cifrar", (root, svc) =>
        {
            var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            var plain = Enumerable.Range(0, 65536 + 4096).Select(i => (byte)(i % 251)).ToArray();
            var cipher = (byte[])plain.Clone();
            using (var data = System.Security.Cryptography.Aes.Create())
            using (var tweak = System.Security.Cryptography.Aes.Create())
            {
                data.Mode = tweak.Mode = System.Security.Cryptography.CipherMode.ECB;
                data.Padding = tweak.Padding = System.Security.Cryptography.PaddingMode.None;
                data.Key = key.Skip(16).ToArray(); tweak.Key = key.Take(16).ToArray();
                using (var d = data.CreateEncryptor()) using (var t = tweak.CreateEncryptor())
                    for (int s = 4096; s < plain.Length; s += 4096)
                    {
                        var counter = new byte[16]; Buffer.BlockCopy(BitConverter.GetBytes((long)s / 4096), 0, counter, 0, 8);
                        var mask = t.TransformFinalBlock(counter, 0, 16);
                        for (int at = s; at < s + 4096; at += 16)
                        {
                            var input = plain.Skip(at).Take(16).Select((b, i) => (byte)(b ^ mask[i])).ToArray();
                            var output = d.TransformFinalBlock(input, 0, 16);
                            for (int i = 0; i < 16; i++) cipher[at + i] = (byte)(output[i] ^ mask[i]);
                            bool carry = (mask[15] & 128) != 0;
                            for (int i = 15; i >= 0; i--) mask[i] = (byte)((mask[i] << 1) | (i == 0 ? 0 : mask[i - 1] >> 7));
                            if (carry) mask[0] ^= 0x87;
                        }
                    }
            }
            string path = Path.Combine(root, "xts.bin"); File.WriteAllBytes(path, cipher);
            using (var f = new PkgFile(path)) using (var x = new PkgXts(f, 4096, key))
            {
                Check(x.Read(0, 32).SequenceEqual(plain.Take(32)));
                Check(x.Read(4090, 64).SequenceEqual(plain.Skip(4090).Take(64)));
                Check(x.Read(65530, 1000).SequenceEqual(plain.Skip(65530).Take(1000)));
            }
        });
        Test("PS4 PKG: PFS anidado y comprimido se instala sin extraer imagen intermedia", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder;
            var inner = Pfs("eboot.bin", Encoding.ASCII.GetBytes("SCE\0test-game"));
            var outer = Pfs("pfs_image.dat", Compress(inner), true, inner.Length);
            string path = Make(folder, "base.pkg", image: outer, nested: true);
            var target = Install(Ps4Package.Inspect(path, CancellationToken.None), folder);
            Check(File.ReadAllBytes(Path.Combine(target, Id, "eboot.bin")).SequenceEqual(Encoding.ASCII.GetBytes("SCE\0test-game")));
        });
        Test("PS4 PKG: repack con bit new-crypt obsoleto y digest no nulo se instala", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder;
            string path = EncryptedPackage(folder, actualModern: false, declaredModern: true, staleDigest: true);
            int recycled = 0;
            string target = Install(Ps4Package.Inspect(path, CancellationToken.None), folder, p => {
                Check(File.Exists(Path.Combine(Directory.GetDirectories(folder, "* - instalado").Single(), Id, "eboot.bin"))); recycled++;
            });
            Check(recycled == 1 && File.Exists(path));
            Check(File.ReadAllBytes(Path.Combine(target, Id, "eboot.bin")).SequenceEqual(Encoding.ASCII.GetBytes("SCE\0test-game")));
        });
        Test("PS4 PKG: cifrado antiguo y nuevo reales, con digest válido o desactualizado", (root, svc) =>
        {
            foreach (bool modern in new[] { false, true }) foreach (bool stale in new[] { false, true })
            {
                string path = EncryptedPackage(svc.GetStatus("PS4").GamesFolder, modern, modern, stale);
                using (var pkg = new Ps4Package(path)) { pkg.LoadFiles(CancellationToken.None); Check(pkg.Files.Single(f => f.Name == "eboot.bin").Data.Read(0, 4).SequenceEqual(new byte[] { 83, 67, 69, 0 })); }
            }
        });
        Test("PS4 PKG: digest tolerante no acepta PFS corrupto ni clave RSA inválida", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder;
            foreach (bool rsa in new[] { false, true })
            {
                string path = EncryptedPackage(folder, false, true, true); byte[] bytes = File.ReadAllBytes(path);
                if (rsa) Array.Clear(bytes, 6144, 256); else Array.Clear(bytes, 8192 + 4096, 4096);
                File.WriteAllBytes(path, bytes); int recycled = 0;
                Fail(() => Install(Ps4Package.Inspect(path, CancellationToken.None), folder, _ => recycled++));
                Check(recycled == 0 && File.Exists(path)); NoStage(folder); Check(Directory.GetDirectories(folder).Length == 0);
            }
        });
        Test("PS4 PKG: cancelación en repack no se convierte en otro intento", (root, svc) =>
        {
            string path = EncryptedPackage(svc.GetStatus("PS4").GamesFolder, false, true, true);
            using (var pkg = new Ps4Package(path)) using (var ct = new CancellationTokenSource())
            { ct.Cancel(); try { pkg.LoadFiles(ct.Token); throw new Exception("No canceló"); } catch (OperationCanceledException) { } Check(pkg.Files == null); }
        });
        Test("PS4 PKG: regresión lote de 36 paquetes base parche y 34 DLC", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"); Make(folder, "patch.pkg", "gp");
            for (int i = 0; i < 34; i++) Make(Path.Combine(folder, "DLC"), i + ".pkg", "ac-nodata", "UP0700-CUSA09072_00-ADDON" + i.ToString("D11"));
            var recycled = new HashSet<string>(); string target = Install(Ps4Package.Inspect(path, CancellationToken.None), folder, p => recycled.Add(p));
            Check(recycled.Count == 36 && Directory.GetDirectories(Path.Combine(target, "addons", Id)).Length == 34);
            Check(File.Exists(Path.Combine(target, Id, "eboot.bin")) && File.Exists(Path.Combine(target, Id + "-UPDATE", "eboot.bin")));
        });
        Test("PS4 PKG: metadatos absurdos no reservan memoria sin límites", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = Make(folder, "base.pkg"); var good = File.ReadAllBytes(path);
            foreach (Action<byte[]> damage in new Action<byte[]>[] { b => Be(b, 0x10, uint.MaxValue), b => Be(b, 0x610 + 4, uint.MaxValue), b => b[0] = 0 })
            {
                var b = (byte[])good.Clone(); damage(b); File.WriteAllBytes(path, b);
                Check(Ps4Package.Inspect(path, CancellationToken.None).State == ArchiveState.Unreadable);
            }
        });
    }
    private sealed class ProgressValue : IProgress<long> { private readonly Action<long> action; internal ProgressValue(Action<long> action) { this.action = action; } public void Report(long n) => action(n); }
    private static void Check(bool b) { if (!b) throw new Exception("Comprobación PKG fallida"); }
    private static void Fail(Action a) { try { a(); } catch (NexoException) { return; } throw new Exception("Se esperaba NexoException"); }
    private static void NoStage(string folder) => Check(Directory.GetDirectories(folder, ".nexo-extract-*").Length == 0);
    private static string Install(ArchiveEntry a, string folder, Action<string> recycle = null) => Ps4PkgInstaller.InstallAsync(a, folder, null, CancellationToken.None, recycle ?? (p => { })).GetAwaiter().GetResult();
    private static void Put(byte[] b, int at, int n) => Buffer.BlockCopy(BitConverter.GetBytes(n), 0, b, at, 4);
    private static void Long(byte[] b, int at, long n) => Buffer.BlockCopy(BitConverter.GetBytes(n), 0, b, at, 8);
    private static void Be(byte[] b, int at, uint n) { b[at] = (byte)(n >> 24); b[at + 1] = (byte)(n >> 16); b[at + 2] = (byte)(n >> 8); b[at + 3] = (byte)n; }
    private static void BeLong(byte[] b, int at, ulong n) { Be(b, at, (uint)(n >> 32)); Be(b, at + 4, (uint)n); }
    internal static string Make(string folder, string name, string category = "gd", string content = Content, string id = Id, string bootName = "eboot.bin", byte[] image = null, bool nested = false)
    {
        Directory.CreateDirectory(folder);
        byte[] sfo = Sfo(new Dictionary<string, string> { { "TITLE_ID", id }, { "CONTENT_ID", content }, { "CATEGORY", category }, { "TITLE", "Juego sintético" } });
        byte[] pfs = category.StartsWith("ac") ? new byte[0] : image ?? Pfs(bootName, Encoding.ASCII.GetBytes("SCE\0test-game"));
        var bytes = new byte[4096 + pfs.Length]; Be(bytes, 0, 0x7f434e54); Be(bytes, 0x10, 1); Be(bytes, 0x18, 0x600);
        Buffer.BlockCopy(Encoding.ASCII.GetBytes(content), 0, bytes, 0x40, content.Length);
        BeLong(bytes, 0x410, 4096); BeLong(bytes, 0x418, (ulong)pfs.Length); BeLong(bytes, 0x430, (ulong)bytes.Length);
        if (nested) BeLong(bytes, 0x408, 0x8000000000000000);
        Be(bytes, 0x600, 0x1000); Be(bytes, 0x610, 0x800); Be(bytes, 0x614, (uint)sfo.Length);
        Buffer.BlockCopy(sfo, 0, bytes, 0x800, sfo.Length); Buffer.BlockCopy(pfs, 0, bytes, 4096, pfs.Length);
        var path = Path.Combine(folder, name); File.WriteAllBytes(path, bytes); return path;
    }
    private static byte[] Pfs(string name, byte[] content, bool compressed = false, int logical = 0)
    {
        var bytes = new byte[(4 + (content.Length + 4095) / 4096) * 4096];
        Long(bytes, 0, 1); Long(bytes, 8, 20130315); bytes[0x1c] = 8; Put(bytes, 0x20, 4096); Long(bytes, 0x30, 3); Long(bytes, 0x40, 1);
        for (int i = 0; i < 3; i++)
        {
            int at = 4096 + i * 168; bytes[at + 1] = (byte)(i == 2 ? 0x80 : 0x40);
            int size = i == 2 ? content.Length : 4096; Long(bytes, at + 8, size); Long(bytes, at + 16, i == 2 && compressed ? logical : size);
            Put(bytes, at + 4, i == 2 && compressed ? 1 : 0); Put(bytes, at + 0x60, (size + 4095) / 4096); Put(bytes, at + 0x64, i + 2); Put(bytes, at + 0x68, -1);
        }
        Entry(bytes, 8192, 1, 3, "uroot"); Entry(bytes, 12288, 2, 2, name);
        Buffer.BlockCopy(content, 0, bytes, 16384, content.Length); return bytes;
    }
    private static string EncryptedPackage(string folder, bool actualModern, bool declaredModern, bool staleDigest)
    {
        // Independent fixture builder: RSA public wrapping + CBC + sector XTS over
        // a compressed nested PFS. No bytes, passcodes or keys from commercial games.
        var inner = Pfs("eboot.bin", Encoding.ASCII.GetBytes("SCE\0test-game"));
        var image = Pfs("pfs_image.dat", Compress(inner), true, inner.Length); image[0x1c] |= 4;
        var seed = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray(); seed.CopyTo(image, 0x370);
        var ek = Enumerable.Range(80, 32).Select(i => (byte)i).ToArray(); var dk3 = Enumerable.Range(150, 32).Select(i => (byte)i).ToArray();
        byte[] pair;
        using (var first = new System.Security.Cryptography.HMACSHA256(ek))
        using (var final = new System.Security.Cryptography.HMACSHA256(actualModern ? first.ComputeHash(seed) : ek))
            pair = final.ComputeHash(new byte[] { 1, 0, 0, 0 }.Concat(seed).ToArray());
        EncryptImage(image, pair);
        string path = Make(folder, "encrypted.pkg", image: image, nested: true); byte[] initial = File.ReadAllBytes(path);
        var bytes = new byte[initial.Length + 4096]; Buffer.BlockCopy(initial, 0, bytes, 0, 4096); image.CopyTo(bytes, 8192);
        Be(bytes, 0x10, 3); BeLong(bytes, 0x410, 8192); BeLong(bytes, 0x430, (ulong)bytes.Length);
        BeLong(bytes, 0x408, 0x80000000000003ccUL | (declaredModern ? 0x2000000000000000UL : 0));
        Be(bytes, 0x620, 0x10); Be(bytes, 0x628, 0x60000000); Be(bytes, 0x630, 4096); Be(bytes, 0x634, 2048);
        Be(bytes, 0x640, 0x20); Be(bytes, 0x648, 0xe0000000); Be(bytes, 0x64c, 0x3000); Be(bytes, 0x650, 6144); Be(bytes, 0x654, 256);
        Wrap(dk3, false).CopyTo(bytes, 4096 + 1024);
        var digest = Ps4PkgCrypto.Sha(ek).Select((b, i) => (byte)(b ^ ek[i])).ToArray();
        (staleDigest ? Enumerable.Repeat((byte)0x5a, 32).ToArray() : digest).CopyTo(bytes, 4096 + 64);
        byte[] ivKey = Ps4PkgCrypto.Sha(bytes.Skip(0x640).Take(32).Concat(dk3).ToArray());
        using (var aes = System.Security.Cryptography.Aes.Create())
        {
            aes.Mode = System.Security.Cryptography.CipherMode.CBC; aes.Padding = System.Security.Cryptography.PaddingMode.None;
            aes.IV = ivKey.Take(16).ToArray(); aes.Key = ivKey.Skip(16).ToArray();
            using (var enc = aes.CreateEncryptor()) { byte[] wrapped = Wrap(ek, true); enc.TransformFinalBlock(wrapped, 0, 256).CopyTo(bytes, 6144); }
        }
        File.WriteAllBytes(path, bytes); return path;
    }
    private static byte[] Wrap(byte[] key, bool image)
    {
        string hex = (string)typeof(Ps4PkgCrypto).GetField(image ? "FakeN" : "DerivedN", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
        using (var rsa = new System.Security.Cryptography.RSACryptoServiceProvider())
        {
            rsa.PersistKeyInCsp = false;
            rsa.ImportParameters(new System.Security.Cryptography.RSAParameters { Modulus = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray(), Exponent = new byte[] { 1, 0, 1 } });
            return rsa.Encrypt(key, false);
        }
    }
    private static void EncryptImage(byte[] image, byte[] key)
    {
        using (var data = System.Security.Cryptography.Aes.Create()) using (var tweak = System.Security.Cryptography.Aes.Create())
        {
            data.Mode = tweak.Mode = System.Security.Cryptography.CipherMode.ECB; data.Padding = tweak.Padding = System.Security.Cryptography.PaddingMode.None;
            data.Key = key.Skip(16).ToArray(); tweak.Key = key.Take(16).ToArray();
            using (var enc = data.CreateEncryptor()) using (var tw = tweak.CreateEncryptor())
                for (int s = 4096; s < image.Length; s += 4096)
                {
                    var counter = new byte[16]; BitConverter.GetBytes((long)s / 4096).CopyTo(counter, 0); var mask = tw.TransformFinalBlock(counter, 0, 16);
                    for (int p = s; p < s + 4096; p += 16)
                    {
                        var plain = image.Skip(p).Take(16).Select((b, i) => (byte)(b ^ mask[i])).ToArray(); var encrypted = enc.TransformFinalBlock(plain, 0, 16);
                        for (int i = 0; i < 16; i++) image[p + i] = (byte)(encrypted[i] ^ mask[i]);
                        bool carry = (mask[15] & 128) != 0;
                        for (int i = 15; i >= 0; i--) mask[i] = (byte)((mask[i] << 1) | (i == 0 ? 0 : mask[i - 1] >> 7));
                        if (carry) mask[0] ^= 0x87;
                    }
                }
        }
    }
    private static void Entry(byte[] b, int at, int id, int type, string name)
    { var text = Encoding.UTF8.GetBytes(name); Put(b, at, id); Put(b, at + 4, type); Put(b, at + 8, text.Length); Put(b, at + 12, (text.Length + 24) / 8 * 8); Buffer.BlockCopy(text, 0, b, at + 16, text.Length); }
    private static byte[] Sfo(Dictionary<string, string> values)
    {
        var keys = new List<byte>(); var data = new List<byte>(); var rows = new List<byte[]>();
        foreach (var kv in values)
        {
            var row = new byte[16]; var value = Encoding.UTF8.GetBytes(kv.Value + "\0");
            Buffer.BlockCopy(BitConverter.GetBytes((ushort)keys.Count), 0, row, 0, 2); row[2] = 4; row[3] = 2;
            Put(row, 4, value.Length); Put(row, 8, value.Length); Put(row, 12, data.Count); rows.Add(row);
            keys.AddRange(Encoding.ASCII.GetBytes(kv.Key + "\0")); data.AddRange(value);
        }
        var bytes = new byte[20 + rows.Count * 16 + keys.Count + data.Count]; Put(bytes, 0, 0x46535000); Put(bytes, 4, 0x101);
        Put(bytes, 8, 20 + rows.Count * 16); Put(bytes, 12, 20 + rows.Count * 16 + keys.Count); Put(bytes, 16, rows.Count);
        int p = 20; foreach (var row in rows) { Buffer.BlockCopy(row, 0, bytes, p, 16); p += 16; }
        keys.CopyTo(bytes, p); data.CopyTo(bytes, p + keys.Count); return bytes;
    }
    private static byte[] Compress(byte[] original)
    {
        int count = original.Length / 4096; var header = new byte[48 + (count + 1) * 8]; Put(header, 0, 0x43534650); Put(header, 12, 4096); Long(header, 16, 4096); Long(header, 24, 48); Long(header, 32, header.Length); Long(header, 40, original.Length);
        using (var stream = new MemoryStream())
        {
            stream.Write(header, 0, header.Length);
            for (int i = 0; i < count; i++)
            {
                Long(header, 48 + i * 8, stream.Position); stream.WriteByte(0x78); stream.WriteByte(0x9c);
                using (var deflate = new DeflateStream(stream, CompressionLevel.Optimal, true)) deflate.Write(original, i * 4096, 4096);
                uint a = 1, b = 0; for (int j = 0; j < 4096; j++) { a = (a + original[i * 4096 + j]) % 65521; b = (b + a) % 65521; }
                var checksum = new byte[4]; Be(checksum, 0, b << 16 | a); stream.Write(checksum, 0, 4);
            }
            Long(header, 48 + count * 8, stream.Position); stream.Position = 0; stream.Write(header, 0, header.Length); return stream.ToArray();
        }
    }
}
