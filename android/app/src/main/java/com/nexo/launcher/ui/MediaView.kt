package com.nexo.launcher.ui

import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.spring
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.GenericShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Rect
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.material3.Text
import coil3.compose.AsyncImage
import com.nexo.launcher.core.ConsoleSystem
import com.nexo.launcher.core.Game
import com.nexo.launcher.core.MediaKind
import com.nexo.launcher.core.PendingArchive

/**
 * Un juego dibujado como el soporte físico de su consola. Al seleccionarlo cobra vida:
 * se agranda y brilla, el disco sale de la caja y gira, la tarjeta se inclina.
 */
@Composable
fun MediaPiece(game: Game, system: ConsoleSystem, selected: Boolean, u: Dp, modifier: Modifier = Modifier) {
    val lift by animateFloatAsState(if (selected) 1f else 0f, spring(dampingRatio = 0.55f, stiffness = 300f), label = "lift")
    Column(modifier.width(u * 96), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            Modifier
                .height(u * 112)
                .fillMaxWidth()
                .graphicsLayer {
                    val s = 1f + 0.1f * lift
                    scaleX = s; scaleY = s
                    translationY = -lift * 8.dp.toPx()
                    transformOrigin = androidx.compose.ui.graphics.TransformOrigin(0.5f, 1f)
                    rotationZ = if (system.media == MediaKind.GameCard || system.media == MediaKind.Cartridge || system.media == MediaKind.Umd) -5f * lift else 0f
                },
            contentAlignment = Alignment.BottomCenter,
        ) {
            when (system.media) {
                MediaKind.Umd -> Umd(game, system, selected, u)
                MediaKind.GameCard -> GameCard(game, system, u)
                MediaKind.Cartridge -> when (system.id) {
                    "GBC" -> GameBoyCartridge(game, system, u)
                    "GBA" -> AdvanceCartridge(game, system, u)
                    else -> SnesCartridge(game, system, u)
                }
                else -> DiscCase(game, system, selected, u)
            }
        }
        Text(
            game.title, color = if (selected) Ink.Ink else Ink.InkSoft, fontSize = 11.sp,
            fontWeight = if (selected) FontWeight.Bold else FontWeight.SemiBold,
            maxLines = 2, overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
            modifier = Modifier.padding(top = u * 5, start = 2.dp, end = 2.dp),
        )
        compatibilityDot(game)?.let { dot ->
            Box(Modifier.padding(top = 2.dp).size(6.dp).clip(CircleShape).background(dot))
        }
    }
}

private fun compatibilityDot(game: Game): Color? = when (game.compatibility?.status) {
    "Perfect", "Playable" -> Ink.Green
    "Ingame", "Menu" -> Ink.Amber
    "Intro", "Loadable", "Nothing" -> Ink.Red
    else -> null
}

// ---------------------------------------------------------------------------- Cajas con disco

private class CaseStyle(
    val width: Float, val height: Float, val plastic: Color, val band: Color, val ink: Color,
    val brand: String, val italic: Boolean = false, val sheen: Boolean = false, val fullDisc: Boolean = true,
)

