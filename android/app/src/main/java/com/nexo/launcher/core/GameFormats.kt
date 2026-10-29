package com.nexo.launcher.core

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.Locale

/** Pure parsing/selection logic: also exercised on the desktop JVM. */
internal object GameFormats {
    val archives = setOf("zip", "7z", "rar")
    fun extension(name: String) = name.substringAfterLast('.', "").lowercase(Locale.ROOT)
    fun stem(name: String) = if ('.' in name) name.substringBeforeLast('.') else name
    fun title(name: String) = stem(name).replace(Regex("\\s*\\([^)]*\\)|\\s*\\[[^]]*]"), "").trim().ifEmpty { stem(name) }
    fun extensions(id: String): Set<String> = when (id) {
        "Retro" -> setOf("nes", "fds", "unf", "unif", "sfc", "smc", "fig", "swc", "bs", "n64", "z64", "v64", "ndd", "md", "gen", "smd", "bin", "32x", "sms", "gg", "gb", "gbc", "dmg", "cgb", "sgb", "gba", "agb")
        "GBC" -> setOf("gb", "gbc", "dmg")
        "GBA" -> setOf("gba", "mb")
        "PS2" -> setOf("iso", "bin", "img", "mdf", "gz", "cso", "zso", "chd", "cue")
        "PS3" -> setOf("iso")
        "PSP" -> setOf("iso", "cso", "chd", "elf", "pbp")
        "GameCube-Wii" -> setOf("gcm", "tgc", "bin", "iso", "ciso", "gcz", "wbfs", "wia", "rvz", "nfs", "wad", "dol", "elf", "json")
        "Switch" -> setOf("xci", "nsp", "nca", "nro")
        "Xbox" -> setOf("iso", "xiso")
        "Xbox360" -> setOf("iso", "xex", "zar")
        else -> emptySet()
    }
    fun core(id: String, name: String): String? = when {
        id == "GBC" -> "gambatte"
        id == "GBA" -> "mgba"
        id != "Retro" -> null
        else -> when (extension(name)) {
            "nes", "fds", "unf", "unif" -> "fceumm"
            "sfc", "smc", "fig", "swc", "bs" -> "snes9x"
            "n64", "z64", "v64", "ndd" -> "mupen64plus_next_gles3"
            "gb", "gbc", "dmg", "sgb", "cgb" -> "gambatte"
            "gba", "agb" -> "mgba"
            // Genesis Plus GX does NOT emulate 32X. Do not silently send it there.
            "32x" -> "picodrive"
            "md", "gen", "smd", "bin", "sms", "gg" -> "genesis_plus_gx"
            else -> null
        }
    }
    fun externalPath(authority: String?, documentId: String, primary: String = "/storage/emulated/0"): String? {
        if (authority != "com.android.externalstorage.documents") return null
        val parts = documentId.split(':', limit = 2)
        if (parts.size != 2) return null
        val root = when {
            parts[0] == "primary" -> primary.trimEnd('/')
            Regex("[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}").matches(parts[0]) -> "/storage/${parts[0]}"
            else -> return null
        }
        val relative = parts[1]
        if (relative.startsWith('/') || relative.any { it == '\\' || it == ':' || it.code < 32 } ||
            relative.split('/').any { it == ".." || it == "." }) return null
        return if (relative.isEmpty()) root else "$root/$relative"
    }
    data class Disc(val id: String, val platform: String)
    fun discHeader(bytes: ByteArray): Disc? {
        if (bytes.size < 32) return null
        val id = String(bytes, 0, 6, Charsets.US_ASCII)
        if (!Regex("[A-Z0-9]{6}").matches(id)) return null
        val b = ByteBuffer.wrap(bytes).order(ByteOrder.BIG_ENDIAN)
        return when {
            b.getInt(24) == 0x5d1c9ea3 -> Disc(id, "Wii")
            b.getInt(28) == 0xc2339f3d.toInt() -> Disc(id, "GameCube")
            else -> null
        }
    }
    fun discOffset(ext: String, head: ByteArray): Long? {
        if (head.size < 32) return null
        val magic = String(head, 0, 4, Charsets.ISO_8859_1)
        return when (ext) {
            "iso", "gcm" -> 0
            "rvz", "wia" -> if (magic == (if (ext == "rvz") "RVZ\u0001" else "WIA\u0001")) 0x58 else null
            "wbfs" -> if (magic == "WBFS" && head[8].toInt() in 9..20 && head[9].toInt() in 15..31 && head[12] != 0.toByte()) 1L shl head[8].toInt() else null
            "ciso" -> if (magic == "CISO" && head[8] == 1.toByte() && ByteBuffer.wrap(head).order(ByteOrder.LITTLE_ENDIAN).getInt(4) in 32768..67108864) 0x8000 else null
            else -> null
        }
    }
    fun sdkAsset(names: List<String>, sdk: Int, arm82: Boolean): String? {
        val eligible = names.filter { it.endsWith(".apk", true) && !it.contains("nightly", true) }
        if (!arm82 || sdk < 30) return eligible.singleOrNull { it.contains("legacy-armv8.0-sdk26", true) }
        return listOf(35, 33, 30).filter { it <= sdk }.firstNotNullOfOrNull { level ->
            eligible.singleOrNull { it.contains("armv8.2-sdk$level", true) }
        } ?: eligible.singleOrNull { it.contains("legacy-armv8.0-sdk26", true) }
    }
    fun edenVariant(sdk: Int): String? = when { sdk >= 33 -> "standard"; sdk >= 29 -> "legacy"; else -> null }
    fun ps2Serial(bytes: ByteArray): String? {
        val match = Regex("(?im)^\\s*BOOT2\\s*=\\s*cdrom0?:[\\\\/]*([A-Z]{4})[_-]([0-9]{3})[.]([0-9]{2});1\\s*$")
            .find(String(bytes, Charsets.US_ASCII)) ?: return null
        return "${match.groupValues[1]}-${match.groupValues[2]}${match.groupValues[3]}".uppercase(Locale.ROOT)
    }
}
