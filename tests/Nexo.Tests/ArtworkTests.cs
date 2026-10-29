using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexo.Core;

internal static class ArtworkTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run("Arte: " + name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-art-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-art-")) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        Test("PS3/PS4/PS5: PIC1, PIC0 y portada local sin red en escaneo", (root, service) =>
        {
            foreach (string id in new[] { "PS3", "PS4", "PS5" })
            {
                string folder = Path.Combine(root, "Juegos", id, "Mi juego");
                string content = Path.Combine(folder, id == "PS3" ? "PS3_GAME" : "sce_sys");
                Put(Path.Combine(folder, id == "PS3" ? "PS3_GAME/USRDIR/EBOOT.BIN" : "eboot.bin"), new byte[4]);
                Put(Path.Combine(content, "param.json"), Encoding.UTF8.GetBytes("{\"title\":\"Juego\"}"));
                Put(Path.Combine(content, "ICON0.PNG"), Png()); Put(Path.Combine(content, "PIC0.PNG"), Png(false, 1280, 720));
                var first = service.ScanGamesAsync(id).GetAwaiter().GetResult().Single();
                Check(first.CoverPath.EndsWith("ICON0.PNG", StringComparison.OrdinalIgnoreCase) && first.BackgroundPath.EndsWith("PIC0.PNG", StringComparison.OrdinalIgnoreCase));
                Put(Path.Combine(content, "PIC1.PNG"), Png(false, 1920, 1080));
                var second = service.ScanGamesAsync(id).GetAwaiter().GetResult().Single(); Check(second.BackgroundPath.EndsWith("PIC1.PNG", StringComparison.OrdinalIgnoreCase));
                Check(second.Compatibility != null && second.Platform == null);
            }
        });
        Test("PSP: ISO lee solo ICON0/PIC1, valida y publica sin red con opción desactivada", (root, service) =>
        {
            Disable(root);
            string file = Path.Combine(root, "Juegos/PSP/Juego (USA).iso");
            MiniIso(file, "PSP_GAME", new Dictionary<string, byte[]> { ["ICON0.PNG"] = Png(), ["PIC1.PNG"] = Png(false, 1280, 720) });
            var scanned = service.ScanGamesAsync("PSP").GetAwaiter().GetResult().Single(); Check(scanned.CoverPath == null);
            var art = service.EnsureArtAsync(scanned, CancellationToken.None).GetAwaiter().GetResult();
            Check(art.CoverPath.EndsWith("Juego (USA).icon.png") && art.BackgroundPath.EndsWith("Juego (USA).embedded.bg.png"));
            Check(File.ReadAllBytes(art.CoverPath).SequenceEqual(Png()));
            var again = service.ScanGamesAsync("PSP").GetAwaiter().GetResult().Single(); Equal(art.BackgroundPath, again.BackgroundPath);
            Check(ReferenceEquals(scanned.Compatibility, art.Compatibility));
        });
        Test("ISO: anidado, límite, ruta inválida, truncado y cancelación", (root, service) =>
        {
            string iso = Path.Combine(root, "mini.iso"); MiniIso(iso, "PSP_GAME", new Dictionary<string, byte[]> { ["ICON0.PNG"] = Png() });
            Check(Ps3IsoInspector.ReadFile(iso, new[] { "PSP_GAME", "ICON0.PNG" }, 8, CancellationToken.None) == null);
            Check(Ps3IsoInspector.ReadFile(iso, new[] { "..", "ICON0.PNG" }, 1024, CancellationToken.None) == null);
            Fail<OperationCanceledException>(() => Ps3IsoInspector.ReadFile(iso, new[] { "PSP_GAME", "ICON0.PNG" }, 1024, new CancellationToken(true)));
            using (var f = File.OpenWrite(iso)) f.SetLength(f.Length - 2048);
            Check(Ps3IsoInspector.ReadFile(iso, new[] { "PSP_GAME", "ICON0.PNG" }, 1024, CancellationToken.None) == null);
        });
        Test("PS2: serial de SYSTEM.CNF alimenta URL exacta de xlenore", (root, service) =>
        {
            string file = Path.Combine(root, "Juegos/PS2/Un titulo.iso");
            MiniIso(file, null, new Dictionary<string, byte[]> { ["SYSTEM.CNF"] = Encoding.ASCII.GetBytes("BOOT2 = cdrom0:\\SLUS_209.46;1\r\n") });
            var game = new GameEntry("PS2", "titulo", file);
            var source = ArtworkSources.For(game, new ArtworkPaths(game, Path.GetDirectoryName(file)), CancellationToken.None);
            Equal("https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/SLUS-20946.jpg", source.Covers.First().AbsoluteUri);
            Equal(0, source.Backgrounds.Count);
        });
        Test("GameCube/Wii: ISO, GCM, RVZ, WBFS y CISO exponen plataforma e ID6", (root, service) =>
        {
            foreach (string format in new[] { "iso", "gcm", "rvz", "wbfs", "ciso" })
            foreach (bool wii in new[] { false, true })
            {
                string file = Path.Combine(root, "Juegos/GameCube-Wii/" + (wii ? "w" : "g") + "." + format);
                Disc(file, format, wii);
                var identity = ArtworkMetadata.NintendoDisc(file); Equal(wii ? "Wii" : "GameCube", identity.Item2);
                Equal(wii ? "RMGE01" : "GMSE01", identity.Item1);
                var games = service.ScanGamesAsync("GameCube-Wii").GetAwaiter().GetResult();
                Equal(identity.Item2, games.Single(g => g.Path == file.Replace('/', '\\')).Platform);
            }
        });
        Test("Disc: firmas ajenas, RVZ truncado y shift WBFS absurdo no inventan plataforma", (root, service) =>
        {
            string file = Path.Combine(root, "unknown.iso"); Put(file, Encoding.ASCII.GetBytes("RMGE01" + new string(' ', 64)));
            Check(ArtworkMetadata.NintendoDisc(file) == null);
            file = Path.Combine(root, "bad.rvz"); Put(file, Encoding.ASCII.GetBytes("RVZ\x01" + new string(' ', 28))); Check(ArtworkMetadata.NintendoDisc(file) == null);
            file = Path.Combine(root, "bad.wbfs"); Disc(file, "wbfs", true);
            using (var f = File.OpenWrite(file)) { f.Position = 8; f.WriteByte(255); } Check(ArtworkMetadata.NintendoDisc(file) == null);
        });
        Test("Config: descarga true por defecto; false persiste al elegir ejecutable; tipo estricto", (root, service) =>
        {
            var config = new ConfigStore(root); Check(config.DownloadArtwork); Disable(root); Check(!config.DownloadArtwork);
            config.SetExecutable("PS2", null); Check(!new ConfigStore(root).DownloadArtwork);
            File.WriteAllText(Path.Combine(root, "nexo.json"), "{\"version\":1,\"systems\":{},\"downloadArtwork\":\"false\"}");
            Fail<NexoException>(() => new ConfigStore(root));
        });
        Test("PSP: PIC0 como alternativa; icono inválido no se publica", (root, service) =>
        {
            Disable(root); string file = Path.Combine(root, "Juegos/PSP/Alternative.iso");
            MiniIso(file, "PSP_GAME", new Dictionary<string, byte[]> { ["ICON0.PNG"] = new byte[40], ["PIC0.PNG"] = Png(false, 1280, 720) });
            var result = service.EnsureArtAsync(new GameEntry("PSP", "a", file), CancellationToken.None).GetAwaiter().GetResult();
            Check(result.CoverPath == null && result.BackgroundPath != null);
        });
        Test("Portada manual cover.png de juegos-carpeta conserva prioridad sobre ICON0", (root, service) =>
        {
            string folder = Path.Combine(root, "Juegos/PS3/Game");
            Put(Path.Combine(folder, "PS3_GAME/USRDIR/EBOOT.BIN"), new byte[4]);
            Put(Path.Combine(folder, "PS3_GAME/ICON0.PNG"), Png()); string custom = Path.Combine(folder, "cover.png"); Put(custom, Png());
            Equal(Path.GetFullPath(custom), service.ScanGamesAsync("PS3").GetAwaiter().GetResult().Single().CoverPath);
        });
        Test("PS3: la portada real en línea sustituye al ICON0 apaisado, que queda de respaldo", (root, service) =>
        {
            string folder = Path.GetFullPath(Path.Combine(root, "Juegos/PS3/Game"));
            string eboot = Path.Combine(folder, "PS3_GAME/USRDIR/EBOOT.BIN"); Put(eboot, new byte[4]);
            Put(Path.Combine(folder, "PS3_GAME/ICON0.PNG"), Png());
            string games = Path.GetFullPath(Path.Combine(root, "Juegos", "PS3"));
            var game = new GameEntry("PS3", "Game", eboot, Path.Combine(folder, "PS3_GAME/ICON0.PNG"));
            using (var offline = new ArtworkDownloader(new Handler((u, n) => new HttpResponseMessage(HttpStatusCode.NotFound))))
                Check(new ArtworkService(offline).EnsureAsync(game, games, true, CancellationToken.None).GetAwaiter().GetResult().CoverPath.EndsWith("ICON0.PNG"));
            foreach (var miss in Directory.GetFiles(Path.Combine(root, "Juegos/PS3/covers/.nexo-art"))) File.Delete(miss);
            var handler = new Handler((u, n) => ImageResponse(Png(true)));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var art = new ArtworkService(downloader).EnsureAsync(game, games, true, CancellationToken.None).GetAwaiter().GetResult();
                Check(art.CoverPath.EndsWith(Path.Combine("covers", "Game.jpg")));
                Check(service.ScanGamesAsync("PS3").GetAwaiter().GetResult().Single().CoverPath.EndsWith("Game.jpg"));
            }
        });
        Test("Fallo de escritura opcional no bloquea biblioteca ni descarga sin destino", (root, service) =>
        {
            var game = Rom(root); string blocked = Path.Combine(Path.GetDirectoryName(game.Path), "covers"); Put(blocked, new byte[4]);
            var handler = new Handler((u, n) => ImageResponse(Png()));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var art = new ArtworkService(downloader); Check(Get(art, game).CoverPath == null); Equal(0, handler.Calls);
                File.Delete(blocked); Check(Get(art, game).CoverPath != null); Equal(1, handler.Calls);
            }
        });
        Test("Switch: sin fuentes de carátulas en línea; solo arte local", (root, service) =>
        {
            string file = Path.Combine(root, "Juegos/Switch/Juego [0100000011D90000].nsp"); Put(file, new byte[16]);
            var game = new GameEntry("Switch", "Juego", file);
            var sources = ArtworkSources.For(game, new ArtworkPaths(game, Path.GetDirectoryName(file)), CancellationToken.None);
            Equal(0, sources.Covers.Count); Equal(0, sources.Backgrounds.Count);
            foreach (string url in new[] { "https://tinfoil.media/ti/0100000011D90000/0/0/", "https://tinfoil.io/Title/0100000011D90000" })
                Check(!ArtworkDownloader.Allowed(new Uri(url)));
            var handler = new Handler((u, n) => ImageResponse(Png(false, 1280, 720)));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var art = Get(new ArtworkService(downloader), game);
                Check(art.CoverPath == null && art.BackgroundPath == null); Equal(0, handler.Calls);
            }
        });
        Test("Libretro: nombre No-Intro y sistema por extensión, sin snaps de baja resolución", (root, service) =>
        {
            var game = Rom(root, "A & B (USA).gba");
            var sources = ArtworkSources.For(game, new ArtworkPaths(game, Path.GetDirectoryName(game.Path)), CancellationToken.None);
            Check(sources.Covers.Single().AbsoluteUri.Contains("Nintendo_-_Game_Boy_Advance/master/Named_Boxarts/A%20_%20B%20%28USA%29.png"));
            Equal(0, sources.Backgrounds.Count);
        });
        Test("Caché positiva: portada se descarga una sola vez y no se pide snap", (root, service) =>
        {
            var game = Rom(root); var handler = new Handler((u, n) => ImageResponse(Png()));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var art = new ArtworkService(downloader);
                var all = Task.WhenAll(Enumerable.Range(0, 6).Select(_ => art.EnsureAsync(game, Path.GetDirectoryName(game.Path), true, CancellationToken.None))).GetAwaiter().GetResult();
                Equal(1, handler.Calls); Check(all.All(g => g.CoverPath != null && g.BackgroundPath == null));
                Check(Directory.GetFiles(Path.GetDirectoryName(all[0].CoverPath), "*.tmp").Length == 0);
            }
        });
        Test("Caché negativa: 404 dura siete días y sobrevive a otra instancia", (root, service) =>
        {
            DateTime now = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc); var game = Rom(root);
            var handler = new Handler((u, n) => new HttpResponseMessage(HttpStatusCode.NotFound));
            using (var downloader = new ArtworkDownloader(handler))
            {
                Get(new ArtworkService(downloader, () => now), game); Equal(1, handler.Calls);
                Get(new ArtworkService(downloader, () => now), game); Equal(1, handler.Calls);
                now = now.AddDays(8); Get(new ArtworkService(downloader, () => now), game); Equal(2, handler.Calls);
            }
        });
        Test("Error transitorio cacheado 30 minutos, no siete días", (root, service) =>
        {
            DateTime now = DateTime.UtcNow; var game = Rom(root); var handler = new Handler((u, n) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var art = new ArtworkService(downloader, () => now); Get(art, game); Get(art, game); Equal(1, handler.Calls);
                now = now.AddMinutes(31); Get(art, game); Equal(2, handler.Calls);
            }
        });
        Test("Desactivado conserva arte local y no hace ninguna petición", (root, service) =>
        {
            var game = Rom(root); string background = Path.Combine(Path.GetDirectoryName(game.Path), "Game (USA).bg.png"); Put(background, Png(false, 1280, 720));
            var handler = new Handler((u, n) => throw new Exception("No debería acceder a red"));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var actual = new ArtworkService(downloader).EnsureAsync(game, Path.GetDirectoryName(game.Path), false, CancellationToken.None).GetAwaiter().GetResult();
                Equal(background, actual.BackgroundPath); Equal(0, handler.Calls);
            }
        });
        Test("Sidecar del usuario prevalece y una caché negativa no lo oculta", (root, service) =>
        {
            var game = Rom(root); var handler = new Handler((u, n) => new HttpResponseMessage(HttpStatusCode.NotFound));
            using (var downloader = new ArtworkDownloader(handler))
            {
                var art = new ArtworkService(downloader); Get(art, game);
                string sidecar = Path.ChangeExtension(game.Path, ".png"); Put(sidecar, Png());
                Equal(sidecar, Get(art, game).CoverPath); Equal(1, handler.Calls);
            }
        });
        Test("Descarga rechaza HTTP, host ajeno, credenciales, puertos y redirección fuera del host", (root, service) =>
        {
            foreach (string url in new[] { "http://art.gametdb.com/a.png", "https://evil.example/a.png", "https://u:p@art.gametdb.com/a", "https://art.gametdb.com:444/a" })
                Check(!ArtworkDownloader.Allowed(new Uri(url)));
            var handler = new Handler((u, n) => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("https://evil.example/payload"); return r; });
            using (var downloader = new ArtworkDownloader(handler))
            { Check(downloader.GetAsync(new Uri("https://art.gametdb.com/a"), CancellationToken.None).GetAwaiter().GetResult().Bytes == null); Equal(1, handler.Calls); }
        });
        Test("Redirección HTTPS del mismo host admitida y bucle acotado", (root, service) =>
        {
            var handler = new Handler((u, n) => { if (n == 2) return ImageResponse(Png()); var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("/image", UriKind.Relative); return r; });
            using (var downloader = new ArtworkDownloader(handler)) Check(downloader.GetAsync(new Uri("https://art.gametdb.com/a"), CancellationToken.None).GetAwaiter().GetResult().Bytes != null);
            Equal(2, handler.Calls);
            handler = new Handler((u, n) => { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("/a", UriKind.Relative); return r; });
            using (var downloader = new ArtworkDownloader(handler)) Check(downloader.GetAsync(new Uri("https://art.gametdb.com/a"), CancellationToken.None).GetAwaiter().GetResult().Bytes == null);
            Equal(4, handler.Calls);
        });
        Test("Imagen: PNG/JPEG válidos; HTML, truncado y dimensiones excesivas rechazados", (root, service) =>
        {
            Equal(".png", ArtworkImage.Extension(Png())); Equal(".jpg", ArtworkImage.Extension(Png(true)));
            Check(ArtworkImage.Extension(Encoding.UTF8.GetBytes("<html>esto no es una imagen</html>")) == null);
            var bytes = Png(); Check(ArtworkImage.Extension(bytes.Take(bytes.Length - 12).ToArray()) == null);
            bytes[16] = 1; Check(ArtworkImage.Extension(bytes) == null);
            var game = Rom(root); var handler = new Handler((u, n) => ImageResponse(Encoding.UTF8.GetBytes("<html>falso JPG</html>")));
            using (var downloader = new ArtworkDownloader(handler)) Check(Get(new ArtworkService(downloader), game).CoverPath == null);
        });
        Test("Límite de descarga se aplica con y sin Content-Length", (root, service) =>
        {
            foreach (bool announced in new[] { false, true })
            {
                var handler = new Handler((u, n) =>
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(new byte[ArtworkDownloader.MaxBytes + 1])) };
                    if (announced) response.Content.Headers.ContentLength = ArtworkDownloader.MaxBytes + 1;
                    else response.Content.Headers.ContentLength = null;
                    return response;
                });
                using (var downloader = new ArtworkDownloader(handler)) Check(downloader.GetAsync(new Uri("https://art.gametdb.com/a"), CancellationToken.None).GetAwaiter().GetResult().Bytes == null);
            }
        });
        Test("Cancelación y timeout no dejan imagen parcial; cancelación no crea caché negativa", (root, service) =>
        {
            var game = Rom(root);
            using (var downloader = new ArtworkDownloader(new WaitingHandler(), TimeSpan.FromMilliseconds(25)))
            {
                Check(downloader.GetAsync(new Uri("https://art.gametdb.com/a"), CancellationToken.None).GetAwaiter().GetResult().Bytes == null);
                using (var cancel = new CancellationTokenSource(10))
                    Fail<OperationCanceledException>(() => new ArtworkService(downloader).EnsureAsync(game, Path.GetDirectoryName(game.Path), true, cancel.Token).GetAwaiter().GetResult());
            }
            Check(!Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Any());
            Check(!Directory.GetFiles(root, "*.miss", SearchOption.AllDirectories).Any());
        });
        Test("HD: umbral exacto PNG/JPEG; cada dimensión debe alcanzar el mínimo", (root, service) =>
        {
            foreach (bool jpeg in new[] { false, true })
            {
                Check(ArtworkHeader.IsHd(Png(jpeg, 1280, 720)));
                Check(ArtworkHeader.IsHd(Png(jpeg, 1920, 1080)));
                foreach (var size in new[] { Tuple.Create(1279, 720), Tuple.Create(1280, 719), Tuple.Create(480, 272), Tuple.Create(512, 384) })
                    Check(!ArtworkHeader.IsHd(Png(jpeg, size.Item1, size.Item2)));
            }
        });
        Test("HD: lector solo inspecciona cabeceras y rechaza cabeceras rotas o absurdas", (root, service) =>
        {
            foreach (bool jpeg in new[] { false, true })
            {
                using (var counted = new CountedStream(Png(jpeg, 1920, 1080)))
                { Check(ArtworkHeader.Dimensions(counted, out int w, out int h)); Equal(1920, w); Equal(1080, h); Check(counted.ReadCount < 1024); }
            }
            var bytes = Png(false, 1920, 1080); bytes[16] = 127; Check(!ArtworkHeader.IsHd(bytes));
            Check(!ArtworkHeader.IsHd(new byte[32])); Check(!ArtworkHeader.IsHd(Png(true).Take(30).ToArray()));
        });
        Test("HD: prioridad sidecar > PIC1 > descargado > generado; caché pequeña nunca se devuelve", (root, service) =>
        {
            string games = Path.Combine(root, "Juegos", "PS3"), folder = Path.Combine(games, "Game");
            string file = Path.Combine(folder, "PS3_GAME", "USRDIR", "EBOOT.BIN"); Put(file, new byte[4]);
            var game = new GameEntry("PS3", "Game", file); var paths = new ArtworkPaths(game, games);
            string user = Path.Combine(folder, "Game.bg.jpg"), own = Path.Combine(folder, "PS3_GAME", "PIC1.PNG");
            string cached = paths.BasePath + ".bg.png", generated = paths.BasePath + ".ai.png";
            Put(generated, Png(false, 1920, 1080)); Put(cached, Png(false, 512, 384)); Put(own, Png(false, 480, 272));
            Equal(generated, ArtworkPaths.Local(game, games).BackgroundPath);
            Put(cached, Png(false, 1280, 720)); Equal(cached, ArtworkPaths.Local(game, games).BackgroundPath);
            Put(own, Png(false, 1920, 1080)); Equal(own, ArtworkPaths.Local(game, games).BackgroundPath);
            Put(user, Png(true, 1280, 720)); Equal(user, ArtworkPaths.Local(game, games).BackgroundPath);
            File.Delete(user); File.Delete(own); File.Delete(cached); Put(generated, Png());
            Check(ArtworkPaths.Local(game, games).BackgroundPath == null);
        });
        Test("HD: PSP PIC1 pequeño se ignora y arte generado sobrevive al fallo de red", (root, service) =>
        {
            string file = Path.Combine(root, "Juegos", "PSP", "Small.iso");
            MiniIso(file, "PSP_GAME", new Dictionary<string, byte[]> { ["ICON0.PNG"] = Png(), ["PIC1.PNG"] = Png(false, 480, 272) });
            string games = Path.GetDirectoryName(file); var game = new GameEntry("PSP", "Small", file);
            string generated = new ArtworkPaths(game, games).BasePath + ".ai.png"; Put(generated, Png(false, 1920, 1080));
            using (var downloader = new ArtworkDownloader(new Handler((u, n) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
            {
                var art = new ArtworkService(downloader);
                var result = art.EnsureAsync(game, games, true, CancellationToken.None).GetAwaiter().GetResult();
                Equal(generated, result.BackgroundPath); Check(result.CoverPath.EndsWith(".icon.png"));
                Check(!File.Exists(new ArtworkPaths(game, games).BasePath + ".embedded.bg.png"));
                File.Delete(generated); Check(art.EnsureAsync(game, games, false, CancellationToken.None).GetAwaiter().GetResult().BackgroundPath == null);
            }
        });
        Test("HD: PSP propio precede a fondo descargado y a generado", (root, service) =>
        {
            string file = Path.Combine(root, "Juegos", "PSP", "HD.iso");
            MiniIso(file, "PSP_GAME", new Dictionary<string, byte[]> { ["PIC1.PNG"] = Png(false, 1280, 720) });
            var game = new GameEntry("PSP", "HD", file); var paths = new ArtworkPaths(game, Path.GetDirectoryName(file));
            Put(paths.BasePath + ".bg.png", Png(false, 1920, 1080)); Put(paths.BasePath + ".ai.png", Png(false, 1920, 1080));
            Disable(root); Check(service.EnsureArtAsync(game, CancellationToken.None).GetAwaiter().GetResult().BackgroundPath.EndsWith(".embedded.bg.png"));
        });
        Test("HD: descarga pequeña no se publica; HD no destruye caché antigua de baja resolución", (root, service) =>
        {
            string destination = Path.Combine(root, "bg");
            Check(ArtworkService.Publish(destination, Png(false, 512, 384), CancellationToken.None, true) == null);
            Check(!File.Exists(destination + ".png"));
            Put(destination + ".png", Png(false, 512, 384)); byte[] previous = File.ReadAllBytes(destination + ".png");
            string result = ArtworkService.Publish(destination, Png(false, 1280, 720), CancellationToken.None, true);
            Equal(destination + ".hd.png", result); Check(previous.SequenceEqual(File.ReadAllBytes(destination + ".png")));
        });
        Test("GameTDB: HQ antes de M y pequeña, con fallback probado", (root, service) =>
        {
            foreach (bool wii in new[] { false, true })
            {
                string file = Path.Combine(root, "Juegos", "GameCube-Wii", wii ? "Wii.iso" : "Cube.iso"); Disc(file, "iso", wii);
                var game = new GameEntry("GameCube-Wii", "Game", file);
                var sources = ArtworkSources.For(game, new ArtworkPaths(game, Path.GetDirectoryName(file)), CancellationToken.None).Covers;
                Check(sources[0].AbsolutePath.Contains("/coverHQ/US/"));
                Check(sources.FindIndex(u => u.AbsolutePath.Contains("/coverM/")) < sources.FindIndex(u => u.AbsolutePath.Contains("/cover/")));
                var handler = new Handler((u, n) => u.AbsolutePath.Contains("/coverM/") ? ImageResponse(Png(true)) : new HttpResponseMessage(HttpStatusCode.NotFound));
                using (var downloader = new ArtworkDownloader(handler)) Check(Get(new ArtworkService(downloader), game).CoverPath.EndsWith(".jpg"));
                Equal(3, handler.Calls); // US/EN HQ misses, then US M
            }
        });
        Test("Biblioteca: nombres iguales en subcarpetas no colisionan; rutas externas rechazadas", (root, service) =>
        {
            var first = Rom(root, "edicion1/Game.nes"); var second = Rom(root, "edicion2/Game.nes");
            string folder = Path.Combine(root, "Juegos/Retro");
            var one = new ArtworkPaths(first, folder); var two = new ArtworkPaths(second, folder); Check(one.BasePath != two.BasePath);
            var external = new GameEntry("PS2", "x", Path.Combine(root, "out.iso")); Put(external.Path, new byte[8]);
            Fail<NexoException>(() => service.EnsureArtAsync(external, CancellationToken.None).GetAwaiter().GetResult());
        });
    }
    private static GameEntry Get(ArtworkService art, GameEntry game) => art.EnsureAsync(game, Path.GetDirectoryName(game.Path), true, CancellationToken.None).GetAwaiter().GetResult();
    private static void Disable(string root) => File.WriteAllText(Path.Combine(root, "nexo.json"), "{\"version\":1,\"systems\":{},\"downloadArtwork\":false}");
    private static GameEntry Rom(string root, string name = "Game (USA).nes")
    { string file = Path.GetFullPath(Path.Combine(root, "Juegos/Retro", name)); Put(file, new byte[8]); return new GameEntry("Retro", "Game", file); }
    private static HttpResponseMessage ImageResponse(byte[] bytes) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<Uri, int, HttpResponseMessage> send; internal int Calls;
        internal Handler(Func<Uri, int, HttpResponseMessage> send) { this.send = send; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(send(request.RequestUri, Interlocked.Increment(ref Calls)));
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { await Task.Delay(10000, ct); return new HttpResponseMessage(HttpStatusCode.OK); }
    }
    private sealed class CountedStream : MemoryStream
    {
        internal int ReadCount;
        internal CountedStream(byte[] bytes) : base(bytes, false) { }
        public override int Read(byte[] buffer, int offset, int count) { int n = base.Read(buffer, offset, count); ReadCount += n; return n; }
        public override int ReadByte() { int n = base.ReadByte(); if (n >= 0) ReadCount++; return n; }
    }
    private static byte[] Png(bool jpeg = false, int width = 4, int height = 3)
    {
        using (var bitmap = new Bitmap(width, height)) using (var stream = new MemoryStream())
        { bitmap.SetPixel(0, 0, Color.Aquamarine); bitmap.Save(stream, jpeg ? ImageFormat.Jpeg : ImageFormat.Png); return stream.ToArray(); }
    }
    private static void Put(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, bytes); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Esperado " + expected + "; recibido " + actual); }
    private static void Check(bool value) { if (!value) throw new Exception("Comprobación de arte fallida."); }
    private static void Fail<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Falta excepción " + typeof(T).Name); }

    private static void Disc(string file, string format, bool wii)
    {
        int offset = format == "rvz" ? 0x58 : format == "wbfs" ? 512 : format == "ciso" ? 0x8000 : 0;
        var bytes = new byte[offset + 32];
        if (format == "rvz") Encoding.ASCII.GetBytes("RVZ\x01").CopyTo(bytes, 0);
        if (format == "wbfs") { Encoding.ASCII.GetBytes("WBFS").CopyTo(bytes, 0); bytes[8] = 9; bytes[9] = 21; bytes[12] = 1; }
        if (format == "ciso") { Encoding.ASCII.GetBytes("CISO").CopyTo(bytes, 0); BitConverter.GetBytes(2097152).CopyTo(bytes, 4); bytes[8] = 1; }
        Encoding.ASCII.GetBytes(wii ? "RMGE01" : "GMSE01").CopyTo(bytes, offset);
        new byte[] { 0x5d, 0x1c, 0x9e, 0xa3 }.CopyTo(bytes, offset + 24);
        if (!wii) { Array.Clear(bytes, offset + 24, 4); new byte[] { 0xc2, 0x33, 0x9f, 0x3d }.CopyTo(bytes, offset + 28); }
        Put(file, bytes);
    }
    private static void MiniIso(string file, string subfolder, Dictionary<string, byte[]> contents)
    {
        int sector = 22, total = sector + contents.Sum(p => (p.Value.Length + 2047) / 2048);
        var bytes = new byte[total * 2048]; int pvd = 16 * 2048;
        bytes[pvd] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(bytes, pvd + 1); bytes[pvd + 6] = 1;
        Both(bytes, pvd + 80, total); bytes[pvd + 129] = 8; bytes[pvd + 130] = 8;
        Record(bytes, pvd + 156, new byte[] { 0 }, 20, 2048, true);
        int dir = 20 * 2048;
        if (subfolder != null) { Record(bytes, dir, Encoding.ASCII.GetBytes(subfolder), 21, 2048, true); dir = 21 * 2048; }
        foreach (var pair in contents)
        {
            dir += Record(bytes, dir, Encoding.ASCII.GetBytes(pair.Key + ";1"), sector, pair.Value.Length, false);
            pair.Value.CopyTo(bytes, sector * 2048); sector += (pair.Value.Length + 2047) / 2048;
        }
        Put(file, bytes);
    }
    private static int Record(byte[] bytes, int at, byte[] name, int sector, int length, bool directory)
    {
        int size = 33 + name.Length; if (size % 2 != 0) size++;
        bytes[at] = (byte)size; Both(bytes, at + 2, sector); Both(bytes, at + 10, length); bytes[at + 25] = directory ? (byte)2 : (byte)0;
        bytes[at + 32] = (byte)name.Length; name.CopyTo(bytes, at + 33); return size;
    }
    private static void Both(byte[] bytes, int at, int value)
    { BitConverter.GetBytes(value).CopyTo(bytes, at); for (int i = 0; i < 4; i++) bytes[at + 4 + i] = (byte)(value >> ((3 - i) * 8)); }
}
