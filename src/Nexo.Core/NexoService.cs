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
        private readonly string root;
        private readonly ConfigStore config;
        private readonly Func<SystemStatus, ControllerSetupResult> prepareControllers;
        private readonly Func<ProcessStartInfo, int> startProcess;
        private readonly Func<SystemStatus, GpuInfo, GraphicsSetupResult> prepareGraphics;
        private readonly Lazy<GpuInfo> gpu;
        private readonly bool refreshDisplay;
        private readonly ArtworkService artwork = new ArtworkService();
        private readonly Ps4CompatibilityCache ps4Compatibility;
        private readonly Ps4CompatibilityCache xboxCompatibility;
        public IReadOnlyList<GameSystem> Systems => Catalog.Systems;
        public GpuInfo GpuInfo => refreshDisplay ? GpuDetector.RefreshDisplay(gpu.Value) : gpu.Value;
        public GraphicsSetupResult LastGraphicsSetup { get; private set; } = new GraphicsSetupResult(false, "Todavía no se han preparado los gráficos.", "Bajo");
        public ControllerSetupResult LastControllerSetup { get; private set; } = new ControllerSetupResult(false, "Todavía no se han preparado los controles.");

        public NexoService(string installRoot) : this(installRoot,
            status => ControllerSetup.Prepare(status, XInputGamepad.SetupControllerIndex()), Launcher.Start) { }

        internal NexoService(string installRoot, Func<SystemStatus, ControllerSetupResult> prepareControllers, Func<ProcessStartInfo, int> startProcess,
            Func<GpuInfo> detectGpu = null, Func<SystemStatus, GpuInfo, GraphicsSetupResult> prepareGraphics = null)
        {
            this.prepareControllers = prepareControllers;
            this.startProcess = startProcess;
            this.prepareGraphics = prepareGraphics ?? ((status, info) => GraphicsSetup.Prepare(status, info));
            refreshDisplay = detectGpu == null;
            gpu = new Lazy<GpuInfo>(() =>
            {
                try { return (detectGpu ?? GpuDetector.Detect)() ?? GpuDetector.Select(new GpuAdapter[0]); }
                catch (Exception) { return GpuDetector.Select(new GpuAdapter[0]); }
            });
            root = SafePaths.Full(installRoot);
            ps4Compatibility = new Ps4CompatibilityCache(Path.Combine(root, "Juegos", "PS4"));
            xboxCompatibility = XboxCompatibility.Cache(Path.Combine(root, "Juegos", "Xbox"));
            if (SafePaths.HasReparsePoint(root)) throw new NexoException("La instalación no puede estar dentro de un enlace o junction.");
            try
            {
                Directory.CreateDirectory(root);
                config = new ConfigStore(root);
                foreach (var profile in Catalog.Profiles)
                {
                    CreateFolder("Emuladores", profile, "Copia aquí " + profile.Info.EmulatorName + " completo, con sus subcarpetas, o usa la instalación oficial desde Nexo.\r\nEjecutables conocidos: " + string.Join(", ", profile.Executables) + ".");
                    CreateFolder("Juegos", profile, profile.Instructions + "\r\nPuedes añadir una portada .png o .jpg con el mismo nombre que el juego, o en covers. Nexo no incluye juegos.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { throw new NexoException("Nexo necesita permisos de escritura en su carpeta de instalación.", ex); }
        }

        private string Folder(string category, string id)
        {
            Catalog.Get(id);
            var folder = Path.Combine(root, category, id);
            if (SafePaths.HasReparsePoint(folder)) throw new NexoException("No se admiten enlaces ni junctions en las carpetas de Nexo.");
            return folder;
        }

        private void CreateFolder(string category, SystemProfile profile, string text)
        {
            var folder = Folder(category, profile.Info.Id);
            Directory.CreateDirectory(folder);
            var readme = Path.Combine(folder, "LEEME.txt");
            if (!File.Exists(readme))
                using (var stream = new FileStream(readme, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.WriteLine(text);
        }

        public SystemStatus GetStatus(string systemId)
        {
            var profile = Catalog.Get(systemId);
            var folder = Folder("Emuladores", systemId);
            var manual = config.Get(systemId).Executable;
            string executable = null;
            if (manual != null)
            {
                try { executable = SafePaths.Executable(manual); } catch (NexoException) { }
            }
            if (executable == null) executable = DetectExecutable(profile, folder);
            if (systemId == "GBC" || systemId == "GBA")
            {
                string shared = Folder("Emuladores", "GBA"), other = Folder("Emuladores", systemId == "GBA" ? "GBC" : "GBA");
                if (executable == null) { executable = DetectExecutable(profile, other); folder = executable == null ? shared : other; }
                else if (SafePaths.Within(executable, other)) folder = other;
            }
            return new SystemStatus(profile.Info, executable, executable != null, folder, Folder("Juegos", systemId));
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
            var folder = Folder("Juegos", game.SystemId);
            // Includes local ISO reads/image validation: never do these on the WPF dispatcher.
            return Task.Run(() => artwork.EnsureAsync(game, folder, config.DownloadArtwork, ct), ct);
        }

        public int LaunchEmulator(string systemId)
        {
            var status = GetStatus(systemId);
            if (!status.Installed) throw new NexoException("Copia el emulador en su carpeta o elige su ejecutable primero.");
            PrepareControllers(status);
            PrepareGraphics(status);
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
            var folder = Folder("Juegos", archive.SystemId);
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
            return Launcher.StartInfo(status.ExecutablePath, Launcher.GameArguments(game.SystemId, status.ExecutablePath, verified, config.Get(game.SystemId).Arguments));
        }

        public int LaunchGame(GameEntry game)
        {
            var info = PrepareGame(game);
            Ps4PkgInstaller.PrepareAddons(game, info.FileName);
            PrepareControllers(game.SystemId);
            PrepareGraphics(game.SystemId);
            return startProcess(info);
        }

        public void OpenFolder(string path)
        {
            var full = SafePaths.Full(path);
            if (!SafePaths.Within(full, root) || !Directory.Exists(full) || SafePaths.HasReparsePoint(full))
                throw new NexoException("Solo se pueden abrir carpetas de esta instalación de Nexo.");
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
