using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Nexo.Core
{
    internal static class XboxCompatibility
    {
        internal const string Url = "https://xemu.app/compat.json", CacheName = "xemu-compatibility.json";
        internal static Dictionary<string, GameCompatibility> Parse(byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (bytes == null || bytes.Length > Ps4Compatibility.MaxBytes) throw new InvalidDataException("Lista xemu demasiado grande.");
            var list = new JavaScriptSerializer { MaxJsonLength = Ps4Compatibility.MaxBytes, RecursionLimit = 24 }
                .DeserializeObject(new UTF8Encoding(false, true).GetString(bytes)) as object[];
            if (list == null || list.Length == 0 || list.Length > 20000) throw new InvalidDataException("Lista xemu no reconocida.");
            var result = new Dictionary<string, GameCompatibility>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                ct.ThrowIfCancellationRequested(); var row = item as Dictionary<string, object>;
                if (row == null || !row.TryGetValue("url", out var u) || !(u is string url) || url.Length > 512 ||
                    !row.TryGetValue("status", out var s) || !(s is string status)) continue;
                var match = Regex.Match(url, @"^/titles/([a-fA-F0-9]{8})(?:/|#|$)");
                if (!match.Success) continue;
                string mapped, explanation;
                switch (status)
                {
                    case "Broken": mapped = "Nothing"; explanation = "No arranca o falla inmediatamente."; break;
                    case "Intro": mapped = "Intro"; explanation = "Muestra la introducción, pero no llega a jugarse."; break;
                    case "Starts": mapped = "Ingame"; explanation = "Llega a jugarse, pero puede bloquearse o tener problemas importantes."; break;
                    case "Playable": mapped = "Playable"; explanation = "Jugable con fallos menores."; break;
                    case "Perfect": mapped = "Perfect"; explanation = "La lista lo considera jugable de principio a fin sin fallos apreciables."; break;
                    case "Unknown": mapped = "Desconocido"; explanation = "No hay una prueba registrada."; break;
                    default: continue;
                }
                string id = match.Groups[1].Value.ToUpperInvariant();
                result[id] = new GameCompatibility(mapped, null, null, "xemu: " + explanation + " El resultado depende de la versión, el juego y el PC; la lista resumida no indica el sistema operativo probado.", null,
                    reportUrl: "https://xemu.app/titles/" + id.ToLowerInvariant() + "/");
            }
            if (result.Count == 0) throw new InvalidDataException("La lista xemu no contiene títulos reconocidos.");
            return result;
        }
        internal static Dictionary<string, GameCompatibility> Read(string folder, CancellationToken ct)
        {
            try { return Ps4CompatibilityCache.ReadFile(Path.Combine(folder, ".nexo-cache", CacheName), ct, Parse); }
            catch (Exception e) when (ArtworkMetadata.OptionalFailure(e) || e is InvalidOperationException) { return new Dictionary<string, GameCompatibility>(); }
        }
        internal static Ps4CompatibilityCache Cache(string folder, Func<CancellationToken, Task<byte[]>> download = null, Func<DateTime> now = null)
            => new Ps4CompatibilityCache(folder, download ?? Download, now, CacheName, Parse);
        internal static bool Allowed(Uri u) => u != null && u.IsAbsoluteUri && u.Scheme == "https" && u.Host == "xemu.app" &&
            u.Port == 443 && u.UserInfo.Length == 0 && u.AbsolutePath == "/compat.json";
        private static async Task<byte[]> Download(CancellationToken ct)
        { using (var d = new Ps4CompatibilityDownload(url: Url, allowed: Allowed)) return await d.GetAsync(ct).ConfigureAwait(false); }
    }
}
