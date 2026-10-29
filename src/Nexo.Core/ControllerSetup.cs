using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexo.Core
{
    public sealed class ControllerSetupResult
    {
        public bool Changed { get; }
        public string Message { get; }
        public ControllerSetupResult(bool changed, string message) { Changed = changed; Message = message; }
    }

    internal static class ControllerSetup
    {
        internal static readonly object Gate = new object();
        internal static ControllerSetupResult Prepare(SystemStatus status, int? xinput, IEnumerable<string> externalFolders = null)
        {
            bool changed = false;
            lock (Gate)
            {
                try
                {
                    if (!status.Installed) return Result(false, "Primero copia el emulador en su carpeta.");
                    string folder = Path.GetDirectoryName(status.ExecutablePath);
                    // A manually selected executable outside Nexo is launchable, but never reconfigured.
                    if (!SafePaths.Within(status.ExecutablePath, status.EmulatorFolder) || SafePaths.HasReparsePoint(folder))
                        return Result(false, "Se conserva el emulador externo; la preparación automática requiere su carpeta Emuladores de Nexo.");
                    string id = status.System.Id;
                    string marker = id == "PS2" || id == "GBA" || id == "GBC" ? "portable.ini" : id == "GameCube-Wii" || id == "Xbox360" ? "portable.txt" : id == "Xbox" ? "xemu.toml" : null;
                    string data = id == "Switch" ? "user" : id == "PSP" ? "memstick" : null;
                    if (id == "Switch" && !new[] { "eden.exe", "yuzu.exe", "citron.exe" }.Contains(Path.GetFileName(status.ExecutablePath).ToLowerInvariant()))
                        return Result(false, "Se conserva la configuración de este emulador de Switch; no se ha verificado su formato de controles.");

                    bool portable = marker != null && File.Exists(Path.Combine(folder, marker)) || data != null && Directory.Exists(Path.Combine(folder, data));
                    if (id == "PS2" && File.Exists(Path.Combine(folder, "portable.txt"))) portable = true;
                    if (id == "PS2" && File.Exists(Path.Combine(folder, "portable.txt")))
                    {
                        string redirect = Path.Combine(folder, "portable.txt");
                        if (SafePaths.HasReparsePoint(redirect) || new FileInfo(redirect).Length > 4096 || !string.IsNullOrWhiteSpace(File.ReadAllText(redirect)))
                            return Result(false, "PCSX2 usa una ruta portable personalizada; se conserva sin cambios.");
                    }
                    if (id == "PSP" && File.Exists(Path.Combine(folder, "installed.txt")))
                        return Result(false, "PPSSPP ya usa installed.txt. Se conserva su memoria y sus controles; para hacerlo portable hay que trasladar antes esa memoria a memstick.");
                    if ((marker != null || data != null) && !portable &&
                        (externalFolders ?? ExternalFolders(id)).Any(HasData))
                        return Result(false, "Hay datos de " + status.System.EmulatorName + " en el perfil de Windows. Se conservan: trasládalos a la carpeta portable antes de activar la preparación automática.");

                    switch (id)
                    {
                        case "GBC": case "GBA":
                            changed |= EnsureFile(folder, marker);
                            var mgba = new ControllerConfig(Path.Combine(folder, "config.ini"));
                            if (!mgba.HasMgbaPadSettings)
                            {
                                // Qt 0.10.5 has profiles for old XInput names, but not SDL2's modern Xbox names.
                                foreach (string name in new[] { "Xbox 360 Controller", "Xbox One Controller" })
                                    mgba.SetSection("gba.input-profile." + name, new Dictionary<string, string> {
                                        ["keyA"] = "0", ["keyB"] = "1", ["keyL"] = "4", ["keyR"] = "5", ["keySelect"] = "6", ["keyStart"] = "7",
                                        ["keyUp"] = "-1", ["keyDown"] = "-1", ["keyLeft"] = "-1", ["keyRight"] = "-1",
                                        ["hat0Up"] = "6", ["hat0Down"] = "7", ["hat0Left"] = "5", ["hat0Right"] = "4",
                                        ["axisLeftAxis"] = "-0", ["axisLeftValue"] = "-16384", ["axisRightAxis"] = "+0", ["axisRightValue"] = "16384",
                                        ["axisUpAxis"] = "-1", ["axisUpValue"] = "-16384", ["axisDownAxis"] = "+1", ["axisDownValue"] = "16384" });
                                changed |= mgba.Save();
                            }
                            return Result(changed, "mGBA preparado con perfiles SDL para Xbox 360/One y los perfiles de fábrica para otros mandos. Se conservan los controles personalizados; conecta el mando antes de abrir el juego.");
                        case "Xbox":
                            changed |= EnsureFile(folder, marker);
                            changed |= XboxFiles.Configure(folder);
                            return Result(changed, "xemu asigna los mandos SDL al primer puerto libre con auto_bind de fábrica. Se conservan las asignaciones y auto_bind personalizados.");
                        case "Xbox360":
                            changed |= EnsureFile(folder, marker);
                            if (!xinput.HasValue) return Result(changed, "Conecta el mando XInput antes de abrir el juego para preparar Xenia; entretanto se conserva su controlador de fábrica o personalizado.");
                            var xenia = new ControllerConfig(Path.Combine(folder, "xenia-canary.config.toml"), toml: true);
                            var hid = xenia.ReadSection("HID");
                            if (hid == null || !hid.TryGetValue("hid", out var handler) || GraphicsSetup.Equivalent(handler, "any"))
                            { xenia.SetSection("HID", new Dictionary<string, string> { ["hid"] = "\"xinput\"" }); changed |= xenia.Save(); }
                            return Result(changed, "Xenia preparado para XInput; se conservan otros controladores elegidos manualmente.");
                        case "PS2":
                        {
                            var file = new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini"));
                            var pad = file.ReadSection("Pad1");
                            bool eligible = !file.Exists || Matches(pad, ControllerProfiles.Ps2Defaults, ControllerProfiles.Ps2Keyboard.Keys);
                            // A non-default version/layout must be handled by the emulator's own migration.
                            var ui = file.ReadSection("UI");
                            if (file.Exists && ui != null && ui.TryGetValue("SettingsVersion", out var version) && version != "1") eligible = false;
                            if (eligible)
                            {
                                file.SetSection("Pad1", ControllerProfiles.Ps2Pad(xinput));
                                var inputs = new Dictionary<string, string> { ["SDL"] = "true" };
                                if (xinput.HasValue) inputs["XInput"] = "true";
                                file.SetSection("InputSources", inputs);
                                if (!file.Exists) file.SetSection("UI", new Dictionary<string, string> { ["SettingsVersion"] = "1", ["SetupWizardIncomplete"] = "true" });
                                changed |= file.Save();
                            }
                            changed |= EnsureFile(folder, marker);
                            changed |= Ps2Bootstrap.Configure(folder);
                            return Result(changed, eligible ? "Mando de jugador 1 preparado para PCSX2: " + (xinput.HasValue ? "XInput " + (xinput.Value + 1) : "primer mando SDL") + "." : "Se conservan los controles personalizados o no reconocidos de PCSX2.");
                        }
                        case "GameCube-Wii":
                        {
                            // Without a connected XInput pad, don't permanently bind an arbitrary slot.
                            if (!xinput.HasValue) return Result(false, "Conecta un mando XInput para preparar GameCube. Wii requiere un perfil adecuado a cada juego.");
                            var file = new ControllerConfig(Path.Combine(folder, "User", "Config", "GCPadNew.ini"));
                            var pad = file.ReadSection("GCPad1");
                            var defaults = ControllerProfiles.DolphinKeyboard;
                            defaults["Device"] = "DInput/0/Keyboard Mouse";
                            defaults["Main Stick/Calibration"] = defaults["C-Stick/Calibration"] = "100.00 141.42 100.00 141.42 100.00 141.42 100.00 141.42";
                            bool eligible = !file.Exists || Matches(pad, defaults, ControllerProfiles.DolphinKeyboard.Keys);
                            if (eligible)
                            {
                                var bindings = ControllerProfiles.DolphinPad(xinput.Value);
                                bindings["Main Stick/Calibration"] = bindings["C-Stick/Calibration"] = "100.00";
                                file.SetSection("GCPad1", bindings);
                                changed |= file.Save();
                            }
                            // Wii games use the Wii Remote, configured separately; only replace Dolphin's keyboard/mouse default.
                            var wii = new ControllerConfig(Path.Combine(folder, "User", "Config", "WiimoteNew.ini"));
                            var remote = wii.ReadSection("Wiimote1");
                            bool wiiEligible = !wii.Exists || Matches(remote, ControllerProfiles.DolphinWiimoteKeyboard,
                                new[] { "Device", "Buttons/A", "Buttons/B", "Buttons/1", "Buttons/2", "D-Pad/Up", "IR/Up", "Extension", "Nunchuk/Stick/Up" });
                            if (wiiEligible)
                            {
                                wii.SetSection("Wiimote1", ControllerProfiles.DolphinWiimote(xinput.Value));
                                changed |= wii.Save();
                            }
                            changed |= EnsureDirectory(folder, "User");
                            changed |= EnsureFile(folder, marker);
                            string gc = eligible ? "Mando XInput preparado para GameCube (jugador 1)." : "Se conservan los controles personalizados de GameCube.";
                            string wiiText = wiiEligible ? " Wii: Wiimote + Nunchuk en el mando (puntero con el stick derecho, agitar con B)." : " Wii conserva su perfil personalizado.";
                            return Result(changed, gc + wiiText);
                        }
                        case "PS3":
                        {
                            var written = new ControllerConfig(Path.Combine(folder, "config", "input_configs", "global", "Default.yml"));
                            if (written.Exists && written.RepairPs3UnquotedDevice())
                            {
                                changed |= written.Save();
                                return Result(changed, "Reparado el mando de RPCS3: el nombre del dispositivo estaba sin comillas y RPCS3 no lo encontraba.");
                            }
                            if (!xinput.HasValue) return Result(false, "Conecta un mando XInput para preparar RPCS3. DualShock 4, DualSense y SDL se pueden seleccionar en Pads del emulador.");
                            // Respect a deliberately selected global profile. Never create an unused Default.yml.
                            string active = Path.Combine(folder, "config", "input_configs", "active_input_configurations.yml");
                            if (File.Exists(active))
                            {
                                if (SafePaths.HasReparsePoint(active) || new FileInfo(active).Length > 65536) throw new IOException("Perfil de entrada no reconocido.");
                                var significant = File.ReadAllLines(active).Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#")).ToArray();
                                if (significant.Length != 2 || significant[0] != "Active Configurations:" || significant[1].Trim() != "global: Default")
                                    return Result(false, "RPCS3 tiene una selección de perfiles propia; se conserva sin cambios.");
                            }
                            var file = new ControllerConfig(Path.Combine(folder, "config", "input_configs", "global", "Default.yml"));
                            if (!file.ReplacePs3Keyboard(xinput.Value)) return Result(false, "Se conservan los controles personalizados o no reconocidos de RPCS3.");
                            changed |= file.Save();
                            return Result(changed, "Mando XInput preparado para RPCS3 (jugador 1). Los perfiles por juego se conservan.");
                        }
                        case "PSP":
                            changed |= EnsureDirectory(folder, "memstick");
                            return Result(changed, "PPSSPP usa su mapeo XInput de fábrica y memoria portable. Se conservan los controles existentes.");
                        case "Retro":
                            return Result(false, "RetroArch usa la autodetección y sus perfiles autoconfig. Los mapeos y ajustes existentes se conservan.");
                        case "PS4":
                            return Result(false, "shadPS4 usa SDL Gamepad y su asignación predeterminada. Se conservan los controles existentes.");
                        case "PS5":
                            return Result(false, "Nova5 abre los mandos con SDL Gamepad y procesa sus botones y ejes. Se conservan sus ajustes.");
                        case "Switch":
                            changed |= EnsureDirectory(folder, "user");
                            return Result(changed, "Carpeta portable preparada. En Eden/yuzu selecciona una vez el mando en Controles de jugador 1: necesita su GUID SDL real; no admite engine:xinput." +
                                (string.Equals(Path.GetFileName(status.ExecutablePath), "eden.exe", StringComparison.OrdinalIgnoreCase) ? EdenFirmwareStatus.Read(folder) : null));
                        default:
                            return Result(false, "No hay preparación automática para este emulador.");
                    }
                }
                catch (Exception ex)
                {
                    // Controller preparation is optional; even malformed configuration never blocks play.
                    return Result(changed, "No se pudo completar la preparación del mando. El emulador se abrirá igualmente. " + ex.Message);
                }
            }
        }

        private static ControllerSetupResult Result(bool changed, string message) => new ControllerSetupResult(changed, message);
        internal static bool Matches(Dictionary<string, string> actual, Dictionary<string, string> defaults, IEnumerable<string> required)
            => actual != null && required.All(actual.ContainsKey) && actual.All(p => defaults.TryGetValue(p.Key, out var value) && value == p.Value);

        private static bool EnsureFile(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (SafePaths.HasReparsePoint(path)) throw new IOException("La configuración portable es un enlace.");
            if (File.Exists(path)) return false;
            using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            return true;
        }
        private static bool EnsureDirectory(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (SafePaths.HasReparsePoint(path)) throw new IOException("La carpeta portable es un enlace.");
            if (Directory.Exists(path)) return false;
            Directory.CreateDirectory(path); return true;
        }
        private static bool HasData(string path)
        {
            if (File.Exists(path)) return true;
            return Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();
        }
        internal static IEnumerable<string> ExternalFolders(string id)
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (id == "PS2") { yield return Path.Combine(docs, "PCSX2"); yield return Path.Combine(roaming, "PCSX2"); }
            if (id == "GBA" || id == "GBC") yield return Path.Combine(roaming, "mGBA");
            if (id == "Xbox") yield return Path.Combine(roaming, "xemu", "xemu");
            if (id == "Xbox360") yield return Path.Combine(docs, "Xenia");
            if (id == "GameCube-Wii") { yield return Path.Combine(docs, "Dolphin Emulator"); yield return Path.Combine(roaming, "Dolphin Emulator"); yield return Path.Combine(local, "Dolphin Emulator"); }
            if (id == "Switch") foreach (var name in new[] { "eden", "yuzu", "citron" }) yield return Path.Combine(roaming, name);
            // Without installed.txt PPSSPP explicitly uses <exe>/memstick; Documents is irrelevant.
        }
    }
}
