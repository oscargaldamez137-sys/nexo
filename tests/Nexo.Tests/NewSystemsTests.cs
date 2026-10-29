using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexo.Core;

internal static class NewSystemsTests
{
    private static readonly string[] Ids = { "GBC", "GBA", "Xbox", "Xbox360" };
    private static readonly string[] Exes = { "mGBA.exe", "mGBA.exe", "xemu.exe", "xenia_canary.exe" };
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> body) => run(name, () => {
            string root = Path.Combine(workspace, ".nexo-test-newsystems-" + Guid.NewGuid().ToString("N"));
            try { body(root, new NexoService(root)); }
            finally { if (!SafePaths.Within(root, workspace)) throw new Exception("Ruta de limpieza inválida."); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        Test("Nuevos sistemas: catálogo, medios, fechas y fuentes fijadas", (root, s) => {
            for (int i = 0; i < 4; i++) {
                var sys = Catalog.Get(Ids[i]); Equal(i < 2 ? "Nintendo" : "Microsoft", sys.Info.Maker);
                Equal(new[] { 1998, 2001, 2001, 2005 }[i], sys.Info.Year.Value);
                Equal(i < 2 ? MediaKind.Cartridge : MediaKind.DvdCase, sys.Info.Media);
                True(OfficialPackages.For(Ids[i]).All(p => p.Url.StartsWith("https://github.com/") && !p.Url.Contains("/latest/")));
            }
            Equal(OfficialPackages.For("GBA")[0].Url, OfficialPackages.For("GBC")[0].Url);
            True(OfficialPackages.For("Xbox")[1].DiskImage); True(OfficialPackages.For("Xbox")[1].Hash.Length == 64);
            True(OfficialPackages.For("Xbox360")[0].Name == "xenia_canary_windows_.zip");
        });
        foreach (string requested in new[] { "GBC", "GBA" }) Test("Instalar " + requested + ": mGBA compartido, portable y sin segunda descarga", (root, s) => {
            using (var d = Downloader(_ => Zip("mGBA-0.10.5-win64/mGBA.exe", Pe())))
                True(EmulatorInstaller.Install(s.GetStatus(requested), d, null, CancellationToken.None, new[] { Package() }).GetAwaiter().GetResult());
            var gbc = s.GetStatus("GBC"); var gba = s.GetStatus("GBA");
            True(gbc.Installed && gba.Installed); Equal(gbc.ExecutablePath, gba.ExecutablePath);
            True(gbc.GamesFolder != gba.GamesFolder); True(SafePaths.Within(gba.ExecutablePath, Path.Combine(root, "Emuladores", "GBA")));
            True(File.Exists(Path.Combine(Path.GetDirectoryName(gba.ExecutablePath), "portable.ini")));
            using (var d = Downloader(_ => throw new Exception("No descargar")))
                True(!EmulatorInstaller.Install(s.GetStatus(requested == "GBA" ? "GBC" : "GBA"), d, null, CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("mGBA: detección propia y elección manual conservadas", (root, s) => {
            string gbc = Put(root, "Emuladores/GBC/otro/mGBA.exe", Pe()); Equal(gbc, s.GetStatus("GBA").ExecutablePath);
            string gba = Put(root, "Emuladores/GBA/otro/mGBA.exe", Pe()); Equal(gba, s.GetStatus("GBA").ExecutablePath); Equal(gbc, s.GetStatus("GBC").ExecutablePath);
            s.SetExecutable("GBC", gba); Equal(gba, s.GetStatus("GBC").ExecutablePath); Equal(s.GetStatus("GBA").EmulatorFolder, s.GetStatus("GBC").EmulatorFolder);
        });
        Test("Xbox: instalador publica exe y QCOW2 oficial juntos", (root, s) => {
            var hdd = Package(); hdd.Url += "?hdd"; hdd.DiskImage = true;
            using (var d = Downloader(u => u.Query.Length > 0 ? Qcow() : Zip("xemu/xemu.exe", Pe())))
                True(EmulatorInstaller.Install(s.GetStatus("Xbox"), d, null, CancellationToken.None, new[] { Package(), hdd }).GetAwaiter().GetResult());
            var status = s.GetStatus("Xbox"); True(status.Installed); Equal(2, status.SetupMessages.Count);
            string folder = Path.GetDirectoryName(status.ExecutablePath);
            True(XboxFiles.IsQcow(Path.Combine(folder, "system", "xbox_hdd.qcow2")));
            True(File.ReadAllText(Path.Combine(folder, "xemu.toml")).Contains("hdd_path = \"system/xbox_hdd.qcow2\""));
        });
        Test("Xbox: HDD inválido no publica una instalación a medias", (root, s) => {
            var hdd = Package(); hdd.Url += "?hdd"; hdd.DiskImage = true;
            using (var d = Downloader(u => u.Query.Length > 0 ? new byte[200] : Zip("xemu.exe", Pe())))
                Throws<NexoException>(() => EmulatorInstaller.Install(s.GetStatus("Xbox"), d, null, CancellationToken.None, new[] { Package(), hdd }).GetAwaiter().GetResult());
            True(!s.GetStatus("Xbox").Installed); Equal(0, Directory.GetDirectories(s.GetStatus("Xbox").EmulatorFolder).Length);
        });
        Test("Xenia: instalación portable detectable", (root, s) => {
            using (var d = Downloader(_ => Zip("xenia_canary.exe", Pe())))
                True(EmulatorInstaller.Install(s.GetStatus("Xbox360"), d, null, CancellationToken.None, new[] { Package() }).GetAwaiter().GetResult());
            True(File.Exists(Path.Combine(Path.GetDirectoryName(s.GetStatus("Xbox360").ExecutablePath), "portable.txt")));
            Equal(0, s.GetStatus("Xbox360").SetupMessages.Count);
        });
        Test("Xbox: cancelar la descarga del HDD deja todo sin publicar", (root, s) => {
            var hdd = Package(); hdd.Url += "?hdd"; hdd.DiskImage = true;
            using (var ct = new CancellationTokenSource())
            using (var d = Downloader(u => { if (u.Query.Length > 0) ct.Cancel(); return u.Query.Length > 0 ? Qcow() : Zip("xemu.exe", Pe()); }))
                Throws<OperationCanceledException>(() => EmulatorInstaller.Install(s.GetStatus("Xbox"), d, null, ct.Token, new[] { Package(), hdd }).GetAwaiter().GetResult());
            True(!s.GetStatus("Xbox").Installed); Equal(0, Directory.GetDirectories(s.GetStatus("Xbox").EmulatorFolder).Length);
        });
        Test("SHA256 fijado no se reemplaza por un digest distinto", (root, s) => {
            var p = OfficialPackages.For("Xbox360")[0];
            string json = "{\"assets\":[{\"name\":\"" + p.Name + "\",\"browser_download_url\":\"" + p.Url + "\",\"digest\":\"sha256:" + new string('a', 64) + "\"}]}";
            using (var d = Downloader(_ => Encoding.UTF8.GetBytes(json))) Throws<NexoException>(() => OfficialPackages.Resolve(p, d, CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("Cuatro sistemas: pantalla completa solo para juegos y argumentos citados", (root, s) => {
            string[][] expected = { new[] { "-f", "g a.gbc" }, new[] { "-f", "g a.gba" }, new[] { "-full-screen", "-dvd_path", "g a.iso" }, new[] { "--fullscreen", "g a.iso" } };
            for (int i = 0; i < 4; i++) {
                var args = Launcher.GameArguments(Ids[i], Exes[i], new GameEntry(Ids[i], "g", expected[i].Last()), null);
                Equal(string.Join("|", expected[i]), string.Join("|", args)); Equal(0, Launcher.EmulatorArguments(Ids[i]).Length);
                Equal("custom|g a.iso", string.Join("|", Launcher.GameArguments(Ids[i], Exes[i], new GameEntry(Ids[i], "g", "g a.iso"), new[] { "custom", "{game}" })));
            }
        });
        Test("mGBA: perfiles nuevos, idempotencia y perfil personalizado intacto", (root, s) => {
            var status = Emu(s, "GBA", "mGBA.exe"); string folder = Path.GetDirectoryName(status.ExecutablePath);
            True(ControllerSetup.Prepare(status, 0, new string[0]).Changed);
            string config = Path.Combine(folder, "config.ini"), before = File.ReadAllText(config);
            True(before.Contains("[gba.input-profile.Xbox 360 Controller]")); True(before.Contains("keyA = 0"));
            True(!ControllerSetup.Prepare(status, 0, new string[0]).Changed); Equal(before, File.ReadAllText(config));
            File.WriteAllText(config, "[gba.input.SDLB]\r\nkeyA = 9\r\n");
            True(!ControllerSetup.Prepare(status, 0, new string[0]).Changed); True(File.ReadAllText(config).Contains("keyA = 9"));
            File.Delete(config); True(ControllerSetup.Prepare(status, 0, new string[0]).Changed);
        });
        Test("Xbox/Xenia: controles propios no se pisan", (root, s) => {
            var xbox = Emu(s, "Xbox", "xemu.exe"); string folder = Path.GetDirectoryName(xbox.ExecutablePath);
            string cfg = Put(folder, "xemu.toml", Encoding.UTF8.GetBytes("[input]\nauto_bind = false\n[input.bindings]\nport1 = 'mi mando'\n"));
            string before = File.ReadAllText(cfg); ControllerSetup.Prepare(xbox, 0, new string[0]); Equal(before, File.ReadAllText(cfg));
            var xenia = Emu(s, "Xbox360", "xenia_canary.exe"); folder = Path.GetDirectoryName(xenia.ExecutablePath);
            ControllerSetup.Prepare(xenia, 0, new string[0]); cfg = Path.Combine(folder, "xenia-canary.config.toml"); True(File.ReadAllText(cfg).Contains("hid = \"xinput\""));
            File.WriteAllText(cfg, "[HID]\nhid = \"sdl\"\n"); before = File.ReadAllText(cfg); ControllerSetup.Prepare(xenia, 0, new string[0]); Equal(before, File.ReadAllText(cfg));
        });
        Test("Portabilidad: datos globales existentes no se ocultan", (root, s) => {
            string global = Put(root, "global/data", new byte[] { 1 });
            for (int i = 0; i < 4; i++) {
                var status = Emu(s, Ids[i], Exes[i]);
                True(!ControllerSetup.Prepare(status, 0, new[] { global }).Changed);
                True(!GraphicsSetup.Prepare(status, Gpu(3), new[] { global }).Changed);
            }
        });
        Test("Gráficos Xbox: cuatro niveles y ascenso con procedencia", (root, s) => {
            var status = Emu(s, "Xbox", "xemu.exe"); string file = Path.Combine(Path.GetDirectoryName(status.ExecutablePath), "xemu.toml");
            for (int i = 0; i < 4; i++) {
                GraphicsSetup.Prepare(status, Gpu(i), new string[0]); Equal((i + 1).ToString(), new ControllerConfig(file).ReadSection("display.quality")["surface_scale"]);
            }
            File.WriteAllText(file, "[display.quality]\nsurface_scale = 7\n"); string before = File.ReadAllText(file);
            True(GraphicsSetup.Prepare(status, Gpu(0), new string[0]).Message.Contains("personalizados")); Equal(before, File.ReadAllText(file));
        });
        Test("Gráficos Xenia: D3D12, escalado 1/2/3 y renderer propio respetado", (root, s) => {
            var status = Emu(s, "Xbox360", "xenia_canary.exe"); string file = Path.Combine(Path.GetDirectoryName(status.ExecutablePath), "xenia-canary.config.toml");
            for (int i = 0; i < 4; i++) {
                GraphicsSetup.Prepare(status, Gpu(i), new string[0]); var cfg = new ControllerConfig(file).ReadSection("GPU");
                Equal(new[] { "1", "2", "2", "3" }[i], cfg["draw_resolution_scale_x"]); Equal(cfg["draw_resolution_scale_x"], cfg["draw_resolution_scale_y"]); Equal("\"d3d12\"", cfg["gpu"]);
            }
            File.WriteAllText(file, "[GPU]\ngpu = \"vulkan\"\ndraw_resolution_scale_x = 4\ndraw_resolution_scale_y = 4\n"); string before = File.ReadAllText(file);
            GraphicsSetup.Prepare(status, Gpu(0), new string[0]); Equal(before, File.ReadAllText(file));
        });
        Test("Gráficos mGBA: píxel nítido y configuración global personalizada", (root, s) => {
            var status = Emu(s, "GBA", "mGBA.exe"); string file = Path.Combine(Path.GetDirectoryName(status.ExecutablePath), "config.ini");
            GraphicsSetup.Prepare(status, Gpu(3), new string[0]); var cfg = new ControllerConfig(file).ReadSection("ports.qt"); Equal("1", cfg["lockIntegerScaling"]); Equal("0", cfg["resampleVideo"]);
            File.WriteAllText(file, "resampleVideo=1\n[ports.qt]\nlockIntegerScaling=1\n"); string before = File.ReadAllText(file);
            GraphicsSetup.Prepare(status, Gpu(3), new string[0]); Equal(before, File.ReadAllText(file));
        });
        Test("Xenia: TOML real con comentarios, comillas simples y procedencia", (root, s) => {
            var status = Emu(s, "Xbox360", "xenia_canary.exe"); string folder = Path.GetDirectoryName(status.ExecutablePath);
            string file = Put(folder, "xenia-canary.config.toml", Encoding.UTF8.GetBytes("[GPU] # graphics\ngpu = 'any' # backend\ndraw_resolution_scale_x = 1 # pixels\ndraw_resolution_scale_y = 1 # pixels\n[HID]\nhid = 'any' # input\n"));
            ControllerSetup.Prepare(status, 0, new string[0]); GraphicsSetup.Prepare(status, Gpu(1), new string[0]);
            True(File.ReadAllText(file).Contains("hid = \"xinput\" # input")); True(File.ReadAllText(file).Contains("draw_resolution_scale_x = 2 # pixels"));
            GraphicsSetup.Prepare(status, Gpu(3), new string[0]); Equal("3", new ControllerConfig(file, true).ReadSection("GPU")["draw_resolution_scale_x"]);
            File.WriteAllText(file, "[GPU]\ngpu = 'custom#backend' # kept\ndraw_resolution_scale_x = 4 # personal\ndraw_resolution_scale_y = 4\n[HID]\nhid = 'sdl' # personal\n"); string before = File.ReadAllText(file);
            ControllerSetup.Prepare(status, 0, new string[0]); GraphicsSetup.Prepare(status, Gpu(0), new string[0]); Equal(before, File.ReadAllText(file));
        });
        Test("TOML ambiguo se conserva; Xenia sin mando no fija un handler", (root, s) => {
            var status = Emu(s, "Xbox360", "xenia_canary.exe"); string folder = Path.GetDirectoryName(status.ExecutablePath), path = Path.Combine(folder, "xenia-canary.config.toml");
            ControllerSetup.Prepare(status, null, new string[0]); True(!File.Exists(path));
            foreach (string content in new[] { "[GPU]\ngpu = '''multiline\n[HID]\n'''\n", "['GPU']\ngpu = 'vulkan'\n", "[GPU]\ngpu = 'any'\ngpu = 'vulkan'\n" }) {
                File.WriteAllText(path, content); GraphicsSetup.Prepare(status, Gpu(3), new string[0]); Equal(content, File.ReadAllText(path));
            }
        });
        Test("Xbox: importa MCPX, flash y QCOW2; estado y rutas portables", (root, s) => {
            var status = Emu(s, "Xbox", "xemu.exe"); Equal(3, status.SetupMessages.Count);
            string mcpx = Put(root, "input/mcpx.bin", new byte[512]), bios = Put(root, "input/bios.bin", new byte[262144]), hdd = Put(root, "input/disk.qcow2", Qcow());
            var imported = s.ImportSystemFilesAsync("Xbox", new[] { mcpx, bios, hdd }).GetAwaiter().GetResult(); True(imported.Changed); Equal(0, s.GetStatus("Xbox").SetupMessages.Count);
            string cfg = File.ReadAllText(Path.Combine(Path.GetDirectoryName(status.ExecutablePath), "xemu.toml")); True(!cfg.Contains(root)); True(cfg.Contains("system/mcpx_1.0.bin"));
            True(File.Exists(mcpx) && File.Exists(bios) && File.Exists(hdd));
        });
        Test("Xbox: importación inválida conserva anteriores y rutas propias", (root, s) => {
            var status = Emu(s, "Xbox", "xemu.exe"); string folder = Path.GetDirectoryName(status.ExecutablePath);
            string old = Put(folder, "system/flash.bin", new byte[] { 99 }); string mcpx = Put(root, "in/mcpx.bin", new byte[512]); string bad = Put(root, "in/bad.bin", new byte[200]);
            Throws<NexoException>(() => s.ImportSystemFilesAsync("Xbox", new[] { mcpx, bad }).GetAwaiter().GetResult()); Equal((byte)99, File.ReadAllBytes(old)[0]); True(!File.Exists(Path.Combine(folder, "system", "mcpx_1.0.bin")));
            string config = Put(folder, "xemu.toml", Encoding.UTF8.GetBytes("[sys.files]\nbootrom_path = \"custom.bin\"\n"));
            s.ImportSystemFilesAsync("Xbox", new[] { mcpx }).GetAwaiter().GetResult(); True(File.ReadAllText(config).Contains("custom.bin"));
        });
        Test("Xbox: QCOW2 rechaza backing externo, cifrado y truncado", (root, s) => {
            var bytes = Qcow(); string path = Put(root, "h.qcow2", bytes); True(XboxFiles.IsQcow(path));
            bytes[15] = 1; File.WriteAllBytes(path, bytes); True(!XboxFiles.IsQcow(path)); bytes[15] = 0; bytes[35] = 1; File.WriteAllBytes(path, bytes); True(!XboxFiles.IsQcow(path));
            File.WriteAllBytes(path, new byte[10]); True(!XboxFiles.IsQcow(path));
        });
        Test("Xbox: importación cancelada no escribe archivos", (root, s) => {
            var status = Emu(s, "Xbox", "xemu.exe"); string path = Put(root, "mcpx.bin", new byte[512]);
            using (var ct = new CancellationTokenSource()) { ct.Cancel(); Throws<OperationCanceledException>(() => s.ImportSystemFilesAsync("Xbox", new[] { path }, ct.Token).GetAwaiter().GetResult()); }
            True(!Directory.Exists(Path.Combine(Path.GetDirectoryName(status.ExecutablePath), "system")));
        });
        Test("Escaneo GBC/GBA/Xbox y ZIP: biblioteca y extracción reutilizables", (root, s) => {
            string[] ext = { ".gbc", ".gba", ".iso", ".xex" };
            for (int i = 0; i < 4; i++) {
                string folder = s.GetStatus(Ids[i]).GamesFolder; Put(folder, "normal" + ext[i], new byte[] { 1 });
                Put(folder, "comprimido.zip", Zip("extraido" + ext[i], new byte[] { 2 }));
                var scan = s.ScanGamesDetailedAsync(Ids[i]).GetAwaiter().GetResult(); Equal(1, scan.Games.Count); Equal(1, scan.Archives.Count);
                bool recycled = false;
                ArchiveExtractor.ExtractAsync(scan.Archives[0], folder, null, CancellationToken.None, _ => recycled = true).GetAwaiter().GetResult(); True(recycled);
                Equal(2, s.ScanGamesAsync(Ids[i]).GetAwaiter().GetResult().Count);
                True(ArchiveInspector.IsArchive(Ids[i], "juego.7z") && ArchiveInspector.IsArchive(Ids[i], "juego.rar"));
            }
        });
        Test("Xbox360: GOD/STFS identificados; partidas, DLC y módulos excluidos", (root, s) => {
            string folder = s.GetStatus("Xbox360").GamesFolder;
            Put(folder, "god", Stfs(0x7000)); Put(folder, "arcade", Stfs(0xd0000)); Put(folder, "partida", Stfs(1)); Put(folder, "dlc", Stfs(2)); Put(folder, "basura", new byte[8]);
            Put(folder, "Mi juego/default.xex", new byte[24]); Put(folder, "Mi juego/modulo.xex", new byte[24]);
            var games = s.ScanGamesAsync("Xbox360").GetAwaiter().GetResult(); Equal(3, games.Count); True(games.Any(g => g.Title == "Mi juego")); True(games.Any(g => g.Title == "Juego de prueba"));
            Equal("4D5307E6", XboxMetadata.Xbox360Container(Path.Combine(folder, "god")).Item1);
        });
        Test("Xbox360: Title ID XEX y offsets inválidos", (root, s) => {
            byte[] b = new byte[128]; Encoding.ASCII.GetBytes("XEX2").CopyTo(b, 0); Be(b, 20, 1); Be(b, 24, 0x40006); Be(b, 28, 64); Be(b, 76, 0x4d5307e6);
            string p = Put(root, "default.xex", b); Equal("4D5307E6", XboxMetadata.XexTitleId(p)); Be(b, 28, uint.MaxValue); File.WriteAllBytes(p, b); Equal(null, XboxMetadata.XexTitleId(p));
        });
        Test("Xbox: XISO obtiene certificado XBE sin extraer y rechaza offsets", (root, s) => {
            string p = Put(root, "disc.iso", Xiso()); Equal("4D530001", XboxMetadata.XboxTitleId(p, CancellationToken.None));
            var b = Xiso(); BitConverter.GetBytes(uint.MaxValue).CopyTo(b, 32 * 2048 + 20); File.WriteAllBytes(p, b); Equal(null, XboxMetadata.XboxTitleId(p, CancellationToken.None));
            using (var ct = new CancellationTokenSource()) { ct.Cancel(); Throws<OperationCanceledException>(() => XboxMetadata.XboxTitleId(p, ct.Token)); }
        });
        Test("Xbox: compatibilidad oficial mapea estados y consulta el ID del disco", (root, s) => {
            string[] from = { "Broken", "Intro", "Starts", "Playable", "Perfect", "Unknown" }, to = { "Nothing", "Intro", "Ingame", "Playable", "Perfect", "Desconocido" };
            for (int i = 0; i < from.Length; i++) Equal(to[i], XboxCompatibility.Parse(Compat(from[i]), CancellationToken.None)["4D530001"].Status);
            string folder = s.GetStatus("Xbox").GamesFolder; Put(folder, ".nexo-cache/xemu-compatibility.json", Compat("Starts")); Put(folder, "test.iso", Xiso());
            var game = s.ScanGamesAsync("Xbox").GetAwaiter().GetResult().Single(); Equal("Ingame", game.Compatibility.Status); Equal("https://xemu.app/titles/4d530001/", game.Compatibility.ReportUrl);
            Throws<InvalidDataException>(() => XboxCompatibility.Parse(Encoding.UTF8.GetBytes("[{\"url\":\"https://evil/4D530001\",\"status\":\"Playable\"}]"), CancellationToken.None));
        });
        Test("Xbox: caché semanal, sin red y respuesta inválida conservan la copia", (root, s) => {
            string folder = s.GetStatus("Xbox").GamesFolder; DateTime now = DateTime.UtcNow; int downloads = 0;
            var cache = XboxCompatibility.Cache(folder, _ => { downloads++; return Task.FromResult(Compat("Playable")); }, () => now);
            True(cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult()); Equal(1, downloads);
            True(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult()); Equal(1, downloads);
            now = now.AddDays(8); True(cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult()); Equal(2, downloads);
            now = now.AddDays(8); var offline = XboxCompatibility.Cache(folder, _ => throw new IOException("sin red"), () => now);
            True(!offline.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult()); Equal("Playable", XboxCompatibility.Read(folder, CancellationToken.None)["4D530001"].Status);
            var bad = XboxCompatibility.Cache(folder, _ => Task.FromResult(Encoding.UTF8.GetBytes("<html>fallo</html>")), () => now);
            True(!bad.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult()); Equal("Playable", XboxCompatibility.Read(folder, CancellationToken.None)["4D530001"].Status);
            True(!XboxCompatibility.Allowed(new Uri("https://evil.example/compat.json"))); True(!XboxCompatibility.Allowed(new Uri("http://xemu.app/compat.json")));
        });
        Test("Arte: fuentes de cuatro sistemas, nombre de carpeta XEX y ningún snap", (root, s) => {
            string[] repos = { "Nintendo_-_Game_Boy_Color", "Nintendo_-_Game_Boy_Advance", "Microsoft_-_Xbox", "Microsoft_-_Xbox_360" };
            for (int i = 0; i < 4; i++) {
                string folder = s.GetStatus(Ids[i]).GamesFolder, path = Put(folder, i == 3 ? "Titulo (USA)/default.xex" : "Titulo (USA)." + new[] { "gbc", "gba", "iso", "xex" }[i], new byte[] { 1 });
                var game = new GameEntry(Ids[i], "Titulo", path); var paths = new ArtworkPaths(game, folder); var sources = ArtworkSources.For(game, paths, CancellationToken.None);
                True(sources.Covers.Any(u => u.AbsoluteUri.Contains(repos[i]) && Uri.UnescapeDataString(u.AbsoluteUri).Contains("Titulo (USA)"))); Equal(0, sources.Backgrounds.Count);
            }
        });
        Test("Errores al preparar no bloquean el lanzamiento de sistemas nuevos", (root, s) => {
            for (int i = 0; i < 4; i++) {
                Emu(s, Ids[i], Exes[i]); string path = Put(s.GetStatus(Ids[i]).GamesFolder, "test." + (i == 0 ? "gbc" : i == 1 ? "gba" : "iso"), new byte[] { 1 });
                int started = 0; var service = new NexoService(root, _ => throw new IOException("mandos"), _ => ++started, () => Gpu(3), (_, __) => throw new IOException("gráficos"));
                Equal(1, service.LaunchGame(new GameEntry(Ids[i], "test", path))); True(service.LastGraphicsSetup.Message.Contains("gráficos"));
            }
        });
    }
    private static byte[] Compat(string state) => Encoding.UTF8.GetBytes("[{\"name\":\"Test\",\"url\":\"/titles/4d530001#Test\",\"status\":\"" + state + "\"}]");
    private static GpuInfo Gpu(int level) => GpuDetector.Select(new[] { new GpuAdapter(new[] { "Intel UHD", "NVIDIA GTX 1650", "NVIDIA RTX 3070", "NVIDIA RTX 4070 SUPER" }[level], new[] { 1L, 4L, 8L, 12L }[level] << 30) }, 3440, 1440);
    private static string Put(string root, string relative, byte[] data) { string p = Path.Combine(root, relative.Replace('/', '\\')); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, data); return p; }
    private static SystemStatus Emu(NexoService s, string id, string exe) { Put(s.GetStatus(id).EmulatorFolder, "package/" + exe, Pe()); return s.GetStatus(id); }
    private static byte[] Pe() { var b = new byte[256]; b[0] = 0x4d; b[1] = 0x5a; b[60] = 128; b[128] = 0x50; b[129] = 0x45; b[132] = 0x64; b[133] = 0x86; return b; }
    private static void Be(byte[] b, int p, uint v) { b[p] = (byte)(v >> 24); b[p + 1] = (byte)(v >> 16); b[p + 2] = (byte)(v >> 8); b[p + 3] = (byte)v; }
    private static byte[] Qcow() { var b = new byte[512]; Be(b, 0, 0x514649fb); Be(b, 4, 3); Be(b, 20, 16); Be(b, 24, 2); return b; }
    private static byte[] Stfs(uint type) { var b = new byte[0xa000]; Encoding.ASCII.GetBytes("LIVE").CopyTo(b, 0); Be(b, 0x340, 0x971a); Be(b, 0x344, type); Be(b, 0x360, 0x4d5307e6); Encoding.BigEndianUnicode.GetBytes("Juego de prueba").CopyTo(b, 0x411); return b; }
    private static byte[] Xiso() {
        var b = new byte[36 * 2048]; int v = 32 * 2048, d = 33 * 2048, x = 34 * 2048;
        Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA").CopyTo(b, v); BitConverter.GetBytes(33u).CopyTo(b, v + 20); BitConverter.GetBytes(2048u).CopyTo(b, v + 24);
        BitConverter.GetBytes(34u).CopyTo(b, d + 4); BitConverter.GetBytes(2048u).CopyTo(b, d + 8); b[d + 13] = 11; Encoding.ASCII.GetBytes("default.xbe").CopyTo(b, d + 14);
        Encoding.ASCII.GetBytes("XBEH").CopyTo(b, x); BitConverter.GetBytes(0x10000u).CopyTo(b, x + 0x104); BitConverter.GetBytes(0x10180u).CopyTo(b, x + 0x118); BitConverter.GetBytes(0x4d530001u).CopyTo(b, x + 0x188); return b;
    }
    private static byte[] Zip(string name, byte[] b) { using (var m = new MemoryStream()) { using (var z = new ZipArchive(m, ZipArchiveMode.Create, true)) using (var f = z.CreateEntry(name).Open()) f.Write(b, 0, b.Length); return m.ToArray(); } }
    private static OfficialPackage Package() => new OfficialPackage { Name = "fixture.zip", Url = "https://github.com/official/fixture.zip" };
    private static PackageDownloader Downloader(Func<Uri, byte[]> response) => new PackageDownloader(new FakeHandler(response));
    private sealed class FakeHandler : HttpMessageHandler {
        private readonly Func<Uri, byte[]> response; internal FakeHandler(Func<Uri, byte[]> response) { this.response = response; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(response(request.RequestUri)) }); }
    }
    private static void True(bool b) { if (!b) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Esperado " + expected + "; recibido " + actual); }
    private static void Throws<T>(Action f) where T : Exception { try { f(); } catch (T) { return; } throw new Exception("Se esperaba " + typeof(T).Name); }
}
