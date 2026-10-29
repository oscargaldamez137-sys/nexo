package com.nexo.launcher.core

import java.util.Locale

/** Almacén clave-valor de las opciones (SharedPreferences «nexo» en Android; un mapa en las pruebas JVM). */
interface SettingsStore {
    fun getBoolean(key: String, default: Boolean): Boolean
    fun putBoolean(key: String, value: Boolean): Boolean
}

/**
 * Opciones de la pantalla de Opciones. Por defecto todo se comporta como en 1.0.0:
 * se buscan actualizaciones al abrir, se descargan portadas y se muestran los avisos.
 */
class NexoSettings(private val store: SettingsStore) {
    var checkUpdates: Boolean
        get() = store.getBoolean(CHECK_UPDATES, true)
        set(value) { check(store.putBoolean(CHECK_UPDATES, value)) { "No se pudo guardar la opción." } }
    /** Misma clave que ya leía AndroidArtwork en 1.0.0. */
    var downloadArtwork: Boolean
        get() = store.getBoolean(DOWNLOAD_ARTWORK, true)
        set(value) { check(store.putBoolean(DOWNLOAD_ARTWORK, value)) { "No se pudo guardar la opción." } }
    /** «No volver a mostrar» el aviso de claves de Switch que Nexo no puede ver. */
    var hideSwitchKeysNotice: Boolean
        get() = store.getBoolean(HIDE_SWITCH_KEYS, false)
        set(value) { check(store.putBoolean(HIDE_SWITCH_KEYS, value)) { "No se pudo guardar la opción." } }

    companion object {
        const val CHECK_UPDATES = "checkUpdates"
        const val DOWNLOAD_ARTWORK = "downloadArtwork"
        const val HIDE_SWITCH_KEYS = "hideSwitchKeysNotice"
    }
}

/** Dónde busca Nexo los juegos dentro de la biblioteca elegida. Lógica pura, probada en LibraryChecks. */
internal object LibraryLayout {
    const val GAMES = "Juegos"
    /** Nombres de carpeta que también se aceptan para una consola (además de su ID, sin distinguir mayúsculas). */
    private val aliases = mapOf(
        "Switch" to listOf("Nintendo Switch", "NSW"),
        "Xbox360" to listOf("Xbox 360"),
    )

    /**
     * La carpeta de juegos es «Juegos» dentro de la biblioteca (como siempre). Si no existe todavía y el usuario eligió
     * directamente su carpeta «Juegos», o una carpeta que ya tiene carpetas de consola (por ejemplo «Switch»), se usa
     * esa misma carpeta en vez de crear otra «Juegos» dentro donde nunca encontraría sus juegos.
     */
    fun useRootAsGames(rootName: String?, children: List<Pair<String, Boolean>>, systemIds: List<String>): Boolean {
        if (children.any { it.second && it.first.equals(GAMES, true) }) return false
        if (rootName != null && rootName.trim().equals(GAMES, true)) return true
        return systemIds.any { id -> systemFolder(id, children) != null }
    }

    /** Nombre de la carpeta existente de esa consola (su ID o un alias), o null si hay que crearla con el ID. */
    fun systemFolder(id: String, children: List<Pair<String, Boolean>>): String? {
        val names = listOf(id) + aliases[id].orEmpty()
        for (name in names) children.firstOrNull { it.second && it.first.trim().equals(name, true) }?.let { return it.first }
        return null
    }
}

/** Avisos de juegos y archivos del sistema que nunca ocultan juegos ni impiden abrirlos. */
internal object SwitchNotice {
    /** Formatos de Switch que el emulador no abre: se muestran como pendientes con su explicación, nunca se esconden. */
    fun unsupported(systemId: String, fileName: String): String? =
        if (systemId == "Switch" && GameFormats.extension(fileName) in setOf("nsz", "xcz"))
            "${GameFormats.extension(fileName).uppercase(Locale.ROOT)} comprimido: Eden no lo abre directamente. Descomprímelo a NSP/XCI en un PC (por ejemplo con nsz) y cópialo aquí."
        else null

    /**
     * Aviso cuando Nexo no ve prod.keys: en BIOS/Switch ni en la carpeta de Eden (Android no deja leer los datos privados
     * de Eden sin su permiso de carpeta). No bloquea nada; null si Nexo sí las ve o el usuario lo ocultó.
     */
    fun keys(keysVisible: Boolean, hidden: Boolean, path: String): String? =
        if (keysVisible || hidden) null
        else "Nexo no ve tus claves de Switch (no hay prod.keys en $path ni permiso sobre la carpeta de Eden). " +
            "Si ya configuraste las claves en Eden, puedes ignorar este aviso: tus juegos aparecen y se abren igual."

    /** Notas del estado del emulador de Switch sin los avisos de claves cuando el usuario pidió no volver a verlos. */
    fun visibleNotes(notes: List<String>, hidden: Boolean): List<String> =
        if (!hidden) notes else notes.filterNot { "claves" in it.lowercase(Locale.ROOT) || "prod.keys" in it }
}

/**
 * Qué es cada archivo de la carpeta de una consola. Solo depende del nombre: ni las claves, ni el firmware, ni el
 * tamaño (los NSP de más de 4 GB son normales) deciden si un juego aparece en la estantería.
 */
internal object ShelfRules {
    enum class Kind { Game, Archive, Unsupported, Ignored }
    fun kind(systemId: String, name: String, cueNames: Set<String> = emptySet()): Kind {
        val ext = GameFormats.extension(name)
        return when {
            SwitchNotice.unsupported(systemId, name) != null -> Kind.Unsupported
            ext in GameFormats.archives -> Kind.Archive
            ext in GameFormats.extensions(systemId) && !(ext == "bin" && GameFormats.stem(name).lowercase() in cueNames) -> Kind.Game
            else -> Kind.Ignored
        }
    }
}
