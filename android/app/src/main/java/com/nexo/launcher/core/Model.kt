package com.nexo.launcher.core

import android.app.Activity
import android.content.Intent
import android.net.Uri

/** Cómo se dibuja un juego en la estantería: el soporte físico de su consola. */
enum class MediaKind { Cartridge, DvdCase, BluRayCase, Disc, Umd, MiniDisc, GameCard }

/** Una consola del carrusel. Los colores son ARGB (0xFFRRGGBB). */
data class ConsoleSystem(
    val id: String,
    val name: String,
    val emulatorName: String,
    val maker: String,
    val year: Int?,
    val description: String,
    val primary: Long,
    val secondary: Long,
    val accent: Long,
    val media: MediaKind,
    /** Emulador no oficial o inmaduro en Android: la UI lo marca como experimental. */
    val experimental: Boolean,
    /** Foto de la consola en los assets ("PS2.png"), compartida con la versión de PC. */
    val photoAsset: String,
    /** Texto de ayuda cuando no hay juegos: carpeta y extensiones admitidas. */
    val gamesHint: String,
)

enum class EmulatorState { Installed, NotInstalled, Unavailable }

data class EmulatorStatus(
    val systemId: String,
    val state: EmulatorState,
    /** Nombre visible del emulador elegido ("PPSSPP", "Dolphin"…). */
    val label: String,
    val packageName: String?,
    /** Requisitos pendientes o avisos ("Necesita la BIOS de PS2 dentro del emulador"). */
    val notes: List<String> = emptyList(),
)

/** Compatibilidad conocida; status usa los mismos valores que en PC (Perfect, Playable, Ingame, Menu, Intro, Loadable, Nothing). */
data class GameCompatibility(val status: String, val explanation: String? = null)

data class Game(
    val systemId: String,
    val title: String,
    /** Documento del juego (content:// del árbol elegido por el usuario). */
    val uri: Uri,
    val fileName: String,
    val sizeBytes: Long,
    /** Portada y fondo: content://, file:// o https://; null si no hay. */
    val cover: String? = null,
    val background: String? = null,
    /** "GameCube" o "Wii" cuando se sabe. */
    val platform: String? = null,
    val compatibility: GameCompatibility? = null,
)

/** Un comprimido o paquete que hay que preparar antes de jugar. */
data class PendingArchive(
    val systemId: String,
    val title: String,
    val uri: Uri,
    val sizeBytes: Long,
    val ready: Boolean,
    val message: String,
)

data class ScanResult(val games: List<Game>, val archives: List<PendingArchive>)

/** Progreso real de escritura; totalBytes es null mientras se examina el archivo. */
data class ArchiveProgress(val bytesWritten: Long, val totalBytes: Long?)

enum class SystemSetupMode { NotRequired, MissingFiles, Guided, NeedsEdenGrant, Automatic, Installed }
data class SystemSetupStatus(
    val systemId: String,
    val mode: SystemSetupMode,
    val folder: Uri?,
    val message: String,
    val steps: List<String> = emptyList(),
)
data class SystemFilesResult(val status: SystemSetupStatus, val message: String, val openEmulatorIntent: Intent? = null)

/** El original se conserva. Este recibo permite borrarlo SOLO tras la confirmación del usuario. */
class ArchiveExtractionResult internal constructor(
    val archive: PendingArchive,
    val directory: Uri,
    val bytesWritten: Long,
    internal val receipt: String,
)

sealed class LaunchResult {
    object Started : LaunchResult()
    data class Failed(val message: String) : LaunchResult()
}

/**
 * Lo que la interfaz necesita del núcleo. La interfaz nunca toca archivos, paquetes ni intents directamente.
 */
interface NexoCore {
    val systems: List<ConsoleSystem>

    /** Carpeta raíz de la biblioteca elegida por el usuario (árbol SAF persistente), o null si aún no hay. */
    val libraryRoot: Uri?

    /** Guarda el permiso persistente; scan crea Juegos/<Id> en IO. Puede fallar si Android deniega el permiso: la UI debe mostrar la excepción. */
    fun setLibraryRoot(tree: Uri)

    fun emulatorStatus(systemId: String): EmulatorStatus

    suspend fun scan(systemId: String): ScanResult

    /** Busca portada y fondo (local o en línea, con caché); devuelve el juego actualizado. */
    suspend fun ensureArt(game: Game): Game

    /** Cancela cancelando la corrutina. onProgress se invoca en el contexto del llamador. */
    suspend fun extractArchive(archive: PendingArchive, onProgress: (ArchiveProgress) -> Unit = {}): ArchiveExtractionResult

    /** Borrado permanente SAF, explícito, de un original extraído correctamente en esta sesión. */
    suspend fun deleteArchive(result: ArchiveExtractionResult)

    /** Consulta SAF en IO y actualiza las notas del emulador; no afirma conocer ajustes privados. */
    suspend fun setupStatus(systemId: String): SystemSetupStatus
    /** Lanzar con ActivityResultContracts.StartActivityForResult; luego pasar data.data a onEdenGrant. */
    fun edenGrantIntent(): Intent
    fun onEdenGrant(uri: Uri)
    suspend fun installSystemFiles(systemId: String, onProgress: (ArchiveProgress) -> Unit = {}): SystemFilesResult

    fun launch(activity: Activity, game: Game): LaunchResult

    /** Abre el emulador sin juego. */
    fun openEmulator(activity: Activity, systemId: String): LaunchResult

    /** Lleva a la instalación oficial del emulador (Play Store o descarga oficial + instalador del sistema). */
    fun installEmulator(activity: Activity, systemId: String): LaunchResult

    /**
     * Versión nueva de Nexo publicada en GitHub con Nexo.apk; null sin red, sin versión nueva o ante cualquier error.
     * Automática (manual = false): sin ninguna petición si «Buscar actualizaciones al abrir» está desactivada.
     */
    suspend fun checkAppUpdate(manual: Boolean = false): AppUpdate?

    /** Descarga y verifica (tamaño, SHA-256, paquete y versión) el APK de la actualización. onProgress de 0 a 1. */
    suspend fun downloadAppUpdate(update: AppUpdate, onProgress: (Float) -> Unit): java.io.File

    /** Abre el instalador del sistema (el usuario confirma); sin permiso de instalar apps, abre ese ajuste. */
    suspend fun installAppUpdate(activity: Activity, apk: java.io.File): LaunchResult

    /** Si Android ya permite a Nexo instalar apps (para reanudar la actualización al volver del ajuste). */
    fun canInstallApps(): Boolean

    /** Abre en el navegador la página de donación de Ko-fi. */
    fun openDonate(activity: Activity): LaunchResult

    /** Opciones guardadas (actualizaciones al abrir, portadas en línea, aviso de claves de Switch). */
    val settings: NexoSettings

    /** Versión instalada de Nexo («1.1.0»). */
    val appVersion: String

    /** Abre en el navegador el repositorio de Nexo en GitHub. */
    fun openRepository(activity: Activity): LaunchResult
}
