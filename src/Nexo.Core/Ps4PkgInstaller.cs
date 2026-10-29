using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace Nexo.Core
{
    internal static class Ps4PkgInstaller
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        internal static Task<string> InstallAsync(ArchiveEntry archive, string gamesFolder, IProgress<long> progress,
            CancellationToken ct, Action<string> recycle = null, Func<string, long> available = null)
            => Task.Run(async () =>
            {
                await Gate.WaitAsync(ct).ConfigureAwait(false);
                try { return Install(archive, gamesFolder, progress, ct, recycle ?? Recycle, available ?? Free); }
                finally { Gate.Release(); }
            }, ct);
        private static void Recycle(string path) => FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        private static long Free(string path) => new DriveInfo(System.IO.Path.GetPathRoot(path)).AvailableFreeSpace;
        private static string Install(ArchiveEntry archive, string gamesFolder, IProgress<long> progress,
            CancellationToken ct, Action<string> recycle, Func<string, long> available)
        {
            var packages = new List<Ps4Package>(); string stage = null, published = null;
            try
            {
                ct.ThrowIfCancellationRequested(); gamesFolder = SafePaths.Full(gamesFolder);
                if (archive == null) throw new NexoException("Selecciona un PKG de PS4.");
                var fullPath = SafePaths.Full(archive.Path);
                if (!Ps4Package.IsPkg(archive.SystemId, fullPath) || !SafePaths.Within(fullPath, gamesFolder) ||
                    !SafePaths.IsRegularFile(fullPath) || SafePaths.HasReparsePoint(gamesFolder) ||
                    fullPath.Substring(gamesFolder.Length).Split('\\', '/').Any(p => p.StartsWith(".nexo-extract-", StringComparison.OrdinalIgnoreCase)))
                    throw new NexoException("El PKG debe estar en Juegos/PS4, sin enlaces ni temporales.");
                var sourceFolder = System.IO.Path.GetDirectoryName(fullPath);
                var main = new Ps4Package(fullPath); packages.Add(main);
                if (!main.Base) throw new NexoException("Selecciona el PKG del juego base para instalar también su parche y sus DLC.");
                // Deliberately bounded: siblings + DLC subdirectory only; not the whole PS4 library.
                var candidates = Directory.GetFiles(sourceFolder, "*.pkg").ToList();
                var dlc = System.IO.Path.Combine(sourceFolder, "DLC");
                if (Directory.Exists(dlc))
                {
                    if (SafePaths.HasReparsePoint(dlc)) throw new NexoException("La carpeta DLC contiene un enlace.");
                    candidates.AddRange(Directory.GetFiles(dlc, "*.pkg"));
                }
                if (candidates.Count > 512) throw new NexoException("Hay demasiados PKG en el lote (máximo 512).");
                foreach (var path in candidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.Equals(path, main.Path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!SafePaths.IsRegularFile(path)) throw new NexoException("Un PKG del lote no es un archivo regular.");
                    var other = new Ps4Package(path);
                    if (other.TitleId != main.TitleId) { other.Dispose(); continue; }
                    packages.Add(other);
                    if (other.Base) throw new NexoException("Hay dos juegos base del mismo Title ID. Deja solo uno en la carpeta.");
                }
                if (packages.Count(p => p.Patch) > 1 || packages.Where(p => p.Addon).GroupBy(p => p.ContentId, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new NexoException("Hay parches o DLC duplicados. Deja una sola versión de cada uno antes de instalar.");
                long total = 0, fileCount = 0, directoryCount = 0;
                foreach (var p in packages)
                {
                    ct.ThrowIfCancellationRequested(); p.LoadFiles(ct);
                    directoryCount += p.Directories.Count + 4;
                    foreach (var f in p.Files) { total = checked(total + f.Data.Length); fileCount++; }
                }
                long required = checked(total + (fileCount + directoryCount) * 4096 + 64L * 1024 * 1024);
                if (available(gamesFolder) < required)
                    throw new NexoException(string.Format("No hay espacio suficiente. Se necesitan {0:0} MB libres adicionales para instalar el lote; los PKG se conservan hasta verificar el resultado.", required / 1048576.0));
                stage = System.IO.Path.Combine(gamesFolder, ".nexo-extract-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage); long written = 0; progress?.Report(0); var watch = Stopwatch.StartNew();
                foreach (var p in packages)
                {
                    string relative = p.Base ? p.TitleId : p.Patch ? p.TitleId + "-UPDATE" : "addons/" + p.TitleId + "/" + p.ContentId;
                    var target = System.IO.Path.Combine(stage, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    foreach (var directory in p.Directories)
                    {
                        ct.ThrowIfCancellationRequested(); ArchiveExtractor.ValidateName(directory);
                        var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(target, directory.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                        if (!SafePaths.Within(dir, stage) || SafePaths.HasReparsePoint(dir)) throw new NexoException("Carpeta PFS no segura.");
                        Directory.CreateDirectory(dir);
                    }
                    foreach (var item in p.Files)
                    {
                        ct.ThrowIfCancellationRequested(); ArchiveExtractor.ValidateName(item.Name);
                        var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(target, item.Name.Replace('/', System.IO.Path.DirectorySeparatorChar)));
                        if (!SafePaths.Within(destination, stage) || SafePaths.HasReparsePoint(destination)) throw new NexoException("Ruta no segura durante la instalación del PKG.");
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            for (long offset = 0; offset < item.Data.Length;)
                            {
                                ct.ThrowIfCancellationRequested(); int take = (int)Math.Min(256 * 1024, item.Data.Length - offset);
                                var bytes = item.Data.Read(offset, take); output.Write(bytes, 0, bytes.Length); written += bytes.Length; offset += bytes.Length;
                                if (watch.ElapsedMilliseconds >= 150) { progress?.Report(written); watch.Restart(); }
                            }
                            output.Flush(true);
                        }
                    }
                    var sfo = System.IO.Path.Combine(target, "sce_sys", "param.sfo");
                    if (Metadata.ReadSfoString(sfo, "TITLE_ID") != main.TitleId || Metadata.ReadSfoString(sfo, "CONTENT_ID") != p.ContentId ||
                        Metadata.ReadSfoString(sfo, "CATEGORY") != p.Category || string.IsNullOrWhiteSpace(Metadata.ReadSfoTitle(sfo)) ||
                        (!p.Addon && !SafePaths.IsRegularFile(System.IO.Path.Combine(target, "eboot.bin"))))
                        throw new NexoException("El juego extraído no supera la verificación. Todos los PKG se conservan.");
                }
                if (written != total) throw new NexoException("El tamaño instalado no coincide con el lote.");
                VerifyStage(stage, total, fileCount, ct);
                progress?.Report(written); ct.ThrowIfCancellationRequested();
                // One directory rename commits base, update and DLC together, without overwriting.
                string name = main.TitleId + " - instalado";
                for (int i = 1; i <= 10000; i++)
                {
                    var candidate = System.IO.Path.Combine(sourceFolder, name + (i == 1 ? "" : " (" + i + ")"));
                    if (Directory.Exists(candidate) || File.Exists(candidate)) continue;
                    if (SafePaths.HasReparsePoint(candidate)) throw new NexoException("El destino contiene un enlace.");
                    Directory.Move(stage, candidate); published = candidate; stage = null; break;
                }
                if (published == null) throw new NexoException("No se encuentra un destino libre para instalar el juego.");
                var originals = packages.Select(p => p.Path).ToArray();
                foreach (var p in packages) p.Dispose(); packages.Clear();
                // Cancellation stops before commit. After commit originals only go to the Recycle Bin.
                foreach (var path in originals)
                {
                    try { recycle(path); }
                    catch (Exception ex) { throw new NexoException("El lote está instalado y verificado en " + published + ", pero no se pudo enviar a la Papelera " + System.IO.Path.GetFileName(path) + ". No se borra permanentemente ningún PKG.", ex); }
                }
                return published;
            }
            catch (OperationCanceledException) { throw; }
            catch (NexoException) { throw; }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException || ex is InvalidOperationException || ex is System.Security.Cryptography.CryptographicException)
            { throw new NexoException("No se pudo instalar el PKG de PS4. Los originales se conservan. " + ex.Message, ex); }
            finally
            {
                foreach (var p in packages) p.Dispose();
                if (stage != null) ArchiveExtractor.CleanPackageStage(stage, gamesFolder);
            }
        }

        private static void VerifyStage(string stage, long expectedSize, long expectedCount, CancellationToken ct)
        {
            long size = 0, count = 0; var pending = new Stack<string>(); pending.Push(stage);
            while (pending.Count != 0)
            {
                ct.ThrowIfCancellationRequested(); var folder = pending.Pop();
                if (!SafePaths.Within(folder, stage) || SafePaths.HasReparsePoint(folder)) throw new NexoException("Enlace detectado en el temporal de instalación.");
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    ct.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new NexoException("Enlace detectado en el temporal de instalación.");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                    else { size = checked(size + new FileInfo(path).Length); count++; }
                    if (size > expectedSize || count > expectedCount) throw new NexoException("El temporal contiene archivos inesperados.");
                }
            }
            if (size != expectedSize || count != expectedCount) throw new NexoException("No se pudo verificar todo el lote instalado.");
        }

        internal static string AddonFolder(GameEntry game)
        {
            if (game.SystemId != "PS4") return null;
            string baseDir = System.IO.Path.GetDirectoryName(game.Path), titleId = System.IO.Path.GetFileName(baseDir);
            if (!System.Text.RegularExpressions.Regex.IsMatch(titleId, @"^CUSA[0-9]{5}$")) return null;
            string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(baseDir), "addons");
            return Directory.Exists(System.IO.Path.Combine(root, titleId)) && !SafePaths.HasReparsePoint(root) ? root : null;
        }
        internal static void PrepareAddons(GameEntry game, string executable, Action<ProcessStartInfo> run = null)
        {
            string folder = AddonFolder(game); if (folder == null) return;
            if (!string.Equals(System.IO.Path.GetFileName(executable), "shadPS4.exe", StringComparison.OrdinalIgnoreCase))
                throw new NexoException("Este juego tiene DLC: configura su carpeta addons en el emulador elegido.");
            var info = Launcher.StartInfo(executable, new[] { "--set-addon-folder", folder });
            info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden;
            if (run != null) { run(info); return; }
            // This is a settings-only command. Combining it with -g would exit without launching.
            try
            {
                using (var process = Process.Start(info))
                {
                    if (process == null) throw new NexoException("No se pudo preparar la carpeta de DLC.");
                    if (!process.WaitForExit(10000)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new NexoException("shadPS4 no terminó de preparar la carpeta de DLC."); }
                    if (process.ExitCode != 0) throw new NexoException("shadPS4 rechazó la carpeta de DLC. Los juegos y PKG se conservan.");
                }
            }
            catch (System.ComponentModel.Win32Exception ex) { throw new NexoException("Windows no pudo abrir shadPS4 para preparar los DLC.", ex); }
        }
    }
}