private fun caseStyle(system: ConsoleSystem, platform: String?): CaseStyle = when (system.id) {
    "PS2" -> CaseStyle(70f, 100f, Color(0xFF16171B), Color(0xFF050507), Color.White, "PlayStation 2", italic = true)
    "PS3" -> CaseStyle(78f, 96f, Color(0xFF141519), Color(0xFF0A0B0E), Color(0xFFE6E8EE), "PS3", sheen = true)
    "PS4" -> CaseStyle(78f, 96f, Color(0xFF0E2A66), Color(0xFF0043B0), Color.White, "PS4")
    "PS5" -> CaseStyle(78f, 96f, Color(0xFF1A3E8C), Color(0xFFFAFBFD), Color(0xFF101114), "PS5")
    "Xbox" -> CaseStyle(70f, 100f, Color(0xFF121315), Color(0xFF3E8E1F), Color.White, "XBOX", sheen = true)
    "Xbox360" -> CaseStyle(70f, 100f, Color(0xFF2F7D24), Color(0xFFF7F8F9), Color(0xFF5C9E2E), "XBOX 360")
    "GameCube-Wii" -> if (platform.equals("Wii", true))
        CaseStyle(72f, 99f, Color(0xFFEEF0F4), Color.White, Color(0xFF8C919A), "Wii", italic = true)
    else CaseStyle(72f, 99f, Color(0xFF151519), Color(0xFF050507), Color(0xFFA696FF),
        if (platform.equals("GameCube", true)) "GAMECUBE" else "GAMECUBE · Wii", fullDisc = false)
    else -> CaseStyle(78f, 96f, mix(color(system.primary), Color.Black, 0.7f), mix(color(system.primary), Color.Black, 0.35f), Color.White, system.name)
}

@Composable
private fun DiscCase(game: Game, system: ConsoleSystem, selected: Boolean, u: Dp) {
    val style = caseStyle(system, game.platform)
    val w = u * style.width
    val h = u * style.height
    val discSize = if (style.fullDisc) w * 0.9f else w * 0.66f
    val slide by animateFloatAsState(if (selected) 1f else 0f, tween(if (selected) 520 else 300), label = "slide")
    val angle = if (selected) {
        val spin = rememberInfiniteTransition(label = "spin")
        spin.animateFloat(0f, 360f, infiniteRepeatable(tween(2200, easing = LinearEasing), RepeatMode.Restart), label = "angle")
    } else null

    Box(Modifier.width(w).height(h + discSize * 0.6f), contentAlignment = Alignment.BottomCenter) {
        // Disco detrás de la caja: asoma y gira al seleccionar.
        Disc(
            game, system, discSize,
            Modifier
                .offset(y = -(h - discSize - u * 4) - discSize * 0.58f * slide)
                .graphicsLayer { rotationZ = angle?.value ?: 0f },
        )
        val light = isLight(style.plastic)
        val boxArt = game.cover != null
        Box(
            Modifier
                .width(w).height(h)
                .shadow(u * 6, RoundedCornerShape(u * 3))
                .clip(RoundedCornerShape(u * 3))
                .background(Brush.horizontalGradient(listOf(mix(style.plastic, if (light) Color.Gray else Color.White, 0.12f), style.plastic, mix(style.plastic, Color.Black, 0.25f))))
                .border(1.dp, if (light) Color.Black.alpha(0.35f) else Color.White.alpha(0.3f), RoundedCornerShape(u * 3)),
        ) {
            Column(Modifier.fillMaxSize().padding(start = u * 5, top = u * 1.5f, end = u * 1.5f, bottom = u * 1.5f)) {
                // Una portada real ya trae impresa la franja de su consola; se dibuja solo sobre las generadas.
                if (!boxArt) Band(style, u)
                Box(Modifier.fillMaxWidth().weight(1f)) { CoverArt(game, system, Modifier.fillMaxSize()) }
            }
            Gloss()
        }
    }
}

@Composable
private fun Band(style: CaseStyle, u: Dp) {
    val brush = if (style.sheen) Brush.horizontalGradient(listOf(style.band, mix(style.band, Color(0xFF9AA0AC), 0.45f))) else Brush.linearGradient(listOf(style.band, style.band))
    Box(Modifier.fillMaxWidth().height(u * 10).background(brush), contentAlignment = Alignment.CenterStart) {
        Text(
            style.brand, color = style.ink, fontSize = (u.value * 6).sp, fontWeight = FontWeight.Black,
            fontStyle = if (style.italic) FontStyle.Italic else FontStyle.Normal, maxLines = 1,
            modifier = Modifier.padding(start = u * 3),
        )
    }
}

