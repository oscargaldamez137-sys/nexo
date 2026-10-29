package com.nexo.launcher.core

import java.io.Closeable
import java.io.IOException
import java.io.OutputStream
import java.util.Locale

/** No path from an archive is ever passed to File, SAF or a decoder's extraction helper. */
internal object ArchivePaths {
    fun parts(name: String): List<String> {
        val normalized = name.replace('\\', '/').removeSuffix("/")
        val parts = normalized.split('/')
        if (normalized.length > 2048 || parts.size > 32 || parts.any {
            it.isEmpty() || it == "." || it == ".." || it.length > 240 || it.endsWith('.') || it.endsWith(' ') ||
                it.any { c -> c < ' ' || c == ':' || c == '\u007f' } ||
                it.substringBefore('.').uppercase(Locale.ROOT) in setOf("CON", "PRN", "AUX", "NUL", "COM1", "LPT1")
        }) throw IOException("Ruta insegura en el comprimido: ${name.take(120)}")
        return parts
    }
    fun folder(name: String): String {
        val stem = GameFormats.stem(name).take(180).trim().trimEnd('.')
        return parts(stem).singleOrNull() ?: throw IOException("Nombre de comprimido no válido.")
    }
    fun unique(stem: String, existing: Collection<String>): String {
        val used = existing.map { it.lowercase(Locale.ROOT) }.toHashSet()
        if (stem.lowercase(Locale.ROOT) !in used) return stem
        for (n in 2..10000) { val candidate = "$stem ($n)"; if (candidate.lowercase(Locale.ROOT) !in used) return candidate }
        throw IOException("Hay demasiadas carpetas con el mismo nombre.")
    }
}

internal data class PackedEntry(val name: String, val directory: Boolean, val size: Long, val link: Boolean = false)
internal interface ArchiveReader : Closeable {
    val entries: List<PackedEntry>
    val compressedSize: Long
    fun write(index: Int, output: OutputStream)
}

/** The transaction owns only newly-created staging documents, never existing user files. */
internal interface ArchiveDestination<T> {
    fun availableBytes(): Long
    fun begin()
    fun directory(parts: List<String>)
    fun file(parts: List<String>): OutputStream
    fun commit(): T
    fun rollback()
}

internal object ArchiveEngine {
    fun <T> extract(reader: ArchiveReader, target: ArchiveDestination<T>, check: () -> Unit,
                    progress: (Long, Long) -> Unit): Pair<T, Long> {
        val entries = reader.entries
        if (entries.size > ArchiveValidation.MAX_ENTRIES || entries.none { !it.directory }) throw IOException("El comprimido no contiene archivos o tiene demasiadas entradas.")
        var total = 0L
        val names = HashMap<String, Boolean>()
        val paths = entries.map { entry ->
            check()
            if (entry.link) throw IOException("El comprimido contiene enlaces; Nexo no los extrae.")
            val parts = ArchivePaths.parts(entry.name)
            val key = parts.joinToString("/").lowercase(Locale.ROOT)
            if (names.put(key, entry.directory) != null) throw IOException("El comprimido contiene nombres repetidos: ${entry.name.take(100)}")
            if (entry.size < 0 || (entry.directory && entry.size != 0L)) throw IOException("El comprimido contiene tamaños no válidos.")
            total = ArchiveValidation.sum(total, entry.size)
            val packed = reader.compressedSize.coerceAtLeast(1)
            if (total > ArchiveValidation.MAX_TOTAL || (packed <= ArchiveValidation.MAX_TOTAL / ArchiveValidation.MAX_RATIO && total > packed * ArchiveValidation.MAX_RATIO))
                throw IOException("El comprimido supera el límite de 256 GiB o de expansión 1000:1.")
            parts
        }
        val allNames = names.keys.toHashSet()
        paths.forEach { parts ->
            for (i in 1 until parts.size) {
                val parent = parts.take(i).joinToString("/").lowercase(Locale.ROOT)
                if (names[parent] == false) throw IOException("Una ruta del comprimido es a la vez archivo y carpeta.")
                allNames.add(parent)
                if (allNames.size > ArchiveValidation.MAX_ENTRIES) throw IOException("Demasiadas carpetas y archivos dentro del comprimido.")
            }
        }
        val available = target.availableBytes()
        if (available < 0) throw IOException("No se puede comprobar el espacio libre. Usa una biblioteca del almacenamiento interno o de una tarjeta SD.")
        if (available < ArchiveValidation.sum(total, ArchiveValidation.RESERVE)) throw IOException("No hay espacio suficiente: hacen falta ${total / 1048576} MB y 64 MB de margen. El original se conserva.")
        progress(0, total); check()
        var begun = false; var committed = false; var written = 0L
        try {
            begun = true; target.begin()
            entries.forEachIndexed { index, entry ->
                check()
                if (entry.directory) target.directory(paths[index]) else {
                    var fileBytes = 0L
                    target.file(paths[index]).use { out ->
                        reader.write(index, object : OutputStream() {
                            private val single = ByteArray(1)
                            override fun write(b: Int) { single[0] = b.toByte(); write(single, 0, 1) }
                            override fun write(b: ByteArray, off: Int, len: Int) {
                                check()
                                if (len < 0 || fileBytes > entry.size - len) throw IOException("El archivo descomprimido excede el tamaño declarado.")
                                out.write(b, off, len)
                                fileBytes += len; written += len; progress(written, total)
                            }
                        })
                    }
                    if (fileBytes != entry.size) throw IOException("Descarga incompleta: el tamaño extraído de ${entry.name.take(100)} no coincide.")
                }
            }
            check()
            val result = target.commit(); committed = true
            return result to written
        } catch (e: Throwable) {
            if (begun && !committed) try { target.rollback() } catch (cleanup: Exception) { e.addSuppressed(cleanup) }
            throw e
        }
    }
}
