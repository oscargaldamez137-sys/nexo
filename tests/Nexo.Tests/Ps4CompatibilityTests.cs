using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Nexo.Core;

internal static class Ps4CompatibilityTests
{
    private static readonly DateTime Now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    internal static void RunAll(Action<string, Action> run, string workspace)
    {
        void Test(string name, Action<string, NexoService> action) => run(name, () =>
        {
            string root = Path.Combine(workspace, ".nexo-test-ps4compat-" + Guid.NewGuid().ToString("N"));
            try { action(root, new NexoService(root)); }
            finally { if (!SafePaths.Within(root, workspace) || !Path.GetFileName(root).StartsWith(".nexo-test-ps4compat-")) throw new Exception("Limpieza insegura"); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        Test("PS4 compatibilidad: los cinco estados usan el contrato de la UI", (root, svc) =>
        {
            var expected = new Dictionary<string, string> { { "nothing", "Nothing" }, { "boots", "Loadable" }, { "menus", "Menu" }, { "ingame", "Ingame" }, { "playable", "Playable" } };
            foreach (var pair in expected)
            {
                var value = Parse(Json("status-" + pair.Key))["CUSA09072"];
                Check(value.Status == pair.Value && value.Date == "2026-10-20" && value.EmulatorVersion == "v0.15.0");
                Check(value.OperatingSystem == "Windows" && value.ReportUrl == Ps4Compatibility.Reports + "38");
                Check(value.RecommendedUpdate == null && value.UpdateHint == null && value.Explanation.Contains("experimental"));
                Check(value.Explanation.Contains("Actualización del informe") && value.Explanation.Contains("no detalla etiquetas"));
            }
            Check(Parse(Json("status-boots"))["CUSA09072"].Explanation.Contains("imagen o sonido"));
            Check(svc.Systems.Single(s => s.Id == "PS4").Experimental);
        });
        Test("PS4 compatibilidad: Windows prevalece sobre Linux más reciente y jugable", (root, svc) =>
        {
            var data = new Dictionary<string, object> { { "CUSA09072", new Dictionary<string, object> {
                { "os-linux", Entry("status-playable", "2026-10-24T01:00:00Z") }, { "os-windows", Entry("status-ingame", "2025-01-01T00:00:00Z") } } } };
            var value = Parse(Encode(data))["CUSA09072"];
            Check(value.Status == "Ingame" && value.OperatingSystem == "Windows");
            ((Dictionary<string, object>)((Dictionary<string, object>)data["CUSA09072"])["os-windows"])["status"] = "status-unknown";
            Check(Parse(Encode(data))["CUSA09072"].Status == "Desconocido");
        });
        Test("PS4 compatibilidad: sin Windows elige otro informe y lo identifica", (root, svc) =>
        {
            var value = Parse(Json("status-playable", "os-linux"))["CUSA09072"];
            Check(value.OperatingSystem == "Linux" && value.Explanation.Contains("No hay informe de Windows"));
        });
        Test("PS4 compatibilidad: etiquetas opcionales traducidas sin inferirlas del estado", (root, svc) =>
        {
            var entry = Entry("status-ingame"); entry["labels"] = new object[] { "issues: graphics", new Dictionary<string, object> { { "name", "issues: audio" } }, "issues: crashes", "Unreal Engine" };
            var value = Parse(Encode(new Dictionary<string, object> { { "CUSA09072", new Dictionary<string, object> { { "os-windows", entry } } } }))["CUSA09072"];
            Check(value.Explanation.Contains("fallos gráficos") && value.Explanation.Contains("fallos de audio") && value.Explanation.Contains("cierres inesperados") && !value.Explanation.Contains("Unreal"));
            Check(!Parse(Json())["CUSA09072"].Explanation.Contains("fallos gráficos"));
        });
        Test("PS4 compatibilidad: campos inválidos y enlaces externos no se publican", (root, svc) =>
        {
            var entry = Entry("status-ingame", "fecha falsa"); entry["version"] = "<script>"; entry["issue_number"] = "../38"; entry["url"] = "https://example.com/issues/38";
            var value = Parse(Encode(new Dictionary<string, object> { { "CUSA09072", new Dictionary<string, object> { { "os-windows", entry } } } }))["CUSA09072"];
            Check(value.Date == null && value.EmulatorVersion == null && value.ReportUrl == null);
            foreach (string invalid in new[] { "{}", "[]", "<html>", "{\"CUSA09072\":{\"os-windows\":{\"status\":\"nuevo-desconocido\"}}}" })
                Check(!TryParse(Encoding.UTF8.GetBytes(invalid)));
        });
        Test("PS4 compatibilidad: caché vigente evita red y caducada se renueva atómicamente", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = PutCache(folder, Json(), Now.AddDays(-6)); int calls = 0;
            var cache = new Ps4CompatibilityCache(folder, ct => { calls++; return Task.FromResult(Json("status-playable")); }, () => Now);
            Check(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult() && calls == 0);
            File.SetLastWriteTimeUtc(path, Now.AddDays(-8));
            Check(cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult() && calls == 1);
            Check(Ps4CompatibilityCache.Read(folder, CancellationToken.None)["CUSA09072"].Status == "Playable");
            Check(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult() && calls == 1);
            Check(Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0);
        });
        Test("PS4 compatibilidad: sin red conserva caché antigua y limita reintentos", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = PutCache(folder, Json(), Now.AddDays(-30)); var before = File.ReadAllBytes(path); int calls = 0;
            var cache = new Ps4CompatibilityCache(folder, ct => { calls++; throw new HttpRequestException("sin red"); }, () => Now);
            Check(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult());
            Check(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult() && calls == 1);
            Check(before.SequenceEqual(File.ReadAllBytes(path)) && Ps4CompatibilityCache.Read(folder, CancellationToken.None)["CUSA09072"].Status == "Ingame");
        });
        Test("PS4 compatibilidad: respuesta inválida no reemplaza copia válida", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = PutCache(folder, Json(), Now.AddDays(-30)); var before = File.ReadAllBytes(path);
            foreach (string text in new[] { "{}", "<html>error</html>", "{roto" })
            {
                var cache = new Ps4CompatibilityCache(folder, ct => Task.FromResult(Encoding.UTF8.GetBytes(text)), () => Now);
                Check(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult() && before.SequenceEqual(File.ReadAllBytes(path)));
            }
        });
        Test("PS4 compatibilidad: fallo de escritura conserva caché y limpia temporal", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = PutCache(folder, Json(), Now.AddDays(-30)); var before = File.ReadAllBytes(path);
            var cache = new Ps4CompatibilityCache(folder, ct => Task.FromResult(Json("status-playable")), () => Now);
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) Check(!cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult());
            Check(before.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0);
        });
        Test("PS4 compatibilidad: peticiones simultáneas comparten una sola renovación", (root, svc) =>
        {
            int calls = 0; var cache = new Ps4CompatibilityCache(svc.GetStatus("PS4").GamesFolder, ct => { calls++; return Task.FromResult(Json()); }, () => Now);
            var jobs = Enumerable.Range(0, 8).Select(i => cache.RefreshAsync(CancellationToken.None)).ToArray(); Task.WaitAll(jobs);
            Check(calls == 1 && jobs.Count(t => t.Result) == 1);
        });
        Test("PS4 compatibilidad: caché corrupta, Title ID ausente y PS5 dan desconocido", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder;
            var db = new CompatibilityDatabase("PS4", null, folder);
            Check(db.LookupPs4(null, CancellationToken.None).Status == "Desconocido");
            Check(db.Lookup(new GameEntry("PS4", "Sin SFO", Path.Combine(folder, "eboot.bin")), CancellationToken.None).Status == "Desconocido");
            PutCache(folder, Encoding.UTF8.GetBytes("roto"), Now);
            Check(new CompatibilityDatabase("PS4", null, folder).LookupPs4("CUSA09072", CancellationToken.None).Status == "Desconocido");
            var cache = new Ps4CompatibilityCache(folder, ct => Task.FromResult(Json()), () => Now);
            Check(cache.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult());
            Check(new CompatibilityDatabase("PS4", null, folder).LookupPs4("CUSA99999", CancellationToken.None).Status == "Desconocido");
            Check(new CompatibilityDatabase("PS5", null, folder).LookupPs4("CUSA09072", CancellationToken.None).Status == "Desconocido");
        });
        Test("PS4 compatibilidad: cancelación conserva copia previa y limpia temporales", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder, path = PutCache(folder, Json(), Now.AddDays(-30)); var before = File.ReadAllBytes(path); var cts = new CancellationTokenSource();
            var cache = new Ps4CompatibilityCache(folder, ct => { cts.Cancel(); return Task.FromResult(Json("status-playable")); }, () => Now);
            try { cache.RefreshAsync(cts.Token).GetAwaiter().GetResult(); throw new Exception("No canceló"); } catch (OperationCanceledException) { }
            Check(before.SequenceEqual(File.ReadAllBytes(path)) && Directory.GetFiles(Path.GetDirectoryName(path), "*.tmp").Length == 0);
        });
        Test("PS4 compatibilidad: escaneo de PKG y juego usa la misma copia sin depender de red", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder; PutCache(folder, Json(), Now.AddDays(-90));
            string pkg = Ps4PkgTests.Make(folder, "base.pkg");
            var pending = svc.ScanGamesDetailedAsync("PS4").GetAwaiter().GetResult().Archives.Single();
            Check(pending.TitleId == "CUSA09072" && pending.Compatibility.Status == "Ingame" && pending.Compatibility.ReportUrl.EndsWith("/38"));
            Ps4PkgInstaller.InstallAsync(pending, folder, null, CancellationToken.None, p => File.Delete(p)).GetAwaiter().GetResult();
            var game = svc.ScanGamesDetailedAsync("PS4").GetAwaiter().GetResult().Games.Single();
            Check(game.Compatibility.Status == "Ingame" && game.Compatibility.EmulatorVersion == "v0.15.0");
        });
        Test("PS4 compatibilidad: descarga pendiente nunca retiene el escaneo", (root, svc) =>
        {
            string folder = svc.GetStatus("PS4").GamesFolder; Ps4PkgTests.Make(folder, "base.pkg");
            var entered = new ManualResetEventSlim(); var source = new TaskCompletionSource<byte[]>();
            var cache = new Ps4CompatibilityCache(folder, ct => { entered.Set(); return source.Task; }, () => Now);
            var refresh = cache.RefreshAsync(CancellationToken.None);
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(3)));
                var scan = svc.ScanGamesDetailedAsync("PS4"); Check(scan.Wait(TimeSpan.FromSeconds(3)) && scan.Result.Archives.Single().Compatibility.Status == "Desconocido");
            }
            finally { source.SetResult(Json()); refresh.GetAwaiter().GetResult(); entered.Dispose(); }
        });
        Test("PS4 compatibilidad HTTP: redirecciones oficiales y límite de descarga", (root, svc) =>
        {
            int calls = 0; var handler = new FakeHttp((r, ct) =>
            {
                calls++;
                if (calls == 1) { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://release-assets.githubusercontent.com/example.json"); return Task.FromResult(response); }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Json()) });
            });
            using (var client = new Ps4CompatibilityDownload(handler)) Check(Parse(client.GetAsync(CancellationToken.None).GetAwaiter().GetResult()).Count == 1 && calls == 2);
            foreach (string url in new[] { "http://github.com/x", "https://example.com/x", "https://github.com/otro/repo/file" })
            {
                calls = 0;
                using (var client = new Ps4CompatibilityDownload(new FakeHttp((r, ct) => { calls++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(url); return Task.FromResult(response); })))
                    ExpectInvalid(() => client.GetAsync(CancellationToken.None).GetAwaiter().GetResult());
                Check(calls == 1);
            }
            using (var client = new Ps4CompatibilityDownload(new FakeHttp((r, ct) =>
            { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[0]) }; response.Content.Headers.ContentLength = Ps4Compatibility.MaxBytes + 1; return Task.FromResult(response); })))
                ExpectInvalid(() => client.GetAsync(CancellationToken.None).GetAwaiter().GetResult());
        });
        Test("PS4 compatibilidad HTTP: timeout conserva caché y cancelación se propaga", (root, svc) =>
        {
            using (var client = new Ps4CompatibilityDownload(new FakeHttp(async (r, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; }), TimeSpan.FromMilliseconds(30)))
            {
                try { client.GetAsync(CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Sin timeout"); } catch (IOException) { }
            }
            var cts = new CancellationTokenSource(); cts.Cancel();
            using (var client = new Ps4CompatibilityDownload(new FakeHttp((r, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); })))
            { try { client.GetAsync(cts.Token).GetAwaiter().GetResult(); throw new Exception("Sin cancelación"); } catch (OperationCanceledException) { } }
        });
    }
    private sealed class FakeHttp : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action;
        internal FakeHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) { this.action = action; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => action(r, ct);
    }
    private static Dictionary<string, object> Entry(string status = "status-ingame", string date = "2026-10-20T10:30:00Z") =>
        new Dictionary<string, object> { { "status", status }, { "last_tested", date }, { "version", "v0.15.0" }, { "issue_number", "38" } };
    private static byte[] Json(string status = "status-ingame", string os = "os-windows") => Encode(new Dictionary<string, object> { { "CUSA09072", new Dictionary<string, object> { { os, Entry(status) } } } });
    private static byte[] Encode(object data) => Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data));
    private static Dictionary<string, GameCompatibility> Parse(byte[] b) => Ps4Compatibility.Parse(b, CancellationToken.None);
    private static bool TryParse(byte[] b) { try { Parse(b); return true; } catch (Exception) { return false; } }
    private static string PutCache(string folder, byte[] data, DateTime time)
    { string path = Ps4CompatibilityCache.CachePath(folder); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, data); File.SetLastWriteTimeUtc(path, time); return path; }
    private static void Check(bool b) { if (!b) throw new Exception("Comprobación PS4 compatibility fallida"); }
    private static void ExpectInvalid(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Se esperaba rechazo"); }
}
