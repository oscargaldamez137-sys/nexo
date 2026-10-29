package com.nexo.launcher.core

import android.content.Context
import android.net.Uri
import android.os.ParcelFileDescriptor
import java.io.Closeable
import java.io.EOFException
import java.io.IOException
import java.nio.ByteBuffer

internal class SeekDocument(context: Context, uri: Uri) : BinarySource, Closeable {
    private val stream = ParcelFileDescriptor.AutoCloseInputStream(context.contentResolver.openFileDescriptor(uri, "r") ?: throw IOException("Documento no disponible."))
    override fun read(offset: Long, count: Int): ByteArray {
        require(offset >= 0 && count in 0..(16 * 1024 * 1024) && offset <= Long.MAX_VALUE - count)
        val buffer = ByteBuffer.allocate(count)
        while (buffer.hasRemaining()) {
            val got = stream.channel.read(buffer, offset + buffer.position())
            if (got <= 0) throw EOFException("Cabecera incompleta o documento sin acceso aleatorio.")
        }
        return buffer.array()
    }
    override fun close() = stream.close()
}
