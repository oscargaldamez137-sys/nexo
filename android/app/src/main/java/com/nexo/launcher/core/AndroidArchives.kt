package com.nexo.launcher.core

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.os.StatFs
import android.os.storage.StorageManager
import android.provider.DocumentsContract as Docs
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import java.io.IOException
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicReference

internal class AndroidArchives(private val context: Context, private val library: SafLibrary) {
    private val resolver = context.contentResolver
    private val lock = Mutex()
    private data class Receipt(val result: ArchiveExtractionResult, val root: Uri, val size: Long, val modified: Long, val digest: String)
    private val receipts = ConcurrentHashMap<String, Receipt>()

    suspend fun extract(archive: PendingArchive, onProgress: (ArchiveProgress) -> Unit): ArchiveExtractionResult = coroutineScope {
        check(lock.tryLock()) { "Ya se está descomprimiendo otro archivo. Espera o cancélalo." }
        val progress = AtomicReference(ArchiveProgress(0, null))
        // Child inherits caller dispatcher: UI callbacks never execute on the decoder's IO thread.
        val reporter = launch {
            var previous: ArchiveProgress? = null
            while (isActive) {
                val value = progress.get()
                if (value != previous) { onProgress(value); previous = value }
                delay(150)
            }
        }
        try {
            val result = withContext(Dispatchers.IO) {
                val root = library.root ?: throw IOException("Elige primero la biblioteca.")
                val location = library.archiveLocations[archive.uri.toString()]
                    ?: throw IOException("Vuelve a escanear la biblioteca antes de descomprimir.")
                checkLocation(archive, root, location)
                val before = library.info(archive.uri)
                val ext = GameFormats.extension(before.name)
                if (ext !in GameFormats.archives) throw IOException("Este archivo no es un ZIP, 7z o RAR.")
                val job = currentCoroutineContext()
                val check = { job.ensureActive(); if (library.root != root) throw IOException("La biblioteca cambió durante la extracción.") }
                open(archive.uri).use { stream ->
                    val fingerprint = fingerprint(stream.channel, check)
                    val reader = ArchiveReaders.open(stream.channel, ext, check)
                    reader.use {
                        val destination = SafDestination(location.parent, ArchivePaths.folder(before.name), check)
                        val (directory, bytes) = ArchiveEngine.extract(reader, destination, check) { done, total ->
                            progress.set(ArchiveProgress(done, total))
                        }
                        val result = ArchiveExtractionResult(archive, directory, bytes, UUID.randomUUID().toString())
                        receipts[result.receipt] = Receipt(result, root, before.size, before.modified, fingerprint)
                        result
                    }
                }
            }
            reporter.cancelAndJoin()
            onProgress(progress.get())
            result
        } catch (e: CancellationException) { throw e }
        catch (e: Exception) {
            val cleanup = if (e.suppressed.isNotEmpty()) " No se pudo retirar una carpeta temporal .nexo-extract-; puedes borrarla con tu gestor de archivos." else ""
            throw IOException("No se pudo descomprimir. El original se conserva. ${friendly(e)}$cleanup", e)
        } finally {
            reporter.cancel()
            lock.unlock()
        }
    }

    suspend fun delete(result: ArchiveExtractionResult) = withContext(Dispatchers.IO) {
        check(lock.tryLock()) { "Espera a que termine la extracción antes de borrar el original." }
        try {
            val receipt = receipts[result.receipt]
            check(receipt != null && receipt.result === result) { "No hay una extracción verificada para este archivo en esta sesión." }
            check(library.root == receipt.root) { "La biblioteca cambió. El comprimido se conserva." }
            val archive = result.archive
            val current = library.info(archive.uri)
            check(current.size == receipt.size && current.modified == receipt.modified) { "El comprimido cambió desde la extracción. No se ha borrado." }
            check(library.info(result.directory).directory) { "La carpeta extraída ya no existe. El original se conserva." }
            open(archive.uri).use {
                val job = currentCoroutineContext()
                check(fingerprint(it.channel) { job.ensureActive() } == receipt.digest) { "El comprimido cambió desde la extracción. No se ha borrado." }
            }
            currentCoroutineContext().ensureActive()
            if (!Docs.deleteDocument(resolver, archive.uri)) throw IOException("Android no pudo borrar el comprimido.")
            receipts.remove(result.receipt)
            library.archiveLocations.remove(archive.uri.toString())
        } finally { lock.unlock() }
    }

