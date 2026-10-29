using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    internal static class GameScanner
    {
        internal static IReadOnlyList<GameEntry> Scan(SystemProfile system, string folder, string executable, CancellationToken ct,
            int maxDepth = 10, int maxEntries = 50000)
            => ScanDetailed(system, folder, executable, ct, maxDepth, maxEntries).Games;

        internal static GameScanResult ScanDetailed(SystemProfile system, string folder, string executable, CancellationToken ct,
            int maxDepth = 10, int maxEntries = 50000)
        {
            var games = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
            var archives = new List<ArchiveEntry>();
            int ignored = 0;
            var compatibility = new CompatibilityDatabase(system.Info.Id, executable, folder);
            foreach (var file in FileTree.Files(folder, maxDepth, maxEntries, TimeSpan.FromSeconds(8), ct))
            {
                ct.ThrowIfCancellationRequested();
                if (system.Info.Id == "Switch" && SwitchContainerInspector.IsContainer(file))
                {
                    var problem = SwitchContainerInspector.Inspect(file, ct);
                    if (problem != null) { archives.Add(problem); continue; }
                }
                if (ArchiveInspector.IsArchive(system.Info.Id, file))
                {
                    var pending = ArchiveInspector.Inspect(system.Info.Id, file, ct);
                    if (pending != null)
                    {
                        if (system.Info.Id == "PS4") pending = new ArchiveEntry(pending.SystemId, pending.Path, pending.Title, pending.Size,
                            pending.ExpectedSize, pending.State, pending.Message, compatibility.LookupPs4(pending.TitleId, ct), pending.TitleId);
                        archives.Add(pending);
                    }
                    continue;
                }
                var game = Describe(system, file, executable, out var missingCore);
                if (game != null)
                {
                    game = new GameEntry(game.SystemId, game.Title, game.Path, game.CoverPath, compatibility.Lookup(game, ct));
                    try { game = ArtworkPaths.Local(game, folder); }
                    catch (Exception ex) when (ArtworkMetadata.OptionalFailure(ex)) { }
                    games[game.Path] = game;
                }
                else if (missingCore) ignored++;
            }
            ct.ThrowIfCancellationRequested();
            var sorted = Array.AsReadOnly(games.Values.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(g => g.Path, StringComparer.OrdinalIgnoreCase).ToArray());
            return new GameScanResult(sorted, ignored, ignored == 0 ? null :
                "Hay ROMs reconocidas que no se muestran porque falta un núcleo compatible. Coloca RetroArch completo y sus núcleos en la subcarpeta cores junto a retroarch.exe.",
                Array.AsReadOnly(archives.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Path, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        internal static GameEntry Describe(SystemProfile system, string file, string executable)
            => Describe(system, file, executable, out _);

        private static GameEntry Describe(SystemProfile system, string file, string executable, out bool missingCore)
        {
            missingCore = false;
            if (!SafePaths.IsRegularFile(file)) return null;
            string folder = Path.GetDirectoryName(file), title, cover;
            if (system.Info.Id == "PS3")
            {
                if (!string.Equals(Path.GetFileName(file), "EBOOT.BIN", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(Path.GetFileName(folder), "USRDIR", StringComparison.OrdinalIgnoreCase)) return null;
                var content = Path.GetDirectoryName(folder);
                var gameRoot = string.Equals(Path.GetFileName(content), "PS3_GAME", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(content) : content;
                title = Metadata.ReadSfoTitle(Path.Combine(content, "PARAM.SFO")) ?? CleanTitle(Path.GetFileName(gameRoot));
                cover = FolderCover(gameRoot) ?? ExistingImage(Path.Combine(content, "ICON0.PNG"));
            }
            else if (system.Info.Id == "PS4" || system.Info.Id == "PS5")
            {
                if (!string.Equals(Path.GetFileName(file), "eboot.bin", StringComparison.OrdinalIgnoreCase)) return null;
                var metadata = Path.Combine(folder, "sce_sys");
                var json = Path.Combine(metadata, "param.json");
                var sfo = Path.Combine(metadata, "param.sfo");
                if (system.Info.Id == "PS4" && (Path.GetFileName(folder).EndsWith("-UPDATE", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(folder).EndsWith("-patch", StringComparison.OrdinalIgnoreCase) ||
                    Metadata.ReadSfoString(sfo, "CATEGORY") == "gp" || (Metadata.ReadSfoString(sfo, "CATEGORY") ?? "").StartsWith("ac", StringComparison.Ordinal))) return null;
                if (system.Info.Id == "PS5" ? !SafePaths.IsRegularFile(json) : !SafePaths.IsRegularFile(json) && !SafePaths.IsRegularFile(sfo)) return null;
                title = Metadata.ReadJsonTitle(json) ?? Metadata.ReadSfoTitle(sfo) ?? CleanTitle(Path.GetFileName(folder));
                cover = FolderCover(folder) ?? ExistingImage(Path.Combine(metadata, "icon0.png"));
            }
            else
            {
                var container = system.Info.Id == "Xbox360" && Path.GetExtension(file).Length == 0 ? XboxMetadata.Xbox360Container(file) : null;
                if (container == null && !system.Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) return null;
                if (system.Info.Id == "Xbox360" && Path.GetExtension(file).Equals(".xex", StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(file).Equals("default.xex", StringComparison.OrdinalIgnoreCase) && SafePaths.IsRegularFile(Path.Combine(folder, "default.xex"))) return null;
                if (system.Info.Id == "Retro" && RetroCores.Find(executable, file) == null)
                { missingCore = true; return null; }
                title = CleanTitle(Path.GetFileNameWithoutExtension(file));
                cover = Sidecar(folder, Path.GetFileNameWithoutExtension(file));
                if (container != null) title = container.Item2 ?? title;
                if (system.Info.Id == "Xbox360" && Path.GetFileName(file).Equals("default.xex", StringComparison.OrdinalIgnoreCase))
                { title = CleanTitle(Path.GetFileName(folder)); cover = FolderCover(folder) ?? cover; }
            }
            return new GameEntry(system.Info.Id, title, Path.GetFullPath(file), cover);
        }

        internal static string CleanTitle(string value)
        {
            var clean = Regex.Replace(value ?? "", @"\s*(\[[^\]]*\]|\((?:USA|Europe|Japan|World|PAL|NTSC|En|Es|Fr|De|It|Ja|Rev\s+[^)]*|v\d[^)]*|Disc\s+\d+)(?:[, +/][^)]*)?\))", "", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean.Replace('_', ' '), @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(clean) ? (value ?? "Sin título") : clean;
        }

        private static string ExistingImage(string path)
        {
            try { return SafePaths.IsRegularFile(path) && new FileInfo(path).Length <= 32 * 1024 * 1024 ? path : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string Sidecar(string folder, string name)
        {
            foreach (var sub in new[] { "", "covers" })
                foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
                {
                    var found = ExistingImage(Path.Combine(folder, sub, name + extension));
                    if (found != null) return found;
                }
            return null;
        }

        private static string FolderCover(string folder) => Sidecar(folder, Path.GetFileName(folder)) ?? Sidecar(folder, "cover") ??
            Sidecar(Path.GetDirectoryName(folder), Path.GetFileName(folder));
    }

    internal static class Metadata
    {
        private const int MaxBytes = 1024 * 1024;
        private static byte[] ReadSmall(string file)
        {
            if (!SafePaths.IsRegularFile(file)) return null;
            using (var stream = File.OpenRead(file))
            {
                if (stream.Length > MaxBytes) return null;
                var bytes = new byte[(int)stream.Length];
                int offset = 0, read;
                while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) != 0) offset += read;
                return offset == bytes.Length ? bytes : null;
            }
        }

        internal static string ReadSfoTitle(string file) => ReadSfoString(file, "TITLE");

        internal static string ReadSfoString(string file, string field)
        {
            try { return ReadSfoString(ReadSmall(file), field); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return null; }
        }

        internal static string ReadSfoString(byte[] data, string field)
        {
            try
            {
                if (data == null || data.Length < 20 || BitConverter.ToUInt32(data, 0) != 0x46535000) return null;
                long keys = BitConverter.ToUInt32(data, 8), values = BitConverter.ToUInt32(data, 12), count = BitConverter.ToUInt32(data, 16);
                if (count > 4096 || 20 + count * 16 > data.Length || keys < 20 + count * 16 || values < keys || values > data.Length) return null;
                for (int i = 0; i < count; i++)
                {
                    int entry = 20 + i * 16;
                    long key = keys + BitConverter.ToUInt16(data, entry);
                    long length = BitConverter.ToUInt32(data, entry + 4), value = values + BitConverter.ToUInt32(data, entry + 12);
                    if (key < keys || key >= values || value < values || length > MaxBytes || value + length > data.Length) continue;
                    int end = (int)key;
                    while (end < values && data[end] != 0) end++;
                    if (end >= values || BitConverter.ToUInt16(data, entry + 2) != 0x0204 || Encoding.UTF8.GetString(data, (int)key, end - (int)key) != field) continue;
                    return Clean(Encoding.UTF8.GetString(data, (int)value, (int)length));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { }
            return null;
        }

        internal static string ReadJsonTitle(string file)
        {
            try
            {
                var bytes = ReadSmall(file);
                if (bytes == null) return null;
                var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                var data = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 }.DeserializeObject(text);
                return FindTitle(data as Dictionary<string, object>, 0);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException) { return null; }
        }

        private static string FindTitle(Dictionary<string, object> data, int depth)
        {
            if (data == null || depth > 6) return null;
            foreach (var key in new[] { "titleName", "title", "TITLE" })
                if (data.TryGetValue(key, out var value) && value is string title && Clean(title) != null) return Clean(title);
            if (data.TryGetValue("defaultLanguage", out var language) && language is string lang && data.TryGetValue(lang, out var selected))
            {
                var title = FindTitle(selected as Dictionary<string, object>, depth + 1);
                if (title != null) return title;
            }
            foreach (var item in data.Values)
            {
                var title = FindTitle(item as Dictionary<string, object>, depth + 1);
                if (title != null) return title;
            }
            return null;
        }

        private static string Clean(string value)
        {
            var title = Regex.Replace(value.Replace('\0', ' '), @"[\x00-\x1F\x7F]", " ").Trim();
            return title.Length == 0 ? null : title.Substring(0, Math.Min(300, title.Length));
        }
    }
}
