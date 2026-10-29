package com.nexo.launcher.core

import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import java.io.ByteArrayOutputStream
import java.io.IOException
import java.io.OutputStream
import java.net.URI
import javax.net.ssl.HttpsURLConnection

internal class HttpFailure(val status: Int) : IOException("El servidor respondió HTTP $status.")
internal object OfficialHttp {
    val apkHosts = setOf("api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com",
        "buildbot.libretro.com", "www.ppsspp.org", "ppsspp.org", "dolphin-emu.org", "dl.dolphin-emu.org", "git.eden-emu.dev", "stable.eden-emu.dev")
    val artHosts = setOf("raw.githubusercontent.com", "art.gametdb.com")
    fun validate(url: String, hosts: Set<String>): URI {
        val uri = URI(url)
        require(uri.scheme == "https" && uri.host?.lowercase() in hosts && uri.userInfo == null && uri.port in listOf(-1, 443) && uri.fragment == null) {
            "La descarga no pertenece a una fuente HTTPS permitida."
        }
        return uri
    }
    suspend fun bytes(url: String, hosts: Set<String>, max: Int): ByteArray {
        val out = ByteArrayOutputStream()
        download(url, hosts, max.toLong(), out)
        return out.toByteArray()
    }
    suspend fun download(url: String, hosts: Set<String>, limit: Long, out: OutputStream, progress: (Long, Long) -> Unit = { _, _ -> }) {
        var uri = validate(url, hosts)
        val deadline = System.nanoTime() + 15L * 60 * 1000000000
        repeat(6) { hop ->
            currentCoroutineContext().ensureActive()
            val connection = uri.toURL().openConnection() as HttpsURLConnection
            connection.instanceFollowRedirects = false
            connection.connectTimeout = 15000; connection.readTimeout = 20000
            connection.setRequestProperty("User-Agent", "Nexo-Android/1.0")
            connection.setRequestProperty("Accept-Encoding", "identity")
            try {
                val status = connection.responseCode
                if (status in listOf(301, 302, 303, 307, 308)) {
                    if (hop == 5) throw IOException("Demasiadas redirecciones.")
                    uri = validate(uri.resolve(connection.getHeaderField("Location") ?: throw IOException("Redirección sin destino.")).toString(), hosts)
                    return@repeat
                }
                if (status != 200) throw HttpFailure(status)
                val size = connection.contentLengthLong
                if (size > limit) throw IOException("La descarga supera el tamaño permitido.")
                connection.inputStream.use { input ->
                    val buffer = ByteArray(65536); var written = 0L
                    while (true) {
                        currentCoroutineContext().ensureActive()
                        if (System.nanoTime() > deadline) throw IOException("La descarga agotó el tiempo disponible.")
                        val count = input.read(buffer); if (count < 0) break
                        written += count
                        if (written > limit) throw IOException("La descarga supera el tamaño permitido.")
                        out.write(buffer, 0, count); progress(written, size)
                    }
                    if (size >= 0 && written != size) throw IOException("Descarga incompleta; vuelve a intentarlo.")
                }
                return
            } finally { connection.disconnect() }
        }
    }
}
