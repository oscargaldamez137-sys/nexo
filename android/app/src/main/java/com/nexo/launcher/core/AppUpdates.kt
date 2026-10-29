package com.nexo.launcher.core

import java.io.File
import java.io.IOException

/** Una versión de Nexo más nueva publicada en GitHub, con su APK. */
data class AppUpdate(val version: String, val url: String, val size: Long, val sha256: String?)

/** Lo que interesa de una publicación de GitHub (releases/latest), ya leído del JSON. */
internal data class ReleaseInfo(val tag: String?, val draft: Boolean, val prerelease: Boolean, val assets: List<ReleaseAsset>)
internal data class ReleaseAsset(val name: String, val url: String, val size: Long, val digest: String?)

/**
 * Lógica pura de la actualización automática (sin Android ni red, probada en AppUpdateChecks).
 * La misma publicación que Windows: etiqueta vX.Y.Z con Instalar-Nexo.exe y Nexo.apk.
 */
internal object AppUpdates {
    const val REPO = "oscargaldamez137-sys/nexo"
    const val RELEASE_URL = "https://api.github.com/repos/$REPO/releases/latest"
    const val RELEASES_PAGE = "https://github.com/$REPO/releases/latest"
    const val ASSET = "Nexo.apk"
    private const val DOWNLOAD_PREFIX = "https://github.com/$REPO/releases/download/"
    const val MAX_APK = 256L * 1024 * 1024

    /** "v1.2.3", "V1.2.3", "1.2.3" o "1.2" → [major, minor, patch]; cualquier otra cosa → null. */
    fun parse(tag: String?): IntArray? {
        val t = tag?.trim()?.removePrefix("v")?.removePrefix("V") ?: return null
        if (!Regex("\\d{1,4}\\.\\d{1,2}(\\.\\d{1,2})?").matches(t)) return null
        val parts = t.split('.').map { it.toInt() }
        if (parts[1] > 99 || parts.getOrElse(2) { 0 } > 99) return null
        return intArrayOf(parts[0], parts[1], parts.getOrElse(2) { 0 })
    }

    /** Regla de build.gradle.kts: major*10000 + minor*100 + patch (minor y patch hasta 99). */
    fun versionCode(version: IntArray): Int = version[0] * 10000 + version[1] * 100 + version[2]

    fun newer(latest: IntArray, current: IntArray): Boolean = versionCode(latest) > versionCode(current)

    /** La actualización que describe la publicación, o null si no es más nueva, le falta Nexo.apk o no es fiable. */
    fun select(release: ReleaseInfo, currentVersion: String): AppUpdate? {
        if (release.draft || release.prerelease) return null
        val latest = parse(release.tag) ?: return null
        val current = parse(currentVersion) ?: return null
        if (!newer(latest, current)) return null
        val asset = release.assets.firstOrNull { it.name == ASSET } ?: return null
        if (!asset.url.startsWith(DOWNLOAD_PREFIX) || !asset.url.endsWith("/$ASSET")) return null
        runCatching { OfficialHttp.validate(asset.url, OfficialHttp.apkHosts) }.getOrNull() ?: return null
        if (asset.size !in 1..MAX_APK) return null
        val digest = asset.digest?.takeUnless { it.isBlank() || it == "null" }
        // Una huella que Nexo no sabe comprobar no se ignora: no se instala.
        if (digest != null && !Regex("sha256:[0-9a-fA-F]{64}").matches(digest)) return null
        return AppUpdate(latest.joinToString("."), asset.url, asset.size, digest?.substringAfter(':')?.lowercase())
    }

    /** Consulta la publicación; sin red, sin versión nueva o ante cualquier error devuelve null, sin avisos. */
    suspend fun check(currentVersion: String, fetch: suspend () -> ReleaseInfo): AppUpdate? =
        try { select(fetch(), currentVersion) }
        catch (e: kotlinx.coroutines.CancellationException) { throw e }
        catch (_: Exception) { null }

    /** Tamaño exacto y, si GitHub la publica, huella SHA-256. */
    fun verify(file: File, size: Long, sha256: String?, check: () -> Unit = {}) {
        if (file.length() != size) throw IOException("Descarga incompleta (${file.length()} de $size bytes). No se instalará.")
        if (sha256 != null) ApkBundles.verify(file, sha256, check)
    }
}
