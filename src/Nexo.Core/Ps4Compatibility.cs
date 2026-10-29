using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    // The release generator, not the issue search API, defines this schema.
    internal static class Ps4Compatibility
    {
        internal const int MaxBytes = 8 * 1024 * 1024;
        internal const string Reports = "https://github.com/shadps4-compatibility/shadps4-game-compatibility/issues/";
        internal static bool ValidId(string id) => id != null && Regex.IsMatch(id, @"^CUSA[0-9]{5}$");
        internal static Dictionary<string, GameCompatibility> Parse(byte[] bytes, CancellationToken ct)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException("Tamaño de compatibilidad no válido.");
            string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            var root = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 16 }.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null || root.Count == 0 || root.Count > 50000) throw new InvalidDataException("La base de compatibilidad está vacía o no es válida.");
            var result = new Dictionary<string, GameCompatibility>(StringComparer.Ordinal);
            foreach (var item in root)
            {
                ct.ThrowIfCancellationRequested();
                if (!ValidId(item.Key) || !(item.Value is Dictionary<string, object> systems) || systems.Count > 32) continue;
                // Windows always wins, even if its result is worse/older or unknown.
                string os = systems.ContainsKey("os-windows") ? "os-windows" : systems.Keys
                    .Where(k => k == "os-linux" || k == "os-macOS" || k == "os-macos" || k == "os-unknown")
                    .OrderByDescending(k => ParseDate((systems[k] as Dictionary<string, object>), "last_tested"))
                    .ThenBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                if (os == null || !(systems[os] is Dictionary<string, object> entry)) continue;
                string raw = Text(entry, "status", 64), status;
                switch (raw)
                {
                    case "status-nothing": case "nothing": status = "Nothing"; break;
                    case "status-boots": case "boots": status = "Loadable"; break;
                    case "status-menus": case "menus": status = "Menu"; break;
                    case "status-ingame": case "ingame": status = "Ingame"; break;
                    case "status-playable": case "playable": status = "Playable"; break;
                    case "status-unknown": case "unknown": status = "Desconocido"; break;
                    default: continue; // malformed schema must not replace the last usable cache
                }
                var tested = ParseDate(entry, "last_tested");
                string date = tested.HasValue ? tested.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
                string version = Text(entry, "version", 80);
                if (version == "unknown" || version == null || !Regex.IsMatch(version, @"^v?[0-9]+\.[0-9]+(?:\.[0-9]+)?(?:[-+][A-Za-z0-9._-]+)?$")) version = null;
                string osName = os == "os-windows" ? "Windows" : os == "os-linux" ? "Linux" : os == "os-unknown" ? "No indicado" : "macOS";
                string explanation = Explain(status) + " shadPS4 es experimental; el informe puede estar desactualizado y no garantiza el resultado en este PC.";
                explanation += os == "os-windows" ? " Informe de Windows." : " No hay informe de Windows: se muestra uno de " + osName + ".";
                if (version != null) explanation += " Versión indicada de shadPS4: " + version + ".";
                // Upstream calls this last_tested but fills it from GitHub updated_at.
                if (date != null) explanation += " Actualización del informe: " + date + ".";
                var issues = IssueLabels(entry);
                explanation += issues.Length > 0 ? " Problemas indicados: " + string.Join(", ", issues) + "." : " Este JSON no detalla etiquetas de problemas; consulta el informe.";
                result[item.Key] = new GameCompatibility(status, date, null, explanation, null, version, osName, Report(entry));
            }
            if (result.Count == 0) throw new InvalidDataException("No hay entradas reconocibles en la base de compatibilidad.");
            return result;
        }
        private static string Explain(string status)
        {
            switch (status)
            {
                case "Nothing": return "No consigue arrancar o se queda en negro.";
                case "Loadable": return "Arranca y puede mostrar imagen o sonido, pero se bloquea antes de llegar al menú.";
                case "Menu": return "Llega a los menús, pero no permite avanzar hasta el juego.";
                case "Ingame": return "Llega al juego, pero tiene problemas que pueden impedir jugar o completarlo, incluidos bloqueos.";
                case "Playable": return "Clasificado como jugable sin problemas importantes en el informe.";
                default: return "La base no indica un resultado conocido para este juego.";
            }
        }
        private static string[] IssueLabels(Dictionary<string, object> entry)
        {
            // Optional forward compatibility: the current official export omits labels.
            if (!entry.TryGetValue("labels", out var raw) || !(raw is object[] labels) || labels.Length > 128) return new string[0];
            var result = new List<string>();
            foreach (var value in labels)
            {
                string label = value as string ?? Text(value as Dictionary<string, object>, "name", 120);
                if (label == null || !label.StartsWith("issues:", StringComparison.OrdinalIgnoreCase)) continue;
                string issue = label.Substring(7).Trim().ToLowerInvariant(), translated;
                switch (issue)
                {
                    case "graphics": translated = "fallos gráficos"; break;
                    case "audio": translated = "fallos de audio"; break;
                    case "crash": case "crashes": translated = "cierres inesperados"; break;
                    case "hang": case "hangs": case "freeze": case "freezes": translated = "bloqueos"; break;
                    case "performance": translated = "rendimiento insuficiente"; break;
                    case "input": translated = "problemas de controles"; break;
                    default: translated = "etiqueta «" + issue + "»"; break;
                }
                if (translated.Length <= 160 && !translated.Any(char.IsControl)) result.Add(translated);
            }
            return result.Distinct().ToArray();
        }
        private static string Report(Dictionary<string, object> entry)
        {
            if (entry.TryGetValue("issue_number", out var number))
            {
                string value = number is string s ? s : number is int i ? i.ToString(CultureInfo.InvariantCulture) : null;
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0) return Reports + n.ToString(CultureInfo.InvariantCulture);
            }
            string url = Text(entry, "url", 256);
            return url != null && url.StartsWith(Reports, StringComparison.Ordinal) && Regex.IsMatch(url.Substring(Reports.Length), @"^[1-9][0-9]{0,9}$") ? url : null;
        }
        private static DateTimeOffset? ParseDate(Dictionary<string, object> entry, string key)
        {
            string value = Text(entry, key, 64);
            if (value == null || !Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}(?:T.*)?$")) return null;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? (DateTimeOffset?)date : null;
        }
        private static string Text(Dictionary<string, object> entry, string key, int max)
            => entry != null && entry.TryGetValue(key, out var value) && value is string s && s.Length <= max && !s.Any(char.IsControl) ? s : null;
    }

    internal sealed class Ps4CompatibilityCache
    {
        internal const string Url = "https://github.com/shadps4-compatibility/shadps4-game-compatibility/releases/latest/download/compatibility_data.json";
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private readonly string path;
        private readonly Func<CancellationToken, Task<byte[]>> download;
        private readonly Func<DateTime> now;
        private readonly Func<byte[], CancellationToken, Dictionary<string, GameCompatibility>> parse;
        private DateTime retryAfter;
        internal Ps4CompatibilityCache(string gamesFolder, Func<CancellationToken, Task<byte[]>> download = null, Func<DateTime> now = null,
            string cacheName = null, Func<byte[], CancellationToken, Dictionary<string, GameCompatibility>> parse = null)
        { path = cacheName == null ? CachePath(gamesFolder) : Path.Combine(gamesFolder, ".nexo-cache", cacheName);
          this.download = download ?? Download; this.now = now ?? (() => DateTime.UtcNow); this.parse = parse ?? Ps4Compatibility.Parse; }
        internal static string CachePath(string gamesFolder) => Path.Combine(gamesFolder, ".nexo-cache", "shadps4-compatibility.json");
        internal static Dictionary<string, GameCompatibility> Read(string gamesFolder, CancellationToken ct)
        {
            try { return ReadFile(CachePath(gamesFolder), ct); }
            catch (Exception ex) when (Optional(ex)) { return new Dictionary<string, GameCompatibility>(); }
        }
        internal static Dictionary<string, GameCompatibility> ReadFile(string file, CancellationToken ct,
            Func<byte[], CancellationToken, Dictionary<string, GameCompatibility>> parse = null)
        {
            ct.ThrowIfCancellationRequested();
            if (!SafePaths.IsRegularFile(file)) return new Dictionary<string, GameCompatibility>();
            byte[] bytes;
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length > Ps4Compatibility.MaxBytes) throw new InvalidDataException("Caché demasiado grande.");
                bytes = new byte[(int)stream.Length]; int n = 0;
                while (n < bytes.Length) { ct.ThrowIfCancellationRequested(); int read = stream.Read(bytes, n, bytes.Length - n); if (read == 0) throw new EndOfStreamException(); n += read; }
            }
            return (parse ?? Ps4Compatibility.Parse)(bytes, ct);
        }
        internal Task<bool> RefreshAsync(CancellationToken ct) => Task.Run(async () =>
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            string temp = null;
            try
            {
                var time = now(); if (time < retryAfter) return false;
                if (SafePaths.HasReparsePoint(path)) return false;
                if (File.Exists(path))
                {
                    var age = time - File.GetLastWriteTimeUtc(path);
                    if (age >= TimeSpan.Zero && age < TimeSpan.FromDays(7))
                    {
                        try { if (ReadFile(path, ct, parse).Count > 0) return false; }
                        catch (Exception ex) when (Optional(ex)) { }
                    }
                }
                retryAfter = time.AddHours(1);
                var bytes = await download(ct).ConfigureAwait(false);
                parse(bytes, ct); // never replace a good cache with HTML/invalid JSON
                ct.ThrowIfCancellationRequested();
                if (SafePaths.HasReparsePoint(path)) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
                File.SetLastWriteTimeUtc(temp, time);
                ct.ThrowIfCancellationRequested();
                if (SafePaths.HasReparsePoint(path)) return false;
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                temp = null; retryAfter = DateTime.MinValue;
                return true;
            }
            catch (OperationCanceledException) { retryAfter = DateTime.MinValue; throw; }
            catch (Exception ex) when (Optional(ex) || ex is HttpRequestException) { return false; }
            finally
            {
                try { if (temp != null && File.Exists(temp) && !SafePaths.HasReparsePoint(temp)) File.Delete(temp); }
                catch (Exception ex) when (Optional(ex)) { }
                Gate.Release();
            }
        }, ct);
        private static bool Optional(Exception e) => e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is OverflowException || e is NotSupportedException;
        private static async Task<byte[]> Download(CancellationToken ct)
        { using (var client = new Ps4CompatibilityDownload()) return await client.GetAsync(ct).ConfigureAwait(false); }
    }

    internal sealed class Ps4CompatibilityDownload : IDisposable
    {
        private readonly HttpClient client;
        private readonly TimeSpan timeout;
        private readonly string url;
        private readonly Func<Uri, bool> allowed;
        internal Ps4CompatibilityDownload(HttpMessageHandler handler = null, TimeSpan? timeout = null, string url = null, Func<Uri, bool> allowed = null)
        {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false });
            client.Timeout = Timeout.InfiniteTimeSpan; client.DefaultRequestHeaders.UserAgent.ParseAdd("Nexo/1.0 (PS4 compatibility)");
            this.timeout = timeout ?? TimeSpan.FromSeconds(20);
            this.url = url ?? Ps4CompatibilityCache.Url; this.allowed = allowed ?? Allowed;
        }
        internal static bool Allowed(Uri uri) => uri != null && uri.IsAbsoluteUri && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
            (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/shadps4-compatibility/shadps4-game-compatibility/releases/", StringComparison.Ordinal) ||
             uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        internal async Task<byte[]> GetAsync(CancellationToken ct)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(timeout);
                try
                {
                    var uri = new Uri(url);
                    for (int i = 0; i < 5; i++)
                    {
                        if (!allowed(uri)) throw new InvalidDataException("Redirección de compatibilidad no permitida.");
                        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                        {
                            int code = (int)response.StatusCode;
                            if (code == 301 || code == 302 || code == 303 || code == 307 || code == 308)
                            { if (response.Headers.Location == null) throw new InvalidDataException("Redirección sin destino."); uri = new Uri(uri, response.Headers.Location); continue; }
                            response.EnsureSuccessStatusCode();
                            if (response.Content.Headers.ContentLength > Ps4Compatibility.MaxBytes) throw new InvalidDataException("Base de compatibilidad demasiado grande.");
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new MemoryStream())
                            using (deadline.Token.Register(() => input.Dispose()))
                            {
                                var buffer = new byte[32768]; int read;
                                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
                                { if (output.Length + read > Ps4Compatibility.MaxBytes) throw new InvalidDataException("Base de compatibilidad demasiado grande."); output.Write(buffer, 0, read); }
                                deadline.Token.ThrowIfCancellationRequested();
                                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength != output.Length) throw new InvalidDataException("Descarga de compatibilidad incompleta.");
                                return output.ToArray();
                            }
                        }
                    }
                    throw new InvalidDataException("Demasiadas redirecciones.");
                }
                catch (Exception) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                { throw new IOException("La descarga de compatibilidad agotó su tiempo de espera.", ex); }
            }
        }
        public void Dispose() => client.Dispose();
    }
}
