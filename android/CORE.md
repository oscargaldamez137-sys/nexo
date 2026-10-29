# Núcleo Android de Nexo

`NexoCores.create(context)` devuelve `AndroidNexoCore`. La API de `Model.kt` conserva los métodos existentes y añade extracción/borrado de comprimidos y preparación de archivos del sistema.

## Biblioteca y primer inicio

Elige una carpeta local, por ejemplo `Nexo`, mediante `OpenDocumentTree`. Se conserva su permiso de lectura/escritura en SharedPreferences. El primer `scan` crea `Juegos/<Id>/LEEME.txt` y `BIOS/<Id>/LEEME.txt` para las doce consolas, sin sobrescribir archivos existentes. Esta inicialización y el escaneo se ejecutan en IO, no dentro del callback del selector.

`setLibraryRoot` puede lanzar una excepción con explicación en español si Android rechaza el permiso. La UI debe capturarla y conservar la biblioteca anterior. `scan` es cancelable: los proveedores reciben un `CancellationSignal`. Hay límites de profundidad (8), carpetas (2048), entradas totales (50 000) y entradas por carpeta (20 000). Se consultan columnas de `DocumentsContract` en una consulta por carpeta; no se utiliza `DocumentFile.listFiles`.

ZIP, 7z y RAR aparecen en `ScanResult.archives`. El escaneo valida su estructura sin descomprimir los juegos: directorio central/local y ZIP64, cabeceras y CRC de 7z, bloques y tamaños RAR4/RAR5. Una descarga incompleta o ilegible devuelve `ready = false` y el motivo en `message`, con tamaño esperado si la cabecera permite calcularlo. `ready = true` significa que pasa esa comprobación estructural; los CRC de los datos se verifican durante la extracción. PS3 admite ISO y carpetas con `PS3_GAME/USRDIR/EBOOT.BIN`, o `USRDIR/EBOOT.BIN`; el título puede proceder de PARAM.SFO. PS4/PS5 están disponibles en el catálogo visual, pero su estado es `Unavailable`.

## Descomprimir en Android

Contrato para la UI, sin cambios en `PendingArchive`:

```kotlin
data class ArchiveProgress(val bytesWritten: Long, val totalBytes: Long?)
// Constructor interno: el núcleo emite el recibo tras una extracción correcta.
class ArchiveExtractionResult {
    val archive: PendingArchive
    val directory: Uri
    val bytesWritten: Long
}
suspend fun extractArchive(
    archive: PendingArchive,
    onProgress: (ArchiveProgress) -> Unit = {}
): ArchiveExtractionResult
suspend fun deleteArchive(result: ArchiveExtractionResult)
```

Llamar desde una corrutina del ViewModel; `onProgress` vuelve al contexto del llamador, como máximo unas siete veces por segundo, más el resultado final. `totalBytes` es null mientras se inspecciona el contenido y después suma los tamaños sin comprimir. Cancelar el Job cancela la operación; **propagar `CancellationException`**, no convertirla en aviso de fallo. El trabajo de disco/decodificación se hace en IO. Solo se admite una extracción/borrado a la vez por instancia del núcleo.

Al terminar, reescanear el sistema. El resultado queda en la misma carpeta que el original, dentro de `<nombre sin extensión>/`, o `<nombre> (2)/` si existe. Se preservan los directorios interiores: una ISO de PSP y una carpeta `PS3_GAME` se detectan con las reglas normales del escáner. No se extraen archivos comprimidos anidados de forma automática.

El original **siempre se conserva**. La UI puede preguntar «¿Borrar el comprimido para liberar …?» y, solo al aceptar, llamar a `deleteArchive(result)`. Es un borrado permanente de SAF, no una Papelera. El núcleo exige su recibo de esta sesión, la misma biblioteca, que siga existiendo la carpeta extraída y que coincidan tamaño, fecha y huellas de las cabeceras/extremos del original. Tras reiniciar Nexo no se reutilizan recibos: el usuario puede borrar originales desde su gestor de archivos.

