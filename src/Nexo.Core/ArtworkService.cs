using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexo.Core
{
    internal sealed class ArtworkService
    {
        private static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
        private static readonly SemaphoreSlim Transfers = new SemaphoreSlim(3, 3);
        private static readonly ArtworkDownloader SharedDownloader = new ArtworkDownloader();
        private readonly ArtworkDownloader downloader;
        private readonly Func<DateTime> now;
        internal ArtworkService(ArtworkDownloader downloader = null, Func<DateTime> now = null)
        { this.downloader = downloader ?? SharedDownloader; this.now = now ?? (() => DateTime.UtcNow); }

        internal async Task<GameEntry> EnsureAsync(GameEntry game, string gamesFolder, bool download, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Catalog.Get(game.SystemId);
            string full = SafePaths.Full(game.Path);
            if (!SafePaths.Within(full, gamesFolder) || !SafePaths.IsRegularFile(full)) throw new NexoException("El juego debe existir dentro de la carpeta de juegos de su consola.");
            var gate = Gates[(StringComparer.OrdinalIgnoreCase.GetHashCode(full) & int.MaxValue) % Gates.Length];
            await gate.WaitAsync(ct).ConfigureAwait(false);
            var result = game;
            try
            {
                result = ArtworkPaths.Local(game, gamesFolder);
                var paths = new ArtworkPaths(result, gamesFolder);
                string cover = result.CoverPath, background = paths.FindBackground(false);
                if (game.SystemId == "PSP" && Path.GetExtension(full).Equals(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    if (cover == null) cover = ArtworkPaths.FindImage(paths.BasePath + ".icon") ?? Embedded(full, paths.BasePath + ".icon", "ICON0.PNG", ct);
                    if (paths.UserBackground() == null && paths.OwnBackground() == null)
                        Embedded(full, paths.BasePath + ".embedded.bg", "PIC1.PNG", ct, true);
                    if (paths.UserBackground() == null && paths.OwnBackground() == null)
                        Embedded(full, paths.BasePath + ".embedded.bg", "PIC0.PNG", ct, true);
                    background = paths.FindBackground(false);
                }
                result = Copy(result, cover, background ?? result.BackgroundPath);
                // ICON0 is a small landscape icon, not the box: prefer the real cover online and keep it as fallback.
                bool iconOnly = IsIcon(cover);
                if (download && (cover == null || iconOnly || background == null))
                {
                    var sources = ArtworkSources.For(result, paths, ct);
                    if (cover == null || iconOnly) cover = await Fetch(sources.Covers, paths.BasePath, ct).ConfigureAwait(false) ?? cover;
                    result = Copy(result, cover, background ?? result.BackgroundPath);
                    if (background == null) background = await Fetch(sources.Backgrounds, paths.BasePath + ".bg", ct, true).ConfigureAwait(false);
                    result = Copy(result, cover, background ?? paths.FindBackground());
                }
                ct.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception ex) when (ArtworkMetadata.OptionalFailure(ex)) { return result; }
            finally { gate.Release(); }
        }
        private static bool IsIcon(string path) => path != null
            && (Path.GetFileName(path).Equals("ICON0.PNG", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(path).EndsWith(".icon", StringComparison.OrdinalIgnoreCase));
        private static GameEntry Copy(GameEntry game, string cover, string background) => new GameEntry(game.SystemId,
            game.Title, game.Path, cover, game.Compatibility, background, game.Platform);

        private static string Embedded(string iso, string destination, string name, CancellationToken ct, bool background = false)
        {
            try
            {
                byte[] bytes = Ps3IsoInspector.ReadFile(iso, new[] { "PSP_GAME", name }, ArtworkDownloader.MaxBytes, ct);
                return Publish(destination, bytes, ct, background);
            }
            catch (Exception ex) when (ArtworkMetadata.OptionalFailure(ex)) { return null; }
        }

        private async Task<string> Fetch(IEnumerable<Uri> sources, string destination, CancellationToken ct, bool background = false)
        {
            foreach (var uri in sources)
            {
                ct.ThrowIfCancellationRequested();
                string folder = Path.GetDirectoryName(destination);
                string miss = Path.Combine(folder, ".nexo-art", Hash(uri.AbsoluteUri) + ".miss");
                if (NegativeCached(miss)) continue;
                // Fail locally before using the network if the cache is unwritable/a junction.
                SafeDirectory(folder);
                ArtworkDownload response;
                await Transfers.WaitAsync(ct).ConfigureAwait(false);
                try { response = await downloader.GetAsync(uri, ct).ConfigureAwait(false); }
                finally { Transfers.Release(); }
                ct.ThrowIfCancellationRequested();
                string image = Publish(destination, response.Bytes, ct, background);
                if (image != null) return image;
                WriteNegative(miss, response.NotFound ? TimeSpan.FromDays(7) : TimeSpan.FromMinutes(30));
            }
            return null;
        }
        private bool NegativeCached(string file)
        {
            if (!SafePaths.IsRegularFile(file) || new FileInfo(file).Length > 100) return false;
            if (!long.TryParse(File.ReadAllText(file), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)) return false;
            long time = now().Ticks;
            return ticks > time && ticks <= time + TimeSpan.FromDays(8).Ticks;
        }
        private void WriteNegative(string file, TimeSpan duration)
        {
            SafeDirectory(Path.GetDirectoryName(file));
            if (SafePaths.HasReparsePoint(file)) return;
            Atomic(file, Encoding.ASCII.GetBytes(now().Add(duration).Ticks.ToString(CultureInfo.InvariantCulture)), true);
        }
        internal static string Publish(string destination, byte[] bytes, CancellationToken ct, bool background = false)
        {
            ct.ThrowIfCancellationRequested();
            if (background && !ArtworkHeader.IsHd(bytes)) return null;
            string extension = ArtworkImage.Extension(bytes);
            if (extension == null) return null;
            ct.ThrowIfCancellationRequested();
            SafeDirectory(Path.GetDirectoryName(destination));
            string path = destination + extension;
            // Preserve a user/old low-resolution file. A new HD cache gets a separate suffix.
            if (background && File.Exists(path) && !ArtworkHeader.IsHd(path)) path = destination + ".hd" + extension;
            if (SafePaths.HasReparsePoint(path)) throw new IOException("No se guardan imágenes mediante enlaces.");
            if (!File.Exists(path)) Atomic(path, bytes, false);
            return background ? (ArtworkHeader.IsHd(path) ? path : null) : ArtworkPaths.Existing(path);
        }
        private static void SafeDirectory(string folder)
        {
            if (SafePaths.HasReparsePoint(folder)) throw new IOException("No se guardan imágenes mediante enlaces.");
            Directory.CreateDirectory(folder);
        }
        private static void Atomic(string path, byte[] bytes, bool replace)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (replace && File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }
    }
}
