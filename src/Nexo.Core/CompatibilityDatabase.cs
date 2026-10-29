using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    // One bounded, lazy snapshot per scan, never a database parse per game. No network,
    // emulator launches, per-game configuration writes or extraction during browsing.
    internal sealed class CompatibilityDatabase
    {
        private const int MaxBytes = 16 * 1024 * 1024;
        private readonly string systemId, emulatorFolder, gamesFolder;
        private Dictionary<string, GameCompatibility> ps4;
        private Dictionary<string, object> ps3;
        private Dictionary<string, int> ps2;
        private bool loaded;
        internal CompatibilityDatabase(string systemId, string executable, string gamesFolder = null)
        { this.systemId = systemId; this.gamesFolder = gamesFolder; emulatorFolder = executable == null ? null : Path.GetDirectoryName(executable); }

        internal GameCompatibility Lookup(GameEntry game, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (systemId == "PS4") return LookupPs4(Metadata.ReadSfoString(Path.Combine(Path.GetDirectoryName(game.Path), "sce_sys", "param.sfo"), "TITLE_ID"), ct);
            if (systemId == "Xbox" && gamesFolder != null)
            {
                if (ps4 == null) ps4 = XboxCompatibility.Read(gamesFolder, ct);
                string titleId = XboxMetadata.XboxTitleId(game.Path, ct);
                return titleId != null && ps4.TryGetValue(titleId, out var value) ? value : GameCompatibility.Unknown;
            }
            if (emulatorFolder == null || systemId != "PS3" && systemId != "PS2") return GameCompatibility.Unknown;
            try
            {
                if (!loaded)
                {
                    loaded = true;
                    if (systemId == "PS3")
                    {
                        string text = ReadText(Path.Combine(emulatorFolder, "GuiConfigs", "compat_database.dat"));
                        if (text != null)
                        {
                            var data = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 32 }.DeserializeObject(text) as Dictionary<string, object>;
                            if (data != null && data.TryGetValue("return_code", out var code) && code is int n && n >= 0 && data.TryGetValue("results", out var results))
                                ps3 = results as Dictionary<string, object>;
                        }
                    }
                    else
                    {
                        string text = ReadText(Path.Combine(emulatorFolder, "resources", "GameIndex.yaml"));
                        if (text != null) ps2 = ParseGameIndex(text, ct);
                    }
                }
                ct.ThrowIfCancellationRequested();
                if (systemId == "PS3") return LookupPs3(game);
                if (ps2 == null || !string.Equals(Path.GetExtension(game.Path), ".iso", StringComparison.OrdinalIgnoreCase)) return GameCompatibility.Unknown;
                byte[] cnf = Ps3IsoInspector.ReadRootFile(game.Path, "SYSTEM.CNF", 65536, ct);
                if (cnf == null) return GameCompatibility.Unknown;
                var serials = Regex.Matches(Encoding.ASCII.GetString(cnf), @"(?im)^[ \t]*BOOT2[ \t]*=[ \t]*cdrom0?:[\\/]*([A-Z]{4})[_-]([0-9]{3})\.([0-9]{2});1[ \t\r]*$");
                if (serials.Count != 1) return GameCompatibility.Unknown;
                var serial = serials[0];
                string id = (serial.Groups[1].Value + "-" + serial.Groups[2].Value + serial.Groups[3].Value).ToUpperInvariant();
                if (!ps2.TryGetValue(id, out int compat) || compat < 1 || compat > 6) return GameCompatibility.Unknown;
                string status = new[] { "Desconocido", "Nothing", "Intro", "Menu", "Ingame", "Playable", "Perfect" }[compat];
                return new GameCompatibility(status, null, null, Explain(status, "PCSX2"), null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is OverflowException)
            { return GameCompatibility.Unknown; }
        }

        internal GameCompatibility LookupPs4(string id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (systemId != "PS4" || gamesFolder == null || !Ps4Compatibility.ValidId(id)) return GameCompatibility.Unknown;
            if (ps4 == null) ps4 = Ps4CompatibilityCache.Read(gamesFolder, ct);
            return ps4.TryGetValue(id, out var entry) ? entry : GameCompatibility.Unknown;
        }

        private GameCompatibility LookupPs3(GameEntry game)
        {
            if (ps3 == null) return GameCompatibility.Unknown;
            string content = Path.GetDirectoryName(Path.GetDirectoryName(game.Path));
            string sfo = Path.Combine(content, "PARAM.SFO");
            string id = Metadata.ReadSfoString(sfo, "TITLE_ID");
            if (id == null || !Regex.IsMatch(id, @"^[A-Z]{4}[0-9]{5}$") || !ps3.TryGetValue(id, out var raw) || !(raw is Dictionary<string, object> entry))
                return GameCompatibility.Unknown;
            string status = Text(entry, "status");
            if (!new[] { "Playable", "Ingame", "Intro", "Loadable", "Nothing" }.Contains(status)) status = "Desconocido";
            string date = Text(entry, "date");
            if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) date = null;
            string update = Text(entry, "update"), hint = null;
            Version target = ParseVersion(update);
            if (target == null) update = null;
            else
            {
                string installedPath = Path.Combine(emulatorFolder, "dev_hdd0", "game", id, "PARAM.SFO");
                Version installed = ParseVersion(Metadata.ReadSfoString(installedPath, "APP_VER"));
                Version disc = ParseVersion(Metadata.ReadSfoString(sfo, "APP_VER"));
                // Some disc revisions already include an update. Don't report it missing.
                string prefix = "La base de RPCS3 indica la actualización " + DisplayVersion(update);
                if (disc == null || disc < target)
                {
                    if (installed == null)
                        hint = prefix + (File.Exists(installedPath) ? "; no se pudo comprobar la versión instalada." : "; no la tienes instalada.");
                    else if (installed < target) hint = prefix + "; tienes la versión " + installed.Major + "." + installed.Minor.ToString("D2", CultureInfo.InvariantCulture) + ".";
                }
            }
            return new GameCompatibility(status, date, update, Explain(status, "RPCS3"), hint);
        }

        private static string Explain(string status, string emulator)
        {
            string text;
            switch (status)
            {
                case "Playable": text = "Clasificado como jugable; se puede completar con rendimiento jugable."; break;
                case "Perfect": text = "Clasificado sin fallos conocidos."; break;
                case "Ingame": text = "Llega al juego, pero puede tener fallos graves, bloqueos o rendimiento insuficiente para completarlo."; break;
                case "Menu": text = "Llega a los menús, pero no está clasificado como jugable."; break;
                case "Intro": text = "Solo llega a las introducciones o menús; no está clasificado como jugable."; break;
                case "Loadable": text = "Carga, pero no llega a mostrar una imagen utilizable; puede quedarse en negro."; break;
                case "Nothing": text = "No consigue arrancar correctamente."; break;
                default: return GameCompatibility.Unknown.Explanation;
            }
            return text + " Según la base local de " + emulator + "; puede estar desactualizada y no garantiza el resultado en este PC.";
        }

        internal static Dictionary<string, int> ParseGameIndex(string text, CancellationToken ct)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            string current = null;
            bool seenCompat = false, duplicate = false;
            using (var reader = new StringReader(text))
                for (string line; (line = reader.ReadLine()) != null;)
                {
                    ct.ThrowIfCancellationRequested();
                    if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line[0] != '#')
                    {
                        var match = Regex.Match(line, @"^([A-Za-z]{4}-[0-9]{5}):\s*(?:#.*)?$");
                        current = match.Success ? match.Groups[1].Value : null;
                        seenCompat = false;
                        duplicate = current != null && result.ContainsKey(current);
                        if (current != null) result[current] = 0;
                    }
                    else if (current != null && line.StartsWith("  compat:", StringComparison.Ordinal))
                    {
                        var match = Regex.Match(line, @"^  compat:\s*([0-6])\s*(?:#.*)?$");
                        result[current] = !duplicate && !seenCompat && match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                        seenCompat = true;
                    }
                }
            return result;
        }
        private static string Text(Dictionary<string, object> entry, string key) => entry.TryGetValue(key, out var value) ? value as string : null;
        private static Version ParseVersion(string text)
        {
            if (text == null || !Regex.IsMatch(text, @"^[0-9]{1,3}\.[0-9]{1,3}$")) return null;
            return Version.TryParse(text, out var version) ? version : null;
        }
        private static string DisplayVersion(string text) => ParseVersion(text).Major + "." + ParseVersion(text).Minor.ToString("D2", CultureInfo.InvariantCulture);
        private static string ReadText(string path)
        {
            if (!SafePaths.IsRegularFile(path)) return null;
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > MaxBytes) return null;
                var bytes = new byte[(int)stream.Length];
                int used = 0;
                while (used < bytes.Length) { int read = stream.Read(bytes, used, bytes.Length - used); if (read == 0) return null; used += read; }
                return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            }
        }
    }
}
