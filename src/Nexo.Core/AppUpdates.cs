using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    /// <summary>A newer Nexo release with its Windows installer.</summary>
    public sealed class AppUpdate
    {
        public Version Version { get; }
        public string Url { get; }
        public long Size { get; }
        /// <summary>Hex SHA-256 published by GitHub ("digest"), or null if the release does not publish it.</summary>
        public string Sha256 { get; }
        internal string ReleaseUrl { get; }
        internal AppUpdate(Version version, string url, long size, string sha256, string releaseUrl)
        { Version = version; Url = url; Size = size; Sha256 = sha256; ReleaseUrl = releaseUrl; }
    }

    /// <summary>
    /// Self-update from the latest GitHub release: same flow as Nova5 (check, download to %TEMP%,
    /// verify size and SHA-256, run the release's own installer silently over this installation).
    /// </summary>
    public static class AppUpdates
    {
        public const string Repository = "oscargaldamez137-sys/nexo";
        public const string SetupAsset = "Instalar-Nexo.exe";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";
        internal const string DefaultReleaseUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        internal const string DownloadPrefix = "https://github.com/" + Repository + "/releases/download/";
        /// <summary>Tests only: a release served from this PC (http://127.0.0.1:port/...). Non-loopback values are ignored.</summary>
        public const string TestUrlVariable = "NEXO_UPDATE_URL";
        /// <summary>Written by instalar.ps1: its presence means this folder is an installation.</summary>
        public const string InstalledManifest = "nexo-installed-files.txt";
        private const long MaxSetupSize = 512L * 1024 * 1024;
        private static readonly string[] GithubHosts = { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com" };

        /// <summary>Version of the running Nexo.exe (Version in Nexo.csproj), as major.minor.patch.</summary>
        public static Version Current { get; } = Normalize((Assembly.GetEntryAssembly() ?? typeof(AppUpdates).Assembly).GetName().Version);

        /// <summary>Folder in %TEMP% for downloads, the extracted installer, the last attempt and the installer log.</summary>
        public static string WorkFolder => Path.Combine(Path.GetTempPath(), "Nexo-Update");
        public static string LogPath => Path.Combine(WorkFolder, "error.txt");

        internal static Version Normalize(Version v) => v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(v.Build, 0));

        /// <summary>"v1.2.3", "V1.2.3", "1.2.3" or "1.2" → version; anything else (pre-releases, text) → null.</summary>
        public static Version ParseTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string t = tag.Trim();
            if (t[0] == 'v' || t[0] == 'V') t = t.Substring(1);
            if (!Regex.IsMatch(t, @"^\d{1,6}\.\d{1,6}(\.\d{1,6})?$")) return null;
            return Version.TryParse(t, out var v) ? Normalize(v) : null;
        }

        /// <summary>The update described by a GitHub release JSON, or null if it is not newer, lacks the asset or is malformed.</summary>
        internal static AppUpdate ParseRelease(string json, Version current, string releaseUrl = DefaultReleaseUrl)
        {
            Dictionary<string, object> data;
            try { data = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>; }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) { return null; }
            if (data == null || !data.TryGetValue("tag_name", out var tag)) return null;
            if (data.TryGetValue("draft", out var draft) && draft is bool d && d) return null;
            if (data.TryGetValue("prerelease", out var pre) && pre is bool p && p) return null;
            var latest = ParseTag(tag as string);
            if (latest == null || latest <= Normalize(current)) return null;
            if (!data.TryGetValue("assets", out var raw) || !(raw is object[] assets)) return null;
            var asset = assets.OfType<Dictionary<string, object>>().FirstOrDefault(a => a.TryGetValue("name", out var n) && n as string == SetupAsset);
            if (asset == null) return null;
            if (!asset.TryGetValue("browser_download_url", out var u) || !(u is string url) || !AssetAllowed(url, releaseUrl)) return null;
            if (!asset.TryGetValue("size", out var s) || !(s is int || s is long)) return null;
            long size = Convert.ToInt64(s);
            if (size <= 0 || size > MaxSetupSize) return null;
            string sha = null;
            if (asset.TryGetValue("digest", out var digest) && digest != null)
            {
                // A digest Nexo cannot check is not silently skipped.
                if (!(digest is string text) || !Regex.IsMatch(text, "^sha256:[0-9a-fA-F]{64}$")) return null;
                sha = text.Substring(7).ToLowerInvariant();
            }
            return new AppUpdate(latest, url, size, sha, releaseUrl);
        }

        /// <summary>Latest release newer than <paramref name="current"/>; null without network, release or asset (never throws).</summary>
        public static Task<AppUpdate> CheckAsync(Version current, CancellationToken ct = default(CancellationToken)) => CheckWithAsync(current, ct, null, null);

        // Tests inject the HTTP handler (no network) or the release URL.
        internal static async Task<AppUpdate> CheckWithAsync(Version current, CancellationToken ct, HttpMessageHandler handler, string releaseUrl = null)
        {
            releaseUrl = releaseUrl ?? ReleaseUrl();
            try
            {
                EnsureTls();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                using (var downloader = new PackageDownloader(handler, HostsFor(releaseUrl)))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    string json = await downloader.TextAsync(releaseUrl, timeout.Token).ConfigureAwait(false);
                    return ParseRelease(json, current, releaseUrl);
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>Downloads the installer into <paramref name="folder"/> and checks size and SHA-256. Reuses an already verified copy.</summary>
        public static Task<string> DownloadAsync(AppUpdate update, string folder, IProgress<double> progress, CancellationToken ct = default(CancellationToken))
            => DownloadWithAsync(update, folder, progress, ct, null);

        internal static async Task<string> DownloadWithAsync(AppUpdate update, string folder, IProgress<double> progress, CancellationToken ct, HttpMessageHandler handler)
        {
            if (update == null) throw new ArgumentNullException(nameof(update));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Instalar-Nexo-" + update.Version + ".exe");
            if (File.Exists(path))
            {
                try { Verify(path, update.Size, update.Sha256); progress?.Report(1); return path; }
                catch (NexoException) { File.Delete(path); }
            }
            string partial = path + ".part";
            if (File.Exists(partial)) File.Delete(partial);
            try
            {
                EnsureTls();
                using (var downloader = new PackageDownloader(handler, HostsFor(update.ReleaseUrl)))
                    await downloader.Download(update.Url, partial,
                        new InlineProgress<SetupProgress>(p => progress?.Report(Math.Min(1, (double)p.BytesCompleted / update.Size))), ct).ConfigureAwait(false);
                Verify(partial, update.Size, update.Sha256);
                File.Move(partial, path);
                return path;
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }

        internal static void Verify(string path, long size, string sha256)
        {
            long length = new FileInfo(path).Length;
            if (length != size) throw new NexoException("Descarga incompleta (" + length + " de " + size + " bytes). No se instalará.");
            OfficialPackages.Verify(path, sha256);
        }

        /// <summary>
        /// Extracts the IExpress installer without running it (wextract /Q /T:folder /C) and returns the folder
        /// with instalar.ps1 and Nexo.zip. Blocking: call it outside the UI thread.
        /// </summary>
        public static string ExtractInstaller(string setup, string folder)
        {
            string payload = Path.Combine(folder, "payload");
            if (Directory.Exists(payload)) Directory.Delete(payload, true);
            // wextract shows a modal "That folder is invalid" box when /T: does not exist or it cannot parse it:
            // the folder is created first and passed without quotes or spaces (8.3 name if needed).
            Directory.CreateDirectory(payload);
            string target = ShortPath(payload);
            if (target.IndexOf(' ') >= 0 || target.IndexOf('"') >= 0)
                throw new NexoException("La carpeta temporal de Windows contiene espacios y no tiene nombre corto; actualiza con Instalar-Nexo.exe.");
            var info = new ProcessStartInfo(setup, "/Q /T:" + target + " /C") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
            using (var process = Process.Start(info))
            {
                if (!process.WaitForExit(60000))
                {
                    // A stuck extraction (for example, a wextract dialog) never stays on screen: Nexo reports it itself.
                    try { process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                    throw new NexoException("El instalador descargado no terminó de extraerse.");
                }
                if (process.ExitCode != 0) throw new NexoException("El instalador descargado no se pudo extraer (código " + process.ExitCode + ").");
            }
            foreach (var name in new[] { "instalar.ps1", "Nexo.zip" })
                if (!File.Exists(Path.Combine(payload, name))) throw new NexoException("El instalador descargado no contiene " + name + ".");
            return payload;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

        internal static string ShortPath(string path)
        {
            if (path.IndexOf(' ') < 0) return path;
            var buffer = new StringBuilder(1024);
            uint n = GetShortPathName(path, buffer, (uint)buffer.Capacity);
            return n > 0 && n < buffer.Capacity ? buffer.ToString() : path;
        }

        /// <summary>
        /// Starts instalar.ps1 silently over <paramref name="installDir"/>. It waits for this Nexo to close,
        /// replaces the program files, keeps Emuladores, Juegos and nexo.json and, unless told otherwise, opens Nexo again.
        /// </summary>
        public static void StartInstaller(string payload, string installDir, bool relaunch)
        {
            string script = Path.Combine(payload, "instalar.ps1");
            string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            string args = "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File " + Launcher.Quote(script) +
                          " -Destination " + Launcher.Quote(installDir.TrimEnd('\\', '/')) +
                          " -NonInteractive -NoShortcuts -WaitSeconds 60 -LogPath " + Launcher.Quote(LogPath) +
                          (relaunch ? "" : " -NoLaunch");
            using (Process.Start(new ProcessStartInfo(powershell, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = payload })) { }
        }

        /// <summary>True when instalar.ps1 installed this folder (silent updates only target installations).</summary>
        public static bool IsInstalled(string folder) => File.Exists(Path.Combine(folder, InstalledManifest));

        public static bool CanWrite(string folder)
        {
            try
            {
                using (new FileStream(Path.Combine(folder, ".nexo-write-test-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { return false; }
        }

        // An attempt that left Nexo on the old version means the silent installer failed: do not retry in a loop.
        private static string AttemptFile(string folder) => Path.Combine(folder, "intento.txt");

        public static void RecordAttempt(Version version, string folder = null)
        {
            folder = folder ?? WorkFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(AttemptFile(folder), version + "|" + DateTime.UtcNow.Ticks, Encoding.UTF8);
        }

        public static bool AttemptFailed(Version version, Version current, string folder = null)
        {
            try
            {
                var parts = File.ReadAllText(AttemptFile(folder ?? WorkFolder), Encoding.UTF8).Split('|');
                return parts.Length == 2 && ParseTag(parts[0]) == Normalize(version) && Normalize(current) < Normalize(version) &&
                       long.TryParse(parts[1], out var ticks) && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) < TimeSpan.FromDays(1);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { return false; }
        }

        internal static string ReleaseUrl()
        {
            string test = Environment.GetEnvironmentVariable(TestUrlVariable);
            return TestOrigin(test) != null ? test : DefaultReleaseUrl;
        }

        private static Uri TestOrigin(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && u.IsLoopback && u.UserInfo == "" ? u : null;

        private static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.Port == b.Port && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.UserInfo == "";

        // GitHub over HTTPS in production; only the local test server when the release URL is a loopback test URL.
        internal static Func<Uri, bool> HostsFor(string releaseUrl)
        {
            var test = TestOrigin(releaseUrl);
            if (test != null) return u => u != null && u.IsAbsoluteUri && SameOrigin(u, test);
            return u => u != null && u.IsAbsoluteUri && u.Scheme == "https" && u.Port == 443 && u.UserInfo == "" && GithubHosts.Contains(u.Host);
        }

        private static bool AssetAllowed(string url, string releaseUrl)
        {
            var test = TestOrigin(releaseUrl);
            if (test != null) return Uri.TryCreate(url, UriKind.Absolute, out var u) && SameOrigin(u, test);
            return url.StartsWith(DownloadPrefix, StringComparison.Ordinal) && url.EndsWith("/" + SetupAsset, StringComparison.Ordinal);
        }

        // .NET 4.8 uses the OS default (TLS 1.2/1.3 on Windows 10/11). Only a forced older list gets TLS 1.2 added.
        private static void EnsureTls()
        {
            var current = ServicePointManager.SecurityProtocol;
            if (current != SecurityProtocolType.SystemDefault && (current & SecurityProtocolType.Tls12) == 0)
                ServicePointManager.SecurityProtocol = current | SecurityProtocolType.Tls12;
        }
    }

    /// <summary>External links Nexo opens in the default browser. Only these fixed addresses.</summary>
    public static class NexoLinks
    {
        public const string Donate = "https://ko-fi.com/nova5emu";
        public const string Repository = "https://github.com/" + AppUpdates.Repository;
        private static readonly string[] Allowed = { Donate, AppUpdates.ReleasesPage, Repository };

        public static void Open(string url)
        {
            if (Array.IndexOf(Allowed, url) < 0) throw new NexoException("Enlace no permitido.");
            try { using (Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { } }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            { throw new NexoException("No se pudo abrir el navegador: " + ex.Message, ex); }
        }
    }
}
