using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Nexo.Core;

internal static class Program
{
    private static string workspace;
    private static int passed, failed;
    private static int Main(string[] args)
    {
        workspace = Path.GetFullPath(args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());
        Run("Doce sistemas, medios e IDs estables", () => WithFixture((root, svc) =>
        {
            Equal(12, svc.Systems.Count);
            Equal("Retro,PS2,PS3,PSP,GameCube-Wii,PS4,Switch,PS5,GBC,GBA,Xbox,Xbox360", string.Join(",", svc.Systems.Select(s => s.Id)));
            Equal(MediaKind.Cartridge, svc.Systems.Single(s => s.Id == "Retro").Media);
            Equal(MediaKind.GameCard, svc.Systems.Single(s => s.Id == "Switch").Media);
            True(svc.Systems.Single(s => s.Id == "PS5").Experimental);
            foreach (var s in svc.Systems)
            {
                var status = svc.GetStatus(s.Id);
                True(!status.Installed);
                True(File.Exists(Path.Combine(status.GamesFolder, "LEEME.txt")));
                True(File.Exists(Path.Combine(status.EmulatorFolder, "LEEME.txt")));
            }
        }));
        Run("Primer arranque conserva datos y LEEME existentes", () => WithFixture((root, svc) =>
        {
            var path = Path.Combine(root, "Juegos", "PS2", "LEEME.txt"); File.WriteAllText(path, "propio");
            new NexoService(root); Equal("propio", File.ReadAllText(path));
        }));
        Run("Instrucciones públicas coinciden con los formatos de cada sistema", () => WithFixture((root, svc) =>
        {
            foreach (var system in svc.Systems)
            {
                True(!string.IsNullOrWhiteSpace(system.GamesHint));
                Equal(Catalog.Get(system.Id).Instructions, system.GamesHint);
                True(File.ReadAllText(Path.Combine(svc.GetStatus(system.Id).GamesFolder, "LEEME.txt")).Contains(system.GamesHint));
            }
        }));
        Run("IDs inválidos rechazados", () => WithFixture((root, svc) =>
        {
            foreach (var id in new[] { "", "../PS2", "ps2", "Unknown", null }) Throws<NexoException>(() => svc.GetStatus(id));
        }));
        Run("Ruta absoluta, extensión y existencia", () => WithFixture((root, svc) =>
        {
            foreach (var path in new[] { "relative.exe", @"C:relative.exe", @"\root.exe", @"\\server\share\x.exe", FileAt(root, "bad.txt"), Path.Combine(root, "missing.exe") })
                Throws<NexoException>(() => svc.SetExecutable("PS2", path));
        }));
        Run("Detección recursiva y prioridad manual persistente", () => WithFixture((root, svc) =>
        {
            var detected = FileAt(root, "Emuladores/PS2/portable/PCSX2-QT.EXE");
            Equal(detected, svc.GetStatus("PS2").ExecutablePath);
            var manual = FileAt(root, "otro/emulador.exe"); svc.SetExecutable("PS2", manual);
            Equal(manual, new NexoService(root).GetStatus("PS2").ExecutablePath);
            svc.ClearExecutable("PS2"); Equal(detected, svc.GetStatus("PS2").ExecutablePath);
        }));
        Run("Ejecutable desaparecido deja de estar instalado", () => WithFixture((root, svc) =>
        {
            var exe = FileAt(root, "manual.exe"); svc.SetExecutable("PS2", exe); File.Delete(exe);
            True(!svc.GetStatus("PS2").Installed);
        }));
        Run("Detección acotada en profundidad", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Emuladores/PS2/1/2/3/4/5/pcsx2-qt.exe"); True(!svc.GetStatus("PS2").Installed);
            FileAt(root, "Emuladores/PS2/1/2/3/4/pcsx2-qt.exe"); True(svc.GetStatus("PS2").Installed);
        }));
        Run("JSON corrupto se conserva en arranque y escritura", () => WithFixture((root, svc) =>
        {
            var json = FileAt(root, "nexo.json", "{roto");
            Throws<NexoException>(() => new NexoService(root));
            Throws<NexoException>(() => svc.SetExecutable("PS2", FileAt(root, "fake.exe")));
            Equal("{roto", File.ReadAllText(json));
        }));
        Run("Configuración estricta rechaza versión, ID y tipos inválidos", () => WithFixture((root, svc) =>
        {
            foreach (var json in new[] { "{}", "{\"version\":2,\"systems\":{}}", "{\"version\":1,\"systems\":{\"X\":{}}}",
                "{\"version\":1,\"systems\":{\"PS2\":{\"gameArguments\":[42]}}}" })
            { FileAt(root, "nexo.json", json); Throws<NexoException>(() => new NexoService(root)); }
        }));
        Run("Escritura atómica conserva configuración ante error", () => WithFixture((root, svc) =>
        {
            var a = FileAt(root, "a.exe"); var b = FileAt(root, "b.exe"); svc.SetExecutable("PS2", a);
            var config = Path.Combine(root, "nexo.json"); var before = File.ReadAllText(config);
            using (File.Open(config, FileMode.Open, FileAccess.Read, FileShare.Read)) Throws<NexoException>(() => svc.SetExecutable("PS2", b));
            Equal(before, File.ReadAllText(config)); Equal(0, Directory.GetFiles(root, "*.tmp").Length);
        }));
        Run("PS2, PSP, Dolphin y Switch: extensiones, títulos y portadas", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/PS2/Gran_Turismo (USA) [!].iso");
            var cover = FileAt(root, "Juegos/PS2/Gran_Turismo (USA) [!].png");
            FileAt(root, "Juegos/PS2/no.txt");
            var game = Scan(svc, "PS2").Single(); Equal("Gran Turismo", game.Title); Equal(cover, game.CoverPath);
            FileAt(root, "Juegos/PSP/Test.CSO"); Equal(1, Scan(svc, "PSP").Count);
            FileAt(root, "Juegos/GameCube-Wii/Test.rvz"); Equal(1, Scan(svc, "GameCube-Wii").Count);
            SwitchContainerTests.MinimalXci(FileAt(root, "Juegos/Switch/Test.xci")); Equal(1, Scan(svc, "Switch").Count);
        }));
        Run("Portadas en covers", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/PSP/Game.iso"); var cover = FileAt(root, "Juegos/PSP/covers/Game.jpg");
            Equal(cover, Scan(svc, "PSP").Single().CoverPath);
        }));
        Run("Retro solo muestra ROMs con núcleo compatible", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/Retro/Game.nes"); Equal(0, Scan(svc, "Retro").Count);
            FileAt(root, "Emuladores/Retro/retroarch.exe"); Equal(0, Scan(svc, "Retro").Count);
            var core = FileAt(root, "Emuladores/Retro/cores/mesen_libretro.dll");
            var game = Scan(svc, "Retro").Single();
            var info = svc.PrepareGame(game); True(info.Arguments.Contains(Launcher.Quote(core)));
            File.Delete(core); Throws<NexoException>(() => svc.PrepareGame(game));
        }));
        Run("Diagnóstico Retro distingue ROMs sin núcleo de portadas y otros archivos", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/Retro/NES/Game.NES");
            FileAt(root, "Juegos/Retro/Advance.gba");
            FileAt(root, "Juegos/Retro/NES/Game.png");
            FileAt(root, "Juegos/Retro/unknown.zip");
            Directory.CreateDirectory(Path.Combine(root, "Juegos", "Retro", "folder.nes"));
            var result = svc.ScanGamesDetailedAsync("Retro").GetAwaiter().GetResult();
            Equal(0, result.Games.Count); Equal(2, result.IgnoredCount);
            True(result.Reason.Contains("núcleo"));
            FileAt(root, "Emuladores/Retro/retroarch.exe");
            FileAt(root, "Emuladores/Retro/cores/mesen_libretro.dll");
            result = svc.ScanGamesDetailedAsync("Retro").GetAwaiter().GetResult();
            Equal(1, result.Games.Count); Equal(1, result.IgnoredCount);
            Equal(Scan(svc, "Retro").Single().Path, result.Games.Single().Path);
            FileAt(root, "Emuladores/Retro/cores/mgba_libretro.dll");
            result = svc.ScanGamesDetailedAsync("Retro").GetAwaiter().GetResult();
            Equal(2, result.Games.Count); Equal(0, result.IgnoredCount); Equal<string>(null, result.Reason);
        }));
        Run("Biblioteca vacía y otros sistemas no señalan núcleos ausentes", () => WithFixture((root, svc) =>
        {
            var empty = svc.ScanGamesDetailedAsync("Retro").GetAwaiter().GetResult();
            Equal(0, empty.Games.Count); Equal(0, empty.IgnoredCount); Equal<string>(null, empty.Reason);
            FileAt(root, "Juegos/PS2/Game.iso"); FileAt(root, "Juegos/PS2/Unknown.nes");
            var result = svc.ScanGamesDetailedAsync("PS2").GetAwaiter().GetResult();
            Equal(1, result.Games.Count); Equal(0, result.IgnoredCount); Equal<string>(null, result.Reason);
        }));
        Run("Escaneo detallado valida el ID y respeta cancelación", () => WithFixture((root, svc) =>
        {
            Throws<NexoException>(() => svc.ScanGamesDetailedAsync("../Retro").GetAwaiter().GetResult());
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Throws<OperationCanceledException>(() => svc.ScanGamesDetailedAsync("Retro", cts.Token).GetAwaiter().GetResult());
            }
        }));
        Run("PS3 lee PARAM.SFO, icono y ruta EBOOT", () => WithFixture((root, svc) =>
        {
            var exe = FileAt(root, "Juegos/PS3/Folder/PS3_GAME/USRDIR/EBOOT.BIN");
            WriteSfo(FileAt(root, "Juegos/PS3/Folder/PS3_GAME/PARAM.SFO"), "Título de PS3");
            var cover = FileAt(root, "Juegos/PS3/Folder/PS3_GAME/ICON0.PNG");
            FileAt(root, "Juegos/PS3/NoDirectISO.iso");
            var game = Scan(svc, "PS3").Single(); Equal("Título de PS3", game.Title); Equal(exe, game.Path); Equal(cover, game.CoverPath);
        }));
        Run("PS3 admite USRDIR directamente y SFO dañado", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/PS3/Fallback/USRDIR/EBOOT.BIN"); FileAt(root, "Juegos/PS3/Fallback/PARAM.SFO", "malformed");
            Equal("Fallback", Scan(svc, "PS3").Single().Title);
        }));
        Run("SFO con offsets fuera de rango no falla", () => WithFixture((root, svc) =>
        {
            var file = FileAt(root, "bad.sfo"); WriteSfo(file, "Demo");
            var bytes = File.ReadAllBytes(file); Array.Copy(BitConverter.GetBytes(uint.MaxValue), 0, bytes, 12, 4); File.WriteAllBytes(file, bytes);
            Equal<string>(null, Metadata.ReadSfoTitle(file));
        }));
        Run("PS4 y PS5 carpetas, JSON localizado e icono", () => WithFixture((root, svc) =>
        {
            foreach (var id in new[] { "PS4", "PS5" })
            {
                FileAt(root, "Juegos/" + id + "/Game/eboot.bin");
                FileAt(root, "Juegos/" + id + "/Game/sce_sys/param.json", "{\"localizedParameters\":{\"defaultLanguage\":\"es-ES\",\"es-ES\":{\"titleName\":\"Título localizado\"}}}");
                var cover = FileAt(root, "Juegos/" + id + "/Game/sce_sys/icon0.png");
                var game = Scan(svc, id).Single(); Equal("Título localizado", game.Title); Equal(cover, game.CoverPath);
            }
        }));
        Run("PS4 exige metadatos, PS5 exige param.json", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/PS4/Game/eboot.bin"); Equal(0, Scan(svc, "PS4").Count);
            WriteSfo(FileAt(root, "Juegos/PS4/Game/sce_sys/param.sfo"), "PS4 SFO"); Equal("PS4 SFO", Scan(svc, "PS4").Single().Title);
            FileAt(root, "Juegos/PS5/Game/eboot.bin"); FileAt(root, "Juegos/PS5/Game/sce_sys/param.sfo"); Equal(0, Scan(svc, "PS5").Count);
        }));
        Run("Cancelación del escaneo", () => WithFixture((root, svc) =>
        {
            using (var cts = new CancellationTokenSource()) { cts.Cancel(); Throws<OperationCanceledException>(() => svc.ScanGamesAsync("PS2", cts.Token).GetAwaiter().GetResult()); }
        }));
        Run("Límites de profundidad y número de entradas", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Juegos/PS2/deep/one/two/Game.iso"); var folder = svc.GetStatus("PS2").GamesFolder;
            Equal(0, GameScanner.Scan(Catalog.Get("PS2"), folder, null, CancellationToken.None, 0, 100).Count);
            Equal(0, FileTree.Files(folder, 10, 0, TimeSpan.FromSeconds(1), CancellationToken.None).Count());
        }));
        Run("Argumentos citados sobreviven CommandLineToArgvW", () =>
        {
            foreach (var value in new[] { "", "normal", "espacios y ñ", @"C:\con espacio\", "a\"b", "a\\\"b", "& calc | < > %PATH%", @"C:\foo\\" })
            {
                var argv = ParseWindowsCommandLine("program.exe " + Launcher.Quote(value)); Equal(2, argv.Length); Equal(value, argv[1]);
            }
        });
        Run("Plantillas por emulador, sin shell", () => WithFixture((root, svc) =>
        {
            FileAt(root, "Emuladores/PS2/pcsx2-qt.exe"); FileAt(root, "Juegos/PS2/Game & spaces.iso");
            var game = Scan(svc, "PS2").Single(); var info = svc.PrepareGame(game);
            True(!info.UseShellExecute); Equal(svc.GetStatus("PS2").EmulatorFolder, info.WorkingDirectory);
            Equal("-fullscreen|--|" + game.Path, string.Join("|", ParseWindowsCommandLine("program.exe " + info.Arguments).Skip(1)));
            var entry = new GameEntry("Switch", "Game", "C:\\Game.xci");
            Equal(1, Launcher.GameArguments("Switch", "C:\\Ryujinx.exe", entry, null).Length);
            Equal("-f", Launcher.GameArguments("Switch", "C:\\eden.exe", entry, null)[0]);
            Equal("-b|-C|Dolphin.Display.Fullscreen=True|-e|" + entry.Path, string.Join("|", Launcher.GameArguments("GameCube-Wii", "C:\\Dolphin.exe", entry, null)));
            Equal("--fullscreen", Launcher.GameArguments("PS5", "C:\\kyty_emulator.exe", entry, null)[0]);
            Equal("--play|" + entry.Path, string.Join("|", Launcher.GameArguments("PS5", "C:\\Nova5.exe", entry, null)));
        }));
        Run("shadPS4 SDL abre Big Picture y recibe EBOOT al jugar", () => WithFixture((root, svc) =>
        {
            Equal("-b", string.Join("|", Launcher.EmulatorArguments("PS4")));
            foreach (var id in new[] { "PS2", "PS3", "PSP", "GameCube-Wii", "Retro" })
                Equal(0, Launcher.EmulatorArguments(id).Length);
            FileAt(root, "Emuladores/PS4/shadPS4/shadPS4.exe");
            var eboot = FileAt(root, "Juegos/PS4/Game with spaces/eboot.bin");
            FileAt(root, "Juegos/PS4/Game with spaces/sce_sys/param.json", "{}");
            var info = svc.PrepareGame(Scan(svc, "PS4").Single());
            Equal("--fullscreen|true|-g|" + eboot, string.Join("|", ParseWindowsCommandLine("program.exe " + info.Arguments).Skip(1)));
        }));
        Run("Dolphin batch se puede desactivar; RPCS3 y PPSSPP conservan ruta posicional", () =>
        {
            var entry = new GameEntry("GameCube-Wii", "Demo", @"C:\Juegos\Game with spaces.rvz");
            Equal("-e|" + entry.Path, string.Join("|", Launcher.GameArguments("GameCube-Wii", @"C:\Dolphin.exe", entry, new[] { "-e", "{game}" })));
            Equal("--no-gui|--fullscreen|" + entry.Path, string.Join("|", Launcher.GameArguments("PS3", @"C:\rpcs3.exe", entry, null)));
            Equal("--fullscreen|" + entry.Path, string.Join("|", Launcher.GameArguments("PSP", @"C:\PPSSPPWindows64.exe", entry, null)));
        });
        Run("Detección reproduce las seis estructuras instaladas", () => WithFixture((root, svc) =>
        {
            var folders = new Dictionary<string, string>
            {
                ["PS2"] = "PCSX2/pcsx2-qt.exe", ["PS3"] = "RPCS3/rpcs3.exe",
                ["PSP"] = "PPSSPP/PPSSPPWindows64.exe", ["GameCube-Wii"] = "Dolphin/Dolphin-x64/Dolphin.exe",
                ["PS4"] = "shadPS4/shadPS4.exe", ["Retro"] = "RetroArch/RetroArch-Win64/retroarch.exe"
            };
            foreach (var pair in folders)
            {
                var expected = FileAt(root, "Emuladores/" + pair.Key + "/" + pair.Value);
                Equal(expected, svc.GetStatus(pair.Key).ExecutablePath);
            }
        }));
        Run("Pantalla completa: argumentos sobreviven Windows con rutas Unicode, espacios y ampersand", () => WithFixture((root, svc) =>
        {
            var cases = new[] {
                new[] { "PS2", "pcsx2-qt.exe", "-fullscreen|--" },
                new[] { "PS3", "rpcs3.exe", "--no-gui|--fullscreen" },
                new[] { "PSP", "PPSSPPWindows64.exe", "--fullscreen" },
                new[] { "GameCube-Wii", "Dolphin.exe", "-b|-C|Dolphin.Display.Fullscreen=True|-e" },
                new[] { "PS4", "shadPS4.exe", "--fullscreen|true|-g" },
                new[] { "PS5", "kyty_emulator.exe", "--fullscreen|--game" },
                new[] { "Switch", "EDEN.EXE", "-f|-g" }
            };
            foreach (var item in cases)
            {
                string exe = FileAt(root, "Emuladores/" + item[0] + "/Paquete con espacios/" + item[1]);
                string path = FileAt(root, "Juegos/" + item[0] + "/Pokémon & amigos/juego.bin");
                var game = new GameEntry(item[0], "Prueba", path);
                var info = Launcher.StartInfo(exe, Launcher.GameArguments(item[0], exe, game, null));
                Equal(item[2] + "|" + path, string.Join("|", ParseWindowsCommandLine("program.exe " + info.Arguments).Skip(1)));
                True(!info.UseShellExecute); Equal(Path.GetDirectoryName(exe), info.WorkingDirectory);
            }
        }));
        Run("Pantalla completa Retro: conserva selección del núcleo y GUI normal al abrir emuladores", () => WithFixture((root, svc) =>
        {
            string exe = FileAt(root, "Emuladores/Retro/RetroArch/retroarch.exe");
            string core = FileAt(root, "Emuladores/Retro/RetroArch/cores/mesen_libretro.dll");
            string game = FileAt(root, "Juegos/Retro/Juego & español.nes");
            var info = Launcher.StartInfo(exe, Launcher.GameArguments("Retro", exe, new GameEntry("Retro", "Juego", game), null));
            Equal("-f|-L|" + core + "|" + game, string.Join("|", ParseWindowsCommandLine("program.exe " + info.Arguments).Skip(1)));
            foreach (var system in svc.Systems)
                Equal(system.Id == "PS4" ? "-b" : "", string.Join("|", Launcher.EmulatorArguments(system.Id)));
        }));
        Run("Pantalla completa: plantillas personalizadas no reciben opciones adicionales", () => WithFixture((root, svc) =>
        {
            string exe = FileAt(root, "Emuladores/Retro/retroarch.exe"); FileAt(root, "Emuladores/Retro/cores/mesen_libretro.dll");
            string path = FileAt(root, "Juegos/Retro/Test.nes");
            foreach (var system in svc.Systems)
            {
                var args = Launcher.GameArguments(system.Id, exe, new GameEntry(system.Id, "Test", path), new[] { "--mi-opcion", "{game}" });
                Equal("--mi-opcion|" + path, string.Join("|", args));
            }
        }));
        Run("Pantalla completa Switch: no presupone flags de forks sin versión verificada", () =>
        {
            var game = new GameEntry("Switch", "Test", @"C:\Juegos\Test.nsp");
            foreach (string exe in new[] { "citron.exe", "yuzu.exe" })
                Equal("-g|" + game.Path, string.Join("|", Launcher.GameArguments("Switch", exe, game, null)));
            Equal(game.Path, Launcher.GameArguments("Switch", "Ryujinx.exe", game, null).Single());
        });
        Run("Argumentos personalizados conservados al cambiar exe", () => WithFixture((root, svc) =>
        {
            FileAt(root, "nexo.json", "{\"version\":1,\"systems\":{\"PS2\":{\"gameArguments\":[\"--custom\",\"{game}\"]}}}");
            svc.SetExecutable("PS2", FileAt(root, "custom.exe")); FileAt(root, "Juegos/PS2/Game.iso");
            True(svc.PrepareGame(Scan(svc, "PS2").Single()).Arguments.StartsWith("\"--custom\""));
        }));
        Run("No se ejecutan rutas externas, ausentes o de otro sistema", () => WithFixture((root, svc) =>
        {
            svc.SetExecutable("PS2", FileAt(root, "custom.exe"));
            Throws<NexoException>(() => svc.PrepareGame(new GameEntry("PS2", "x", FileAt(root, "outside.iso"))));
            Throws<NexoException>(() => svc.PrepareGame(new GameEntry("PS2", "x", FileAt(root, "Juegos/PSP/x.iso"))));
            Throws<NexoException>(() => svc.PrepareGame(new GameEntry("PS2", "x", Path.Combine(root, "Juegos/PS2/missing.iso"))));
        }));
        Run("Errores de lanzamiento en español con exe falso", () => WithFixture((root, svc) =>
        {
            Throws<NexoException>(() => svc.LaunchEmulator("PS2"));
            svc.SetExecutable("PS2", FileAt(root, "fake.exe", "NOT A REAL EXECUTABLE"));
            Throws<NexoException>(() => svc.LaunchEmulator("PS2"));
        }));
        Run("OpenFolder rechaza salir de la instalación", () => WithFixture((root, svc) =>
        {
            Throws<NexoException>(() => svc.OpenFolder(Path.GetDirectoryName(root)));
            Throws<NexoException>(() => svc.OpenFolder(root + "-neighbor"));
        }));
        Run("No se inventan webs para Switch; Nova5 tiene la suya", () => WithFixture((root, svc) =>
        {
            Throws<NexoException>(() => svc.OpenWebsite("Switch"));
            Equal("https://nova5emu.github.io/nova5/", svc.Systems.Single(s => s.Id == "PS5").Website);
            var nova5 = OfficialPackages.For("PS5").Single();
            True(nova5.Latest); Equal("Nova5-Portable.zip", nova5.Name);
        }));
        Run("Mando: pulsación, repetición 380/140 y liberación", () =>
        {
            PadSnapshot? snapshot = new PadSnapshot(8); var pad = new XInputGamepad(() => snapshot);
            Equal(PadButton.Right, pad.Poll(0).Single()); True(pad.Connected);
            Equal(0, pad.Poll(379).Count); Equal(PadButton.Right, pad.Poll(380).Single());
            Equal(0, pad.Poll(519).Count); Equal(PadButton.Right, pad.Poll(520).Single());
            snapshot = new PadSnapshot(0); Equal(0, pad.Poll(530).Count);
            snapshot = new PadSnapshot(8); Equal(PadButton.Right, pad.Poll(531).Single());
        });
        Run("Mando: A no se repite, reconexión limpia y zona muerta", () =>
        {
            PadSnapshot? snapshot = new PadSnapshot(0x1000, 8000, -8000); var pad = new XInputGamepad(() => snapshot);
            Equal(PadButton.A, pad.Poll(0).Single()); Equal(0, pad.Poll(1000).Count);
            snapshot = null; Equal(0, pad.Poll(1100).Count); True(!pad.Connected);
            snapshot = new PadSnapshot(0x1000, 8001, -8001); var actions = pad.Poll(1200);
            True(actions.Contains(PadButton.A) && actions.Contains(PadButton.Right) && actions.Contains(PadButton.Down));
        });
        Run("Mando: cambio de mando y reloj no dejan botones atascados", () =>
        {
            PadSnapshot? snapshot = new PadSnapshot(0x2000); var pad = new XInputGamepad(() => snapshot);
            Equal(PadButton.B, pad.Poll(100).Single());
            snapshot = new PadSnapshot(0x2000, 0, 0, 1); Equal(PadButton.B, pad.Poll(101).Single());
            Equal(PadButton.B, pad.Poll(1).Single());
        });
        ArchiveTests.RunAll(Run, workspace);
        Ps3IsoTests.RunAll(Run, workspace);
        ControllerTests.RunAll(Run, workspace);
        GraphicsTests.RunAll(Run, workspace);
        CompatibilityTests.RunAll(Run, workspace);
        SwitchContainerTests.RunAll(Run, workspace);
        ArtworkTests.RunAll(Run, workspace);
        SetupTests.RunAll(Run, workspace);
        Ps4PkgTests.RunAll(Run, workspace);
        Ps4CompatibilityTests.RunAll(Run, workspace);
        NewSystemsTests.RunAll(Run, workspace);
        UpdateTests.RunAll(Run, workspace);
        Console.WriteLine("Resultado: " + passed + " correctas, " + failed + " fallidas.");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    { try { test(); passed++; Console.WriteLine("OK  " + name); } catch (Exception ex) { failed++; Console.WriteLine("ERROR " + name + "\n" + ex); } }
    private static void True(bool value) { if (!value) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Esperado: " + expected + "; recibido: " + actual); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Se esperaba " + typeof(T).Name); }
    private static void WithFixture(Action<string, NexoService> test)
    {
        var root = Path.Combine(workspace, ".nexo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root, new NexoService(root)); }
        finally
        {
            var full = Path.GetFullPath(root);
            if (!SafePaths.Within(full, workspace) || !Path.GetFileName(full).StartsWith(".nexo-test-", StringComparison.Ordinal)) throw new Exception("Limpieza fuera del área de pruebas.");
            Directory.Delete(full, true);
        }
    }
    private static string FileAt(string root, string relative, string text = "fixture")
    { var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); return path; }
    private static IReadOnlyList<GameEntry> Scan(NexoService service, string id) => service.ScanGamesAsync(id, CancellationToken.None).GetAwaiter().GetResult();
    private static void WriteSfo(string path, string title)
    {
        var text = Encoding.UTF8.GetBytes(title + "\0");
        using (var stream = File.Create(path)) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x46535000u); writer.Write(0x00000101u); writer.Write(36u); writer.Write(42u); writer.Write(1u);
            writer.Write((ushort)0); writer.Write((ushort)0x0204); writer.Write((uint)text.Length); writer.Write((uint)text.Length); writer.Write(0u);
            writer.Write(Encoding.ASCII.GetBytes("TITLE\0")); writer.Write(text);
        }
    }
    [DllImport("shell32.dll", SetLastError = true)] private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int argc);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static string[] ParseWindowsCommandLine(string command)
    {
        var memory = CommandLineToArgvW(command, out var count);
        if (memory == IntPtr.Zero) throw new Exception("CommandLineToArgvW falló.");
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))).ToArray(); }
        finally { LocalFree(memory); }
    }
}
