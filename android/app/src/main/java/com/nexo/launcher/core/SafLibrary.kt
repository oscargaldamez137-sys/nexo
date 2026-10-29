package com.nexo.launcher.core

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.CancellationSignal
import android.provider.DocumentsContract as Docs
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap

internal data class Document(val uri: Uri, val id: String, val name: String, val directory: Boolean, val size: Long, val modified: Long)
internal data class GameLocation(val game: Document, val parent: Uri, val systemFolder: Uri, val systemId: String, val content: Uri? = null)

internal class SafLibrary(private val context: Context) {
    private val resolver = context.contentResolver
    private val prefs = context.getSharedPreferences("nexo", Context.MODE_PRIVATE)
    val locations = ConcurrentHashMap<String, GameLocation>()
    val archiveLocations = ConcurrentHashMap<String, GameLocation>()
    val root: Uri? get() = prefs.getString("library", null)?.let(Uri::parse)
    private var initialized: String? = null

    @Synchronized fun setRoot(tree: Uri) {
        require(tree.scheme == "content" && Docs.isTreeUri(tree)) { "Elige una carpeta mediante el selector de Android." }
        try { resolver.takePersistableUriPermission(tree, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION) }
        catch (e: SecurityException) { throw IllegalStateException("Android no concedió acceso permanente de lectura y escritura. Vuelve a elegir una carpeta local para Nexo.", e) }
        val previous = root
        check(prefs.edit().putString("library", tree.toString()).commit()) { "No se pudo guardar la biblioteca." }
        // Cambio de carpeta: se suelta el permiso de la anterior (no se toca ni se borra nada en ella).
        if (previous != null && previous != tree) runCatching {
            resolver.releasePersistableUriPermission(previous, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
        }
        // Persist first, initialize on Dispatchers.IO during scan, never 12 provider queries on the UI thread.
        locations.clear(); archiveLocations.clear(); initialized = null
    }
    fun document(tree: Uri) = Docs.buildDocumentUriUsingTree(tree, Docs.getTreeDocumentId(tree))
    suspend fun children(folder: Uri): List<Document> {
        val coroutine = currentCoroutineContext()
        coroutine.ensureActive()
        val signal = CancellationSignal()
        return suspendCancellableCoroutine { continuation ->
          continuation.invokeOnCancellation { signal.cancel() }
          try {
            val query = Docs.buildChildDocumentsUriUsingTree(folder, Docs.getDocumentId(folder))
            val columns = arrayOf(Docs.Document.COLUMN_DOCUMENT_ID, Docs.Document.COLUMN_DISPLAY_NAME, Docs.Document.COLUMN_MIME_TYPE,
                Docs.Document.COLUMN_SIZE, Docs.Document.COLUMN_LAST_MODIFIED)
            val documents = resolver.query(query, columns, null, null, null, signal)?.use { cursor ->
                val result = ArrayList<Document>()
                while (cursor.moveToNext()) {
                    coroutine.ensureActive()
                    if (result.size >= 20000) throw IOException("La carpeta supera el límite de 20 000 entradas; divídela en subcarpetas.")
                    val id = cursor.getString(0) ?: continue
                    val name = cursor.getString(1) ?: continue
                    if (name == "." || name == ".." || '/' in name || '\\' in name) continue
                    result += Document(Docs.buildDocumentUriUsingTree(folder, id), id, name,
                        cursor.getString(2) == Docs.Document.MIME_TYPE_DIR, if (cursor.isNull(3)) -1 else cursor.getLong(3),
                        if (cursor.isNull(4)) 0 else cursor.getLong(4))
                }
                result
            } ?: throw IOException("No se pudo consultar la carpeta. Vuelve a elegir tu biblioteca.")
            continuation.resume(documents)
          } catch (e: Exception) { continuation.resumeWithException(e) }
        }
    }
    private suspend fun directory(parent: Uri, name: String): Uri {
        val found = children(parent).firstOrNull { it.name.equals(name, true) }
        if (found != null) {
            check(found.directory) { "$name existe, pero no es una carpeta." }; return found.uri
        }
        return Docs.createDocument(resolver, parent, Docs.Document.MIME_TYPE_DIR, name)
            ?: throw IOException("No se pudo crear la carpeta $name.")
    }
    private val initLock = kotlinx.coroutines.sync.Mutex()
    suspend fun systemFolder(id: String): Uri {
        require(Catalog.systems.any { it.id == id }) { "Consola no válida." }
        val chosen = root ?: throw IOException("Primero elige la carpeta de tu biblioteca.")
        initLock.lock()
        try {
            val games = gamesFolder(chosen)
            if (initialized != chosen.toString()) {
                val bios = directory(document(chosen), "BIOS")
                val existing = children(games).map { it.name to it.directory }
                for (system in Catalog.systems) {
                    currentCoroutineContext().ensureActive()
                    val biosFolder = directory(bios, system.id)
                    if (children(biosFolder).none { it.name.equals("LEEME.txt", true) }) {
                        val readme = Docs.createDocument(resolver, biosFolder, "text/plain", "LEEME.txt") ?: throw IOException("No se pudo crear el LEEME de BIOS.")
                        resolver.openOutputStream(readme, "wt")?.use { it.write((system.name + "\n\n" + SystemFilesLogic.readme(system.id) + "\n").toByteArray()) }
                            ?: throw IOException("No se pudo escribir el LEEME de BIOS.")
                    }
                    val folder = directory(games, LibraryLayout.systemFolder(system.id, existing) ?: system.id)
                    if (children(folder).none { it.name.equals("LEEME.txt", true) }) {
                        val readme = Docs.createDocument(resolver, folder, "text/plain", "LEEME.txt")
                            ?: throw IOException("No se pudo crear LEEME.txt.")
                        val help = "${system.name}\n\n${system.gamesHint}\nFormatos: ${GameFormats.extensions(system.id).joinToString { ".$it" }}.\n" +
                            "ZIP, 7z y RAR: Nexo puede descomprimirlos aquí. El original solo se borra si tú lo confirmas.\nPortada: <nombre>.png/jpg; fondo HD: <nombre>.bg.png/jpg; generado: <nombre>.ai.png.\n"
                        resolver.openOutputStream(readme, "wt")?.use { it.write(help.toByteArray()) }
                            ?: throw IOException("No se pudo escribir LEEME.txt.")
                    }
                }
                if (root == chosen) initialized = chosen.toString()
            }
            return directory(games, LibraryLayout.systemFolder(id, children(games).map { it.name to it.directory }) ?: id)
        } finally { initLock.unlock() }
    }
    /** «Juegos» dentro de la biblioteca o, si el usuario eligió su propia carpeta de juegos, esa misma (LibraryLayout). */
    private suspend fun gamesFolder(chosen: Uri): Uri {
        val rootDoc = document(chosen)
        val entries = children(rootDoc).map { it.name to it.directory }
        val name = runCatching { info(rootDoc).name }.getOrNull()
        return if (LibraryLayout.useRootAsGames(name, entries, Catalog.systems.map { it.id })) rootDoc else directory(rootDoc, LibraryLayout.GAMES)
    }
    suspend fun biosFolder(id: String): Uri {
        val chosen = root ?: throw IOException("Primero elige la biblioteca.")
        systemFolder(id)
        check(chosen == root) { "La biblioteca cambió." }
        return directory(directory(document(chosen), "BIOS"), id)
    }
    suspend fun scan(id: String): ScanResult {
        val selectedRoot = root ?: return ScanResult(emptyList(), emptyList())
        val base = systemFolder(id)
        val games = mutableListOf<Game>(); val archives = mutableListOf<PendingArchive>()
        val queue = ArrayDeque<Pair<Uri, Int>>(); queue.add(base to 0)
        val visited = HashSet<String>(); var total = 0
        while (queue.isNotEmpty()) {
            currentCoroutineContext().ensureActive()
            check(root == selectedRoot) { "La biblioteca cambió durante el escaneo." }
            val (folder, depth) = queue.removeFirst()
            if (!visited.add(Docs.getDocumentId(folder))) continue
            if (visited.size > 2048) throw IOException("Demasiadas carpetas; divide la biblioteca.")
            val entries = children(folder)
            total += entries.size
            if (total > 50000) throw IOException("El escaneo supera las 50 000 entradas; divide la biblioteca.")
            // Disc layout and installed PS3 game layout. Do not descend through every game asset.
            if (id == "PS3") {
                val ps3 = entries.firstOrNull { it.directory && it.name.equals("PS3_GAME", true) }
                val content = ps3?.uri ?: folder
                val contents = if (ps3 != null) children(content) else entries
                val usr = contents.firstOrNull { it.directory && it.name.equals("USRDIR", true) }
                if (usr != null && children(usr.uri).any { !it.directory && it.name.equals("EBOOT.BIN", true) }) {
                    val doc = info(folder)
                    val sfo = contents.firstOrNull { it.name.equals("PARAM.SFO", true) }
                    val title = sfo?.let { runCatching { BinaryMetadata.sfo(readSmall(it.uri, 256 * 1024), "TITLE") }.getOrNull() }
                    val game = Game(id, title ?: GameFormats.title(doc.name), folder, doc.name, 0)
                    locations[folder.toString()] = GameLocation(doc, parentOf(folder) ?: base, base, id, content)
                    games += game; continue
                }
            }
            val cueNames = entries.filter { GameFormats.extension(it.name) == "cue" }.map { GameFormats.stem(it.name).lowercase() }.toSet()
            for (entry in entries) {
                if (entry.directory) {
                    if (depth < 8 && !entry.name.startsWith('.') && !entry.name.equals("covers", true)) queue.add(entry.uri to depth + 1)
                    continue
                }
                val ext = GameFormats.extension(entry.name)
                val kind = ShelfRules.kind(id, entry.name, cueNames)
                if (kind == ShelfRules.Kind.Unsupported) {
                    // Nunca se esconde un juego en silencio: aparece como pendiente con la explicación.
                    archives += PendingArchive(id, GameFormats.title(entry.name), entry.uri, entry.size, false, SwitchNotice.unsupported(id, entry.name)!!)
                    archiveLocations[entry.uri.toString()] = GameLocation(entry, folder, base, id)
                } else if (kind == ShelfRules.Kind.Archive) {
                    val job = currentCoroutineContext()
                    val health = try {
                        android.os.ParcelFileDescriptor.AutoCloseInputStream(resolver.openFileDescriptor(entry.uri, "r")
                            ?: throw IOException("No se pudo leer el comprimido.")).use { stream ->
                            ArchiveValidation.inspect(stream.channel, ext) { job.ensureActive() }
                        }
                    } catch (e: IOException) { ArchiveHealth(false, "No se pudo comprobar la descarga: ${e.message}") }
                    archives += PendingArchive(id, GameFormats.title(entry.name), entry.uri, entry.size, health.ready, health.message)
                    archiveLocations[entry.uri.toString()] = GameLocation(entry, folder, base, id)
                } else if (kind == ShelfRules.Kind.Game) {
                    val platform = if (id == "GameCube-Wii") runCatching { SeekDocument(context, entry.uri).use { BinaryMetadata.disc(it, ext)?.platform } }.getOrNull() else null
                    games += Game(id, GameFormats.title(entry.name), entry.uri, entry.name, entry.size, platform = platform)
                    locations[entry.uri.toString()] = GameLocation(entry, folder, base, id)
                }
            }
        }
        return ScanResult(games.sortedBy { it.title.lowercase() }, archives.sortedBy { it.title.lowercase() })
    }
    fun info(uri: Uri): Document = resolver.query(uri, arrayOf(Docs.Document.COLUMN_DISPLAY_NAME, Docs.Document.COLUMN_MIME_TYPE, Docs.Document.COLUMN_SIZE, Docs.Document.COLUMN_LAST_MODIFIED), null, null, null)?.use {
        if (!it.moveToFirst()) throw IOException("Documento no disponible.")
        Document(uri, Docs.getDocumentId(uri), it.getString(0), it.getString(1) == Docs.Document.MIME_TYPE_DIR, if (it.isNull(2)) -1 else it.getLong(2), if (it.isNull(3)) 0 else it.getLong(3))
    } ?: throw IOException("Documento no disponible.")
    private fun parentOf(uri: Uri): Uri? {
        val id = Docs.getDocumentId(uri); if ('/' !in id) return null
        // Only used for external-storage IDs; other providers can use opaque IDs.
        return if (uri.authority == "com.android.externalstorage.documents") Docs.buildDocumentUriUsingTree(uri, id.substringBeforeLast('/')) else null
    }
    fun realPath(uri: Uri): String? = GameFormats.externalPath(uri.authority, Docs.getDocumentId(uri), android.os.Environment.getExternalStorageDirectory().absolutePath)
    fun readSmall(uri: Uri, limit: Int): ByteArray = resolver.openInputStream(uri)?.use { stream ->
        val out = java.io.ByteArrayOutputStream(); val buffer = ByteArray(8192)
        while (true) { val n = stream.read(buffer); if (n < 0) break; if (out.size() + n > limit) throw IOException("Archivo demasiado grande."); out.write(buffer, 0, n) }
        out.toByteArray()
    } ?: throw IOException("No se pudo leer el documento.")
}
