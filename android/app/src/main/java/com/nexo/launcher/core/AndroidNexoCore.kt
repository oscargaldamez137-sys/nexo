package com.nexo.launcher.core

import android.app.Activity
import android.content.ClipData
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import android.provider.DocumentsContract
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

internal class AndroidNexoCore(private val context: Context) : NexoCore {
    override val systems = Catalog.systems
    private val library = SafLibrary(context)
    private val artwork = AndroidArtwork(context, library)
    private val installer = EmulatorInstaller(context)
    private val updater = AndroidAppUpdater(context)
    private val archives = AndroidArchives(context, library)
    private val systemFiles = AndroidSystemFiles(context, library)
    override val libraryRoot: Uri? get() = library.root
    override fun setLibraryRoot(tree: Uri) { library.setRoot(tree); systemFiles.clearStatuses() }

    @Suppress("DEPRECATION")
    private fun installed(profile: EmulatorProfile): ApplicationInfo? = profile.packages.firstNotNullOfOrNull {
        runCatching { context.packageManager.getApplicationInfo(it, 0).takeIf { app -> app.enabled } }.getOrNull()
    }

    override fun emulatorStatus(systemId: String): EmulatorStatus {
        val system = systems.firstOrNull { it.id == systemId }
            ?: return EmulatorStatus(systemId, EmulatorState.Unavailable, "Sistema desconocido", null)
        val profile = Emulators.profile(systemId)
            ?: return EmulatorStatus(systemId, EmulatorState.Unavailable, system.emulatorName, null, listOf(system.description))
        val app = installed(profile)
        if (app == null && systemId == "Switch" && GameFormats.edenVariant(Build.VERSION.SDK_INT) == null)
            return EmulatorStatus(systemId, EmulatorState.Unavailable, system.emulatorName, null, listOf("Eden necesita Android 10 o posterior."))
        val coreHint = if (app != null && Emulators.isRetro(app.packageName)) when (systemId) {
            "GBC" -> listOf("Abre RetroArch → Online Updater → Core Downloader → Nintendo - Game Boy / Color (Gambatte).")
            "GBA" -> listOf("Abre RetroArch → Online Updater → Core Downloader → Nintendo - Game Boy Advance (mGBA).")
            else -> emptyList()
        } else emptyList()
        val fallbackNotes = if (app?.packageName?.startsWith("it.dbtecno.pizzaboy") == true || app?.packageName?.startsWith("com.fastemulator.") == true)
            listOf("El lanzamiento externo de esta alternativa depende de su versión; si no abre el juego, selecciónalo dentro del emulador.") else emptyList()
        val retroNotes = if (systemId in listOf("GBC", "GBA") && app != null && Emulators.isRetro(app.packageName)) Emulators.profile("Retro")!!.notes else emptyList()
        return EmulatorStatus(systemId, if (app == null) EmulatorState.NotInstalled else EmulatorState.Installed,
            if (systemId in listOf("GBC", "GBA")) Emulators.label(systemId, app?.packageName) else system.emulatorName,
            app?.packageName, profile.notes + coreHint + fallbackNotes + retroNotes + systemFiles.note(systemId))
    }

    override suspend fun scan(systemId: String): ScanResult = withContext(Dispatchers.IO) {
        require(systems.any { it.id == systemId }) { "Sistema desconocido." }
        try { library.scan(systemId).also { systemFiles.status(systemId) } }
        catch (e: SecurityException) { throw IllegalStateException("Android retiró el permiso de la biblioteca. Vuelve a elegir su carpeta.", e) }
    }
    override suspend fun ensureArt(game: Game): Game = withContext(Dispatchers.IO) { artwork.ensure(game) }
    override suspend fun extractArchive(archive: PendingArchive, onProgress: (ArchiveProgress) -> Unit) = archives.extract(archive, onProgress)
    override suspend fun deleteArchive(result: ArchiveExtractionResult) { archives.delete(result) }
    override suspend fun setupStatus(systemId: String) = withContext(Dispatchers.IO) { systemFiles.status(systemId) }
    override fun edenGrantIntent() = systemFiles.grantIntent()
    override fun onEdenGrant(uri: Uri) = systemFiles.onGrant(uri)
    override suspend fun installSystemFiles(systemId: String, onProgress: (ArchiveProgress) -> Unit) = systemFiles.install(systemId, onProgress)

