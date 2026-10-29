using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace Nexo.Core
{
    internal static class ArchiveExtractor
    {
        private const int MaxEntries = 50000;
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        internal static string TarPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "tar.exe");
        private sealed class Item
        {
            internal string Name;
            internal long Size;
            internal bool Directory;
        }

        internal static Task<string> ExtractAsync(ArchiveEntry archive, string gamesFolder, IProgress<long> progress,
            CancellationToken ct, Action<string> recycle = null, Func<string, long> freeSpace = null)
            => Task.Run(async () =>
            {
                await Gate.WaitAsync(ct).ConfigureAwait(false);
                try { return await Extract(archive, gamesFolder, progress, ct, recycle ?? Recycle, freeSpace ?? Available).ConfigureAwait(false); }
                finally { Gate.Release(); }
            }, ct);

        private static async Task<string> Extract(ArchiveEntry archive, string gamesFolder, IProgress<long> progress,
            CancellationToken ct, Action<string> recycle, Func<string, long> freeSpace)
        {
            string staging = null, published = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                Catalog.Get(archive.SystemId);
                gamesFolder = SafePaths.Full(gamesFolder);
                var path = SafePaths.Full(archive.Path);
                if (SwitchContainerInspector.IsContainer(path))
                    throw new NexoException("Los contenedores NSP/XCI y NSZ/NCZ no se extraen con esta función. Abre la carpeta y revisa el archivo indicado.");
                if (!Directory.Exists(gamesFolder) || SafePaths.HasReparsePoint(gamesFolder) || !SafePaths.Within(path, gamesFolder) ||
                    !SafePaths.IsRegularFile(path) || path.Substring(gamesFolder.Length).Split('\\', '/').Any(p => p.StartsWith(".nexo-extract-", StringComparison.OrdinalIgnoreCase)))
                    throw new NexoException("El comprimido debe estar en la carpeta de juegos de su sistema, sin enlaces ni junctions.");
                if (!File.Exists(TarPath)) throw new NexoException("No se encuentra tar.exe de Windows. Esta función necesita la herramienta de extracción de Windows.");
                // Lock against modification/replacement for BOTH listing and extraction.
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var fresh = ArchiveInspector.Inspect(archive.SystemId, path, ct);
                    if (fresh.State != ArchiveState.Ready) throw new NexoException(fresh.Message);
                    bool ps3Iso = ArchiveInspector.IsPs3Iso(archive.SystemId, path);
                    var items = await List(path, ct, ps3Iso).ConfigureAwait(false);
                    if (ps3Iso && !items.Any(i => !i.Directory && string.Equals(i.Name, "PS3_GAME/USRDIR/EBOOT.BIN", StringComparison.OrdinalIgnoreCase)))
                        throw new NexoException("Windows no encuentra PS3_GAME/USRDIR/EBOOT.BIN en el listado de la ISO.");
                    long total = items.Aggregate(0L, (sum, item) => checked(sum + item.Size));
                    long required = checked(total + 64L * 1024 * 1024 + items.Count * 4096L);
                    if (freeSpace(gamesFolder) < required)
                        throw new NexoException(string.Format("No hay espacio libre suficiente. Se necesitan al menos {0:0} MB para extraer y verificar el archivo.", required / 1048576.0));
                    staging = Path.Combine(gamesFolder, ".nexo-extract-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(staging);
                    progress?.Report(0);
                    var watch = Stopwatch.StartNew();
                    // -O sends data to stdout: tar NEVER receives a disk extraction destination.
                    // This prevents traversal/link attacks even if its own path checks regress.
                    await WithTar(new[] { "-xOf", path }, async output =>
                    {
                        var buffer = new byte[256 * 1024];
                        foreach (var item in items)
                        {
                            ct.ThrowIfCancellationRequested();
                            var destination = SafeDestination(staging, item.Name);
                            if (item.Directory) { Directory.CreateDirectory(destination); continue; }
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            if (SafePaths.HasReparsePoint(destination)) throw new NexoException("Se ha detectado un enlace durante la extracción.");
                            using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read, buffer.Length, true))
                            {
                                long remaining = item.Size;
                                while (remaining > 0)
                                {
                                    ct.ThrowIfCancellationRequested();
                                    int read = await output.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, remaining), ct).ConfigureAwait(false);
                                    if (read == 0) throw new NexoException("El comprimido está dañado o incompleto: faltan datos del juego.");
                                    await file.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                                    remaining -= read;
                                    if (watch.ElapsedMilliseconds >= 250)
                                    {
                                        await file.FlushAsync(ct).ConfigureAwait(false);
                                        progress?.Report(InspectTree(staging, null, ct));
                                        watch.Restart();
                                    }
                                }
                                await file.FlushAsync(ct).ConfigureAwait(false);
                            }
                        }
                        if (await output.ReadAsync(buffer, 0, 1, ct).ConfigureAwait(false) != 0)
                            throw new NexoException("El tamaño extraído no coincide con el contenido anunciado por el comprimido.");
                    }, ct).ConfigureAwait(false);
                    if (InspectTree(staging, items, ct) != total) throw new NexoException("No se pudo verificar el tamaño de los archivos extraídos.");
                    if (ps3Iso)
                    {
                        var eboot = items.Single(i => !i.Directory && string.Equals(i.Name, "PS3_GAME/USRDIR/EBOOT.BIN", StringComparison.OrdinalIgnoreCase));
                        using (var check = File.OpenRead(SafeDestination(staging, eboot.Name)))
                            if (check.ReadByte() != 83 || check.ReadByte() != 67 || check.ReadByte() != 69 || check.ReadByte() != 0)
                                throw new NexoException("El EBOOT extraído no es legible; se conserva la ISO sin enviar a la Papelera.");
                    }
                    progress?.Report(total);
                    ct.ThrowIfCancellationRequested();
                    var stem = Path.GetFileNameWithoutExtension(path);
                    // Validate the final name with exactly the same Windows path rules.
                    ValidateName(stem);
                    for (int suffix = 1; ; suffix++)
                    {
                        var candidate = Path.Combine(gamesFolder, stem + (suffix == 1 ? "" : " (" + suffix + ")"));
                        if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
                        if (SafePaths.HasReparsePoint(candidate)) throw new NexoException("La carpeta de destino contiene un enlace.");
                        try { Directory.Move(staging, candidate); published = candidate; staging = null; break; }
                        catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate)) { }
                    }
                }
                // Publication is the commit point: no cancellation between it and recycling.
                // On recycle failure preserve BOTH the complete output and the original archive.
                try { recycle(path); }
                catch (Exception error)
                { throw new NexoException("El juego se ha extraído en " + published + ", pero no se pudo enviar el comprimido a la Papelera. No se intentó borrarlo permanentemente.", error); }
                return published;
            }
            catch (OperationCanceledException) { throw; }
            catch (NexoException) { throw; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is OverflowException || error is System.ComponentModel.Win32Exception || error is InvalidOperationException)
            { throw new NexoException("No se pudo descomprimir el archivo. El comprimido se conserva. " + error.Message, error); }
            finally
            {
                if (staging != null)
                {
                    try { DeleteStaging(staging, gamesFolder); }
                    catch (Exception error) { throw new NexoException("La extracción no terminó y no se pudo limpiar el temporal: " + staging + ". El comprimido se conserva.", error); }
                }
            }
        }

        private static long Available(string folder) => new DriveInfo(Path.GetPathRoot(folder)).AvailableFreeSpace;

        // Package/firmware import shares the same validated listing and stdout-only extraction
        // as games. Caller owns a private staging directory and publishes it only after success.
        internal static async Task ExtractPackageAsync(string archive, string destination, IProgress<long> progress, CancellationToken ct)
        {
            if (!SafePaths.IsRegularFile(archive) || SafePaths.HasReparsePoint(destination) || Directory.Exists(destination) || File.Exists(destination))
                throw new NexoException("El paquete o su destino temporal no son seguros.");
            if (!File.Exists(TarPath)) throw new NexoException("Falta tar.exe de Windows; actualiza Windows antes de instalar emuladores.");
            using (var locked = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var items = await List(archive, ct).ConfigureAwait(false);
                long total = items.Aggregate(0L, (sum, item) => checked(sum + item.Size));
                if (total > 16L * 1024 * 1024 * 1024 || Available(Path.GetDirectoryName(destination)) < total + 64L * 1024 * 1024 + items.Count * 4096L)
                    throw new NexoException("Paquete demasiado grande o espacio libre insuficiente para extraerlo.");
                Directory.CreateDirectory(destination);
                long written = 0;
                var watch = Stopwatch.StartNew();
                await WithTar(new[] { "-xOf", archive }, async output =>
                {
                    byte[] buffer = new byte[262144];
                    foreach (var item in items)
                    {
                        ct.ThrowIfCancellationRequested();
                        string path = SafeDestination(destination, item.Name);
                        if (item.Directory) { Directory.CreateDirectory(path); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, buffer.Length, true))
                        {
                            long left = item.Size;
                            while (left > 0)
                            {
                                int n = await output.ReadAsync(buffer, 0, (int)Math.Min(left, buffer.Length), ct).ConfigureAwait(false);
                                if (n == 0) throw new NexoException("El paquete está incompleto.");
                                await file.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                                left -= n; written += n;
                                if (watch.ElapsedMilliseconds >= 250) { progress?.Report(written); watch.Restart(); }
                            }
                        }
                    }
                    if (await output.ReadAsync(buffer, 0, 1, ct).ConfigureAwait(false) != 0) throw new NexoException("El tamaño del paquete no coincide con su listado.");
                }, ct).ConfigureAwait(false);
                if (InspectTree(destination, items, ct) != total) throw new NexoException("No se pudo verificar el paquete extraído.");
                progress?.Report(total);
            }
        }

        internal static void CleanPackageStage(string stage, string parent) => DeleteStaging(stage, parent);
        private static void Recycle(string path) => FileSystem.DeleteFile(path, UIOption.AllDialogs,
            RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);

        private static async Task<List<Item>> List(string archive, CancellationToken ct, bool iso = false)
        {
            var bytes = new MemoryStream();
            using (bytes)
            {
                await WithTar(new[] { "-tvf", archive, "--numeric-owner" }, async output =>
                {
                    var buffer = new byte[8192]; int read;
                    while ((read = await output.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) != 0)
                    {
                        if (bytes.Length + read > 16 * 1024 * 1024) throw new NexoException("El listado del comprimido es demasiado grande.");
                        bytes.Write(buffer, 0, read);
                    }
                }, ct).ConfigureAwait(false);
                // Windows bsdtar uses the ANSI code page for its textual listing (not UTF-8).
                var listing = Encoding.Default.GetString(bytes.ToArray());
                var items = new List<Item>();
                var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var format = new Regex(@"^(?<type>[-d])[rwxStTs-]{9}[+@.]?\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+\S+\s+\d{1,2}\s+\S+\s(?<name>.+)$");
                foreach (var line in listing.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    ct.ThrowIfCancellationRequested();
                    var match = format.Match(line);
                    if (!match.Success) throw new NexoException("El comprimido contiene enlaces, entradas especiales o un listado que no se puede validar.");
                    bool directory = match.Groups["type"].Value == "d";
                    string name = match.Groups["name"].Value;
                    if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains(" link to ") || name.Contains(" -> "))
                        throw new NexoException("El comprimido contiene rutas absolutas o enlaces.");
                    while (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
                    if (directory) name = name.TrimEnd('/');
                    if (directory && (name == "" || name == ".")) continue;
                    if (iso) name = NormalizeIsoName(name);
                    ValidateName(name);
                    long length = long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
                    // ISO directory records occupy sectors, but -xO emits only regular files.
                    if (directory && length != 0 && !iso) throw new NexoException("El comprimido contiene una carpeta con un tamaño de datos no válido.");
                    if (directory) length = 0;
                    if (names.ContainsKey(name)) throw new NexoException("El comprimido contiene nombres duplicados o incompatibles con Windows.");
                    names.Add(name, directory);
                    items.Add(new Item { Name = name, Directory = directory, Size = length });
                    if (items.Count > MaxEntries) throw new NexoException("El comprimido contiene demasiadas entradas (máximo 50 000).");
                }
                foreach (var item in items)
                {
                    string parent = item.Name;
                    while (parent.LastIndexOf('/') >= 0)
                    {
                        parent = parent.Substring(0, parent.LastIndexOf('/'));
                        if (names.TryGetValue(parent, out var isDirectory) && !isDirectory)
                            throw new NexoException("El comprimido usa un mismo nombre como archivo y carpeta.");
                    }
                }
                if (!items.Any(i => !i.Directory)) throw new NexoException("El comprimido no contiene archivos para extraer.");
                return items;
            }
        }

        // ISO9660 marca con un punto final los archivos sin extensión ("initfs_Ps3." es "initfs_Ps3").
        // Windows no admite nombres acabados en punto y RPCS3 busca el nombre sin él.
        internal static string NormalizeIsoName(string name)
            => string.Join("/", name.Split('/').Select(part =>
                part.EndsWith(".", StringComparison.Ordinal) && part.TrimEnd('.').Length > 0 ? part.TrimEnd('.') : part));

        internal static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("/", StringComparison.Ordinal) || name.Contains('\\') || name.Contains(':') || name.Length > 2000)
                throw new NexoException("El comprimido contiene una ruta absoluta, ambigua o no admitida.");
            var parts = name.Split('/');
            if (parts.Length > 32) throw new NexoException("El comprimido tiene demasiados niveles de carpetas.");
            foreach (var part in parts)
            {
                if (part == "" || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.StartsWith(".nexo-extract-", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(part.Split('.')[0].TrimEnd(' '), @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])$", RegexOptions.IgnoreCase))
                    throw new NexoException("El comprimido contiene una ruta insegura o un nombre no admitido por Windows: " + part);
            }
        }

        private static string SafeDestination(string root, string name)
        {
            var destination = Path.GetFullPath(Path.Combine(root, name.Replace('/', '\\')));
            if (destination == root || !SafePaths.Within(destination, root) || SafePaths.HasReparsePoint(destination))
                throw new NexoException("Una entrada intenta salir del temporal o usar un enlace.");
            return destination;
        }

        private static long InspectTree(string root, List<Item> expected, CancellationToken ct)
        {
            if (SafePaths.HasReparsePoint(root)) throw new NexoException("El temporal contiene un enlace o junction.");
            var files = expected?.Where(i => !i.Directory).ToDictionary(i => i.Name.Replace('/', '\\'), i => i.Size, StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(); pending.Enqueue(root); long bytes = 0; int count = 0, found = 0;
            while (pending.Count > 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Dequeue()))
                {
                    ct.ThrowIfCancellationRequested();
                    if (++count > MaxEntries * 32) throw new NexoException("Demasiados archivos dentro del temporal.");
                    if (!SafePaths.Within(Path.GetFullPath(entry), root) || SafePaths.HasReparsePoint(entry)) throw new NexoException("Se ha detectado una ruta no segura en el temporal.");
                    if (Directory.Exists(entry)) pending.Enqueue(entry);
                    else
                    {
                        long length = new FileInfo(entry).Length;
                        if (files != null && (!files.TryGetValue(entry.Substring(root.Length + 1), out var size) || size != length))
                            throw new NexoException("El contenido extraído no coincide con el listado verificado.");
                        bytes = checked(bytes + length); found++;
                    }
                }
            }
            if (files != null && found != files.Count) throw new NexoException("Faltan archivos después de extraer.");
            return bytes;
        }

        private static async Task WithTar(string[] args, Func<Stream, Task> consume, CancellationToken ct)
        {
            var info = Launcher.StartInfo(TarPath, args);
            info.CreateNoWindow = true;
            info.RedirectStandardInput = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            info.StandardErrorEncoding = Encoding.Default;
            info.EnvironmentVariables.Remove("TAR_READER_OPTIONS");
            info.EnvironmentVariables.Remove("TAR_OPTIONS");
            using (var process = Process.Start(info))
            {
                process.StandardInput.Close(); // Never wait for a password/interactive prompt.
                using (ct.Register(() => Kill(process)))
                {
                    var errors = DrainErrors(process.StandardError);
                    try
                    {
                        await consume(process.StandardOutput.BaseStream).ConfigureAwait(false);
                        await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                        var message = await errors.ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        if (process.ExitCode != 0) throw new NexoException("Windows no pudo leer el comprimido: puede estar dañado, incompleto, protegido con contraseña o usar un formato no compatible. " + message);
                    }
                    catch
                    {
                        Kill(process); process.WaitForExit();
                        await errors.ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        throw;
                    }
                }
            }
        }
        private static void Kill(Process process) { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
        private static async Task<string> DrainErrors(StreamReader reader)
        {
            var message = new StringBuilder(); var buffer = new char[2048]; int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                if (message.Length < 4096) message.Append(buffer, 0, Math.Min(read, 4096 - message.Length));
            return message.ToString().Trim();
        }

        private static void DeleteStaging(string path, string gamesFolder)
        {
            if (!string.Equals(Path.GetDirectoryName(path), gamesFolder, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(path).StartsWith(".nexo-extract-", StringComparison.Ordinal) || !SafePaths.Within(path, gamesFolder) || SafePaths.HasReparsePoint(gamesFolder))
                throw new IOException("Temporal fuera de la carpeta de juegos.");
            if (Directory.Exists(path)) DeleteDirectoryNoLinks(path);
        }
        private static void DeleteDirectoryNoLinks(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                {
                    if ((File.GetAttributes(child) & FileAttributes.Directory) != 0) DeleteDirectoryNoLinks(child);
                    else File.Delete(child);
                }
            }
            Directory.Delete(path, false);
        }
    }
}
