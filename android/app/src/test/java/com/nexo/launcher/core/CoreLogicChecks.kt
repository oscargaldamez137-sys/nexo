package com.nexo.launcher.core

import java.io.EOFException
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Deliberately dependency-free: run :app:coreLogicTest, even with an offline Gradle cache. */
object CoreLogicChecks {
    private var passed = 0
    private fun test(name: String, body: () -> Unit) {
        try { body(); passed++; println("OK $name") }
        catch (e: Throwable) { throw AssertionError("FAILED $name", e) }
    }
    private fun eq(expected: Any?, actual: Any?) { check(expected == actual) { "Expected $expected; got $actual" } }
    private fun rejects(body: () -> Unit) { var failed = false; try { body() } catch (_: Exception) { failed = true }; check(failed) }
    @JvmStatic fun main(args: Array<String>) {
        val authority = "com.android.externalstorage.documents"
        test("SAF primary") { eq("/storage/emulated/0/Nexo/Juegos/GBA/Game.gba", GameFormats.externalPath(authority, "primary:Nexo/Juegos/GBA/Game.gba")) }
        test("SAF SD") { eq("/storage/12AB-34CD/roms/A B.gbc", GameFormats.externalPath(authority, "12AB-34CD:roms/A B.gbc")) }
        test("SAF multi-user") { eq("/storage/emulated/10/Games", GameFormats.externalPath(authority, "primary:Games", "/storage/emulated/10")) }
        test("SAF root") { eq("/storage/emulated/0", GameFormats.externalPath(authority, "primary:")) }
        test("SAF cloud denied") { eq(null, GameFormats.externalPath("cloud.documents", "primary:game.iso")) }
        test("SAF traversal denied") { listOf("primary:../x", "primary:a/../x", "primary:./x", "primary:/x", "primary:a\\x", "primary:a:x", "primary:a\u0000b").forEach { eq(null, GameFormats.externalPath(authority, it)) } }
        test("SAF unknown volume denied") { eq(null, GameFormats.externalPath(authority, "downloads:12")) }
        val assets = listOf("ARMSX2-2.8.4-a11-armv8.2-sdk30.apk", "ARMSX2-2.8.4-a13-armv8.2-sdk33.apk", "ARMSX2-2.8.4-a15-armv8.2-sdk35.apk", "ARMSX2-2.8.4-legacy-armv8.0-sdk26.apk")
        test("ARMSX2 SDK35") { eq(assets[2], GameFormats.sdkAsset(assets, 35, true)) }
        test("ARMSX2 SDK34") { eq(assets[1], GameFormats.sdkAsset(assets, 34, true)) }
        test("ARMSX2 SDK30") { eq(assets[0], GameFormats.sdkAsset(assets, 30, true)) }
        test("ARMSX2 legacy OS") { eq(assets[3], GameFormats.sdkAsset(assets, 29, true)) }
        test("ARMSX2 unknown CPU") { eq(assets[3], GameFormats.sdkAsset(assets, 35, false)) }
        test("ARMSX2 no compatible asset") { eq(null, GameFormats.sdkAsset(assets.take(3), 26, false)) }
        test("Eden variants") { eq(null, GameFormats.edenVariant(28)); eq("legacy", GameFormats.edenVariant(29)); eq("legacy", GameFormats.edenVariant(32)); eq("standard", GameFormats.edenVariant(33)) }
        test("extensions isolated") { check("gba" in GameFormats.extensions("GBA")); check("iso" !in GameFormats.extensions("GBA")); check(GameFormats.extensions("PS4").isEmpty()); check(GameFormats.extensions("PS5").isEmpty()) }
        test("archives pending, not games") { for (id in listOf("Retro", "GBC", "GBA", "PS2", "PS3", "PSP", "GameCube-Wii", "Switch", "Xbox", "Xbox360")) check(GameFormats.extensions(id).intersect(GameFormats.archives).isEmpty()) }
        test("Retro core mapping") { eq("fceumm", GameFormats.core("Retro", "a.NES")); eq("mgba", GameFormats.core("GBA", "a.gba")); eq("gambatte", GameFormats.core("GBC", "a.gb")); eq("picodrive", GameFormats.core("Retro", "a.32x")); eq(null, GameFormats.core("Retro", "a.iso")) }
        test("clean title") { eq("Game", GameFormats.title("Game (USA) [!].iso")) }
        test("GameCube header") { eq(GameFormats.Disc("GM8E01", "GameCube"), GameFormats.discHeader(disc(false))) }
        test("Wii header") { eq(GameFormats.Disc("RMGE01", "Wii"), GameFormats.discHeader(disc(true))) }
        test("bad disc header") { eq(null, GameFormats.discHeader(ByteArray(32))); eq(null, GameFormats.discHeader(ByteArray(10))) }
        test("RVZ header position") { val b = ByteArray(0x78); "RVZ\u0001".toByteArray().copyInto(b); disc(false).copyInto(b, 0x58); eq("GameCube", BinaryMetadata.disc(source(b), "rvz")?.platform) }
        test("WBFS header position") { val b = ByteArray(544); "WBFS".toByteArray().copyInto(b); b[8] = 9; b[9] = 21; b[12] = 1; disc(true).copyInto(b, 512); eq("Wii", BinaryMetadata.disc(source(b), "wbfs")?.platform) }
        test("WBFS absurd shift") { val b = ByteArray(32); "WBFS".toByteArray().copyInto(b); b[8] = 63; b[9] = 21; b[12] = 1; eq(null, GameFormats.discOffset("wbfs", b)) }
        test("CISO header") { val b = ByteArray(32); "CISO".toByteArray().copyInto(b); le(b).putInt(4, 0x200000); b[8] = 1; eq(0x8000L, GameFormats.discOffset("ciso", b)); b[8] = 0; eq(null, GameFormats.discOffset("ciso", b)) }
        test("PS2 serial") { eq("SLUS-20946", GameFormats.ps2Serial("BOOT2 = cdrom0:\\SLUS_209.46;1\r\nVER = 1.00".toByteArray())) }
        test("PS2 invalid serial") { eq(null, GameFormats.ps2Serial("BOOT = something.exe".toByteArray())) }
        test("ISO SYSTEM.CNF without extraction") { eq("SLUS-20946", BinaryMetadata.isoFile(source(iso()), "SYSTEM.CNF", 4096)?.let(GameFormats::ps2Serial)) }
        test("ISO nested PSP image") { eq("small image", BinaryMetadata.isoFile(source(iso()), "PSP_GAME/ICON0.PNG", 4096)?.toString(Charsets.UTF_8)) }
        test("ISO absent file") { eq(null, BinaryMetadata.isoFile(source(iso()), "MISSING", 4096)) }
        test("ISO excessive file rejected") { eq(null, BinaryMetadata.isoFile(source(iso()), "SYSTEM.CNF", 2)) }
        test("ISO oversized directory rejected") { val b = iso(); le(b).putInt(0x8000 + 166, Int.MAX_VALUE); eq(null, BinaryMetadata.isoFile(source(b), "SYSTEM.CNF", 4096)) }
        test("ISO short source") { rejects { BinaryMetadata.isoFile(source(ByteArray(1024)), "SYSTEM.CNF", 4096) } }
        test("SFO title") { eq("Test Game", BinaryMetadata.sfo(sfo(), "TITLE")) }
        test("SFO malformed offset") { val b = sfo(); le(b).putInt(12, Int.MAX_VALUE); eq(null, BinaryMetadata.sfo(b, "TITLE")) }
        test("Switch has no online cover source") { eq(emptyList<String>(), ArtworkSources.covers("Switch", "Game [0100123400000000].nsp")); for (url in listOf("https://tinfoil.media/ti/0100123400000000/0/0/", "https://tinfoil.io/Title/0100123400000000")) rejects { OfficialHttp.validate(url, OfficialHttp.artHosts) } }
        test("GameTDB HQ first") { val urls = ArtworkSources.covers("GameCube-Wii", "game.iso", null, GameFormats.Disc("GM8E01", "GameCube")); check(urls.first().contains("/coverHQ/US/GM8E01.png")); check(urls.any { it.contains("/coverM/") }); check(urls.any { it.contains("/cover/") }) }
        test("No Intro names preserved") { val url = ArtworkSources.covers("GBA", "Game (USA).gba", null, null).first(); check(url.contains("Nintendo_-_Game_Boy_Advance")); check(url.contains("Game%20(USA)") || url.contains("Game%20%28USA%29")); check(!url.contains("Named_Snaps")) }
        test("PS2 serial cover") { check(ArtworkSources.covers("PS2", "game.iso", "SLUS-20946", null).first().endsWith("SLUS-20946.jpg")) }
        test("HTTPS allowlist") { eq("api.github.com", OfficialHttp.validate("https://api.github.com/repos/test", OfficialHttp.apkHosts).host) }
        test("HTTPS rejects other host and downgrade") { for (url in listOf("http://api.github.com/a", "https://evil.test/a", "https://api.github.com.evil.test/a", "https://name@api.github.com/a", "https://api.github.com:444/a")) rejects { OfficialHttp.validate(url, OfficialHttp.apkHosts) } }
        test("HD minimum and full HD accepted") { check(ArtworkPolicy.accepts("image/png", 1280, 720, true)); check(ArtworkPolicy.accepts("image/jpeg", 1920, 1080, true)) }
        test("PSP PIC1 and low-resolution snaps not backgrounds") { check(!ArtworkPolicy.accepts("image/png", 480, 272, true)); check(!ArtworkPolicy.accepts("image/png", 512, 384, true)); check(!ArtworkPolicy.accepts("image/png", 1279, 1080, true)); check(!ArtworkPolicy.accepts("image/png", 1920, 719, true)) }
        test("small covers are valid") { check(ArtworkPolicy.accepts("image/png", 160, 184, false)) }
        test("non-images and excessive dimensions denied") { check(!ArtworkPolicy.accepts("text/html", 1920, 1080, true)); check(!ArtworkPolicy.accepts("image/png", 16384, 16384, false)); check(!ArtworkPolicy.accepts(null, -1, -1, false)) }
        test("negative cache expires") { eq(604801000L, ArtworkPolicy.retryAt(1000L, true)); eq(1801000L, ArtworkPolicy.retryAt(1000L, false)) }
        println("$passed/$passed pruebas OK; sin red ni Android.")
    }
    private fun le(b: ByteArray) = ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN)
    private fun disc(wii: Boolean) = ByteArray(32).also { b ->
        (if (wii) "RMGE01" else "GM8E01").toByteArray().copyInto(b)
        ByteBuffer.wrap(b).putInt(if (wii) 24 else 28, if (wii) 0x5d1c9ea3 else 0xc2339f3d.toInt())
    }
    private fun source(b: ByteArray) = object : BinarySource {
        override fun read(offset: Long, count: Int): ByteArray {
            if (offset < 0 || count < 0 || offset > b.size || count > b.size - offset) throw EOFException()
            return b.copyOfRange(offset.toInt(), offset.toInt() + count)
        }
    }
    private fun record(name: String, extent: Int, size: Int, directory: Boolean = false): ByteArray {
        val n = name.toByteArray(); val b = ByteArray((33 + n.size + 1) and -2)
        b[0] = b.size.toByte(); le(b).putInt(2, extent).putInt(10, size); b[25] = if (directory) 2 else 0; b[32] = n.size.toByte(); n.copyInto(b, 33); return b
    }
    private fun iso(): ByteArray {
        val b = ByteArray(25 * 2048); b[0x8000] = 1; "CD001".toByteArray().copyInto(b, 0x8001)
        le(b).putShort(0x8000 + 128, 2048).putInt(0x8000 + 158, 20).putInt(0x8000 + 166, 2048)
        val cnf = "BOOT2 = cdrom0:\\SLUS_209.46;1\n".toByteArray()
        val first = record("SYSTEM.CNF;1", 21, cnf.size); first.copyInto(b, 20 * 2048)
        record("PSP_GAME", 22, 2048, true).copyInto(b, 20 * 2048 + first.size)
        cnf.copyInto(b, 21 * 2048)
        record("ICON0.PNG;1", 23, 11).copyInto(b, 22 * 2048); "small image".toByteArray().copyInto(b, 23 * 2048)
        return b
    }
    private fun sfo(): ByteArray {
        val b = ByteArray(64); byteArrayOf(0, 0x50, 0x53, 0x46).copyInto(b)
        le(b).putInt(8, 36).putInt(12, 44).putInt(16, 1).putShort(22, 0x204).putInt(24, 10)
        "TITLE\u0000".toByteArray().copyInto(b, 36); "Test Game\u0000".toByteArray().copyInto(b, 44); return b
    }
}
