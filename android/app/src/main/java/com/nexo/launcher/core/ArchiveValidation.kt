package com.nexo.launcher.core

import java.io.EOFException
import java.io.IOException
import java.nio.ByteBuffer
import java.nio.channels.SeekableByteChannel
import java.util.Locale
import java.util.zip.CRC32

internal data class ArchiveHealth(val ready: Boolean, val message: String, val expectedSize: Long? = null)
internal class ShortArchive(val expected: Long? = null) : IOException()

/** Structural checks only: no game data is decompressed during a library scan. */
internal object ArchiveValidation {
    const val MAX_ENTRIES = 50000
    const val MAX_HEADER = 16 * 1024 * 1024
    const val MAX_TOTAL = 256L * 1024 * 1024 * 1024
    const val MAX_RATIO = 1000L
    const val RESERVE = 64L * 1024 * 1024

    fun inspect(channel: SeekableByteChannel, extension: String, check: () -> Unit = {}): ArchiveHealth = try {
        val input = Headers(channel, check)
        when (extension.lowercase(Locale.ROOT)) {
            "zip" -> zip(input)
            "7z" -> seven(input)
            "rar" -> rar(input)
            else -> throw IOException("Formato comprimido no admitido.")
        }
        ArchiveHealth(true, "Listo para descomprimir. El original se conservará.")
    } catch (e: ShortArchive) {
        val have = channel.size().coerceAtLeast(0) / (1024 * 1024)
        val expected = e.expected?.let { " de ${it / (1024 * 1024)} MB" } ?: " MB; faltan datos o la cabecera final"
        ArchiveHealth(false, "Descarga incompleta: $have$expected. Vuelve a descargar el archivo.", e.expected)
    } catch (e: EOFException) {
        ArchiveHealth(false, "Descarga incompleta: falta parte de la cabecera. Vuelve a descargar el archivo.")
    } catch (e: IOException) {
        ArchiveHealth(false, e.message ?: "No se puede leer este archivo comprimido.")
    }

