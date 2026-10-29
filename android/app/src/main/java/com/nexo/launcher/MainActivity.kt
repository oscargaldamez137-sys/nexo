package com.nexo.launcher

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.hardware.input.InputManager
import android.os.Bundle
import android.os.SystemClock
import android.view.InputDevice
import android.view.KeyEvent
import android.view.MotionEvent
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.activity.viewModels
import androidx.lifecycle.lifecycleScope
import androidx.compose.runtime.mutableStateOf
import androidx.core.view.WindowCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.WindowInsetsControllerCompat
import com.nexo.launcher.ui.NexoApp
import com.nexo.launcher.ui.NexoViewModel
import com.nexo.launcher.ui.OptionItem
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlin.math.abs

class MainActivity : ComponentActivity(), InputManager.InputDeviceListener {
    private val vm: NexoViewModel by viewModels()
    private val padConnected = mutableStateOf(false)
    private var stickHeld = 0
    private var verticalHeld = 0
    private var stickRepeat: Job? = null
    private var inputBlockedUntil = 0L
    private val navigationKeys = setOf(KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT,
        KeyEvent.KEYCODE_DPAD_UP, KeyEvent.KEYCODE_DPAD_DOWN, KeyEvent.KEYCODE_BUTTON_A,
        KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER, KeyEvent.KEYCODE_BUTTON_START,
        KeyEvent.KEYCODE_BUTTON_B, KeyEvent.KEYCODE_BUTTON_L1, KeyEvent.KEYCODE_BUTTON_R1, KeyEvent.KEYCODE_ESCAPE,
        KeyEvent.KEYCODE_BUTTON_Y, KeyEvent.KEYCODE_BUTTON_X, KeyEvent.KEYCODE_BUTTON_SELECT, KeyEvent.KEYCODE_MENU)

