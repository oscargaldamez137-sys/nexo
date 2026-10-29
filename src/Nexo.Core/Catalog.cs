using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexo.Core
{
    internal static class Catalog
    {
        internal static readonly SystemProfile[] Profiles =
        {
            new SystemProfile(new GameSystem("Retro", "Retro", "RetroArch", "Varios fabricantes", null,
                "Una puerta a muchas generaciones. Reúne distintos sistemas mediante núcleos que se configuran dentro de RetroArch.",
                "https://www.retroarch.com/", "#7C5CFC", "#241650", "#C8BAFF", MediaKind.Cartridge),
                new[] { "retroarch.exe" }, RetroCores.Extensions,
                "ROMs .nes, .sfc, .smc, .gba, .gb, .gbc, .md, .gen, .smd, .n64, .z64, .v64, .pce, .sms y .gg. Solo aparecen si hay un núcleo compatible en la carpeta cores del emulador. No se incluyen núcleos."),
            new SystemProfile(new GameSystem("PS2", "PlayStation 2", "PCSX2", "Sony", 2000,
                "Vuelve a la generación de PlayStation 2 desde una biblioteca propia. Configura el emulador y sus controles a tu ritmo.",
                "https://pcsx2.net/", "#368CFF", "#102752", "#9BD8FF", MediaKind.DvdCase),
                new[] { "pcsx2-qt.exe", "pcsx2.exe", "pcsx2-qtx64.exe", "pcsx2-avx2.exe", "pcsx2-sse4.exe" },
                new[] { ".iso", ".chd", ".cso", ".bin", ".gz" }, "Juegos .iso, .chd, .cso, .bin o .gz. Configura antes PCSX2 y su BIOS por separado."),
            new SystemProfile(new GameSystem("PS3", "PlayStation 3", "RPCS3", "Sony", 2006,
                "La generación de PlayStation 3 en tu PC. La compatibilidad y el rendimiento dependen del juego y del equipo.",
                "https://rpcs3.net/", "#E85A87", "#46162F", "#FFB4D0", MediaKind.BluRayCase),
                new[] { "rpcs3.exe" }, new string[0],
                "Carpetas con PS3_GAME\\USRDIR\\EBOOT.BIN o USRDIR\\EBOOT.BIN; lee PARAM.SFO e ICON0.PNG. Las ISO de PS3 descifradas aparecen pendientes: pulsa Descomprimir. Las ISO cifradas se avisan y no se extraen. No instala PKG."),
            new SystemProfile(new GameSystem("PSP", "PSP", "PPSSPP", "Sony", 2004,
                "El espíritu de una portátil, en una pantalla grande. Prepara PPSSPP y sus controles para tu próxima partida.",
                "https://www.ppsspp.org/", "#F4AD47", "#49301A", "#FFE0A0", MediaKind.Umd),
                new[] { "PPSSPPWindows64.exe", "PPSSPPWindows.exe", "PPSSPP.exe" },
                new[] { ".iso", ".cso", ".pbp", ".chd", ".elf" }, "Juegos .iso, .cso, .pbp, .chd o .elf; la compatibilidad depende de la versión de PPSSPP."),
            new SystemProfile(new GameSystem("GameCube-Wii", "GameCube / Wii", "Dolphin", "Nintendo", 2001,
                "Dos consolas, un mismo lugar. Explora tus juegos de GameCube y Wii con el emulador preparado a tu gusto.",
                "https://dolphin-emu.org/", "#19BDD0", "#083C49", "#9CF4EE", MediaKind.MiniDisc),
                new[] { "Dolphin.exe" }, new[] { ".iso", ".gcm", ".wbfs", ".rvz", ".ciso", ".gcz", ".wad", ".dol", ".elf" },
                "Juegos .iso, .gcm, .wbfs, .rvz, .ciso, .gcz, .wad, .dol o .elf. Algunos contenidos requieren instalación/configuración dentro de Dolphin."),
            new SystemProfile(new GameSystem("PS4", "PlayStation 4", "shadPS4", "Sony", 2013,
                "Tus juegos de PlayStation 4, organizados como una colección de discos. La compatibilidad depende del emulador y del juego.",
                "https://shadps4.net/", "#467BFF", "#10204D", "#9BBFFF", MediaKind.Disc, true),
                new[] { "shadPS4.exe" }, new string[0],
                "Carpetas con eboot.bin y sce_sys\\param.sfo o param.json. Los PKG fake/de desarrollo aparecen pendientes: instala el base junto al parche y los DLC del mismo Title ID (misma carpeta o subcarpeta DLC). Portada: sce_sys\\icon0.png."),
            new SystemProfile(new GameSystem("Switch", "Nintendo Switch", "Ryujinx / familia yuzu", "Nintendo", 2017,
                "Una estantería de tarjetas para tus juegos de Switch. Copia tu emulador compatible y configura sus requisitos por separado.",
                "", "#EE5266", "#481B2C", "#FFB9C5", MediaKind.GameCard),
                new[] { "Ryujinx.exe", "eden.exe", "citron.exe", "yuzu.exe" }, new[] { ".nsp", ".xci", ".nca", ".nro" },
                "Archivos .nsp, .xci, .nca o .nro. Los NSP/XCI incompletos o inválidos aparecen como avisos, no como juegos. NSZ/NCZ no son compatibles con Eden 0.2.1. Las actualizaciones, DLC y algunos NCA no son juegos arrancables. Configura claves y firmware en el emulador. No hay una web única verificada offline para esta familia."),
            new SystemProfile(new GameSystem("PS5", "PlayStation 5", "Nova5", "Sony", 2020,
                "Nova5, nuestro emulador de PS5. Soporte experimental: mostrar un juego no garantiza que pueda ejecutarse.",
                "https://nova5emu.github.io/nova5/", "#A7B6DC", "#222943", "#EEF3FF", MediaKind.Disc, true),
                new[] { "Nova5.exe", "Nova5Emulator.exe", "kyty_emulator.exe" }, new string[0],
                "Carpetas con eboot.bin y sce_sys\\param.json; portada sce_sys\\icon0.png. Nexo instala la última versión de Nova5 desde su publicación oficial y abre los juegos a través de Nova5, con sus ajustes."),
            new SystemProfile(new GameSystem("GBC", "Game Boy Color", "mGBA", "Nintendo", 1998,
                "Pequeños cartuchos, grandes aventuras. Tu colección de Game Boy y Game Boy Color con píxeles nítidos.",
                "https://mgba.io/", "#A874E8", "#332044", "#DFC1FF", MediaKind.Cartridge),
                new[] { "mGBA.exe" }, new[] { ".gbc", ".gb" },
                "ROMs .gbc y .gb. No requieren BIOS. GBC y GBA comparten la instalación de mGBA en Emuladores\\GBA y mantienen sus juegos separados."),
            new SystemProfile(new GameSystem("GBA", "Game Boy Advance", "mGBA", "Nintendo", 2001,
                "Una portátil llena de color. Recupera tus aventuras de Game Boy Advance en una estantería de cartuchos.",
                "https://mgba.io/", "#7268EE", "#242044", "#C7C2FF", MediaKind.Cartridge),
                new[] { "mGBA.exe" }, new[] { ".gba" }, "ROMs .gba. BIOS opcional; mGBA funciona sin ella. Comparte el emulador con GBC."),
            new SystemProfile(new GameSystem("Xbox", "Xbox", "xemu", "Microsoft", 2001,
                "La primera generación de Xbox, en tu colección. Prepara los archivos de tu consola y consulta la compatibilidad de cada juego.",
                "https://xemu.app/", "#69C62F", "#18341B", "#C4FFA3", MediaKind.DvdCase),
                new[] { "xemu.exe" }, new[] { ".iso" },
                "Discos .iso en formato XISO (las ISO Redump completas requieren conversión). Importa MCPX, BIOS compatible con xemu y, opcionalmente, tu disco .qcow2. La instalación descarga un HDD inicial oficial."),
            new SystemProfile(new GameSystem("Xbox360", "Xbox 360", "Xenia Canary", "Microsoft", 2005,
                "Una nueva generación de juegos en cajas verdes. Xenia Canary es experimental: el resultado depende de cada título.",
                "https://github.com/xenia-canary/xenia-canary", "#9CD342", "#293B20", "#E4FFC0", MediaKind.DvdCase, true),
                new[] { "xenia_canary.exe", "xenia.exe" }, new[] { ".iso", ".xex" },
                "Discos .iso, ejecutables .xex (default.xex en juegos extraídos) y contenedores de juego STFS/GOD sin extensión. Conserva la carpeta .data del GOD. No requiere BIOS. No se muestran partidas, perfiles ni DLC como juegos.")
        };

        internal static readonly IReadOnlyList<GameSystem> Systems = Array.AsReadOnly(Profiles.Select(p => p.Info).ToArray());
        internal static SystemProfile Get(string id)
        {
            var profile = Profiles.FirstOrDefault(p => string.Equals(p.Info.Id, id, StringComparison.Ordinal));
            if (profile == null) throw new NexoException("Sistema desconocido.");
            return profile;
        }
    }

    internal static class RetroCores
    {
        private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".nes"] = new[] { "mesen", "fceumm", "nestopia" },
            [".sfc"] = new[] { "snes9x", "bsnes" }, [".smc"] = new[] { "snes9x", "bsnes" },
            [".gba"] = new[] { "mgba", "vba_next" },
            [".gb"] = new[] { "gambatte", "sameboy", "mgba" }, [".gbc"] = new[] { "gambatte", "sameboy", "mgba" },
            [".md"] = new[] { "genesis_plus_gx", "picodrive" }, [".gen"] = new[] { "genesis_plus_gx", "picodrive" },
            [".smd"] = new[] { "genesis_plus_gx", "picodrive" },
            [".n64"] = new[] { "mupen64plus_next", "parallel_n64" },
            [".z64"] = new[] { "mupen64plus_next", "parallel_n64" }, [".v64"] = new[] { "mupen64plus_next", "parallel_n64" },
            [".pce"] = new[] { "mednafen_pce_fast", "mednafen_pce" },
            [".sms"] = new[] { "genesis_plus_gx", "picodrive" }, [".gg"] = new[] { "genesis_plus_gx", "gearsystem" }
        };
        internal static string[] Extensions => Map.Keys.ToArray();
        internal static string Find(string executable, string game)
        {
            if (string.IsNullOrEmpty(executable) || !Map.TryGetValue(System.IO.Path.GetExtension(game), out var cores)) return null;
            foreach (var core in cores)
            {
                var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(executable), "cores", core + "_libretro.dll");
                if (SafePaths.IsRegularFile(path)) return path;
            }
            return null;
        }
    }
}
