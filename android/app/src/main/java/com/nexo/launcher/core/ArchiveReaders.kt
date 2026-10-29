package com.nexo.launcher.core

import com.github.junrar.Archive
import com.github.junrar.ArchiveOptions
import com.github.junrar.io.SeekableReadOnlyByteChannel
import com.github.junrar.volume.Volume
import com.github.junrar.volume.VolumeManager
import org.apache.commons.compress.archivers.sevenz.SevenZFile
import org.apache.commons.compress.archivers.zip.ZipFile
import java.io.Closeable
import java.io.IOException
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.util.Locale
import java.util.zip.CRC32

/** Bounded, cancellable random access. Libraries cannot close the caller's SAF descriptor. */
internal class CheckedChannel(private val inner: SeekableByteChannel, private val check: () -> Unit) : SeekableByteChannel {
    override fun read(dst: ByteBuffer): Int { check(); return inner.read(dst) }
    override fun position(): Long { check(); return inner.position() }
    override fun position(newPosition: Long): SeekableByteChannel { check(); inner.position(newPosition); return this }
    override fun size(): Long { check(); return inner.size() }
    override fun isOpen() = inner.isOpen
    override fun close() { /* owner closes descriptor */ }
    override fun write(src: ByteBuffer): Int = throw IOException("Solo lectura.")
    override fun truncate(size: Long): SeekableByteChannel = throw IOException("Solo lectura.")
}

internal object ArchiveReaders {
    fun open(channel: SeekableByteChannel, extension: String, check: () -> Unit): ArchiveReader {
        val health = ArchiveValidation.inspect(channel, extension, check)
        if (!health.ready) throw IOException(health.message)
        val safe = CheckedChannel(channel, check)
        safe.position(0)
        return when (extension) {
            "zip" -> ZipReader(safe)
            "7z" -> SevenReader(safe)
            "rar" -> RarReader(safe)
            else -> throw IOException("Formato comprimido no admitido.")
        }
    }
    // A mobile heap must not be exhausted by the dictionary requested by an archive.
    internal fun memoryLimitKiB(): Int = (Runtime.getRuntime().maxMemory() / 4 / 1024).coerceIn(8192, 131072).toInt()
    private class ZipReader(channel: SeekableByteChannel) : ArchiveReader {
        override val compressedSize = channel.size()
        private val zip = ZipFile.builder().setSeekableByteChannel(channel).get()
        private val headers = try { zip.entries.toList().also { list ->
            if (list.size > ArchiveValidation.MAX_ENTRIES) throw IOException("Demasiadas entradas ZIP.")
            if (list.any { !zip.canReadEntryData(it) || it.method !in listOf(0, 8, 9, 12) }) throw IOException("Este ZIP usa cifrado o un método de compresión no admitido.")
        } } catch (e: Exception) { zip.close(); throw e }
        override val entries = headers.map { PackedEntry(it.name, it.isDirectory, it.size, it.isUnixSymlink ||
            (it.unixMode and 0xf000 !in listOf(0, 0x8000, 0x4000))) }
        override fun write(index: Int, output: OutputStream) {
            val header = headers[index]; val crc = CRC32()
            zip.getInputStream(header).use { input ->
                val buffer = ByteArray(65536)
                while (true) { val n = input.read(buffer); if (n < 0) break; if (n == 0) continue; crc.update(buffer, 0, n); output.write(buffer, 0, n) }
            }
            if (crc.value != header.crc) throw IOException("El ZIP está dañado: no coincide el CRC de ${header.name.take(100)}.")
        }
        override fun close() = zip.close()
    }
    private class SevenReader(channel: SeekableByteChannel) : ArchiveReader {
        override val compressedSize = channel.size()
        private val seven = SevenZFile.builder().setSeekableByteChannel(channel).setMaxMemoryLimitKiB(memoryLimitKiB())
            .setTryToRecoverBrokenArchives(false).get()
        private val headers = try { seven.entries.toList().also {
            if (it.size > ArchiveValidation.MAX_ENTRIES) throw IOException("Demasiadas entradas 7z.")
            if (it.any { entry -> entry.isAntiItem }) throw IOException("Los elementos de borrado 7z no están admitidos.")
        } } catch (e: Exception) { seven.close(); throw e }
        override val entries = headers.map {
            val attrs = if (it.hasWindowsAttributes) it.windowsAttributes else 0
            val mode = (attrs ushr 16) and 0xf000
            PackedEntry(it.name ?: throw IOException("Entrada 7z sin nombre."), it.isDirectory, it.size,
                attrs and 0x400 != 0 || mode !in listOf(0, 0x8000, 0x4000))
        }
        override fun write(index: Int, output: OutputStream) {
            seven.getInputStream(headers[index]).use { input ->
                val buffer = ByteArray(65536)
                while (true) { val n = input.read(buffer); if (n < 0) break; if (n > 0) output.write(buffer, 0, n) }
            }
        }
        override fun close() = seven.close()
    }
    private class RarReader(channel: SeekableByteChannel) : ArchiveReader {
        override val compressedSize = channel.size()
        private val rar = Archive(VolumeManager { archive, last ->
            if (last != null) throw IOException("Falta una parte del RAR: usa tu gestor de archivos para reunir los volúmenes.")
            object : Volume {
                override fun getArchive() = archive
                override fun getLength() = channel.size()
                override fun getChannel(): SeekableReadOnlyByteChannel = object : SeekableReadOnlyByteChannel {
                    override fun getPosition() = channel.position()
                    override fun setPosition(pos: Long) { channel.position(pos) }
                    override fun read(): Int { val b = ByteBuffer.allocate(1); return if (channel.read(b) <= 0) -1 else b.array()[0].toInt() and 255 }
                    override fun read(buffer: ByteArray, off: Int, count: Int) = channel.read(ByteBuffer.wrap(buffer, off, count))
                    override fun readFully(buffer: ByteArray, count: Int): Int {
                        val b = ByteBuffer.wrap(buffer, 0, count)
                        while (b.hasRemaining()) if (channel.read(b) <= 0) throw IOException("Descarga RAR incompleta.")
                        return count
                    }
                    override fun close() { }
                }
            }
        }, ArchiveOptions.builder().maxDictionarySize(memoryLimitKiB().toLong() * 1024).build())
        private val headers = try {
            if (rar.hasBrokenHeaders()) throw IOException("El RAR contiene cabeceras dañadas o incompletas.")
            rar.fileHeaders.also { list ->
                if (list.size > ArchiveValidation.MAX_ENTRIES) throw IOException("Demasiadas entradas RAR.")
                if (list.any { it.isEncrypted || it.isSplitBefore || it.isSplitAfter || it.isUnpSizeUnknown })
                    throw IOException("RAR cifrado, dividido o sin tamaños: descomprímelo con tu gestor de archivos.")
            }
        } catch (e: Exception) { rar.close(); throw e }
        override val entries = headers.map {
            val mode = it.fileAttr and 0xf000
            val unix = it.hostOS?.name == "unix" || it.rar5HostOsValue == 1L
            PackedEntry(it.fileName, it.isDirectory, it.fullUnpackSize,
                it.redirection != null || (unix && mode !in listOf(0, 0x8000, 0x4000)) || (!unix && it.fileAttr and 0x400 != 0))
        }
        override fun write(index: Int, output: OutputStream) = rar.extractFile(headers[index], output)
        override fun close() = rar.close()
    }
}

