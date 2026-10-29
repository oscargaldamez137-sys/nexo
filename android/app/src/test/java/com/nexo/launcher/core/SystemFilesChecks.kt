package com.nexo.launcher.core

import java.io.*
import java.nio.file.Files
import java.security.MessageDigest
import java.util.concurrent.CancellationException
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

object SystemFilesChecks {
    private var passed = 0
    private fun test(name: String, f: () -> Unit) { try { f(); passed++; println("OK setup: $name") } catch (e: Throwable) { throw AssertionError(name, e) } }
    private fun rejects(f: () -> Unit): Exception { try { f() } catch (e: Exception) { return e }; error("Expected failure") }
    @JvmStatic fun main(args: Array<String>) {
        val parent = File("build/system-file-tests").apply { mkdirs() }
        val root = Files.createTempDirectory(parent.toPath(), "check-").toFile()
        try {
            test("dedicated recommendations") { check(Emulators.profile("GBC")!!.packages.first() == "com.explusalpha.GbcEmu"); check(Emulators.profile("GBA")!!.packages.first() == "com.explusalpha.GbaEmu") }
            test("fallback priority") { check(Emulators.profile("GBA")!!.packages == listOf("com.explusalpha.GbaEmu", "com.sky.SkyEmu", "it.dbtecno.pizzaboygba", "it.dbtecno.pizzaboygbapro", "com.fastemulator.gba", "com.retroarch.aarch64", "com.retroarch", "com.retroarch.ra32")) }
            test("dedicated launch activities") { check(Emulators.activity("GBC", "com.explusalpha.GbcEmu") == "com.imagine.BaseActivity"); check(Emulators.activity("GBA", "com.sky.SkyEmu") == "com.sky.SkyEmu.EnhancedNativeActivity") }
            test("alternative activities") { check(Emulators.activity("GBA", "it.dbtecno.pizzaboygba") == "it.dbtecno.pizzaboygba.MainActivity"); check(Emulators.activity("GBC", "com.fastemulator.gbc") == "com.fastemulator.gbc.EmulatorActivity"); check(Emulators.activity("GBA", "com.retroarch") == "com.retroarch.browser.retroactivity.RetroActivityFuture") }
            test("fallback labels") { check(Emulators.label("GBC", "com.retroarch") == "RetroArch"); check(Emulators.label("GBA", "com.sky.SkyEmu") == "SkyEmu"); check(!Emulators.isRetro("com.explusalpha.GbaEmu")) }
            test("dedicated extensions") { check(GameFormats.extensions("GBC") == setOf("gb", "gbc", "dmg")); check(GameFormats.extensions("GBA") == setOf("gba", "mb")) }
            test("fixed official tagged release and bundles") { check(ApkBundles.RELEASE_URL.endsWith("/releases/tags/Pre-release")); check(ApkBundles.asset("GBC") == "GbcEmu.zip"); check(ApkBundles.asset("GBA") == "GbaEmu.zip") }
            val zip = File(root, "bundle.zip")
            fun bundle(vararg names: String) { ZipOutputStream(zip.outputStream()).use { out -> names.forEach { out.putNextEntry(ZipEntry(it)); out.write("example apk".toByteArray()); out.closeEntry() } } }
            test("bundle checksum applies to ZIP before extraction") {
                bundle("GbaEmu-12345678.apk")
                val digest = MessageDigest.getInstance("SHA-256").digest(zip.readBytes()).joinToString("") { "%02x".format(it.toInt() and 255) }
                ApkBundles.verify(zip, digest) {}; rejects { ApkBundles.verify(zip, "0".repeat(64)) {} }
                val apk = File(root, "result.apk"); ApkBundles.extract(zip, apk) {}; check(apk.readText() == "example apk")
            }
            test("multiple APKs rejected") { bundle("first.apk", "second.apk"); val apk = File(root, "duplicate.apk"); rejects { ApkBundles.extract(zip, apk) {} }; check(!apk.exists()) }
            test("APK path traversal rejected") { bundle("../escape.apk"); val apk = File(root, "bad.apk"); rejects { ApkBundles.extract(zip, apk) {} }; check(!apk.exists() && !File(root, "escape.apk").exists()) }
            test("APK cancellation keeps source, removes partial output") { bundle("first.apk"); val apk = File(root, "cancel.apk"); check(rejects { ApkBundles.extract(zip, apk) { throw CancellationException() } } is CancellationException); check(zip.exists() && !apk.exists()) }
            test("missing system files") { check(SystemFilesLogic.missing("PS2", emptyList()) != null); check(SystemFilesLogic.missing("PS3", listOf("other.pup")) != null); check(SystemFilesLogic.missing("Switch", listOf("title.keys")) != null) }
            test("case-insensitive supplied system files") { check(SystemFilesLogic.missing("PS2", listOf("SCPH.BIN")) == null); check(SystemFilesLogic.missing("PS3", listOf("PS3UPDAT.PUP")) == null); check(SystemFilesLogic.missing("Switch", listOf("PROD.KEYS")) == null) }
            test("Xbox requires three separate kinds") { check(SystemFilesLogic.missing("Xbox", listOf("mcpx_1.0.bin"))!!.contains("BIOS flash")); check(SystemFilesLogic.missing("Xbox", listOf("mcpx_1.0.bin", "flash.bin", "hdd.qcow2")) == null) }
            test("GBC/GBA do not require BIOS") { for (id in listOf("GBA", "GBC", "PSP")) check(SystemFilesLogic.missing(id, emptyList()) == null) }
            val keys = ("# User supplied\nheader_key = " + "ab".repeat(32) + "\nmaster_key_00 = " + "01".repeat(16)).toByteArray()
            test("prod.keys valid") { SystemFilesLogic.validateKeys("prod.keys", keys) }
            test("prod.keys missing header_key") { rejects { SystemFilesLogic.validateKeys("prod.keys", "master_key_00 = ${"ab".repeat(16)}".toByteArray()) } }
            test("HTML or malformed keys rejected") { rejects { SystemFilesLogic.validateKeys("prod.keys", "<html>download error</html>".toByteArray()) }; rejects { SystemFilesLogic.validateKeys("title.keys", "id=xyz".toByteArray()) } }
            test("title.keys valid") { SystemFilesLogic.validateKeys("title.keys", ("01".repeat(16) + " = " + "ab".repeat(16)).toByteArray()) }
            test("empty optional title.keys does not block firmware") { SystemFilesLogic.validateKeys("title.keys", byteArrayOf()); rejects { SystemFilesLogic.validateKeys("prod.keys", byteArrayOf()) } }
            val name = "a".repeat(32) + ".nca"
            test("firmware NCA paths flattened deliberately") { check(SystemFilesLogic.firmwareEntries(listOf(PackedEntry("Firmware/$name", false, 4096))) == listOf(0 to name)) }
            test("firmware traversal and links rejected") { rejects { SystemFilesLogic.firmwareEntries(listOf(PackedEntry("../$name", false, 4096))) }; rejects { SystemFilesLogic.firmwareEntries(listOf(PackedEntry(name, false, 4096, true))) } }
            test("duplicate NCA basename rejected") { rejects { SystemFilesLogic.firmwareEntries(listOf(PackedEntry("one/$name", false, 4096), PackedEntry("two/$name", false, 4096))) } }
            test("firmware empty, invalid names, sizes rejected") { rejects { SystemFilesLogic.firmwareEntries(emptyList()) }; rejects { SystemFilesLogic.firmwareEntries(listOf(PackedEntry("random.nca", false, 4096))) }; rejects { SystemFilesLogic.firmwareEntries(listOf(PackedEntry(name, false, 12))) } }
            val source = File(root, "prod.keys").apply { writeText("new") }
            val nca = File(root, "new.nca").apply { writeBytes(ByteArray(2048) { 7 }) }
            val plan = linkedMapOf("keys/prod.keys" to source, "nand/system/Contents/registered/$name" to nca)
            fun backups(label: String) = File(root, label).apply { mkdir() }
            test("existing keys truncated in place, not renamed") {
                val store = MemoryStore(); store.files["keys/prod.keys"] = "old and much longer".toByteArray()
                SystemFileTransaction.install(plan, store, backups("success"), {}, { _, _ -> })
                check(store.files["keys/prod.keys"]!!.decodeToString() == "new"); check(store.created == listOf("nand/system/Contents/registered/$name")); check(source.readText() == "new")
            }
            test("cancel restores old keys and deletes new NCA") {
                val store = MemoryStore(); store.files["keys/prod.keys"] = "old".toByteArray(); var cancel = false
                check(rejects { SystemFileTransaction.install(plan, store, backups("cancel"), { if (cancel) throw CancellationException() }, { n, _ -> if (n > 3) cancel = true }) } is CancellationException)
                check(store.files.size == 1 && store.files["keys/prod.keys"]!!.decodeToString() == "old"); check(source.exists())
            }
            test("write failure rolls back all writes") {
                val store = MemoryStore(); store.files["keys/prod.keys"] = "before".toByteArray(); store.failNca = true
                rejects { SystemFileTransaction.install(plan, store, backups("failure"), {}, { _, _ -> }) }
                check(store.files.size == 1 && store.files["keys/prod.keys"]!!.decodeToString() == "before")
            }
            test("cancel before modifications preserves store") {
                val store = MemoryStore(); store.files["keys/prod.keys"] = "before".toByteArray()
                rejects { SystemFileTransaction.install(plan, store, backups("early"), { throw CancellationException() }, { _, _ -> }) }
                check(store.created.isEmpty() && store.files["keys/prod.keys"]!!.decodeToString() == "before")
            }
            println("System file checks: $passed/$passed OK")
        } finally { root.deleteRecursively() }
    }
    private class MemoryStore : SystemFileStore<String> {
        val files = linkedMapOf<String, ByteArray>(); val created = ArrayList<String>(); var failNca = false
        override fun find(path: String) = path.takeIf { it in files }
        override fun create(path: String): String { check(path !in files); files[path] = byteArrayOf(); created += path; return path }
        override fun read(document: String): InputStream = ByteArrayInputStream(files.getValue(document))
        override fun write(document: String): OutputStream {
            files[document] = byteArrayOf()
            return object : ByteArrayOutputStream() {
                override fun write(b: ByteArray, off: Int, len: Int) { if (failNca && document.endsWith(".nca")) throw IOException("No space"); super.write(b, off, len) }
                override fun close() { files[document] = toByteArray(); super.close() }
            }
        }
        override fun delete(document: String) { files.remove(document) }
    }
}
