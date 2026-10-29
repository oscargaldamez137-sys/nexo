using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nexo.Core
{
    internal sealed class ArtworkSources
    {
        internal List<Uri> Covers { get; } = new List<Uri>();
        internal List<Uri> Backgrounds { get; } = new List<Uri>();
        internal static ArtworkSources For(GameEntry game, ArtworkPaths paths, CancellationToken ct)
        {
            var result = new ArtworkSources();
            string id, repo = null;
            switch (game.SystemId)
            {
                case "GBC": repo = Path.GetExtension(game.Path).Equals(".gb", StringComparison.OrdinalIgnoreCase) ? "Nintendo_-_Game_Boy" : "Nintendo_-_Game_Boy_Color"; break;
                case "GBA": repo = "Nintendo_-_Game_Boy_Advance"; break;
                case "Xbox": repo = "Microsoft_-_Xbox"; break;
                case "Xbox360": repo = "Microsoft_-_Xbox_360"; break;
                case "PS2":
                    repo = "Sony_-_PlayStation_2";
                    try { id = ArtworkMetadata.Ps2Serial(game.Path, ct); }
                    catch (Exception ex) when (ArtworkMetadata.OptionalFailure(ex)) { id = null; }
                    if (id != null) result.Covers.Add(new Uri("https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/" + id + ".jpg"));
                    break;
                case "PS3":
                    repo = "Sony_-_PlayStation_3";
                    id = Metadata.ReadSfoString(Path.Combine(paths.ContentFolder, "PARAM.SFO"), "TITLE_ID");
                    if (id != null && Regex.IsMatch(id, "^[A-Z]{4}[0-9]{5}$"))
                        foreach (string variant in new[] { "coverHQ", "coverM", "cover" })
                            foreach (var region in Regions(id[2], true)) result.Covers.Add(new Uri("https://art.gametdb.com/ps3/" + variant + "/" + region + "/" + id + ".jpg"));
                    break;
                case "GameCube-Wii":
                    var disc = ArtworkMetadata.NintendoDisc(game.Path);
                    id = disc?.Item1;
                    if (disc != null) repo = disc.Item2 == "Wii" ? "Nintendo_-_Wii" : "Nintendo_-_GameCube";
                    if (id != null)
                        foreach (string variant in new[] { "coverHQ", "coverM", "cover" })
                            foreach (var region in Regions(id[3], false)) result.Covers.Add(new Uri("https://art.gametdb.com/wii/" + variant + "/" + region + "/" + id + ".png"));
                    break;
                case "PSP": repo = "Sony_-_PlayStation_Portable"; break;
                case "PS4": repo = "Sony_-_PlayStation_4"; break;
                case "Retro":
                    switch (Path.GetExtension(game.Path).ToLowerInvariant())
                    {
                        case ".nes": repo = "Nintendo_-_Nintendo_Entertainment_System"; break;
                        case ".sfc": case ".smc": repo = "Nintendo_-_Super_Nintendo_Entertainment_System"; break;
                        case ".gba": repo = "Nintendo_-_Game_Boy_Advance"; break;
                        case ".gb": repo = "Nintendo_-_Game_Boy"; break;
                        case ".gbc": repo = "Nintendo_-_Game_Boy_Color"; break;
                        case ".md": case ".gen": case ".smd": repo = "Sega_-_Mega_Drive_-_Genesis"; break;
                        case ".n64": case ".z64": case ".v64": repo = "Nintendo_-_Nintendo_64"; break;
                        case ".pce": repo = "NEC_-_PC_Engine_-_TurboGrafx_16"; break;
                        case ".sms": repo = "Sega_-_Master_System_-_Mark_III"; break;
                        case ".gg": repo = "Sega_-_Game_Gear"; break;
                    }
                    break;
            }
            if (repo != null)
            {
                // Libretro uses the complete No-Intro/Redump name, including region tags.
                string name = Regex.Replace(paths.Stem, "[&*/:`<>?\\\\|\"]", "_");
                if (game.SystemId == "Xbox360" && Path.GetExtension(game.Path).Length == 0)
                    name = Regex.Replace(game.Title, "[&*/:`<>?\\\\|\"]", "_");
                string prefix = "https://raw.githubusercontent.com/libretro-thumbnails/" + repo + "/master/";
                result.Covers.Add(new Uri(prefix + "Named_Boxarts/" + Uri.EscapeDataString(name) + ".png"));
            }
            return result;
        }

        private static IEnumerable<string> Regions(char code, bool ps3)
        {
            string region;
            if (ps3) region = code == 'U' ? "US" : code == 'J' ? "JA" : code == 'K' ? "KO" : code == 'A' ? "ZH" : "EN";
            else
                switch (code)
                {
                    case 'E': region = "US"; break; case 'J': region = "JA"; break; case 'D': region = "DE"; break;
                    case 'F': region = "FR"; break; case 'S': region = "ES"; break; case 'I': region = "IT"; break;
                    case 'K': region = "KO"; break; case 'W': region = "ZH"; break; default: region = "EN"; break;
                }
            return new[] { region, "EN", "US" }.Distinct();
        }
    }
}
