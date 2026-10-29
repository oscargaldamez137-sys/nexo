using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexo.Core
{
    public sealed class NexoService
    {
        private const string Games = "Juegos", Emulators = "Emuladores";
        private readonly string root;
        private readonly ConfigStore config;
        private readonly Func<SystemStatus, ControllerSetupResult> prepareControllers;
        private readonly Func<ProcessStartInfo, int> startProcess;
        private readonly Func<SystemStatus, GpuInfo, GraphicsSetupResult> prepareGraphics;
        private readonly Func<Version, CancellationToken, Task<AppUpdate>> checkUpdate;
        private readonly Lazy<GpuInfo> gpu;
        private readonly bool refreshDisplay;
        private readonly ArtworkService artwork;
        private readonly object folderGate = new object();
        // Carpetas raíz vigentes (la predeterminada dentro de la instalación o la elegida en Opciones).
        private volatile string gamesRoot, emulatorsRoot;
        private volatile Ps4CompatibilityCache ps4Compatibility;
        private volatile Ps4CompatibilityCache xboxCompatibility;
        // Carpetas raíz anteriores de esta sesión: se pueden abrir para que el usuario mueva sus juegos.
        private readonly HashSet<string> previousRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyList<GameSystem> Systems => Catalog.Systems;
        public GpuInfo GpuInfo => refreshDisplay ? GpuDetector.RefreshDisplay(gpu.Value) : gpu.Value;
        public GraphicsSetupResult LastGraphicsSetup { get; private set; } = new GraphicsSetupResult(false, "Todavía no se han preparado los gráficos.", "Bajo");
        public ControllerSetupResult LastControllerSetup { get; private set; } = new ControllerSetupResult(false, "Todavía no se han preparado los controles.");

        /// <summary>Carpeta de instalación de Nexo.</summary>
        public string InstallRoot => root;
        /// <summary>Carpeta raíz de los juegos: dentro, una subcarpeta por consola (Juegos\PS2, D:\MisJuegos\PS2…).</summary>
        public string GamesRoot => gamesRoot;
        /// <summary>Carpeta raíz de los emuladores: dentro, una subcarpeta por consola.</summary>
        public string EmulatorsRoot => emulatorsRoot;
        public string DefaultGamesRoot => Path.Combine(root, Games);
        public string DefaultEmulatorsRoot => Path.Combine(root, Emulators);
        public bool GamesRootIsDefault => string.Equals(gamesRoot, DefaultGamesRoot, StringComparison.OrdinalIgnoreCase);
        public bool EmulatorsRootIsDefault => string.Equals(emulatorsRoot, DefaultEmulatorsRoot, StringComparison.OrdinalIgnoreCase);
        /// <summary>Aviso si una carpeta elegida no estaba disponible al abrir Nexo (por ejemplo, un disco externo desconectado); si no, null.</summary>
        public string FolderWarning { get; private set; }

        public NexoService(string installRoot) : this(installRoot,
            status => ControllerSetup.Prepare(status, XInputGamepad.SetupControllerIndex()), Launcher.Start) { }

        internal NexoService(string installRoot, Func<SystemStatus, ControllerSetupResult> prepareControllers, Func<ProcessStartInfo, int> startProcess,
            Func<GpuInfo> detectGpu = null, Func<SystemStatus, GpuInfo, GraphicsSetupResult> prepareGraphics = null,
            ArtworkService artworkService = null, Func<Version, CancellationToken, Task<AppUpdate>> checkUpdate = null)
        {
            this.prepareControllers = prepareControllers;
            this.startProcess = startProcess;
            this.prepareGraphics = prepareGraphics ?? ((status, info) => GraphicsSetup.Prepare(status, info));
            this.checkUpdate = checkUpdate ?? ((current, ct) => AppUpdates.CheckAsync(current, ct));
            artwork = artworkService ?? new ArtworkService();
            refreshDisplay = detectGpu == null;
            gpu = new Lazy<GpuInfo>(() =>
            {
                try { return (detectGpu ?? GpuDetector.Detect)() ?? GpuDetector.Select(new GpuAdapter[0]); }
                catch (Exception) { return GpuDetector.Select(new GpuAdapter[0]); }
            });
            root = SafePaths.Full(installRoot);
            if (SafePaths.HasReparsePoint(root)) throw new NexoException("La instalación no puede estar dentro de un enlace o junction.");
            try
            {
                Directory.CreateDirectory(root);
                config = new ConfigStore(root);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { throw new NexoException("Nexo necesita permisos de escritura en su carpeta de instalación.", ex); }
            string games = config.GamesRoot ?? DefaultGamesRoot, emulators = config.EmulatorsRoot ?? DefaultEmulatorsRoot;
            var warnings = new List<string>();
            foreach (var kind in new[] { Emulators, Games })
            {
                string chosen = kind == Games ? games : emulators, fallback = kind == Games ? DefaultGamesRoot : DefaultEmulatorsRoot;
                try { PrepareRoot(kind, chosen); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NexoException)
                {
                    // La carpeta predeterminada es obligatoria, como en 1.0.0. Una elegida que no está disponible
                    // (disco desconectado, permisos) no impide abrir Nexo: se avisa y no se crea nada en otro sitio.
                    if (string.Equals(chosen, fallback, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ex is NexoException) throw;
                        throw new NexoException("Nexo necesita permisos de escritura en su carpeta de instalación.", ex);
                    }
                    warnings.Add("La carpeta de " + (kind == Games ? "juegos" : "emuladores") + " elegida (" + chosen + ") no está disponible: "
                        + (ex is NexoException ? ex.Message : "¿está conectado el disco?") + " Conéctala o cámbiala en Opciones › Carpetas.");
                }
            }
            if (warnings.Count > 0) FolderWarning = string.Join("\n", warnings);
            ApplyRoots(games, emulators);
        }

        private void ApplyRoots(string games, string emulators)
        {
            gamesRoot = games;
            emulatorsRoot = emulators;
            ps4Compatibility = new Ps4CompatibilityCache(Path.Combine(games, "PS4"));
            xboxCompatibility = XboxCompatibility.Cache(Path.Combine(games, "Xbox"));
        }

        private string Folder(string category, string id)
        {
            Catalog.Get(id);
            var folder = Path.Combine(category == Games ? gamesRoot : emulatorsRoot, id);
            if (SafePaths.HasReparsePoint(folder)) throw new NexoException("No se admiten enlaces ni junctions en las carpetas de Nexo.");
            return folder;
        }

        // Crea las subcarpetas de consola que falten (con su LEEME breve, sin sobrescribir los existentes).
        private static void PrepareRoot(string category, string folderRoot)
        {
            if (SafePaths.HasReparsePoint(folderRoot)) throw new NexoException("No se admiten enlaces ni junctions en las carpetas de Nexo.");
            foreach (var profile in Catalog.Profiles)
            {
                string text = category == Emulators
                    ? "Copia aquí " + profile.Info.EmulatorName + " completo, con sus subcarpetas, o usa la instalación oficial desde Nexo.\r\nEjecutables conocidos: " + string.Join(", ", profile.Executables) + "."
                    : profile.Instructions + "\r\nPuedes añadir una portada .png o .jpg con el mismo nombre que el juego, o en covers. Nexo no incluye juegos.";
                var folder = Path.Combine(folderRoot, profile.Info.Id);
                if (SafePaths.HasReparsePoint(folder)) throw new NexoException("No se admiten enlaces ni junctions en las carpetas de Nexo.");
                Directory.CreateDirectory(folder);
                var readme = Path.Combine(folder, "LEEME.txt");
                if (!File.Exists(readme))
                    using (var stream = new FileStream(readme, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.WriteLine(text);
            }
        }

        // ------------------------------------------------------------------ Carpetas elegidas por el usuario

        /// <summary>
        /// Cambia la carpeta raíz de los juegos. null (o la predeterminada) restablece Juegos dentro de la instalación.
        /// Valida que sea local, escribible y sin enlaces; crea las subcarpetas de consola y guarda nexo.json.
        /// No mueve ni borra nada: los juegos de la carpeta anterior se quedan donde estaban.
        /// </summary>
        public FolderChangeResult SetGamesRoot(string path) => ChangeRoot(Games, path);

        /// <summary>Igual que <see cref="SetGamesRoot"/> para la carpeta raíz de los emuladores.</summary>
        public FolderChangeResult SetEmulatorsRoot(string path) => ChangeRoot(Emulators, path);

        private FolderChangeResult ChangeRoot(string category, string path)
        {
            lock (folderGate)
            {
                bool games = category == Games;
                string fallback = games ? DefaultGamesRoot : DefaultEmulatorsRoot, previous = games ? gamesRoot : emulatorsRoot;
                string target = path == null ? fallback : ValidateRoot(category, path);
                try { PrepareRoot(category, target); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                { throw new NexoException("No se pudieron crear las carpetas de consola en " + target + ": " + ex.Message, ex); }
                string stored = string.Equals(target, fallback, StringComparison.OrdinalIgnoreCase) ? null : target;
                config.SetRoots(games ? stored : config.GamesRoot, games ? config.EmulatorsRoot : stored);
                bool changed = !string.Equals(previous, target, StringComparison.OrdinalIgnoreCase);
                if (changed) previousRoots.Add(previous);
                ApplyRoots(games ? target : gamesRoot, games ? emulatorsRoot : target);
                FolderWarning = null;
                bool hasContent = changed && HasUserContent(previous);
                string what = games ? "juegos" : "emuladores";
                string message = !changed ? "La carpeta de " + what + " ya era " + target + "."
                    : "Carpeta de " + what + ": " + target + ". Nexo creó dentro una carpeta por consola."
                      + (hasContent ? " Tus " + what + " anteriores siguen en " + previous + ": Nexo no los mueve ni los borra; muévelos tú si quieres." : "");
                return new FolderChangeResult(changed, previous, target, hasContent, message);
            }
        }

        // Las pruebas simulan una carpeta sin permiso de escritura sin tocar permisos reales.
        internal Func<string, bool> CanWriteFolder = AppUpdates.CanWrite;

        /// <summary>Comprueba una carpeta raíz elegida: absoluta, de un disco local, escribible, sin enlaces y sin solaparse.</summary>
        internal string ValidateRoot(string category, string path)
        {
            bool games = category == Games;
            string full = SafePaths.Full(path);
            string fallback = games ? DefaultGamesRoot : DefaultEmulatorsRoot;
            if (string.Equals(full, fallback, StringComparison.OrdinalIgnoreCase)) return fallback;
            if (full.Length <= Path.GetPathRoot(full).Length) throw new NexoException("Elige una carpeta, no la raíz de una unidad (por ejemplo D:\\Juegos en vez de D:\\).");
            DriveType drive;
            try { var info = new DriveInfo(Path.GetPathRoot(full)); drive = info.DriveType; if (!info.IsReady) throw new NexoException("La unidad " + info.Name + " no está disponible."); }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            { throw new NexoException("La unidad de esa carpeta no está disponible.", ex); }
            if (drive != DriveType.Fixed && drive != DriveType.Removable && drive != DriveType.Ram)
                throw new NexoException("Elige una carpeta de un disco de este PC (no de red ni de CD/DVD).");
            if (SafePaths.Within(full, root)) throw new NexoException("Elige una carpeta fuera de la instalación de Nexo, o usa «Restablecer» para volver a la predeterminada.");
            if (SafePaths.Within(root, full)) throw new NexoException("La carpeta no puede contener la instalación de Nexo.");
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) && SafePaths.Within(full, windows)) throw new NexoException("No se admiten carpetas de Windows.");
            string other = games ? emulatorsRoot : gamesRoot;
            if (SafePaths.Within(full, other) || SafePaths.Within(other, full))
                throw new NexoException("La carpeta de juegos y la de emuladores deben ser distintas y no estar una dentro de la otra.");
            if (SafePaths.HasReparsePoint(full)) throw new NexoException("No se admiten enlaces ni junctions (tampoco carpetas sincronizadas que lo sean) en las carpetas de Nexo.");
            try { Directory.CreateDirectory(full); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            { throw new NexoException("No se pudo crear la carpeta " + full + ": " + ex.Message, ex); }
            if (SafePaths.HasReparsePoint(full)) throw new NexoException("No se admiten enlaces ni junctions en las carpetas de Nexo.");
            if (!CanWriteFolder(full)) throw new NexoException("No tengo permiso para escribir en " + full + ". Elige otra carpeta.");
            return full;
        }

        // ¿Hay algo del usuario (además de los LEEME de Nexo) en la carpeta raíz anterior?
        private static bool HasUserContent(string folderRoot)
        {
            try
            {
                foreach (var file in FileTree.Files(folderRoot, 6, 5000, TimeSpan.FromMilliseconds(400), CancellationToken.None))
                    if (!string.Equals(Path.GetFileName(file), "LEEME.txt", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            return false;
        }

        // ------------------------------------------------------------------ Opciones

        /// <summary>Valor de una opción (true por defecto: el comportamiento de Nexo 1.0.0).</summary>
        public bool GetOption(NexoOption option) => config.Option(option);

        /// <summary>Cambia y guarda una opción en nexo.json.</summary>
        public void SetOption(NexoOption option, bool value) => config.SetOption(option, value);

        /// <summary>
        /// Comprobación de actualizaciones. Automática (al abrir): no hace ninguna petición si la opción está desactivada.
        /// Manual («Buscar actualizaciones ahora»): consulta siempre. Devuelve null sin red o sin versión nueva.
        /// </summary>
        public Task<AppUpdate> CheckForUpdateAsync(bool manual, CancellationToken ct = default(CancellationToken))
        {
            if (!manual && !GetOption(NexoOption.CheckUpdates)) return Task.FromResult<AppUpdate>(null);
            return checkUpdate(AppUpdates.Current, ct);
        }

        public SystemStatus GetStatus(string systemId)
        {
            var profile = Catalog.Get(systemId);
            var folder = Folder(Emulators, systemId);
            var manual = config.Get(systemId).Executable;
            string executable = null;
            if (manual != null)
            {
                try { executable = SafePaths.Executable(manual); } catch (NexoException) { }
            }
            if (executable == null) executable = DetectExecutable(profile, folder);
            if (systemId == "GBC" || systemId == "GBA")
            {
                string shared = Folder(Emulators, "GBA"), other = Folder(Emulators, systemId == "GBA" ? "GBC" : "GBA");
                if (executable == null) { executable = DetectExecutable(profile, other); folder = executable == null ? shared : other; }
                else if (SafePaths.Within(executable, other)) folder = other;
            }
            return new SystemStatus(profile.Info, executable, executable != null, folder, Folder(Games, systemId));
        }

        // Kept separate so installed binaries can be checked without creating folders or settings.
        internal static string DetectExecutable(SystemProfile profile, string folder)
        {
            string executable = null;
            int best = int.MaxValue;
            foreach (var file in FileTree.Files(folder, 4, 10000, TimeSpan.FromMilliseconds(350), CancellationToken.None))
            {
                int priority = Array.FindIndex(profile.Executables, name => string.Equals(name, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase));
                if (priority >= 0 && priority < best) { executable = file; best = priority; if (best == 0) break; }
            }
            return executable;
        }

        public void SetExecutable(string systemId, string exePath)
        { Catalog.Get(systemId); config.SetExecutable(systemId, SafePaths.Executable(exePath)); }

        public void ClearExecutable(string systemId)
        { Catalog.Get(systemId); config.SetExecutable(systemId, null); }

        public Task<EmulatorInstallResult> InstallEmulatorAsync(string systemId, IProgress<SetupProgress> progress, CancellationToken ct)
        {
            Catalog.Get(systemId);
            return Task.Run(async () =>
            {
                if (!Environment.Is64BitOperatingSystem) throw new NexoException("Estos emuladores requieren Windows de 64 bits.");
                await EmulatorInstaller.Gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using (var downloader = new PackageDownloader())
                    {
                        bool changed = await EmulatorInstaller.Install(GetStatus(systemId), downloader, progress, ct).ConfigureAwait(false);
                        return new EmulatorInstallResult(changed, changed ? "Emulador instalado desde su publicación oficial. Mandos y gráficos se preparan al abrirlo." : "El emulador ya está instalado; se conserva sin cambios.", GetStatus(systemId));
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (NexoException) { throw; }
                catch (Exception ex) { throw new NexoException("No se pudo instalar el emulador. " + ex.Message, ex); }
                finally { EmulatorInstaller.Gate.Release(); }
            }, ct);
        }

        public Task<SystemFilesResult> ImportSystemFilesAsync(string systemId, string[] paths, CancellationToken ct = default(CancellationToken))
        {
            Catalog.Get(systemId);
            var selected = paths?.ToArray();
            return Task.Run(async () =>
            {
                await EmulatorInstaller.Gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    string message = await SystemFiles.Import(GetStatus(systemId), selected, ct, startProcess).ConfigureAwait(false);
                    return new SystemFilesResult(true, message, GetStatus(systemId));
                }
                catch (OperationCanceledException) { throw; }
                catch (NexoException) { throw; }
                catch (Exception ex) { throw new NexoException("No se pudieron importar los archivos del sistema. " + ex.Message, ex); }
                finally { EmulatorInstaller.Gate.Release(); }
            }, ct);
        }

        public async Task<IReadOnlyList<GameEntry>> ScanGamesAsync(string systemId, CancellationToken ct = default(CancellationToken))
            => (await ScanGamesDetailedAsync(systemId, ct).ConfigureAwait(false)).Games;

        public Task<GameScanResult> ScanGamesDetailedAsync(string systemId, CancellationToken ct = default(CancellationToken))
        {
            var profile = Catalog.Get(systemId);
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var status = GetStatus(systemId);
                return GameScanner.ScanDetailed(profile, status.GamesFolder, status.ExecutablePath, ct);
            }, ct);
        }

        // Call independently of scanning, e.g. on opening PS4. A true result asks the UI to rescan.
        // Offline/error/fresh cache returns false; user cancellation propagates.
        public Task<bool> RefreshPs4CompatibilityAsync(CancellationToken ct = default(CancellationToken))
            => ps4Compatibility.RefreshAsync(ct);

        public Task<bool> RefreshXboxCompatibilityAsync(CancellationToken ct = default(CancellationToken))
            => xboxCompatibility.RefreshAsync(ct);

        public Task<GameEntry> EnsureArtAsync(GameEntry game, CancellationToken ct)
        {
            if (game == null) throw new NexoException("Selecciona un juego.");
            var folder = Folder(Games, game.SystemId);
            // Includes local ISO reads/image validation: never do these on the WPF dispatcher.
            return Task.Run(() => artwork.EnsureAsync(game, folder, config.DownloadArtwork, ct), ct);
        }

        public int LaunchEmulator(string systemId)
        {
            var status = GetStatus(systemId);
            if (!status.Installed) throw new NexoException("Copia el emulador en su carpeta o elige su ejecutable primero.");
            PrepareBeforeLaunch(status);
            return startProcess(Launcher.StartInfo(status.ExecutablePath, Launcher.EmulatorArguments(systemId)));
        }

        public GraphicsSetupResult PrepareGraphics(string systemId)
        {
            try { return PrepareGraphics(GetStatus(systemId)); }
            catch (Exception ex) { return GraphicsFailure(ex); }
        }

        private GraphicsSetupResult PrepareGraphics(SystemStatus status)
        {
            try { return LastGraphicsSetup = prepareGraphics(status, GpuInfo); }
            catch (Exception ex) { return GraphicsFailure(ex); }
        }

        private GraphicsSetupResult GraphicsFailure(Exception ex)
            => LastGraphicsSetup = new GraphicsSetupResult(false, "No se pudieron preparar los gráficos; el lanzamiento puede continuar. " + ex.Message, GpuInfo.Tier);

        public ControllerSetupResult PrepareControllers(string systemId)
        {
            try { return PrepareControllers(GetStatus(systemId)); }
            catch (Exception ex) { return ControllerFailure(ex); }
        }

        private ControllerSetupResult PrepareControllers(SystemStatus status)
        {
            try { return LastControllerSetup = prepareControllers(status); }
            catch (Exception ex) { return ControllerFailure(ex); }
        }

        private ControllerSetupResult ControllerFailure(Exception ex)
            => LastControllerSetup = new ControllerSetupResult(false, "No se pudieron preparar los controles; el lanzamiento puede continuar. " + ex.Message);

        public Task<string> ExtractArchiveAsync(ArchiveEntry archive, IProgress<long> bytesWritten, CancellationToken ct)
        {
            if (archive == null) throw new NexoException("Selecciona un archivo comprimido.");
            var folder = Folder(Games, archive.SystemId);
            if (Ps4Package.IsPkg(archive.SystemId, archive.Path)) return Ps4PkgInstaller.InstallAsync(archive, folder, bytesWritten, ct);
            return ArchiveExtractor.ExtractAsync(archive, folder, bytesWritten, ct);
        }

        internal ProcessStartInfo PrepareGame(GameEntry game)
        {
            if (game == null) throw new NexoException("Selecciona un juego.");
            var status = GetStatus(game.SystemId);
            if (!status.Installed) throw new NexoException("Copia el emulador en su carpeta o elige su ejecutable primero.");
            var path = SafePaths.Full(game.Path);
            if (!SafePaths.Within(path, status.GamesFolder)) throw new NexoException("El juego debe estar en la carpeta Juegos de su sistema.");
            if (game.SystemId == "Switch" && SwitchContainerInspector.IsContainer(path))
            {
                var problem = SwitchContainerInspector.Inspect(path);
                if (problem != null) throw new NexoException(problem.Message);
            }
            var verified = GameScanner.Describe(Catalog.Get(game.SystemId), path, status.ExecutablePath);
            if (verified == null) throw new NexoException("El juego ya no existe, no tiene un formato admitido o le falta un núcleo compatible.");
            return Launcher.StartInfo(status.ExecutablePath, Launcher.GameArguments(game.SystemId, status.ExecutablePath, verified, config.Get(game.SystemId).Arguments,
                GetOption(NexoOption.FullscreenGames)));
        }

        public int LaunchGame(GameEntry game)
        {
            var info = PrepareGame(game);
            Ps4PkgInstaller.PrepareAddons(game, info.FileName);
            try { PrepareBeforeLaunch(GetStatus(game.SystemId)); }
            catch (Exception ex) { ControllerFailure(ex); }
            return startProcess(info);
        }

        // Al abrir: mandos y después gráficos, salvo que el usuario lo haya desactivado en Opciones.
        private void PrepareBeforeLaunch(SystemStatus status)
        {
            if (GetOption(NexoOption.AutoControllers)) PrepareControllers(status);
            else LastControllerSetup = new ControllerSetupResult(false, "La configuración automática de mandos está desactivada en Opciones; se usan los ajustes del emulador.");
            if (GetOption(NexoOption.AutoGraphics)) PrepareGraphics(status);
            else LastGraphicsSetup = new GraphicsSetupResult(false, "Los gráficos automáticos están desactivados en Opciones; se conservan los ajustes del emulador.", GpuInfo.Tier);
        }

        // La instalación, las carpetas raíz vigentes y las anteriores de esta sesión (para mover juegos a mano).
        internal bool CanOpenFolder(string full)
        {
            if (SafePaths.Within(full, root) || SafePaths.Within(full, gamesRoot) || SafePaths.Within(full, emulatorsRoot)) return true;
            lock (folderGate) return previousRoots.Any(p => SafePaths.Within(full, p));
        }

        public void OpenFolder(string path)
        {
            var full = SafePaths.Full(path);
            if (!CanOpenFolder(full) || !Directory.Exists(full) || SafePaths.HasReparsePoint(full))
                throw new NexoException("Solo se pueden abrir carpetas de esta instalación de Nexo o las carpetas de juegos y emuladores elegidas.");
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Launcher.Start(Launcher.StartInfo(explorer, new[] { full }));
        }

        public void OpenWebsite(string systemId)
        {
            var site = Catalog.Get(systemId).Info.Website;
            if (string.IsNullOrWhiteSpace(site)) throw new NexoException("No se ha configurado una web oficial verificada para este sistema.");
            try { using (Process.Start(new ProcessStartInfo(site) { UseShellExecute = true })) { } }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            { throw new NexoException("No se pudo abrir el navegador predeterminado.", ex); }
        }
    }
}
