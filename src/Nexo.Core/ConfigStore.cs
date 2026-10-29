using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    internal sealed class SystemSettings
    {
        internal string Executable;
        internal string[] Arguments;
    }

    /// <summary>Opciones de la pantalla de Opciones. Todas son true por defecto: así se comportaba Nexo 1.0.0.</summary>
    public enum NexoOption
    {
        /// <summary>Consultar al abrir si hay una versión nueva de Nexo («checkUpdates»).</summary>
        CheckUpdates,
        /// <summary>Buscar portadas y fondos en Internet («downloadArtwork», ya existía en 1.0.0).</summary>
        DownloadArtwork,
        /// <summary>Sonidos de la interfaz («sounds»).</summary>
        Sounds,
        /// <summary>Preparar los gráficos del emulador al abrirlo («autoGraphics»).</summary>
        AutoGraphics,
        /// <summary>Preparar los mandos del emulador al abrirlo («autoControllers»).</summary>
        AutoControllers,
        /// <summary>Añadir las opciones de pantalla completa al abrir un juego («fullscreenGames»).</summary>
        FullscreenGames,
    }

    internal sealed class ConfigStore
    {
        private sealed class Data
        {
            internal Dictionary<string, SystemSettings> Systems = new Dictionary<string, SystemSettings>(StringComparer.Ordinal);
            internal readonly Dictionary<NexoOption, bool> Options = new Dictionary<NexoOption, bool>();
            internal string GamesRoot, EmulatorsRoot;
        }

        internal const string GamesRootKey = "gamesRoot", EmulatorsRootKey = "emulatorsRoot";
        private static readonly Dictionary<NexoOption, string> OptionKeys = new Dictionary<NexoOption, string>
        {
            [NexoOption.CheckUpdates] = "checkUpdates",
            [NexoOption.DownloadArtwork] = "downloadArtwork",
            [NexoOption.Sounds] = "sounds",
            [NexoOption.AutoGraphics] = "autoGraphics",
            [NexoOption.AutoControllers] = "autoControllers",
            [NexoOption.FullscreenGames] = "fullscreenGames",
        };
        internal static string KeyOf(NexoOption option) => OptionKeys[option];

        private readonly string file;
        private readonly object gate = new object();
        internal ConfigStore(string root) { file = Path.Combine(root, "nexo.json"); Read(); }

        internal bool DownloadArtwork => Option(NexoOption.DownloadArtwork);

        internal bool Option(NexoOption option)
        { lock (gate) { return Read().Options.TryGetValue(option, out var value) ? value : true; } }

        /// <summary>Carpeta raíz de juegos elegida por el usuario, o null para la predeterminada (Juegos dentro de la instalación).</summary>
        internal string GamesRoot { get { lock (gate) return Read().GamesRoot; } }
        internal string EmulatorsRoot { get { lock (gate) return Read().EmulatorsRoot; } }

        private Data Read()
        {
            var result = new Data();
            if (!File.Exists(file)) return result;
            try
            {
                if (!SafePaths.IsRegularFile(file) || new FileInfo(file).Length > 1024 * 1024) throw new FormatException();
                var serializer = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 32 };
                var data = serializer.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (data == null || !data.TryGetValue("version", out var version) || !(version is int) || (int)version != 1 ||
                    !data.TryGetValue("systems", out var raw) || !(raw is Dictionary<string, object> systems)) throw new FormatException();
                foreach (var key in data.Keys)
                    if (key != "version" && key != "systems" && key != GamesRootKey && key != EmulatorsRootKey && !OptionKeys.ContainsValue(key)) throw new FormatException();
                foreach (var pair in OptionKeys)
                    if (data.TryGetValue(pair.Value, out var value))
                    { if (!(value is bool)) throw new FormatException(); result.Options[pair.Key] = (bool)value; }
                result.GamesRoot = RootValue(data, GamesRootKey);
                result.EmulatorsRoot = RootValue(data, EmulatorsRootKey);
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
                    result.Systems.Add(pair.Key, settings);
                }
                return result;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is NexoException)
            { throw new NexoException("No se pudo leer nexo.json. Se conserva sin sobrescribir; revisa su formato y permisos.", ex); }
        }

        // null o ausente: carpeta predeterminada. Solo rutas absolutas de unidad local; la disponibilidad se comprueba al usarla.
        private static string RootValue(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null) return null;
            if (!(value is string text)) throw new FormatException();
            return SafePaths.Full(text);
        }

        internal SystemSettings Get(string id)
        {
            lock (gate) { var settings = Read().Systems; return settings.TryGetValue(id, out var entry) ? entry : new SystemSettings(); }
        }

        internal void SetExecutable(string id, string path)
        {
            Catalog.Get(id);
            lock (gate)
            {
                var all = Read();
                if (!all.Systems.TryGetValue(id, out var entry)) all.Systems[id] = entry = new SystemSettings();
                entry.Executable = path;
                Write(all);
            }
        }

        internal void SetOption(NexoOption option, bool value)
        {
            if (!OptionKeys.ContainsKey(option)) throw new NexoException("Opción desconocida.");
            lock (gate) { var all = Read(); all.Options[option] = value; Write(all); }
        }

        /// <summary>Guarda las carpetas raíz (null = predeterminada). Las rutas ya vienen validadas por NexoService.</summary>
        internal void SetRoots(string gamesRoot, string emulatorsRoot)
        {
            lock (gate) { var all = Read(); all.GamesRoot = gamesRoot; all.EmulatorsRoot = emulatorsRoot; Write(all); }
        }

        private void Write(Data all)
        {
            var systems = new Dictionary<string, object>();
            string root = Path.GetDirectoryName(file);
            foreach (var pair in all.Systems)
            {
                string executable = pair.Value.Executable;
                if (executable != null && SafePaths.Within(executable, Path.Combine(root, "Emuladores"))) executable = executable.Substring(root.Length + 1).Replace('\\', '/');
                var item = new Dictionary<string, object> { ["executable"] = executable };
                if (pair.Value.Arguments != null) item["gameArguments"] = pair.Value.Arguments;
                systems[pair.Key] = item;
            }
            // downloadArtwork se escribe siempre, como en 1.0.0. Las claves nuevas solo cuando difieren del valor
            // predeterminado: un nexo.json con los valores de fábrica sigue siendo legible por Nexo 1.0.0.
            var json = new Dictionary<string, object> { ["version"] = 1, ["systems"] = systems,
                ["downloadArtwork"] = !all.Options.TryGetValue(NexoOption.DownloadArtwork, out var art) || art };
            foreach (var pair in OptionKeys.Where(p => p.Key != NexoOption.DownloadArtwork))
                if (all.Options.TryGetValue(pair.Key, out var value) && !value) json[pair.Value] = false;
            if (all.GamesRoot != null) json[GamesRootKey] = all.GamesRoot;
            if (all.EmulatorsRoot != null) json[EmulatorsRootKey] = all.EmulatorsRoot;
            var text = new JavaScriptSerializer().Serialize(json);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(text);
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
