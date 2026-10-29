package com.nexo.launcher.ui

import android.app.Activity
import android.app.Application
import android.content.Intent
import android.net.Uri
import android.os.SystemClock
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.viewModelScope
import com.nexo.launcher.core.ArchiveExtractionResult
import com.nexo.launcher.core.ArchiveProgress
import com.nexo.launcher.core.ConsoleSystem
import com.nexo.launcher.core.EmulatorState
import com.nexo.launcher.core.EmulatorStatus
import com.nexo.launcher.core.Game
import com.nexo.launcher.core.LaunchResult
import com.nexo.launcher.core.NexoCore
import com.nexo.launcher.core.NexoCores
import com.nexo.launcher.core.PendingArchive
import com.nexo.launcher.core.ScanResult
import com.nexo.launcher.core.SystemSetupMode
import com.nexo.launcher.core.SystemSetupStatus
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.launch

/** Una pieza de la estantería: un juego o un comprimido pendiente. */
sealed class ShelfItem {
    data class GameItem(val game: Game) : ShelfItem()
    data class ArchiveItem(val archive: PendingArchive) : ShelfItem()
}

data class Toast(val text: String, val error: Boolean = false, val id: Long = System.nanoTime())

/** Filas de la pantalla de Opciones, en orden. */
enum class OptionItem { Library, Artwork, AutoUpdate, UpdateNow, SwitchNotice, About, Donate }

class NexoViewModel(app: Application) : AndroidViewModel(app) {
    val core: NexoCore = NexoCores.create(app)
    val systems: List<ConsoleSystem> = core.systems

    var active by mutableIntStateOf(0)
        private set
    var gamesMode by mutableStateOf(false)
        private set
    var gameIndex by mutableIntStateOf(0)
        private set
    var libraryRoot by mutableStateOf(core.libraryRoot)
        private set
    var toast by mutableStateOf<Toast?>(null)
        private set
    var scanning by mutableStateOf(false)
        private set

    val statuses = mutableStateMapOf<String, EmulatorStatus>()
    private val shelves = mutableStateMapOf<String, List<ShelfItem>>()
    private var scanJob: Job? = null
    private val artRequested = mutableSetOf<String>()
    private val artCache = mutableMapOf<String, Game>()
    private val artJobs = mutableMapOf<String, Job>()
    var artRevision by mutableIntStateOf(0)
        private set

    val current: ConsoleSystem get() = systems[active]
    val shelf: List<ShelfItem> get() = shelves[current.id] ?: emptyList()
    val selectedItem: ShelfItem? get() = if (gamesMode) shelf.getOrNull(gameIndex) else null
    val currentStatus: EmulatorStatus? get() = statuses[current.id]

    init {
        refreshStatuses()
        rescan()
        checkAppUpdate(manual = false)
    }

    // ------------------------------------------------------------------ Opciones

    var optionsOpen by mutableStateOf(false)
        private set
    var optionIndex by mutableIntStateOf(0)
        private set
    var downloadArtwork by mutableStateOf(core.settings.downloadArtwork)
        private set
    var checkUpdates by mutableStateOf(core.settings.checkUpdates)
        private set
    var hideSwitchKeysNotice by mutableStateOf(core.settings.hideSwitchKeysNotice)
        private set
    var checkingUpdates by mutableStateOf(false)
        private set
    val appVersion: String get() = core.appVersion
    val options = OptionItem.entries

    fun openOptions() { if (gamesMode) exitGames(); optionsOpen = true; optionIndex = 0 }
    fun closeOptions() { optionsOpen = false }
    fun moveOption(direction: Int) { optionIndex = (optionIndex + direction).coerceIn(0, options.lastIndex) }
    fun selectOption(index: Int) { optionIndex = index.coerceIn(0, options.lastIndex) }

