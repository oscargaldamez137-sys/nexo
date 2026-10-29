using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Nexo.Core;

// Self-update: no network, the GitHub API and the downloads are served by a fake handler.
internal static class UpdateTests
{
    private const string Release = AppUpdates.DefaultReleaseUrl;
    private const string AssetUrl = "https://github.com/oscargaldamez137-sys/nexo/releases/download/v1.0.1/Instalar-Nexo.exe";
    private static readonly Version V100 = new Version(1, 0, 0);

    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        run("Actualización: versiones con y sin «v», orden y etiquetas no válidas", () =>
        {
            Equal(new Version(1, 0, 0), AppUpdates.ParseTag("v1.0.0"));
            Equal(new Version(1, 0, 1), AppUpdates.ParseTag("1.0.1"));
            Equal(new Version(2, 3, 0), AppUpdates.ParseTag("V2.3"));
            True(AppUpdates.ParseTag("v1.0.0") < AppUpdates.ParseTag("v1.0.1"));
            True(AppUpdates.ParseTag("1.0.1") < AppUpdates.ParseTag("v1.1.0"));
            True(AppUpdates.ParseTag("v1.9.0") < AppUpdates.ParseTag("v1.10.0"));
            foreach (var bad in new[] { null, "", "latest", "v", "v1", "1.0.0-beta", "v1.0.0.0", "1.0.x", "v 1.0.0", "1..0" })
                Equal(null, AppUpdates.ParseTag(bad));
            Equal(new Version(1, 0, 0), AppUpdates.Normalize(new Version(1, 0, 0, 0)));
            Equal(new Version(1, 2, 0), AppUpdates.Normalize(new Version(1, 2)));
        });
        run("Actualización: elige Instalar-Nexo.exe entre los archivos de la versión", () =>
        {
            string sha = new string('a', 64);
            var update = AppUpdates.ParseRelease(Json("v1.0.1", Asset("Nexo.apk", "https://github.com/oscargaldamez137-sys/nexo/releases/download/v1.0.1/Nexo.apk", 50, "sha256:" + new string('b', 64)),
                Asset("Instalar-Nexo.exe", AssetUrl, 1234, "sha256:" + sha.ToUpperInvariant())), V100);
            True(update != null);
            Equal(new Version(1, 0, 1), update.Version); Equal(AssetUrl, update.Url); Equal(1234L, update.Size); Equal(sha, update.Sha256);
            Equal(null, AppUpdates.ParseRelease(Json("1.1.0", Asset("Instalar-Nexo.exe", AssetUrl, 9, null)), V100).Sha256);
        });
        run("Actualización: misma versión, anterior, borrador o prerrelease no actualizan", () =>
        {
            var asset = Asset("Instalar-Nexo.exe", AssetUrl, 10, null);
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.0", asset), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v0.9.9", asset), V100));
            Equal(null, AppUpdates.ParseRelease(Json("nightly", asset), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", asset).Replace("\"assets\"", "\"prerelease\":true,\"assets\""), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", asset).Replace("\"assets\"", "\"draft\":true,\"assets\""), V100));
            Equal(null, AppUpdates.ParseRelease("{roto", V100));
            Equal(null, AppUpdates.ParseRelease("[]", V100));
        });
        run("Actualización: sin el instalador, con enlace ajeno o huella ilegible no se ofrece", () =>
        {
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", Asset("Nexo.apk", AssetUrl.Replace("Instalar-Nexo.exe", "Nexo.apk"), 10, null)), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1"), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", Asset("Instalar-Nexo.exe", "https://github.com/otro/repo/releases/download/v1.0.1/Instalar-Nexo.exe", 10, null)), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", Asset("Instalar-Nexo.exe", "http://github.com/oscargaldamez137-sys/nexo/releases/download/v1.0.1/Instalar-Nexo.exe", 10, null)), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", Asset("Instalar-Nexo.exe", AssetUrl, 10, "md5:abc")), V100));
            Equal(null, AppUpdates.ParseRelease(Json("v1.0.1", Asset("Instalar-Nexo.exe", AssetUrl, 0, null)), V100));
        });
        run("Actualización: consulta a GitHub con cliente simulado; sin red, 404 o JSON roto devuelve null", () =>
        {
            var json = Json("v1.0.1", Asset("Instalar-Nexo.exe", AssetUrl, 3, null));
            Uri asked = null;
            var found = AppUpdates.CheckWithAsync(V100, CancellationToken.None, new FakeHandler(u => { asked = u; return Ok(json); })).GetAwaiter().GetResult();
            Equal(new Version(1, 0, 1), found.Version); Equal(Release, asked.ToString());
            Equal(null, AppUpdates.CheckWithAsync(new Version(1, 0, 1), CancellationToken.None, new FakeHandler(u => Ok(json))).GetAwaiter().GetResult());
            Equal(null, AppUpdates.CheckWithAsync(V100, CancellationToken.None, new FakeHandler(u => throw new HttpRequestException("sin red"))).GetAwaiter().GetResult());
            Equal(null, AppUpdates.CheckWithAsync(V100, CancellationToken.None, new FakeHandler(u => new HttpResponseMessage(HttpStatusCode.NotFound))).GetAwaiter().GetResult());
            Equal(null, AppUpdates.CheckWithAsync(V100, CancellationToken.None, new FakeHandler(u => Ok("<html>"))).GetAwaiter().GetResult());
            // A redirect outside GitHub is never followed.
            Equal(null, AppUpdates.CheckWithAsync(V100, CancellationToken.None, new FakeHandler(u => Redirect("https://malicioso.example/x"))).GetAwaiter().GetResult());
        });
        run("Actualización: descarga verificada por tamaño y SHA-256; se reutiliza la copia ya verificada", () => WithFolder(workspace, folder =>
        {
            var setup = Encoding.ASCII.GetBytes("MZ instalador de prueba");
            var update = Update(setup.Length, Sha(setup));
            double last = 0;
            string path = AppUpdates.DownloadWithAsync(update, folder, new Inline(p => last = p), CancellationToken.None, new FakeHandler(u => Ok(setup))).GetAwaiter().GetResult();
            True(File.ReadAllBytes(path).SequenceEqual(setup)); Equal(1.0, last);
            True(Path.GetFileName(path) == "Instalar-Nexo-1.0.1.exe");
            Equal(path, AppUpdates.DownloadWithAsync(update, folder, null, CancellationToken.None, new FakeHandler(u => throw new Exception("No debe descargar otra vez"))).GetAwaiter().GetResult());
            // Sin huella publicada basta el tamaño exacto.
            File.Delete(path);
            True(File.Exists(AppUpdates.DownloadWithAsync(Update(setup.Length, null), folder, null, CancellationToken.None, new FakeHandler(u => Ok(setup))).GetAwaiter().GetResult()));
        }));
        run("Actualización: tamaño o SHA-256 distintos se rechazan sin dejar archivos", () => WithFolder(workspace, folder =>
        {
            var setup = Encoding.ASCII.GetBytes("MZ instalador de prueba");
            Throws<NexoException>(() => AppUpdates.DownloadWithAsync(Update(setup.Length + 1, Sha(setup)), folder, null, CancellationToken.None, new FakeHandler(u => Ok(setup))).GetAwaiter().GetResult());
            Throws<NexoException>(() => AppUpdates.DownloadWithAsync(Update(setup.Length, new string('0', 64)), folder, null, CancellationToken.None, new FakeHandler(u => Ok(setup))).GetAwaiter().GetResult());
            Equal(0, Directory.GetFiles(folder).Length);
            // A cached copy that no longer matches is discarded and downloaded again.
            File.WriteAllBytes(Path.Combine(folder, "Instalar-Nexo-1.0.1.exe"), new byte[] { 1, 2, 3 });
            string path = AppUpdates.DownloadWithAsync(Update(setup.Length, Sha(setup)), folder, null, CancellationToken.None, new FakeHandler(u => Ok(setup))).GetAwaiter().GetResult();
            True(File.ReadAllBytes(path).SequenceEqual(setup));
            Throws<HttpRequestException>(() => AppUpdates.DownloadWithAsync(Update(5, null), Path.Combine(folder, "otra"), null, CancellationToken.None, new FakeHandler(u => throw new HttpRequestException("sin red"))).GetAwaiter().GetResult());
        }));
        run("Actualización: hosts permitidos y URL de prueba solo local", () =>
        {
            var github = AppUpdates.HostsFor(Release);
            True(github(new Uri(AssetUrl))); True(github(new Uri("https://release-assets.githubusercontent.com/x")));
            foreach (var bad in new[] { "http://github.com/a", "https://github.com.evil/a", "https://github.com:444/a", "https://user@github.com/a", "http://127.0.0.1:8080/a" })
                True(!github(new Uri(bad)));
            var local = AppUpdates.HostsFor("http://127.0.0.1:8123/release.json");
            True(local(new Uri("http://127.0.0.1:8123/Instalar-Nexo.exe"))); True(!local(new Uri("http://127.0.0.1:9999/x"))); True(!local(new Uri(AssetUrl)));
            string previous = Environment.GetEnvironmentVariable(AppUpdates.TestUrlVariable);
            try
            {
                Environment.SetEnvironmentVariable(AppUpdates.TestUrlVariable, "https://malicioso.example/release.json");
                Equal(Release, AppUpdates.ReleaseUrl());
                Environment.SetEnvironmentVariable(AppUpdates.TestUrlVariable, "http://127.0.0.1:8123/release.json");
                Equal("http://127.0.0.1:8123/release.json", AppUpdates.ReleaseUrl());
                Environment.SetEnvironmentVariable(AppUpdates.TestUrlVariable, null);
                Equal("https://api.github.com/repos/oscargaldamez137-sys/nexo/releases/latest", AppUpdates.ReleaseUrl());
            }
            finally { Environment.SetEnvironmentVariable(AppUpdates.TestUrlVariable, previous); }
            var json = Json("v1.0.1", Asset("Instalar-Nexo.exe", "http://127.0.0.1:8123/Instalar-Nexo.exe", 3, null));
            True(AppUpdates.ParseRelease(json, V100, "http://127.0.0.1:8123/release.json") != null);
            Equal(null, AppUpdates.ParseRelease(json, V100));
        });
        run("Actualización: un intento fallido no se repite en bucle; instalación y permisos detectados", () => WithFolder(workspace, folder =>
        {
            var v101 = new Version(1, 0, 1);
            True(!AppUpdates.AttemptFailed(v101, V100, folder));
            AppUpdates.RecordAttempt(v101, folder);
            True(AppUpdates.AttemptFailed(v101, V100, folder));
            True(!AppUpdates.AttemptFailed(v101, v101, folder));
            True(!AppUpdates.AttemptFailed(new Version(1, 0, 2), V100, folder));
            True(!AppUpdates.IsInstalled(folder));
            File.WriteAllText(Path.Combine(folder, AppUpdates.InstalledManifest), "Nexo.exe");
            True(AppUpdates.IsInstalled(folder)); True(AppUpdates.CanWrite(folder));
            True(!AppUpdates.CanWrite(Path.Combine(folder, "no-existe")));
            Equal(2, Directory.GetFiles(folder).Length); // the write probe leaves nothing behind
        }));
        run("Donar: solo se abren los enlaces fijos de Nexo", () =>
        {
            Equal("https://ko-fi.com/nova5emu", NexoLinks.Donate);
            Throws<NexoException>(() => NexoLinks.Open("https://ko-fi.com/otro"));
            Throws<NexoException>(() => NexoLinks.Open("file:///C:/Windows/notepad.exe"));
        });
    }

    private static AppUpdate Update(long size, string sha) =>
        AppUpdates.ParseRelease(Json("v1.0.1", Asset("Instalar-Nexo.exe", AssetUrl, size, sha == null ? null : "sha256:" + sha)), V100);

    private static string Json(string tag, params Dictionary<string, object>[] assets) =>
        new JavaScriptSerializer().Serialize(new Dictionary<string, object> { ["tag_name"] = tag, ["name"] = "Nexo " + tag, ["assets"] = assets });

    private static Dictionary<string, object> Asset(string name, string url, long size, string digest)
    {
        var asset = new Dictionary<string, object> { ["name"] = name, ["browser_download_url"] = url, ["size"] = size };
        if (digest != null) asset["digest"] = digest;
        return asset;
    }

    private static string Sha(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant(); }

    private static void WithFolder(string workspace, Action<string> test)
    {
        string folder = Path.Combine(workspace, ".nexo-test-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { test(folder); }
        finally
        {
            if (!SafePaths.Within(folder, workspace) || !Path.GetFileName(folder).StartsWith(".nexo-test-update-", StringComparison.Ordinal)) throw new Exception("Limpieza fuera del área de pruebas.");
            Directory.Delete(folder, true);
        }
    }

    private static HttpResponseMessage Ok(string text) => Ok(Encoding.UTF8.GetBytes(text));
    private static HttpResponseMessage Ok(byte[] data) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    private static HttpResponseMessage Redirect(string to) { var r = new HttpResponseMessage(HttpStatusCode.Found); r.Headers.Location = new Uri(to); return r; }

    private sealed class Inline : IProgress<double>
    {
        private readonly Action<double> report;
        internal Inline(Action<double> report) { this.report = report; }
        public void Report(double value) => report(value);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<Uri, HttpResponseMessage> send;
        internal FakeHandler(Func<Uri, HttpResponseMessage> send) { this.send = send; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.Headers.UserAgent.Count == 0) throw new Exception("GitHub exige User-Agent.");
            return Task.FromResult(send(request.RequestUri));
        }
    }

    private static void True(bool v) { if (!v) throw new Exception("Condición falsa."); }
    private static void Equal<T>(T a, T b) { if (!Equals(a, b)) throw new Exception("Esperado " + a + "; recibido " + b); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Se esperaba " + typeof(T).Name); }
}
