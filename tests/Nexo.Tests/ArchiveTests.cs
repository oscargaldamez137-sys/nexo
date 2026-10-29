using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Nexo.Core;

internal static class ArchiveTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            var root = Path.Combine(workspace, ".nexo-test-archive-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(Path.GetFullPath(root), Path.GetFullPath(workspace))) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        foreach (var extension in new[] { ".7z", ".zip" })
        {
            Test("Archivo " + extension + ": tar real, Ready, extracción, bytes y reciclaje posterior", (root, svc) =>
            {
                var path = MakeTar(root, "Paquete" + extension);
                var result = svc.ScanGamesDetailedAsync("PS2").GetAwaiter().GetResult();
                Check(result.Games.Count == 0 && result.Archives.Count == 1);
                var archive = result.Archives.Single(); Check(archive.State == ArchiveState.Ready);
                long progress = -1; bool recycled = false;
                var folder = Path.Combine(root, "Juegos", "PS2");
                var output = ArchiveExtractor.ExtractAsync(archive, folder, new InlineProgress(n => { Check(n >= progress); progress = n; }), CancellationToken.None,
                    p =>
                    {
                        Check(File.Exists(Path.Combine(folder, "Paquete", "Niño con espacios.iso")));
                        Check(Directory.GetDirectories(folder, ".nexo-extract-*").Length == 0);
                        // Simulated Recycle Bin keeps the archive recoverable and tests the commit order.
                        Directory.CreateDirectory(Path.Combine(root, "papelera"));
                        File.Move(p, Path.Combine(root, "papelera", Path.GetFileName(p))); recycled = true;
                    }).GetAwaiter().GetResult();
                Check(recycled && !File.Exists(path));
                Check(File.ReadAllText(Path.Combine(output, "Niño con espacios.iso")) == "contenido del juego");
                Check(File.ReadAllText(Path.Combine(output, "sub", "otro.bin")) == "segundo archivo");
                Check(progress == new FileInfo(Path.Combine(output, "Niño con espacios.iso")).Length + new FileInfo(Path.Combine(output, "sub", "otro.bin")).Length);
                Check(svc.ScanGamesDetailedAsync("PS2").GetAwaiter().GetResult().Archives.Count == 0);
            });
        }
        Test("7z truncado conserva tamaño esperado y no se extrae aunque el cliente diga Ready", (root, svc) =>
        {
            var path = MakeTar(root, "Cortado.7z"); long expected = new FileInfo(path).Length;
            using (var stream = File.OpenWrite(path)) stream.SetLength(expected - 8);
            var archive = ArchiveInspector.Inspect("PS2", path);
            Check(archive.State == ArchiveState.Incomplete && archive.ExpectedSize == expected);
            var forged = new ArchiveEntry("PS2", path, "falso", 0, null, ArchiveState.Ready, "");
            Fail<NexoException>(() => svc.ExtractArchiveAsync(forged, null, CancellationToken.None).GetAwaiter().GetResult());
            Check(File.Exists(path)); NoStage(root);
        });
        Test("7z rechaza CRC de cabecera incorrecto y desbordamiento", (root, svc) =>
        {
            var path = MakeTar(root, "Header.7z"); var bytes = File.ReadAllBytes(path);
            bytes[8] ^= 1; File.WriteAllBytes(path, bytes);
            Check(ArchiveInspector.Inspect("PS2", path).State == ArchiveState.Unreadable);
            for (int i = 12; i < 28; i++) bytes[i] = 255;
            Array.Copy(BitConverter.GetBytes(ArchiveInspector.Crc32(bytes, 12, 20)), 0, bytes, 8, 4);
            File.WriteAllBytes(path, bytes); Check(ArchiveInspector.Inspect("PS2", path).State == ArchiveState.Unreadable);
        });
        Test("ZIP sin EOCD queda Incomplete; firma incorrecta queda Unreadable", (root, svc) =>
        {
            var path = MakeTar(root, "Cortado.zip");
            using (var stream = File.OpenWrite(path)) stream.SetLength(stream.Length - 22);
            Check(ArchiveInspector.Inspect("PS2", path).State == ArchiveState.Incomplete);
            File.WriteAllText(path, "Esto no es ZIP"); Check(ArchiveInspector.Inspect("PS2", path).State == ArchiveState.Unreadable);
        });
        Test("RAR4 y RAR5 se reconocen por firma sin afirmar integridad completa", (root, svc) =>
        {
            var path = Path.Combine(root, "Juegos", "PS2", "Demo.rar");
            foreach (var signature in new[] { new byte[] { 82, 97, 114, 33, 26, 7, 0 }, new byte[] { 82, 97, 114, 33, 26, 7, 1, 0 } })
            { File.WriteAllBytes(path, signature); Check(ArchiveInspector.Inspect("PS2", path).State == ArchiveState.Ready); }
        });
        Test("Rutas maliciosas ZIP se rechazan antes de escribir o reciclar", (root, svc) =>
        {
            foreach (var name in new[] { "../outside.iso", "/absolute.iso", "C:/absolute.iso", "sub/../../outside.iso", "sub\\..\\outside.iso", "file.iso:stream", "CON.iso" })
            {
                var path = Path.Combine(root, "Juegos", "PS2", "Danger.zip");
                MakeZip(path, name);
                bool recycled = false;
                Fail<NexoException>(() => Extract(path, root, recycle: p => recycled = true));
                Check(!recycled && File.Exists(path)); NoStage(root);
                Check(!Directory.EnumerateFiles(root, "*.iso", SearchOption.AllDirectories).Any());
            }
        });
        Test("ZIP con enlace simbólico se rechaza", (root, svc) =>
        {
            var path = Path.Combine(root, "Juegos", "PS2", "Link.zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using (var writer = new StreamWriter(entry.Open())) writer.Write("../outside");
            }
            // ZIP creator platform must be Unix for libarchive to interpret symlink mode bits.
            var bytes = File.ReadAllBytes(path);
            for (int i = 0; i <= bytes.Length - 46; i++)
                if (BitConverter.ToUInt32(bytes, i) == 0x02014b50) bytes[i + 5] = 3;
            File.WriteAllBytes(path, bytes);
            Fail<NexoException>(() => Extract(path, root)); NoStage(root); Check(File.Exists(path));
        });
        Test("ZIP con nombres que colisionan en Windows se rechaza sin sobrescribir", (root, svc) =>
        {
            var path = Path.Combine(root, "Juegos", "PS2", "Duplicate.zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (var name in new[] { "Game.iso", "GAME.ISO" })
                    using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(name);
            Fail<NexoException>(() => Extract(path, root)); NoStage(root);
            Check(File.Exists(path) && !Directory.Exists(Path.Combine(root, "Juegos", "PS2", "Duplicate")));
        });
        Test("Cancelación limpia el temporal y conserva el comprimido sin reciclar", (root, svc) =>
        {
            foreach (bool atEnd in new[] { false, true })
            {
                var path = MakeTar(root, "Cancel" + atEnd + ".zip"); bool recycled = false;
                using (var cts = new CancellationTokenSource())
                {
                    var report = new InlineProgress(n => { if (atEnd ? n > 0 : n == 0) cts.Cancel(); });
                    Fail<OperationCanceledException>(() => Extract(path, root, report, cts.Token, p => recycled = true));
                }
                Check(File.Exists(path) && !recycled); NoStage(root);
                Check(!Directory.Exists(Path.Combine(root, "Juegos", "PS2", "Cancel" + atEnd)));
            }
        });
        Test("Destino existente se conserva y el nuevo recibe (2)", (root, svc) =>
        {
            var path = MakeTar(root, "Repetido.zip"); var existing = Path.Combine(root, "Juegos", "PS2", "Repetido");
            Directory.CreateDirectory(existing); File.WriteAllText(Path.Combine(existing, "propio.txt"), "conservar");
            var output = Extract(path, root);
            Check(output == existing + " (2)"); Check(File.ReadAllText(Path.Combine(existing, "propio.txt")) == "conservar");
        });
        Test("Espacio libre se compara con tamaño descomprimido y conserva original", (root, svc) =>
        {
            var path = MakeTar(root, "Espacio.zip"); bool recycled = false;
            Fail<NexoException>(() => ArchiveExtractor.ExtractAsync(ArchiveInspector.Inspect("PS2", path), Path.Combine(root, "Juegos", "PS2"), null,
                CancellationToken.None, p => recycled = true, p => 0).GetAwaiter().GetResult());
            Check(!recycled && File.Exists(path)); NoStage(root);
        });
        Test("CRC de datos ZIP dañado nunca publica ni recicla", (root, svc) =>
        {
            var path = Path.Combine(root, "Juegos", "PS2", "Corrupto.zip"); MakeZip(path, "game.iso");
            var bytes = File.ReadAllBytes(path);
            // Damage the expected CRC, not unused padding bits in the deflate header.
            for (int i = 0; i <= bytes.Length - 46; i++)
                if (BitConverter.ToUInt32(bytes, i) == 0x02014b50) { bytes[i + 16] ^= 1; break; }
            File.WriteAllBytes(path, bytes);
            Check(ArchiveInspector.Inspect("PS2", path).State == ArchiveState.Ready);
            bool recycled = false; Fail<NexoException>(() => Extract(path, root, recycle: p => recycled = true));
            Check(!recycled && File.Exists(path)); NoStage(root);
            Check(!Directory.Exists(Path.Combine(root, "Juegos", "PS2", "Corrupto")));
        });
        Test("Error de Papelera preserva salida completa y comprimido", (root, svc) =>
        {
            var path = MakeTar(root, "TrashError.zip");
            Fail<NexoException>(() => Extract(path, root, recycle: p => { throw new IOException("Papelera no disponible"); }));
            Check(File.Exists(path)); Check(File.Exists(Path.Combine(root, "Juegos", "PS2", "TrashError", "Niño con espacios.iso"))); NoStage(root);
        });
        Test("El temporal no aparece y el archivo permanece bloqueado mientras se extrae", (root, svc) =>
        {
            var path = MakeTar(root, "Lock.zip"); bool checkedLock = false;
            Extract(path, root, new InlineProgress(n =>
            {
                if (n != 0) return;
                Fail<IOException>(() => { using (File.OpenWrite(path)) { } }); checkedLock = true;
                var stage = Directory.GetDirectories(Path.Combine(root, "Juegos", "PS2"), ".nexo-extract-*").Single();
                File.WriteAllText(Path.Combine(stage, "hidden.iso"), "not a game yet");
                Check(svc.ScanGamesDetailedAsync("PS2").GetAwaiter().GetResult().Games.Count == 0);
                File.Delete(Path.Combine(stage, "hidden.iso"));
            }));
            Check(checkedLock);
        });
        Test("No se permite extraer archivos de otro sistema o fuera de Juegos", (root, svc) =>
        {
            var path = MakeTar(root, "Outside.zip"); var archive = ArchiveInspector.Inspect("PSP", path);
            Fail<NexoException>(() => svc.ExtractArchiveAsync(archive, null, CancellationToken.None).GetAwaiter().GetResult());
            Check(File.Exists(path));
        });
    }

    private static string MakeTar(string root, string name)
    {
        var source = Path.Combine(root, "payload"); Directory.CreateDirectory(Path.Combine(source, "sub"));
        File.WriteAllText(Path.Combine(source, "Niño con espacios.iso"), "contenido del juego");
        File.WriteAllText(Path.Combine(source, "sub", "otro.bin"), "segundo archivo");
        var path = Path.Combine(root, "Juegos", "PS2", name);
        var info = Launcher.StartInfo(ArchiveExtractor.TarPath, new[] { "-a", "-cf", path, "-C", source, "." });
        info.CreateNoWindow = true; info.RedirectStandardError = true;
        using (var process = Process.Start(info))
        { var error = process.StandardError.ReadToEnd(); process.WaitForExit(); if (process.ExitCode != 0) throw new Exception(error); }
        return path;
    }
    private static void MakeZip(string path, string name)
    {
        if (File.Exists(path)) File.Delete(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var stream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open())
        { var bytes = Encoding.UTF8.GetBytes("contenido verificable"); stream.Write(bytes, 0, bytes.Length); }
    }
    private static string Extract(string path, string root, IProgress<long> progress = null, CancellationToken ct = default(CancellationToken), Action<string> recycle = null)
        => ArchiveExtractor.ExtractAsync(ArchiveInspector.Inspect("PS2", path), Path.Combine(root, "Juegos", "PS2"), progress, ct,
            recycle ?? (p => { })).GetAwaiter().GetResult();
    private sealed class InlineProgress : IProgress<long> { private readonly Action<long> action; internal InlineProgress(Action<long> action) { this.action = action; } public void Report(long n) => action(n); }
    private static void NoStage(string root) => Check(Directory.GetDirectories(Path.Combine(root, "Juegos", "PS2"), ".nexo-extract-*").Length == 0);
    private static void Check(bool condition) { if (!condition) throw new Exception("Comprobación de archivos comprimidos fallida."); }
    private static void Fail<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Falta excepción " + typeof(T).Name); }
}
