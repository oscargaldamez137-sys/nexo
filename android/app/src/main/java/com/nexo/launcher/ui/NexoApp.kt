package com.nexo.launcher.ui

import android.app.Activity
import androidx.activity.compose.BackHandler
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.Crossfade
import androidx.compose.animation.animateColorAsState
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.key
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.zIndex
import coil3.compose.AsyncImage
import com.nexo.launcher.core.ConsoleSystem
import com.nexo.launcher.core.EmulatorState
import com.nexo.launcher.core.Game
import kotlinx.coroutines.delay
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.floor

@Composable
fun NexoApp(vm: NexoViewModel, padConnected: Boolean, onPickLibrary: () -> Unit, onEdenGrant: () -> Unit) {
    val activity = LocalContext.current as Activity
    BackHandler(enabled = vm.gamesMode) { vm.exitGames() }
    BoxWithConstraints(Modifier.fillMaxSize().background(Ink.Background)) {
        // Unidad de diseño: la pantalla de referencia es un móvil horizontal de 360 dp de alto.
        val unit = minOf(maxHeight.value / 360f, maxWidth.value / 640f).coerceIn(0.5f, 2.2f).dp
        val focus by animateFloatAsState(if (vm.gamesMode) 1f else 0f, spring(stiffness = 220f), label = "focus")

        Backdrop(vm, focus)

        if (vm.libraryRoot == null) LibraryPrompt(unit, onPickLibrary)
        else Column(Modifier.fillMaxSize()) {
            TopBar(vm, activity, padConnected, unit)
            // El carrusel se recoge al entrar en la estantería, como en PC.
            Box(Modifier.fillMaxWidth().height(unit * (128f - 100f * focus)).zIndex(-1f)) {
                Carousel(vm, unit, focus)
            }
            InfoPanel(vm, activity, unit, Modifier.weight(1f), onEdenGrant)
            key(vm.current.id) { Shelf(vm, activity, unit, focus) }
            Hints(vm, padConnected, unit)
        }

        // Descarga lista: Android pide confirmar la instalación (solo con Nexo en primer plano).
        LaunchedEffect(vm.update?.ready) { if (vm.update?.ready == true) vm.resumeUpdate(activity) }

        vm.pendingDelete?.let { DeletePrompt(vm, it, unit) }
        vm.guide?.let { GuideDialog(vm, it, activity, unit) }
        ToastHost(vm, unit)
    }
}

// ---------------------------------------------------------------------------- Fondo

