package com.nexo.launcher.core

internal object ArtworkPolicy {
    fun accepts(mime: String?, width: Int, height: Int, background: Boolean): Boolean =
        mime in setOf("image/png", "image/jpeg", "image/webp") && width in 1..16384 && height in 1..16384 &&
            width.toLong() * height <= 64000000 && (!background || width >= 1280 && height >= 720)
    /** Se busca en línea solo si falta la portada, la opción está activada y no hay un fallo reciente recordado. */
    fun shouldDownload(enabled: Boolean, hasCover: Boolean, now: Long, retryUntil: Long): Boolean = enabled && !hasCover && now >= retryUntil
    fun retryAt(now: Long, onlyNotFound: Boolean): Long = now + if (onlyNotFound) 7L * 86400000 else 1800000L
}
