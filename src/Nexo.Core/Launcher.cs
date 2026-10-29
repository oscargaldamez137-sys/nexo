using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexo.Core
{
    internal static class Launcher
    {
        // CommandLineToArgvW/MS C runtime rules: double backslashes preceding quotes and the closing quote.
        internal static string Quote(string value)
        {
            if (value == null || value.IndexOf('\0') >= 0) throw new NexoException("Argumento de ejecución no válido.");
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char ch in value)
            {
                if (ch == '\\') { slashes++; continue; }
                if (ch == '"') result.Append('\\', slashes * 2 + 1).Append(ch);
                else result.Append('\\', slashes).Append(ch);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        // shadPS4 SDL 0.20.0 has no Qt launcher; its no-argument path only displays a notice.
        internal static string[] EmulatorArguments(string systemId) =>
            systemId == "PS4" ? new[] { "-b" } : new string[0];

        internal static string[] GameArguments(string systemId, string executable, GameEntry game, string[] configured)
        {
            string core = systemId == "Retro" ? RetroCores.Find(executable, game.Path) : null;
            if (systemId == "Retro" && core == null) throw new NexoException("No hay un núcleo compatible instalado en la carpeta cores de RetroArch.");
            string[] template = configured;
            if (template == null)
            {
                switch (systemId)
                {
                    case "GBC": case "GBA": template = new[] { "-f", "{game}" }; break;
                    case "Xbox": template = new[] { "-full-screen", "-dvd_path", "{game}" }; break;
                    case "Xbox360": template = new[] { "--fullscreen", "{game}" }; break;
                    case "Retro": template = new[] { "-f", "-L", "{core}", "{game}" }; break;
                    case "PS2": template = new[] { "-fullscreen", "--", "{game}" }; break;
                    // RPCS3's --fullscreen is only valid with --no-gui.
                    case "PS3": template = new[] { "--no-gui", "--fullscreen", "{game}" }; break;
                    case "PSP": template = new[] { "--fullscreen", "{game}" }; break;
                    // Dolphin's command-line config layer is temporary (Save does nothing).
                    case "GameCube-Wii": template = new[] { "-b", "-C", "Dolphin.Display.Fullscreen=True", "-e", "{game}" }; break;
                    case "PS4": template = new[] { "--fullscreen", "true", "-g", "{game}" }; break;
                    // Through the Nova5 launcher the game gets Nova5's settings, patches and play time.
                    case "PS5": template = string.Equals(Path.GetFileName(executable), "Nova5.exe", StringComparison.OrdinalIgnoreCase)
                        ? new[] { "--play", "{game}" } : new[] { "--fullscreen", "--game", "{game}" }; break;
                    case "Switch":
                        string name = Path.GetFileName(executable);
                        // Verified against Eden 0.2.1. Don't assume another fork's CLI.
                        template = string.Equals(name, "eden.exe", StringComparison.OrdinalIgnoreCase)
                            ? new[] { "-f", "-g", "{game}" }
                            : string.Equals(name, "Ryujinx.exe", StringComparison.OrdinalIgnoreCase)
                                ? new[] { "{game}" } : new[] { "-g", "{game}" }; break;
                    default: template = new[] { "{game}" }; break;
                }
            }
            if (!template.Any(s => s.IndexOf("{game}", StringComparison.Ordinal) >= 0 || s.IndexOf("{gameFolder}", StringComparison.Ordinal) >= 0))
                throw new NexoException("gameArguments debe incluir {game} o {gameFolder}.");
            var folder = Path.GetDirectoryName(game.Path);
            return template.Select(arg => arg.Replace("{game}", game.Path).Replace("{gameFolder}", folder)
                .Replace("{core}", core ?? "")).ToArray();
        }

        internal static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
        {
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable),
                Arguments = string.Join(" ", arguments.Select(Quote))
            };
            if (info.Arguments.Length > 30000) throw new NexoException("Los argumentos de ejecución son demasiado largos.");
            return info;
        }

        internal static int Start(ProcessStartInfo info)
        {
            try
            {
                using (var process = Process.Start(info))
                {
                    if (process == null) throw new NexoException("Windows no pudo iniciar la aplicación.");
                    return process.Id;
                }
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is IOException)
            { throw new NexoException("Windows no pudo abrir la aplicación. Comprueba el ejecutable y sus archivos necesarios.", ex); }
        }
    }
}
