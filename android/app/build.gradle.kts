plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Súbela junto con <Version> de src/Nexo/Nexo.csproj antes de publicar la versión vX.Y.Z.
val nexoVersion = "1.0.0"

android {
    namespace = "com.nexo.launcher"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.nexo.launcher"
        minSdk = 26
        targetSdk = 35
        // Versión de Nexo: la misma que <Version> en src/Nexo/Nexo.csproj y que la etiqueta vX.Y.Z de GitHub.
        // versionCode = major*10000 + minor*100 + patch (1.0.0 -> 10000, 1.0.1 -> 10001, 1.2.0 -> 10200),
        // con minor y patch entre 0 y 99: cada versión publicada supera a la anterior y Android acepta la actualización.
        // Es la misma regla que AppUpdates.versionCode en el código de la app.
        versionName = nexoVersion
        versionCode = nexoVersion.split(".").map { it.toInt() }.let { (major, minor, patch) ->
            require(minor in 0..99 && patch in 0..99) { "minor y patch deben estar entre 0 y 99" }
            major * 10000 + minor * 100 + patch
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            // Se instala a mano (sideload): se firma con la clave de depuración local.
            signingConfig = signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
    }

    // Preserve the libraries' notices instead of discarding duplicate license resources.
    packaging.resources.merges += setOf("META-INF/LICENSE", "META-INF/NOTICE", "META-INF/LICENSE.txt", "META-INF/NOTICE.txt")

    // Las fotos de las consolas son las mismas de la versión de PC: se usan sin copiarlas.
    sourceSets["main"].assets.directories.add("../../src/Nexo/Photos")
}

dependencies {
    implementation(platform("androidx.compose:compose-bom:2025.01.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.foundation:foundation")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.activity:activity-compose:1.10.0")
    implementation("androidx.core:core-ktx:1.15.0")
    implementation("androidx.lifecycle:lifecycle-viewmodel-compose:2.8.7")
    implementation("io.coil-kt.coil3:coil-compose:3.0.4")
    implementation("io.coil-kt.coil3:coil-network-okhttp:3.0.4")
    implementation("org.apache.commons:commons-compress:1.28.0")
    implementation("org.tukaani:xz:1.12")
    implementation("com.github.junrar:junrar:8.1.2")
}

// JVM checks without downloading JUnit. Uses the same compiled core as the APK.
tasks.register<JavaExec>("coreLogicTest") {
    group = "verification"
    description = "Pruebas puras del núcleo, sin Android ni red ni dependencias de test adicionales."
    dependsOn("compileDebugKotlin", "compileDebugUnitTestKotlin")
    classpath = files(
        tasks.named("compileDebugKotlin").map { it.outputs.files },
        tasks.named("compileDebugUnitTestKotlin").map { it.outputs.files },
        configurations.named("debugUnitTestRuntimeClasspath")
    )
    mainClass.set("com.nexo.launcher.core.CoreLogicChecks")
}

// Keep the normal entry point useful offline. The executable checks above throw
// on failure; there are deliberately no JUnit-annotated methods to discover yet.
tasks.withType<Test>().configureEach {
    if (name == "testDebugUnitTest") {
        dependsOn("coreLogicTest")
        dependsOn("archiveTest")
        dependsOn("systemFilesTest")
        dependsOn("appUpdateTest")
        failOnNoDiscoveredTests.set(false)
    }
}

tasks.register<JavaExec>("archiveTest") {
    group = "verification"
    dependsOn("compileDebugKotlin", "compileDebugUnitTestKotlin")
    classpath = files(
        tasks.named("compileDebugKotlin").map { it.outputs.files },
        tasks.named("compileDebugUnitTestKotlin").map { it.outputs.files },
        configurations.named("debugUnitTestRuntimeClasspath")
    )
    mainClass.set("com.nexo.launcher.core.ArchiveChecks")
}

tasks.register<JavaExec>("systemFilesTest") {
    group = "verification"
    dependsOn("compileDebugKotlin", "compileDebugUnitTestKotlin")
    classpath = files(
        tasks.named("compileDebugKotlin").map { it.outputs.files },
        tasks.named("compileDebugUnitTestKotlin").map { it.outputs.files },
        configurations.named("debugUnitTestRuntimeClasspath")
    )
    mainClass.set("com.nexo.launcher.core.SystemFilesChecks")
}

tasks.register<JavaExec>("appUpdateTest") {
    group = "verification"
    description = "Actualización automática: versiones, elección de Nexo.apk, verificación y sin red."
    dependsOn("compileDebugKotlin", "compileDebugUnitTestKotlin")
    classpath = files(
        tasks.named("compileDebugKotlin").map { it.outputs.files },
        tasks.named("compileDebugUnitTestKotlin").map { it.outputs.files },
        configurations.named("debugUnitTestRuntimeClasspath")
    )
    mainClass.set("com.nexo.launcher.core.AppUpdateChecks")
}