La extracción usa documentos SAF; no necesita permisos de acceso a todos los archivos ni duplica el comprimido en la caché. Requiere un proveedor local con acceso aleatorio y renombrado: almacenamiento interno o SD elegidos en el selector estándar. Comprueba el espacio del volumen seleccionado con `StorageManager`/`StatFs`, sin confundirlo con el espacio de la caché interna. Si el proveedor no permite comprobarlo, se explica que debe elegirse una biblioteca local. No se promete extracción en proveedores de nube.

Antes de crear nada se rechazan rutas absolutas, `..`, enlaces, nombres duplicados, conflictos archivo/carpeta y tamaños no válidos. Límites: 50 000 entradas, 256 GiB expandidos, relación máxima 1000:1 y 64 MiB libres de margen. Diccionario de decodificación limitado al menor de 128 MiB y un cuarto del heap (mínimo 8 MiB). No se admiten archivos con contraseña ni volúmenes partidos; el mensaje dirige al gestor de archivos. Se escribe en `.nexo-extract-<UUID>`, invisible para el escáner, y se renombra tras verificar todos los archivos. Un fallo/cancelación limpia solo ese temporal. Si Android mata el proceso o se retira la SD, puede quedar un temporal oculto; se puede borrar manualmente. Nunca se limpia una carpeta ajena por su nombre.

Dependencias de Maven Central, sin binarios nativos:

| Coordenadas | Uso |
|---|---|
| `org.apache.commons:commons-compress:1.28.0` | ZIP/ZIP64 y 7z |
| `org.tukaani:xz:1.12` | LZMA/LZMA2 de 7z |
| `com.github.junrar:junrar:8.1.2` | RAR4 y RAR5 |

Transitivas de estas versiones: `commons-codec:commons-codec:1.19.0`, `commons-io:commons-io:2.20.0`, `org.apache.commons:commons-lang3:3.18.0`, `org.slf4j:slf4j-api:2.0.17`. Resolverlas desde la sesión con red antes de usar `--offline`. No se añade zstd-jni ni ninguna descarga durante la ejecución de Nexo.

