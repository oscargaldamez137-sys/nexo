using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    // A per-setting receipt, NOT an "already configured" marker. Missing settings can
    // always be recreated. A mismatch relinquishes ownership. Never guess provenance
    // merely because a value happens to match one of our old quality profiles.
    internal sealed class GraphicsHistory
    {
        private readonly string path;
        private readonly byte[] original;
        private readonly Dictionary<string, string> values = new Dictionary<string, string>();
        internal GraphicsHistory(string configPath, bool configExists)
        {
            path = configPath + ".nexo-graphics.json";
            if (SafePaths.HasReparsePoint(path)) throw new IOException("El registro gráfico es un enlace.");
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 65536) throw new IOException("Registro gráfico demasiado grande; se conserva.");
            original = File.ReadAllBytes(path);
            var json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 8 };
            var data = json.DeserializeObject(new UTF8Encoding(false, true).GetString(original)) as Dictionary<string, object>;
            if (data == null || data.Count != 2 || !data.TryGetValue("version", out var version) || !(version is int) || (int)version != 1 ||
                !data.TryGetValue("values", out var raw) || !(raw is Dictionary<string, object> entries) || entries.Count > 64)
                throw new IOException("Registro gráfico no reconocido; se conserva.");
            foreach (var entry in entries)
            {
                if (entry.Key.Length > 256 || !(entry.Value is string text) || text.Length > 256) throw new IOException("Registro gráfico no reconocido.");
                if (configExists) values.Add(entry.Key, text);
            }
        }

        private static string Key(string section, string key) => section + "\n" + key;
        internal int Count => values.Count;
        internal bool Owns(string section, string key, string current)
            => current != null && values.TryGetValue(Key(section, key), out var last) && GraphicsSetup.Equivalent(current, last);
        internal void Forget(string section, string key) => values.Remove(Key(section, key));
        internal void Record(string section, string key, string value) => values[Key(section, key)] = value;

        internal void Save()
        {
            if (original == null && values.Count == 0) return;
            var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { ["version"] = 1, ["values"] = values.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value) });
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            if (original != null && original.SequenceEqual(bytes)) return;
            if (SafePaths.HasReparsePoint(path)) throw new IOException("El registro gráfico es un enlace.");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (original == null) File.Move(temporary, path);
                else
                {
                    if (!File.ReadAllBytes(path).SequenceEqual(original)) throw new IOException("El registro gráfico cambió durante la preparación.");
                    File.Replace(temporary, path, null);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