    /** A o toque sobre una fila: cambia la opción o hace la acción. pickLibrary abre el selector de carpetas de Android. */
    fun activateOption(item: OptionItem, activity: Activity, pickLibrary: () -> Unit) {
        try {
            when (item) {
                OptionItem.Library -> pickLibrary()
                OptionItem.Artwork -> {
                    core.settings.downloadArtwork = !downloadArtwork
                    downloadArtwork = core.settings.downloadArtwork
                    // Al activarlas, la estantería vuelve a pedir el arte que falte.
                    if (downloadArtwork) { artCache.clear(); rescan() }
                }
                OptionItem.AutoUpdate -> { core.settings.checkUpdates = !checkUpdates; checkUpdates = core.settings.checkUpdates }
                OptionItem.UpdateNow -> checkAppUpdate(manual = true)
                OptionItem.SwitchNotice -> {
                    core.settings.hideSwitchKeysNotice = !hideSwitchKeysNotice
                    hideSwitchKeysNotice = core.settings.hideSwitchKeysNotice
                    refreshStatuses()
                }
                OptionItem.About -> report(core.openRepository(activity), "Abriendo el repositorio de Nexo en tu navegador…")
                OptionItem.Donate -> donate(activity)
            }
        } catch (e: Exception) { show("No se pudo guardar la opción: ${e.message}", error = true) }
    }

    /** «No volver a mostrar» desde la guía de claves de Switch. */
    fun hideSwitchKeysNotice() {
        try { core.settings.hideSwitchKeysNotice = true; hideSwitchKeysNotice = true } catch (e: Exception) { show("No se pudo guardar: ${e.message}", error = true); return }
        guide = null
        refreshStatuses()
        show("De acuerdo: no volveré a avisar de las claves de Switch. Puedes reactivarlo en Opciones.")
    }

    // ------------------------------------------------------------------ Actualización de Nexo

    /** Lo que se ve de la actualización: descargando, lista, esperando el permiso o ya pedida una vez. */
    data class UpdateUi(val version: String, val progress: Float = 0f, val ready: Boolean = false,
                        val needsPermission: Boolean = false, val asked: Boolean = false)

    var update by mutableStateOf<UpdateUi?>(null)
        private set
    private var updateApk: java.io.File? = null
    private var installingUpdate = false

    /**
     * Al abrir (si la opción está activada): si GitHub tiene una versión nueva con Nexo.apk, se descarga y verifica sola.
     * Sin red no se avisa de nada. manual: «Buscar actualizaciones ahora» en Opciones, que sí informa del resultado.
     */
    private fun checkAppUpdate(manual: Boolean) {
        if (manual && (checkingUpdates || update != null)) { update?.let { show("Nexo ${it.version}: " + if (it.ready) "listo para instalar." else "descargando…") }; return }
        viewModelScope.launch {
            if (!manual) delay(3000) else checkingUpdates = true
            val found = try { core.checkAppUpdate(manual) } finally { checkingUpdates = false }
            if (found == null) {
                if (manual) show("No hay una versión nueva: tienes Nexo ${core.appVersion} (o no se pudo consultar GitHub).")
                return@launch
            }
            update = UpdateUi(found.version)
            show("Hay una versión nueva de Nexo (${found.version}). Se descarga ahora; después Android te pedirá confirmar la instalación. Tus juegos y ajustes se conservan.")
            try {
                updateApk = core.downloadAppUpdate(found) { p -> viewModelScope.launch { update = update?.copy(progress = p) } }
                update = update?.copy(progress = 1f, ready = true)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                update = null
                show("No se pudo descargar la actualización de Nexo: ${e.message}. Se volverá a intentar la próxima vez que abras Nexo.", error = true)
            }
        }
    }

    /** Con la descarga lista y Nexo en primer plano, abre el instalador una vez (o al volver de conceder el permiso). */
    fun resumeUpdate(activity: Activity) {
        val u = update ?: return
        if (!u.ready || u.asked || installingUpdate) return
        if (!(activity as LifecycleOwner).lifecycle.currentState.isAtLeast(Lifecycle.State.RESUMED)) return
        if (u.needsPermission && !core.canInstallApps()) return
        installUpdate(activity)
    }

    /** Ⓨ o tocar el aviso de la barra superior: instalar ya la versión descargada. */
    fun installUpdate(activity: Activity) {
        val u = update ?: return
        val apk = updateApk ?: return
        if (!u.ready || installingUpdate) return
        installingUpdate = true
        viewModelScope.launch {
            try {
                when (val result = core.installAppUpdate(activity, apk)) {
                    LaunchResult.Started -> {
                        update = u.copy(needsPermission = false, asked = true)
                        show("Confirma la actualización a Nexo ${u.version} en la ventana de Android.")
                    }
                    is LaunchResult.Failed -> {
                        update = u.copy(needsPermission = !core.canInstallApps())
                        show(result.message, error = !update!!.needsPermission)
                    }
                }
            } finally { installingUpdate = false }
        }
    }