    // El instalador del sistema termina cuando Nexo ya volvió al frente: se escucha el aviso de app instalada.
    private val packages = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            vm.refreshStatuses()
            if (intent.action == Intent.ACTION_PACKAGE_ADDED) {
                val name = intent.data?.schemeSpecificPart
                vm.statuses.values.firstOrNull { it.packageName == name }?.let { vm.show("${it.label} instalado. Ya puedes jugar.") }
            }
        }
    }

    // Permiso único sobre la carpeta de Eden (su propio proveedor de documentos) para copiar claves y firmware.
    private val edenGrant = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        val uri = result.data?.data
        if (result.resultCode == RESULT_OK && uri != null) vm.onEdenGranted(uri)
        else vm.show("Sin permiso sobre la carpeta de Eden no puedo copiar las claves. Vuelve a pulsar X y elige «Eden».", error = true)
    }

    private fun requestEdenGrant() {
        vm.show("Elige la carpeta «Eden» en el selector y pulsa «Usar esta carpeta».")
        try { edenGrant.launch(vm.core.edenGrantIntent()) }
        catch (e: Exception) { vm.show("No pude abrir el selector: ${e.message}", error = true) }
    }

    private val pickLibrary = registerForActivityResult(ActivityResultContracts.OpenDocumentTree()) { tree ->
        if (tree != null) vm.setLibrary(tree)
    }

    // El selector empieza en la biblioteca actual al cambiarla desde la barra superior u Opciones.
    private fun pickLibrary() {
        try { pickLibrary.launch(vm.libraryRoot) }
        catch (e: Exception) { vm.show("No pude abrir el selector de carpetas: ${e.message}", error = true) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        immersive()
        getSystemService(InputManager::class.java).registerInputDeviceListener(this, null)
        padConnected.value = hasGamepad()
        // Exclusivamente broadcasts protegidos del sistema: Android 14+ permite omitir flags.
        registerReceiver(packages, IntentFilter().apply {
            addAction(Intent.ACTION_PACKAGE_ADDED); addAction(Intent.ACTION_PACKAGE_REMOVED); addAction(Intent.ACTION_PACKAGE_REPLACED)
            addDataScheme("package")
        })
        setContent {
            NexoApp(vm, padConnected.value, onPickLibrary = { pickLibrary() }, onEdenGrant = { requestEdenGrant() })
        }
    }

    override fun onResume() {
        super.onResume()
        immersive()
        // Al volver de un juego: el emulador o la biblioteca pueden haber cambiado.
        vm.refreshStatuses()
        vm.rescan()
        // Al volver del ajuste «Instalar apps desconocidas» (o de un juego), la actualización descargada sigue.
        vm.resumeUpdate(this)
    }

    override fun onDestroy() {
        resetStick()
        getSystemService(InputManager::class.java).unregisterInputDeviceListener(this)
        unregisterReceiver(packages)
        super.onDestroy()
    }

    private fun immersive() {
        WindowCompat.setDecorFitsSystemWindows(window, false)
        WindowInsetsControllerCompat(window, window.decorView).apply {
            hide(WindowInsetsCompat.Type.systemBars())
            systemBarsBehavior = WindowInsetsControllerCompat.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE
        }
    }

    // ------------------------------------------------------------------ Mando

    // Se intercepta antes que Compose: la vista enfocada consumiría la cruceta para mover el foco.
    override fun dispatchKeyEvent(event: KeyEvent): Boolean {
        // El selector inicial debe poder recibir Enter/A y el foco de Compose.
        if (vm.libraryRoot == null) return super.dispatchKeyEvent(event)
        if (event.keyCode in navigationKeys) {
            if (event.action == KeyEvent.ACTION_DOWN && hasWindowFocus() && SystemClock.uptimeMillis() >= inputBlockedUntil)
                handleKey(event.keyCode, event)
            // Consumir también UP: no entregar a un clickable una liberación sin su DOWN.
            return true
        }
        return super.dispatchKeyEvent(event)
    }

    private fun handleKey(keyCode: Int, event: KeyEvent): Boolean {
        // Guía de preparación: A abre el emulador, B cierra.
        vm.guide?.let {
            if (event.repeatCount == 0) when (keyCode) {
                KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> vm.openGuideEmulator(this)
                KeyEvent.KEYCODE_BUTTON_B, KeyEvent.KEYCODE_ESCAPE -> vm.closeGuide()
                KeyEvent.KEYCODE_BUTTON_Y -> if (it.keysNotice) vm.hideSwitchKeysNotice()
            }
            return true
        }
        // Opciones: ▲▼ elegir, Ⓐ cambiar, ◀▶ también cambian los interruptores, Ⓑ/Start/Menú cierran.
        if (vm.optionsOpen) {
            if (event.repeatCount == 0 || keyCode == KeyEvent.KEYCODE_DPAD_UP || keyCode == KeyEvent.KEYCODE_DPAD_DOWN) when (keyCode) {
                KeyEvent.KEYCODE_DPAD_UP -> vm.moveOption(-1)
                KeyEvent.KEYCODE_DPAD_DOWN -> vm.moveOption(1)
                KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> vm.activateOption(vm.options[vm.optionIndex], this) { pickLibrary() }
                KeyEvent.KEYCODE_DPAD_LEFT, KeyEvent.KEYCODE_DPAD_RIGHT -> {
                    val item = vm.options[vm.optionIndex]
                    if (item == OptionItem.Artwork || item == OptionItem.AutoUpdate || item == OptionItem.SwitchNotice) vm.activateOption(item, this) { pickLibrary() }
                }
                KeyEvent.KEYCODE_BUTTON_B, KeyEvent.KEYCODE_ESCAPE, KeyEvent.KEYCODE_BUTTON_START, KeyEvent.KEYCODE_MENU -> vm.closeOptions()
            }
            return true
        }
        // «¿Borrar el comprimido?»: A/B conservan (lo seguro) y solo Y borra.
        if (vm.pendingDelete != null) {
            if (event.repeatCount == 0) when (keyCode) {
                KeyEvent.KEYCODE_BUTTON_Y -> vm.confirmDelete(true)
                KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER,
                KeyEvent.KEYCODE_BUTTON_B, KeyEvent.KEYCODE_ESCAPE -> vm.confirmDelete(false)
            }
            return true
        }
        return when (keyCode) {
            KeyEvent.KEYCODE_DPAD_LEFT -> { left(); true }
            KeyEvent.KEYCODE_DPAD_RIGHT -> { right(); true }
            KeyEvent.KEYCODE_DPAD_DOWN -> { vm.enterGames(); true }
            KeyEvent.KEYCODE_DPAD_UP -> { vm.exitGames(); true }
            KeyEvent.KEYCODE_BUTTON_A, KeyEvent.KEYCODE_DPAD_CENTER, KeyEvent.KEYCODE_ENTER -> {
                if (event.repeatCount == 0) vm.primary(this); true
            }
            // Start (o Menú) abre Opciones; Ⓐ sigue siendo la acción principal.
            KeyEvent.KEYCODE_BUTTON_START, KeyEvent.KEYCODE_MENU -> { if (event.repeatCount == 0) vm.openOptions(); true }
            KeyEvent.KEYCODE_BUTTON_B, KeyEvent.KEYCODE_ESCAPE -> { if (vm.gamesMode) vm.exitGames(); true }
            KeyEvent.KEYCODE_BUTTON_X -> { if (!vm.gamesMode && event.repeatCount == 0) vm.systemFiles { requestEdenGrant() }; true }
            KeyEvent.KEYCODE_BUTTON_L1 -> { vm.step(-1); true }
            KeyEvent.KEYCODE_BUTTON_R1 -> { vm.step(1); true }
            KeyEvent.KEYCODE_BUTTON_Y -> { if (event.repeatCount == 0 && vm.update?.ready == true) vm.installUpdate(this); true }
            KeyEvent.KEYCODE_BUTTON_SELECT -> { if (event.repeatCount == 0) vm.donate(this); true }
            else -> false
        }
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        resetStick()
        if (hasFocus) inputBlockedUntil = SystemClock.uptimeMillis() + 700
    }

    private fun resetStick() {
        stickRepeat?.cancel(); stickRepeat = null
        stickHeld = 0; verticalHeld = 0
    }

    /** Stick izquierdo: se mueve como la cruceta, con repetición al mantenerlo. */
    override fun dispatchGenericMotionEvent(event: MotionEvent): Boolean {
        if (event.source and InputDevice.SOURCE_JOYSTICK == InputDevice.SOURCE_JOYSTICK && event.action == MotionEvent.ACTION_MOVE) {
            if (vm.libraryRoot == null) return super.dispatchGenericMotionEvent(event)
            if (!hasWindowFocus() || SystemClock.uptimeMillis() < inputBlockedUntil) return true
            val x = event.getAxisValue(MotionEvent.AXIS_X).let { if (abs(it) < 0.6f) event.getAxisValue(MotionEvent.AXIS_HAT_X) else it }
            val y = event.getAxisValue(MotionEvent.AXIS_Y).let { if (abs(it) < 0.6f) event.getAxisValue(MotionEvent.AXIS_HAT_Y) else it }
            val dir = when { x < -0.6f -> -1; x > 0.6f -> 1; else -> 0 }
            if (dir != stickHeld) {
                stickRepeat?.cancel()
                stickHeld = dir
                if (dir != 0) {
                    if (dir < 0) left() else right()
                    // Un stick inmóvil no tiene por qué emitir más ACTION_MOVE.
                    stickRepeat = lifecycleScope.launch {
                        delay(380)
                        while (hasWindowFocus() && stickHeld == dir) {
                            if (dir < 0) left() else right()
                            delay(140)
                        }
                    }
                }
            }
            val vertical = when { y > 0.7f -> 1; y < -0.7f -> -1; else -> 0 }
            if (vertical != verticalHeld) {
                if (vm.optionsOpen) { if (vertical != 0) vm.moveOption(vertical) }
                else {
                    if (vertical > 0) vm.enterGames()
                    if (vertical < 0) vm.exitGames()
                }
                verticalHeld = vertical
            }
            return true
        }
        return super.dispatchGenericMotionEvent(event)
    }

    private fun left() { if (vm.optionsOpen || vm.guide != null || vm.pendingDelete != null) return; if (vm.gamesMode) vm.stepGame(-1) else vm.step(-1) }
    private fun right() { if (vm.optionsOpen || vm.guide != null || vm.pendingDelete != null) return; if (vm.gamesMode) vm.stepGame(1) else vm.step(1) }

    private fun hasGamepad() = InputDevice.getDeviceIds().any { id ->
        val sources = InputDevice.getDevice(id)?.sources ?: 0
        sources and InputDevice.SOURCE_GAMEPAD == InputDevice.SOURCE_GAMEPAD || sources and InputDevice.SOURCE_JOYSTICK == InputDevice.SOURCE_JOYSTICK
    }

    override fun onInputDeviceAdded(deviceId: Int) {
        val was = padConnected.value
        padConnected.value = hasGamepad()
        if (!was && padConnected.value) vm.show("Mando conectado: muévete con la cruceta o el stick y pulsa A.")
    }

    override fun onInputDeviceRemoved(deviceId: Int) { resetStick(); padConnected.value = hasGamepad() }
    override fun onInputDeviceChanged(deviceId: Int) { padConnected.value = hasGamepad() }
}
