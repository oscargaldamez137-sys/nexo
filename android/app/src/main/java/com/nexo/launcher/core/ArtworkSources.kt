package com.nexo.launcher.core

import java.net.URLEncoder

internal object ArtworkSources {
    fun covers(system: String, name: String, serial: String? = null, disc: GameFormats.Disc? = null): List<String> {
        val result = mutableListOf<String>()
        if (system == "PS2" && serial != null && Regex("[A-Z]{4}-[0-9]{5}").matches(serial))
            result += "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default/$serial.jpg"
        if (system == "GameCube-Wii" && disc != null) {
            val region = when (disc.id[3]) { 'E' -> "US"; 'J' -> "JA"; 'S' -> "ES"; 'F' -> "FR"; 'D' -> "DE"; 'I' -> "IT"; 'K' -> "KO"; 'W' -> "ZH"; else -> "EN" }
            for (variant in listOf("coverHQ", "coverM", "cover")) for (r in listOf(region, "EN", "US").distinct())
                result += "https://art.gametdb.com/wii/$variant/$r/${disc.id}.png"
        }
        val repo = when (system) {
            "GBC" -> if (GameFormats.extension(name) == "gb") "Nintendo_-_Game_Boy" else "Nintendo_-_Game_Boy_Color"
            "GBA" -> "Nintendo_-_Game_Boy_Advance"
            "PS2" -> "Sony_-_PlayStation_2"
            "PS3" -> "Sony_-_PlayStation_3"
            "PSP" -> "Sony_-_PlayStation_Portable"
            "Xbox" -> "Microsoft_-_Xbox"
            "Xbox360" -> "Microsoft_-_Xbox_360"
            "GameCube-Wii" -> if (disc?.platform == "Wii") "Nintendo_-_Wii" else if (disc?.platform == "GameCube") "Nintendo_-_GameCube" else null
            "Retro" -> when (GameFormats.core(system, name)) {
                "fceumm" -> "Nintendo_-_Nintendo_Entertainment_System"
                "snes9x" -> "Nintendo_-_Super_Nintendo_Entertainment_System"
                "mupen64plus_next_gles3" -> "Nintendo_-_Nintendo_64"
                "gambatte" -> if (GameFormats.extension(name) == "gb") "Nintendo_-_Game_Boy" else "Nintendo_-_Game_Boy_Color"
                "mgba" -> "Nintendo_-_Game_Boy_Advance"
                "picodrive" -> "Sega_-_32X"
                "genesis_plus_gx" -> when (GameFormats.extension(name)) { "sms" -> "Sega_-_Master_System_-_Mark_III"; "gg" -> "Sega_-_Game_Gear"; else -> "Sega_-_Mega_Drive_-_Genesis" }
                else -> null
            }
            else -> null
        }
        if (repo != null) {
            val safe = GameFormats.stem(name).replace(Regex("[&*/:`<>?\\\\|\"]"), "_")
            val encoded = URLEncoder.encode(safe, "UTF-8").replace("+", "%20")
            result += "https://raw.githubusercontent.com/libretro-thumbnails/$repo/master/Named_Boxarts/$encoded.png"
        }
        return result
    }
}