    fun donate(activity: Activity) = report(core.openDonate(activity), "Abriendo Ko-fi en tu navegador…")

    fun refreshStatuses() {
        for (s in systems) statuses[s.id] = core.emulatorStatus(s.id)
        refreshSetups()
    }

    // ------------------------------------------------------------------ BIOS, firmware y claves

    /** Estado de los archivos del sistema de cada consola (BIOS/<Id> en la biblioteca). */
    val setups = mutableStateMapOf<String, SystemSetupStatus>()

    /** Guía paso a paso para los emuladores que no dejan instalar desde fuera. */
    data class Guide(val title: String, val message: String, val steps: List<String>, val openEmulator: Intent?, val keysNotice: Boolean = false)

    var guide by mutableStateOf<Guide?>(null)
        private set
    var installingFiles by mutableStateOf(false)
        private set

    private fun refreshSetups() {
        if (libraryRoot == null) return
        viewModelScope.launch {
            for (s in systems) {
                try { setups[s.id] = core.setupStatus(s.id) }
                catch (e: CancellationException) { throw e }
                catch (_: Exception) { }
            }
        }
    }

    /** Botón «BIOS / firmware» de la ficha (o X en el mando). */
    fun systemFiles(requestEdenGrant: () -> Unit) {
        val setup = setups[current.id] ?: return
        when (setup.mode) {
            SystemSetupMode.NotRequired -> show(setup.message)
            // Switch: Nexo no ve las claves, pero pueden estar ya en Eden. Es solo un aviso y se puede ocultar.
            SystemSetupMode.MissingFiles -> guide = if (current.id == "Switch") Guide("Claves de Switch", setup.message, setup.steps, null, keysNotice = true)
                else Guide("Faltan archivos de ${current.name}", setup.message, setup.steps, null)
            SystemSetupMode.NeedsEdenGrant -> requestEdenGrant()
            else -> installFiles(current.id)
        }
    }

    /** Eden concedió su carpeta: se instalan ya las claves y el firmware. */
    fun onEdenGranted(uri: Uri) {
        try {
            core.onEdenGrant(uri)
        } catch (e: Exception) {
            show("No pude usar la carpeta de Eden: ${e.message}. Elige la carpeta «Eden» que ofrece el selector.", error = true)
            return
        }
        installFiles("Switch")
    }

    private fun installFiles(systemId: String) {
        if (installingFiles) return
        installingFiles = true
        viewModelScope.launch {
            try {
                val result = core.installSystemFiles(systemId)
                setups[systemId] = result.status
                if (result.status.mode == SystemSetupMode.Guided || result.openEmulatorIntent != null && result.status.steps.isNotEmpty())
                    guide = Guide("Preparar ${statuses[systemId]?.label ?: systemId}", result.message, result.status.steps, result.openEmulatorIntent)
                else show(result.message)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                show("No se pudieron instalar los archivos: ${e.message}", error = true)
            } finally {
                installingFiles = false
                statuses[systemId] = core.emulatorStatus(systemId)
            }
        }
    }

    fun openGuideEmulator(activity: Activity) {
        val intent = guide?.openEmulator ?: run { guide = null; return }
        guide = null
        try { activity.startActivity(intent) }
        catch (e: Exception) { show("No pude abrir el emulador: ${e.message}", error = true) }
    }

    fun closeGuide() { guide = null }

    fun setLibrary(tree: Uri) {
        try {
            core.setLibraryRoot(tree)
        } catch (e: Exception) {
            show("No pude usar esa carpeta: ${e.message}. Prueba con otra.", error = true)
            return
        }
        val changed = libraryRoot != null
        libraryRoot = core.libraryRoot
        shelves.clear()
        artCache.clear()
        gamesMode = false
        gameIndex = 0
        show(if (changed) "Biblioteca cambiada. Nexo no movió ni borró nada de la carpeta anterior; busca ahora tus juegos en la nueva."
             else "Biblioteca lista. Copia tus juegos en Juegos/<consola> dentro de esa carpeta y aparecerán solos.")
        refreshStatuses()
        rescan()
    }

