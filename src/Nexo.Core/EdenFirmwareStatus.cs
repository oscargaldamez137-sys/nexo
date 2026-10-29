using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Nexo.Core
{
    internal static class EdenFirmwareStatus
    {
        // Diagnose the emulator's observation, not the filename/version advertised for a
        // keys pack. No secret key material is read, logged or inferred from filenames.
        internal static string Read(string emulatorFolder)
        {
            try
            {
                string log = Path.Combine(emulatorFolder, "user", "log", "eden_log.txt");
                if (!SafePaths.IsRegularFile(log)) return null;
                DateTime date = File.GetLastWriteTime(log);
                string tail;
                using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bool truncated = stream.Length > 1024 * 1024;
                    stream.Position = Math.Max(0, stream.Length - 1024 * 1024);
                    var buffer = new byte[(int)Math.Min(stream.Length, 1024 * 1024)];
                    int count = 0, read;
                    while (count < buffer.Length && (read = stream.Read(buffer, count, buffer.Length - count)) != 0) count += read;
                    tail = Encoding.UTF8.GetString(buffer, 0, count);
                    if (truncated) { int newline = tail.IndexOf('\n'); tail = newline < 0 ? "" : tail.Substring(newline + 1); }
                }
                var matches = Regex.Matches(tail, @"SystemVersion[^\r\n]{0,160}Using hardcoded firmware version[^\r\n]{0,80}?(\d{1,3}\.\d{1,3}\.\d{1,3})", RegexOptions.CultureInvariant);
                if (matches.Count == 0) return null;
                string version = matches[matches.Count - 1].Groups[1].Value;
                string message = " Aviso de firmware: en el último registro de Eden (" + date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                    ") no se pudo leer SystemVersion de la NAND; se usó la versión interna " + version + ". Revisa la instalación completa del firmware y sus claves compatibles. Este aviso no demuestra que las claves sean antiguas ni que esa sea la versión máxima admitida.";
                if (tail.Contains("ErrorMissingKeyAreaKey") || tail.Contains("ErrorMissingTitlekey") || tail.Contains("ErrorMissingTitlekek"))
                    message += " El registro también contiene un error explícito de claves ausentes; puede corresponder al firmware o a un juego.";
                return message;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { return " No se pudo consultar el último registro de firmware de Eden; el lanzamiento puede continuar."; }
        }
    }
}