    internal class Headers(val channel: SeekableByteChannel, val check: () -> Unit) {
        val size = channel.size()
        private var budget = 64L * 1024 * 1024
        fun read(offset: Long, count: Int): ByteArray {
            check()
            if (offset < 0 || count < 0 || count > MAX_HEADER) throw IOException("Cabecera de archivo no válida.")
            val end = sum(offset, count.toLong())
            if (end > size) throw ShortArchive(end)
            budget -= count
            if (budget < 0) throw IOException("El archivo supera el límite de cabeceras de Nexo.")
            channel.position(offset)
            val buffer = ByteBuffer.allocate(count)
            while (buffer.hasRemaining()) { check(); if (channel.read(buffer) <= 0) throw ShortArchive(end) }
            return buffer.array()
        }
    }
    fun sum(a: Long, b: Long): Long {
        if (a < 0 || b < 0 || a > Long.MAX_VALUE - b) throw IOException("Tamaño de archivo no válido.")
        return a + b
    }
    fun u16(b: ByteArray, p: Int) = (b[p].toInt() and 255) or ((b[p + 1].toInt() and 255) shl 8)
    fun u32(b: ByteArray, p: Int): Long = (0..3).fold(0L) { v, i -> v or ((b[p + i].toLong() and 255) shl (8 * i)) }
    fun u64(b: ByteArray, p: Int): Long {
        if (b[p + 7] < 0) throw IOException("Tamaño de archivo fuera de rango.")
        return (0..7).fold(0L) { v, i -> v or ((b[p + i].toLong() and 255) shl (8 * i)) }
    }
    private fun crc(b: ByteArray, from: Int = 0, count: Int = b.size - from) = CRC32().apply { update(b, from, count) }.value
    private fun seven(h: Headers) {
        val b = h.read(0, 32)
        if (!b.copyOfRange(0, 6).contentEquals(byteArrayOf(0x37, 0x7a, 0xbc.toByte(), 0xaf.toByte(), 0x27, 0x1c)))
            throw IOException("El archivo no tiene una cabecera 7z válida.")
        if (b[6] != 0.toByte() || crc(b, 12) != u32(b, 8)) throw IOException("La cabecera 7z está dañada o usa una versión no admitida.")
        val offset = sum(32, u64(b, 12)); val length = u64(b, 20); val expected = sum(offset, length)
        if (expected > h.size) throw ShortArchive(expected)
        if (length > MAX_HEADER) throw IOException("La cabecera 7z es demasiado grande.")
        if (crc(h.read(offset, length.toInt())) != u32(b, 28)) throw IOException("La cabecera final 7z está dañada.")
    }
    private fun zip(h: Headers) {
        if (h.size < 22) throw ShortArchive(22)
        val tail = h.read((h.size - 65557).coerceAtLeast(0), h.size.coerceAtMost(65557).toInt())
        val e = (tail.size - 22 downTo 0).firstOrNull {
            u32(tail, it) == 0x06054b50L && it + 22 + u16(tail, it + 20) == tail.size
        } ?: throw ShortArchive()
        val eocd = h.size - tail.size + e
        if (u16(tail, e + 4) != 0 || u16(tail, e + 6) != 0) throw IOException("ZIP dividido en partes: descomprímelo con tu gestor de archivos.")
        var entries = u16(tail, e + 10).toLong(); var cdSize = u32(tail, e + 12); var cd = u32(tail, e + 16)
        var cdLimit = eocd
        // ZIP64 writers may include the locator/end record even when every classic
        // field still fits (e.g. Zip64Mode.Always). The locator is authoritative.
        val hasLocator = eocd >= 20 && u32(h.read(eocd - 20, 4), 0) == 0x07064b50L
        if (hasLocator || entries == 65535L || cdSize == 0xffffffffL || cd == 0xffffffffL) {
            if (!hasLocator) throw IOException("Falta el localizador ZIP64.")
            val locator = h.read(eocd - 20, 20)
            if (u32(locator, 0) != 0x07064b50L || u32(locator, 4) != 0L || u32(locator, 16) != 1L) throw IOException("ZIP64 dividido o no válido.")
            cdLimit = u64(locator, 8)
            val z = h.read(cdLimit, 56)
            if (u32(z, 0) != 0x06064b50L || u32(z, 16) != 0L || u32(z, 20) != 0L || u64(z, 24) != u64(z, 32) ||
                sum(cdLimit, sum(12, u64(z, 4))) != eocd - 20) throw IOException("Directorio ZIP64 no válido.")
            if ((entries != 65535L && entries != u64(z, 32)) || (cdSize != 0xffffffffL && cdSize != u64(z, 40)) ||
                (cd != 0xffffffffL && cd != u64(z, 48))) throw IOException("Los directorios ZIP y ZIP64 no coinciden.")
            entries = u64(z, 32); cdSize = u64(z, 40); cd = u64(z, 48)
        } else if (u16(tail, e + 8).toLong() != entries) throw IOException("ZIP dividido o no válido.")
        if (entries > MAX_ENTRIES || cdSize > MAX_HEADER) throw IOException("El ZIP tiene demasiadas entradas.")
        val cdEnd = sum(cd, cdSize)
        if (cdEnd > h.size) throw ShortArchive(sum(cdEnd, 22))
        if (cdEnd != cdLimit) throw IOException("El directorio central del ZIP tiene tamaños incoherentes.")
        var pos = cd
        repeat(entries.toInt()) {
            val b = h.read(pos, 46)
            if (u32(b, 0) != 0x02014b50L) throw IOException("Directorio central ZIP dañado.")
            val flags = u16(b, 8)
            if (flags and 0x41 != 0) throw IOException("ZIP con contraseña: descomprímelo con tu gestor de archivos.")
            val name = u16(b, 28); val extra = u16(b, 30); val comment = u16(b, 32)
            var packed = u32(b, 20); val unpacked = u32(b, 24); var local = u32(b, 42)
            var disk = u16(b, 34).toLong()
            if (packed == 0xffffffffL || unpacked == 0xffffffffL || local == 0xffffffffL || disk == 65535L) {
                val ex = h.read(pos + 46 + name, extra); var x = 0; var found = false
                while (x + 4 <= ex.size) {
                    val id = u16(ex, x); val len = u16(ex, x + 2); x += 4
                    if (x + len > ex.size) throw IOException("Datos ZIP64 no válidos.")
                    if (id == 1) {
                        var p = x
                        fun next(): Long { if (p + 8 > x + len) throw IOException("Datos ZIP64 incompletos."); return u64(ex, p).also { p += 8 } }
                        if (unpacked == 0xffffffffL) next()
                        if (packed == 0xffffffffL) packed = next()
                        if (local == 0xffffffffL) local = next()
                        if (disk == 65535L) { if (p + 4 > x + len) throw IOException("Falta el disco ZIP64."); disk = u32(ex, p) }
                        found = true; break
                    }
                    x += len
                }
                if (!found) throw IOException("Faltan los tamaños ZIP64.")
            }
            if (disk != 0L) throw IOException("ZIP dividido en partes no admitido.")
            val lh = h.read(local, 30)
            if (u32(lh, 0) != 0x04034b50L || u16(lh, 8) != u16(b, 10)) throw IOException("Cabecera local ZIP no válida.")
            val dataEnd = sum(sum(local, 30L + u16(lh, 26) + u16(lh, 28)), packed)
            if (dataEnd > h.size) throw ShortArchive(dataEnd)
            if (dataEnd > cd) throw IOException("Los datos ZIP se solapan con su directorio central.")
            pos = sum(pos, 46L + name + extra + comment)
            if (pos > cdEnd) throw IOException("Directorio central ZIP incompleto.")
        }
        if (pos != cdEnd) throw IOException("El número de entradas ZIP no coincide con el directorio.")
    }
    private fun rar(h: Headers) {
        val signature = h.read(0, 7)
        if (!signature.copyOfRange(0, 6).contentEquals(byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 0x07))) throw IOException("Firma RAR no válida.")
        when (signature[6].toInt()) {
            0 -> rar4(h)
            1 -> { if (h.read(7, 1)[0] != 0.toByte()) throw IOException("Firma RAR5 no válida."); rar5(h) }
            else -> throw IOException("Versión RAR no admitida.")
        }
    }
    private fun rar4(h: Headers) {
        var pos = 7L
        repeat(MAX_ENTRIES + 16) {
            val first = h.read(pos, 7); val length = u16(first, 5); val type = first[2].toInt() and 255; val flags = u16(first, 3)
            if (length < 7) throw IOException("Cabecera RAR dañada.")
            val b = h.read(pos, length)
            // Legacy embedded comments have special CRC coverage. Junrar verifies these on extraction.
            if (flags and 8 == 0 && crc(b, 2).toInt() and 65535 != u16(b, 0)) throw IOException("Cabecera RAR dañada (CRC).")
            if (type == 0x73 && flags and 0x81 != 0) throw IOException("RAR dividido o con contraseña: usa tu gestor de archivos.")
            var packed = if (flags and 0x8000 != 0) { if (length < 11) throw IOException("Cabecera RAR dañada."); u32(b, 7) } else 0L
            if (type == 0x74) {
                if (length < 32) throw IOException("Cabecera de entrada RAR dañada.")
                if (flags and 7 != 0) throw IOException("RAR dividido o con contraseña: usa tu gestor de archivos.")
                if (flags and 0x100 != 0) { if (length < 40) throw IOException("Cabecera RAR incompleta."); packed = sum(packed, u32(b, 32) * 0x100000000L) }
            }
            pos = sum(sum(pos, length.toLong()), packed)
            if (pos > h.size) throw ShortArchive(pos)
            if (type == 0x7b) return
            if (pos == h.size) throw ShortArchive()
        }
        throw IOException("El RAR tiene demasiadas entradas.")
    }
    private class Vints(val bytes: ByteArray, var pos: Int = 0) {
        fun next(): Long {
            var v = 0L
            for (i in 0..8) {
                if (pos >= bytes.size) throw IOException("Cabecera RAR5 incompleta.")
                val n = bytes[pos++].toInt() and 255; v = v or ((n and 127).toLong() shl (7 * i))
                if (n and 128 == 0) return v
            }
            throw IOException("Tamaño RAR5 fuera de rango.")
        }
    }
    private fun rar5(h: Headers) {
        var pos = 8L
        repeat(MAX_ENTRIES + 16) {
            // Read the size vint byte by byte, including on very short final headers.
            val sizeBytes = ArrayList<Byte>()
            for (i in 0..8) { val n = h.read(pos + 4 + i, 1)[0]; sizeBytes += n; if (n >= 0) break }
            val size = Vints(sizeBytes.toByteArray()).next()
            if (size > MAX_HEADER || size < 2) throw IOException("Cabecera RAR5 demasiado grande o no válida.")
            val b = h.read(pos, 4 + sizeBytes.size + size.toInt())
            if (crc(b, 4) != u32(b, 0)) throw IOException("Cabecera RAR5 dañada (CRC).")
            val v = Vints(b, 4 + sizeBytes.size); val type = v.next(); val flags = v.next()
            if (flags and 1 != 0L) v.next() // extra area size
            val packed = if (flags and 2 != 0L) v.next() else 0L
            if (type == 4L || flags and 0x18 != 0L || (type == 1L && v.next() and 1 != 0L))
                throw IOException("RAR dividido o con contraseña: usa tu gestor de archivos.")
            pos = sum(sum(pos, b.size.toLong()), packed)
            if (pos > h.size) throw ShortArchive(pos)
            if (type == 5L) { if (v.next() and 1 != 0L) throw IOException("Faltan volúmenes del RAR."); return }
            if (pos == h.size) throw ShortArchive()
        }
        throw IOException("El RAR tiene demasiadas entradas.")
    }
}
