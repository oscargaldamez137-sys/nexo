using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexo.Core
{
    internal static class EmulatorInstaller
    {
        internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        internal static async Task<bool> Install(SystemStatus status, PackageDownloader downloader, IProgress<SetupProgress> progress,
            CancellationToken ct, OfficialPackage[] packages = null)
        {
            if (status.Installed) return false;
            packages = packages ?? OfficialPackages.For(status.System.Id);
            if (!File.Exists(ArchiveExtractor.TarPath)) throw new NexoException("Falta tar.exe de Windows. Actualiza Windows antes de descargar los emuladores.");
            string parent = SafePaths.Full(status.EmulatorFolder);
            if (!Directory.Exists(parent) || SafePaths.HasReparsePoint(parent)) throw new NexoException("La carpeta de emuladores no es segura.");
            // Gate is owned by the service, and re-detection happens after acquiring it.
            if (NexoService.DetectExecutable(Catalog.Get(status.System.Id), parent) != null) return false;
            string stage = Path.Combine(parent, ".nexo-extract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                string payload = Path.Combine(stage, "payload"); string executable = null;
                foreach (var package in packages)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new SetupProgress("Consultando publicación oficial", 0));
                    await OfficialPackages.Resolve(package, downloader, ct).ConfigureAwait(false);
                    string archive = Path.Combine(stage, package.Cores ? "cores.pkg" : "emulator.pkg");
                    await downloader.Download(package.Url, archive, progress, ct).ConfigureAwait(false);
                    progress?.Report(new SetupProgress(package.Hash == null ? "Verificando contenido (sin SHA256 disponible)" : "Verificando SHA256 oficial", 0));
                    OfficialPackages.Verify(archive, package.Hash, ct);
                    if (package.DiskImage)
                    {
                        if (executable == null || !XboxFiles.IsQcow(archive)) throw new NexoException("El disco duro oficial no tiene una cabecera QCOW2 válida.");
                        string system = Path.Combine(Path.GetDirectoryName(executable), "system");
                        Directory.CreateDirectory(system);
                        File.Move(archive, Path.Combine(system, "xbox_hdd.qcow2"));
                        continue;
                    }
                    string target = package.Cores ? Path.Combine(stage, "core-payload") : payload;
                    await ArchiveExtractor.ExtractPackageAsync(archive, target, new InlineProgress<long>(n => progress?.Report(new SetupProgress("Extrayendo " + package.Name, n))), ct).ConfigureAwait(false);
                    if (!package.Cores)
                    {
                        executable = NexoService.DetectExecutable(Catalog.Get(status.System.Id), payload);
                        if (executable == null || !IsWindowsExecutable(executable)) throw new NexoException("El paquete no contiene el ejecutable de Windows esperado.");
                    }
                    else
                    {
                        if (executable == null) throw new NexoException("Falta RetroArch antes de instalar sus núcleos.");
                        string cores = Path.Combine(Path.GetDirectoryName(executable), "cores");
                        Directory.CreateDirectory(cores);
                        var files = FileTree.Files(target, 5, 20000, TimeSpan.FromSeconds(15), ct).Where(p => p.EndsWith("_libretro.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (files.Length == 0) throw new NexoException("El paquete oficial no contiene núcleos de RetroArch.");
                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            string dest = Path.Combine(cores, Path.GetFileName(file));
                            if (File.Exists(dest)) throw new NexoException("El paquete repite un núcleo; se conserva la instalación anterior.");
                            File.Move(file, dest);
                        }
                    }
                    File.Delete(archive);
                }
                if (executable == null) throw new NexoException("La publicación no contiene un emulador.");
                Portable(status.System.Id, Path.GetDirectoryName(executable));
                ct.ThrowIfCancellationRequested();
                // No existing files are merged/replaced. Publication is a single directory rename.
                string final = Path.Combine(parent, "Nexo-" + status.System.Id);
                if (Directory.Exists(final) || File.Exists(final)) throw new NexoException("Ya existe " + final + ". Se conserva: revisa su contenido antes de instalar otra copia.");
                if (SafePaths.HasReparsePoint(final)) throw new NexoException("El destino contiene un enlace.");
                Directory.Move(payload, final);
                progress?.Report(new SetupProgress("Instalado", 1, 1));
                return true;
            }
            finally { ArchiveExtractor.CleanPackageStage(stage, parent); }
        }
        internal static void Portable(string id, string folder)
        {
            string marker = id == "PS2" || id == "GBA" || id == "GBC" ? "portable.ini" : id == "GameCube-Wii" || id == "Xbox360" ? "portable.txt" : id == "Xbox" ? "xemu.toml" : null;
            if (marker != null)
            {
                string path = Path.Combine(folder, marker);
                if (!File.Exists(path)) using (File.Create(path)) { }
            }
            string data = id == "PSP" ? "memstick" : id == "Switch" ? "user" : id == "GameCube-Wii" ? "User" : id == "PS2" ? "bios" : null;
            if (data != null) Directory.CreateDirectory(Path.Combine(folder, data));
            if (id == "Xbox") XboxFiles.Configure(folder);
        }
        private static bool IsWindowsExecutable(string file)
        {
            using (var f = File.OpenRead(file)) using (var reader = new BinaryReader(f))
            {
                if (f.Length < 128 || reader.ReadUInt16() != 0x5a4d) return false;
                f.Position = 60; uint pe = reader.ReadUInt32();
                if (pe > f.Length - 6) return false;
                f.Position = pe;
                return reader.ReadUInt32() == 0x4550 && reader.ReadUInt16() == 0x8664;
            }
        }
    }
}
