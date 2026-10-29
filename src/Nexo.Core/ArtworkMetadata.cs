using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nexo.Core
{
    internal static class ArtworkMetadata
    {
        internal static Tuple<string, string> NintendoDisc(string path)
        {
            try
            {
                if (!SafePaths.IsRegularFile(path)) return null;
                using (var stream = File.OpenRead(path))
                {
                    byte[] first = Read(stream, 0, 32); long offset = 0;
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext == ".rvz" || ext == ".wia")
                    {
                        string magic = ext == ".rvz" ? "RVZ\x01" : "WIA\x01";
                        if (Encoding.ASCII.GetString(first, 0, 4) != magic) return null;
                        // WIA/RVZ: 0x48 file header + four u32 fields, then uncompressed disc header.
                        offset = 0x58;
                    }
                    else if (ext == ".wbfs")
                    {
                        if (Encoding.ASCII.GetString(first, 0, 4) != "WBFS" || first[8] < 9 || first[8] > 20 ||
                            first[9] < 15 || first[9] > 31 || first[12] == 0) return null;
                        offset = 1L << first[8]; // first slot's disc-info copy of the header
                    }
                    else if (ext == ".ciso")
                    {
                        uint block = BitConverter.ToUInt32(first, 4);
                        if (Encoding.ASCII.GetString(first, 0, 4) != "CISO" || block < 32768 || block > 64 * 1024 * 1024 || first[8] != 1) return null;
                        offset = 0x8000;
                    }
                    else if (ext != ".iso" && ext != ".gcm") return null;
                    var header = offset == 0 ? first : Read(stream, offset, 32);
                    string id = Encoding.ASCII.GetString(header, 0, 6);
                    if (!Regex.IsMatch(id, "^[A-Z0-9]{6}$")) return null;
                    string platform = Big32(header, 24) == 0x5d1c9ea3 ? "Wii" : Big32(header, 28) == 0xc2339f3d ? "GameCube" : null;
                    return platform == null ? null : Tuple.Create(id, platform);
                }
            }
            catch (Exception ex) when (OptionalFailure(ex)) { return null; }
        }

        internal static string Ps2Serial(string path, CancellationToken ct)
        {
            if (!string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase)) return null;
            var data = Ps3IsoInspector.ReadRootFile(path, "SYSTEM.CNF", 65536, ct);
            if (data == null) return null;
            var matches = Regex.Matches(Encoding.ASCII.GetString(data), @"(?im)^[ \t]*BOOT2[ \t]*=[ \t]*cdrom0?:[\\/]*([A-Z]{4})[_-]([0-9]{3})\.([0-9]{2});1[ \t\r]*$");
            if (matches.Count != 1) return null;
            var m = matches[0]; return (m.Groups[1].Value + "-" + m.Groups[2].Value + m.Groups[3].Value).ToUpperInvariant();
        }

        internal static bool OptionalFailure(Exception ex) => ex is IOException || ex is UnauthorizedAccessException ||
            ex is ArgumentException || ex is OverflowException || ex is NotSupportedException || ex is System.Security.SecurityException;
        private static uint Big32(byte[] bytes, int at) => (uint)bytes[at] << 24 | (uint)bytes[at + 1] << 16 | (uint)bytes[at + 2] << 8 | bytes[at + 3];
        private static byte[] Read(Stream stream, long offset, int length)
        {
            if (offset < 0 || length < 0 || offset > stream.Length - length) throw new IOException("Cabecera incompleta.");
            var result = new byte[length]; stream.Position = offset;
            for (int n = 0; n < length;) { int got = stream.Read(result, n, length - n); if (got == 0) throw new EndOfStreamException(); n += got; }
            return result;
        }
    }

    internal sealed class ArtworkPaths
    {
        internal string BasePath { get; }
        internal string ContentFolder { get; }
        internal string Stem { get; }
        private readonly string gameFolder, gamesFolder;
        private readonly bool lowerCaseContent;
        internal ArtworkPaths(GameEntry game, string gamesFolder)
        {
            this.gamesFolder = gamesFolder = Path.GetFullPath(gamesFolder).TrimEnd('\\', '/');
            lowerCaseContent = game.SystemId == "PS4" || game.SystemId == "PS5";
            string container = game.Path;
            if (game.SystemId == "PS3")
            {
                ContentFolder = Path.GetDirectoryName(Path.GetDirectoryName(game.Path));
                container = Path.GetFileName(ContentFolder).Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(ContentFolder) : ContentFolder;
            }
            else if (game.SystemId == "PS4" || game.SystemId == "PS5")
            { container = Path.GetDirectoryName(game.Path); ContentFolder = Path.Combine(container, "sce_sys"); }
            else if (game.SystemId == "Xbox360" && Path.GetFileName(game.Path).Equals("default.xex", StringComparison.OrdinalIgnoreCase))
            { container = Path.GetDirectoryName(game.Path); ContentFolder = container; }
            bool folderGame = ContentFolder != null;
            gameFolder = folderGame ? container : Path.GetDirectoryName(container);
            Stem = folderGame ? Path.GetFileName(container) : Path.GetFileNameWithoutExtension(container);
            // Mirror parent subfolders to avoid collisions between identically named editions.
            string parent = Path.GetDirectoryName(container);
            string relative = SafePaths.Within(parent, gamesFolder) ? parent.Substring(gamesFolder.Length).TrimStart('\\', '/') : "";
            BasePath = Path.Combine(gamesFolder, "covers", relative, Stem);
            if (!SafePaths.Within(Path.GetFullPath(BasePath), gamesFolder)) throw new IOException();
        }
        internal string Find(bool background)
        {
            if (background) return FindBackground();
            string suffix = background ? ".bg" : "";
            // User sidecars take precedence over downloaded files and embedded artwork.
            foreach (string stem in new[] { Path.Combine(gameFolder, Stem), Path.Combine(gameFolder, "covers", Stem), BasePath })
            {
                var found = FindImage(stem + suffix); if (found != null) return found;
            }
            if (ContentFolder != null)
            {
                string custom = FindImage(Path.Combine(gameFolder, "cover") + suffix) ?? FindImage(Path.Combine(gameFolder, "covers", "cover") + suffix);
                if (custom != null) return custom;
            }
            if (ContentFolder != null)
                foreach (string name in background ? new[] { "PIC1.PNG", "PIC0.PNG" } : new[] { "ICON0.PNG" })
                { string found = Existing(Path.Combine(ContentFolder, lowerCaseContent ? name.ToLowerInvariant() : name)); if (found != null) return found; }
            return null;
        }
        internal string UserBackground()
        {
            foreach (string stem in new[] { Path.Combine(gameFolder, Stem), Path.Combine(gameFolder, "cover"),
                Path.Combine(gameFolder, "covers", Stem), Path.Combine(gameFolder, "covers", "cover") })
            {
                if (string.Equals(stem, BasePath, StringComparison.OrdinalIgnoreCase)) continue;
                string found = FindHd(stem + ".bg"); if (found != null) return found;
            }
            return null;
        }
        internal string OwnBackground()
        {
            if (ContentFolder != null)
                foreach (string name in new[] { "PIC1.PNG", "PIC0.PNG" })
                {
                    string path = Path.Combine(ContentFolder, lowerCaseContent ? name.ToLowerInvariant() : name);
                    if (ArtworkHeader.IsHd(path)) return path;
                }
            return FindHd(BasePath + ".embedded.bg.hd") ?? FindHd(BasePath + ".embedded.bg");
        }
        internal string FindBackground(bool generated = true) => UserBackground() ?? OwnBackground() ??
            FindHd(BasePath + ".bg.hd") ?? FindHd(BasePath + ".bg") ?? (generated ? FindHd(BasePath + ".ai") : null);
        internal static string FindHd(string stem)
        {
            foreach (string ext in new[] { ".png", ".jpg", ".jpeg" })
                if (ArtworkHeader.IsHd(stem + ext)) return stem + ext;
            return null;
        }
        internal static string FindImage(string stem)
        {
            foreach (string ext in new[] { ".png", ".jpg", ".jpeg" })
            { string found = Existing(stem + ext); if (found != null) return found; }
            return null;
        }
        internal static string Existing(string path)
        {
            try { return path != null && SafePaths.IsRegularFile(path) && new FileInfo(path).Length <= 32 * 1024 * 1024 ? path : null; }
            catch (Exception ex) when (ArtworkMetadata.OptionalFailure(ex)) { return null; }
        }
        internal static GameEntry Local(GameEntry game, string gamesFolder)
        {
            var paths = new ArtworkPaths(game, gamesFolder);
            var cover = paths.Find(false) ?? (game.CoverPath != null && SafePaths.Within(Path.GetFullPath(game.CoverPath), gamesFolder) ? Existing(game.CoverPath) : null);
            string platform = game.SystemId == "GameCube-Wii" ? ArtworkMetadata.NintendoDisc(game.Path)?.Item2 : null;
            return new GameEntry(game.SystemId, game.Title, game.Path, cover, game.Compatibility, paths.Find(true), platform);
        }
    }
}