    // ------------------------------------------------------------------ Carrusel

    fun select(index: Int) {
        val target = index.coerceIn(0, systems.lastIndex)
        if (target == active) return
        if (gamesMode) exitGames()
        active = target
        gameIndex = 0
        rescan()
    }

    fun step(direction: Int) {
        // El carrusel da la vuelta, como en PC.
        val n = systems.size
        select(((active + direction) % n + n) % n)
    }

    fun rescan() {
        if (libraryRoot == null) return
        val system = current
        scanJob?.cancel()
        val revision = ++artRevision
        artJobs.values.toList().forEach { it.cancel() }
        artJobs.clear()
        artRequested.clear()
        scanning = true
        scanJob = viewModelScope.launch {
            val result = try {
                core.scan(system.id)
            } catch (e: CancellationException) {
                throw e // un escaneo cancelado no toca la estantería
            } catch (e: Exception) {
                show("No pude leer los juegos de ${system.name}: ${e.message}", error = true)
                ScanResult(emptyList(), emptyList())
            }
            currentCoroutineContext().ensureActive()
            if (revision != artRevision) return@launch
            val selectedUri = selectedItem?.uriKey()
            val present = result.games.map { it.uri.toString() }.toSet()
            artCache.entries.removeAll { it.value.systemId == system.id && it.key !in present }
            // Conservar solo el arte: los metadatos nuevos del escáner siempre prevalecen.
            shelves[system.id] = result.archives.map { ShelfItem.ArchiveItem(it) } +
                result.games.map { game ->
                    val cached = artCache[game.uri.toString()]?.takeIf { sameFile(it, game) }
                    ShelfItem.GameItem(if (cached == null) game else game.copy(cover = cached.cover, background = cached.background))
                }
            val retained = shelf.indexOfFirst { it.uriKey() == selectedUri }
            gameIndex = if (retained >= 0) retained else gameIndex.coerceIn(0, maxOf(0, shelf.lastIndex))
            if (shelf.isEmpty()) gamesMode = false
            scanning = false
        }
    }

    /** Arte de las piezas visibles: se pide una vez por juego y actualiza la estantería al llegar. */
    fun requestArt(game: Game) {
        val key = game.uri.toString()
        if (scanning || !artRequested.add(key)) return
        val revision = artRevision
        val job = viewModelScope.launch(start = CoroutineStart.LAZY) {
          try {
            val updated = try {
                core.ensureArt(game)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (revision == artRevision) artRequested.remove(key)
                return@launch
            }
            currentCoroutineContext().ensureActive()
            if (revision != artRevision) return@launch
            val list = shelves[game.systemId] ?: return@launch
            if (list.none { it is ShelfItem.GameItem && sameFile(it.game, game) }) return@launch
            artCache[key] = updated
            shelves[game.systemId] = list.map {
                if (it is ShelfItem.GameItem && sameFile(it.game, game))
                    ShelfItem.GameItem(it.game.copy(cover = updated.cover, background = updated.background, platform = updated.platform ?: it.game.platform))
                else it
            }
          } finally { if (revision == artRevision) artJobs.remove(key) }
        }
        artJobs[key] = job
        job.start()
    }

    private fun sameFile(a: Game, b: Game) = a.systemId == b.systemId && a.uri == b.uri &&
        a.fileName == b.fileName && a.sizeBytes == b.sizeBytes && a.title == b.title

    private fun ShelfItem.uriKey(): String = when (this) {
        is ShelfItem.GameItem -> game.uri.toString()
        is ShelfItem.ArchiveItem -> archive.uri.toString()
    }

    // ------------------------------------------------------------------ Estantería

    fun enterGames() {
        if (shelf.isEmpty()) {
            show(if (libraryRoot == null) "Primero elige la carpeta de tu biblioteca." else "Aún no hay juegos de ${current.name}. ${current.gamesHint}")
            return
        }
        gamesMode = true
        gameIndex = gameIndex.coerceIn(0, shelf.lastIndex)
    }

    fun exitGames() {
        gamesMode = false
    }

    fun stepGame(direction: Int): Boolean {
        val next = (gameIndex + direction).coerceIn(0, maxOf(0, shelf.lastIndex))
        if (next == gameIndex) return false
        gameIndex = next
        return true
    }

