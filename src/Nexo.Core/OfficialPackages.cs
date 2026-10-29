using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    internal sealed class OfficialPackage
    {
        internal string Url, MetadataUrl, ChecksumUrl, Name, Hash;
        internal bool Cores, DiskImage;
        // Always the latest release of the repository; the URL and hash come from its metadata.
        internal bool Latest;
    }
    internal static class OfficialPackages
    {
        internal static OfficialPackage[] For(string id)
        {
            switch (id)
            {
                case "GBC": case "GBA": return new[] { Github("mgba-emu/mgba", "0.10.5", "mGBA-0.10.5-win64.7z") };
                case "Xbox":
                    var xbox = Github("xemu-project/xemu", "v0.8.138", "xemu-0.8.138-windows-x86_64.zip");
                    xbox.Hash = "bb6d8b3a82ca514a162ebd0f237a912143238bfc635311f9835768eb2ceedba4";
                    var hdd = Github("xemu-project/xemu-dashboard", "v20260516-0955", "xbox_hdd.qcow2");
                    hdd.DiskImage = true; hdd.Hash = "00d7df7a2bc235f8801764f00b7f40e194d1e392f7a9619d6b2396c89770f6dd";
                    return new[] { xbox, hdd };
                case "Xbox360":
                    var xenia = Github("xenia-canary/xenia-canary-releases", "02d2cb5", "xenia_canary_windows_.zip");
                    xenia.Hash = "1adbb2c1c69cbd5fabadb43d03b4c407db3b245de922e11aba699867c68d0796";
                    return new[] { xenia };
                case "PS2": return new[] { Github("PCSX2/pcsx2", "v2.8.2", "pcsx2-v2.8.2-windows-x64-Qt.7z") };
                case "PS3":
                    var ps3 = Github("RPCS3/rpcs3-binaries-win", "build-06ae1af55161e8a9e8ad77395a97455d493b5cb3", "rpcs3-v0.0.43-20393-06ae1af5_win64_msvc.7z");
                    ps3.ChecksumUrl = ps3.Url + ".sha256"; return new[] { ps3 };
                case "PSP": return new[] { Github("hrydgard/ppsspp", "v1.20.4", "PPSSPP-v1.20.4-Windows-x64.zip") };
                case "PS4": return new[] { Github("shadps4-emu/shadPS4", "v.0.20.0", "shadps4-win64-sdl-2026-0.20.0.zip") };
                case "GameCube-Wii": return new[] { Direct("https://dl.dolphin-emu.org/releases/2609/dolphin-2609-x64.7z") };
                case "Retro": return new[] { Direct("https://buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch.7z"),
                    new OfficialPackage { Url = "https://buildbot.libretro.com/stable/1.22.2/windows/x86_64/RetroArch_cores.7z", Name = "RetroArch_cores.7z", Cores = true } };
                case "Switch": return new[] { new OfficialPackage { Name = "Eden-0.2.1.zip", MetadataUrl = "https://git.eden-emu.dev/eden-emu/eden/releases/tag/v0.2.1" } };
                // Nova5 is our own emulator and updates itself; Nexo installs whatever is current.
                case "PS5": return new[] { new OfficialPackage { Name = "Nova5-Portable.zip", Latest = true,
                    MetadataUrl = "https://api.github.com/repos/nova5emu/nova5/releases/latest" } };
                default: throw new NexoException("Este sistema no tiene descarga automática.");
            }
        }
        private static OfficialPackage Direct(string url) => new OfficialPackage { Url = url, Name = Path.GetFileName(new Uri(url).AbsolutePath) };
        private static OfficialPackage Github(string repo, string tag, string name) => new OfficialPackage {
            Url = "https://github.com/" + repo + "/releases/download/" + tag + "/" + name,
            MetadataUrl = "https://api.github.com/repos/" + repo + "/releases/tags/" + tag, Name = name };

        internal static async Task Resolve(OfficialPackage p, PackageDownloader downloader, CancellationToken ct)
        {
            if (p.MetadataUrl != null)
            {
                string metadata = await downloader.TextAsync(p.MetadataUrl, ct).ConfigureAwait(false);
                if (p.Url == null && !p.Latest) p.Url = EdenUrl(metadata);
                else
                {
                    var data = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.DeserializeObject(metadata) as Dictionary<string, object>;
                    if (data == null || !data.TryGetValue("assets", out var assets) || !(assets is object[] list)) throw new NexoException("La publicación oficial no tiene una lista de archivos válida.");
                    var asset = list.OfType<Dictionary<string, object>>().SingleOrDefault(a => a.TryGetValue("name", out var n) && (string)n == p.Name);
                    if (p.Latest && asset != null && asset.TryGetValue("browser_download_url", out var latestUrl) && latestUrl is string u &&
                        u.StartsWith("https://github.com/nova5emu/nova5/releases/download/", StringComparison.Ordinal)) p.Url = u;
                    if (asset == null || !asset.TryGetValue("browser_download_url", out var url) || (string)url != p.Url) throw new NexoException("El archivo ya no coincide con la publicación oficial verificada.");
                    if (p.Latest && !(asset.TryGetValue("digest", out var latestDigest) && latestDigest is string ld && ld.StartsWith("sha256:", StringComparison.Ordinal)))
                        throw new NexoException("La publicación de Nova5 no trae huella SHA-256; no se instala sin verificar.");
                    if (asset.TryGetValue("digest", out var digest) && digest is string d && !string.IsNullOrEmpty(d))
                    {
                        if (!Regex.IsMatch(d, "^sha256:[0-9a-fA-F]{64}$")) throw new NexoException("La publicación usa una huella que Nexo no puede verificar.");
                        if (p.Hash != null && !p.Hash.Equals(d.Substring(7), StringComparison.OrdinalIgnoreCase)) throw new NexoException("La huella oficial cambió respecto a la publicación fijada.");
                        p.Hash = d.Substring(7);
                    }
                }
            }
            if (p.ChecksumUrl != null)
            {
                string text = await downloader.TextAsync(p.ChecksumUrl, ct).ConfigureAwait(false);
                var match = Regex.Match(text.Trim(), @"^([0-9a-fA-F]{64})(?:\s+\*?([^\r\n]+))?$");
                if (!match.Success || match.Groups[2].Success && match.Groups[2].Value != p.Name) throw new NexoException("La huella oficial SHA256 no es válida.");
                string hash = match.Groups[1].Value;
                if (p.Hash != null && !p.Hash.Equals(hash, StringComparison.OrdinalIgnoreCase)) throw new NexoException("Las huellas oficiales no coinciden.");
                p.Hash = hash;
            }
        }
        internal static string EdenUrl(string html)
        {
            // Resolve ONLY the Windows MSVC artifact linked by the pinned official release.
            // The generic MSVC build avoids the MinGW v3/Zen4-only CPU requirement.
            var links = Regex.Matches(html, "href=[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase).Cast<Match>()
                .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).Distinct().Where(s =>
                    Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "stable.eden-emu.dev" && u.Port == 443 &&
                    u.UserInfo == "" && u.AbsolutePath.IndexOf("0.2.1", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    u.AbsolutePath.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    u.AbsolutePath.IndexOf("msvc", StringComparison.OrdinalIgnoreCase) >= 0 && u.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (links.Length != 1) throw new NexoException("No se pudo identificar un único paquete Windows MSVC en la publicación oficial de Eden 0.2.1.");
            return links[0];
        }
        internal static void Verify(string path, string expected, CancellationToken ct = default(CancellationToken))
        {
            if (expected == null) return;
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(path))
            {
                byte[] buffer = new byte[262144]; int n;
                while ((n = file.Read(buffer, 0, buffer.Length)) > 0) { ct.ThrowIfCancellationRequested(); sha.TransformBlock(buffer, 0, n, buffer, 0); }
                sha.TransformFinalBlock(buffer, 0, 0);
                if (!BitConverter.ToString(sha.Hash).Replace("-", "").Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new NexoException("La descarga no coincide con el SHA256 oficial. No se instalará.");
            }
        }
    }

    internal sealed class PackageDownloader : IDisposable
    {
        private readonly HttpClient client;
        private readonly Func<Uri, bool> allowed;
        // allowed: hosts this downloader may reach, redirects included (by default the official emulator sources).
        internal PackageDownloader(HttpMessageHandler handler = null, Func<Uri, bool> allowed = null)
        {
            this.allowed = allowed ?? Allowed;
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nexo/1.0");
        }
        internal static bool Allowed(Uri u) => u != null && u.IsAbsoluteUri && u.Scheme == "https" && u.Port == 443 && u.UserInfo == "" &&
            new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com",
                "dl.dolphin-emu.org", "buildbot.libretro.com", "git.eden-emu.dev", "stable.eden-emu.dev" }.Contains(u.Host);
        internal async Task<string> TextAsync(string url, CancellationToken ct)
        {
            using (var stream = new MemoryStream())
            { await Transfer(url, stream, 4 * 1024 * 1024, null, ct).ConfigureAwait(false); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        internal async Task Download(string url, string destination, IProgress<SetupProgress> progress, CancellationToken ct)
        {
            if (SafePaths.HasReparsePoint(destination)) throw new NexoException("La descarga contiene un enlace.");
            using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 262144, true))
                await Transfer(url, file, 4L * 1024 * 1024 * 1024, progress, ct).ConfigureAwait(false);
        }
        private async Task Transfer(string url, Stream output, long limit, IProgress<SetupProgress> progress, CancellationToken ct)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromMinutes(30));
                try
                {
                    var uri = new Uri(url);
                    for (int i = 0; i < 6; i++)
                    {
                        if (!allowed(uri)) throw new NexoException("La descarga salió de los servidores oficiales permitidos.");
                        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                        {
                            int code = (int)response.StatusCode;
                            if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                            {
                                if (response.Headers.Location == null) throw new NexoException("Redirección oficial sin destino.");
                                uri = new Uri(uri, response.Headers.Location); continue;
                            }
                            if (!response.IsSuccessStatusCode) throw new NexoException("El servidor oficial respondió HTTP " + code + ". Inténtalo más tarde.");
                            long? total = response.Content.Headers.ContentLength;
                            if (total > limit) throw new NexoException("La descarga supera el tamaño máximo permitido.");
                            if (output is FileStream f && total.HasValue && new DriveInfo(Path.GetPathRoot(f.Name)).AvailableFreeSpace < total.Value + 67108864)
                                throw new NexoException("No hay espacio libre suficiente para descargar el emulador.");
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (deadline.Token.Register(() => input.Dispose()))
                            {
                                byte[] buffer = new byte[262144]; long done = 0; int n;
                                while ((n = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
                                {
                                    done += n;
                                    if (done > limit) throw new NexoException("La descarga supera el límite de seguridad.");
                                    await output.WriteAsync(buffer, 0, n, deadline.Token).ConfigureAwait(false);
                                    progress?.Report(new SetupProgress("Descargando", done, total));
                                }
                                if (total.HasValue && total.Value != done) throw new NexoException("La descarga está incompleta.");
                            }
                            return;
                        }
                    }
                    throw new NexoException("Demasiadas redirecciones en la descarga oficial.");
                }
                catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
                catch (OperationCanceledException ex) { throw new NexoException("La descarga agotó su tiempo de espera.", ex); }
            }
        }
        public void Dispose() => client.Dispose();
    }
}
