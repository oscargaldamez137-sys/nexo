using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Nexo.Core;

// Carpetas elegidas por el usuario y pantalla de Opciones (1.1.0). Sin red: las descargas usan manejadores simulados.
// Cada prueba trabaja en <workspace>\.nexo-test-<guid>\ con la instalación en Nexo\ y las carpetas externas al lado.
internal static class FoldersOptionsTests
{
    private static string workspace;

    internal static void RunAll(Action<string, Action> run, string root)
    {
        workspace = root;
        run("Carpetas: por defecto Juegos y Emuladores dentro de la instalación, sin crear nexo.json", () => With((install, external) =>
        {
            var svc = new NexoService(install);
            Equal(Path.Combine(install, "Juegos"), svc.GamesRoot); Equal(Path.Combine(install, "Emuladores"), svc.EmulatorsRoot);
            True(svc.GamesRootIsDefault && svc.EmulatorsRootIsDefault); Equal<string>(null, svc.FolderWarning);
            Equal(Path.Combine(install, "Juegos", "PS2"), svc.GetStatus("PS2").GamesFolder);
            Equal(Path.Combine(install, "Emuladores", "PS2"), svc.GetStatus("PS2").EmulatorFolder);
            True(!File.Exists(Path.Combine(install, "nexo.json")));
            foreach (NexoOption option in Enum.GetValues(typeof(NexoOption))) True(svc.GetOption(option));
        }));

        run("Carpetas: cambiar la de juegos crea las consolas, reescanea allí y no mueve ni borra nada", () => With((install, external) =>
        {
            var svc = new NexoService(install);
            string old = Put(Path.Combine(install, "Juegos", "PS2", "Viejo.iso"), "mi juego");
            string target = Path.Combine(external, "MisJuegos");
            var result = svc.SetGamesRoot(target);
            True(result.Changed && result.PreviousHasContent); Equal(Path.Combine(install, "Juegos"), result.PreviousFolder); Equal(target, result.Folder);
            True(result.Message.Contains("no los mueve ni los borra"));
            foreach (var system in svc.Systems) True(File.Exists(Path.Combine(target, system.Id, "LEEME.txt")));
            Equal("mi juego", File.ReadAllText(old));
            Equal(Path.Combine(target, "PS2"), svc.GetStatus("PS2").GamesFolder);
            Equal(0, Scan(svc, "PS2").Count);
            string moved = Put(Path.Combine(target, "PS2", "Nuevo.iso"));
            Equal(moved, Scan(svc, "PS2").Single().Path);
            True(!svc.GamesRootIsDefault && svc.EmulatorsRootIsDefault);
            // Persistencia: otra instancia (el siguiente arranque) usa la misma carpeta.
            var again = new NexoService(install);
            Equal(target, again.GamesRoot); Equal(moved, Scan(again, "PS2").Single().Path);
            var json = Json(install);
            Equal(target, (string)json["gamesRoot"]); True(!json.ContainsKey("emulatorsRoot"));
            // Abrir carpetas: la nueva y la anterior de esta sesión sí; otras no.
            True(svc.CanOpenFolder(Path.Combine(target, "PS2"))); True(svc.CanOpenFolder(Path.Combine(install, "Juegos", "PS2")));
            True(!svc.CanOpenFolder(Path.Combine(external, "otra"))); True(!again.CanOpenFolder(Path.Combine(external, "otra")));
        }));

        run("Carpetas: restablecer la predeterminada borra la clave y conserva la carpeta externa", () => With((install, external) =>
        {
            var svc = new NexoService(install);
            string target = Path.Combine(external, "J");
            svc.SetGamesRoot(target);
            string kept = Put(Path.Combine(target, "PSP", "Juego.iso"), "externo");
            var result = svc.SetGamesRoot(null);
            True(result.Changed && result.PreviousHasContent); True(svc.GamesRootIsDefault);
            Equal("externo", File.ReadAllText(kept));
            True(!Json(install).ContainsKey("gamesRoot"));
            True(!svc.SetGamesRoot(Path.Combine(install, "Juegos")).Changed); // elegir la predeterminada equivale a restablecer
        }));

        run("Carpetas: emuladores externos se detectan, se preparan y se lanzan desde su carpeta", () => With((install, external) =>
        {
            var started = new List<ProcessStartInfo>(); var prepared = new List<SystemStatus>();
            var svc = new NexoService(install, s => { prepared.Add(s); return new ControllerSetupResult(false, "ok"); }, i => { started.Add(i); return 7; },
                () => GpuDetector.Select(new GpuAdapter[0]), (s, g) => new GraphicsSetupResult(false, "ok", g.Tier));
            string target = Path.Combine(external, "Emus");
            svc.SetEmulatorsRoot(target);
            True(!svc.GetStatus("PS2").Installed);
            string exe = Put(Path.Combine(target, "PS2", "PCSX2", "pcsx2-qt.exe"));
            var status = svc.GetStatus("PS2");
            True(status.Installed); Equal(exe, status.ExecutablePath); Equal(Path.Combine(target, "PS2"), status.EmulatorFolder);
            string game = Put(Path.Combine(svc.GamesRoot, "PS2", "Juego.iso"));
            Equal(7, svc.LaunchGame(Scan(svc, "PS2").Single()));
            Equal(exe, started.Single().FileName); Equal(Path.Combine(target, "PS2"), prepared.Single().EmulatorFolder);
            True(started.Single().Arguments.Contains("-fullscreen"));
            // GBC/GBA comparten mGBA también en la carpeta externa.
            string mgba = Put(Path.Combine(target, "GBA", "Nexo-GBA", "mGBA.exe"));
            Equal(mgba, svc.GetStatus("GBC").ExecutablePath);
            Equal(target, new NexoService(install).EmulatorsRoot);
        }));

        run("Carpetas: validación rechaza raíz de unidad, rutas no locales, instalación, solapes y sin escritura", () => With((install, external) =>
        {
            var svc = new NexoService(install);
            string drive = Path.GetPathRoot(install);
            foreach (var bad in new[] { drive, "relativa", @"\\servidor\juegos", @"C:juegos", Path.Combine(install, "Otra"), Path.Combine(install, "Emuladores"),
                     Path.GetDirectoryName(install), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "NexoPrueba") })
                Throws<NexoException>(() => svc.SetGamesRoot(bad));
            True(!Directory.Exists(Path.Combine(install, "Otra")));
            True(!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "NexoPrueba")));
            // Juegos y emuladores no pueden coincidir ni estar una dentro de la otra.
            svc.SetEmulatorsRoot(Path.Combine(external, "Comun"));
            Throws<NexoException>(() => svc.SetGamesRoot(Path.Combine(external, "Comun")));
            Throws<NexoException>(() => svc.SetGamesRoot(Path.Combine(external, "Comun", "Juegos")));
            Throws<NexoException>(() => svc.SetGamesRoot(external));
            // Sin permiso de escritura (simulado): no se guarda nada.
            svc.CanWriteFolder = _ => false;
            Throws<NexoException>(() => svc.SetGamesRoot(Path.Combine(external, "SoloLectura")));
            True(svc.GamesRootIsDefault); True(!Json(install).ContainsKey("gamesRoot"));
        }));

        run("Carpetas: una junction en la ruta elegida se rechaza sin escribir en su destino", () => With((install, external) =>
        {
            var svc = new NexoService(install);
            string target = Path.Combine(external, "destino"); Directory.CreateDirectory(target);
            string junction = Path.Combine(external, "enlace");
            var info = new ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(info)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Equal(0, p.ExitCode); }
            try
            {
                Throws<NexoException>(() => svc.SetGamesRoot(junction));
                Throws<NexoException>(() => svc.SetEmulatorsRoot(Path.Combine(junction, "Emus")));
                Equal(0, Directory.GetFileSystemEntries(target).Length);
                True(svc.GamesRootIsDefault && svc.EmulatorsRootIsDefault);
            }
            finally { Directory.Delete(junction); }
        }));

        run("Carpetas: si la carpeta elegida no está disponible, Nexo abre igual, avisa y no crea nada en otro sitio", () => With((install, external) =>
        {
            string missing = FreeDrive() + @":\NexoNoExiste\Juegos";
            File.WriteAllText(Path.Combine(install, "nexo.json"), new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            { ["version"] = 1, ["systems"] = new Dictionary<string, object>(), ["gamesRoot"] = missing }));
            var svc = new NexoService(install);
            Equal(missing, svc.GamesRoot); True(svc.FolderWarning != null && svc.FolderWarning.Contains("no está disponible"));
            Equal(0, Scan(svc, "PS2").Count);
            True(!Directory.Exists(Path.Combine(install, "Juegos", "PS2")));
            // Restablecer desde Opciones vuelve a la carpeta predeterminada y quita el aviso.
            svc.SetGamesRoot(null); Equal<string>(null, svc.FolderWarning); True(Directory.Exists(Path.Combine(install, "Juegos", "PS2")));
        }));

        run("Configuración: nexo.json de 1.0.0 se lee igual y se conserva al guardar opciones y carpetas", () => With((install, external) =>
        {
            Put(Path.Combine(install, "Emuladores", "PS2", "pcsx2-qt.exe"));
            string v100 = "{\"version\":1,\"systems\":{\"PS2\":{\"executable\":\"Emuladores/PS2/pcsx2-qt.exe\",\"gameArguments\":[\"--mio\",\"{game}\"]}},\"downloadArtwork\":false}";
            File.WriteAllText(Path.Combine(install, "nexo.json"), v100);
            var svc = new NexoService(install);
            True(!svc.GetOption(NexoOption.DownloadArtwork)); True(svc.GetOption(NexoOption.Sounds)); True(svc.GamesRootIsDefault);
            svc.SetOption(NexoOption.Sounds, false);
            svc.SetGamesRoot(Path.Combine(external, "Juegos"));
            var json = Json(install);
            Equal(false, json["downloadArtwork"]); Equal(false, json["sounds"]);
            var ps2 = (Dictionary<string, object>)((Dictionary<string, object>)json["systems"])["PS2"];
            Equal("Emuladores/PS2/pcsx2-qt.exe", (string)ps2["executable"]);
            Equal("--mio|{game}", string.Join("|", ((object[])ps2["gameArguments"]).Cast<string>()));
            var again = new NexoService(install);
            True(!again.GetOption(NexoOption.Sounds) && !again.GetOption(NexoOption.DownloadArtwork) && again.GetOption(NexoOption.CheckUpdates));
            Equal(Path.Combine(install, "Emuladores", "PS2", "pcsx2-qt.exe"), again.GetStatus("PS2").ExecutablePath);
            // Con los valores de fábrica, nexo.json solo lleva las claves que entiende 1.0.0.
            again.SetOption(NexoOption.Sounds, true); again.SetGamesRoot(null); again.SetOption(NexoOption.DownloadArtwork, true);
            Equal("downloadArtwork,systems,version", string.Join(",", Json(install).Keys.OrderBy(k => k, StringComparer.Ordinal)));
        }));

        run("Configuración: tipos estrictos en las claves nuevas; un JSON inválido no se sobrescribe", () => With((install, external) =>
        {
            foreach (var bad in new[] { "{\"version\":1,\"systems\":{},\"sounds\":\"no\"}", "{\"version\":1,\"systems\":{},\"gamesRoot\":5}",
                     "{\"version\":1,\"systems\":{},\"gamesRoot\":\"relativa\"}", "{\"version\":1,\"systems\":{},\"otraCosa\":true}" })
            {
                File.WriteAllText(Path.Combine(install, "nexo.json"), bad);
                Throws<NexoException>(() => new NexoService(install));
                Equal(bad, File.ReadAllText(Path.Combine(install, "nexo.json")));
            }
        }));

        run("Opciones: cada opción se guarda y vuelve a leerse", () => With((install, external) =>
        {
            var svc = new NexoService(install);
            foreach (NexoOption option in Enum.GetValues(typeof(NexoOption)))
            {
                svc.SetOption(option, false); True(!new NexoService(install).GetOption(option));
                svc.SetOption(option, true); True(new NexoService(install).GetOption(option));
            }
        }));

        run("Opciones: con las portadas en línea desactivadas no se hace ninguna petición", () => With((install, external) =>
        {
            var handler = new CountingHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) });
            using (var downloader = new ArtworkDownloader(handler))
            {
                var svc = Service(install, artwork: new ArtworkService(downloader));
                svc.SetGamesRoot(Path.Combine(external, "Juegos"));
                svc.SetOption(NexoOption.DownloadArtwork, false);
                var first = Rom(svc, "Primero (USA).nes");
                Equal<string>(null, svc.EnsureArtAsync(first, CancellationToken.None).GetAwaiter().GetResult().CoverPath);
                Equal(0, handler.Calls);
                svc.SetOption(NexoOption.DownloadArtwork, true);
                var second = Rom(svc, "Segundo (USA).nes");
                string cover = svc.EnsureArtAsync(second, CancellationToken.None).GetAwaiter().GetResult().CoverPath;
                True(handler.Calls > 0);
                // La caché de portadas vive en la carpeta de juegos elegida.
                True(cover != null && cover.StartsWith(Path.Combine(external, "Juegos", "Retro", "covers") + "\\", StringComparison.OrdinalIgnoreCase));
            }
        }));

        run("Opciones: sin «buscar actualizaciones al abrir» no se consulta GitHub; «buscar ahora» sí", () => With((install, external) =>
        {
            int calls = 0;
            var svc = Service(install, update: (v, ct) => { calls++; return Task.FromResult<AppUpdate>(null); });
            svc.CheckForUpdateAsync(manual: false).GetAwaiter().GetResult(); Equal(1, calls);
            svc.SetOption(NexoOption.CheckUpdates, false);
            Equal<AppUpdate>(null, svc.CheckForUpdateAsync(manual: false).GetAwaiter().GetResult()); Equal(1, calls);
            svc.CheckForUpdateAsync(manual: true).GetAwaiter().GetResult(); Equal(2, calls);
        }));

        run("Opciones: pantalla completa desactivada quita solo esas opciones; las plantillas propias mandan", () => With((install, external) =>
        {
            var svc = Service(install);
            Put(Path.Combine(install, "Emuladores", "PS2", "pcsx2-qt.exe"));
            Put(Path.Combine(install, "Juegos", "PS2", "Juego.iso"));
            var game = Scan(svc, "PS2").Single();
            True(svc.PrepareGame(game).Arguments.Contains("-fullscreen"));
            svc.SetOption(NexoOption.FullscreenGames, false);
            string windowed = svc.PrepareGame(game).Arguments;
            True(!windowed.Contains("fullscreen") && windowed.Contains("\"--\"") && windowed.Contains("Juego.iso"));
            var path = @"C:\Juegos\x.iso";
            var fullscreenFlags = new[] { "-f", "-fullscreen", "--fullscreen", "-full-screen", "true", "-C", "Dolphin.Display.Fullscreen=True" };
            foreach (var pair in new Dictionary<string, string> { ["PS3"] = "rpcs3.exe", ["PSP"] = "PPSSPPWindows64.exe", ["GameCube-Wii"] = "Dolphin.exe",
                ["PS4"] = "shadPS4.exe", ["Switch"] = "eden.exe", ["Xbox"] = "xemu.exe", ["Xbox360"] = "xenia_canary.exe", ["GBA"] = "mGBA.exe" })
            {
                var windowedArgs = Launcher.GameArguments(pair.Key, pair.Value, new GameEntry(pair.Key, "x", path), null, fullscreen: false);
                var fullArgs = Launcher.GameArguments(pair.Key, pair.Value, new GameEntry(pair.Key, "x", path), null);
                True(!windowedArgs.Intersect(fullscreenFlags).Any() && windowedArgs.Contains(path));
                True(fullArgs.Intersect(fullscreenFlags).Any());
                // Solo desaparecen las opciones de pantalla completa: el resto de la plantilla se mantiene.
                True(windowedArgs.All(a => fullArgs.Contains(a)));
            }
            Equal("--play|" + path, string.Join("|", Launcher.GameArguments("PS5", @"C:\N\Nova5.exe", new GameEntry("PS5", "x", path), null, fullscreen: false)));
            Equal("--mia|" + path, string.Join("|", Launcher.GameArguments("PS2", "pcsx2-qt.exe", new GameEntry("PS2", "x", path), new[] { "--mia", "{game}" }, fullscreen: false)));
        }));

        run("Switch: claves y firmware de Eden también fuera del modo portable (%APPDATA%\\eden) y de Ryujinx", () => With((install, external) =>
        {
            var previous = SystemFiles.AppDataFolder;
            string appData = Path.Combine(external, "AppData");
            SystemFiles.AppDataFolder = () => appData;
            try
            {
                var svc = Service(install);
                string eden = Put(Path.Combine(install, "Emuladores", "Switch", "Eden", "eden.exe"));
                True(svc.GetStatus("Switch").SetupMessages.Any(m => m.Contains("prod.keys")));
                Put(Path.Combine(appData, "eden", "keys", "prod.keys"), "header_key = " + new string('1', 64));
                Put(Path.Combine(appData, "eden", "nand", "system", "Contents", "registered", new string('a', 32) + ".nca"));
                var messages = svc.GetStatus("Switch").SetupMessages;
                True(!messages.Any(m => m.StartsWith("Falta")));
                // Con carpeta portable «user», Eden solo usa esa: lo de %APPDATA% ya no cuenta.
                Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(eden), "user"));
                True(svc.GetStatus("Switch").SetupMessages.Any(m => m.Contains("prod.keys")));
                var ryujinx = SystemFiles.SwitchDataFolders(@"C:\R\Ryujinx.exe", appData);
                Equal(Path.Combine(appData, "Ryujinx", "system", "prod.keys"), ryujinx.Keys.Single());
                Equal(Path.Combine(appData, "Ryujinx", "bis", "system", "Contents", "registered"), ryujinx.Firmware.Single());
            }
            finally { SystemFiles.AppDataFolder = previous; }
        }));

        run("Switch: NSP válidos con acentos, varias NCA y ticket se listan como juegos, con claves o sin ellas", () => With((install, external) =>
        {
            var svc = Service(install);
            string folder = Path.Combine(svc.GamesRoot, "Switch");
            foreach (var name in new[] { "Pokémon Sword Base NSP.nsp", "Sub carpeta\\Pokémon Brilliant Diamond Base.nsp" })
                Nsp(Path.Combine(folder, name), new[] { new string('a', 32) + ".nca", new string('b', 32) + ".nca", new string('c', 32) + ".cnmt.nca", "0100000011d90000000000000000000b.tik", "0100000011d90000000000000000000b.cert" });
            var scan = svc.ScanGamesDetailedAsync("Switch").GetAwaiter().GetResult();
            Equal(2, scan.Games.Count); Equal(0, scan.Archives.Count);
            True(scan.Games.Any(g => g.Title.StartsWith("Pokémon Sword")) && scan.Games.Any(g => g.Title.StartsWith("Pokémon Brilliant")));
        }));

        run("Opciones: mandos y gráficos automáticos desactivados no tocan el emulador al lanzar", () => With((install, external) =>
        {
            int controllers = 0, graphics = 0;
            var svc = new NexoService(install, s => { controllers++; return new ControllerSetupResult(true, "mandos"); }, i => 9,
                () => GpuDetector.Select(new GpuAdapter[0]), (s, g) => { graphics++; return new GraphicsSetupResult(true, "gráficos", g.Tier); });
            Put(Path.Combine(install, "Emuladores", "PS2", "pcsx2-qt.exe"));
            Put(Path.Combine(install, "Juegos", "PS2", "Juego.iso"));
            var game = Scan(svc, "PS2").Single();
            svc.LaunchEmulator("PS2"); svc.LaunchGame(game); Equal(2, controllers); Equal(2, graphics);
            svc.SetOption(NexoOption.AutoControllers, false);
            svc.LaunchGame(game); Equal(2, controllers); Equal(3, graphics); True(!svc.LastControllerSetup.Changed && svc.LastControllerSetup.Message.Contains("desactivada"));
            svc.SetOption(NexoOption.AutoGraphics, false);
            svc.LaunchEmulator("PS2"); Equal(2, controllers); Equal(3, graphics); True(!svc.LastGraphicsSetup.Changed && svc.LastGraphicsSetup.Message.Contains("desactivados"));
        }));
    }

    private static NexoService Service(string install, ArtworkService artwork = null, Func<Version, CancellationToken, Task<AppUpdate>> update = null) =>
        new NexoService(install, s => new ControllerSetupResult(false, "prueba"), i => 1, () => GpuDetector.Select(new GpuAdapter[0]),
            (s, g) => new GraphicsSetupResult(false, "prueba", g.Tier), artwork, update);

    // PFS0 completo y pequeño: cabecera, tabla de entradas de 24 bytes, nombres y datos de 16 bytes por archivo.
    private static void Nsp(string path, string[] names)
    {
        var table = new MemoryStream(); var strings = new MemoryStream();
        using (var w = new BinaryWriter(table))
        {
            for (int i = 0; i < names.Length; i++)
            {
                w.Write((ulong)(i * 16)); w.Write(16UL); w.Write((uint)strings.Length); w.Write(0u);
                var bytes = System.Text.Encoding.UTF8.GetBytes(names[i] + "\0"); strings.Write(bytes, 0, bytes.Length);
            }
            while (strings.Length % 16 != 0) strings.WriteByte(0);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new BinaryWriter(File.Create(path)))
            {
                file.Write(System.Text.Encoding.ASCII.GetBytes("PFS0")); file.Write((uint)names.Length); file.Write((uint)strings.Length); file.Write(0u);
                file.Write(table.ToArray()); file.Write(strings.ToArray()); file.Write(new byte[16 * names.Length]);
            }
        }
    }

    private static GameEntry Rom(NexoService svc, string name)
    {
        Put(Path.Combine(svc.GamesRoot, "Retro", name), "rom");
        return new GameEntry("Retro", Path.GetFileNameWithoutExtension(name), Path.Combine(svc.GamesRoot, "Retro", name));
    }

    // Instalación en <base>\Nexo y carpetas externas en <base>\Externo. Se borra al terminar (solo dentro del área de pruebas).
    private static void With(Action<string, string> test)
    {
        var baseFolder = Path.Combine(workspace, ".nexo-test-" + Guid.NewGuid().ToString("N"));
        string install = Path.Combine(baseFolder, "Nexo"), external = Path.Combine(baseFolder, "Externo");
        Directory.CreateDirectory(install); Directory.CreateDirectory(external);
        try { test(install, external); }
        finally
        {
            var full = Path.GetFullPath(baseFolder);
            if (!SafePaths.Within(full, workspace) || !Path.GetFileName(full).StartsWith(".nexo-test-", StringComparison.Ordinal)) throw new Exception("Limpieza fuera del área de pruebas.");
            Directory.Delete(full, true);
        }
    }

    private static string FreeDrive()
    {
        var used = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
        for (char c = 'Z'; c >= 'G'; c--) if (!used.Contains(c)) return c.ToString();
        throw new Exception("No hay una letra de unidad libre para la prueba.");
    }

    private static Dictionary<string, object> Json(string install) =>
        (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(Path.Combine(install, "nexo.json")));
    private static IReadOnlyList<GameEntry> Scan(NexoService service, string id) => service.ScanGamesAsync(id, CancellationToken.None).GetAwaiter().GetResult();
    private static string Put(string path, string text = "fixture") { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); return path; }
    private static byte[] Png()
    {
        using (var bitmap = new Bitmap(4, 3)) using (var stream = new MemoryStream())
        { bitmap.SetPixel(0, 0, Color.Aquamarine); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray(); }
    }
    private static void True(bool value) { if (!value) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Esperado: " + expected + "; recibido: " + actual); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Se esperaba " + typeof(T).Name); }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> respond; internal int Calls;
        internal CountingHandler(Func<HttpResponseMessage> respond) { this.respond = respond; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); return Task.FromResult(respond()); }
    }
}