@Composable
private fun Backdrop(vm: NexoViewModel, focus: Float) {
    val system = vm.current
    val primary by animateColorAsState(color(system.primary), tween(600), label = "primary")
    val secondary by animateColorAsState(color(system.secondary), tween(600), label = "secondary")
    Box(Modifier.fillMaxSize()) {
        // Auroras con los colores de la consola.
        Box(Modifier.fillMaxSize().background(Brush.radialGradient(listOf(primary.alpha(0.55f), Color.Transparent), radius = 1400f, center = androidx.compose.ui.geometry.Offset(0f, 0f))))
        Box(Modifier.fillMaxSize().background(Brush.radialGradient(listOf(secondary.alpha(0.6f), Color.Transparent), radius = 1600f, center = androidx.compose.ui.geometry.Offset(2400f, 1400f))))
        // Arte del juego seleccionado, nítido; si no hay fondo HD, su portada a la derecha.
        val game = (vm.selectedItem as? ShelfItem.GameItem)?.game
        Crossfade(targetState = game?.let { it.background ?: it.cover?.let { c -> "hero:$c" } }, animationSpec = tween(650), label = "art") { art ->
            if (art != null) {
                Box(Modifier.fillMaxSize()) {
                    if (art.startsWith("hero:")) {
                        // Sin fondo HD: la portada nítida a la derecha, sin tapar la ficha.
                        AsyncImage(art.removePrefix("hero:"), null,
                            Modifier.align(Alignment.CenterEnd).fillMaxWidth(0.3f).fillMaxHeight(0.62f).padding(end = 36.dp),
                            contentScale = ContentScale.Fit, alignment = Alignment.CenterEnd)
                        Box(Modifier.fillMaxSize().background(Brush.horizontalGradient(0f to Ink.Background.alpha(0.6f), 0.55f to Ink.Background.alpha(0.45f), 1f to Ink.Background.alpha(0.15f))))
                    } else {
                        AsyncImage(art, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
                        Box(Modifier.fillMaxSize().background(Ink.Background.alpha(0.55f)))
                    }
                }
            }
        }
        Box(Modifier.fillMaxSize().background(Brush.verticalGradient(listOf(Ink.Background.alpha(0.2f), Ink.Background.alpha(0.85f)))))
    }
}

// ---------------------------------------------------------------------------- Barra superior

@Composable
private fun TopBar(vm: NexoViewModel, activity: Activity, padConnected: Boolean, u: Dp) {
    val clock by produceState(now()) { while (true) { delay(15_000); value = now() } }
    Row(Modifier.fillMaxWidth().padding(horizontal = u * 20, vertical = u * 8), verticalAlignment = Alignment.CenterVertically) {
        Text("N", color = color(vm.current.accent), fontSize = (u.value * 20).sp, fontWeight = FontWeight.Black)
        Spacer(Modifier.width(u * 6))
        Text("Nexo", color = Ink.Ink, fontSize = (u.value * 16).sp, fontWeight = FontWeight.Bold)
        Spacer(Modifier.width(u * 8))
        // El lema cede su sitio al aviso de actualización en pantallas estrechas.
        if (vm.update == null) Text("Tus consolas y tus juegos, en un solo lugar", color = Ink.InkMuted, fontSize = (u.value * 9).sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
        Spacer(Modifier.weight(1f))
        vm.update?.let { up ->
            val text = when {
                !up.ready -> "⬇ Nexo ${up.version} · ${(up.progress * 100).toInt()} %"
                up.needsPermission -> "Nexo ${up.version}: permite instalar apps" + if (padConnected) " · Ⓨ" else ""
                else -> "Nexo ${up.version} listo · " + if (padConnected) "Ⓨ instalar" else "toca para instalar"
            }
            Chip(text, u, accent = color(vm.current.accent), onClick = if (up.ready) ({ vm.installUpdate(activity) }) else null)
            Spacer(Modifier.width(u * 8))
        }
        // Donar: discreto; con mando, el botón Select.
        Chip("☕ Donar" + if (padConnected) " · Select" else "", u, onClick = { vm.donate(activity) })
        Spacer(Modifier.width(u * 8))
        if (padConnected) Chip("🎮 Mando conectado", u)
        Spacer(Modifier.width(u * 8))
        Text(clock, color = Ink.InkSoft, fontSize = (u.value * 12).sp, fontWeight = FontWeight.SemiBold)
    }
}

private fun now() = SimpleDateFormat("HH:mm", Locale.getDefault()).format(Date())

@Composable
private fun Chip(text: String, u: Dp, accent: Color? = null, onClick: (() -> Unit)? = null) {
    Box(Modifier.clip(RoundedCornerShape(50)).background(Color.White.alpha(0.06f)).border(1.dp, (accent ?: Color.White).alpha(if (accent != null) 0.5f else 0.12f), RoundedCornerShape(50))
        .then(if (onClick != null) Modifier.clickable { onClick() } else Modifier).padding(horizontal = u * 9, vertical = u * 3)) {
        Text(text, color = Ink.InkSoft, fontSize = (u.value * 9).sp)
    }
}

// ---------------------------------------------------------------------------- Carrusel

@Composable
private fun Carousel(vm: NexoViewModel, u: Dp, focus: Float) {
    val n = vm.systems.size
    var target by remember { mutableFloatStateOf(vm.active.toFloat()) }
    LaunchedEffect(vm.active) {
        val delta = ((vm.active - target + n / 2f) % n + n) % n - n / 2f
        target += delta
    }
    val pos by animateFloatAsState(target, spring(dampingRatio = 0.8f, stiffness = 180f), label = "pos")
    // Al entrar en la estantería el carrusel se encoge hacia arriba y se desvanece.
    if (focus > 0.97f) return
    BoxWithConstraints(Modifier.fillMaxSize().graphicsLayer {
            alpha = (1f - focus * 1.4f).coerceIn(0f, 1f)
            val s = 1f - 0.5f * focus
            scaleX = s; scaleY = s
            transformOrigin = androidx.compose.ui.graphics.TransformOrigin(0.5f, 0f)
            clip = false
        }
        .pointerInput(Unit) {
            var dragged = 0f
            detectHorizontalDragGestures(
                onDragStart = { dragged = 0f },
                onDragCancel = { dragged = 0f },
                onDragEnd = { if (abs(dragged) > 60.dp.toPx()) vm.step(if (dragged < 0) 1 else -1); dragged = 0f },
                onHorizontalDrag = { _, amount -> dragged += amount },
            )
        }, contentAlignment = Alignment.Center) {
        val cardW = u * 96
        val spacing = u * 92
        vm.systems.forEachIndexed { i, system ->
            // Distancia circular: el carrusel da la vuelta.
            val offset = ((i - pos + n / 2f) % n + n) % n - n / 2f
            val d = abs(offset)
            if (d > 3.2f) return@forEachIndexed
            Card(
                system, vm.statuses[system.id]?.state, u, cardW,
                Modifier
                    .zIndex(10f - d)
                    .offset(x = spacing * offset * (1f - 0.08f * d.coerceAtMost(2f)))
                    .graphicsLayer {
                        val s = 1f - 0.16f * d.coerceAtMost(2.5f)
                        scaleX = s; scaleY = s
                        rotationY = (-offset * 32f).coerceIn(-60f, 60f)
                        cameraDistance = 14f * density
                        alpha = (1f - (d - 1.6f).coerceAtLeast(0f) * 0.6f).coerceIn(0f, 1f)
                    }
                    .clickable(remember { MutableInteractionSource() }, null) { if (d < 0.5f) vm.enterGames() else vm.select(i) },
                selected = d < 0.5f,
            )
        }
    }
}

@Composable
private fun Card(system: ConsoleSystem, state: EmulatorState?, u: Dp, width: Dp, modifier: Modifier, selected: Boolean) {
    val primary = color(system.primary)
    Box(modifier.width(width).height(width * 1.18f)
        .shadow(if (selected) u * 18 else u * 6, RoundedCornerShape(u * 14), spotColor = primary, ambientColor = primary)
        .clip(RoundedCornerShape(u * 14))
        .background(Brush.verticalGradient(listOf(mix(primary, Color.White, 0.08f), mix(primary, color(system.secondary), 0.7f))))
        .border(if (selected) 2.dp else 1.dp, Color.White.alpha(if (selected) 0.55f else 0.18f), RoundedCornerShape(u * 14))) {
        AsyncImage("file:///android_asset/${system.photoAsset}", system.name,
            Modifier.fillMaxWidth().fillMaxHeight(0.66f).padding(u * 10), contentScale = ContentScale.Fit)
        Column(Modifier.align(Alignment.BottomStart).padding(u * 10)) {
            Text(system.name, color = Color.White, fontSize = (u.value * 13).sp, fontWeight = FontWeight.Bold, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(system.emulatorName + if (system.experimental) " · experimental" else "", color = Color.White.alpha(0.7f), fontSize = (u.value * 8).sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
        val (badge, tone) = when (state) {
            EmulatorState.Installed -> "Listo" to Ink.Green
            EmulatorState.NotInstalled -> "Instalar" to Ink.Amber
            else -> "Sin emulador" to Ink.InkMuted
        }
        Box(Modifier.align(Alignment.TopEnd).padding(u * 8).clip(RoundedCornerShape(50)).background(Color.Black.alpha(0.35f)).padding(horizontal = u * 6, vertical = u * 2)) {
            Text(badge, color = tone, fontSize = (u.value * 7).sp, fontWeight = FontWeight.SemiBold)
        }
    }
}

// ---------------------------------------------------------------------------- Ficha

@Composable
private fun InfoPanel(vm: NexoViewModel, activity: Activity, u: Dp, modifier: Modifier, onEdenGrant: () -> Unit) {
    val system = vm.current
    val status = vm.currentStatus
    val item = vm.selectedItem
    val accent = color(system.accent)
    Column(modifier.fillMaxWidth().padding(horizontal = u * 28), horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
        val (kicker, title, description) = when (item) {
            is ShelfItem.GameItem -> Triple(
                "${system.name} · juego ${vm.gameIndex + 1} de ${vm.shelf.size}".uppercase(),
                item.game.title,
                compatibilityText(item.game) + "Pulsa A o toca para jugar · B para volver",
            )
            is ShelfItem.ArchiveItem -> Triple("${system.name} · pendiente".uppercase(), item.archive.title, item.archive.message)
            null -> Triple(
                listOfNotNull(system.maker + (system.year?.let { " $it" } ?: ""), "Emulador: " + (status?.label ?: system.emulatorName) + if (system.experimental) " (experimental)" else "").joinToString("  ·  ").uppercase(),
                system.name, system.description,
            )
        }
        Text(kicker, color = accent, fontSize = (u.value * 8.5f).sp, fontWeight = FontWeight.SemiBold, letterSpacing = 0.6.sp, maxLines = 1)
        Text(title, color = Ink.Ink, fontSize = (u.value * 22).sp, fontWeight = FontWeight.Black, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Text(description, color = Ink.InkSoft, fontSize = (u.value * 9f).sp, textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis,
            modifier = Modifier.widthIn(max = u * 520))
        Spacer(Modifier.height(u * 5))
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(u * 8)) {
            StatusChip(vm, u)
            val label = when {
                item is ShelfItem.GameItem -> "Jugar"
                item is ShelfItem.ArchiveItem && item.archive.uri.toString() in vm.extracting -> "Descomprimiendo…"
                item is ShelfItem.ArchiveItem && item.archive.ready -> "Descomprimir"
                item is ShelfItem.ArchiveItem -> "Ver detalle"
                status?.state == EmulatorState.Installed -> "Abrir ${status.label}"
                status?.state == EmulatorState.NotInstalled -> "Instalar ${status.label}"
                else -> "Sin emulador"
            }
            Box(Modifier.clip(RoundedCornerShape(u * 10)).background(Brush.horizontalGradient(listOf(color(system.primary), mix(color(system.primary), Color.White, 0.2f))))
                .clickable { vm.primary(activity) }.padding(horizontal = u * 16, vertical = u * 7)) {
                Text("▶  $label", color = Color.White, fontSize = (u.value * 10.5f).sp, fontWeight = FontWeight.Bold)
            }
            // BIOS, firmware o claves: solo en consolas que los necesitan y con el emulador instalado.
            val setup = vm.setups[system.id]
            if (item == null && status?.state == EmulatorState.Installed && setup != null && setup.mode != com.nexo.launcher.core.SystemSetupMode.NotRequired) {
                val filesLabel = when {
                    vm.installingFiles -> "Instalando…"
                    setup.mode == com.nexo.launcher.core.SystemSetupMode.Installed -> "✓ BIOS lista"
                    system.id == "Switch" -> "Claves y firmware"
                    system.id == "PS3" -> "Firmware"
                    else -> "BIOS"
                }
                val ok = setup.mode == com.nexo.launcher.core.SystemSetupMode.Installed
                Box(Modifier.clip(RoundedCornerShape(u * 10)).background(Color.White.alpha(0.08f))
                    .border(1.dp, (if (ok) Ink.Green else Ink.Amber).alpha(0.6f), RoundedCornerShape(u * 10))
                    .clickable { vm.systemFiles(onEdenGrant) }.padding(horizontal = u * 12, vertical = u * 7)) {
                    Text("Ⓧ $filesLabel", color = if (ok) Ink.Green else Ink.Amber, fontSize = (u.value * 10).sp, fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

private fun compatibilityText(game: Game): String {
    val c = game.compatibility ?: return ""
    val label = when (c.status) {
        "Perfect" -> "perfecto"; "Playable" -> "jugable"; "Ingame" -> "entra al juego, con fallos"
        "Menu" -> "solo llega a los menús"; "Intro" -> "no pasa de la intro"; "Loadable" -> "solo carga"; "Nothing" -> "no arranca"
        else -> return ""
    }
    return "● $label" + (c.explanation?.let { "\n$it" } ?: "") + "\n"
}

@Composable
private fun StatusChip(vm: NexoViewModel, u: Dp) {
    val status = vm.currentStatus
    val (text, tone) = when {
        status == null -> "Comprobando…" to Ink.InkMuted
        status.state == EmulatorState.Installed && status.notes.isEmpty() -> "Emulador listo" to Ink.Green
        status.state == EmulatorState.Installed -> status.notes.first() to Ink.Amber
        status.state == EmulatorState.NotInstalled -> "Falta el emulador" to Ink.Amber
        else -> (status.notes.firstOrNull() ?: "Sin emulador fiable en Android") to Ink.InkMuted
    }
    Row(Modifier.clip(RoundedCornerShape(50)).background(Color.Black.alpha(0.3f)).padding(horizontal = u * 9, vertical = u * 4), verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(u * 6).clip(CircleShape).background(tone))
        Spacer(Modifier.width(u * 5))
        Text(text, color = tone, fontSize = (u.value * 8.5f).sp, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.widthIn(max = u * 260))
    }
}

// ---------------------------------------------------------------------------- Estantería

@Composable
private fun Shelf(vm: NexoViewModel, activity: Activity, u: Dp, focus: Float) {
    val items = vm.shelf
    val pos by animateFloatAsState(vm.gameIndex.toFloat(), spring(dampingRatio = 0.8f, stiffness = 200f), label = "shelf")
    Column(Modifier.fillMaxWidth().height(u * (30f + 66f + 62f * focus))) {
        Row(Modifier.padding(horizontal = u * 22), verticalAlignment = Alignment.CenterVertically) {
            Text("Juegos de ${vm.current.name}", color = Ink.Ink, fontSize = (u.value * 11).sp, fontWeight = FontWeight.Bold)
            Spacer(Modifier.width(u * 8))
            Text(when {
                vm.scanning -> "buscando…"
                items.isEmpty() -> ""
                else -> "${items.size} " + if (items.size == 1) "elemento" else "elementos"
            }, color = Ink.InkMuted, fontSize = (u.value * 8.5f).sp)
        }
        BoxWithConstraints(Modifier.fillMaxSize().pointerInput(vm.current.id) {
            var dragged = 0f
            detectHorizontalDragGestures(
                onDragStart = { dragged = 0f },
                onDragCancel = { dragged = 0f },
                onDragEnd = { if (abs(dragged) > 50.dp.toPx()) { if (!vm.gamesMode) vm.enterGames(); vm.stepGame(if (dragged < 0) 1 else -1) }; dragged = 0f },
                onHorizontalDrag = { _, amount -> dragged += amount },
            )
        }, contentAlignment = Alignment.BottomCenter) {
            if (items.isEmpty() && !vm.scanning) {
                Text(if (vm.libraryRoot == null) "" else "Aún no hay juegos. ${vm.current.gamesHint}", color = Ink.InkMuted,
                    fontSize = (u.value * 9).sp, textAlign = TextAlign.Center, modifier = Modifier.align(Alignment.Center).padding(horizontal = u * 40))
                return@BoxWithConstraints
            }
            val spacing = u * 104
            val scale = 0.52f + 0.48f * focus
            val center = pos.coerceIn(0f, maxOf(0, items.lastIndex).toFloat())
            val radius = (maxWidth.value / 2 + 120) / (spacing.value * scale)
            val first = floor(center - radius).toInt().coerceAtLeast(0)
            val last = ceil(center + radius).toInt().coerceAtMost(items.lastIndex)
            for (i in first..last) {
                val item = items[i]
                val uri = when (item) {
                    is ShelfItem.GameItem -> item.game.uri
                    is ShelfItem.ArchiveItem -> item.archive.uri
                }
              key(item::class, uri) {
                val offset = i - center
                val selected = vm.gamesMode && i == vm.gameIndex
                val m = Modifier
                    .zIndex(100f - abs(offset))
                    .offset(x = spacing * offset * scale)
                    .graphicsLayer {
                        scaleX = scale; scaleY = scale
                        transformOrigin = androidx.compose.ui.graphics.TransformOrigin(0.5f, 1f)
                        alpha = (1f - (abs(offset) - 3f).coerceAtLeast(0f) * 0.3f).coerceIn(0.25f, 1f) * (0.75f + 0.25f * focus)
                    }
                    .clickable(remember { MutableInteractionSource() }, null) {
                        if (selected) vm.primary(activity) else vm.selectGame(i)
                    }
                when (item) {
                    is ShelfItem.GameItem -> {
                        LaunchedEffect(item.game.uri, item.game.sizeBytes, vm.artRevision, vm.scanning) {
                            if (!vm.scanning) vm.requestArt(item.game)
                        }
                        MediaPiece(item.game, vm.current, selected, u, m)
                    }
                    is ShelfItem.ArchiveItem -> ArchivePiece(item.archive, selected, u, m, vm.extracting[item.archive.uri.toString()])
                }
              }
            }
        }
    }
}

// ---------------------------------------------------------------------------- Ayudas y avisos

@Composable
private fun Hints(vm: NexoViewModel, padConnected: Boolean, u: Dp) {
    val text = when {
        !padConnected && vm.gamesMode -> "Desliza para elegir · toca el juego seleccionado para jugar"
        !padConnected -> "Desliza para cambiar de consola · toca la consola para ver sus juegos"
        vm.gamesMode -> "◀ ▶ elegir juego   Ⓐ jugar   Ⓑ volver   LB RB cambiar consola"
        else -> "◀ ▶ consola   ▼ ver juegos   Ⓐ abrir   Ⓧ BIOS"
    }
    Text(text, color = Ink.InkMuted, fontSize = (u.value * 8).sp, textAlign = TextAlign.Center, modifier = Modifier.fillMaxWidth().padding(bottom = u * 6))
}

@Composable
private fun LibraryPrompt(u: Dp, onPick: () -> Unit) {
    val pickerFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { pickerFocus.requestFocus() }
    Box(Modifier.fillMaxSize().background(Ink.Background.alpha(0.7f)), contentAlignment = Alignment.Center) {
        Column(Modifier.widthIn(max = u * 420).clip(RoundedCornerShape(u * 16)).background(Ink.Panel).border(1.dp, Color.White.alpha(0.12f), RoundedCornerShape(u * 16)).padding(u * 20),
            horizontalAlignment = Alignment.CenterHorizontally) {
            Text("Elige tu biblioteca", color = Ink.Ink, fontSize = (u.value * 18).sp, fontWeight = FontWeight.Black)
            Spacer(Modifier.height(u * 6))
            Text("Elige o crea una carpeta para Nexo (por ejemplo «Nexo» en la memoria del teléfono o en la tarjeta SD). Nexo creará dentro Juegos/<consola> para que copies ahí tus juegos.",
                color = Ink.InkSoft, fontSize = (u.value * 10).sp, textAlign = TextAlign.Center)
            Spacer(Modifier.height(u * 12))
            Box(Modifier.clip(RoundedCornerShape(u * 10)).background(Color(0xFF5B6CFF)).focusRequester(pickerFocus).clickable { onPick() }.padding(horizontal = u * 18, vertical = u * 8)) {
                Text("Elegir carpeta", color = Color.White, fontSize = (u.value * 11).sp, fontWeight = FontWeight.Bold)
            }
        }
    }
}

/** Pasos para preparar un emulador que no deja instalar su BIOS desde fuera, con botón para abrirlo. */
@Composable
private fun GuideDialog(vm: NexoViewModel, guide: NexoViewModel.Guide, activity: Activity, u: Dp) {
    Box(Modifier.fillMaxSize().background(Ink.Background.alpha(0.75f)), contentAlignment = Alignment.Center) {
        Column(Modifier.widthIn(max = u * 520).clip(RoundedCornerShape(u * 16)).background(Ink.Panel).border(1.dp, Color.White.alpha(0.12f), RoundedCornerShape(u * 16)).padding(u * 18)) {
            Text(guide.title, color = Ink.Ink, fontSize = (u.value * 16).sp, fontWeight = FontWeight.Black)
            Spacer(Modifier.height(u * 4))
            Text(guide.message, color = Ink.InkSoft, fontSize = (u.value * 9.5f).sp)
            guide.steps.forEachIndexed { i, step ->
                Row(Modifier.padding(top = u * 5)) {
                    Text("${i + 1}.", color = color(vm.current.accent), fontSize = (u.value * 9.5f).sp, fontWeight = FontWeight.Bold, modifier = Modifier.width(u * 18))
                    Text(step, color = Ink.Ink, fontSize = (u.value * 9.5f).sp)
                }
            }
            Spacer(Modifier.height(u * 12))
            Row(horizontalArrangement = Arrangement.spacedBy(u * 10), modifier = Modifier.align(Alignment.End)) {
                Box(Modifier.clip(RoundedCornerShape(u * 10)).background(Color.White.alpha(0.08f)).clickable { vm.closeGuide() }.padding(horizontal = u * 14, vertical = u * 7)) {
                    Text("Ⓑ Cerrar", color = Ink.InkSoft, fontSize = (u.value * 10.5f).sp, fontWeight = FontWeight.Bold)
                }
                if (guide.openEmulator != null) {
                    Box(Modifier.clip(RoundedCornerShape(u * 10)).background(color(vm.current.primary)).clickable { vm.openGuideEmulator(activity) }.padding(horizontal = u * 14, vertical = u * 7)) {
                        Text("Ⓐ Abrir ${vm.currentStatus?.label ?: "emulador"}", color = Color.White, fontSize = (u.value * 10.5f).sp, fontWeight = FontWeight.Bold)
                    }
                }
            }
        }
    }
}

/** «¿Borrar el comprimido?»: en Android el borrado es definitivo, así que se pregunta y Conservar es lo seguro. */
@Composable
private fun DeletePrompt(vm: NexoViewModel, result: com.nexo.launcher.core.ArchiveExtractionResult, u: Dp) {
    Box(Modifier.fillMaxSize().background(Ink.Background.alpha(0.7f)), contentAlignment = Alignment.Center) {
        Column(Modifier.widthIn(max = u * 420).clip(RoundedCornerShape(u * 16)).background(Ink.Panel).border(1.dp, Color.White.alpha(0.12f), RoundedCornerShape(u * 16)).padding(u * 20),
            horizontalAlignment = Alignment.CenterHorizontally) {
            Text("¿Borrar el comprimido?", color = Ink.Ink, fontSize = (u.value * 17).sp, fontWeight = FontWeight.Black)
            Spacer(Modifier.height(u * 6))
            Text("${result.archive.title} ya está descomprimido. Borrar el comprimido libera ${sizeText(result.archive.sizeBytes)}. En Android no hay Papelera: no se puede deshacer.",
                color = Ink.InkSoft, fontSize = (u.value * 10).sp, textAlign = TextAlign.Center)
            Spacer(Modifier.height(u * 12))
            Row(horizontalArrangement = Arrangement.spacedBy(u * 10)) {
                Box(Modifier.clip(RoundedCornerShape(u * 10)).background(Color(0xFF5B6CFF)).clickable { vm.confirmDelete(false) }.padding(horizontal = u * 16, vertical = u * 8)) {
                    Text("Ⓐ Conservar", color = Color.White, fontSize = (u.value * 11).sp, fontWeight = FontWeight.Bold)
                }
                Box(Modifier.clip(RoundedCornerShape(u * 10)).background(Color.White.alpha(0.08f)).border(1.dp, Ink.Red.alpha(0.6f), RoundedCornerShape(u * 10)).clickable { vm.confirmDelete(true) }.padding(horizontal = u * 16, vertical = u * 8)) {
                    Text("Ⓨ Borrar", color = Ink.Red, fontSize = (u.value * 11).sp, fontWeight = FontWeight.Bold)
                }
            }
        }
    }
}

@Composable
private fun ToastHost(vm: NexoViewModel, u: Dp) {
    val toast = vm.toast
    LaunchedEffect(toast) { if (toast != null) { delay(if (toast.error) 6000 else 3800); vm.dismissToast(toast) } }
    Box(Modifier.fillMaxSize().padding(u * 14), contentAlignment = Alignment.TopCenter) {
        AnimatedVisibility(toast != null, enter = fadeIn(), exit = fadeOut()) {
            val t = toast ?: return@AnimatedVisibility
            Box(Modifier.widthIn(max = u * 460).clip(RoundedCornerShape(u * 10)).background(if (t.error) Color(0xF2331418) else Color(0xF21A1D28))
                .border(1.dp, (if (t.error) Ink.Red else Color.White).alpha(0.3f), RoundedCornerShape(u * 10)).padding(horizontal = u * 14, vertical = u * 8)) {
                Text(t.text, color = Ink.Ink, fontSize = (u.value * 9.5f).sp)
            }
        }
    }
}
