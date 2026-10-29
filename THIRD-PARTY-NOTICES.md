# Avisos de terceros

Nexo se distribuye bajo la licencia MIT (ver [LICENSE](LICENSE)). Algunos componentes, datos e imágenes que acompañan a Nexo proceden de terceros y conservan sus propias licencias, que se resumen aquí. Ante cualquier duda prevalece el texto de la licencia original de cada proyecto.

## Windows (`src/Nexo`, `src/Nexo.Core`)

La aplicación de Windows solo utiliza ensamblados de .NET Framework 4.8 incluidos en Windows; no incorpora paquetes NuGet de terceros.

### Constantes de PKG "fake" de desarrollo de PS4

`src/Nexo.Core/Ps4PkgCrypto.cs` contiene los números públicos de las claves RSA "fake" de desarrollo para paquetes PKG de PS4, tal como los documenta el archivo `Util/Keys.cs` del proyecto **LibOrbisPkg** (autor: maxton), <https://github.com/maxton/LibOrbisPkg>. Se usan únicamente como constantes de interoperabilidad del formato para paquetes fake/de desarrollo; no son claves de consolas comerciales. El código de análisis y descifrado de Nexo es propio. Consulta la licencia de LibOrbisPkg en su repositorio.

## Android (`android/`)

Bibliotecas que se incluyen en el APK, descargadas de Maven Central / Google Maven durante la compilación:

| Componente | Versión | Licencia |
| --- | --- | --- |
| AndroidX (Compose BOM 2025.01.00: Compose UI, Foundation, Material 3; Activity Compose, Core KTX, Lifecycle ViewModel Compose) | según `android/app/build.gradle.kts` | Apache-2.0 |
| Kotlin standard library | la del plugin de Kotlin del proyecto | Apache-2.0 |
| Coil 3 (`coil-compose`, `coil-network-okhttp`) | 3.0.4 | Apache-2.0 |
| OkHttp / Okio | dependencias de Coil | Apache-2.0 |
| Apache Commons Compress | 1.28.0 | Apache-2.0 |
| Apache Commons Codec | 1.19.0 (transitiva) | Apache-2.0 |
| Apache Commons IO | 2.20.0 (transitiva) | Apache-2.0 |
| Apache Commons Lang | 3.18.0 (transitiva) | Apache-2.0 |
| SLF4J API | 2.0.17 (transitiva de junrar) | MIT |
| XZ for Java (`org.tukaani:xz`) | 1.12 | Dominio público / 0BSD |
| junrar (`com.github.junrar:junrar`) | 8.1.2 | Licencia UnRAR (ver abajo) |

El Gradle Wrapper (`android/gradlew`, `android/gradlew.bat`, `android/gradle/wrapper/gradle-wrapper.jar`) pertenece a Gradle y se distribuye bajo Apache-2.0.

La configuración de empaquetado del APK conserva los archivos `META-INF/LICENSE` y `META-INF/NOTICE` de estas bibliotecas.

### junrar y la licencia UnRAR

