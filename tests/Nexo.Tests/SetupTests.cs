using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexo.Core;

internal static class SetupTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-setup-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-setup-")) throw new Exception("Ruta de limpieza incorrecta.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        Test("Instalación: fuentes oficiales fijas y Nova5 desde su última publicación", (root, service) =>
        {
            foreach (string id in new[] { "Retro", "PS2", "PS3", "PSP", "PS4", "GameCube-Wii", "Switch", "PS5" })
                foreach (var p in OfficialPackages.For(id)) True(PackageDownloader.Allowed(new Uri(p.Url ?? p.MetadataUrl)));
            var nova5 = OfficialPackages.For("PS5").Single();
            Equal("https://api.github.com/repos/nova5emu/nova5/releases/latest", nova5.MetadataUrl);
            Throws<NexoException>(() => OfficialPackages.For("Desconocido"));
        });
        Test("Instalación: paquete con SHA256, portable, profundidad real y sin tocar datos", (root, service) =>
        {
            foreach (var pair in new[] { new[] { "PS2", "pcsx2-qt.exe", "portable.ini" }, new[] { "GameCube-Wii", "Dolphin-x64/Dolphin.exe", "portable.txt" }, new[] { "Switch", "eden.exe", "user" }, new[] { "PSP", "PPSSPPWindows64.exe", "memstick" } })
            {
                byte[] zip = Zip(pair[1], Pe());
                string sentinel = Put(service.GetStatus(pair[0]).EmulatorFolder, "mis-datos.txt", "propio");
                var package = Package(Hash(zip)); var handler = new FakeHandler(_ => Response(zip));
                using (var downloader = new PackageDownloader(handler))
                    True(EmulatorInstaller.Install(service.GetStatus(pair[0]), downloader, null, CancellationToken.None, new[] { package }).GetAwaiter().GetResult());
                var status = service.GetStatus(pair[0]); True(status.Installed);
                string marker = Path.Combine(Path.GetDirectoryName(status.ExecutablePath), pair[2]);
                True(File.Exists(marker) || Directory.Exists(marker)); Equal("propio", File.ReadAllText(sentinel));
                Equal(0, Directory.GetDirectories(status.EmulatorFolder, ".nexo-extract-*").Length);
                using (var downloader = new PackageDownloader(new FakeHandler(_ => throw new Exception("No debe descargar"))))
                    True(!EmulatorInstaller.Install(status, downloader, null, CancellationToken.None).GetAwaiter().GetResult());
            }
        });
        Test("Instalación: núcleos Retro se publican con el ejecutable y a profundidad detectable", (root, service) =>
        {
            var p = Package(); var cores = Package(); cores.Cores = true; cores.Name = "cores.zip"; cores.Url += "?cores";
            using (var downloader = new PackageDownloader(new FakeHandler(u => Response(u.Query.Length == 0 ? Zip("RetroArch-Win64/retroarch.exe", Pe()) : Zip("RetroArch-Win64/cores/mesen_libretro.dll", new byte[] { 1 })))))
                True(EmulatorInstaller.Install(service.GetStatus("Retro"), downloader, null, CancellationToken.None, new[] { p, cores }).GetAwaiter().GetResult());
            True(File.Exists(Path.Combine(Path.GetDirectoryName(service.GetStatus("Retro").ExecutablePath), "cores", "mesen_libretro.dll")));
        });
        Test("Instalación: hash incorrecto, paquete hostil y ejecutable inválido no publican", (root, service) =>
        {
            foreach (var zip in new[] { Zip("pcsx2-qt.exe", Pe()), Zip("../fuera.txt", new byte[] { 1 }), Zip("pcsx2-qt.exe", new byte[] { 1 }) })
            {
                var p = Package(zip.Length == Zip("pcsx2-qt.exe", Pe()).Length ? new string('0', 64) : null);
                using (var downloader = new PackageDownloader(new FakeHandler(_ => Response(zip))))
                    Throws<NexoException>(() => EmulatorInstaller.Install(service.GetStatus("PS2"), downloader, null, CancellationToken.None, new[] { p }).GetAwaiter().GetResult());
                True(!service.GetStatus("PS2").Installed);
                Equal(0, Directory.GetDirectories(service.GetStatus("PS2").EmulatorFolder, ".nexo-extract-*").Length);
                True(!File.Exists(Path.Combine(service.GetStatus("PS2").EmulatorFolder, "fuera.txt")));
            }
        });
        Test("Instalación: cancelación limpia la descarga y no publica", (root, service) =>
        {
            using (var cts = new CancellationTokenSource())
            using (var downloader = new PackageDownloader(new FakeHandler(_ => Response(Zip("pcsx2-qt.exe", Pe())))))
            {
                var progress = new InlineProgress<SetupProgress>(p => { if (p.Stage == "Descargando") cts.Cancel(); });
                Throws<OperationCanceledException>(() => EmulatorInstaller.Install(service.GetStatus("PS2"), downloader, progress, cts.Token, new[] { Package() }).GetAwaiter().GetResult());
                True(!service.GetStatus("PS2").Installed); Equal(0, Directory.GetDirectories(service.GetStatus("PS2").EmulatorFolder).Length);
            }
        });
        Test("Descargas: redirección oficial permitida y dominio ajeno bloqueado", (root, service) =>
        {
            int calls = 0;
            using (var d = new PackageDownloader(new FakeHandler(u => { calls++; return u.Host == "github.com" ? Redirect("https://release-assets.githubusercontent.com/a") : Response(new byte[] { 1 }); })))
                d.Download("https://github.com/a", Path.Combine(root, "good"), null, CancellationToken.None).GetAwaiter().GetResult();
            Equal(2, calls); calls = 0;
            using (var d = new PackageDownloader(new FakeHandler(u => { calls++; return Redirect("https://malicioso.example/a"); })))
                Throws<NexoException>(() => d.Download("https://github.com/a", Path.Combine(root, "bad"), null, CancellationToken.None).GetAwaiter().GetResult());
            Equal(1, calls); True(!PackageDownloader.Allowed(new Uri("http://github.com/a"))); True(!PackageDownloader.Allowed(new Uri("https://github.com.evil/a")));
        });
        Test("Descargas: listado GitHub verifica URL, digest y checksum publicado", (root, service) =>
        {
            var p = OfficialPackages.For("PS3")[0]; string hash = new string('a', 64);
            string json = "{\"assets\":[{\"name\":\"" + p.Name + "\",\"browser_download_url\":\"" + p.Url + "\",\"digest\":\"sha256:" + hash + "\"}]}";
            using (var d = new PackageDownloader(new FakeHandler(u => Response(Encoding.UTF8.GetBytes(u.Host == "api.github.com" ? json : hash + "  " + p.Name)))))
                OfficialPackages.Resolve(p, d, CancellationToken.None).GetAwaiter().GetResult();
            Equal(hash, p.Hash);
            using (var d = new PackageDownloader(new FakeHandler(_ => Response(Encoding.UTF8.GetBytes(json.Replace(p.Url, "https://github.com/otro/a"))))))
                Throws<NexoException>(() => OfficialPackages.Resolve(p, d, CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("Descargas: resuelve Eden desde enlace oficial y rechaza ambiguos", (root, service) =>
        {
            string link = "https://stable.eden-emu.dev/releases/v0.2.1/Eden-Windows-msvc.zip";
            Equal(link, OfficialPackages.EdenUrl("<a href='" + link + "'>Windows</a>"));
            Throws<NexoException>(() => OfficialPackages.EdenUrl("<a href='https://otro.example/Eden-Windows-msvc-0.2.1.zip'>x</a>"));
            Throws<NexoException>(() => OfficialPackages.EdenUrl("<a href='" + link + "'>x</a><a href='" + link.Replace("msvc", "msvc-otro") + "'>x</a>"));
        });
        Test("PS2: BIOS válida completa el primer arranque con mando y rutas relativas", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            string bios = Path.Combine(root, "mi-bios.bin"); File.WriteAllBytes(bios, Bios());
            True(s.SetupMessages.Any(m => m.Contains("Falta la BIOS")));
            service.ImportSystemFilesAsync("PS2", new[] { bios }).GetAwaiter().GetResult();
            var ini = new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini"));
            Equal("1", ini.ReadSection("UI")["SettingsVersion"]); Equal("false", ini.ReadSection("UI")["SetupWizardIncomplete"]);
            Equal("mi-bios.bin", ini.ReadSection("Filenames")["BIOS"]); Equal("bios", ini.ReadSection("Folders")["Bios"]);
            True(ini.ReadSection("Pad1")["Cross"].Contains("/") && File.Exists(Path.Combine(folder, "portable.ini")));
            True(!service.GetStatus("PS2").SetupMessages.Any(m => m.Contains("Falta la BIOS")));
            True(!Ps2Bootstrap.Configure(folder));
        });
        Test("PS2: BIOS copiada a mano se selecciona al preparar controles", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            string bios = Put(folder, "bios/manual.bin", ""); File.WriteAllBytes(bios, Bios());
            ControllerSetup.Prepare(s, 0, new string[0]);
            Equal("manual.bin", new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini")).ReadSection("Filenames")["BIOS"]);
        });
        Test("PS2: sin BIOS no completa asistente; ROM falsa se rechaza", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            ControllerSetup.Prepare(s, 0, new string[0]);
            Equal("true", new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini")).ReadSection("UI")["SetupWizardIncomplete"]);
            string bad = Put(root, "falsa.bin", "no bios");
            Throws<NexoException>(() => service.ImportSystemFilesAsync("PS2", new[] { bad }).GetAwaiter().GetResult());
            True(!File.Exists(Path.Combine(folder, "bios", "falsa.bin")));
        });
        Test("PS2: BIOS seleccionada y asignaciones personalizadas se conservan", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            string text = "[UI]\nSettingsVersion=1\nSetupWizardIncomplete=true\n[Filenames]\nBIOS=otra.bin\n[Pad1]\nCross=Keyboard/Z\n";
            Put(folder, "inis/PCSX2.ini", text); Put(folder, "portable.ini", "");
            File.WriteAllBytes(Put(folder, "bios/nueva.bin", ""), Bios());
            ControllerSetup.Prepare(s, 0, new string[0]); Equal(text, File.ReadAllText(Path.Combine(folder, "inis", "PCSX2.ini")));
        });
        Test("Wiimote: conservar A de fábrica no permite borrar cambios en B o IR", (root, service) =>
        {
            var s = Emu(service, "GameCube-Wii", "Dolphin.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            foreach (var key in new[] { "Buttons/B", "IR/Up", "Source", "Nunchuk/Stick/Up", "Extension" })
            {
                var values = ControllerProfiles.DolphinWiimoteKeyboard; values[key] = "Personalizado";
                string text = "[Wiimote1]\n" + string.Concat(values.Select(p => p.Key + " = " + p.Value + "\n"));
                string path = Put(folder, "User/Config/WiimoteNew.ini", text);
                ControllerSetup.Prepare(s, 0, new string[0]); Equal(text, File.ReadAllText(path));
            }
        });
        Test("Importación Switch: claves, firmware y backups sin exponer secretos", (root, service) =>
        {
            var s = Emu(service, "Switch", "eden.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            string keys = Put(root, "prod.keys", "header_key = " + new string('1', 64));
            Put(folder, "user/keys/prod.keys", "antigua");
            string zip = Put(root, "firmware.zip", ""); File.WriteAllBytes(zip, Zip(new string('a', 32) + ".nca", new byte[0x4000]));
            Put(folder, "user/nand/system/Contents/registered/anterior.nca", "anterior");
            var result = service.ImportSystemFilesAsync("Switch", new[] { keys, zip }).GetAwaiter().GetResult();
            True(result.Changed && !result.Message.Contains(new string('1', 64)));
            Equal(File.ReadAllText(keys), File.ReadAllText(Path.Combine(folder, "user", "keys", "prod.keys")));
            True(File.Exists(Path.Combine(folder, "user/nand/system/Contents/registered", new string('a', 32) + ".nca")));
            Equal(1, Directory.GetFiles(Path.Combine(folder, "user/keys"), "prod.keys.nexo-backup-*").Length);
            Equal(1, Directory.GetDirectories(Path.Combine(folder, "user/nand/system/Contents"), "registered.nexo-backup-*").Length);
            True(!result.Status.SetupMessages.Any(m => m.StartsWith("Falta")));
        });
        Test("Importación: ZIP hostil o segundo archivo inválido no cambia claves", (root, service) =>
        {
            var s = Emu(service, "Switch", "eden.exe"); string folder = Path.GetDirectoryName(s.ExecutablePath);
            string keys = Put(root, "prod.keys", "master_key_00 = " + new string('1', 32));
            string old = Put(folder, "user/keys/prod.keys", "anterior");
            string zip = Put(root, "firmware.zip", ""); File.WriteAllBytes(zip, Zip("../escape.nca", new byte[0x4000]));
            Throws<NexoException>(() => service.ImportSystemFilesAsync("Switch", new[] { keys, zip }).GetAwaiter().GetResult());
            Equal("anterior", File.ReadAllText(old)); Equal(0, Directory.GetDirectories(s.EmulatorFolder, ".nexo-extract-*").Length);
        });
        Test("Importación: claves RSA/keyblob largas se conservan sin normalizar", (root, service) =>
        {
            var status = Emu(service, "Switch", "eden.exe");
            string text = "eticket_rsa_keypair = " + new string('a', 1056) + "\nkeyblob_00 = " + new string('b', 288) + "\nmariko_master_kek_source_05 = " + new string('c', 34);
            string source = Put(root, "prod.keys", text);
            service.ImportSystemFilesAsync("Switch", new[] { source }).GetAwaiter().GetResult();
            Equal(text, File.ReadAllText(Path.Combine(Path.GetDirectoryName(status.ExecutablePath), "user/keys/prod.keys")));
        });
        Test("Importación: fallo durante commit restaura el primer archivo", (root, service) =>
        {
            var status = Emu(service, "Switch", "eden.exe"); string folder = Path.GetDirectoryName(status.ExecutablePath);
            string prod = Put(root, "prod.keys", "header_key = " + new string('1', 64));
            string titles = Put(root, "title.keys", new string('a', 32) + " = " + new string('b', 32));
            string original = Put(folder, "user/keys/prod.keys", "anterior");
            Directory.CreateDirectory(Path.Combine(folder, "user/keys/title.keys"));
            Throws<NexoException>(() => service.ImportSystemFilesAsync("Switch", new[] { prod, titles }).GetAwaiter().GetResult());
            Equal("anterior", File.ReadAllText(original));
            Equal(0, Directory.GetFiles(Path.GetDirectoryName(original), "*.nexo-backup-*").Length);
        });
        Test("Instalación: destino existente sin ejecutable nunca se mezcla ni reemplaza", (root, service) =>
        {
            string sentinel = Put(service.GetStatus("PS2").EmulatorFolder, "Nexo-PS2/documento", "mío");
            using (var d = new PackageDownloader(new FakeHandler(_ => Response(Zip("pcsx2-qt.exe", Pe())))))
                Throws<NexoException>(() => EmulatorInstaller.Install(service.GetStatus("PS2"), d, null, CancellationToken.None, new[] { Package() }).GetAwaiter().GetResult());
            Equal("mío", File.ReadAllText(sentinel)); True(!service.GetStatus("PS2").Installed);
        });
        Test("Descargas: HTTP erróneo y longitud anunciada excesiva no se instalan", (root, service) =>
        {
            using (var d = new PackageDownloader(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))))
                Throws<NexoException>(() => d.Download("https://github.com/a", Path.Combine(root, "404"), null, CancellationToken.None).GetAwaiter().GetResult());
            using (var d = new PackageDownloader(new FakeHandler(_ => { var r = Response(new byte[1]); r.Content.Headers.ContentLength = 5L * 1024 * 1024 * 1024; return r; })))
                Throws<NexoException>(() => d.Download("https://github.com/a", Path.Combine(root, "huge"), null, CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("Importación: cancelación previa no modifica datos", (root, service) =>
        {
            Emu(service, "Switch", "eden.exe"); using (var cts = new CancellationTokenSource())
            {
                cts.Cancel(); Throws<OperationCanceledException>(() => service.ImportSystemFilesAsync("Switch", new[] { Put(root, "prod.keys", "x = " + new string('1', 32)) }, cts.Token).GetAwaiter().GetResult());
            }
        });
        Test("PS3: PUP validado pasa como argumento aislado y no afirma instalación completada", (root, unused) =>
        {
            ProcessStartInfo captured = null; var service = new NexoService(root, s => new ControllerSetupResult(false, ""), p => { captured = p; return 123; });
            Emu(service, "PS3", "rpcs3.exe"); byte[] pup = new byte[64]; Encoding.ASCII.GetBytes("SCEUF\0\0\0").CopyTo(pup, 0);
            string path = Put(root, "firmware con espacios.pup", ""); File.WriteAllBytes(path, pup);
            var result = service.ImportSystemFilesAsync("PS3", new[] { path }).GetAwaiter().GetResult();
            True(captured.Arguments.Contains("--installfw") && captured.Arguments.Contains("\"" + path + "\"") && !captured.UseShellExecute);
            True(result.Message.Contains("Todavía no") && result.Status.SetupMessages.Any(m => m.Contains("Falta instalar el firmware")));
        });
        Test("Portabilidad: nexo.json guarda ejecutables internos relativos y bloquea traversal", (root, service) =>
        {
            var s = Emu(service, "PS2", "pcsx2-qt.exe"); service.SetExecutable("PS2", s.ExecutablePath);
            string text = File.ReadAllText(Path.Combine(root, "nexo.json")); True(text.Contains("Emuladores/PS2/package/pcsx2-qt.exe") && !text.Contains("C:"));
            string moved = Path.Combine(root, "otraInstalacion"); Directory.CreateDirectory(moved);
            Directory.Move(Path.Combine(root, "Emuladores"), Path.Combine(moved, "Emuladores"));
            File.Copy(Path.Combine(root, "nexo.json"), Path.Combine(moved, "nexo.json"));
            True(new NexoService(moved).GetStatus("PS2").ExecutablePath.StartsWith(moved));
            File.WriteAllText(Path.Combine(root, "nexo.json"), text.Replace("Emuladores/PS2/package/", "Emuladores/../"));
            Throws<NexoException>(() => new NexoService(root));
        });
        Test("Importación: ejecutable externo nunca recibe BIOS ni carpetas", (root, service) =>
        {
            string external = Put(root, "externo/pcsx2-qt.exe", "fixture"); service.SetExecutable("PS2", external);
            Throws<NexoException>(() => service.ImportSystemFilesAsync("PS2", new[] { external }).GetAwaiter().GetResult());
            True(service.GetStatus("PS2").SetupMessages.Any(m => m.Contains("externo"))); True(!Directory.Exists(Path.Combine(root, "externo", "bios")));
        });
    }
    private static OfficialPackage Package(string hash = null) => new OfficialPackage { Url = "https://dl.dolphin-emu.org/fixture.zip", Name = "fixture.zip", Hash = hash };
    private static byte[] Zip(string path, byte[] bytes)
    {
        using (var stream = new MemoryStream())
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) using (var entry = zip.CreateEntry(path).Open()) entry.Write(bytes, 0, bytes.Length);
            return stream.ToArray();
        }
    }
    private static byte[] Pe()
    { byte[] bytes = new byte[256]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[60] = 128; bytes[128] = 0x50; bytes[129] = 0x45; bytes[132] = 0x64; bytes[133] = 0x86; return bytes; }
    private static byte[] Bios()
    {
        byte[] bytes = new byte[4 * 1048576]; Encoding.ASCII.GetBytes("RESET").CopyTo(bytes, 0); BitConverter.GetBytes(1024u).CopyTo(bytes, 12);
        Encoding.ASCII.GetBytes("ROMVER").CopyTo(bytes, 16); BitConverter.GetBytes(14u).CopyTo(bytes, 28); Encoding.ASCII.GetBytes("0200AC20040614").CopyTo(bytes, 1024); return bytes;
    }
    private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
    private static string Put(string root, string relative, string text) { string path = Path.Combine(root, relative.Replace('/', '\\')); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); return path; }
    private static SystemStatus Emu(NexoService s, string id, string exe) { Put(s.GetStatus(id).EmulatorFolder, "package/" + exe, "fixture"); return s.GetStatus(id); }
    private static HttpResponseMessage Response(byte[] bytes) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static HttpResponseMessage Redirect(string target) { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri(target); return r; }
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> send;
        internal FakeHandler(Func<Uri, HttpResponseMessage> send) { this.send = send; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(send(request.RequestUri)); }
    }
    private static void True(bool v) { if (!v) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T a, T b) { if (!Equals(a, b)) throw new Exception("Esperado " + a + "; recibido " + b); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Se esperaba " + typeof(T).Name); }
}