    private fun checkLocation(archive: PendingArchive, root: Uri, location: GameLocation) {
        check(location.systemId == archive.systemId && location.game.uri == archive.uri &&
            root.authority == archive.uri.authority && Docs.getTreeDocumentId(root) == Docs.getTreeDocumentId(archive.uri)) {
            "El comprimido ya no pertenece a la biblioteca elegida."
        }
    }
    private fun open(uri: Uri) = ParcelFileDescriptor.AutoCloseInputStream(resolver.openFileDescriptor(uri, "r")
        ?: throw IOException("No se puede abrir el comprimido."))

    private fun fingerprint(channel: SeekableByteChannel, check: () -> Unit): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val size = channel.size(); digest.update(ByteBuffer.allocate(8).putLong(size).array())
        for (offset in listOf(0L, (size - 65536).coerceAtLeast(0)).distinct()) {
            val b = ByteBuffer.allocate((size - offset).coerceAtMost(65536).toInt())
            channel.position(offset)
            while (b.hasRemaining()) { check(); if (channel.read(b) <= 0) throw IOException("Descarga incompleta.") }
            digest.update(b.array())
        }
        return digest.digest().joinToString("") { "%02x".format(it.toInt() and 255) }
    }
    private fun friendly(e: Exception): String = when {
        e is SecurityException -> "Android denegó el acceso. Vuelve a elegir la biblioteca."
        e.javaClass.simpleName.contains("MemoryLimit") -> "El archivo necesita demasiada memoria para este móvil. Descomprímelo en un PC o con tu gestor de archivos."
        e.javaClass.simpleName.contains("Password") || e.message?.contains("password", true) == true -> "El archivo tiene contraseña; usa tu gestor de archivos."
        e.javaClass.simpleName.contains("Crc", true) || e.message?.contains("CRC", true) == true -> "El archivo está dañado (CRC). Vuelve a descargarlo."
        else -> e.message ?: "Comprueba que la descarga esté completa y quede espacio libre."
    }

    private inner class SafDestination(private val parent: Uri, private val stem: String, private val check: () -> Unit) : ArchiveDestination<Uri> {
        private var staging: Uri? = null
        private val directories = HashMap<String, Uri>()
        private var writtenSinceSpaceCheck = 0L
        override fun availableBytes(): Long {
            // StatFs queries the selected volume, not the application's internal cache volume.
            val path = library.realPath(parent) ?: return -1
            val id = Docs.getDocumentId(parent).substringBefore(':')
            val storage = context.getSystemService(StorageManager::class.java)
            val volume = storage.storageVolumes.firstOrNull { if (id == "primary") it.isPrimary else it.uuid.equals(id, true) } ?: return -1
            if (volume.state != android.os.Environment.MEDIA_MOUNTED) throw IOException("La tarjeta o el almacenamiento no está disponible para escritura.")
            val root = if (id == "primary") android.os.Environment.getExternalStorageDirectory().absolutePath else "/storage/$id"
            return try { StatFs(path).availableBytes } catch (_: IllegalArgumentException) { StatFs(root).availableBytes }
        }
        override fun begin() {
            check()
            val name = ".nexo-extract-${UUID.randomUUID()}"
            val uri = Docs.createDocument(resolver, parent, Docs.Document.MIME_TYPE_DIR, name)
                ?: throw IOException("No se pudo crear la carpeta temporal.")
            staging = uri
            verifyChild(parent, uri, name)
            val flags = resolver.query(uri, arrayOf(Docs.Document.COLUMN_FLAGS), null, null, null)?.use { if (it.moveToFirst()) it.getInt(0) else 0 } ?: 0
            if (flags and Docs.Document.FLAG_SUPPORTS_RENAME == 0) throw IOException("Este proveedor no permite renombrar carpetas. Elige una biblioteca local o de tarjeta SD.")
            directories[""] = uri
        }
        override fun directory(parts: List<String>) { ensureDirectory(parts) }
        private fun ensureDirectory(parts: List<String>): Uri {
            var current = staging ?: throw IOException("No hay carpeta temporal.")
            for (i in parts.indices) {
                check()
                val key = parts.take(i + 1).joinToString("/")
                val previous = current
                current = directories.getOrPut(key) {
                    val created = Docs.createDocument(resolver, previous, Docs.Document.MIME_TYPE_DIR, parts[i])
                        ?: throw IOException("No se pudo crear una carpeta del juego.")
                    verifyChild(previous, created, parts[i]); created
                }
            }
            return current
        }
        override fun file(parts: List<String>): OutputStream {
            val folder = ensureDirectory(parts.dropLast(1))
            val uri = Docs.createDocument(resolver, folder, "application/octet-stream", parts.last())
                ?: throw IOException("No se pudo crear ${parts.last()}.")
            verifyChild(folder, uri, parts.last())
            val output = ParcelFileDescriptor.AutoCloseOutputStream(resolver.openFileDescriptor(uri, "w")
                ?: throw IOException("No se puede escribir ${parts.last()}."))
            return object : OutputStream() {
                override fun write(b: Int) { output.write(b); writtenSinceSpaceCheck++ }
                override fun write(b: ByteArray, off: Int, len: Int) {
                    if (writtenSinceSpaceCheck >= 32L * 1024 * 1024) {
                        if (availableBytes() < ArchiveValidation.RESERVE) throw IOException("Se está agotando el espacio libre; se ha detenido la extracción.")
                        writtenSinceSpaceCheck = 0
                    }
                    output.write(b, off, len); writtenSinceSpaceCheck += len
                }
                override fun close() = output.close()
            }
        }
        private fun verifyChild(parent: Uri, child: Uri, expectedName: String) {
            if (child.authority != parent.authority || Docs.getTreeDocumentId(child) != Docs.getTreeDocumentId(parent) ||
                Docs.getDocumentId(child) != Docs.getDocumentId(parent).trimEnd('/') + "/" + expectedName || library.info(child).name != expectedName)
                throw IOException("El proveedor cambió el nombre o la ubicación de un archivo; extracción detenida.")
        }
        override fun commit(): Uri {
            check()
            val query = Docs.buildChildDocumentsUriUsingTree(parent, Docs.getDocumentId(parent))
            val names = resolver.query(query, arrayOf(Docs.Document.COLUMN_DISPLAY_NAME), null, null, null)?.use { c ->
                val result = ArrayList<String>()
                while (c.moveToNext()) { check(); if (result.size >= 50000) throw IOException("Demasiados archivos en la carpeta de destino."); result += c.getString(0) }
                result
            } ?: throw IOException("No se pudo consultar la carpeta de destino.")
            val name = ArchivePaths.unique(stem, names)
            val final = Docs.renameDocument(resolver, staging!!, name) ?: throw IOException("No se pudo completar el cambio de nombre de la carpeta extraída.")
            // Keep the returned URI for rollback even when a provider unexpectedly changes the name.
            staging = final
            verifyChild(parent, final, name)
            return final
        }
        override fun rollback() {
            staging?.let { if (!Docs.deleteDocument(resolver, it)) throw IOException("No se pudo borrar la carpeta temporal $it.") }
            staging = null
        }
    }
}
