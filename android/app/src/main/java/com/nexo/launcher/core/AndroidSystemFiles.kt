package com.nexo.launcher.core

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.ParcelFileDescriptor
import android.provider.DocumentsContract as Docs
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import java.io.File
import java.io.IOException
import java.io.OutputStream
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicReference

internal class AndroidSystemFiles(private val context: Context, private val library: SafLibrary) {
    private val resolver = context.contentResolver
    private val prefs = context.getSharedPreferences("nexo", Context.MODE_PRIVATE)
    private val statuses = ConcurrentHashMap<String, Pair<Uri?, SystemSetupStatus>>()
    private val mutex = Mutex()
    private val required = setOf("PS2", "PS3", "Switch", "Xbox", "Xbox360", "Retro")
    fun clearStatuses() = statuses.clear()
    fun note(id: String): List<String> = if (id !in required) emptyList() else
        listOf(statuses[id]?.takeIf { it.first == library.root }?.second?.message ?: "Archivos del sistema: consulta BIOS/$id y el botón de preparación de Nexo.")
    @Suppress("DEPRECATION") private fun installedPackage(id: String): String? = Emulators.profile(id)?.packages?.firstOrNull {
        runCatching { context.packageManager.getApplicationInfo(it, 0).enabled }.getOrDefault(false)
    }
    private fun launchIntent(id: String): Intent? = installedPackage(id)?.let { context.packageManager.getLaunchIntentForPackage(it) }
    private fun authority(): String {
        val pkg = installedPackage("Switch") ?: throw IOException("Instala primero Eden.")
        val authority = "$pkg.user"
        @Suppress("DEPRECATION") val provider = context.packageManager.resolveContentProvider(authority, 0)
        check(provider != null && provider.packageName == pkg && provider.exported) {
            "Esta versión de Eden no ofrece su carpeta al selector de Android. Usa Instalar claves/firmware dentro de Eden."
        }
        return authority
    }
    fun grantIntent(): Intent = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).apply {
        putExtra(Docs.EXTRA_INITIAL_URI, Docs.buildDocumentUri(authority(), "root/"))
        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION or
            Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION or Intent.FLAG_GRANT_PREFIX_URI_PERMISSION)
    }
    fun onGrant(uri: Uri) {
        require(uri.scheme == "content" && Docs.isTreeUri(uri) && uri.authority == authority() && Docs.getTreeDocumentId(uri).trimEnd('/') == "root") {
            "Elige la raíz de Eden, no una subcarpeta ni la biblioteca Nexo."
        }
        resolver.takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION)
        check(prefs.edit().putString("edenTree", uri.toString()).commit()) { "No se pudo guardar el permiso de Eden." }
        statuses.remove("Switch")
    }
    private fun grantedTree(): Uri? {
        val uri = prefs.getString("edenTree", null)?.let(Uri::parse) ?: return null
        val expected = runCatching { authority() }.getOrNull() ?: return null
        return uri.takeIf { it.authority == expected && Docs.isTreeUri(it) && Docs.getTreeDocumentId(it).trimEnd('/') == "root" &&
            resolver.persistedUriPermissions.any { p -> p.uri == it && p.isReadPermission && p.isWritePermission } }
    }
    suspend fun status(id: String): SystemSetupStatus {
        require(Catalog.systems.any { it.id == id }) { "Consola no válida." }
        val selectedRoot = library.root
        val result = try { statusImpl(id) }
        catch (e: CancellationException) { throw e }
        catch (e: Exception) { SystemSetupStatus(id, SystemSetupMode.Guided, null,
            "No se pudo consultar BIOS/$id: ${e.message ?: "revisa los permisos"}", listOf(SystemFilesLogic.readme(id))) }
        if (selectedRoot == library.root) statuses[id] = selectedRoot to result
        return result
    }
    private suspend fun statusImpl(id: String): SystemSetupStatus {
        if (id !in required) return SystemSetupStatus(id, SystemSetupMode.NotRequired, null, "No necesita BIOS ni firmware externos para el uso normal.")
        if (library.root == null) return SystemSetupStatus(id, SystemSetupMode.MissingFiles, null, "Elige primero la biblioteca para crear BIOS/$id.")
        val folder = library.biosFolder(id)
        val path = library.realPath(folder) ?: "${library.info(library.document(library.root!!)).name}/BIOS/$id"
        val files = library.children(folder).filter { !it.directory && it.size != 0L }
        val names = files.map { it.name }
        if (id == "Switch") {
            var existingKeys = false
            val tree = grantedTree()
            if (tree != null) {
                val store = EdenStore(tree)
                val prod = store.find("keys/prod.keys")
                val hasKeys = prod != null && runCatching { SystemFilesLogic.validateKeys("prod.keys", library.readSmall(prod, 2 * 1024 * 1024)) }.isSuccess
                existingKeys = hasKeys
                val registered = store.folder("nand/system/Contents/registered", false)
                val hasFirmware = registered != null && store.children(registered).any { !it.directory && Regex("[0-9a-fA-F]{32}\\.nca").matches(it.name) && it.size >= 1024 }
                if (hasKeys && hasFirmware) return SystemSetupStatus(id, SystemSetupMode.Installed, folder,
                    "Claves y firmware presentes en Eden. Reinícialo tras instalar. Su correspondencia de versiones debe comprobarla Eden.", listOf("Puedes volver a importar los archivos de $path cuando los actualices."))
            }
            val missing = if (existingKeys) null else SystemFilesLogic.missing(id, names)
            val steps = listOf("Pon prod.keys, title.keys (opcional) y un único ZIP de firmware en $path.",
                "Cierra Eden antes de importar. Concede su carpeta raíz una vez y pulsa instalar.",
                "Reinicia Eden después. Nexo no descifra NCA ni puede deducir la versión del firmware o las claves por el nombre del ZIP.")
            // Android no deja a Nexo leer los datos privados de Eden: no ver las claves es un aviso, nunca un bloqueo.
            if (missing != null) return SystemSetupStatus(id, SystemSetupMode.MissingFiles, folder, SwitchNotice.keys(false, false, path)!!, steps)
            return SystemSetupStatus(id, if (tree == null) SystemSetupMode.NeedsEdenGrant else SystemSetupMode.Automatic, folder,
                if (tree == null) "Concede acceso a la carpeta raíz de Eden para importar los archivos de $path."
                else "Archivos listos para copiar a Eden.${if (names.none { it.endsWith(".zip", true) }) " No hay ZIP de firmware en $path; se copiarán solo las claves." else ""}", steps)
        }
        val missing = SystemFilesLogic.missing(id, names)
        val steps = when (id) {
            "PS2" -> listOf("Abre ARMSX2 → asistente → BIOS (o BIOS Manager si ya hiciste el asistente).", "Selecciona la carpeta $path; ARMSX2 importa las BIOS .bin que contiene.")
            "PS3" -> listOf("Abre aPS3e → QuickStart/asistente → instalar firmware.", "Selecciona $path/PS3UPDAT.PUP y espera a que su instalador termine.")
            "Xbox" -> listOf("Abre X1 BOX → asistente de configuración.", "Selecciona MCPX, BIOS flash y HDD entre estos archivos de $path: ${names.filterNot { it.equals("LEEME.txt", true) }.joinToString().ifEmpty { "aún no hay archivos" }}.", "Selecciona después Juegos/Xbox como carpeta de juegos.")
            "Xbox360" -> listOf("Abre aX360e y selecciona ${library.realPath(library.systemFolder(id)) ?: "Juegos/Xbox360"} como carpeta de juegos desde su menú. No necesita BIOS.")
            else -> listOf("Abre RetroArch → Ajustes → Directorios → Sistema/BIOS → $path.", "Concede acceso al almacenamiento a RetroArch. Solo algunos núcleos requieren BIOS.")
        }
        return SystemSetupStatus(id, if (missing != null) SystemSetupMode.MissingFiles else SystemSetupMode.Guided, folder,
            if (missing != null) "$missing Pon los archivos en $path." else "Preparación guiada: ${steps.first()} Nexo no puede comprobar la configuración privada del emulador.", steps)
    }

    suspend fun install(id: String, callback: (ArchiveProgress) -> Unit): SystemFilesResult = coroutineScope {
        check(mutex.tryLock()) { "Ya hay una importación de archivos del sistema en curso." }
        val progress = AtomicReference(ArchiveProgress(0, null))
        val reporter = launch { var previous: ArchiveProgress? = null; while (isActive) {
            val value = progress.get(); if (value != previous) { callback(value); previous = value }; delay(150)
        } }
        try {
            val result = withContext(Dispatchers.IO) {
                val status = status(id)
                if (id != "Switch" || status.mode in listOf(SystemSetupMode.MissingFiles, SystemSetupMode.NeedsEdenGrant) || status.folder == null)
                    return@withContext SystemFilesResult(status, (listOf(status.message) + status.steps).joinToString("\n"), launchIntent(id))
                val tree = grantedTree() ?: return@withContext SystemFilesResult(status, "Concede la carpeta raíz de Eden o usa su instalador interno.", launchIntent(id))
                val root = library.root
                val job = currentCoroutineContext()
                val check = { job.ensureActive(); check(root == library.root) { "La biblioteca cambió durante la importación." } }
                val stage = File(context.cacheDir, "system-import-${UUID.randomUUID()}")
                check(stage.mkdirs()) { "No se pudo crear el temporal de importación." }
                var keepBackup = false
                try {
                    val sources = library.children(status.folder).filter { !it.directory }
                    val files = linkedMapOf<String, File>()
                    for (name in listOf("prod.keys", "title.keys")) {
                        val matches = sources.filter { it.name.equals(name, true) }
                        if (matches.size > 1) throw IOException("Hay varios $name; deja solo el que deseas importar.")
                        val doc = matches.singleOrNull() ?: continue
                        val bytes = library.readSmall(doc.uri, 2 * 1024 * 1024)
                        SystemFilesLogic.validateKeys(name, bytes)
                        files["keys/$name"] = File(stage, name).apply { writeBytes(bytes) }
                    }
                    val zips = sources.filter { it.name.endsWith(".zip", true) }
                    var stagedFirmwareBytes = 0L
                    if (zips.size > 1) throw IOException("Hay varios ZIP de firmware. Deja solo la versión que deseas instalar en BIOS/Switch.")
                    zips.singleOrNull()?.let { zip ->
                        ParcelFileDescriptor.AutoCloseInputStream(resolver.openFileDescriptor(zip.uri, "r") ?: throw IOException("No se pudo leer el ZIP de firmware.")).use { input ->
                            ArchiveReaders.open(input.channel, "zip", check).use { reader ->
                                val plan = SystemFilesLogic.firmwareEntries(reader.entries)
                                val total = plan.sumOf { reader.entries[it.first].size }
                                val progressTotal = 2 * total + files.values.sumOf { it.length() }
                                if (stage.usableSpace < total + ArchiveValidation.RESERVE) throw IOException("No hay espacio en la caché para preparar ${total / 1048576} MB de firmware.")
                                var done = 0L
                                for ((index, name) in plan) {
                                    check(); val entry = reader.entries[index]; var size = 0L
                                    val outFile = File(stage, name)
                                    outFile.outputStream().use { output -> reader.write(index, object : OutputStream() {
                                        override fun write(b: Int) = write(byteArrayOf(b.toByte()), 0, 1)
                                        override fun write(b: ByteArray, off: Int, len: Int) {
                                            check(); if (size > entry.size - len) throw IOException("La NCA supera el tamaño declarado.")
                                            output.write(b, off, len); size += len; done += len; progress.set(ArchiveProgress(done, progressTotal))
                                        }
                                    }) }
                                    if (size != entry.size) throw IOException("Firmware incompleto.")
                                    files["nand/system/Contents/registered/$name"] = outFile
                                }
                                stagedFirmwareBytes = done
                            }
                        }
                    }
                    if (files.isEmpty()) throw IOException("No hay claves ni un ZIP de firmware para importar en BIOS/Switch.")
                    SystemFileTransaction.install(files, EdenStore(tree), stage, check) { done, total -> progress.set(ArchiveProgress(stagedFirmwareBytes + done, stagedFirmwareBytes + total)) }
                    val updated = this@AndroidSystemFiles.status(id)
                    SystemFilesResult(updated, "Archivos copiados a Eden. Cierra y vuelve a abrir Eden para recargar claves y firmware. " +
                        "Nexo no puede verificar que sus versiones coincidan; si Eden rechaza las NCA, comprueba tus claves. Los originales de BIOS/Switch se conservan.", launchIntent(id))
                } catch (e: Throwable) {
                    keepBackup = e.suppressed.isNotEmpty()
                    if (keepBackup && e !is CancellationException) throw IOException("La importación falló y no se pudieron restaurar todos los archivos de Eden. Se conservan las copias de respaldo en la caché de Nexo ($stage). Reimporta tus archivos antes de jugar.", e)
                    throw e
                } finally { if (!keepBackup) stage.deleteRecursively() }
            }
            reporter.cancelAndJoin(); callback(progress.get()); result
        } finally { reporter.cancel(); mutex.unlock() }
    }

    /** Only the explicitly granted root of Eden; all paths below are fixed by the importer. */
    private inner class EdenStore(tree: Uri) : SystemFileStore<Uri> {
        private val root = library.document(tree)
        private val folders = HashMap<String, Uri>().apply { put("", root) }
        fun children(parent: Uri): List<Document> {
            val query = Docs.buildChildDocumentsUriUsingTree(parent, Docs.getDocumentId(parent))
            return resolver.query(query, arrayOf(Docs.Document.COLUMN_DOCUMENT_ID, Docs.Document.COLUMN_DISPLAY_NAME,
                Docs.Document.COLUMN_MIME_TYPE, Docs.Document.COLUMN_SIZE), null, null, null)?.use { c ->
                val result = ArrayList<Document>()
                while (c.moveToNext()) {
                    if (result.size >= 10000) throw IOException("La carpeta de Eden contiene demasiados archivos.")
                    val id = c.getString(0); val name = c.getString(1)
                    result += Document(Docs.buildDocumentUriUsingTree(parent, id), id, name, c.getString(2) == Docs.Document.MIME_TYPE_DIR, if (c.isNull(3)) -1 else c.getLong(3), 0)
                }; result
            } ?: throw IOException("No se puede leer la carpeta de Eden; concede de nuevo el permiso.")
        }
        fun folder(path: String, create: Boolean): Uri? {
            var current = root
            val parts = if (path.isEmpty()) emptyList() else ArchivePaths.parts(path)
            for (i in parts.indices) {
                val key = parts.take(i + 1).joinToString("/")
                val cached = folders[key]
                if (cached != null) { current = cached; continue }
                val existing = children(current).filter { it.name.equals(parts[i], true) }
                if (existing.size > 1 || existing.any { !it.directory }) throw IOException("La carpeta $key de Eden no es válida.")
                current = existing.singleOrNull()?.uri ?: if (create) Docs.createDocument(resolver, current, Docs.Document.MIME_TYPE_DIR, parts[i])
                    ?: throw IOException("No se pudo crear $key en Eden.") else return null
                folders[key] = current
            }
            return current
        }
        override fun find(path: String): Uri? {
            val parent = folder(path.substringBeforeLast('/'), false) ?: return null
            val files = children(parent).filter { it.name.equals(path.substringAfterLast('/'), true) }
            if (files.size > 1 || files.any { it.directory }) throw IOException("Hay un conflicto en $path de Eden.")
            return files.singleOrNull()?.uri
        }
        override fun create(path: String): Uri {
            check(find(path) == null) { "$path apareció durante la importación; vuelve a intentarlo con Eden cerrado." }
            val parent = folder(path.substringBeforeLast('/'), true)!!
            val doc = Docs.createDocument(resolver, parent, "application/octet-stream", path.substringAfterLast('/')) ?: throw IOException("No se pudo crear $path en Eden.")
            if (library.info(doc).name != path.substringAfterLast('/')) {
                Docs.deleteDocument(resolver, doc)
                throw IOException("Eden cambió el nombre de $path; importación detenida.")
            }
            return doc
        }
        override fun read(document: Uri) = resolver.openInputStream(document) ?: throw IOException("No se pudo proteger el archivo actual de Eden.")
        override fun write(document: Uri) = resolver.openOutputStream(document, "wt") ?: throw IOException("No se pudo escribir en Eden.")
        override fun delete(document: Uri) { if (!Docs.deleteDocument(resolver, document)) throw IOException("No se pudo retirar un archivo incompleto de Eden.") }
    }
}
