using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexo.Core;

internal static class ControllerTests
{
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> test) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-controller-" + Guid.NewGuid().ToString("N"));
            try { test(root, new NexoService(root)); }
            finally
            {
                if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-controller-")) throw new Exception("Limpieza fuera del proyecto.");
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        Test("Controles PS2: creación, versión, XInput real y SDL habilitado", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe");
            var r = Prepare(status, 2); True(r.Changed);
            string text = Read(status, "inis/PCSX2.ini");
            True(text.Contains("Cross = XInput-2/A") && text.Contains("LUp = XInput-2/-LeftY"));
            True(text.Contains("SDL = true") && text.Contains("XInput = true") && text.Contains("SettingsVersion = 1"));
            True(File.Exists(At(status, "portable.ini")));
        });
        Test("Controles PS2: sin XInput usa SDL sin inventar equivalencia de índices", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe"); Prepare(status, null);
            var text = Read(status, "inis/PCSX2.ini");
            True(text.Contains("Cross = SDL-0/FaceSouth") && text.Contains("Analog = SDL-0/Guide") && text.Contains("LargeMotor = SDL-0/LargeMotor"));
        });
        Test("Controles PS2: teclado de fábrica se reemplaza y conserva BIOS y Pad2", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe");
            Write(status, "inis/PCSX2.ini", "[UI]\nSettingsVersion = 1\n[Filenames]\nBIOS = mia.bin\n; comentario\n" + Ini("Pad1", ControllerProfiles.Ps2Defaults) + "[Pad2]\nCross = Keyboard/Z\n");
            True(Prepare(status, 0).Changed);
            var text = Read(status, "inis/PCSX2.ini");
            True(text.Contains("BIOS = mia.bin\n; comentario\n") && text.Contains("[Pad2]\nCross = Keyboard/Z\n"));
            True(text.Contains("Cross = XInput-0/A"));
        });
        Test("Controles PS2: una sola tecla o ajuste personalizado deja bytes intactos", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe"); Write(status, "portable.ini", "");
            foreach (var change in new[] { "Cross", "Deadzone", "Macro1", "Type" })
            {
                var d = ControllerProfiles.Ps2Defaults; d[change] = "custom";
                Write(status, "inis/PCSX2.ini", Ini("Pad1", d));
                byte[] before = File.ReadAllBytes(At(status, "inis/PCSX2.ini"));
                True(!Prepare(status, 0).Changed);
                True(before.SequenceEqual(File.ReadAllBytes(At(status, "inis/PCSX2.ini"))));
            }
        });
        Test("Controles: segunda llamada no escribe y borrar controles permite reaplicar", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe"); Prepare(status, 0);
            var path = At(status, "inis/PCSX2.ini"); var marker = At(status, "portable.ini");
            File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1)); File.SetLastWriteTimeUtc(marker, new DateTime(2020, 1, 1));
            True(!Prepare(status, 0).Changed);
            Equal(2020, File.GetLastWriteTimeUtc(path).Year); Equal(2020, File.GetLastWriteTimeUtc(marker).Year);
            File.Delete(path); True(Prepare(status, 0).Changed);
        });
        Test("Controles: INI ambiguo, vacío o jugador desactivado no se sobrescribe", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe"); Write(status, "portable.ini", "");
            foreach (var text in new[] { "", "[Pad1]\nType = None\n", "[Pad1]\nCross=A\nCross=B\n", "[Pad1]\n[Pad1]\n", "[Pad2]\nType=None\n" })
            {
                Write(status, "inis/PCSX2.ini", text); True(!Prepare(status, 0).Changed); Equal(text, Read(status, "inis/PCSX2.ini"));
            }
        });
        Test("Controles: error de E/S no daña el INI ni deja temporales", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe");
            Write(status, "inis/PCSX2.ini", Ini("Pad1", ControllerProfiles.Ps2Keyboard));
            string before = Read(status, "inis/PCSX2.ini");
            using (File.Open(At(status, "inis/PCSX2.ini"), FileMode.Open, FileAccess.Read, FileShare.Read))
            { var r = Prepare(status, 0); True(!r.Changed && r.Message.Contains("igualmente")); }
            Equal(before, Read(status, "inis/PCSX2.ini"));
            Equal(0, Directory.GetFiles(Path.GetDirectoryName(status.ExecutablePath), "*.tmp", SearchOption.AllDirectories).Length);
        });
        Test("Controles: no oculta datos globales al activar portable", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe");
            var global = Path.Combine(root, "datosGlobales"); Directory.CreateDirectory(global); File.WriteAllText(Path.Combine(global, "partida"), "save");
            var r = ControllerSetup.Prepare(status, 0, new[] { global });
            True(!r.Changed && r.Message.Contains("perfil de Windows")); True(!File.Exists(At(status, "portable.ini")));
            True(!File.Exists(At(status, "inis/PCSX2.ini")));
        });
        Test("Controles: portable.txt redirigido de PCSX2 no se altera", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe"); Write(status, "portable.txt", @"D:\MisDatos");
            True(!Prepare(status, 0).Changed); True(!File.Exists(At(status, "inis/PCSX2.ini")));
        });
        Test("Controles Dolphin: crea puerto correcto, gatillos y stick con Y positiva arriba", (root, service) =>
        {
            var status = Emu(root, service, "GameCube-Wii", "Dolphin.exe"); True(Prepare(status, 3).Changed);
            string text = Read(status, "User/Config/GCPadNew.ini");
            True(text.Contains("Device = XInput/3/Gamepad") && text.Contains("Main Stick/Up = `Left Y+`") && text.Contains("Triggers/L-Analog = `Trigger L`"));
            True(File.Exists(At(status, "portable.txt")));
            string wii = Read(status, "User/Config/WiimoteNew.ini");
            True(wii.Contains("[Wiimote1]") && wii.Contains("Device = XInput/3/Gamepad") && wii.Contains("IR/Up = `Right Y+`") && wii.Contains("Extension = Nunchuk"));
            True(!Prepare(status, 3).Changed);
        });
        Test("Controles Dolphin: reconoce calibración cuadrada del teclado de fábrica", (root, service) =>
        {
            var status = Emu(root, service, "GameCube-Wii", "Dolphin.exe"); var d = ControllerProfiles.DolphinKeyboard;
            d["Device"] = "DInput/0/Keyboard Mouse";
            d["Main Stick/Calibration"] = d["C-Stick/Calibration"] = "100.00 141.42 100.00 141.42 100.00 141.42 100.00 141.42";
            Write(status, "User/Config/GCPadNew.ini", Ini("GCPad1", d) + "[GCPad2]\nDevice = SDL/0/Mio\n");
            True(Prepare(status, 0).Changed); True(Read(status, "User/Config/GCPadNew.ini").Contains("[GCPad2]\nDevice = SDL/0/Mio\n"));
        });
        Test("Controles Dolphin: el Wiimote de teclado de fábrica pasa al mando y conserva los demás", (root, service) =>
        {
            var status = Emu(root, service, "GameCube-Wii", "Dolphin.exe");
            Write(status, "User/Config/WiimoteNew.ini", Ini("Wiimote1", ControllerProfiles.DolphinWiimoteKeyboard) + "[Wiimote2]\nDevice = DInput/0/Keyboard Mouse\n");
            True(Prepare(status, 0).Changed);
            string wii = Read(status, "User/Config/WiimoteNew.ini");
            True(wii.Contains("Device = XInput/0/Gamepad") && wii.Contains("Buttons/B = `Trigger R`") && !wii.Contains("`Click 1`"));
            True(wii.Contains("[Wiimote2]\nDevice = DInput/0/Keyboard Mouse\n"));
        });
        Test("Controles Dolphin: perfil personalizado y Wii intactos", (root, service) =>
        {
            var status = Emu(root, service, "GameCube-Wii", "Dolphin.exe"); var d = ControllerProfiles.DolphinKeyboard; d["Buttons/A"] = "`Q`";
            string text = Ini("GCPad1", d); Write(status, "User/Config/GCPadNew.ini", text); Write(status, "User/Config/WiimoteNew.ini", "mi wii");
            Write(status, "portable.txt", ""); True(!Prepare(status, 0).Changed);
            Equal(text, Read(status, "User/Config/GCPadNew.ini")); Equal("mi wii", Read(status, "User/Config/WiimoteNew.ini"));
        });
        Test("Controles RPCS3: nuevo YAML usa jugador y número de XInput correctos", (root, service) =>
        {
            var status = Emu(root, service, "PS3", "rpcs3.exe"); True(Prepare(status, 2).Changed);
            var text = Read(status, "config/input_configs/global/Default.yml");
            // Entre comillas: sin ellas, YAML corta en « #» y RPCS3 busca «XInput Pad» (caso real de un usuario).
            True(text.Contains("Player 1 Input:") && text.Contains("Device: \"XInput Pad #3\"") && text.Contains("Cross: A") && text.Contains("Left Stick Up: LS Y+"));
            True(!text.Contains("Device: XInput Pad #3"));
            True(!Prepare(status, 2).Changed);
        });
        Test("Controles RPCS3: repara el Device sin comillas que escribía Nexo, aun sin mando", (root, service) =>
        {
            var status = Emu(root, service, "PS3", "rpcs3.exe");
            var broken = "Player 1 Input:\n  Handler: XInput\n  Device: XInput Pad #1\n  Config:\n    Cross: A\n  Buddy Device: \"\"\nPlayer 2 Input:\n  Handler: DualSense\n  Device: Mi mando\n";
            Write(status, "config/input_configs/global/Default.yml", broken);
            True(Prepare(status, null).Changed);
            var text = Read(status, "config/input_configs/global/Default.yml");
            True(text.Contains("  Device: \"XInput Pad #1\"") && text.Contains("    Cross: A") && text.Contains("  Device: Mi mando"));
            True(!Prepare(status, null).Changed);
        });
        Test("Controles RPCS3: teclado de fábrica cambia sin tocar jugador 2", (root, service) =>
        {
            var status = Emu(root, service, "PS3", "rpcs3.exe");
            var yaml = Ps3Keyboard() + "Player 2 Input:\n  Handler: DualSense\n  Device: Mi mando\n";
            Write(status, "config/input_configs/global/Default.yml", yaml);
            Write(status, "config/input_configs/active_input_configurations.yml", "Active Configurations:\n  global: Default\n");
            True(Prepare(status, 1).Changed);
            True(Read(status, "config/input_configs/global/Default.yml").EndsWith("Player 2 Input:\n  Handler: DualSense\n  Device: Mi mando\n"));
        });
        Test("Controles RPCS3: cambios de tecla, perfil o parámetros conservan YAML", (root, service) =>
        {
            var status = Emu(root, service, "PS3", "rpcs3.exe");
            foreach (var text in new[] { Ps3Keyboard().Replace("Cross: X", "Cross: K"), Ps3Keyboard().Replace("Handler: Keyboard", "Handler: SDL"), Ps3Keyboard().Replace("Left Stick Deadzone: 0", "Left Stick Deadzone: 2"), Ps3Keyboard().Replace("    Cross: X\n", ""), Ps3Keyboard() + Ps3Keyboard(), "no es yaml" })
            { Write(status, "config/input_configs/global/Default.yml", text); True(!Prepare(status, 0).Changed); Equal(text, Read(status, "config/input_configs/global/Default.yml")); }
            Write(status, "config/input_configs/global/Default.yml", Ps3Keyboard());
            Write(status, "config/input_configs/active_input_configurations.yml", "Active Configurations:\n  global: Mi perfil\n");
            True(!Prepare(status, 0).Changed);
        });
        Test("Controles: RPCS3 y Dolphin esperan mando sin fijar un puerto desconectado", (root, service) =>
        {
            foreach (var pair in new[] { new[] { "PS3", "rpcs3.exe" }, new[] { "GameCube-Wii", "Dolphin.exe" } })
            { var status = Emu(root, service, pair[0], pair[1]); var r = Prepare(status, null); True(!r.Changed && r.Message.Contains("Conecta")); }
        });
        Test("Controles PPSSPP: portable y asignaciones propias intactas", (root, service) =>
        {
            var status = Emu(root, service, "PSP", "PPSSPPWindows64.exe"); True(Prepare(status, 0).Changed); True(!Prepare(status, 0).Changed);
            Write(status, "memstick/PSP/SYSTEM/controls.ini", "mi control"); Prepare(status, 0); Equal("mi control", Read(status, "memstick/PSP/SYSTEM/controls.ini"));
            Write(status, "installed.txt", @"D:\Partidas"); True(!Prepare(status, 0).Changed); Equal(@"D:\Partidas", Read(status, "installed.txt"));
        });
        Test("Controles Eden: portable una sola vez y ninguna asignación con GUID inventado", (root, service) =>
        {
            var status = Emu(root, service, "Switch", "eden.exe"); var r = Prepare(status, 0); True(r.Changed && r.Message.Contains("GUID"));
            True(Directory.Exists(At(status, "user"))); True(!Prepare(status, 0).Changed);
            Write(status, "user/config/qt-config.ini", "player_0_button_a=mi mando"); Prepare(status, 0); Equal("player_0_button_a=mi mando", Read(status, "user/config/qt-config.ini"));
        });
        Test("Controles: Retro, shad y Kyty no sustituyen autodetección ni ajustes propios", (root, service) =>
        {
            foreach (var p in new[] { new[] { "Retro", "retroarch.exe" }, new[] { "PS4", "shadPS4.exe" }, new[] { "PS5", "kyty_emulator.exe" } })
            { var status = Emu(root, service, p[0], p[1]); Write(status, "custom.ini", "propio"); True(!Prepare(status, 0).Changed); Equal("propio", Read(status, "custom.ini")); }
        });
        Test("Controles: emulador elegido fuera de su carpeta nunca se reconfigura", (root, service) =>
        {
            string path = Path.Combine(root, "externo.exe"); File.WriteAllText(path, "fixture"); service.SetExecutable("PS2", path);
            var r = Prepare(service.GetStatus("PS2"), 0); True(!r.Changed && r.Message.Contains("externo")); True(!File.Exists(Path.Combine(root, "portable.ini")));
        });
        Test("Controles: ruta de configuración con junction se rechaza sin escribir fuera", (root, service) =>
        {
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe");
            string target = Path.Combine(root, "otroDestino"); Directory.CreateDirectory(target);
            string junction = At(status, "inis");
            // This is a workspace-only junction; no shell deletion or traversal is used for cleanup.
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + junction + "\" \"" + target + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = System.Diagnostics.Process.Start(info))
            { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); Equal(0, p.ExitCode); }
            try
            {
                var r = Prepare(status, 0); True(!r.Changed && r.Message.Contains("enlace"));
                Equal(0, Directory.GetFileSystemEntries(target).Length);
            }
            finally { Directory.Delete(junction); }
        });
        Test("Controles: errores no bloquean LaunchEmulator ni LaunchGame; resultado accesible", (root, unused) =>
        {
            int launches = 0;
            var service = new NexoService(root, s => throw new IOException("Prueba de acceso denegado"), info => { launches++; return 123; });
            Emu(root, service, "PS2", "pcsx2-qt.exe");
            string game = Path.Combine(service.GetStatus("PS2").GamesFolder, "Juego.iso"); File.WriteAllText(game, "fixture");
            Equal(123, service.LaunchEmulator("PS2")); True(service.LastControllerSetup.Message.Contains("acceso denegado"));
            Equal(123, service.LaunchGame(new GameEntry("PS2", "Juego", game))); Equal(2, launches);
            True(!service.LastControllerSetup.Changed);
        });
        Test("Controles: E/S real fallida tampoco bloquea el proceso", (root, unused) =>
        {
            int launches = 0;
            var service = new NexoService(root, s => Prepare(s, 0), info => { launches++; return 99; });
            var status = Emu(root, service, "PS2", "pcsx2-qt.exe"); Write(status, "inis", "archivo que impide crear la carpeta");
            Equal(99, service.LaunchEmulator("PS2")); Equal(1, launches); True(service.LastControllerSetup.Message.Contains("igualmente"));
        });
        Test("Eden: detecta fallback del último registro sin atribuirlo a claves antiguas", (root, service) =>
        {
            var status = Emu(root, service, "Switch", "eden.exe");
            Write(status, "user/log/eden_log.txt", "[1] Common.Filesystem <Warning> system_version.cpp:SystemVersion:17: called - Using hardcoded firmware version 'NintendoSDK Firmware for NX 22.0.0-3.0'\nsecreto que no debe salir\n");
            var before = Read(status, "user/log/eden_log.txt"); var result = Prepare(status, null);
            True(!result.Changed && result.Message.Contains("22.0.0") && result.Message.Contains("último registro"));
            True(result.Message.Contains("no demuestra") && !result.Message.Contains("secreto"));
            Equal(before, Read(status, "user/log/eden_log.txt"));
        });
        Test("Eden: ausencia de aviso no afirma compatibilidad y no consulta logs antiguos", (root, service) =>
        {
            var status = Emu(root, service, "Switch", "eden.exe");
            Write(status, "user/log/eden_log.txt.old.txt", "SystemVersion: Using hardcoded firmware version 22.0.0");
            True(!Prepare(status, null).Message.Contains("Aviso de firmware"));
            Write(status, "user/log/eden_log.txt", "Firmware 23.0.1\n");
            True(EdenFirmwareStatus.Read(Path.GetDirectoryName(status.ExecutablePath)) == null);
            File.Move(status.ExecutablePath, At(status, "yuzu.exe"));
            Write(status, "user/log/eden_log.txt", "SystemVersion: Using hardcoded firmware version 22.0.0");
            True(!Prepare(service.GetStatus("Switch"), null).Message.Contains("Aviso de firmware"));
        });
        Test("Eden: lectura acotada y error de claves informado sin exponer contenido del log", (root, service) =>
        {
            var status = Emu(root, service, "Switch", "eden.exe");
            string old = "SystemVersion: Using hardcoded firmware version 21.0.0\n";
            Write(status, "user/log/eden_log.txt", old + new string('x', 1024 * 1024 + 100) + "\nSystemVersion: Using hardcoded firmware version 22.0.0\nErrorMissingKeyAreaKey secreto\n");
            var result = Prepare(status, null); True(result.Message.Contains("22.0.0") && !result.Message.Contains("21.0.0"));
            True(result.Message.Contains("error explícito") && !result.Message.Contains("secreto"));
        });
        run("Controles: conserva puerto observado, reconecta y detecta ausencia", () =>
        {
            Equal((int?)2, XInputGamepad.ChooseControllerIndex(2, i => i == 0 || i == 2));
            Equal((int?)1, XInputGamepad.ChooseControllerIndex(2, i => i == 1));
            Equal((int?)null, XInputGamepad.ChooseControllerIndex(-1, i => false));
        });
    }

    private static string Ps3Keyboard() => "Player 1 Input:\n  Handler: Keyboard\n  Device: Keyboard\n  Config:\n" + string.Concat(ControllerProfiles.Ps3Defaults.Select(p => "    " + p.Key + ": " + (p.Value.Length == 0 ? "\"\"" : p.Value) + "\n")) + "  Buddy Device: \"\"\n";
    private static SystemStatus Emu(string root, NexoService service, string id, string exe)
    { string path = Path.Combine(service.GetStatus(id).EmulatorFolder, "package", exe); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "fixture"); return service.GetStatus(id); }
    private static string At(SystemStatus s, string relative) => Path.Combine(Path.GetDirectoryName(s.ExecutablePath), relative.Replace('/', Path.DirectorySeparatorChar));
    private static string Read(SystemStatus s, string relative) => File.ReadAllText(At(s, relative));
    private static void Write(SystemStatus s, string relative, string text) { string path = At(s, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    private static string Ini(string section, IDictionary<string, string> values) => "[" + section + "]\n" + string.Concat(values.Select(p => p.Key + " = " + p.Value + "\n"));
    private static ControllerSetupResult Prepare(SystemStatus s, int? index) => ControllerSetup.Prepare(s, index, new string[0]);
    private static void True(bool condition) { if (!condition) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T a, T b) { if (!Equals(a, b)) throw new Exception("Esperado " + a + "; recibido " + b); }
}
