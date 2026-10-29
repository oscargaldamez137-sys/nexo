package com.nexo.launcher.core

import java.io.File
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.util.Locale

/** File selection and import transactions are independent of Android and tested on the JVM. */
internal object SystemFilesLogic {
    const val MAX_FIRMWARE = 4L * 1024 * 1024 * 1024
    fun readme(id: String) = when (id) {
        "PS2" -> "Pon aquí la BIOS de tu PS2 (.bin). En ARMSX2: asistente → BIOS → elige esta carpeta; después puedes usar BIOS Manager. Nexo no puede configurar la carpeta privada del emulador."
        "PS3" -> "Pon aquí PS3UPDAT.PUP. En aPS3e: QuickStart/asistente → instalar firmware → selecciona ese archivo. El firmware lo publica Sony en su web oficial. No copies dev_flash a mano."
        "Switch" -> "Pon aquí prod.keys, title.keys (opcional) y un único ZIP de firmware con archivos .nca. Concede a Nexo la raíz de la carpeta Eden para copiarlos. Cierra Eden antes y reinícialo después. Las claves deben corresponder a la versión del firmware; Nexo no puede comprobar esa correspondencia sin descifrar las NCA."
        "Xbox" -> "Pon aquí mcpx_1.0.bin (boot ROM MCPX), tu BIOS flash .bin y el HDD .qcow2/.img. En el asistente de X1 BOX elige los tres archivos y después la carpeta de juegos."
        "Xbox360" -> "No necesita BIOS. En el menú de aX360e elige Juegos/Xbox360 como carpeta de juegos."
        "Retro" -> "BIOS opcionales según el núcleo. En RetroArch → Ajustes → Directorios → Sistema/BIOS elige esta carpeta; concede a RetroArch acceso al almacenamiento si lo solicita."
        else -> "Este emulador no necesita BIOS externa para el uso normal. Conserva tus juegos en Juegos/$id."
    }
    fun missing(id: String, names: List<String>): String? {
        val n = names.map { it.lowercase(Locale.ROOT) }
        return when (id) {
            "PS2" -> if (n.none { it.endsWith(".bin") }) "Falta la BIOS de PS2 (.bin)." else null
            "PS3" -> if ("ps3updat.pup" !in n) "Falta PS3UPDAT.PUP." else null
            "Switch" -> if ("prod.keys" !in n) "Faltan las claves de Switch (prod.keys)." else null
            "Xbox" -> {
                val mcpx = n.filter { "mcpx" in it && it.endsWith(".bin") }
                val missing = listOfNotNull(if (mcpx.isEmpty()) "MCPX" else null,
                    if (n.none { it.endsWith(".bin") && it !in mcpx }) "BIOS flash" else null,
                    if (n.none { it.endsWith(".qcow2") || it.endsWith(".img") }) "disco duro .qcow2/.img" else null)
                if (missing.isNotEmpty()) "Falta: ${missing.joinToString(", ")}." else null
            }
            else -> null
        }
    }
    fun validateKeys(name: String, bytes: ByteArray) {
        if (bytes.isEmpty() && name == "title.keys") return // Optional; no tickets may have been exported yet.
        if (bytes.isEmpty() || bytes.size > 2 * 1024 * 1024) throw IOException("$name está vacío o es demasiado grande.")
        val lines = bytes.toString(Charsets.UTF_8).removePrefix("\uFEFF").lineSequence()
            .map { it.substringBefore('#').trim() }.filter { it.isNotEmpty() }.toList()
        if (lines.isEmpty() && name == "title.keys") return
        if (lines.isEmpty() || lines.any { !Regex("[a-zA-Z0-9_]+\\s*=\\s*(?:[0-9a-fA-F]{2}){16,512}").matches(it) })
            throw IOException("$name no tiene un formato de claves válido. No se ha copiado.")
        if (name == "prod.keys" && lines.none { Regex("header_key\\s*=\\s*[0-9a-fA-F]{64}").matches(it) })
            throw IOException("prod.keys no contiene header_key. Comprueba el archivo de claves de tu consola.")
    }
    fun firmwareEntries(entries: List<PackedEntry>): List<Pair<Int, String>> {
        if (entries.size > 4096) throw IOException("El ZIP de firmware tiene demasiadas entradas.")
        var total = 0L; val names = HashSet<String>(); val result = ArrayList<Pair<Int, String>>()
        entries.forEachIndexed { index, entry ->
            val parts = ArchivePaths.parts(entry.name)
            if (entry.link) throw IOException("El ZIP de firmware contiene enlaces.")
            if (!entry.directory && parts.last().endsWith(".nca", true)) {
                val name = parts.last().lowercase(Locale.ROOT)
                if (!Regex("[0-9a-f]{32}\\.nca").matches(name) || !names.add(name)) throw IOException("Nombres NCA no válidos o duplicados en el firmware.")
                if (entry.size !in 1024..(512L * 1024 * 1024)) throw IOException("Una NCA del firmware tiene un tamaño no válido.")
                total = ArchiveValidation.sum(total, entry.size)
                if (total > MAX_FIRMWARE) throw IOException("El firmware supera el límite de 4 GiB.")
                result += index to name
            }
        }
        if (result.isEmpty()) throw IOException("El ZIP no contiene archivos NCA de firmware.")
        return result
    }
}

