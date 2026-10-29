package com.nexo.launcher.core

import android.content.Context

/** Punto único de creación del núcleo que usa la interfaz. */
object NexoCores {
    fun create(context: Context): NexoCore = AndroidNexoCore(context.applicationContext)
}