package com.nexo.launcher.core

import java.io.File
import java.io.IOException
import java.io.OutputStream
import java.security.MessageDigest

/** Official EmuEx bundles contain one APK. Never trust its path as an output path. */
internal object ApkBundles {
    const val EMUEX_REPO = "Rakashazi/emu-ex-plus-alpha"
    const val RELEASE_URL = "https://api.github.com/repos/$EMUEX_REPO/releases/tags/Pre-release"
    fun asset(id: String): String = when (id) { "GBC" -> "GbcEmu.zip"; "GBA" -> "GbaEmu.zip"; else -> error("Consola no válida.") }
    fun packageName(id: String) = when (id) { "GBC" -> "com.explusalpha.GbcEmu"; "GBA" -> "com.explusalpha.GbaEmu"; else -> error("Consola no válida.") }
    fun verify(file: File, expected: String, check: () -> Unit) {
        require(Regex("[a-fA-F0-9]{64}").matches(expected)) { "Falta un SHA256 válido en la publicación oficial." }
        val hash = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val b = ByteArray(65536)
            while (true) { check(); val n = input.read(b); if (n < 0) break; hash.update(b, 0, n) }
        }
        val actual = hash.digest().joinToString("") { "%02x".format(it.toInt() and 255) }
        if (!actual.equals(expected, true)) throw IOException("El SHA256 de la descarga no coincide con la publicación oficial.")
    }
    fun extract(file: File, apk: File, check: () -> Unit) {
        try {
            file.inputStream().channel.use { channel -> ArchiveReaders.open(channel, "zip", check).use { reader ->
                if (reader.entries.size > 100) throw IOException("El paquete oficial contiene demasiadas entradas.")
                reader.entries.forEach { if (it.link) throw IOException("El paquete contiene enlaces."); ArchivePaths.parts(it.name) }
                val index = reader.entries.indices.singleOrNull { !reader.entries[it].directory && reader.entries[it].name.endsWith(".apk", true) }
                    ?: throw IOException("La descarga debe contener un único APK.")
                val entry = reader.entries[index]
                if (entry.size !in 1..(256L * 1024 * 1024)) throw IOException("El APK supera el tamaño permitido.")
                var size = 0L
                apk.outputStream().use { output ->
                    reader.write(index, object : OutputStream() {
                        override fun write(b: Int) = write(byteArrayOf(b.toByte()), 0, 1)
                        override fun write(b: ByteArray, off: Int, len: Int) {
                            check(); if (size > entry.size - len) throw IOException("El APK excede su tamaño declarado.")
                            output.write(b, off, len); size += len
                        }
                    })
                }
                if (size != entry.size) throw IOException("APK incompleto.")
            } }
        } catch (e: Throwable) { apk.delete(); throw e }
    }
}
