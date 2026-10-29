package com.nexo.launcher.core

import org.apache.commons.compress.archivers.sevenz.SevenZArchiveEntry
import org.apache.commons.compress.archivers.sevenz.SevenZOutputFile
import org.apache.commons.compress.archivers.zip.ZipArchiveEntry
import org.apache.commons.compress.archivers.zip.ZipArchiveOutputStream
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.file.Files
import java.util.concurrent.CancellationException
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

/** Actual decoder tests, no network, no emulator or Android runtime. Fixtures are generated here. */
object ArchiveChecks {
    private var passed = 0
    private fun test(name: String, body: () -> Unit) {
        try { body(); passed++; println("OK archive: $name") }
        catch (e: Throwable) { throw AssertionError("FAILED archive: $name", e) }
    }
    private fun rejects(body: () -> Unit): Exception {
        try { body() } catch (e: Exception) { return e }
        error("Expected rejection")
    }
    @JvmStatic fun main(args: Array<String>) {
        val parent = File("build/archive-tests").apply { mkdirs() }
        val root = Files.createTempDirectory(parent.toPath(), "check-").toFile()
        try {
            val data = ByteArray(220000) { (it * 31 + it / 997).toByte() }
            val zip = File(root, "PSP.zip").apply { writeBytes(zip(mapOf("game/game.iso" to data, "game/LEEME.txt" to "hola".toByteArray()))) }
            val seven = File(root, "PS3.7z")
            SevenZOutputFile(seven).use { out ->
                for ((name, bytes) in mapOf("PS3_GAME/USRDIR/EBOOT.BIN" to data, "PS3_GAME/PARAM.SFO" to byteArrayOf(0, 0x50, 0x53, 0x46))) {
                    val entry = SevenZArchiveEntry().apply { this.name = name; size = bytes.size.toLong() }
                    out.putArchiveEntry(entry); out.write(bytes); out.closeArchiveEntry()
                }
            }
            test("valid ZIP ready, extraction and byte progress") {
                check(health(zip).ready)
                val target = DiskTarget(File(root, "zip-out")); var last = 0L; var total = 0L
                unpack(zip, target, progress = { n, t -> check(n >= last && n <= t); last = n; total = t })
                check(File(target.final, "game/game.iso").readBytes().contentEquals(data))
                check(last == data.size + 4L && last == total && zip.exists())
            }
            test("valid 7z ready and PS3 layout preserved") {
                check(health(seven).ready)
                val target = DiskTarget(File(root, "7z-out")); unpack(seven, target)
                check(File(target.final, "PS3_GAME/USRDIR/EBOOT.BIN").readBytes().contentEquals(data)); check(seven.exists())
            }
            test("7z truncated reports expected final size") {
                val bytes = seven.readBytes(); val truncated = File(root, "short.7z").apply { writeBytes(bytes.copyOf(bytes.size - 8)) }
                val h = health(truncated); check(!h.ready && h.expectedSize == seven.length() && h.message.startsWith("Descarga incompleta"))
            }
            test("7z bad start CRC rejected") {
                val file = File(root, "crc.7z").apply { writeBytes(seven.readBytes().apply { this[12] = (this[12].toInt() xor 1).toByte() }) }
                check(!health(file).ready)
            }
            test("7z bad end header CRC rejected") {
                val file = File(root, "end-crc.7z").apply { writeBytes(seven.readBytes().apply { this[lastIndex] = (this[lastIndex].toInt() xor 1).toByte() }) }
                check(!health(file).ready)
            }
            test("ZIP missing EOCD incomplete") {
                val file = File(root, "short.zip").apply { writeBytes(zip.readBytes().dropLast(22).toByteArray()) }
                check(!health(file).ready && health(file).message.startsWith("Descarga incompleta"))
            }
            test("ZIP central directory sizes inconsistent") {
                val file = File(root, "size.zip").apply { writeBytes(zip.readBytes().apply { le(this).putInt(size - 22 + 12, Int.MAX_VALUE) }) }
                check(!health(file).ready)
            }
            test("ZIP entry compressed size outside file") {
                val bytes = zip.readBytes(); val c = index(bytes, 0x02014b50)
                le(bytes).putInt(c + 20, Int.MAX_VALUE)
                val file = File(root, "data-short.zip").apply { writeBytes(bytes) }
                check(!health(file).ready && health(file).message.startsWith("Descarga incompleta"))
            }
            test("ZIP64 small fixture accepted") {
                val file = File(root, "zip64.zip")
                ZipArchiveOutputStream(file).use { out ->
                    out.setUseZip64(org.apache.commons.compress.archivers.zip.Zip64Mode.Always)
                    out.putArchiveEntry(ZipArchiveEntry("test.iso")); out.write(data); out.closeArchiveEntry()
                }
                check(health(file).ready) { health(file).message }
                unpack(file, DiskTarget(File(root, "zip64-out")))
            }
            test("ZIP64 entry larger than 4 GiB without allocating it") {
                virtualZip64(0, 0x100000010L).use { check(ArchiveValidation.inspect(it, "zip").ready) }
            }
            test("ZIP64 local-header offset larger than 4 GiB") {
                virtualZip64(0x100000010L, 20).use { check(ArchiveValidation.inspect(it, "zip").ready) }
            }
            test("ZIP traversal rejected before any output") {
                val file = File(root, "traversal.zip").apply { writeBytes(zip(mapOf("../escape.iso" to data))) }
                val target = DiskTarget(File(root, "traversal-out"))
                rejects { unpack(file, target) }; check(!target.begun && !File(root, "escape.iso").exists() && file.exists())
            }
            test("7z traversal rejected before any output") {
                val file = File(root, "traversal.7z")
                SevenZOutputFile(file).use { out -> out.putArchiveEntry(SevenZArchiveEntry().apply { name = "../escape.iso"; size = 1 }); out.write(byteArrayOf(1)); out.closeArchiveEntry() }
                val target = DiskTarget(File(root, "7z-traversal-out")); rejects { unpack(file, target) }; check(!target.begun)
            }
            test("ZIP symlink rejected") {
                val file = File(root, "link.zip")
                ZipArchiveOutputStream(file).use { out ->
                    out.putArchiveEntry(ZipArchiveEntry("escape").apply { unixMode = 0xa1ff }); out.write("../outside".toByteArray()); out.closeArchiveEntry()
                }
                val target = DiskTarget(File(root, "link-out")); rejects { unpack(file, target) }; check(!target.begun)
            }
            test("unsafe paths rejected") {
                listOf("/game.iso", "C:\\game.iso", "\\\\host\\game.iso", "a/../b", "a\\..\\b", "a//b", "a/./b", "a\u0000b", "a:stream", "..", "a./b").forEach { rejects { ArchivePaths.parts(it) } }
            }
            test("safe Unicode and backslash directory names") { check(ArchivePaths.parts("Juegos\\Pokémon.iso") == listOf("Juegos", "Pokémon.iso")) }
            test("cancellation during ZIP deletes staging and keeps source") {
                val target = DiskTarget(File(root, "cancel-out")); var cancel = false
                val e = rejects { unpack(zip, target, check = { if (cancel) throw CancellationException() }, progress = { n, _ -> if (n > 0) cancel = true }) }
                check(e is CancellationException && target.rolledBack && !target.staging.exists() && !target.final.exists() && zip.exists())
            }
            test("cancellation during 7z deletes staging and keeps source") {
                val target = DiskTarget(File(root, "cancel7-out")); var cancel = false
                val e = rejects { unpack(seven, target, check = { if (cancel) throw CancellationException() }, progress = { n, _ -> if (n > 0) cancel = true }) }
                check(e is CancellationException && target.rolledBack && !target.staging.exists() && seven.exists())
            }
            test("cancellation during validation propagates") {
                val e = rejects { zip.inputStream().channel.use { ArchiveValidation.inspect(it, "zip") { throw CancellationException() } } }; check(e is CancellationException)
            }
            test("insufficient space never creates staging") {
                val target = DiskTarget(File(root, "space-out"), 100)
                rejects { unpack(zip, target) }; check(!target.begun)
            }
            test("unknown available space never creates staging") {
                val target = DiskTarget(File(root, "unknown-space"), -1)
                rejects { unpack(zip, target) }; check(!target.begun)
            }
            test("partial IO failure cleans staging") {
                val target = DiskTarget(File(root, "io-out"), failWrite = true)
                rejects { unpack(zip, target) }; check(target.rolledBack && !target.staging.exists() && zip.exists())
            }
            test("rename failure cleans staging") {
                val target = DiskTarget(File(root, "rename-out"), failCommit = true)
                rejects { unpack(zip, target) }; check(target.rolledBack && !target.staging.exists() && zip.exists())
            }
            test("duplicate output folder uses (2)") {
                check(ArchivePaths.unique("PSP", listOf("PSP")) == "PSP (2)")
                check(ArchivePaths.unique("PSP", listOf("psp", "PSP (2)")) == "PSP (3)")
            }
            test("duplicate archive file paths rejected") {
                val target = DiskTarget(File(root, "dupe-out"))
                rejects { ArchiveEngine.extract(fake(listOf(PackedEntry("a", false, 0), PackedEntry("A", false, 0))), target, {}, { _, _ -> }) }
                check(!target.begun)
            }
            test("file-directory conflicts rejected") {
                val target = DiskTarget(File(root, "conflict-out"))
                rejects { ArchiveEngine.extract(fake(listOf(PackedEntry("a", false, 0), PackedEntry("a/b", false, 0))), target, {}, { _, _ -> }) }; check(!target.begun)
            }
            test("expansion ratio bomb rejected") {
                val target = DiskTarget(File(root, "ratio-out"))
                rejects { ArchiveEngine.extract(fake(listOf(PackedEntry("a", false, 1001000))), target, {}, { _, _ -> }) }; check(!target.begun)
            }
            test("size ceiling rejected") {
                val target = DiskTarget(File(root, "limit-out"))
                rejects { ArchiveEngine.extract(fake(listOf(PackedEntry("a", false, ArchiveValidation.MAX_TOTAL + 1))), target, {}, { _, _ -> }) }; check(!target.begun)
            }
            test("decoder writing more than declared is stopped") {
                val target = DiskTarget(File(root, "overrun-out"))
                rejects { ArchiveEngine.extract(fake(listOf(PackedEntry("a", false, 1)), byteArrayOf(1, 2)), target, {}, { _, _ -> }) }
                check(target.rolledBack && !target.staging.exists())
            }
            test("decoder writing less than declared fails") {
                val target = DiskTarget(File(root, "underrun-out"))
                rejects { ArchiveEngine.extract(fake(listOf(PackedEntry("a", false, 2)), byteArrayOf(1)), target, {}, { _, _ -> }) }; check(target.rolledBack)
            }
            for (version in listOf(4, 5)) {
                val file = File(root, "rar$version.rar").apply { writeBytes(if (version == 4) rar4("game.iso", data) else rar5("game.iso", data)) }
                test("RAR$version ready and stored extraction") {
                    check(health(file).ready) { health(file).message }
                    val target = DiskTarget(File(root, "rar$version-out")); unpack(file, target)
                    check(File(target.final, "game.iso").readBytes().contentEquals(data)); check(file.exists())
                }
                test("RAR$version truncated data") {
                    val truncated = File(root, "rar$version-short.rar").apply { writeBytes(file.readBytes().copyOf(200)) }
                    val h = health(truncated); check(!h.ready && h.expectedSize!! > truncated.length() && h.message.startsWith("Descarga incompleta"))
                }
                test("RAR$version missing final block") {
                    val bytes = file.readBytes(); val short = File(root, "rar$version-end.rar").apply { writeBytes(bytes.copyOf(bytes.size - 3)) }
                    check(!health(short).ready)
                }
                test("RAR$version traversal rejected") {
                    val bad = File(root, "rar$version-traversal.rar").apply { writeBytes(if (version == 4) rar4("../x", data) else rar5("../x", data)) }
                    val target = DiskTarget(File(root, "rar$version-bad")); rejects { unpack(bad, target) }; check(!target.begun)
                }
            }
            println("Archive checks: $passed/$passed OK")
        } finally { root.deleteRecursively() }
    }
    private fun health(file: File) = file.inputStream().channel.use { ArchiveValidation.inspect(it, file.extension) }
    private fun unpack(file: File, target: DiskTarget, check: () -> Unit = {}, progress: (Long, Long) -> Unit = { _, _ -> }) =
        file.inputStream().channel.use { channel -> ArchiveReaders.open(channel, file.extension, check).use { ArchiveEngine.extract(it, target, check, progress) } }
    private class DiskTarget(val final: File, private val free: Long = Long.MAX_VALUE, private val failWrite: Boolean = false, private val failCommit: Boolean = false) : ArchiveDestination<File> {
        val staging = File(final.parentFile, ".nexo-extract-${final.name}")
        var begun = false; var rolledBack = false
        override fun availableBytes() = free
        override fun begin() { begun = true; check(staging.mkdir()) }
        override fun directory(parts: List<String>) { File(staging, parts.joinToString("/")).mkdirs() }
        override fun file(parts: List<String>): OutputStream {
            val file = File(staging, parts.joinToString("/")); file.parentFile!!.mkdirs()
            val out = file.outputStream()
            return if (!failWrite) out else object : OutputStream() { override fun write(b: Int) { throw IOException("Disco lleno") }; override fun close() = out.close() }
        }
        override fun commit(): File { if (failCommit) throw IOException("Rename failed"); check(staging.renameTo(final)); return final }
        override fun rollback() { rolledBack = true; check(staging.deleteRecursively()) }
    }
    private fun fake(list: List<PackedEntry>, data: ByteArray = byteArrayOf()) = object : ArchiveReader {
        override val entries = list; override val compressedSize = 1000L
        override fun write(index: Int, output: OutputStream) = output.write(data)
        override fun close() { }
    }
    private fun zip(files: Map<String, ByteArray>): ByteArray = ByteArrayOutputStream().also { bytes ->
        ZipOutputStream(bytes).use { out -> files.forEach { (name, data) -> out.putNextEntry(ZipEntry(name)); out.write(data); out.closeEntry() } }
    }.toByteArray()
    private fun le(bytes: ByteArray) = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
    private fun index(bytes: ByteArray, magic: Int) = (0..bytes.size - 4).first { le(bytes).getInt(it) == magic }
    private fun crc(bytes: ByteArray) = CRC32().apply { update(bytes) }.value
    private fun rar4(name: String, data: ByteArray): ByteArray {
        fun header(type: Int, flags: Int, size: Int, init: (ByteArray) -> Unit = {}): ByteArray {
            val b = ByteArray(size); b[2] = type.toByte(); le(b).putShort(3, flags.toShort()).putShort(5, size.toShort()); init(b)
            le(b).putShort(0, crc(b.copyOfRange(2, b.size)).toShort()); return b
        }
        val filename = name.toByteArray()
        return byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 7, 0) + header(0x73, 0, 13) +
            header(0x74, 0x8000, 32 + filename.size) { b ->
                le(b).putInt(7, data.size).putInt(11, data.size).putInt(16, crc(data).toInt()).putShort(26, filename.size.toShort()).putInt(28, 0x20)
                b[15] = 2; b[24] = 20; b[25] = 0x30; filename.copyInto(b, 32)
            } + data + header(0x7b, 0x4000, 7) // Standard RAR4 end marker (CRC 0x3dc4).
    }
    private fun vint(value: Long): ByteArray {
        var n = value; val out = ByteArrayOutputStream()
        do { val b = (n and 127).toInt(); n = n ushr 7; out.write(b or if (n > 0) 128 else 0) } while (n > 0)
        return out.toByteArray()
    }
    private fun rar5(name: String, data: ByteArray): ByteArray {
        fun block(body: ByteArray): ByteArray { val b = vint(body.size.toLong()) + body; return ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(crc(b).toInt()).array() + b }
        val filename = name.toByteArray()
        val file = byteArrayOf(2, 2) + vint(data.size.toLong()) + byteArrayOf(4) + vint(data.size.toLong()) + byteArrayOf(0x20) +
            ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putInt(crc(data).toInt()).array() + byteArrayOf(0, 0) + vint(filename.size.toLong()) + filename
        return byteArrayOf(0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1, 0) + block(byteArrayOf(1, 0, 0)) + block(file) + data + block(byteArrayOf(5, 0, 0))
    }
    /** Sparse in-memory channel: exercises 64-bit fields without writing a multi-GB fixture. */
    private fun virtualZip64(localOffset: Long, packed: Long): java.nio.channels.SeekableByteChannel {
        val local = ByteArray(51)
        le(local).putInt(0, 0x04034b50).putShort(4, 45).putInt(18, -1).putInt(22, -1).putShort(26, 1).putShort(28, 20)
        local[30] = 'g'.code.toByte(); le(local).putShort(31, 1).putShort(33, 16).putLong(35, packed).putLong(43, packed)
        val central = ByteArray(75)
        le(central).putInt(0, 0x02014b50).putShort(4, 45).putShort(6, 45).putInt(20, -1).putInt(24, -1)
            .putShort(28, 1).putShort(30, 28).putInt(42, -1)
        central[46] = 'g'.code.toByte(); le(central).putShort(47, 1).putShort(49, 24).putLong(51, packed).putLong(59, packed).putLong(67, localOffset)
        val cd = localOffset + local.size + packed; val zpos = cd + central.size
        val z = ByteArray(56); le(z).putInt(0, 0x06064b50).putLong(4, 44).putShort(12, 45).putShort(14, 45)
            .putLong(24, 1).putLong(32, 1).putLong(40, central.size.toLong()).putLong(48, cd)
        val locator = ByteArray(20); le(locator).putInt(0, 0x07064b50).putLong(8, zpos).putInt(16, 1)
        val end = ByteArray(22); le(end).putInt(0, 0x06054b50).putShort(8, -1).putShort(10, -1).putInt(12, -1).putInt(16, -1)
        val segments = listOf(localOffset to local, cd to central, zpos to z, zpos + 56 to locator, zpos + 76 to end)
        return object : java.nio.channels.SeekableByteChannel {
            private var pos = 0L; private var open = true
            override fun size() = zpos + 98
            override fun position() = pos
            override fun position(value: Long): java.nio.channels.SeekableByteChannel { pos = value; return this }
            override fun isOpen() = open
            override fun close() { open = false }
            override fun read(dst: ByteBuffer): Int {
                if (pos >= size()) return -1
                val b = ByteArray(minOf(dst.remaining().toLong(), size() - pos).toInt())
                for ((offset, bytes) in segments) {
                    val first = maxOf(pos, offset); val last = minOf(pos + b.size, offset + bytes.size)
                    if (first < last) bytes.copyInto(b, (first - pos).toInt(), (first - offset).toInt(), (last - offset).toInt())
                }
                dst.put(b); pos += b.size; return b.size
            }
            override fun write(src: ByteBuffer): Int = error("read only")
            override fun truncate(size: Long): java.nio.channels.SeekableByteChannel = error("read only")
        }
    }
}
