package com.nexo.launcher.core

import kotlinx.coroutines.runBlocking

/** Opciones, carpeta de la biblioteca y juegos de Switch (1.1.0): sin Android ni red. */
object LibraryChecks {
    private var passed = 0
    private fun test(name: String, body: () -> Unit) {
        try { body(); passed++; println("OK $name") }
        catch (e: Throwable) { throw AssertionError("FAILED $name", e) }
    }
    private fun eq(expected: Any?, actual: Any?) { check(expected == actual) { "Expected $expected; got $actual" } }

    private class MapStore : SettingsStore {
        val values = HashMap<String, Boolean>()
        override fun getBoolean(key: String, default: Boolean) = values[key] ?: default
        override fun putBoolean(key: String, value: Boolean): Boolean { values[key] = value; return true }
    }

    private val ids = Catalog.systems.map { it.id }
    private fun dirs(vararg names: String) = names.map { it to true }

    @JvmStatic fun main(args: Array<String>) {
        test("options default to 1.0.0 behaviour and persist") {
            val store = MapStore(); val settings = NexoSettings(store)
            check(settings.checkUpdates && settings.downloadArtwork && !settings.hideSwitchKeysNotice)
            settings.checkUpdates = false; settings.downloadArtwork = false; settings.hideSwitchKeysNotice = true
            val again = NexoSettings(store)
            check(!again.checkUpdates && !again.downloadArtwork && again.hideSwitchKeysNotice)
            // Same key that AndroidArtwork already read in 1.0.0.
            eq(false, store.values["downloadArtwork"])
        }
        test("automatic update check makes no request when disabled; manual always asks") {
            var calls = 0
            val fetch: suspend () -> ReleaseInfo = { calls++; ReleaseInfo("v1.0.0", false, false, emptyList()) }
            eq(null, runBlocking { AppUpdates.checkIfEnabled(false, false, "1.1.0", fetch) }); eq(0, calls)
            runBlocking { AppUpdates.checkIfEnabled(true, false, "1.1.0", fetch) }; eq(1, calls)
            runBlocking { AppUpdates.checkIfEnabled(false, true, "1.1.0", fetch) }; eq(2, calls)
        }
        test("artwork is never downloaded with the option off") {
            check(!ArtworkPolicy.shouldDownload(false, false, 10, 0))
            check(ArtworkPolicy.shouldDownload(true, false, 10, 0))
            check(!ArtworkPolicy.shouldDownload(true, true, 10, 0))
            check(!ArtworkPolicy.shouldDownload(true, false, 10, 11))
        }
        test("library: existing Juegos keeps working exactly as before") {
            check(!LibraryLayout.useRootAsGames("Nexo", dirs("Juegos", "BIOS"), ids))
            check(!LibraryLayout.useRootAsGames("Juegos", dirs("Juegos", "Switch"), ids))
            check(!LibraryLayout.useRootAsGames("Nexo", emptyList(), ids))
            check(!LibraryLayout.useRootAsGames("Nexo", listOf("Switch" to false), ids)) // a file called Switch is not a folder
        }
        test("library: choosing your own games folder (named Juegos or with console folders) finds the games") {
            check(LibraryLayout.useRootAsGames("Juegos", emptyList(), ids))
            check(LibraryLayout.useRootAsGames("juegos ", dirs("switch"), ids))
            check(LibraryLayout.useRootAsGames("Emulación", dirs("Download", "switch"), ids))
            check(LibraryLayout.useRootAsGames("ROMs", dirs("Nintendo Switch"), ids))
            eq("switch", LibraryLayout.systemFolder("Switch", dirs("PS2", "switch")))
            eq("Nintendo Switch", LibraryLayout.systemFolder("Switch", dirs("Nintendo Switch")))
            eq(null, LibraryLayout.systemFolder("PS2", dirs("Switch")))
        }
        test("Switch games are listed whatever the keys: accents, upper case, subfolders, more than 4 GB") {
            for (name in listOf("Pokémon Sword Base NSP.nsp", "Pokémon Brilliant Diamond Base.nsp", "ZELDA.XCI", "homebrew.nro", "x.nca"))
                eq(ShelfRules.Kind.Game, ShelfRules.kind("Switch", name))
            eq("Pokémon Sword Base NSP", GameFormats.title("Pokémon Sword Base NSP.nsp"))
            // Size never decides (ShelfRules.kind has no size input): a 9.5 GB NSP is a normal Game on the shelf,
            // and sizes above 4 GiB are kept as Long end to end.
            val size: Long = 10_236_752_952L
            check(size > 4L * 1024 * 1024 * 1024 && ShelfRules.kind("Switch", "Pokémon Sword Base NSP.nsp") == ShelfRules.Kind.Game)
            eq(ShelfRules.Kind.Archive, ShelfRules.kind("Switch", "juego.zip"))
            eq(ShelfRules.Kind.Ignored, ShelfRules.kind("Switch", "LEEME.txt"))
        }
        test("NSZ/XCZ are shown as pending with an explanation, never silently hidden") {
            eq(ShelfRules.Kind.Unsupported, ShelfRules.kind("Switch", "Pokémon.nsz"))
            eq(ShelfRules.Kind.Unsupported, ShelfRules.kind("Switch", "Juego.XCZ"))
            check(SwitchNotice.unsupported("Switch", "a.nsz")!!.contains("NSP"))
            eq(null, SwitchNotice.unsupported("PS2", "a.nsz"))
        }
        test("keys notice is non-blocking, can be hidden and disappears when keys are visible") {
            val text = SwitchNotice.keys(keysVisible = false, hidden = false, path = "Nexo/BIOS/Switch")!!
            check("puedes ignorar este aviso" in text && "se abren igual" in text)
            eq(null, SwitchNotice.keys(keysVisible = true, hidden = false, path = "x"))
            eq(null, SwitchNotice.keys(keysVisible = false, hidden = true, path = "x"))
            val notes = listOf("Usa claves y firmware compatibles entre sí.", text, "Otra nota")
            eq(notes, SwitchNotice.visibleNotes(notes, hidden = false))
            eq(listOf("Otra nota"), SwitchNotice.visibleNotes(notes, hidden = true))
        }
        println("$passed/$passed pruebas de biblioteca y opciones OK; sin red ni Android.")
    }
}
