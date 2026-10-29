# Nexo

Nexo es un lanzador para **Windows** y **Android** que reúne en un solo lugar tus consolas, tus emuladores y tus juegos. Eliges una consola en un carrusel, bajas a su estantería de juegos y Nexo abre el emulador correcto con el juego, a pantalla completa y con el mando preparado.

Autor: **Oscar Galdámez**. Web del proyecto: <https://nova5emu.github.io/nova5/nexo.html>

[☕ Donar en Ko-fi](https://ko-fi.com/nova5emu)

## Funciones principales

- **Carrusel de consolas y estantería de juegos**, manejable con ratón, teclado, pantalla táctil (Android) o mando (XInput en Windows).
- **Detección de emuladores** copiados en la carpeta de Nexo e **instalación desde las fuentes oficiales** de cada emulador cuando el usuario lo pide.
- **Biblioteca por consola**: busca los juegos en su carpeta, reconoce los formatos de cada sistema y avisa de descargas incompletas o contenedores no compatibles.
- **Carátulas y fondos**: usa primero las imágenes locales del juego (o las que añada el usuario) y, si faltan, las busca en GameTDB, libretro-thumbnails y xlenore/ps2-covers. La descarga en línea se puede desactivar.
- **Juegos comprimidos**: extracción de ZIP, 7z y RAR (y de ISO de PS3 en Windows), conservando siempre el original.
- **Preparación del emulador** (Windows): perfil gráfico según la GPU y la pantalla, configuración del mando y arranque a pantalla completa, sin tocar los ajustes personales que ya existan.
- **Archivos del sistema del usuario**: ayuda a importar en el emulador la BIOS, el firmware o las claves que el usuario haya obtenido de su propia consola, con validación básica y copia de seguridad de lo sustituido.
- **Instalación de PKG fake/de desarrollo de PS4** para shadPS4 (Windows).
- **Todo en su carpeta**: en Windows, la biblioteca y la configuración viven dentro de la carpeta de instalación. Sin servidor, navegador ni puertos locales.
- **Actualización automática** desde las versiones publicadas en GitHub, en Windows y Android (ver más abajo).

## Consolas soportadas

| Consola | Windows | Android |
| --- | --- | --- |
| Retro (NES, SNES, N64, Mega Drive, Master System, Game Gear, PC Engine...) | RetroArch | RetroArch |
| Game Boy / Game Boy Color | mGBA | GBC.emu |
| Game Boy Advance | mGBA | GBA.emu |
| PS2 | PCSX2 | ARMSX2 |
| PS3 | RPCS3 | aPS3e |
| PSP | PPSSPP | PPSSPP |
| GameCube / Wii | Dolphin | Dolphin |
| PS4 | shadPS4 | - |
| PS5 | Nova5 (experimental) | - |
| Switch | Eden, Ryujinx, Citron, yuzu | Eden |
| Xbox | xemu | X1 BOX |
| Xbox 360 | Xenia Canary (experimental) | aX360e |

La compatibilidad real depende de cada emulador, de su versión, del juego y del equipo. Que Nexo detecte un emulador no garantiza que funcione en tu PC o teléfono.

## Instalación

### Windows

Requiere Windows 10/11 con **.NET Framework 4.8**.

1. Descarga **`Instalar-Nexo.exe`** de la [última versión](https://github.com/oscargaldamez137-sys/nexo/releases/latest) y ejecútalo.
2. Elige una carpeta (por defecto `C:\Nexo`).
3. Abre **Nexo** desde el Escritorio o el menú Inicio.

Nexo se distribuye solo con este instalador. Dentro de la carpeta elegida crea `Emuladores\<consola>` y `Juegos\<consola>`; ahí viven también la configuración y las portadas. Las versiones nuevas se instalan solas (ver *Actualizaciones automáticas*); si lo prefieres, ejecutar de nuevo el instalador sobre la misma carpeta también actualiza y conserva emuladores, juegos, portadas y configuración.

### Android

Requiere Android 8.0 o superior (la mayoría de emuladores necesitan un dispositivo ARM64).

1. Descarga **`Nexo.apk`** de la [última versión](https://github.com/oscargaldamez137-sys/nexo/releases/latest).
2. Instálalo permitiendo "Instalar apps desconocidas" para el navegador o el gestor de archivos.
3. Al abrir Nexo, elige la carpeta de tu biblioteca; Nexo crea dentro `Juegos/<consola>` y `BIOS/<consola>`.

## Actualizaciones automáticas

Nexo consulta al abrirse la última versión publicada en <https://github.com/oscargaldamez137-sys/nexo/releases>. Sin conexión, o si GitHub no responde en unos segundos, sigue funcionando sin avisar. La comprobación no se puede desactivar porque Nexo no tiene pantalla de ajustes.

- **Windows**: si hay una versión nueva, un aviso en la barra superior muestra la descarga de `Instalar-Nexo.exe` con su porcentaje. Nexo comprueba el tamaño y la huella SHA-256 que publica GitHub y, cuando no estás descomprimiendo, instalando ni jugando, cuenta 8 segundos y se actualiza solo: se cierra, el instalador sustituye el programa en la misma carpeta sin mostrar ventanas, conserva `Emuladores`, `Juegos` y `nexo.json` y vuelve a abrir Nexo. Durante la cuenta atrás, **B** en el mando o **Esc** lo dejan para cuando cierres Nexo. Si la carpeta no permite escribir, o si una instalación automática falló, Nexo lo avisa y el aviso abre la página de descargas.
- **Android**: Nexo descarga `Nexo.apk` con su progreso en la barra superior, comprueba tamaño, SHA-256, paquete y versión, y abre el instalador del sistema. Android siempre pide confirmar; si falta el permiso «Instalar apps desconocidas» para Nexo, te lleva a ese ajuste y la actualización sigue al volver. Si cancelas, el aviso (o **Y** en el mando) la vuelve a abrir. El APK nuevo debe estar firmado con la misma clave que el instalado.

## Publicar una versión nueva

Para que todas las instalaciones se actualicen solas:

1. Sube la versión en `src/Nexo/Nexo.csproj` (`<Version>1.0.1</Version>`) y en `android/app/build.gradle.kts` (`val nexoVersion = "1.0.1"`). Usa siempre `X.Y.Z`, con `Y` y `Z` entre 0 y 99: el `versionCode` de Android se calcula como `X*10000 + Y*100 + Z`.
2. Compila Windows (`build.ps1`) y Android (`gradlew.bat assembleRelease`), en el mismo PC de siempre para que el APK conserve la firma.
3. En GitHub, crea una *release* con la etiqueta `vX.Y.Z` (por ejemplo `v1.0.1`), no marcada como *pre-release*, y adjunta exactamente estos dos archivos:
   - `Instalar-Nexo.exe`: `dist\Instalar Nexo.exe` renombrado.
   - `Nexo.apk`: `android/app/build/outputs/apk/release/app-release.apk` renombrado.

GitHub calcula la huella SHA-256 de cada archivo y Nexo la comprueba antes de instalar. Una versión sin alguno de los dos archivos no se ofrece en esa plataforma.

## Qué no incluye

Nexo **no incluye ni descarga** juegos, BIOS, firmware, claves, licencias ni módulos del sistema de ninguna consola. Debes obtenerlos legalmente de tus propias consolas y juegos. Los emuladores son proyectos independientes: Nexo solo los descarga desde sus fuentes oficiales cuando tú lo pides, o usa los que ya tengas.

## Compilar

### Windows

Requisitos: un SDK de .NET con MSBuild (se ha usado .NET SDK 10), el *targeting pack* de **.NET Framework 4.8** y Windows PowerShell 5.1. El instalador se genera con IExpress, incluido en Windows.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

El script compila sin descargar paquetes, ejecuta las pruebas de `tests/Nexo.Tests` y una prueba de instalación, actualización y desinstalación, y genera el instalador `dist\Instalar Nexo.exe`, con la versión de `Nexo.csproj` en sus propiedades. El programa compilado que lleva dentro se prepara en `dist\package` y no es un entregable aparte.

### Android

Requisitos: JDK 17 o posterior y un Android SDK con la plataforma 35. Crea `android/local.properties` (no se incluye en git) con la ruta del SDK, por ejemplo `sdk.dir=C\:\\Android\\Sdk`.

```powershell
cd android
.\gradlew.bat testDebugUnitTest assembleRelease
```

El APK queda en `android/app/build/outputs/apk/release/`. La variante *release* se firma con la clave de depuración local, pensada para instalación manual. Si las dependencias ya están en la caché de Gradle, puedes añadir `--offline`.

## Documentación

La guía completa (formatos de cada consola, preparación de cada emulador, carátulas, mandos, perfiles gráficos, BIOS y firmware) está en [LEEME.md](LEEME.md). Los detalles del núcleo de Android están en [android/CORE.md](android/CORE.md).

## Donar

[☕ Donar en Ko-fi](https://ko-fi.com/nova5emu). En la app, el botón **Donar** de la barra superior abre la misma página (con mando: **View** en Windows, **Select** en Android).

## Licencia

Nexo se distribuye bajo la [licencia MIT](LICENSE). Copyright (c) 2026 Oscar Galdámez.

Algunos componentes e imágenes tienen licencias propias (por ejemplo, junrar con la licencia UnRAR y la foto de PS5 con CC BY-SA 4.0). Consulta [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

PlayStation, Xbox, Nintendo, Switch y las demás marcas citadas pertenecen a sus respectivos dueños. Nexo es un proyecto independiente y no está afiliado a ninguno de ellos.
