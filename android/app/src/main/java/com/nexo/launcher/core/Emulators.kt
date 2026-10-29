package com.nexo.launcher.core

internal data class EmulatorProfile(val packages: List<String>, val activity: String, val notes: List<String> = emptyList())
internal object Emulators {
    private val retro = EmulatorProfile(listOf("com.retroarch.aarch64", "com.retroarch", "com.retroarch.ra32"),
        "com.retroarch.browser.retroactivity.RetroActivityFuture", listOf(
            "Abre RetroArch una vez y concede acceso al almacenamiento. Se recomienda el APK oficial del buildbot.",
            "Instala los núcleos en RetroArch → Online Updater → Core Downloader. Android impide que Nexo compruebe su carpeta privada."))
    fun profile(id: String): EmulatorProfile? = when (id) {
        "Retro" -> retro
        "GBC" -> handheld(false)
        "GBA" -> handheld(true)
        "PS2" -> EmulatorProfile(listOf("com.armsx2", "com.armsx2.nightly"), "com.armsx2.MainActivity", listOf("Importa la BIOS de PS2 en ARMSX2. Configura allí mando y gráficos."))
        "PS3" -> EmulatorProfile(listOf("aenu.aps3e", "aenu.aps3e.premium"), "aenu.aps3e.EmulatorActivity", listOf("Instala el firmware PS3UPDAT.PUP en aPS3e. Emulación experimental; no se garantiza compatibilidad.", "Los juegos-carpeta requieren almacenamiento local accesible por aPS3e."))
        "PSP" -> EmulatorProfile(listOf("org.ppsspp.ppsspp", "org.ppsspp.ppssppgold"), "org.ppsspp.ppsspp.PpssppActivity")
        "GameCube-Wii" -> EmulatorProfile(listOf("org.dolphinemu.dolphinemu"), "org.dolphinemu.dolphinemu.ui.main.MainActivity", listOf("Completa el primer inicio de Dolphin y configura el mando para GameCube o Wii."))
        "Switch" -> EmulatorProfile(listOf("dev.eden.eden_emulator", "dev.legacy.eden_emulator"), "org.yuzu.yuzu_emu.activities.EmulationActivity", listOf("Usa claves y firmware compatibles entre sí. Nexo puede copiarlos mediante el permiso de carpeta de Eden."))
        "Xbox" -> EmulatorProfile(listOf("com.izzy2lost.x1box"), "com.izzy2lost.x1box.LauncherActivity", listOf("Configura MCPX, BIOS flash y disco duro en X1 BOX. Emulación experimental."))
        "Xbox360" -> EmulatorProfile(listOf("aenu.ax360e.free", "aenu.ax360e"), "aenu.ax360e.EmulatorActivity", listOf("Elige antes la carpeta de juegos dentro de aX360e. Emulación experimental; instalación mediante Google Play."))
        else -> null
    }
    private fun handheld(gba: Boolean) = EmulatorProfile(listOf(
        if (gba) "com.explusalpha.GbaEmu" else "com.explusalpha.GbcEmu", "com.sky.SkyEmu",
        if (gba) "it.dbtecno.pizzaboygba" else "it.dbtecno.pizzaboy",
        if (gba) "it.dbtecno.pizzaboygbapro" else "it.dbtecno.pizzaboypro",
        if (gba) "com.fastemulator.gba" else "com.fastemulator.gbc"
    ) + retro.packages, "com.imagine.BaseActivity")

    fun isRetro(pkg: String) = pkg in retro.packages
    fun activity(id: String, pkg: String): String = when {
        isRetro(pkg) -> retro.activity
        pkg.startsWith("com.explusalpha.") -> "com.imagine.BaseActivity"
        pkg == "com.sky.SkyEmu" -> "$pkg.EnhancedNativeActivity"
        pkg.startsWith("it.dbtecno.pizzaboy") -> "$pkg.MainActivity"
        pkg.startsWith("com.fastemulator.") -> "$pkg.EmulatorActivity"
        else -> profile(id)?.activity ?: error("Emulador desconocido.")
    }
    fun label(id: String, pkg: String?): String = when {
        pkg == null -> if (id == "GBC") "GBC.emu" else "GBA.emu"
        isRetro(pkg) -> "RetroArch"
        pkg == "com.sky.SkyEmu" -> "SkyEmu"
        pkg.startsWith("it.dbtecno.pizzaboy") -> if (id == "GBC") "Pizza Boy C" else "Pizza Boy A"
        pkg.startsWith("com.fastemulator.") -> if (id == "GBC") "My OldBoy!" else "My Boy!"
        else -> if (id == "GBC") "GBC.emu" else "GBA.emu"
    }
}