    override fun launch(activity: Activity, game: Game): LaunchResult = try {
        val profile = Emulators.profile(game.systemId) ?: error("No hay un emulador verificado para esta consola en Android.")
        val app = installed(profile) ?: error("Instala primero ${systems.first { it.id == game.systemId }.emulatorName}.")
        val location = library.locations[game.uri.toString()]
        check(location != null && location.systemId == game.systemId) { "Vuelve a escanear la biblioteca antes de abrir este juego." }
        val root = library.root
        check(root != null && root.authority == game.uri.authority && DocumentsContract.getTreeDocumentId(root) == DocumentsContract.getTreeDocumentId(game.uri)) { "Este juego ya no pertenece a la biblioteca elegida." }
        val intent = Intent(Intent.ACTION_VIEW).apply {
            component = ComponentName(app.packageName, Emulators.activity(game.systemId, app.packageName))
            data = game.uri
            clipData = ClipData.newRawUri("Juego de Nexo", game.uri)
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        }
        when (game.systemId) {
            "Retro", "GBC", "GBA" -> {
              if (Emulators.isRetro(app.packageName)) {
                val path = library.realPath(game.uri) ?: error("RetroArch necesita un archivo local. Elige una biblioteca del almacenamiento interno o de una tarjeta SD, no de un proveedor en la nube.")
                val core = GameFormats.core(game.systemId, game.fileName) ?: error("No hay un núcleo asignado a este formato.")
                val coreFile = File(app.dataDir, "cores/${core}_libretro_android.so")
                // Inaccessible private storage does NOT prove that a core is missing.
                if (coreFile.parentFile?.canRead() == true && !coreFile.isFile)
                    error("Abre RetroArch → Online Updater → Core Downloader → $core.")
                @Suppress("DEPRECATION") val sdcard = Environment.getExternalStorageDirectory().absolutePath
                val external = "$sdcard/Android/data/${app.packageName}/files"
                intent.putExtra("ROM", path)
                    .putExtra("LIBRETRO", coreFile.absolutePath)
                    .putExtra("CONFIGFILE", "$external/retroarch.cfg")
                    .putExtra("IME", Settings.Secure.getString(context.contentResolver, Settings.Secure.DEFAULT_INPUT_METHOD) ?: "")
                    .putExtra("DATADIR", app.dataDir)
                    .putExtra("APK", app.sourceDir)
                    // Official buildbot targets 28; scoped-storage Play builds target 30+.
                    .putExtra("SDCARD", if (app.targetSdkVersion >= 30) external else sdcard)
                    .putExtra("EXTERNAL", external)
              } else if (app.packageName.startsWith("com.explusalpha.")) {
                  intent.setDataAndType(game.uri, "*/*")
              } else if (app.packageName.startsWith("it.dbtecno.pizzaboy")) {
                  intent.putExtra("rom_uri", game.uri.toString())
              }
            }
            "PS3" -> {
                intent.action = "aenu.intent.action.APS3E"
                if (location.content != null) {
                    val folder = library.realPath(game.uri) ?: error("aPS3e necesita una ruta local para los juegos-carpeta. Usa almacenamiento interno o una SD y concede acceso también en aPS3e.")
                    intent.putExtra("game_dir", folder).addFlags(Intent.FLAG_GRANT_PREFIX_URI_PERMISSION)
                } else intent.putExtra("iso_uri", game.uri.toString())
            }
            "Xbox360" -> intent.setAction("aenu.intent.action.AX360E").putExtra("game_uri", game.uri.toString())
        }
        @Suppress("DEPRECATION") val target = context.packageManager.getActivityInfo(intent.component!!, 0)
        check(target.enabled && target.exported) { "Esta versión del emulador no permite iniciar juegos desde Nexo. Ábrelo y selecciona allí el juego." }
        activity.startActivity(intent)
        LaunchResult.Started
    } catch (e: Exception) { LaunchResult.Failed("No se pudo abrir el juego: ${e.message ?: "comprueba los permisos del emulador"}") }

    override fun openEmulator(activity: Activity, systemId: String): LaunchResult = try {
        val profile = Emulators.profile(systemId) ?: error("No hay un emulador verificado para esta consola en Android.")
        val app = installed(profile) ?: error("Instala primero el emulador.")
        val intent = context.packageManager.getLaunchIntentForPackage(app.packageName)
            ?: error("El emulador no ofrece una pantalla de inicio accesible.")
        activity.startActivity(intent)
        LaunchResult.Started
    } catch (e: Exception) { LaunchResult.Failed(e.message ?: "No se pudo abrir el emulador.") }

    override fun installEmulator(activity: Activity, systemId: String) = installer.install(activity, systemId)
    override suspend fun checkAppUpdate() = updater.check()
    override suspend fun downloadAppUpdate(update: AppUpdate, onProgress: (Float) -> Unit) = updater.download(update, onProgress)
    override suspend fun installAppUpdate(activity: Activity, apk: File) = updater.install(activity, apk)
    override fun canInstallApps() = updater.canInstall()
    override fun openDonate(activity: Activity) = updater.open(activity, AndroidAppUpdater.DONATE_URL)
}
