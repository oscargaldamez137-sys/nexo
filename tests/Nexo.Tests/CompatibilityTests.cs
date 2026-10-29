using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexo.Core;

internal static class CompatibilityTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-compat-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-compat-")) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        Test("Compatibilidad PS3: estructura JSON real, ID de SFO y cinco estados", (root, service) =>
        {
            string emu = Emulator(service, "PS3", "rpcs3.exe");
            Ps3Game(service, "BLES00759");
            foreach (string state in new[] { "Playable", "Ingame", "Intro", "Loadable", "Nothing" })
            {
                Database(emu, state);
                var game = Scan(service, "PS3");
                Equal(state, game.Compatibility.Status); Equal("2018-04-25", game.Compatibility.Date);
                Equal("01.02", game.Compatibility.RecommendedUpdate);
                Check(game.Compatibility.Explanation.Contains("RPCS3") && game.Compatibility.Explanation.Contains("desactualizada"));
                Equal("La base de RPCS3 indica la actualización 1.02; no la tienes instalada.", game.Compatibility.UpdateHint);
            }
            Check(!Directory.Exists(Path.Combine(emu, "config", "custom_configs")));
        });
        Test("Compatibilidad PS3: versión menor, igual y mayor; comparación numérica", (root, service) =>
        {
            string emu = Emulator(service, "PS3", "rpcs3.exe"); Ps3Game(service, "BLES00759"); Database(emu, "Ingame");
            string update = Path.Combine(emu, "dev_hdd0", "game", "BLES00759", "PARAM.SFO");
            Sfo(update, new Dictionary<string, string> { ["APP_VER"] = "01.01" }); Check(Scan(service, "PS3").Compatibility.UpdateHint.Contains("tienes la versión 1.01"));
            foreach (string version in new[] { "01.02", "1.2", "01.10", "02.00" })
            { Sfo(update, new Dictionary<string, string> { ["APP_VER"] = version }); Equal(null, Scan(service, "PS3").Compatibility.UpdateHint); }
            Write(update, "SFO roto"); Check(Scan(service, "PS3").Compatibility.UpdateHint.Contains("no se pudo comprobar"));
        });
        Test("Compatibilidad PS3: edición de disco actualizada no avisa de actualización ausente", (root, service) =>
        {
            string emu = Emulator(service, "PS3", "rpcs3.exe"); string sfo = Ps3Game(service, "BLES00759"); Database(emu, "Ingame");
            Sfo(sfo, new Dictionary<string, string> { ["TITLE_ID"] = "BLES00759", ["APP_VER"] = "01.02" });
            Equal(null, Scan(service, "PS3").Compatibility.UpdateHint);
            Sfo(sfo, new Dictionary<string, string> { ["TITLE_ID"] = "BLES00759", ["APP_VER"] = "01.00" });
            Check(Scan(service, "PS3").Compatibility.UpdateHint.Contains("no la tienes instalada"));
            Write(Path.Combine(emu, "dev_hdd0", "game", "BLES00759", "PARAM.SFO"), "dañado");
            Check(Scan(service, "PS3").Compatibility.UpdateHint.Contains("no se pudo comprobar"));
        });
        Test("Compatibilidad: BD ausente, corrupta, error de API y estado futuro son desconocidos", (root, service) =>
        {
            string emu = Emulator(service, "PS3", "rpcs3.exe"); Ps3Game(service, "BLES00759");
            Equal("Desconocido", Scan(service, "PS3").Compatibility.Status);
            foreach (string json in new[] { "{roto", "[]", "{\"return_code\":-1,\"results\":{\"BLES00759\":{\"status\":\"Playable\"}}}", "{\"results\":{}}" })
            { Write(Path.Combine(emu, "GuiConfigs", "compat_database.dat"), json); Equal("Desconocido", Scan(service, "PS3").Compatibility.Status); }
            Database(emu, "Nuevo", "ayer", "../../fuera"); var compatibility = Scan(service, "PS3").Compatibility;
            Equal("Desconocido", compatibility.Status); Equal(null, compatibility.Date); Equal(null, compatibility.RecommendedUpdate); Equal(null, compatibility.UpdateHint);
        });
        Test("Compatibilidad PS3: IDs inválidos y SFO truncado no impiden mostrar el juego", (root, service) =>
        {
            string emu = Emulator(service, "PS3", "rpcs3.exe"); string sfo = Ps3Game(service, "BLES00759"); Database(emu, "Playable");
            foreach (string id in new[] { "../../BLES00759", "BLES99999", "" })
            { Sfo(sfo, new Dictionary<string, string> { ["TITLE_ID"] = id }); Equal("Desconocido", Scan(service, "PS3").Compatibility.Status); }
            File.WriteAllBytes(sfo, new byte[] { 0, 80, 83, 70 }); Equal("Desconocido", Scan(service, "PS3").Compatibility.Status);
        });
        Test("Compatibilidad PS3: BD se lee una vez por escaneo y se refresca en el siguiente", (root, service) =>
        {
            string emu = Emulator(service, "PS3", "rpcs3.exe"); Ps3Game(service, "BLES00759"); Database(emu, "Intro");
            var game = Scan(service, "PS3"); var db = new CompatibilityDatabase("PS3", Path.Combine(emu, "rpcs3.exe"));
            Equal("Intro", db.Lookup(game, CancellationToken.None).Status);
            Database(emu, "Playable"); Equal("Intro", db.Lookup(game, CancellationToken.None).Status); Equal("Playable", Scan(service, "PS3").Compatibility.Status);
            bool cancelled = false;
            try { db.Lookup(game, new CancellationToken(true)); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled);
        });
        Test("Compatibilidad PS2: serial SYSTEM.CNF de ISO sin extracción, escala 0 a 6", (root, service) =>
        {
            string emu = Emulator(service, "PS2", "pcsx2-qt.exe"); string iso = Ps2Iso(root, service);
            byte[] before = File.ReadAllBytes(iso);
            var states = new[] { "Desconocido", "Nothing", "Intro", "Menu", "Ingame", "Playable", "Perfect" };
            for (int i = 0; i <= 6; i++)
            {
                Write(Path.Combine(emu, "resources", "GameIndex.yaml"), "# catálogo\nSLUS-12345:\n  name: Juego\n  compat: " + i + " # prueba\n  patches:\n    default: |\n      compat: 0\nSLUS-54321:\n  compat: 1\n");
                Equal(states[i], Scan(service, "PS2").Compatibility.Status);
            }
            Check(File.ReadAllBytes(iso).SequenceEqual(before)); Equal(0, Directory.GetDirectories(service.GetStatus("PS2").GamesFolder).Length);
        });
        Test("Compatibilidad PS2: ISO truncada o extent inválido no bloquean biblioteca", (root, service) =>
        {
            string emu = Emulator(service, "PS2", "pcsx2-qt.exe"); string iso = Ps2Iso(root, service);
            Write(Path.Combine(emu, "resources", "GameIndex.yaml"), "SLUS-12345:\n  compat: 5\n");
            var bytes = File.ReadAllBytes(iso); bytes[0x8000 + 156 + 6] ^= 1; File.WriteAllBytes(iso, bytes);
            Equal("Desconocido", Scan(service, "PS2").Compatibility.Status);
            using (var stream = File.OpenWrite(iso)) stream.SetLength(100);
            Equal("Desconocido", Scan(service, "PS2").Compatibility.Status);
        });
        run("GameIndex: duplicados, claves anidadas y datos desconocidos no dan falso Playable", () =>
        {
            foreach (string text in new[] {
                "SLUS-12345:\n  patches:\n    compat: 5\n",
                "SLUS-12345:\n  compat: 5\n  compat: 1\n",
                "SLUS-12345:\n  compat: 5\nslus-12345:\n  compat: 5\n",
                "SLUS-12345:\n  compat: 99\n", "SLUS-12345:\n  compat: *alias\n" })
                Equal(0, CompatibilityDatabase.ParseGameIndex(text, CancellationToken.None)["SLUS-12345"]);
        });
        run("GameEntry: constructor previo conserva compatibilidad no nula para otros sistemas", () =>
        { Equal("Desconocido", new GameEntry("PSP", "Juego", "juego.iso").Compatibility.Status); });
    }
    private static string Emulator(NexoService service, string id, string exe)
    { string folder = Path.Combine(service.GetStatus(id).EmulatorFolder, "package"); Write(Path.Combine(folder, exe), "fixture"); return folder; }
    private static GameEntry Scan(NexoService service, string id) => service.ScanGamesDetailedAsync(id).GetAwaiter().GetResult().Games.Single();
    private static string Ps3Game(NexoService service, string id)
    {
        string folder = Path.Combine(service.GetStatus("PS3").GamesFolder, "Nombre distinto", "PS3_GAME");
        Write(Path.Combine(folder, "USRDIR", "EBOOT.BIN"), "fixture");
        string sfo = Path.Combine(folder, "PARAM.SFO"); Sfo(sfo, new Dictionary<string, string> { ["TITLE"] = "Blur", ["TITLE_ID"] = id }); return sfo;
    }
    private static void Database(string emu, string status, string date = "2018-04-25", string update = "01.02")
        => Write(Path.Combine(emu, "GuiConfigs", "compat_database.dat"), "{\"return_code\":0,\"results\":{\"BLES00759\":{\"status\":\"" + status + "\",\"date\":\"" + date + "\",\"update\":\"" + update + "\",\"patchsets\":[]}}}");
    private static void Sfo(string path, Dictionary<string, string> fields)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var keys = fields.Keys.Select(k => Encoding.UTF8.GetBytes(k + "\0")).ToArray();
        var values = fields.Values.Select(v => Encoding.UTF8.GetBytes(v + "\0")).ToArray();
        using (var stream = File.Create(path)) using (var writer = new BinaryWriter(stream))
        {
            writer.Write(0x46535000u); writer.Write(0x00000101u); writer.Write((uint)(20 + keys.Length * 16));
            writer.Write((uint)(20 + keys.Length * 16 + keys.Sum(k => k.Length))); writer.Write((uint)keys.Length);
            int keyOffset = 0, valueOffset = 0;
            for (int i = 0; i < keys.Length; i++)
            { writer.Write((ushort)keyOffset); writer.Write((ushort)0x0204); writer.Write((uint)values[i].Length); writer.Write((uint)values[i].Length); writer.Write((uint)valueOffset); keyOffset += keys[i].Length; valueOffset += values[i].Length; }
            foreach (var key in keys) writer.Write(key); foreach (var value in values) writer.Write(value);
        }
    }
    private static string Ps2Iso(string root, NexoService service)
    {
        string source = Path.Combine(root, "iso-source"); Write(Path.Combine(source, "SYSTEM.CNF"), "BOOT2 = cdrom0:\\SLUS_123.45;1\r\nVER = 1.00\r\n");
        string iso = Path.Combine(service.GetStatus("PS2").GamesFolder, "Nombre sin serial.iso");
        var info = Launcher.StartInfo(ArchiveExtractor.TarPath, new[] { "--format=iso9660", "-cf", iso, "-C", source, "." });
        info.CreateNoWindow = true; info.RedirectStandardError = true;
        using (var process = Process.Start(info)) { string error = process.StandardError.ReadToEnd(); process.WaitForExit(); if (process.ExitCode != 0) throw new Exception(error); }
        return iso;
    }
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    private static void Check(bool value) { if (!value) throw new Exception("Comprobación de compatibilidad fallida."); }
    private static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception("Esperado " + expected + "; recibido " + actual); }
}
