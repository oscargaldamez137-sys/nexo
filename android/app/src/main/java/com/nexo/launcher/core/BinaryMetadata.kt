package com.nexo.launcher.core

import java.io.IOException
import java.nio.ByteBuffer
import java.nio.ByteOrder

internal interface BinarySource { fun read(offset: Long, count: Int): ByteArray }
internal object BinaryMetadata {
    private fun u32(b: ByteArray, at: Int) = ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).getInt(at).toLong() and 0xffffffffL
    private fun u16(b: ByteArray, at: Int) = ByteBuffer.wrap(b).order(ByteOrder.LITTLE_ENDIAN).getShort(at).toInt() and 65535
    fun disc(source: BinarySource, ext: String): GameFormats.Disc? {
        val start = source.read(0, 32)
        val offset = GameFormats.discOffset(ext, start) ?: return null
        return GameFormats.discHeader(if (offset == 0L) start else source.read(offset, 32))
    }
    fun sfo(bytes: ByteArray, key: String): String? {
        if (bytes.size < 20 || !bytes.copyOfRange(0, 4).contentEquals(byteArrayOf(0, 0x50, 0x53, 0x46))) return null
        val keys = u32(bytes, 8); val data = u32(bytes, 12); val count = u32(bytes, 16)
        if (count > 4096 || 20 + count * 16 > bytes.size || keys > bytes.size || data > bytes.size) return null
        for (i in 0 until count.toInt()) {
            val at = 20 + i * 16; val name = keys + u16(bytes, at)
            if (name >= bytes.size) return null
            val end = (name.toInt() until minOf(bytes.size, name.toInt() + 1024)).firstOrNull { bytes[it] == 0.toByte() } ?: return null
            if (String(bytes, name.toInt(), end - name.toInt(), Charsets.UTF_8) != key) continue
            val pos = data + u32(bytes, at + 12); val len = u32(bytes, at + 4)
            if (u16(bytes, at + 2) != 0x204 || len == 0L || pos > bytes.size || len > bytes.size - pos) return null
            return String(bytes, pos.toInt(), len.toInt(), Charsets.UTF_8).trimEnd('\u0000').take(256)
        }
        return null
    }
    /** Bounded ISO9660 primary-volume reader. No extraction, decryption, Joliet or CSO inflation. */
    fun isoFile(source: BinarySource, path: String, limit: Int): ByteArray? {
        val pvd = source.read(0x8000, 2048)
        if (pvd[0] != 1.toByte() || String(pvd, 1, 5, Charsets.US_ASCII) != "CD001" || u16(pvd, 128) != 2048) return null
        var extent = u32(pvd, 158); var length = u32(pvd, 166)
        val parts = path.split('/')
        for ((index, part) in parts.withIndex()) {
            if (length !in 1..(2L * 1024 * 1024)) return null
            val directory = source.read(extent * 2048, length.toInt())
            var offset = 0; var found = false
            while (offset < directory.size) {
                val record = directory[offset].toInt() and 255
                if (record == 0) { offset = ((offset / 2048) + 1) * 2048; continue }
                if (record < 34 || offset + record > directory.size || offset % 2048 + record > 2048) return null
                val nameLen = directory[offset + 32].toInt() and 255
                if (33 + nameLen > record) return null
                val name = String(directory, offset + 33, nameLen, Charsets.US_ASCII).substringBefore(';').trimEnd('.')
                if (name.equals(part, true)) {
                    val flags = directory[offset + 25].toInt() and 255
                    if (flags and 0x80 != 0 || directory[offset + 26] != 0.toByte() || directory[offset + 27] != 0.toByte()) return null
                    extent = u32(directory, offset + 2) + (directory[offset + 1].toInt() and 255)
                    length = u32(directory, offset + 10)
                    if (index == parts.lastIndex) {
                        if (flags and 2 != 0 || length !in 1..limit.toLong()) return null
                        return source.read(extent * 2048, length.toInt())
                    }
                    if (flags and 2 == 0) return null
                    found = true; break
                }
                offset += record
            }
            if (!found) return null
        }
        return null
    }
}
