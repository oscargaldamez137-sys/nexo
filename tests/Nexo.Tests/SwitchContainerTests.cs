using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexo.Core;

internal static class SwitchContainerTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-switch-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-switch-")) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        Test("Switch NSP: PFS0 mínimo válido permanece en Games con portada", (root, service) =>
        {
            string path = GamePath(service); MinimalNsp(path); File.WriteAllBytes(Path.ChangeExtension(path, ".png"), new byte[] { 1 });
            byte[] before = File.ReadAllBytes(path); var scan = Scan(service);
            Check(scan.Games.Count == 1 && scan.Archives.Count == 0 && scan.Games[0].CoverPath != null);
            Check(SwitchContainerInspector.Inspect(path) == null && before.SequenceEqual(File.ReadAllBytes(path)));
        });
        Test("Switch NSP: NCA grande primero y ticket final truncados dan tamaño total exacto de 64 bits", (root, service) =>
        {
            string path = GamePath(service); ulong big = 4397UL * 1048576;
            Pfs(path, new[] { "program.nca", "control.nca", "data.nca", "meta.cnmt.nca", "ticket.tik", "ticket.cert" },
                new[] { 0UL, big, big + 100, big + 200, big + 300, big + 400 }, new[] { big, 100UL, 100UL, 100UL, 100UL, 100UL }, 64);
            long expected = new FileInfo(path).Length - 64 + (long)big + 500;
            var scan = Scan(service); Check(scan.Games.Count == 0 && scan.Archives.Count == 1);
            var item = scan.Archives[0]; Equal(ArchiveState.Incomplete, item.State); Equal(expected, item.ExpectedSize.Value);
            Equal(new FileInfo(path).Length, item.Size); Equal("Switch", item.SystemId); Equal(path, item.Path);
            Check(item.Message.Contains("Descarga incompleta") && item.Message.Contains("MB"));
        });
        Test("Switch NSP: usa máximo de rangos desordenados, admite huecos y detecta un byte ausente", (root, service) =>
        {
            string path = GamePath(service); Pfs(path, new[] { "last.nca", "first.nca" }, new[] { 16UL, 0UL }, new[] { 3UL, 5UL }, 19);
            Check(SwitchContainerInspector.Inspect(path) == null); long expected = new FileInfo(path).Length;
            using (var stream = File.OpenWrite(path)) stream.SetLength(expected - 1);
            Equal(expected, SwitchContainerInspector.Inspect(path).ExpectedSize.Value);
        });
        Test("Switch NSP: tabla truncada y cabecera parcial señalan tamaño mínimo", (root, service) =>
        {
            string path = GamePath(service); MinimalNsp(path);
            long tableEnd = new FileInfo(path).Length - 16;
            using (var stream = File.OpenWrite(path)) stream.SetLength(20);
            var problem = SwitchContainerInspector.Inspect(path); Equal(ArchiveState.Incomplete, problem.State); Equal(tableEnd, problem.ExpectedSize.Value);
            Check(problem.Message.Contains("al menos"));
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("PFS0")); Equal(16L, SwitchContainerInspector.Inspect(path).ExpectedSize.Value);
        });
        Test("Switch NSP: firma, tablas absurdas, nombres, solapamientos y overflow se rechazan", (root, service) =>
        {
            string path = GamePath(service); MinimalNsp(path); var good = File.ReadAllBytes(path);
            foreach (Action<byte[]> damage in new Action<byte[]>[] {
                b => b[0] = 0, b => Put(b, 4, BitConverter.GetBytes(uint.MaxValue)),
                b => Put(b, 8, BitConverter.GetBytes(uint.MaxValue)), b => Put(b, 4, BitConverter.GetBytes(0u)),
                b => Put(b, 32, BitConverter.GetBytes(uint.MaxValue)), b => b[40] = 255,
                b => Put(b, 16, BitConverter.GetBytes(ulong.MaxValue)), b => Put(b, 24, BitConverter.GetBytes(ulong.MaxValue)) })
            { var bad = (byte[])good.Clone(); damage(bad); File.WriteAllBytes(path, bad); Equal(ArchiveState.Unreadable, SwitchContainerInspector.Inspect(path).State); }
            Pfs(path, new[] { "a.nca", "b.nca" }, new[] { 0UL, 3UL }, new[] { 5UL, 5UL }, 8);
            Equal(ArchiveState.Unreadable, SwitchContainerInspector.Inspect(path).State);
            Pfs(path, new[] { "a.nca", "A.nca" }, new[] { 0UL, 8UL }, new[] { 8UL, 8UL }, 16);
            Equal(ArchiveState.Unreadable, SwitchContainerInspector.Inspect(path).State);
            MinimalNsp(path); var noNul = File.ReadAllBytes(path); noNul[48] = 1; File.WriteAllBytes(path, noNul);
            Equal(ArchiveState.Unreadable, SwitchContainerInspector.Inspect(path).State);
        });
        Test("Switch: NSZ, NCZ y NSP con NCZ quedan como avisos no extraíbles", (root, service) =>
        {
            string folder = service.GetStatus("Switch").GamesFolder;
            foreach (string extension in new[] { ".nsp", ".nsz", ".ncz" })
            { string path = Path.Combine(folder, "Comprimido" + extension); Pfs(path, new[] { "program.NCZ" }, new[] { 0UL }, new[] { 16UL }, 16); }
            var scan = Scan(service); Check(scan.Games.Count == 0 && scan.Archives.Count == 3);
            Check(scan.Archives.All(a => a.State == ArchiveState.Unreadable && a.Message.Contains("NSZ comprimido: no compatible con Eden 0.2.1")));
        });
        Test("Switch XCI: admite imagen recortada a datos válidos sin exigir capacidad nominal", (root, service) =>
        {
            string path = GamePath(service, ".xci"); MinimalXci(path); var scan = Scan(service);
            Check(scan.Games.Count == 1 && scan.Archives.Count == 0);
            using (var stream = File.OpenWrite(path)) stream.SetLength(0x800); // Optional trailing padding.
            Check(SwitchContainerInspector.Inspect(path) == null);
        });
        Test("Switch XCI: sin HEAD, overflow y límites incoherentes son Unreadable", (root, service) =>
        {
            string path = GamePath(service, ".xci"); MinimalXci(path); var good = File.ReadAllBytes(path);
            foreach (Action<byte[]> damage in new Action<byte[]>[] {
                b => b[0x100] = 0, b => Put(b, 0x118, BitConverter.GetBytes(ulong.MaxValue)),
                b => Put(b, 0x130, BitConverter.GetBytes(0UL)), b => Put(b, 0x138, BitConverter.GetBytes(ulong.MaxValue)) })
            { var bad = (byte[])good.Clone(); damage(bad); File.WriteAllBytes(path, bad); Equal(ArchiveState.Unreadable, SwitchContainerInspector.Inspect(path).State); }
            var scan = Scan(service); Check(scan.Games.Count == 0 && scan.Archives.Count == 1);
        });
        Test("Switch XCI: cabecera o datos truncados quedan Incomplete con ExpectedSize", (root, service) =>
        {
            string path = GamePath(service, ".xci"); MinimalXci(path);
            using (var stream = File.OpenWrite(path)) stream.SetLength(0x500);
            var problem = SwitchContainerInspector.Inspect(path); Equal(ArchiveState.Incomplete, problem.State); Equal(0x600L, problem.ExpectedSize.Value);
            using (var stream = File.OpenWrite(path)) stream.SetLength(0x110);
            Equal(0x200L, SwitchContainerInspector.Inspect(path).ExpectedSize.Value);
        });
        Test("Switch: extracción rechaza incluso ArchiveEntry Ready falsificado, sin temporal ni reciclaje", (root, service) =>
        {
            foreach (string ext in new[] { ".nsp", ".xci", ".nsz", ".ncz" })
            {
                string path = GamePath(service, ext); File.WriteAllText(path, "original");
                var fake = new ArchiveEntry("Switch", path, "Juego", 0, null, ArchiveState.Ready, "");
                Fail(() => service.ExtractArchiveAsync(fake, null, CancellationToken.None).GetAwaiter().GetResult(), "no se extraen");
                bool recycled = false, checkedSpace = false;
                Fail(() => ArchiveExtractor.ExtractAsync(fake, service.GetStatus("Switch").GamesFolder, null, CancellationToken.None,
                    p => recycled = true, p => { checkedSpace = true; return long.MaxValue; }).GetAwaiter().GetResult(), "no se extraen");
                Check(!recycled && !checkedSpace && File.ReadAllText(path) == "original");
            }
            Equal(0, Directory.GetDirectories(service.GetStatus("Switch").GamesFolder).Length);
        });
        Test("Switch: lanzamiento revalida archivo cambiado desde el escaneo", (root, service) =>
        {
            File.WriteAllText(Path.Combine(service.GetStatus("Switch").EmulatorFolder, "eden.exe"), "fixture");
            string path = GamePath(service); MinimalNsp(path); var game = Scan(service).Games.Single();
            Equal(service.GetStatus("Switch").ExecutablePath, service.PrepareGame(game).FileName);
            using (var stream = File.OpenWrite(path)) stream.SetLength(stream.Length - 1);
            Fail(() => service.PrepareGame(game), "Descarga incompleta");
        });
        Test("Switch: cancelación se propaga y otros formatos conservan su tratamiento", (root, service) =>
        {
            string path = GamePath(service); MinimalNsp(path); bool cancelled = false;
            try { SwitchContainerInspector.Inspect(path, new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled);
            File.WriteAllText(GamePath(service, ".nro"), "fixture"); File.WriteAllText(GamePath(service, ".nca"), "fixture");
            Equal(3, Scan(service).Games.Count);
            Check(!ArchiveInspector.IsArchive("Switch", path));
        });
    }
    internal static void MinimalXci(string path)
    {
        var bytes = new byte[0x600]; Put(bytes, 0x100, Encoding.ASCII.GetBytes("HEAD")); bytes[0x10d] = 0xe0; // Nominal 8 GB.
        Put(bytes, 0x118, BitConverter.GetBytes(2UL)); Put(bytes, 0x130, BitConverter.GetBytes(0x200UL));
        Put(bytes, 0x138, BitConverter.GetBytes(16UL)); Put(bytes, 0x200, Encoding.ASCII.GetBytes("HFS0")); File.WriteAllBytes(path, bytes);
    }
    private static void MinimalNsp(string path) => Pfs(path, new[] { "game.nca" }, new[] { 0UL }, new[] { 16UL }, 16);
    private static void Pfs(string path, string[] names, ulong[] offsets, ulong[] sizes, int actualPayload)
    {
        var strings = names.Select(n => Encoding.UTF8.GetBytes(n + "\0")).ToArray();
        using (var stream = File.Create(path)) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("PFS0")); writer.Write((uint)names.Length); writer.Write((uint)strings.Sum(s => s.Length)); writer.Write(0u);
            uint nameOffset = 0;
            for (int i = 0; i < names.Length; i++) { writer.Write(offsets[i]); writer.Write(sizes[i]); writer.Write(nameOffset); writer.Write(0u); nameOffset += (uint)strings[i].Length; }
            foreach (var name in strings) writer.Write(name); writer.Write(new byte[actualPayload]);
        }
    }
    private static void Put(byte[] destination, int offset, byte[] bytes) => Array.Copy(bytes, 0, destination, offset, bytes.Length);
    private static string GamePath(NexoService service, string extension = ".nsp") => Path.Combine(service.GetStatus("Switch").GamesFolder, "Juego" + extension);
    private static GameScanResult Scan(NexoService service) => service.ScanGamesDetailedAsync("Switch").GetAwaiter().GetResult();
    private static void Fail(Action action, string message) { try { action(); } catch (NexoException ex) { Check(ex.Message.Contains(message)); return; } throw new Exception("Falta rechazo NexoException."); }
    private static void Check(bool value) { if (!value) throw new Exception("Comprobación de Switch fallida."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Esperado " + expected + "; recibido " + actual); }
}
