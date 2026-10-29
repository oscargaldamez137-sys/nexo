using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexo.Core;

internal static class Ps3IsoTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-ps3iso-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-ps3iso-")) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        run("ISO PS3: nombre ISO9660 sin extensión ('initfs_Ps3.') se normaliza y valida", () =>
        {
            // Caso real: Plants vs. Zombies Garden Warfare (Europe) trae PS3_GAME/USRDIR/Data/initfs_Ps3.
            string fixedName = ArchiveExtractor.NormalizeIsoName("PS3_GAME/USRDIR/Data/initfs_Ps3.");
            if (fixedName != "PS3_GAME/USRDIR/Data/initfs_Ps3") throw new Exception("No se quitó el punto final: " + fixedName);
            ArchiveExtractor.ValidateName(fixedName);
            if (ArchiveExtractor.NormalizeIsoName("PS3_GAME/USRDIR/EBOOT.BIN") != "PS3_GAME/USRDIR/EBOOT.BIN") throw new Exception("Cambió un nombre con extensión.");
            if (ArchiveExtractor.NormalizeIsoName("a/../b.") != "a/../b") throw new Exception("La normalización no debe ocultar '..'.");
            bool rejected = false;
            try { ArchiveExtractor.ValidateName(ArchiveExtractor.NormalizeIsoName("a/../b.")); } catch (NexoException) { rejected = true; }
            if (!rejected) throw new Exception("'..' debe seguir rechazándose tras normalizar.");
        });
        Test("ISO PS3: bsdtar real, pendiente, extracción segura y Papelera posterior", (root, service) =>
        {
            string iso = MakeIso(root); var scan = service.ScanGamesDetailedAsync("PS3").GetAwaiter().GetResult();
            Check(scan.Games.Count == 0 && scan.Archives.Count == 1);
            var archive = scan.Archives.Single(); Check(archive.State == ArchiveState.Ready && archive.Title == "Blur");
            Check(archive.ExpectedSize <= archive.Size && archive.ExpectedSize > 0);
            string games = service.GetStatus("PS3").GamesFolder; bool recycled = false; long progress = -1;
            string output = ArchiveExtractor.ExtractAsync(archive, games, new Progress(n => { Check(n >= progress); progress = n; }), CancellationToken.None, path =>
            {
                Check(File.Exists(Path.Combine(games, Path.GetFileNameWithoutExtension(iso), "PS3_GAME", "USRDIR", "EBOOT.BIN")));
                Check(Directory.GetDirectories(games, ".nexo-extract-*").Length == 0);
                File.Move(path, Path.Combine(root, "papelera.iso")); recycled = true;
            }).GetAwaiter().GetResult();
            Check(recycled && !File.Exists(iso) && progress > 0);
            Check(File.ReadAllBytes(Path.Combine(output, "PS3_GAME", "USRDIR", "EBOOT.BIN")).SequenceEqual(new byte[] { 83, 67, 69, 0, 1, 2, 3, 4 }));
            Check(File.ReadAllText(Path.Combine(output, "PS3_GAME", "otra carpeta", "datos.txt")) == "datos de prueba");
            scan = service.ScanGamesDetailedAsync("PS3").GetAwaiter().GetResult();
            Check(scan.Archives.Count == 0 && scan.Games.Single().Title == "Blur" && scan.Games.Single().CoverPath.EndsWith("ICON0.PNG"));
        });
        Test("ISO PS3: EBOOT cifrado simulado no se extrae ni se recicla", (root, service) =>
        {
            string iso = MakeIso(root, encrypted: true); var archive = ArchiveInspector.Inspect("PS3", iso);
            Check(archive.State == ArchiveState.Unreadable && archive.Message == "ISO cifrada: RPCS3 necesita una ISO descifrada (o la clave del disco)");
            bool recycled = false;
            var forged = new ArchiveEntry("PS3", iso, "falsa", 0, null, ArchiveState.Ready, "");
            Fail<NexoException>(() => ArchiveExtractor.ExtractAsync(forged, service.GetStatus("PS3").GamesFolder, null, CancellationToken.None, p => recycled = true).GetAwaiter().GetResult());
            Check(!recycled && File.Exists(iso)); NoStage(service);
        });
        Test("ISO PS3: truncada usa tamaño PVD, admite cancelación y no acepta firmas ajenas", (root, service) =>
        {
            string iso = MakeIso(root); var expected = ArchiveInspector.Inspect("PS3", iso).ExpectedSize.Value;
            using (var file = File.OpenWrite(iso)) file.SetLength(expected - 2048);
            var result = ArchiveInspector.Inspect("PS3", iso); Check(result.State == ArchiveState.Incomplete && result.ExpectedSize == expected);
            using (var file = File.OpenWrite(iso)) file.SetLength(0x8002);
            Check(ArchiveInspector.Inspect("PS3", iso).State == ArchiveState.Incomplete);
            Fail<OperationCanceledException>(() => ArchiveInspector.Inspect("PS3", iso, new CancellationToken(true)));
            using (var file = File.OpenWrite(iso)) { file.Position = 0x800; file.WriteByte(0); }
            Check(ArchiveInspector.Inspect("PS3", iso).State == ArchiveState.Unreadable);
        });
        Test("ISO PS3: metadatos ambiguos, EBOOT ausente y extent fuera de volumen se rechazan", (root, service) =>
        {
            string iso = MakeIso(root); byte[] original = File.ReadAllBytes(iso);
            foreach (int offset in new[] { 0x8001, 0x8000 + 84, 0x8000 + 156 + 6 })
            {
                var bytes = (byte[])original.Clone(); bytes[offset] ^= 1; File.WriteAllBytes(iso, bytes);
                Check(ArchiveInspector.Inspect("PS3", iso).State == ArchiveState.Unreadable);
            }
            var invalid = (byte[])original.Clone();
            // Equal LE/BE but deliberately outside the announced volume.
            for (int i = 0; i < 8; i++) invalid[0x8000 + 156 + 2 + i] = 255;
            File.WriteAllBytes(iso, invalid); Check(ArchiveInspector.Inspect("PS3", iso).State == ArchiveState.Unreadable);
            iso = MakeIso(root, missing: true); Check(ArchiveInspector.Inspect("PS3", iso).Message.Contains("No se encuentra"));
        });
        Test("ISO PS3: cancelación limpia temporal; colisión usa (2); sin espacio conserva ISO", (root, service) =>
        {
            string iso = MakeIso(root), games = service.GetStatus("PS3").GamesFolder;
            var archive = ArchiveInspector.Inspect("PS3", iso); bool recycled = false;
            Fail<NexoException>(() => ArchiveExtractor.ExtractAsync(archive, games, null, CancellationToken.None, p => recycled = true, p => 0).GetAwaiter().GetResult());
            Check(!recycled && File.Exists(iso));
            using (var cancel = new CancellationTokenSource())
                Fail<OperationCanceledException>(() => ArchiveExtractor.ExtractAsync(archive, games, new Progress(n => cancel.Cancel()), cancel.Token, p => recycled = true).GetAwaiter().GetResult());
            Check(!recycled && File.Exists(iso)); NoStage(service);
            string existing = Path.Combine(games, Path.GetFileNameWithoutExtension(iso)); Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "propio.txt"), "mío");
            var output = ArchiveExtractor.ExtractAsync(archive, games, null, CancellationToken.None, p => { }).GetAwaiter().GetResult();
            Check(output == existing + " (2)" && File.ReadAllText(Path.Combine(existing, "propio.txt")) == "mío");
        });
        Test("ISO PS3: ruta Rock Ridge con .. se rechaza antes de publicar o reciclar", (root, service) =>
        {
            string iso = MakeIso(root); var bytes = File.ReadAllBytes(iso);
            var from = Encoding.ASCII.GetBytes("datos.txt"); var to = Encoding.ASCII.GetBytes("../escape"); int changes = 0;
            for (int i = 0; i <= bytes.Length - from.Length; i++)
                if (from.Where((b, n) => bytes[i + n] != b).Any() == false) { Array.Copy(to, 0, bytes, i, to.Length); changes++; }
            Check(changes > 0); File.WriteAllBytes(iso, bytes);
            var archive = ArchiveInspector.Inspect("PS3", iso); Check(archive.State == ArchiveState.Ready);
            bool recycled = false;
            Fail<NexoException>(() => ArchiveExtractor.ExtractAsync(archive, service.GetStatus("PS3").GamesFolder, null, CancellationToken.None, p => recycled = true).GetAwaiter().GetResult());
            Check(!recycled && File.Exists(iso)); NoStage(service);
            Check(!Directory.GetFiles(root, "escape", SearchOption.AllDirectories).Any());
        });
        Test("ISO: solo PS3 pasa a archivo pendiente; PS2 sigue siendo juego", (root, service) =>
        {
            var iso = MakeIso(root); string ps2 = Path.Combine(service.GetStatus("PS2").GamesFolder, "Mi juego.iso"); File.Copy(iso, ps2);
            var scan = service.ScanGamesDetailedAsync("PS2").GetAwaiter().GetResult(); Check(scan.Games.Count == 1 && scan.Archives.Count == 0);
            Check(!ArchiveInspector.IsArchive("PSP", ps2));
        });
    }

    private static string MakeIso(string root, bool encrypted = false, bool missing = false)
    {
        var source = Path.Combine(root, "payload"); Directory.CreateDirectory(Path.Combine(source, "PS3_GAME", "USRDIR"));
        var boot = Path.Combine(source, "PS3_GAME", "USRDIR", "EBOOT.BIN");
        if (missing) File.Delete(boot); else File.WriteAllBytes(boot, encrypted ? new byte[] { 9, 8, 7, 6, 5 } : new byte[] { 83, 67, 69, 0, 1, 2, 3, 4 });
        File.WriteAllBytes(Path.Combine(source, "PS3_GAME", "ICON0.PNG"), new byte[] { 137, 80, 78, 71 });
        Directory.CreateDirectory(Path.Combine(source, "PS3_GAME", "otra carpeta"));
        File.WriteAllText(Path.Combine(source, "PS3_GAME", "otra carpeta", "datos.txt"), "datos de prueba");
        string path = Path.Combine(root, "Juegos", "PS3", "Blur (Europe) (En,Fr,De,Es,It).iso");
        var info = Launcher.StartInfo(ArchiveExtractor.TarPath, new[] { "--format=iso9660", "-cf", path, "-C", source, "." });
        info.CreateNoWindow = true; info.RedirectStandardError = true;
        using (var process = Process.Start(info)) { string error = process.StandardError.ReadToEnd(); process.WaitForExit(); if (process.ExitCode != 0) throw new Exception(error); }
        using (var stream = File.OpenWrite(path)) { stream.Position = 0x800; var bytes = Encoding.ASCII.GetBytes("PlayStation3"); stream.Write(bytes, 0, bytes.Length); }
        return path;
    }
    private sealed class Progress : IProgress<long> { private readonly Action<long> report; internal Progress(Action<long> action) { report = action; } public void Report(long n) => report(n); }
    private static void NoStage(NexoService service) => Check(Directory.GetDirectories(service.GetStatus("PS3").GamesFolder, ".nexo-extract-*").Length == 0);
    private static void Check(bool value) { if (!value) throw new Exception("Comprobación ISO PS3 fallida."); }
    private static void Fail<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Falta excepción " + typeof(T).Name); }
}