    fun selectGame(index: Int) {
        if (index !in shelf.indices) return
        gamesMode = true
        gameIndex = index
    }

    // ------------------------------------------------------------------ Acciones

    private var lastLaunch = -2000L

    private fun launchOnce(action: () -> LaunchResult, message: String) {
        val now = SystemClock.elapsedRealtime()
        if (now - lastLaunch < 2000) return
        lastLaunch = now
        report(action(), message)
    }

    fun primary(activity: Activity) {
        val status = currentStatus
        when {
            status == null -> Unit
            gamesMode -> when (val item = selectedItem) {
                is ShelfItem.GameItem -> launchGame(activity, item.game, status)
                is ShelfItem.ArchiveItem -> extract(item.archive)
                null -> Unit
            }
            status.state == EmulatorState.Installed -> launchOnce({ core.openEmulator(activity, current.id) }, "Abriendo ${status.label}…")
            status.state == EmulatorState.NotInstalled -> report(core.installEmulator(activity, current.id), "Instalando ${status.label} desde su fuente oficial…")
            else -> show(status.notes.firstOrNull() ?: "Esta consola aún no tiene un emulador fiable en Android.", error = true)
        }
    }

    private fun launchGame(activity: Activity, game: Game, status: EmulatorStatus) {
        if (status.state != EmulatorState.Installed) {
            show("Para jugar a ${game.title} falta ${status.label}. Vuelve a la consola y pulsa «Instalar».", error = true)
            return
        }
        // Una ráfaga de pulsaciones no debe abrir el juego varias veces.
        launchOnce({ core.launch(activity, game) }, "¡A jugar! Abriendo ${game.title} en ${status.label}…")
    }

    // ------------------------------------------------------------------ Comprimidos

    /** Progreso de los comprimidos que se están descomprimiendo, por URI. */
    val extracting = mutableStateMapOf<String, ArchiveProgress>()

    /** Comprimido ya descomprimido cuyo borrado se está preguntando (en Android no hay Papelera). */
    var pendingDelete by mutableStateOf<ArchiveExtractionResult?>(null)
        private set

    private fun extract(archive: PendingArchive) {
        if (!archive.ready) { show(archive.message, error = true); return }
        val key = archive.uri.toString()
        if (key in extracting) return
        extracting[key] = ArchiveProgress(0, null)
        show("Descomprimiendo ${archive.title}… puedes seguir usando Nexo mientras tanto.")
        viewModelScope.launch {
            try {
                val result = core.extractArchive(archive) { extracting[key] = it }
                extracting.remove(key)
                show("¡Listo! ${archive.title} ya está en tu estantería.")
                pendingDelete = result
                rescan()
            } catch (e: CancellationException) {
                extracting.remove(key)
                throw e
            } catch (e: Exception) {
                extracting.remove(key)
                show("No se pudo descomprimir ${archive.title}: ${e.message}", error = true)
            }
        }
    }

    /** Respuesta a «¿Borrar el comprimido?». Solo se borra si el usuario lo confirma. */
    fun confirmDelete(delete: Boolean) {
        val result = pendingDelete ?: return
        pendingDelete = null
        if (!delete) { show("Se conserva el comprimido ${result.archive.title}."); return }
        viewModelScope.launch {
            try {
                core.deleteArchive(result)
                show("Comprimido borrado: has liberado ${sizeText(result.archive.sizeBytes)}.")
                rescan()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                show("No se pudo borrar el comprimido: ${e.message}", error = true)
            }
        }
    }

    private fun report(result: LaunchResult, ok: String) {
        when (result) {
            LaunchResult.Started -> show(ok)
            is LaunchResult.Failed -> show(result.message, error = true)
        }
    }

    fun show(text: String, error: Boolean = false) {
        toast = Toast(text, error)
    }

    fun dismissToast(t: Toast) {
        if (toast?.id == t.id) toast = null
    }
}

/** Tamaño legible: «850 MB», «4,3 GB». */
fun sizeText(bytes: Long): String =
    if (bytes >= 1L shl 30) String.format(java.util.Locale.getDefault(), "%.1f GB", bytes / 1073741824.0)
    else String.format(java.util.Locale.getDefault(), "%d MB", bytes / 1048576)
