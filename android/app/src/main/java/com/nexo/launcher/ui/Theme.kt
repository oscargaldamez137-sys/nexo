package com.nexo.launcher.ui

import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.lerp

/** Los mismos tonos que la versión de PC (App.xaml). */
object Ink {
    val Background = Color(0xFF07080D)
    val Panel = Color(0xFF12141C)
    val Ink = Color(0xFFF2F4FA)
    val InkSoft = Color(0xFFB7BDD0)
    val InkMuted = Color(0xFF7C8299)
    val Green = Color(0xFF4ADE80)
    val Amber = Color(0xFFFBBF24)
    val Red = Color(0xFFF87171)
    val Blue = Color(0xFF7CC4FF)
}

fun color(argb: Long) = Color(argb)

fun mix(a: Color, b: Color, t: Float) = lerp(a, b, t)

fun Color.alpha(a: Float) = copy(alpha = a)

fun isLight(c: Color) = (c.red * 299 + c.green * 587 + c.blue * 114) / 1000f > 0.63f

/** Un color vivo y estable para un texto (mismo título, mismo color), como Palette.FromHash en PC. */
fun hashColor(text: String): Color {
    var hash = 17
    for (ch in text) hash = hash * 31 + ch.code
    val h = ((hash and 0x7fffffff) % 360).toFloat()
    return Color.hsv(h, 0.65f, 0.9f)
}

fun initials(title: String): String {
    val words = title.split(' ', '-', '_', ':', '.').filter { it.isNotEmpty() && it[0].isLetterOrDigit() }.take(2)
    return if (words.isEmpty()) "?" else words.joinToString("") { it[0].uppercase() }
}
