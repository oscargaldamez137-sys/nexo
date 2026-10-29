using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexo.Core
{
    internal static class SystemFiles
    {
        // Las pruebas sustituyen %APPDATA% por una carpeta del área de pruebas.
        internal static Func<string> AppDataFolder = () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        /// <summary>
        /// Dónde lee cada emulador de Switch sus claves y su firmware. Eden, yuzu y Citron usan «user» junto al ejecutable
        /// si existe (modo portable) y, si no, %APPDATA%\eden|yuzu|citron. Ryujinx usa «portable» o %APPDATA%\Ryujinx.
        /// </summary>
        internal static (string[] Keys, string[] Firmware) SwitchDataFolders(string executable, string appData)
        {
            string folder = Path.GetDirectoryName(executable);
            string name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
            if (name == "ryujinx")
            {
                string data = Directory.Exists(Path.Combine(folder, "portable")) ? Path.Combine(folder, "portable") : string.IsNullOrEmpty(appData) ? null : Path.Combine(appData, "Ryujinx");
                return data == null ? (new string[0], new string[0])
                    : (new[] { Path.Combine(data, "system", "prod.keys") }, new[] { Path.Combine(data, "bis", "system", "Contents", "registered") });
            }
            string user = Path.Combine(folder, "user");
            if (!Directory.Exists(user) && !string.IsNullOrEmpty(appData))
                user = Path.Combine(appData, name == "yuzu" || name == "citron" ? name : "eden");
            return (new[] { Path.Combine(user, "keys", "prod.keys") }, new[] { Path.Combine(user, "nand", "system", "Contents", "registered") });
        }

        // Firmware instalado: NCA como archivos (Eden, yuzu, Citron) o como carpetas «*.nca» (Ryujinx).
        private static bool HasFirmware(string registered)
        {
            try { return Directory.Exists(registered) && !SafePaths.HasReparsePoint(registered) && Directory.EnumerateFileSystemEntries(registered, "*.nca").Any(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
        }

        internal static IReadOnlyList<string> Inspect(SystemStatus status)
        {
            var messages = new List<string>();
            if (!status.Installed) { messages.Add("Falta instalar " + status.System.EmulatorName + "."); return messages.AsReadOnly(); }
            try
            {
                string folder = Path.GetDirectoryName(status.ExecutablePath);
                if (!SafePaths.Within(status.ExecutablePath, status.EmulatorFolder))
                { messages.Add("Ejecutable externo: no viaja con Nexo y no se prepara automáticamente."); return messages.AsReadOnly(); }
                switch (status.System.Id)
                {
                    case "Xbox": messages.AddRange(XboxFiles.Missing(folder)); break;
                    case "PS2":
                        if (Ps2Bootstrap.SelectedBios(folder) == null)
                        {
                            var selected = new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini")).ReadSection("Filenames");
                            if (selected != null && selected.TryGetValue("BIOS", out var name) && !string.IsNullOrEmpty(name))
                                messages.Add("La BIOS seleccionada de PS2 no está disponible o no es válida. Revisa la selección en PCSX2; se conserva tu elección.");
                            else if (Ps2Bootstrap.FindBios(folder) == null) messages.Add("Falta la BIOS de PS2. Importa el archivo .bin de tu consola.");
                            else messages.Add("BIOS de PS2 disponible; se seleccionará al preparar el emulador.");
                        }
                        break;
                    case "PS3":
                        if (!SafePaths.IsRegularFile(Path.Combine(folder, "dev_flash", "sys", "external", "liblv2.sprx")))
                            messages.Add("Falta instalar el firmware de PS3. Importa PS3UPDAT.PUP desde la web oficial de Sony.");
                        break;
                    case "Switch":
                        // Claves y firmware donde los lee el propio emulador: su carpeta portable o, si no la usa, su carpeta de usuario.
                        var data = SwitchDataFolders(status.ExecutablePath, AppDataFolder());
                        if (!data.Keys.Any(SafePaths.IsRegularFile)) messages.Add("Faltan las prod.keys de tu consola Switch.");
                        if (!data.Firmware.Any(HasFirmware))
                            messages.Add("Falta importar el firmware de tu Switch.");
                        messages.Add("La presencia de claves y firmware no demuestra que sean compatibles. Eden debe poder leer SystemVersion.");
                        string diagnostic = EdenFirmwareStatus.Read(folder); if (diagnostic != null) messages.Add(diagnostic.Trim());
                        break;
                    case "Retro":
                        if (!FileTree.Files(Path.Combine(folder, "cores"), 0, 5000, TimeSpan.FromMilliseconds(100), CancellationToken.None).Any(p => p.EndsWith("_libretro.dll", StringComparison.OrdinalIgnoreCase)))
                            messages.Add("Faltan los núcleos de RetroArch; las ROMs sin núcleo no aparecerán.");
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NexoException || ex is ArgumentException)
            { messages.Add("No se pudo comprobar la preparación: " + ex.Message); }
            return messages.AsReadOnly();
        }

        internal static async Task<string> Import(SystemStatus status, string[] paths, CancellationToken ct, Func<ProcessStartInfo, int> start)
        {
            if (!status.Installed) throw new NexoException("Instala el emulador antes de importar sus archivos.");
            if (!SafePaths.Within(status.ExecutablePath, status.EmulatorFolder) || SafePaths.HasReparsePoint(status.ExecutablePath))
                throw new NexoException("Solo se importan archivos al emulador de esta instalación de Nexo.");
            if (paths == null || paths.Length == 0 || paths.Length > 32) throw new NexoException("Selecciona entre 1 y 32 archivos del sistema.");
            paths = paths.Select(SafePaths.Full).ToArray();
            if (paths.Any(p => !SafePaths.IsRegularFile(p))) throw new NexoException("Los archivos seleccionados deben existir y no ser enlaces.");
            string folder = Path.GetDirectoryName(status.ExecutablePath), id = status.System.Id;
            ct.ThrowIfCancellationRequested();
            if (id == "PS3")
            {
                if (paths.Length != 1 || !Path.GetExtension(paths[0]).Equals(".pup", StringComparison.OrdinalIgnoreCase)) throw new NexoException("Selecciona un único PS3UPDAT.PUP.");
                using (var f = File.OpenRead(paths[0]))
                {
                    byte[] signature = new byte[8];
                    if (f.Length < 48 || f.Read(signature, 0, 8) != 8 || Encoding.ASCII.GetString(signature) != "SCEUF\0\0\0") throw new NexoException("El archivo no tiene una cabecera PUP válida.");
                }
                ct.ThrowIfCancellationRequested();
                start(Launcher.StartInfo(status.ExecutablePath, new[] { "--installfw", paths[0] }));
                return "RPCS3 ha abierto la instalación del firmware. Complétala en su ventana; Nexo comprobará dev_flash al actualizar el estado. Todavía no se da por instalado.";
            }
            if (id != "PS2" && id != "Switch" && id != "Xbox") throw new NexoException("Este sistema no tiene importación de BIOS o firmware.");
            if (id == "Switch" && !Path.GetFileName(status.ExecutablePath).Equals("eden.exe", StringComparison.OrdinalIgnoreCase))
                throw new NexoException("La importación Switch está verificada para Eden; configura manualmente los otros emuladores.");
            if (id == "PS2" && File.Exists(Path.Combine(folder, "portable.txt")) && !string.IsNullOrWhiteSpace(File.ReadAllText(Path.Combine(folder, "portable.txt"))))
                throw new NexoException("PCSX2 tiene una carpeta portable personalizada; se conserva. Importa allí la BIOS manualmente.");
            string stage = Path.Combine(status.EmulatorFolder, ".nexo-extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var changes = new List<ImportChange>();
            try
            {
                int number = 0;
                foreach (var source in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(source), temp = Path.Combine(stage, "file-" + number++);
                    string destination;
                    if (id == "PS2")
                    {
                        if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || !Ps2Bootstrap.IsBios(source)) throw new NexoException("No parece una BIOS de PS2 válida (ROM de 4–8 MB con ROMVER).");
                        destination = Path.Combine(folder, "bios", name);
                    }
                    else if (id == "Xbox") destination = Path.Combine(folder, "system", XboxFiles.ImportName(source));
                    else if (name.Equals("prod.keys", StringComparison.OrdinalIgnoreCase) || name.Equals("title.keys", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateKeys(source, name.Equals("title.keys", StringComparison.OrdinalIgnoreCase));
                        destination = Path.Combine(folder, "user", "keys", name.ToLowerInvariant());
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        string extracted = Path.Combine(stage, "firmware-" + number);
                        await ArchiveExtractor.ExtractPackageAsync(source, extracted, null, ct).ConfigureAwait(false);
                        var entries = Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories).Take(2001).ToArray();
                        ct.ThrowIfCancellationRequested();
                        if (entries.Length == 0 || entries.Length > 2000 || entries.Any(p => !Regex.IsMatch(Path.GetFileName(p), @"^[0-9a-fA-F]{32}\.nca$", RegexOptions.IgnoreCase) || new FileInfo(p).Length < 0x4000))
                            throw new NexoException("El firmware debe contener únicamente archivos NCA completos con nombres de 32 dígitos hexadecimales.");
                        Directory.CreateDirectory(temp);
                        foreach (var entry in entries) { ct.ThrowIfCancellationRequested(); File.Move(entry, Path.Combine(temp, Path.GetFileName(entry))); }
                        destination = Path.Combine(folder, "user", "nand", "system", "Contents", "registered");
                        changes.Add(new ImportChange(temp, destination, true));
                        continue;
                    }
                    else throw new NexoException("Selecciona prod.keys, title.keys o un ZIP de firmware Switch.");
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                        await input.CopyToAsync(output, 65536, ct).ConfigureAwait(false);
                    // Revalidate the copied bytes too (source may have changed before opening).
                    if (id == "PS2" && !Ps2Bootstrap.IsBios(temp)) throw new NexoException("La BIOS cambió durante la importación.");
                    if (id == "Xbox")
                    {
                        string check = temp + Path.GetExtension(source); File.Move(temp, check); temp = check;
                        if (XboxFiles.ImportName(temp) != Path.GetFileName(destination)) throw new NexoException("El archivo de Xbox cambió durante la importación.");
                    }
                    if (id == "Switch") ValidateKeys(temp, name.Equals("title.keys", StringComparison.OrdinalIgnoreCase));
                    changes.Add(new ImportChange(temp, destination, false));
                }
                if (changes.Select(p => p.Destination).Distinct(StringComparer.OrdinalIgnoreCase).Count() != changes.Count)
                    throw new NexoException("Se seleccionaron varios archivos para el mismo destino.");
                ct.ThrowIfCancellationRequested();
                // Commit is short and not cancelable. On I/O failure restore every replaced item.
                lock (ControllerSetup.Gate)
                {
                    try
                    {
                        foreach (var change in changes) change.Publish();
                        EmulatorInstaller.Portable(id, folder);
                    }
                    catch
                    {
                        foreach (var change in changes.AsEnumerable().Reverse()) change.Rollback();
                        throw;
                    }
                }
                if (id == "Xbox") return "Archivos de Xbox importados y rutas portables preparadas; se conservan rutas elegidas manualmente. Las versiones sustituidas quedan como .nexo-backup-*. La BIOS debe ser compatible con xemu.";
                if (id == "PS2")
                {
                    var prepared = ControllerSetup.Prepare(status, XInputGamepad.SetupControllerIndex(), new string[0]);
                    return "BIOS importada. " + prepared.Message + " Las versiones sustituidas se conservan como .nexo-backup-* junto al archivo.";
                }
                return "Claves/firmware importados. Los datos sustituidos se conservan como .nexo-backup-*. Nexo no puede certificar la compatibilidad criptográfica: comprueba SystemVersion en Eden.";
            }
            finally { ArchiveExtractor.CleanPackageStage(stage, status.EmulatorFolder); }
        }
        private static void ValidateKeys(string path, bool titleKeys)
        {
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new NexoException("El archivo de claves es demasiado grande.");
            bool any = false;
            foreach (var line in File.ReadLines(path))
            {
                string s = line.Trim(); if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";")) continue;
                // Key dumps also contain RSA keypairs and keyblobs, not only AES keys.
                // This checks syntax/bounds, never claims that the keys decrypt a given firmware.
                string pattern = titleKeys ? @"^[0-9A-Fa-f]{32}\s*=\s*[0-9A-Fa-f]{32}$" : @"^[A-Za-z0-9_]{1,100}\s*=\s*(?:[0-9A-Fa-f]{2}){16,2048}$";
                if (!Regex.IsMatch(s, pattern)) throw new NexoException("Formato de claves no reconocido. Se conserva el archivo anterior.");
                any = true;
            }
            if (!any) throw new NexoException("El archivo de claves está vacío.");
        }
        private sealed class ImportChange
        {
            internal string Source, Destination; private readonly bool directory; private string backup; private bool published;
            internal ImportChange(string source, string destination, bool directory) { Source = source; Destination = destination; this.directory = directory; }
            internal void Publish()
            {
                if (SafePaths.HasReparsePoint(Destination)) throw new NexoException("El destino contiene un enlace.");
                Directory.CreateDirectory(Path.GetDirectoryName(Destination));
                if (directory ? File.Exists(Destination) : Directory.Exists(Destination)) throw new NexoException("El destino tiene un tipo incompatible.");
                if (directory ? Directory.Exists(Destination) : File.Exists(Destination))
                {
                    string saved = Destination + ".nexo-backup-" + Guid.NewGuid().ToString("N");
                    if (!directory) { File.Replace(Source, Destination, saved); backup = saved; published = true; return; }
                    Move(Destination, saved); backup = saved;
                }
                Move(Source, Destination); published = true;
            }
            private void Move(string from, string to) { if (directory) Directory.Move(from, to); else File.Move(from, to); }
            internal void Rollback()
            { if (published) Move(Destination, Source); if (backup != null) Move(backup, Destination); }
        }
    }

    internal static class Ps2Bootstrap
    {
        internal static bool IsBios(string path)
        {
            if (!SafePaths.IsRegularFile(path)) return false;
            using (var file = File.OpenRead(path)) using (var r = new BinaryReader(file))
            {
                if (file.Length < 4 * 1048576 || file.Length > 8 * 1048576) return false;
                // ROMDIR is an aligned table; ROMVER's offset is the sum of aligned module sizes.
                bool found = false; long offset = 0;
                while (file.Position + 16 <= file.Length)
                {
                    byte[] entry = r.ReadBytes(16);
                    string name = Encoding.ASCII.GetString(entry, 0, 10).TrimEnd('\0');
                    if (!found) { if (name != "RESET") continue; found = true; }
                    if (name.Length == 0 || name.Length == 10) return false;
                    uint size = BitConverter.ToUInt32(entry, 12);
                    if (offset + size > file.Length) return false;
                    if (name == "ROMVER")
                    {
                        if (size < 14) return false;
                        file.Position = offset;
                        return Regex.IsMatch(Encoding.ASCII.GetString(r.ReadBytes(14)), @"^\d{4}[A-Z]{2}\d{8}$");
                    }
                    offset += ((long)size + 15) & ~15L;
                }
                return false;
            }
        }
        internal static string FindBios(string folder) => FileTree.Files(Path.Combine(folder, "bios"), 0, 100, TimeSpan.FromMilliseconds(250), CancellationToken.None)
            .Where(p => p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault(IsBios);
        internal static string SelectedBios(string folder)
        {
            var cfg = new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini"));
            var names = cfg.ReadSection("Filenames"); var folders = cfg.ReadSection("Folders");
            if (names == null || !names.TryGetValue("BIOS", out var name) || string.IsNullOrWhiteSpace(name)) return null;
            string dir = folders != null && folders.TryGetValue("Bios", out var d) ? d : "bios";
            string path = Path.GetFullPath(Path.Combine(folder, dir, name));
            return IsBios(path) ? path : null;
        }
        internal static bool Configure(string folder)
        {
            var file = new ControllerConfig(Path.Combine(folder, "inis", "PCSX2.ini"));
            var ui = file.ReadSection("UI");
            if (ui == null || !ui.TryGetValue("SettingsVersion", out var version) || version != "1") return false;
            var names = file.ReadSection("Filenames"); var folders = file.ReadSection("Folders");
            if (folders != null && folders.TryGetValue("Bios", out var directory) && directory != "bios") return false;
            string bios = SelectedBios(folder);
            if (bios == null && (names == null || !names.TryGetValue("BIOS", out var name) || string.IsNullOrEmpty(name)))
            {
                bios = FindBios(folder);
                if (bios == null) return false;
                file.SetSection("Filenames", new Dictionary<string, string> { ["BIOS"] = Path.GetFileName(bios) });
                file.SetSection("Folders", new Dictionary<string, string> { ["Bios"] = "bios" });
            }
            if (bios == null) return false;
            file.SetSection("UI", new Dictionary<string, string> { ["SetupWizardIncomplete"] = "false" });
            return file.Save();
        }
    }
}
