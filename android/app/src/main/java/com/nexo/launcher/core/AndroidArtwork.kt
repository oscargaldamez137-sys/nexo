package com.nexo.launcher.core

import android.content.Context
import android.graphics.BitmapFactory
import android.net.Uri
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest

internal class AndroidArtwork(private val context: Context, private val library: SafLibrary) {
    private val folder = File(context.cacheDir, "artwork").apply { mkdirs() }
    private val locks = Array(16) { Mutex() }
    private val downloads = Semaphore(3)
    private val prefs = context.getSharedPreferences("nexo", Context.MODE_PRIVATE)
    suspend fun ensure(game: Game): Game {
        val location = library.locations[game.uri.toString()] ?: return game
        val key = MessageDigest.getInstance("SHA-256").digest((game.uri.toString() + ":" + game.sizeBytes + ":" + location.game.modified).toByteArray()).joinToString("") { "%02x".format(it) }
        return locks[(key.hashCode() and Int.MAX_VALUE) % locks.size].withLock {
            val stem = if (location.content != null) game.fileName else GameFormats.stem(game.fileName)
            suspend fun listed(uri: Uri) = try { library.children(uri) } catch (e: CancellationException) { throw e } catch (_: Exception) { emptyList() }
            val siblings = listed(location.parent)
            val own = location.content?.let { listed(it) }.orEmpty()
            val bases = mutableListOf(siblings)
            for (parent in listOf(location.parent, location.systemFolder).distinct()) {
                val entries = if (parent == location.parent) siblings else listed(parent)
                entries.firstOrNull { it.directory && it.name.equals("covers", true) }?.let { bases += listed(it.uri) }
            }
            fun bytes(document: Document): ByteArray? = try { library.readSmall(document.uri, 16 * 1024 * 1024) } catch (_: Exception) { null }
            fun image(list: List<Document>, names: List<String>, hd: Boolean): String? {
                for (name in names) {
                    val d = list.firstOrNull { !it.directory && it.name.equals(name, true) } ?: continue
                    val data = bytes(d) ?: continue
                    if (valid(data, hd)) return d.uri.toString()
                }
                return null
            }
            fun names(suffix: String) = listOf("png", "jpg", "jpeg", "webp").map { "$stem$suffix.$it" }
            var cover = bases.firstNotNullOfOrNull { image(it, names(""), false) }
            var background = bases.firstNotNullOfOrNull { image(it, names(".bg"), true) }
                ?: image(own, listOf("PIC1.PNG", "PIC0.PNG", "pic1.png", "pic0.png"), true)
            val fallbackIcon = image(own, listOf("ICON0.PNG", "icon0.png"), false)
            val generated = bases.firstNotNullOfOrNull { image(it, names(".ai"), true) }
            var serial: String? = null; var disc: GameFormats.Disc? = null
            var embeddedCover: String? = fallbackIcon
            try {
                if (!location.game.directory) SeekDocument(context, game.uri).use { source ->
                    val ext = GameFormats.extension(game.fileName)
                    if (game.systemId == "GameCube-Wii") disc = BinaryMetadata.disc(source, ext)
                    if (game.systemId == "PS2" && ext == "iso") serial = BinaryMetadata.isoFile(source, "SYSTEM.CNF", 65536)?.let(GameFormats::ps2Serial)
                    if (game.systemId == "PSP" && ext == "iso") {
                        if (embeddedCover == null) embeddedCover = embedded(key, "icon", BinaryMetadata.isoFile(source, "PSP_GAME/ICON0.PNG", 16 * 1024 * 1024), false)
                        if (background == null) for (name in listOf("PIC1.PNG", "PIC0.PNG")) {
                            background = embedded(key, "embedded-bg", BinaryMetadata.isoFile(source, "PSP_GAME/$name", 16 * 1024 * 1024), true)
                            if (background != null) break
                        }
                    }
                }
            } catch (e: CancellationException) { throw e } catch (_: Exception) { /* cloud pipes/unsupported format: sidecars still work */ }
            val cached = File(folder, "$key.cover")
            if (cover == null && runCatching { cached.exists() && cached.length() <= 16 * 1024 * 1024 && valid(cached.readBytes(), false) }.getOrDefault(false)) cover = Uri.fromFile(cached).toString()
            val negative = File(folder, "$key.negative")
            val until = runCatching { negative.readText().toLong() }.getOrDefault(0)
            if (cover == null && prefs.getBoolean("downloadArtwork", true) && System.currentTimeMillis() >= until) {
                var permanent = true
                for (url in ArtworkSources.covers(game.systemId, game.fileName, serial, disc)) {
                    try {
                        val data = downloads.withPermit { OfficialHttp.bytes(url, OfficialHttp.artHosts, 16 * 1024 * 1024) }
                        if (!valid(data, false)) { permanent = false; continue }
                        save(cached, data); cover = Uri.fromFile(cached).toString(); negative.delete(); break
                    } catch (e: CancellationException) { throw e } catch (e: HttpFailure) { if (e.status != 404) permanent = false }
                    catch (_: Exception) { permanent = false }
                }
                if (cover == null) runCatching { save(negative, ArtworkPolicy.retryAt(System.currentTimeMillis(), permanent).toString().toByteArray()) }
            }
            // No low-resolution Named_Snaps, no automatic decryption of Switch NCA.
            game.copy(cover = cover ?: embeddedCover, background = background ?: generated, platform = disc?.platform ?: game.platform)
        }
    }
    private fun embedded(key: String, suffix: String, data: ByteArray?, hd: Boolean): String? {
        if (data == null || !valid(data, hd)) return null
        val file = File(folder, "$key.$suffix.png")
        if (!file.exists()) save(file, data)
        return Uri.fromFile(file).toString()
    }
    private fun save(file: File, bytes: ByteArray) {
        val tmp = File.createTempFile("art-", ".tmp", folder)
        try { tmp.outputStream().use { it.write(bytes) }; Files.move(tmp.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE) }
        finally { tmp.delete() }
    }
    private fun valid(bytes: ByteArray, hd: Boolean): Boolean {
        if (bytes.size !in 16..(16 * 1024 * 1024)) return false
        val options = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, options)
        return ArtworkPolicy.accepts(options.outMimeType, options.outWidth, options.outHeight, hd)
    }
}
