package com.nexo.launcher.core

import android.app.Activity
import android.app.PendingIntent
import android.app.ProgressDialog
import android.content.*
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import android.widget.Toast
import androidx.core.content.ContextCompat
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import androidx.lifecycle.LifecycleOwner
import kotlinx.coroutines.*
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.lang.ref.WeakReference
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean

internal data class ApkSource(val url: String, val packageName: String, val sha256: String? = null, val zipped: Boolean = false)
internal class EmulatorInstaller(private val context: Context) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val busy = AtomicBoolean()
    private val sessions = ApkSession(context, scope)
    fun install(activity: Activity, id: String): LaunchResult {
        if (id == "Xbox360") return try {
            try { activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=aenu.ax360e.free")).setPackage("com.android.vending")) }
            catch (_: ActivityNotFoundException) { activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=aenu.ax360e.free"))) }
            LaunchResult.Started
        } catch (_: Exception) { LaunchResult.Failed("No se pudo abrir Google Play.") }
        if (Emulators.profile(id) == null) return LaunchResult.Failed("No hay un emulador verificado para esta consola en Android.")
        if (id in listOf("GBC", "GBA")) {
            @Suppress("DEPRECATION") val present = runCatching { context.packageManager.getApplicationInfo(ApkBundles.packageName(id), 0) }.getOrNull()
            if (present != null) return LaunchResult.Failed("${if (id == "GBC") "GBC.emu" else "GBA.emu"} ya está instalado. Usa Abrir emulador; no se sustituye una instalación de Play por otra con distinta firma.")
        }
        if (id !in listOf("GBC", "GBA") && !Build.SUPPORTED_ABIS.contains("arm64-v8a")) return LaunchResult.Failed("Las descargas seleccionadas necesitan un dispositivo Android ARM de 64 bits.")
        if (id == "Switch" && GameFormats.edenVariant(Build.VERSION.SDK_INT) == null) return LaunchResult.Failed("Eden necesita Android 10 o posterior; la versión estándar requiere Android 13.")
        if (!context.packageManager.canRequestPackageInstalls()) return try {
            activity.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")))
            LaunchResult.Failed("Permite instalar aplicaciones desde Nexo, vuelve y pulsa Instalar de nuevo.")
        } catch (_: Exception) { LaunchResult.Failed("Activa «Instalar apps desconocidas» para Nexo en Ajustes de Android.") }
        if (!busy.compareAndSet(false, true)) return LaunchResult.Failed("Ya hay una instalación en curso.")
        val activityRef = WeakReference(activity)
        @Suppress("DEPRECATION") val dialog = ProgressDialog(activity).apply {
            setTitle("Descarga oficial"); setMessage("Consultando la publicación del emulador…"); setProgressStyle(ProgressDialog.STYLE_HORIZONTAL)
            isIndeterminate = true; setCancelable(true); setCanceledOnTouchOutside(false)
        }
        var task: Job? = null
        val lifecycle = (activity as? LifecycleOwner)?.lifecycle
        val observer = LifecycleEventObserver { _, event -> if (event == Lifecycle.Event.ON_DESTROY) task?.cancel() }
        lifecycle?.addObserver(observer)
        dialog.setButton(ProgressDialog.BUTTON_NEGATIVE, "Cancelar") { _, _ -> task?.cancel() }
        dialog.setOnCancelListener { task?.cancel() }
        dialog.show()
        task = scope.launch {
            var apk: File? = null
            var bundle: File? = null
            try {
                val source = withContext(Dispatchers.IO) { resolve(id) }
                val cache = File(context.cacheDir, "install").apply { mkdirs() }
                apk = File.createTempFile("emulator-", ".apk", cache)
                var file: File = requireNotNull(apk)
                var lastUpdate = 0L
                withContext(Dispatchers.IO) {
                    file.outputStream().use { output ->
                        OfficialHttp.download(source.url, OfficialHttp.apkHosts, 768L * 1024 * 1024, output) { written, total ->
                            val now = System.currentTimeMillis()
                            if (now - lastUpdate > 250) {
                                lastUpdate = now
                                scope.launch {
                                    if (dialog.isShowing) {
                                        dialog.isIndeterminate = total <= 0
                                        dialog.progress = if (total > 0) (written * 100 / total).toInt() else 0
                                        dialog.setMessage("${written / 1048576} MB descargados. ${Uri.parse(source.url).host}")
                                    }
                                }
                            }
                        }
                    }
                    currentCoroutineContext().ensureActive()
                    val job = currentCoroutineContext()
                    source.sha256?.let { ApkBundles.verify(file, it) { job.ensureActive() } }
                    if (source.zipped) {
                        check(source.sha256 != null) { "Falta el SHA256 del ZIP oficial." }
                        bundle = file
                        val extracted = File.createTempFile("verified-", ".apk", cache)
                        apk = extracted
                        ApkBundles.extract(file, extracted) { job.ensureActive() }
                        file = extracted
                    }
                    @Suppress("DEPRECATION") val info = context.packageManager.getPackageArchiveInfo(file.absolutePath, 0)
                        ?: throw IOException("El archivo descargado no es un APK válido.")
                    check(info.packageName == source.packageName) { "El APK declara un paquete distinto del emulador esperado." }
                    check((info.applicationInfo?.minSdkVersion ?: Int.MAX_VALUE) <= Build.VERSION.SDK_INT) { "Esta publicación necesita una versión de Android más reciente." }
                }
                val owner = activityRef.get()
                check(owner != null && !owner.isFinishing && !owner.isDestroyed) { "Vuelve a Nexo para iniciar la instalación." }
                dialog.setMessage("APK verificado. Preparando la instalación de Android…"); dialog.isIndeterminate = true
                sessions.install(file, source.packageName, activityRef, ApkSession.Texts(
                    success = "Emulador instalado. Vuelve a Nexo para abrirlo.",
                    failure = "Android no instaló el emulador",
                    retry = "Vuelve a Nexo y pulsa Instalar de nuevo para confirmar la instalación."))
            } catch (e: CancellationException) { toast("Descarga cancelada.") }
            catch (e: Exception) { toast("No se pudo instalar: ${e.message ?: "error de conexión o almacenamiento"}") }
            finally {
                apk?.delete(); bundle?.delete()
                lifecycle?.removeObserver(observer)
                runCatching { dialog.dismiss() }; busy.set(false)
            }
        }
        return LaunchResult.Started
    }
    private fun toast(message: String) = Toast.makeText(context, message, Toast.LENGTH_LONG).show()
    private suspend fun json(url: String) = JSONObject(String(OfficialHttp.bytes(url, OfficialHttp.apkHosts, 2 * 1024 * 1024), Charsets.UTF_8))
    private suspend fun release(repo: String) = json("https://api.github.com/repos/$repo/releases/latest")
    private fun tag(release: JSONObject): String = release.getString("tag_name").removePrefix("v").also {
        require(Regex("[0-9]+(?:[.][0-9]+){1,3}").matches(it)) { "La versión publicada no tiene un formato reconocido." }
    }
    private fun assets(release: JSONObject): List<JSONObject> = release.getJSONArray("assets").let { a -> (0 until a.length()).map { a.getJSONObject(it) } }
    private fun githubAsset(repo: String, asset: JSONObject, pkg: String): ApkSource {
        val url = asset.getString("browser_download_url")
        require(url.startsWith("https://github.com/$repo/releases/download/")) { "Enlace fuera de la publicación oficial." }
        val published = asset.optString("digest").takeUnless { it.isBlank() || it == "null" }
        require(published == null || Regex("sha256:[0-9a-fA-F]{64}").matches(published)) { "El hash publicado usa un formato no reconocido." }
        val digest = published?.substringAfter(':')
        return ApkSource(url, pkg, digest)
    }
    private suspend fun resolve(id: String): ApkSource = when (id) {
        "Retro" -> ApkSource("https://buildbot.libretro.com/stable/${tag(release("libretro/RetroArch"))}/android/RetroArch_aarch64.apk", "com.retroarch.aarch64")
        "GBC", "GBA" -> {
            val release = json(ApkBundles.RELEASE_URL)
            val asset = assets(release).single { it.getString("name") == ApkBundles.asset(id) }
            githubAsset(ApkBundles.EMUEX_REPO, asset, ApkBundles.packageName(id)).also {
                check(it.sha256 != null) { "La publicación oficial no ofrece el SHA256 del ZIP." }
            }.copy(zipped = true)
        }
        "PSP" -> ApkSource("https://www.ppsspp.org/files/${tag(release("hrydgard/ppsspp")).replace('.', '_')}/ppsspp.apk", "org.ppsspp.ppsspp")
        "GameCube-Wii" -> {
            val array = json("https://dolphin-emu.org/update/latest/beta/").getJSONArray("artifacts")
            val asset = (0 until array.length()).map { array.getJSONObject(it) }.single { it.getString("system") == "Android" }
            val url = asset.getString("url")
            require(url.startsWith("https://dl.dolphin-emu.org/releases/") && url.endsWith(".apk"))
            ApkSource(url, "org.dolphinemu.dolphinemu")
        }
        "Switch" -> {
            val release = json("https://git.eden-emu.dev/api/v1/repos/eden-emu/eden/releases/latest")
            val variant = GameFormats.edenVariant(Build.VERSION.SDK_INT) ?: error("Android no compatible con Eden.")
            val asset = assets(release).single { it.getString("name").endsWith("-$variant.apk", true) }
            val url = asset.getString("browser_download_url")
            require(url.startsWith("https://stable.eden-emu.dev/") && url.endsWith("-$variant.apk", true))
            ApkSource(url, if (variant == "standard") "dev.eden.eden_emulator" else "dev.legacy.eden_emulator")
        }
        "PS2" -> {
            val repo = "ARMSX2/ARMSX2"; val assets = assets(release(repo))
            // ABI alone does not establish ARMv8.2. Only use optimized builds when
            // the kernel reports FP16 vector/scalar capability; otherwise official legacy.
            val cpu = runCatching { File("/proc/cpuinfo").bufferedReader().use { reader ->
                val buffer = CharArray(262144); var length = 0
                while (length < buffer.size) { val n = reader.read(buffer, length, buffer.size - length); if (n < 0) break; length += n }
                String(buffer, 0, length)
            } }.getOrDefault("")
            val arm82 = Regex("\\basimdhp\\b").containsMatchIn(cpu) && Regex("\\bfphp\\b").containsMatchIn(cpu)
            val chosen = GameFormats.sdkAsset(assets.map { it.getString("name") }, Build.VERSION.SDK_INT, arm82) ?: error("No hay APK oficial compatible con este Android.")
            githubAsset(repo, assets.single { it.getString("name") == chosen }, "com.armsx2")
        }
        "PS3" -> {
            val repo = "aenu1/aps3e"; val release = release(repo)
            val apks = assets(release).filter { it.getString("name").endsWith(".apk", true) && !it.getString("name").contains("premium", true) }
            val chosen = apks.singleOrNull { it.getString("name") == release.getString("tag_name") + ".apk" } ?: apks.single()
            githubAsset(repo, chosen, "aenu.aps3e")
        }
        "Xbox" -> {
            val repo = "izzy2lost/xemu"
            githubAsset(repo, assets(release(repo)).single { it.getString("name") == "app-release.apk" }, "com.izzy2lost.x1box")
        }
        else -> error("No hay descarga oficial para esta consola.")
    }
}

/**
 * Writes a verified APK into a PackageInstaller session and lets Android install it. Android always asks the
 * user to confirm (sideloaded apps cannot install silently). Shared by emulator downloads and Nexo's own update.
 */
internal class ApkSession(private val context: Context, private val scope: CoroutineScope) {
    data class Texts(val success: String, val failure: String, val retry: String)

    suspend fun install(file: File, packageName: String, activity: WeakReference<Activity>, texts: Texts) {
        val installer = context.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(packageName); setSize(file.length())
            if (Build.VERSION.SDK_INT >= 31) setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_REQUIRED)
        }
        var sessionId = installer.createSession(params)
        var session: PackageInstaller.Session? = null
        try {
            val opened = installer.openSession(sessionId); session = opened
            withContext(Dispatchers.IO) {
                opened.openWrite("base.apk", 0, file.length()).use { out ->
                    file.inputStream().use { input -> val b = ByteArray(65536); while (true) { currentCoroutineContext().ensureActive(); val n = input.read(b); if (n < 0) break; out.write(b, 0, n) } }
                    opened.fsync(out)
                }
            }
            currentCoroutineContext().ensureActive()
            commit(opened, sessionId, activity, texts)
            sessionId = -1 // callback now owns the session
        } finally {
            if (sessionId >= 0) runCatching { installer.abandonSession(sessionId) }
            runCatching { session?.close() }
        }
    }

    private fun commit(session: PackageInstaller.Session, id: Int, activity: WeakReference<Activity>, texts: Texts) {
        val action = "${context.packageName}.INSTALL.${UUID.randomUUID()}"
        var finished = false
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(ctx: Context, intent: Intent) {
                if (intent.action != action || intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID, id) != id) return
                val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
                if (status == PackageInstaller.STATUS_PENDING_USER_ACTION) {
                    @Suppress("DEPRECATION") val confirmation = intent.getParcelableExtra<Intent>(Intent.EXTRA_INTENT)
                    val owner = activity.get()
                    try {
                        check(confirmation != null && owner != null && !owner.isDestroyed && !owner.isFinishing)
                        owner.startActivity(confirmation)
                    } catch (_: Exception) {
                        toast(texts.retry)
                        runCatching { context.packageManager.packageInstaller.abandonSession(id) }
                        finished = true; runCatching { context.unregisterReceiver(this) }
                    }
                } else {
                    toast(if (status == PackageInstaller.STATUS_SUCCESS) texts.success else "${texts.failure}: ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE) ?: "instalación cancelada"}")
                    finished = true; runCatching { context.unregisterReceiver(this) }
                }
            }
        }
        ContextCompat.registerReceiver(context, receiver, IntentFilter(action), ContextCompat.RECEIVER_NOT_EXPORTED)
        val flags = PendingIntent.FLAG_UPDATE_CURRENT or if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0
        val token = PendingIntent.getBroadcast(context, id, Intent(action).setPackage(context.packageName), flags)
        try { session.commit(token.intentSender) }
        catch (e: Exception) { context.unregisterReceiver(receiver); token.cancel(); throw e }
        scope.launch {
            delay(10 * 60 * 1000L)
            if (!finished) { runCatching { context.unregisterReceiver(receiver) }; runCatching { context.packageManager.packageInstaller.abandonSession(id) } }
            token.cancel()
        }
    }

    private fun toast(message: String) = Toast.makeText(context, message, Toast.LENGTH_LONG).show()
}
