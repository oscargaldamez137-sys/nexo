using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nexo.Core
{
    internal sealed class ArtworkDownload
    {
        internal byte[] Bytes;
        internal bool NotFound;
    }
    internal sealed class ArtworkDownloader : IDisposable
    {
        internal const int MaxBytes = 8 * 1024 * 1024;
        private readonly HttpClient client;
        private readonly TimeSpan timeout;
        internal ArtworkDownloader(HttpMessageHandler handler = null, TimeSpan? timeout = null)
        {
            client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false });
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nexo/1.0 (game artwork)");
            this.timeout = timeout ?? TimeSpan.FromSeconds(12);
        }
        internal static bool Allowed(Uri uri) => uri != null && uri.IsAbsoluteUri && uri.Scheme == "https" && uri.Port == 443 &&
            uri.UserInfo.Length == 0 && (uri.Host == "raw.githubusercontent.com" || uri.Host == "art.gametdb.com");

        internal async Task<ArtworkDownload> GetAsync(Uri uri, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!Allowed(uri)) return new ArtworkDownload();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(timeout);
                try
                {
                    string host = uri.Host;
                    for (int redirect = 0; redirect <= 3; redirect++)
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                        {
                            int status = (int)response.StatusCode;
                            if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                            {
                                var location = response.Headers.Location;
                                if (location == null) return new ArtworkDownload();
                                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                                if (!Allowed(uri) || uri.Host != host) return new ArtworkDownload();
                                continue;
                            }
                            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Gone)
                                return new ArtworkDownload { NotFound = true };
                            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes) return new ArtworkDownload();
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new MemoryStream())
                            using (deadline.Token.Register(() => input.Dispose()))
                            {
                                var buffer = new byte[32768];
                                for (;;)
                                {
                                    int count = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false);
                                    if (count == 0) break;
                                    if (output.Length + count > MaxBytes) return new ArtworkDownload();
                                    output.Write(buffer, 0, count);
                                }
                                deadline.Token.ThrowIfCancellationRequested();
                                return new ArtworkDownload { Bytes = output.ToArray() };
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException || ex is ObjectDisposedException)
                { ct.ThrowIfCancellationRequested(); }
            }
            return new ArtworkDownload();
        }
        public void Dispose() => client.Dispose();
    }

    internal static class ArtworkImage
    {
        // Check dimensions before invoking an image decoder, then decode pixels to reject HTML,
        // truncated images and malformed payloads. Never trust a remote extension/MIME type.
        internal static string Extension(byte[] data)
        {
            if (data == null || data.Length < 24 || data.Length > ArtworkDownloader.MaxBytes) return null;
            int width = 0, height = 0; string extension = null;
            if (data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71 && data[4] == 13 && data[5] == 10 && data[6] == 26 && data[7] == 10 &&
                data[12] == 73 && data[13] == 72 && data[14] == 68 && data[15] == 82)
            {
                width = Big(data, 16); height = Big(data, 20); extension = ".png";
                if (data.Length < 12 || data[data.Length - 8] != 73 || data[data.Length - 7] != 69 || data[data.Length - 6] != 78 || data[data.Length - 5] != 68) return null;
            }
            else if (data[0] == 255 && data[1] == 216 && data[data.Length - 2] == 255 && data[data.Length - 1] == 217)
            {
                extension = ".jpg";
                for (int p = 2; p + 4 < data.Length;)
                {
                    if (data[p++] != 255) return null;
                    while (p < data.Length && data[p] == 255) p++;
                    if (p >= data.Length) return null;
                    int marker = data[p++];
                    if (marker == 218 || marker == 217) break;
                    if (marker == 1 || marker >= 208 && marker <= 215) continue;
                    if (p + 2 > data.Length) return null;
                    int length = data[p] * 256 + data[p + 1];
                    if (length < 2 || p + length > data.Length) return null;
                    if (marker == 192 || marker == 193 || marker == 194)
                    { if (length < 8) return null; height = data[p + 3] * 256 + data[p + 4]; width = data[p + 5] * 256 + data[p + 6]; break; }
                    p += length;
                }
            }
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 32 * 1024 * 1024) return null;
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var image = Image.FromStream(stream, false, true))
                {
                    if (image.Width != width || image.Height != height || image.RawFormat.Guid != (extension == ".png" ? ImageFormat.Png.Guid : ImageFormat.Jpeg.Guid)) return null;
                    using (var decoded = new Bitmap(image)) { decoded.GetPixel(width - 1, height - 1); }
                }
                return extension;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is System.Runtime.InteropServices.ExternalException) { return null; }
        }
        private static int Big(byte[] bytes, int at) => bytes[at] << 24 | bytes[at + 1] << 16 | bytes[at + 2] << 8 | bytes[at + 3];
    }
}
