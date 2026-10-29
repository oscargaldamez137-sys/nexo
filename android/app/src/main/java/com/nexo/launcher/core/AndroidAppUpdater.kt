package com.nexo.launcher.core

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.net.Uri
import android.os.Build
import android.provider.Settings
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.lang.ref.WeakReference

/** Actualización de Nexo en Android: consulta GitHub, descarga Nexo.apk, lo verifica y abre el instalador del sistema. */
internal class AndroidAppUpdater(private val context: Context) {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val sessions = ApkSession(context, scope)

    @Suppress("DEPRECATION")
    private fun installed(): PackageInfo = context.packageManager.getPackageInfo(context.packageName, 0)
    val currentVersion: String get() = installed().versionName ?: "0.0.0"

    suspend fun check(enabled: Boolean = true, manual: Boolean = false): AppUpdate? = AppUpdates.checkIfEnabled(enabled, manual, currentVersion) {
        withTimeoutOrNull(20_000) { withContext(Dispatchers.IO) { release(OfficialHttp.bytes(AppUpdates.RELEASE_URL, OfficialHttp.apkHosts, 2 * 1024 * 1024)) } }
            ?: throw IOException("GitHub no respondió a tiempo.")
    }

    private fun release(bytes: ByteArray): ReleaseInfo {
        val json = JSONObject(String(bytes, Charsets.UTF_8))
        val list = json.optJSONArray("assets")
        val assets = (0 until (list?.length() ?: 0)).mapNotNull { i ->
            val a = list?.optJSONObject(i) ?: return@mapNotNull null
            ReleaseAsset(a.optString("name"), a.optString("browser_download_url"), a.optLong("size", -1),
                a.optString("digest").takeUnless { a.isNull("digest") || it.isBlank() })
        }
        return ReleaseInfo(json.optString("tag_name").takeUnless { it.isBlank() }, json.optBoolean("draft"), json.optBoolean("prerelease"), assets)
    }

    /** Descarga en la caché de la app; reutiliza un APK ya verificado. Comprueba tamaño, SHA-256, paquete y versión. */
    suspend fun download(update: AppUpdate, progress: (Float) -> Unit): File = withContext(Dispatchers.IO) {
        val folder = File(context.cacheDir, "update").apply { mkdirs() }
        val apk = File(folder, "Nexo-${update.version}.apk")
        val job = currentCoroutineContext()
        if (apk.exists() && runCatching { checkApk(apk, update) { job.ensureActive() } }.isSuccess) { progress(1f); return@withContext apk }
        // Solo se guarda el APK de la versión que se va a instalar.
        folder.listFiles()?.forEach { it.delete() }
        val partial = File(folder, "descarga-${update.version}.apk") // .apk: el analizador de Android lo exige
        try {
            var last = 0L
            partial.outputStream().use { out ->
                OfficialHttp.download(update.url, OfficialHttp.apkHosts, AppUpdates.MAX_APK, out) { written, _ ->
                    val now = System.currentTimeMillis()
                    if (now - last > 200) { last = now; progress((written.toFloat() / update.size).coerceIn(0f, 1f)) }
                }
            }
            checkApk(partial, update) { job.ensureActive() }
            if (!partial.renameTo(apk)) throw IOException("No se pudo guardar la actualización.")
            progress(1f)
            apk
        } finally { partial.delete() }
    }

    private fun checkApk(file: File, update: AppUpdate, check: () -> Unit) {
        AppUpdates.verify(file, update.size, update.sha256, check)
        @Suppress("DEPRECATION") val info = context.packageManager.getPackageArchiveInfo(file.absolutePath, 0)
            ?: throw IOException("El archivo descargado no es un APK válido.")
        if (info.packageName != context.packageName) throw IOException("El APK no es de Nexo.")
        @Suppress("DEPRECATION") val code = if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong()
        @Suppress("DEPRECATION") val current = if (Build.VERSION.SDK_INT >= 28) installed().longVersionCode else installed().versionCode.toLong()
        if (code <= current) throw IOException("El APK publicado no es más nuevo que el instalado.")
    }

    /** Android siempre pide confirmación. Sin el permiso «Instalar apps desconocidas», lleva a ese ajuste. */
    suspend fun install(activity: Activity, apk: File): LaunchResult {
        if (!context.packageManager.canRequestPackageInstalls()) return try {
            activity.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")))
            LaunchResult.Failed("Permite «Instalar apps desconocidas» para Nexo y vuelve: la actualización seguirá sola.")
        } catch (_: Exception) { LaunchResult.Failed("Activa «Instalar apps desconocidas» para Nexo en Ajustes de Android.") }
        return try {
            sessions.install(apk, context.packageName, WeakReference(activity), ApkSession.Texts(
                success = "Nexo actualizado.",
                failure = "Android no actualizó Nexo",
                retry = "Vuelve a Nexo para confirmar la actualización."))
            LaunchResult.Started
        } catch (e: kotlinx.coroutines.CancellationException) { throw e }
        catch (e: Exception) { LaunchResult.Failed("No se pudo abrir el instalador de Android: ${e.message ?: "error"}") }
    }

    fun canInstall(): Boolean = context.packageManager.canRequestPackageInstalls()

    /** Enlaces fijos que Nexo abre en el navegador (donar y página de descargas). */
    fun open(activity: Activity, url: String): LaunchResult {
        require(url == DONATE_URL || url == AppUpdates.RELEASES_PAGE || url == AppUpdates.REPO_PAGE) { "Enlace no permitido." }
        return try { activity.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addCategory(Intent.CATEGORY_BROWSABLE)); LaunchResult.Started }
        catch (_: ActivityNotFoundException) { LaunchResult.Failed("No hay un navegador para abrir $url") }
    }

    companion object { const val DONATE_URL = "https://ko-fi.com/nova5emu" }
}
