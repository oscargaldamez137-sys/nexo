using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    internal sealed class SystemSettings
    {
        internal string Executable;
        internal string[] Arguments;
    }

    internal sealed class ConfigStore
    {
        private readonly string file;
        private readonly object gate = new object();
        private bool downloadArtwork = true;
        internal bool DownloadArtwork { get { lock (gate) { Read(); return downloadArtwork; } } }
        internal ConfigStore(string root) { file = Path.Combine(root, "nexo.json"); Read(); }

        private Dictionary<string, SystemSettings> Read()
        {
            downloadArtwork = true;
            if (!File.Exists(file)) return new Dictionary<string, SystemSettings>(StringComparer.Ordinal);
            try
            {
                if (!SafePaths.IsRegularFile(file) || new FileInfo(file).Length > 1024 * 1024) throw new FormatException();
                var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
                var data = serializer.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (data == null || !data.TryGetValue("version", out var version) || !(version is int) || (int)version != 1 ||
                    !data.TryGetValue("systems", out var raw) || !(raw is Dictionary<string, object> systems)) throw new FormatException();
                foreach (var key in data.Keys) if (key != "version" && key != "systems" && key != "downloadArtwork") throw new FormatException();
                if (data.TryGetValue("downloadArtwork", out var artwork))
                { if (!(artwork is bool)) throw new FormatException(); downloadArtwork = (bool)artwork; }
                var result = new Dictionary<string, SystemSettings>(StringComparer.Ordinal);
                foreach (var pair in systems)
                {
                    Catalog.Get(pair.Key);
                    var entry = pair.Value as Dictionary<string, object>;
                    if (entry == null) throw new FormatException();
                    var settings = new SystemSettings();
                    foreach (var item in entry)
                    {
                        if (item.Key == "executable")
                        {
                            if (item.Value != null && !(item.Value is string)) throw new FormatException();
                            settings.Executable = (string)item.Value;
                            if (settings.Executable != null)
                            {
                                if (settings.Executable.StartsWith("Emuladores/", StringComparison.Ordinal) || settings.Executable.StartsWith("Emuladores\\", StringComparison.Ordinal))
                                {
                                    var relative = settings.Executable.Replace('\\', '/');
                                    ArchiveExtractor.ValidateName(relative);
                                    settings.Executable = SafePaths.Full(Path.Combine(Path.GetDirectoryName(file), relative.Replace('/', '\\')));
                                }
                                else SafePaths.Full(settings.Executable);
                            }
                        }
                        else if (item.Key == "gameArguments")
                        {
                            if (!(item.Value is object[] args) || args.Length > 64) throw new FormatException();
                            var list = new List<string>();
                            foreach (var arg in args)
                            {
                                if (!(arg is string text) || text.Length > 32768 || text.IndexOf('\0') >= 0) throw new FormatException();
                                list.Add(text);
                            }
                            settings.Arguments = list.ToArray();
                        }
                        else throw new FormatException();
                    }
                    result.Add(pair.Key, settings);
                }
                return result;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is NexoException)
            { throw new NexoException("No se pudo leer nexo.json. Se conserva sin sobrescribir; revisa su formato y permisos.", ex); }
        }

        internal SystemSettings Get(string id)
        {
            lock (gate) { var settings = Read(); return settings.TryGetValue(id, out var entry) ? entry : new SystemSettings(); }
        }

        internal void SetExecutable(string id, string path)
        {
            Catalog.Get(id);
            lock (gate)
            {
                var all = Read();
                if (!all.TryGetValue(id, out var entry)) all[id] = entry = new SystemSettings();
                entry.Executable = path;
                var systems = new Dictionary<string, object>();
                foreach (var pair in all)
                {
                    string executable = pair.Value.Executable;
                    string root = Path.GetDirectoryName(file);
                    if (executable != null && SafePaths.Within(executable, Path.Combine(root, "Emuladores"))) executable = executable.Substring(root.Length + 1).Replace('\\', '/');
                    var item = new Dictionary<string, object> { ["executable"] = executable };
                    if (pair.Value.Arguments != null) item["gameArguments"] = pair.Value.Arguments;
                    systems[pair.Key] = item;
                }
                var json = new JavaScriptSerializer().Serialize(new Dictionary<string, object> { ["version"] = 1, ["systems"] = systems, ["downloadArtwork"] = downloadArtwork });
                var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var bytes = new UTF8Encoding(false).GetBytes(json);
                        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                    }
                    if (File.Exists(file)) File.Replace(temp, file, null);
                    else File.Move(temp, file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { throw new NexoException("No se pudo guardar nexo.json. Comprueba los permisos de la instalación.", ex); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
    }
}