Referencias: [Commons Compress 1.28.0](https://commons.apache.org/compress/download_compress.cgi), [XZ for Java 1.12](https://tukaani.org/xz/java.html), [Junrar 8.1.2, soporte RAR5](https://github.com/junrar/junrar/releases/tag/v8.1.2), [formato RAR5](https://www.rarlab.com/technote.htm). Junrar se distribuye bajo [licencia UnRAR](https://github.com/junrar/junrar/blob/v8.1.2/LICENSE): su código no puede utilizarse para crear un compresor compatible con RAR. Nexo solo lo usa para extraer y no modifica la biblioteca. Se preservan los avisos de licencia de las dependencias en el APK.

## Emuladores y lanzamiento

| Consolas | Aplicación | Instalación oficial | Preparación dentro del emulador |
|---|---|---|---|
| Retro | RetroArch | APK aarch64 del buildbot | Primer inicio, permiso de almacenamiento y núcleos |
| GBC | GBC.emu | GitHub Rakashazi/emu-ex-plus-alpha, etiqueta `Pre-release`, `GbcEmu.zip` | No requiere BIOS externa |
| GBA | GBA.emu | Misma publicación oficial, `GbaEmu.zip` | No requiere BIOS externa |
| PS2 | ARMSX2 | GitHub ARMSX2/ARMSX2, release estable | Importar BIOS propia |
| PS3 | aPS3e | GitHub aenu1/aps3e | Instalar firmware PS3UPDAT.PUP |
| PSP | PPSSPP | ppsspp.org | Primer inicio; sin BIOS |
| GameCube/Wii | Dolphin | JSON de dolphin-emu.org y APK de dl.dolphin-emu.org | Configurar mando GameCube/Wii |
| Switch | Eden | Forgejo oficial y stable.eden-emu.dev | Importar claves propias y firmware compatible |
| Xbox | X1 BOX | GitHub izzy2lost/xemu | MCPX, BIOS flash y HDD |
| Xbox 360 | aX360e | Google Play | Elegir la carpeta de juegos en aX360e |

Los APK seleccionados requieren ARM64, salvo los APK generales de GBC.emu/GBA.emu (ver su sección). ARMSX2 elige SDK 35/33/30 según Android cuando `/proc/cpuinfo` informa las capacidades FP16 de la variante optimizada; en caso contrario usa el APK oficial legacy ARMv8.0 SDK26. Eden usa standard desde Android 13 y legacy en Android 10–12; no instala optimized, ChromeOS ni APK con otro applicationId. La disponibilidad de una app no equivale a compatibilidad de todos sus juegos: PS2, PS3, Switch y Xbox se marcan como experimentales según el catálogo.

Los lanzamientos usan componentes explícitos. Los juegos reciben `data`, permiso de lectura y `ClipData`. aPS3e recibe además `iso_uri` o `game_dir`; aX360e, `game_uri`. Dolphin se abre mediante MainActivity: EmulationActivity no está exportada. Abrir el emulador sin juego utiliza su actividad de inicio.

RetroArch necesita rutas reales. Se convierten exclusivamente documentos del proveedor de almacenamiento externo con volumen `primary:` o tarjeta `XXXX-XXXX:`; se rechazan proveedores remotos y segmentos `..`. Se usa el almacenamiento del usuario Android actual, no se presupone siempre el usuario 0. Se pasan ROM, LIBRETRO, CONFIGFILE, IME, DATADIR, APK, SDCARD y EXTERNAL. Las builds con almacenamiento restringido de Play pueden seguir necesitando la versión del buildbot o abrir el juego desde RetroArch.

Núcleos: fceumm (NES), snes9x (SNES), mupen64plus_next_gles3 (N64), genesis_plus_gx (Mega Drive/Master System/Game Gear), gambatte (GB/GBC), mgba (GBA). 32X se dirige a **picodrive**, porque Genesis Plus GX no emula 32X. Android normalmente impide inspeccionar la carpeta privada `cores` de otra app: los avisos indican abrir **RetroArch → Online Updater → Core Downloader**; un directorio inaccesible no se interpreta como núcleo ausente. Los juegos-carpeta de aPS3e también requieren una ruta local y acceso concedido dentro del propio emulador.

Nexo no escribe BIOS, claves, mandos ni perfiles gráficos dentro de los directorios privados de otras aplicaciones. Los avisos son instrucciones de preparación, no diagnósticos de archivos privados que no se pueden comprobar.

## Descargas e instalación

`installEmulator` inicia una operación asíncrona con diálogo de progreso cancelable. `LaunchResult.Started` significa que comenzó el flujo, no que Android haya terminado de instalar. El permiso «Instalar apps desconocidas» se solicita mediante Ajustes; después hay que volver y pulsar Instalar de nuevo. La instalación del APK siempre requiere la confirmación del usuario en Android.

La descarga se guarda únicamente en `cacheDir/install`. Se valida HTTPS y una lista de hosts en cada redirección, con límite de 6 saltos, 768 MB por APK, 15 s de conexión, 20 s por lectura y plazo global de 15 minutos. Los assets de GitHub deben pertenecer al repositorio esperado; se comprueba SHA256 cuando la API oficial lo publica. `PackageManager.getPackageArchiveInfo` comprueba applicationId y minSdk. Android valida la firma durante la instalación y rechazará una actualización con firma distinta. No se afirma una verificación criptográfica independiente cuando el proyecto no publica un hash.

Se utiliza `PackageInstaller`, sin FileProvider ni actividades añadidas al manifest. El callback se registra solo dentro de la aplicación; el receptor espera la confirmación del sistema. El APK temporal se elimina después de entregarlo a la sesión o ante un fallo/cancelación. Destruir la actividad cancela la descarga. No hay servicio de descarga en segundo plano ni reanudación tras terminar el proceso: mantén Nexo abierto hasta la confirmación. Si Android mata el proceso, puede ser necesario repetir Instalar.

## Arte

El escaneo no descarga imágenes. `ensureArt(game)` realiza la búsqueda aparte, con caché en `cacheDir/artwork`, escrituras atómicas y hasta tres descargas simultáneas. Un 404 se recuerda siete días; un fallo temporal, treinta minutos. Límite de 16 MB por imagen; se verifican tipo y dimensiones con lectura de cabecera (`BitmapFactory` con `inJustDecodeBounds`), sin decodificar todos los píxeles.

Portadas locales: `<nombre sin extensión>.png|jpg|jpeg|webp`, junto al juego o en `covers` de su carpeta o consola. Fondos: `<nombre>.bg.*`. Fondos generados: `<nombre>.ai.*`. Solo se devuelve un fondo de **1280×720 o mayor**, con límites de dimensiones y número de píxeles.

PS3 lee ICON0 y PIC1/PIC0 de la carpeta; PSP ISO los lee mediante ISO9660 sin extraer el disco. ICON0 es respaldo: no impide obtener una caja en línea. No se descomprimen CSO ni se descifran NCA de Switch. PS2 obtiene el serial de SYSTEM.CNF en ISO. GameCube/Wii lee ID6 y plataforma de ISO/GCM, RVZ/WIA, WBFS del primer slot y CISO con su primer bloque presente. Otros formatos conservan plataforma desconocida. Switch obtiene el Title ID del ticket público PFS0 de un NSP, o de `[0100…]` en el nombre; los paquetes con varios títulos no se adivinan.

Fuentes: xlenore por serial PS2; GameTDB por ID6 con `coverHQ → coverM → cover` y región; libretro-thumbnails `Named_Boxarts` por nombre No-Intro para el resto de formatos conocidos. No se descargan `Named_Snaps` ni se amplían fondos pequeños. Prioridad de fondo: sidecar del usuario → PIC1/PIC0 HD → generado. Sin fondo HD fiable se devuelve null y la UI compone la portada. En SharedPreferences `nexo`, `downloadArtwork = false` desactiva las consultas en línea; el arte local y la caché positiva siguen funcionando.

## GBC/GBA dedicados

Se elige la primera app instalada: GBC.emu/GBA.emu → SkyEmu → Pizza Boy gratis/Pro de esa consola → My OldBoy!/My Boy! → RetroArch. `EmulatorStatus.label` identifica la app real. GBC admite GB/GBC/DMG; GBA admite GBA/MB. Los comprimidos siguen pasando por el extractor de Nexo.

La descarga oficial de EmuEx usa `/releases/tags/Pre-release` (publicación continua), verifica el SHA256 **del ZIP**, extrae su único APK y comprueba su paquete/minSdk. No se sustituye una app dedicada ya instalada, que puede llevar la firma de Play. A diferencia de las demás descargas seleccionadas, estos APK generales no se restringen a ARM64 desde Nexo: Android comprueba la ABI al instalarlos.

GBC.emu/GBA.emu reciben `ACTION_VIEW`, `com.imagine.BaseActivity`, URI y MIME `*/*`. SkyEmu recibe `ACTION_VIEW` y `.EnhancedNativeActivity`. Pizza Boy recibe `.MainActivity` y el extra String `rom_uri`; My Boy recibe `.EmulatorActivity` y data. Todas llevan `ClipData` y permiso de lectura. Las dos últimas rutas proceden del informe ES-DE y aún requieren prueba en un móvil: Nexo verifica que la actividad esté exportada y muestra una nota sobre la alternativa. RetroArch conserva sus extras y requisitos de almacenamiento/núcleos.

## BIOS, firmware y claves: contrato para la UI

```kotlin
enum class SystemSetupMode {
    NotRequired, MissingFiles, Guided, NeedsEdenGrant, Automatic, Installed
}
data class SystemSetupStatus(
    val systemId: String, val mode: SystemSetupMode, val folder: Uri?,
    val message: String, val steps: List<String> = emptyList()
)
data class SystemFilesResult(
    val status: SystemSetupStatus, val message: String,
    val openEmulatorIntent: Intent? = null
)
suspend fun setupStatus(systemId: String): SystemSetupStatus
fun edenGrantIntent(): Intent
fun onEdenGrant(uri: Uri)
suspend fun installSystemFiles(
    systemId: String,
    onProgress: (ArchiveProgress) -> Unit = {}
): SystemFilesResult
```

`setupStatus` consulta SAF en IO y actualiza las notas de `emulatorStatus`; el escaneo también las refresca. Volver a leer `emulatorStatus` tras la consulta si la UI conserva una copia. `MissingFiles` habla de los archivos de **la biblioteca**, no demuestra que el emulador carezca de ellos en su carpeta privada. `Guided` tampoco afirma que esté configurado.

Para `NeedsEdenGrant`, lanzar `edenGrantIntent()` mediante `ActivityResultContracts.StartActivityForResult`. En `RESULT_OK`, pasar `data?.data` a `onEdenGrant(uri)` y volver a consultar/importar. Si cancela, no llamar a `onEdenGrant`. Capturar los errores y mostrar su mensaje. El intent sugiere `root/` del proveedor `<paquete Eden instalado>.user`; se acepta **solo esa raíz y el proveedor del paquete instalado**, con permiso persistente de lectura/escritura. Si la edición de Eden no ofrece el proveedor o el selector no lo muestra, usar la instalación del propio Eden. El permiso del árbol de Nexo no autoriza por sí solo escribir en Eden.

`installSystemFiles` no necesita Activity. En sistemas guiados devuelve instrucciones e intent de inicio, sin enviar una BIOS/PUP como si fuera un juego. En Switch copia `prod.keys`, `title.keys` opcional y las NCA de un único ZIP desde `BIOS/Switch` a `keys/` y `nand/system/Contents/registered/`. El ZIP se prepara y verifica primero en la caché (requiere espacio adicional); un fallo de validación no modifica Eden. Las claves existentes se abren con **`wt`**, no se crean duplicados renombrados. Antes de sobrescribir se hacen respaldos; un error/cancelación restaura los anteriores y retira los nuevos. No se borran los archivos ajenos de Eden ni los originales de la biblioteca. Cerrar Eden antes y reiniciarlo después.

El progreso se entrega en el contexto del llamador y cuenta preparación/copia; cancelar el Job cancela el trabajo. Si falla también la restauración, se informa y se conservan los respaldos en la caché. Si Android mata el proceso durante una escritura, reimportar los originales antes de jugar. `Installed` significa que se encontraron claves con formato válido y archivos NCA en Eden: **no certifica firmware completo ni compatibilidad entre versiones**. Nexo no descifra NCA ni deduce la correspondencia entre versiones por el nombre de un ZIP; Eden realiza esas comprobaciones al arrancar.

Pasos guiados: ARMSX2 importa la carpeta `BIOS/PS2` desde su asistente/BIOS Manager; aPS3e instala `BIOS/PS3/PS3UPDAT.PUP` desde QuickStart (jamás se escribe `dev_flash`); X1 BOX selecciona MCPX, flash y HDD de `BIOS/Xbox` en su asistente y luego los juegos; aX360e elige `Juegos/Xbox360`, sin BIOS. RetroArch permite elegir `BIOS/Retro` en Ajustes → Directorios → Sistema/BIOS. Las instrucciones devuelven las rutas locales reales cuando el proveedor lo permite. No se descargan BIOS, claves ni firmware desde esta API.

Fuentes: los informes del proyecto `work/android-gb-emuladores.md` y `work/android-bios.md`, y la [publicación oficial EmuEx](https://github.com/Rakashazi/emu-ex-plus-alpha/releases/tag/Pre-release). El selector/proveedor de Eden y los intents de alternativas necesitan además verificación en el dispositivo.

## Pruebas y compilación

```powershell
$env:JAVA_HOME='<ruta a un JDK 17 o posterior>'
cd android
.\gradlew.bat testDebugUnitTest assembleDebug --offline
```

`app/src/test/.../CoreLogicChecks.kt`, `ArchiveChecks.kt` y `SystemFilesChecks.kt` contienen ejecutables JVM con aserciones, invocados por `:app:coreLogicTest`, `:app:archiveTest` y `:app:systemFilesTest`, dependencias de `testDebugUnitTest`. No son métodos anotados de JUnit: esa biblioteca no estaba en la caché. Cada fallo hace fallar Gradle; la ausencia de tests JUnit se permite expresamente. Las pruebas generan ZIP/ZIP64/7z/RAR4/RAR5 mínimos y comprueban extracción, truncados, CRC, rutas/enlaces, límites, falta de espacio, cancelación, conservación del original y limpieza del temporal. No usan Android, red ni emuladores instalados. El adaptador SAF necesita además una prueba en el dispositivo.

**El proyecto mantiene compileSdk35, targetSdk35 y minSdk26.** `android/local.properties` (no incluido en git) debe indicar en `sdk.dir` un Android SDK con la plataforma 35. Para el APK que se instale en el móvil, compila con el comando anterior y la clave de depuración habitual.

La compilación y las pruebas JVM no sustituyen una prueba en el teléfono de SAF, instalación de APK, permisos y arranque de cada emulador. Esos flujos deben comprobarse en un dispositivo Android real.

## Referencias comprobadas

- [RetroArch 1.22.2: extras de lanzamiento](https://github.com/libretro/RetroArch/blob/v1.22.2/pkg/android/phoenix/src/com/retroarch/browser/mainmenu/MainMenuActivity.java).
- [ARMSX2: release estable y assets SDK30/33/35/legacy con SHA256](https://api.github.com/repos/ARMSX2/ARMSX2/releases/latest).
- [PPSSPP 1.20.4: actividad pública](https://github.com/hrydgard/ppsspp/blob/v1.20.4/android/AndroidManifest.xml).
- [Dolphin 2609a: ClipData y URI de inicio](https://github.com/dolphin-emu/dolphin/blob/2609a/Source/Android/app/src/main/java/org/dolphinemu/dolphinemu/utils/StartupHandler.kt) y [JSON de descarga](https://dolphin-emu.org/update/latest/beta/).
- [Eden 0.2.1: manifest del espejo oficial](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/android/app/src/main/AndroidManifest.xml).
- Los identificadores de aPS3e, aX360e y X1 BOX y el endpoint Forgejo proceden de un informe de investigación interno, no incluido en el repositorio. No se afirma una comprobación de instalación real de esos APK.

## Opciones, carpeta y Switch (1.1.0)

- `NexoCore.settings` (`NexoSettings`) guarda en las preferencias `nexo`: `checkUpdates` (comprobación al abrir; `checkAppUpdate(manual = false)` no hace ninguna petición si está desactivada), `downloadArtwork` (la misma clave que ya leía `AndroidArtwork`) y `hideSwitchKeysNotice`. Todo por defecto como en 1.0.0.
- `setLibraryRoot` puede llamarse otra vez desde la barra superior u Opciones: guarda la nueva carpeta, suelta el permiso persistente de la anterior sin tocar su contenido y la interfaz vuelve a escanear.
- `LibraryLayout`: los juegos van en `Juegos/<Id>`; si la biblioteca no tiene `Juegos` y la carpeta elegida se llama `Juegos` o ya contiene carpetas de consola (`Switch`, `Nintendo Switch`, `NSW`…), se usa esa carpeta directamente.
- `ShelfRules.kind` decide qué es cada archivo solo por su nombre: ni claves, ni firmware, ni tamaño ocultan juegos. NSZ/XCZ aparecen como pendientes con su explicación. Como Android no deja leer los datos privados de Eden, no ver `prod.keys` es un aviso (`SwitchNotice`) que nunca bloquea el lanzamiento y se puede ocultar.
