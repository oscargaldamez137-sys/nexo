using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexo.Core
{
    // Deliberately not a permissive INI/YAML parser: ambiguous files are left untouched.
    // Only the emulator-generated subset is edited; original bytes survive a no-op.
    internal sealed class ControllerConfig
    {
        private readonly string path;
        private readonly byte[] original;
        private readonly Encoding encoding;
        private readonly string newline;
        private readonly List<string> lines;
        private readonly bool toml;
        internal bool Exists => original != null;
        internal bool HasMgbaPadSettings => lines.Any(l => l.TrimStart().StartsWith("[gba.input.SDL", StringComparison.Ordinal) || l.TrimStart().StartsWith("[gba.input-profile.", StringComparison.Ordinal));
        internal string ReadPreamble(string key)
        {
            string value = null;
            foreach (string line in lines)
            {
                string s = line.Trim(); if (s.StartsWith("[")) break;
                if (s.StartsWith("#") || s.StartsWith(";")) continue;
                int eq = s.IndexOf('='); if (eq < 0 || s.Substring(0, eq).Trim() != key) continue;
                if (value != null) throw new IOException("Ajuste global duplicado."); value = s.Substring(eq + 1).Trim();
            }
            return value;
        }
        internal ControllerConfig(string path, bool toml = false)
        {
            this.path = path;
            this.toml = toml;
            if (SafePaths.HasReparsePoint(path)) throw new IOException("La configuración es un enlace.");
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new IOException("La configuración es demasiado grande.");
                original = File.ReadAllBytes(path);
            }
            encoding = original != null && original.Length >= 2 && original[0] == 255 && original[1] == 254
                ? (Encoding)new UnicodeEncoding(false, true, true)
                : new UTF8Encoding(original != null && original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191, true);
            string text = original == null ? "" : encoding.GetString(original).TrimStart('\ufeff');
            newline = text.Contains("\r\n") || !text.Contains("\n") ? "\r\n" : "\n";
            lines = text.Length == 0 ? new List<string>() : text.Split(new[] { newline }, StringSplitOptions.None).ToList();
            if (toml) foreach (string line in lines)
            {
                string clean = TomlCode(line).Trim();
                if (clean.StartsWith("[") && (clean.Contains("\"") || clean.Contains("'")))
                    throw new IOException("Tabla TOML con nombre entre comillas; se conserva sin cambios.");
            }
        }

        // Xenia appends help comments to every scalar it writes. Only strip comments
        // outside strings; keep quoted '#' and escapes, and refuse multiline ambiguity.
        private static string TomlCode(string line)
        {
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote == '"' && c == '\\') { i++; continue; }
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '#') return line.Substring(0, i);
                if (c == '"' || c == '\'')
                {
                    if (i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c)
                        throw new IOException("TOML con cadenas multilínea; se conserva sin cambios.");
                    quote = c;
                }
            }
            if (quote != '\0') throw new IOException("Cadena TOML incompleta; se conserva sin cambios.");
            return line;
        }

        private bool Section(string section, out int start, out int end)
        {
            start = -1; end = lines.Count;
            if (section.Length == 0)
            {
                if (lines.Any(l => l.TrimStart().StartsWith("#include", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith("[")))
                    throw new IOException("El archivo plano contiene inclusiones o secciones; se conserva.");
                return true;
            }
            for (int i = 0; i < lines.Count; i++)
            {
                var text = (toml ? TomlCode(lines[i]) : lines[i]).Trim();
                if (text == "[" + section + "]")
                {
                    if (start >= 0) throw new IOException("Hay secciones duplicadas; se conserva la configuración.");
                    start = i;
                }
                else if (start >= 0 && end == lines.Count && text.StartsWith("[")) end = i;
            }
            return start >= 0;
        }

        internal Dictionary<string, string> ReadSection(string section)
        {
            if (!Section(section, out int start, out int end)) return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = start + 1; i < end; i++)
            {
                string text = (toml ? TomlCode(lines[i]) : lines[i]).Trim();
                if (text.Length == 0 || text.StartsWith(";") || text.StartsWith("#")) continue;
                int eq = text.IndexOf('=');
                if (eq < 1) throw new IOException("Formato de controles desconocido; se conserva.");
                string key = text.Substring(0, eq).Trim(), value = text.Substring(eq + 1).Trim();
                if (toml)
                {
                    if (key.IndexOfAny(new[] { '"', '\'', '.' }) >= 0)
                        throw new IOException("Clave TOML no escalar; se conserva sin cambios.");
                    if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                        value = value.Substring(1, value.Length - 2);
                }
                if (result.ContainsKey(key)) throw new IOException("Hay controles duplicados; se conservan.");
                result.Add(key, value);
            }
            return result;
        }

        internal void SetSection(string section, IDictionary<string, string> values)
        {
            // Also validate duplicated keys before changing anything.
            ReadSection(section);
            if (!Section(section, out int start, out int end))
            { if (lines.Count != 0 && lines.Last().Length != 0) lines.Add(""); lines.Add("[" + section + "]"); start = lines.Count - 1; end = lines.Count; }
            var remaining = new Dictionary<string, string>(values, StringComparer.Ordinal);
            for (int i = start + 1; i < end; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq < 1) continue;
                string key = lines[i].Substring(0, eq).Trim();
                if (remaining.TryGetValue(key, out var value))
                {
                    string code = toml ? TomlCode(lines[i]) : lines[i];
                    string comment = code.Length < lines[i].Length ? " " + lines[i].Substring(code.Length) : "";
                    lines[i] = key + " = " + value + comment; remaining.Remove(key);
                }
            }
            lines.InsertRange(end, remaining.Select(p => p.Key + " = " + p.Value));
        }

        // Las versiones anteriores de Nexo escribían «Device: XInput Pad #1» sin comillas, y en YAML
        // « #» empieza un comentario: RPCS3 leía «XInput Pad» y no encontraba el mando. Se repara solo
        // esa línea exacta escrita por Nexo; cualquier otra configuración se respeta.
        internal bool RepairPs3UnquotedDevice()
        {
            int start = lines.FindIndex(l => l == "Player 1 Input:");
            if (start < 0) return false;
            bool xinput = false;
            for (int i = start + 1; i < lines.Count && (lines[i].Length == 0 || char.IsWhiteSpace(lines[i][0])); i++)
            {
                if (lines[i] == "  Handler: XInput") xinput = true;
                var m = Regex.Match(lines[i], @"^  Device: XInput Pad #([1-4])$");
                if (xinput && m.Success)
                {
                    lines[i] = "  Device: \"XInput Pad #" + m.Groups[1].Value + "\"";
                    return true;
                }
            }
            return false;
        }

        internal bool ReplacePs3Keyboard(int index)
        {
            int start = lines.FindIndex(l => l == "Player 1 Input:");
            if (Exists && start < 0) return false;
            int end = start < 0 ? 0 : start + 1;
            while (end < lines.Count && (lines[end].Length == 0 || char.IsWhiteSpace(lines[end][0]) || lines[end].TrimStart().StartsWith("#"))) end++;
            if (Exists)
            {
                if (lines.Count(l => l == "Player 1 Input:") != 1) return false;
                var defaults = ControllerProfiles.Ps3Defaults;
                var seen = new HashSet<string>();
                bool config = false; string sensor = null;
                for (int i = start + 1; i < end; i++)
                {
                    var line = lines[i]; if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var m = Regex.Match(line, @"^( {6}| {4}| {2})([^ :][^:]*):(?: (.*))?$");
                    if (!m.Success) return false;
                    int indent = m.Groups[1].Length;
                    var key = m.Groups[2].Value; var value = m.Groups[3].Value;
                    if (value == "\"\"" || value == "''") value = "";
                    // Arbitrary YAML quoting, aliases, tags, anchors, flow maps are never rewritten.
                    if (value.IndexOfAny(new[] { '"', '\'', '&', '*', '!', '{', '}' }) >= 0) return false;
                    if (indent == 2)
                    {
                        sensor = null; config = key == "Config";
                        if (!seen.Add(key)) return false;
                        if (key == "Handler" || key == "Device") { if (value != "Keyboard") return false; }
                        else if (key == "Buddy Device") { if (value != "" && value != "Keyboard") return false; }
                        else if (key != "Config" || value != "") return false;
                    }
                    else if (indent == 4 && config)
                    {
                        sensor = null;
                        if (!seen.Add("Config/" + key)) return false;
                        if (new[] { "Motion Sensor X", "Motion Sensor Y", "Motion Sensor Z", "Motion Sensor G" }.Contains(key) && value == "") sensor = key;
                        else if (!defaults.TryGetValue(key, out var expected) || value != expected) return false;
                    }
                    else if (indent == 6 && sensor != null)
                    {
                        if (!seen.Add(sensor + "/" + key)) return false;
                        if (!(key == "Axis" && value == "" || key == "Mirrored" && value == "false" || key == "Shift" && value == "0")) return false;
                    }
                    else return false;
                }
                if (!seen.Contains("Handler") || !seen.Contains("Device")) return false;
                if (ControllerProfiles.Ps3Keyboard.Keys.Any(key => !seen.Contains("Config/" + key))) return false;
            }
            // Entre comillas: en YAML « #» empieza un comentario y RPCS3 leería solo «XInput Pad».
            var replacement = new List<string> { "Player 1 Input:", "  Handler: XInput", "  Device: \"XInput Pad #" + (index + 1) + "\"", "  Config:" };
            replacement.AddRange(ControllerProfiles.Ps3Pad.Select(p => "    " + p.Key + ": " + p.Value));
            replacement.Add("  Buddy Device: \"\"");
            if (start < 0) lines.InsertRange(0, replacement);
            else { lines.RemoveRange(start, end - start); lines.InsertRange(start, replacement); }
            return true;
        }

        // RPCS3's global graphics use scalar entries in the Video mapping. Nested mappings
        // (Vulkan/D3D etc.) remain verbatim; aliases and duplicate keys are not rewritten.
        private bool YamlSection(string section, out int start, out int end)
        {
            start = -1; end = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i];
                if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("#")) continue;
                if (text.Contains("\t") || text.TrimStart().StartsWith("<<:")) throw new IOException("YAML ambiguo; se conserva.");
                if (char.IsWhiteSpace(text[0])) continue;
                var match = Regex.Match(text, @"^([^:]+):(?:\s.*)?$");
                if (!match.Success) throw new IOException("Formato YAML no reconocido; se conserva.");
                if (match.Groups[1].Value == section)
                {
                    if (start >= 0 || text.TrimEnd() != section + ":") throw new IOException("Sección YAML duplicada o no reconocida.");
                    start = i;
                }
                else if (start >= 0 && end == lines.Count) end = i;
            }
            return start >= 0;
        }

        internal Dictionary<string, string> ReadYamlScalars(string section)
        {
            var result = new Dictionary<string, string>();
            if (!YamlSection(section, out var start, out var end)) return result;
            for (int i = start + 1; i < end; i++)
            {
                var text = lines[i];
                if (string.IsNullOrWhiteSpace(text) || text.TrimStart().StartsWith("#")) continue;
                if (text.StartsWith("    ")) continue;
                var m = Regex.Match(text, @"^  ([^ :][^:]*):(?: (.*))?$");
                if (!m.Success || result.ContainsKey(m.Groups[1].Value)) throw new IOException("Propiedad YAML ambigua; se conserva.");
                result.Add(m.Groups[1].Value, m.Groups[2].Value);
            }
            return result;
        }

        internal void SetYamlScalars(string section, IDictionary<string, string> values)
        {
            ReadYamlScalars(section);
            if (!YamlSection(section, out var start, out var end))
            { lines.Add(section + ":"); start = lines.Count - 1; end = lines.Count; }
            var remaining = new Dictionary<string, string>(values);
            for (int i = start + 1; i < end; i++)
            {
                var match = Regex.Match(lines[i], @"^  ([^ :][^:]*):(?: (.*))?$");
                if (match.Success && remaining.TryGetValue(match.Groups[1].Value, out var value))
                { lines[i] = "  " + match.Groups[1].Value + ": " + value; remaining.Remove(match.Groups[1].Value); }
            }
            lines.InsertRange(end, remaining.Select(p => "  " + p.Key + ": " + p.Value));
        }

        internal bool Save()
        {
            if (lines.Count == 0 || lines.Last().Length != 0) lines.Add("");
            byte[] data = encoding.GetPreamble().Concat(encoding.GetBytes(string.Join(newline, lines))).ToArray();
            if (original != null && data.SequenceEqual(original)) return false;
            if (SafePaths.HasReparsePoint(path)) throw new IOException("La configuración es un enlace.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".nexo-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data, 0, data.Length); stream.Flush(true); }
                if (original != null)
                {
                    if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new IOException("El emulador cambió la configuración mientras Nexo la preparaba.");
                    File.Replace(temporary, path, null);
                }
                else File.Move(temporary, path);
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