@Composable
private fun Disc(game: Game, system: ConsoleSystem, size: Dp, modifier: Modifier) {
    Box(modifier.size(size).clip(CircleShape).background(Brush.linearGradient(listOf(color(system.primary), color(system.secondary)))), contentAlignment = Alignment.Center) {
        if (game.cover != null) AsyncImage(game.cover, null, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        Box(Modifier.fillMaxSize().background(Brush.linearGradient(listOf(Color.Transparent, Color(0x559CF4FF), Color(0x66FFFFFF), Color(0x55FFA8E0), Color.Transparent))))
        Box(Modifier.size(size * 0.36f).clip(CircleShape).background(Color(0xE6D8DDE6)))
        Box(Modifier.size(size * 0.13f).clip(CircleShape).background(Color(0xFF0B0C10)))
        Box(Modifier.size(size * 0.08f, 2.dp).offset(x = size * 0.36f).background(Color.White.alpha(0.66f)))
    }
}

// ---------------------------------------------------------------------------- Portátiles y cartuchos

/** Tarjeta de Switch: cuerpo negro, esquina superior derecha recortada, estrías y etiqueta con el arte. */
@Composable
private fun GameCard(game: Game, system: ConsoleSystem, u: Dp) {
    val shape = cutCorner(0.14f)
    Box(Modifier.width(u * 66).height(u * 96).shadow(u * 6, shape).clip(shape)
        .background(Brush.verticalGradient(listOf(Color(0xFF33353B), Color(0xFF17181C))))) {
        Box(Modifier.fillMaxWidth().height(u * 10).background(Brush.verticalGradient(listOf(Color(0xFF42454D), Color(0xFF26282E)))))
        Column(Modifier.padding(start = u * 7, top = u * 3)) {
            repeat(3) { Box(Modifier.padding(bottom = u * 1.6f).width(u * 36).height(1.dp).background(Color.Black.alpha(0.45f))) }
        }
        Box(Modifier.fillMaxSize().padding(start = u * 4, end = u * 4, top = u * 12, bottom = u * 5)
            .clip(RoundedCornerShape(u * 2)).background(Color(0xFFF4F5F7)).padding(1.dp).clip(RoundedCornerShape(u * 1.5f))) {
            CoverArt(game, system, Modifier.fillMaxSize())
        }
        Gloss()
    }
}

/** UMD: carcasa con la parte superior redondeada y la etiqueta impresa con el arte. */
@Composable
private fun Umd(game: Game, system: ConsoleSystem, selected: Boolean, u: Dp) {
    val shape = GenericShape { size, _ ->
        val r = size.width / 2
        moveTo(0f, r * 0.8f)
        arcTo(Rect(0f, 0f, size.width, r * 1.6f), 180f, 180f, false)
        lineTo(size.width, size.height); lineTo(0f, size.height); close()
    }
    Box(Modifier.width(u * 74).height(u * 84).shadow(u * 6, shape).clip(shape)
        .background(Brush.verticalGradient(listOf(Color(0xFF3A3E47), Color(0xFF1A1C22))))) {
        Box(Modifier.fillMaxWidth().height(u * 50).padding(start = u * 5, end = u * 5, top = u * 5).clip(shape)) {
            CoverArt(game, system, Modifier.fillMaxSize())
        }
        Text("UMD", color = Color.White.alpha(0.55f), fontSize = (u.value * 5).sp, fontWeight = FontWeight.Black,
            modifier = Modifier.align(Alignment.BottomEnd).padding(end = u * 6, bottom = u * 22))
        Gloss()
    }
}

/** Cartucho de Game Boy Color: vertical, oscuro, con la esquina recortada y la flecha en relieve. */
@Composable
private fun GameBoyCartridge(game: Game, system: ConsoleSystem, u: Dp) {
    val shape = cutCorner(0.12f)
    Box(Modifier.width(u * 70).height(u * 80).shadow(u * 6, shape).clip(shape)
        .background(Brush.verticalGradient(listOf(Color(0xFF4A4C55), Color(0xFF24252B))))) {
        Column(Modifier.padding(start = u * 7, top = u * 3)) {
            repeat(4) { Box(Modifier.padding(bottom = u * 1.4f).width(u * 44).height(1.dp).background(Color.Black.alpha(0.4f))) }
        }
        Box(Modifier.fillMaxSize().padding(start = u * 6, end = u * 6, top = u * 15, bottom = u * 17).clip(RoundedCornerShape(u * 2))) {
            CoverArt(game, system, Modifier.fillMaxSize())
        }
        Text("▼", color = Color.White.alpha(0.35f), fontSize = (u.value * 5).sp, modifier = Modifier.align(Alignment.BottomCenter).padding(bottom = u * 6))
        Gloss()
    }
}

/** Cartucho de Game Boy Advance: pequeño, apaisado, gris, con la pestaña superior curva. */
@Composable
private fun AdvanceCartridge(game: Game, system: ConsoleSystem, u: Dp) {
    val shape = GenericShape { size, _ ->
        val bump = size.height * 0.08f
        moveTo(0f, bump * 2)
        quadraticTo(size.width / 2, -bump, size.width, bump * 2)
        lineTo(size.width, size.height); lineTo(0f, size.height); close()
    }
    Box(Modifier.width(u * 86).height(u * 56).shadow(u * 6, shape).clip(shape)
        .background(Brush.verticalGradient(listOf(Color(0xFF8E929C), Color(0xFF5E626B))))) {
        Box(Modifier.fillMaxSize().padding(start = u * 5, end = u * 5, top = u * 9, bottom = u * 6).clip(RoundedCornerShape(u * 2))) {
            CoverArt(game, system, Modifier.fillMaxSize())
        }
        Gloss()
    }
}

/** Cartucho retro gris con estrías y etiqueta frontal. */
@Composable
private fun SnesCartridge(game: Game, system: ConsoleSystem, u: Dp) {
    val shape = RoundedCornerShape(topStart = u * 6, topEnd = u * 6, bottomStart = u * 2, bottomEnd = u * 2)
    Box(Modifier.width(u * 90).height(u * 82).shadow(u * 6, shape).clip(shape)
        .background(Brush.verticalGradient(listOf(Color(0xFF9A9FA9), Color(0xFF6B707A))))) {
        Column(Modifier.align(Alignment.TopCenter).padding(top = u * 3)) {
            androidx.compose.foundation.layout.Row {
                repeat(5) { Box(Modifier.padding(horizontal = u * 2.5f).width(u * 2).height(u * 9).background(Color.Black.alpha(0.33f))) }
            }
        }
        Box(Modifier.fillMaxSize().padding(start = u * 7, end = u * 7, top = u * 15, bottom = u * 14).clip(RoundedCornerShape(u * 3))) {
            CoverArt(game, system, Modifier.fillMaxSize())
        }
        Gloss()
    }
}

private fun cutCorner(fraction: Float): Shape = GenericShape { size, _ ->
    val cut = size.width * fraction
    moveTo(0f, 0f); lineTo(size.width - cut, 0f); lineTo(size.width, cut)
    lineTo(size.width, size.height); lineTo(0f, size.height); close()
}

@Composable
private fun Gloss() {
    Box(Modifier.fillMaxSize().background(Brush.linearGradient(
        0f to Color.White.alpha(0.22f), 0.45f to Color.Transparent, 0.8f to Color.Transparent, 1f to Color.White.alpha(0.08f),
        start = Offset.Zero, end = Offset(300f, 500f),
    )))
}

// ---------------------------------------------------------------------------- Portada

/**
 * La portada ajustada al hueco; sin portada, una carátula generada con los colores de la consola y
 * las iniciales del juego (cada juego con su propio tono).
 */
@Composable
fun CoverArt(game: Game, system: ConsoleSystem, modifier: Modifier) {
    if (game.cover != null) {
        Box(modifier) {
            // Una sola imagen: con Crop la segunda copia tapaba por completo el blur inferior.
            AsyncImage(game.cover, game.title, Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        }
        return
    }
    val tint = hashColor(game.title)
    Box(modifier.background(Brush.linearGradient(listOf(mix(color(system.primary), tint, 0.45f), mix(color(system.secondary), tint, 0.25f)))), contentAlignment = Alignment.Center) {
        Text(initials(game.title), color = Color.White.alpha(0.9f), fontSize = 20.sp, fontWeight = FontWeight.Black)
        Text(game.title, color = Color.White, fontSize = 7.sp, fontWeight = FontWeight.SemiBold, maxLines = 2,
            overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center,
            modifier = Modifier.align(Alignment.BottomCenter).padding(4.dp))
    }
}

// ---------------------------------------------------------------------------- Comprimidos

/** Caja con cremallera para un comprimido o paquete pendiente: ámbar si está listo, rojo si no. */
@Composable
fun ArchivePiece(archive: PendingArchive, selected: Boolean, u: Dp, modifier: Modifier = Modifier, progress: com.nexo.launcher.core.ArchiveProgress? = null) {
    val lift by animateFloatAsState(if (selected) 1f else 0f, spring(dampingRatio = 0.55f, stiffness = 300f), label = "lift")
    val tone = if (archive.ready) Color(0xFFF4A93C) else Color(0xFFE5484D)
    Column(modifier.width(u * 96), horizontalAlignment = Alignment.CenterHorizontally) {
        Box(Modifier.height(u * 112).fillMaxWidth().graphicsLayer {
            val s = 1f + 0.1f * lift; scaleX = s; scaleY = s; translationY = -lift * 8.dp.toPx()
            transformOrigin = androidx.compose.ui.graphics.TransformOrigin(0.5f, 1f)
        }, contentAlignment = Alignment.BottomCenter) {
            Box(Modifier.width(u * 70).height(u * 82).shadow(u * 6, RoundedCornerShape(u * 6)).clip(RoundedCornerShape(u * 6))
                .background(Brush.verticalGradient(listOf(tone, mix(tone, Color.Black, 0.35f)))), contentAlignment = Alignment.Center) {
                Box(Modifier.width(u * 3).fillMaxSize().padding(vertical = u * 6).background(Color.Black.alpha(0.25f)))
                Column(Modifier.align(Alignment.BottomCenter).padding(u * 6).clip(RoundedCornerShape(u * 3)).background(Color.Black.alpha(0.45f)).padding(horizontal = u * 4, vertical = u * 2),
                    horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(archive.uri.lastPathSegment?.substringAfterLast('.')?.uppercase()?.take(4) ?: "ZIP",
                        color = Color.White, fontSize = (u.value * 7).sp, fontWeight = FontWeight.Black)
                    // Mientras se descomprime: barra y cantidad escrita.
                    if (progress != null) {
                        val total = progress.totalBytes
                        val fraction = if (total != null && total > 0) (progress.bytesWritten.toFloat() / total).coerceIn(0f, 1f) else null
                        Box(Modifier.padding(top = u * 2).width(u * 50).height(u * 3).clip(RoundedCornerShape(50)).background(Color.White.alpha(0.2f))) {
                            Box(Modifier.fillMaxHeight().fillMaxWidth(fraction ?: ((progress.bytesWritten / 1048576 % 50) / 50f)).background(Color.White))
                        }
                        Text(sizeText(progress.bytesWritten), color = Color.White.alpha(0.85f), fontSize = (u.value * 5.5f).sp)
                    }
                }
            }
        }
        Text(archive.title, color = if (selected) Ink.Ink else Ink.InkSoft, fontSize = 11.sp, maxLines = 2,
            overflow = TextOverflow.Ellipsis, textAlign = TextAlign.Center, modifier = Modifier.padding(top = u * 5))
    }
}