internal interface SystemFileStore<T> {
    fun find(path: String): T?
    fun create(path: String): T
    fun read(document: T): InputStream
    fun write(document: T): OutputStream // must truncate existing files, not create a renamed duplicate
    fun delete(document: T)
}
internal object SystemFileTransaction {
    /** All input files have already passed CRC/format checks. Keep backups until every write succeeds. */
    fun <T> install(files: Map<String, File>, store: SystemFileStore<T>, backupFolder: File,
                    check: () -> Unit, progress: (Long, Long) -> Unit) {
        data class Change<T>(val document: T, val backup: File?)
        val changed = ArrayList<Change<T>>()
        val backups = HashMap<String, Pair<T, File>>()
        val total = files.values.sumOf { it.length() }; var written = 0L
        fun copy(input: InputStream, output: OutputStream, cancel: Boolean, limit: Long, report: Boolean = false): Long {
            val buffer = ByteArray(65536); var size = 0L
            while (true) {
                if (cancel) check()
                val n = input.read(buffer); if (n < 0) break
                size += n; if (size > limit) throw IOException("El archivo excede el tamaño permitido.")
                output.write(buffer, 0, n)
                if (report) { written += n; progress(written, total) }
            }
            return size
        }
        // Back up everything before modifying Eden. Originals in BIOS remain untouched.
        var backupBytes = 0L
        for (path in files.keys) {
            check()
            val doc = store.find(path) ?: continue
            val backup = File(backupFolder, "backup-${backups.size}")
            val available = backupFolder.usableSpace - ArchiveValidation.RESERVE
            if (available <= 0) throw IOException("Falta espacio para proteger los archivos actuales de Eden.")
            store.read(doc).use { input -> backup.outputStream().use { output ->
                backupBytes += copy(input, output, true, minOf(available, SystemFilesLogic.MAX_FIRMWARE - backupBytes))
            } }
            backups[path] = doc to backup
        }
        try {
            progress(0, total)
            for ((path, file) in files) {
                check()
                val saved = backups[path]
                val doc = saved?.first ?: store.create(path)
                changed += Change(doc, saved?.second)
                file.inputStream().use { input -> store.write(doc).use { out ->
                    if (copy(input, out, true, file.length(), true) != file.length()) throw IOException("Copia incompleta de $path.")
                } }
            }
            check()
        } catch (e: Throwable) {
            for (change in changed.asReversed()) try {
                if (change.backup == null) store.delete(change.document)
                else change.backup.inputStream().use { input -> store.write(change.document).use { out -> copy(input, out, false, change.backup.length()) } }
            } catch (restore: Exception) { e.addSuppressed(restore) }
            throw e
        }
    }
}