junrar (<https://github.com/junrar/junrar>) deriva del código fuente de UnRAR y se distribuye bajo la licencia UnRAR. Nexo lo usa exclusivamente para **extraer** archivos RAR4/RAR5. La licencia UnRAR permite usar y redistribuir ese código para descomprimir archivos RAR, pero **prohíbe utilizarlo para desarrollar un compresor RAR** o para recrear el algoritmo de compresión RAR. Cualquier redistribución o modificación de Nexo debe respetar esta condición. Texto completo: <https://github.com/junrar/junrar/blob/v8.1.2/LICENSE>.

## Fotografías de las consolas (`src/Nexo/Photos`)

Las fotos se usan en Windows y en Android. Proceden de **Wikimedia Commons**; cada enlace lleva a la ficha del archivo, con su autoría, historial y licencia.

| Archivo en Nexo | Original en Wikimedia Commons | Autor y licencia |
| --- | --- | --- |
| `Retro.png` | [SNES-Model-2-Set.png](https://commons.wikimedia.org/wiki/File:SNES-Model-2-Set.png) | Evan-Amos; dominio público |
| `PS2.png` | [PS2-Fat-Console-Set.png](https://commons.wikimedia.org/wiki/File:PS2-Fat-Console-Set.png) | Evan-Amos; dominio público |
| `PS3.png` | [Sony-PlayStation-3-4001B-wController-noBG.png](https://commons.wikimedia.org/wiki/File:Sony-PlayStation-3-4001B-wController-noBG.png) | Evan-Amos; dominio público |
| `PSP.png` | [Sony-PSP-1000-Body.png](https://commons.wikimedia.org/wiki/File:Sony-PSP-1000-Body.png) | Evan-Amos; dominio público |
| `GameCube-Wii.png` | [GameCube-Console-Set.png](https://commons.wikimedia.org/wiki/File:GameCube-Console-Set.png) | Evan-Amos; dominio público |
| `PS4.png` | [PS4-Console-wDS4.png](https://commons.wikimedia.org/wiki/File:PS4-Console-wDS4.png) | Evan-Amos; dominio público |
| `Switch.png` | [Nintendo-Switch-wJoyCons-BlRd-Standing-FL.png](https://commons.wikimedia.org/wiki/File:Nintendo-Switch-wJoyCons-BlRd-Standing-FL.png) | Evan-Amos, modificado por Gunnar.offel; dominio público |
| `PS5.png` | [PlayStation 5 and DualSense with transparent background.png](https://commons.wikimedia.org/wiki/File:PlayStation_5_and_DualSense_with_transparent_background.png) | Fotografía de Osh33m; eliminación del fondo por Soberian; [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| `GBC.png` | [Nintendo-Game-Boy-Color-FL.png](https://commons.wikimedia.org/wiki/File:Nintendo-Game-Boy-Color-FL.png) | Evan-Amos; dominio público |
| `GBA.png` | [Nintendo-Game-Boy-Advance-Purple-FL.png](https://commons.wikimedia.org/wiki/File:Nintendo-Game-Boy-Advance-Purple-FL.png) | Evan-Amos; dominio público |
| `Xbox.png` | [Xbox-Console-Set.png](https://commons.wikimedia.org/wiki/File:Xbox-Console-Set.png) | Evan-Amos; dominio público |
| `Xbox360.png` | [Xbox-360-Pro-wController.png](https://commons.wikimedia.org/wiki/File:Xbox-360-Pro-wController.png) | Evan-Amos; fondo eliminado; dominio público |

Las fotos se presentan escaladas en el carrusel de Nexo. `PS5.png` y cualquier adaptación de esa imagen **no** están cubiertas por la licencia MIT de Nexo: conservan la licencia **CC BY-SA 4.0**, con los créditos indicados y el enlace a la licencia. La atribución no implica que sus autores respalden Nexo.

## Carátulas descargadas

Las carátulas que Nexo descarga a la biblioteca del usuario (GameTDB, libretro-thumbnails, xlenore/ps2-covers) no forman parte de este repositorio ni del instalador. Pertenecen a sus respectivos autores y editores.

## Marcas

PlayStation, PS2, PS3, PS4, PS5 y PSP son marcas de Sony Interactive Entertainment. Xbox y Xbox 360 son marcas de Microsoft. Nintendo, Nintendo Switch, GameCube, Wii, Game Boy, Game Boy Color, Game Boy Advance, NES, Super NES y Nintendo 64 son marcas de Nintendo. Sega, Mega Drive, Genesis, Master System y Game Gear son marcas de Sega. Los nombres de emuladores (RetroArch, PCSX2, RPCS3, PPSSPP, Dolphin, shadPS4, Eden, mGBA, xemu, Xenia y otros) pertenecen a sus respectivos proyectos. Todas las demás marcas pertenecen a sus dueños.

Nexo es un proyecto independiente: no está afiliado, patrocinado ni respaldado por Sony, Microsoft, Nintendo, Sega ni por los autores de los emuladores que puede abrir. Las marcas se mencionan solo para identificar las consolas y programas compatibles.
