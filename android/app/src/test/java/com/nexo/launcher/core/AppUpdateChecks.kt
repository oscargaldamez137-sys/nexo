package com.nexo.launcher.core

import kotlinx.coroutines.runBlocking
import java.io.File
import java.io.IOException
import java.security.MessageDigest

/** Actualización automática de Nexo en Android: sin red ni Android (la publicación se inyecta). */
object AppUpdateChecks {
    private var passed = 0
    private fun test(name: String, body: () -> Unit) {
        try { body(); passed++; println("OK $name") }
        catch (e: Throwable) { throw AssertionError("FAILED $name", e) }
    }
    private fun eq(expected: Any?, actual: Any?) { check(expected == actual) { "Expected $expected; got $actual" } }
    private fun rejects(body: () -> Unit) { var failed = false; try { body() } catch (_: Exception) { failed = true }; check(failed) }

    private const val APK_URL = "https://github.com/oscargaldamez137-sys/nexo/releases/download/v1.0.1/Nexo.apk"
    private fun asset(name: String = "Nexo.apk", url: String = APK_URL, size: Long = 100, digest: String? = null) = ReleaseAsset(name, url, size, digest)
    private fun release(tag: String?, vararg assets: ReleaseAsset, prerelease: Boolean = false, draft: Boolean = false) = ReleaseInfo(tag, draft, prerelease, assets.toList())

    @JvmStatic fun main(args: Array<String>) {
        test("versions with and without v") {
            eq(listOf(1, 0, 0), AppUpdates.parse("v1.0.0")?.toList()); eq(listOf(1, 0, 1), AppUpdates.parse("1.0.1")?.toList())
            eq(listOf(2, 3, 0), AppUpdates.parse("V2.3")?.toList())
        }
        test("version order 1.0.0 < 1.0.1 < 1.1.0 < 1.10.0 < 2.0.0") {
            val order = listOf("v1.0.0", "1.0.1", "v1.1.0", "1.10.0", "v2.0.0").map { AppUpdates.parse(it)!! }
            for (i in 0 until order.size - 1) { check(AppUpdates.newer(order[i + 1], order[i])); check(!AppUpdates.newer(order[i], order[i + 1])) }
            check(!AppUpdates.newer(order[0], AppUpdates.parse("1.0")!!))
        }
        test("invalid tags ignored") { for (bad in listOf(null, "", "latest", "v", "1", "1.0.0-beta", "1.0.0.0", "1.0.x", "1.100.0", "v 1.0.0")) eq(null, AppUpdates.parse(bad)) }
        test("versionCode rule of build.gradle.kts") {
            eq(10000, AppUpdates.versionCode(intArrayOf(1, 0, 0))); eq(10001, AppUpdates.versionCode(intArrayOf(1, 0, 1)))
            eq(10200, AppUpdates.versionCode(intArrayOf(1, 2, 0))); check(AppUpdates.versionCode(intArrayOf(1, 0, 0)) > 1)
        }
        test("selects Nexo.apk among the release files") {
            val sha = "A".repeat(64)
            val update = AppUpdates.select(release("v1.0.1", asset("Instalar-Nexo.exe", APK_URL.replace("Nexo.apk", "Instalar-Nexo.exe"), 999), asset(size = 1234, digest = "sha256:$sha")), "1.0.0")
            eq(AppUpdate("1.0.1", APK_URL, 1234, sha.lowercase()), update)
            eq(null, AppUpdates.select(release("v1.1.0", asset(digest = null)), "1.0.0")?.sha256)
        }
        test("same, older, draft and prerelease are not updates") {
            eq(null, AppUpdates.select(release("v1.0.0", asset()), "1.0.0"))
            eq(null, AppUpdates.select(release("v0.9.0", asset()), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(), prerelease = true), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(), draft = true), "1.0.0"))
            eq(null, AppUpdates.select(release("nightly", asset()), "1.0.0"))
        }
        test("missing asset, foreign link, bad size or digest rejected") {
            eq(null, AppUpdates.select(release("v1.0.1"), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset("Instalar-Nexo.exe")), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(url = "https://github.com/otro/nexo/releases/download/v1.0.1/Nexo.apk")), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(url = "http://github.com/oscargaldamez137-sys/nexo/releases/download/v1.0.1/Nexo.apk")), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(size = 0)), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(size = AppUpdates.MAX_APK + 1)), "1.0.0"))
            eq(null, AppUpdates.select(release("v1.0.1", asset(digest = "md5:abc")), "1.0.0"))
        }
        test("no network or broken release returns null") {
            eq(null, runBlocking { AppUpdates.check("1.0.0") { throw IOException("sin red") } })
            eq(null, runBlocking { AppUpdates.check("1.0.0") { throw IllegalStateException("JSON roto") } })
            eq(AppUpdate("1.0.1", APK_URL, 100, null), runBlocking { AppUpdates.check("1.0.0") { release("v1.0.1", asset()) } })
        }
        test("size and SHA-256 verification") {
            val file = File.createTempFile("nexo-update-", ".apk")
            try {
                val data = "apk de prueba".toByteArray(); file.writeBytes(data)
                val sha = MessageDigest.getInstance("SHA-256").digest(data).joinToString("") { "%02x".format(it.toInt() and 255) }
                AppUpdates.verify(file, data.size.toLong(), sha)
                AppUpdates.verify(file, data.size.toLong(), null)
                rejects { AppUpdates.verify(file, data.size + 1L, sha) }
                rejects { AppUpdates.verify(file, data.size.toLong(), "0".repeat(64)) }
            } finally { file.delete() }
        }
        test("GitHub download hosts allowed") {
            for (url in listOf("https://api.github.com/repos/oscargaldamez137-sys/nexo/releases/latest", APK_URL, "https://release-assets.githubusercontent.com/x", "https://objects.githubusercontent.com/x"))
                OfficialHttp.validate(url, OfficialHttp.apkHosts)
        }
        println("$passed/$passed pruebas de actualización OK; sin red ni Android.")
    }
}
