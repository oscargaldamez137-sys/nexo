# Nexo

Una aplicación nativa de Windows para reunir tus consolas, emuladores y juegos. No usa servidor, navegador ni puertos locales. Su biblioteca y configuración viven dentro de la carpeta de instalación.

## Instalar y abrir

1. Descarga **Instalar-Nexo.exe** de la [última versión](https://github.com/oscargaldamez137-sys/nexo/releases/latest) y ábrelo.
2. Elige una carpeta; el instalador propone **C:\Nexo**. Necesitas permiso para escribir en ella.
3. Abre **Nexo** desde el Escritorio o el menú Inicio.

Está dirigida a Windows 10/11 con **.NET Framework 4.8**. No requiere .NET 10 ni Python. El instalador no descarga ni instala componentes. Una instalación de Windows que no tenga Framework 4.8 deberá prepararse por separado. Este prototipo no está firmado digitalmente.

Nexo se distribuye solo con este instalador. En el primer arranque crea las carpetas que falten y un LEEME breve en cada una. Una copia de `Nexo.exe` que no haya pasado por el instalador (por ejemplo, la compilación de `dist\package\app`) funciona igual, pero no se actualiza sola: si hay una versión nueva, avisa de que hay que instalar Nexo.

El botón **☕ Donar** de la barra superior (con mando, **View**) abre <https://ko-fi.com/nova5emu> en el navegador.

## Tus emuladores y juegos

Copia cada emulador **completo**, con sus DLL, recursos y subcarpetas, en `Emuladores\<Id>`, o utiliza la instalación oficial disponible en Core para conectar al botón de la interfaz. Coloca tus juegos en `Juegos\<Id>`. El instalador pequeño de Nexo contiene el lanzador y sus imágenes; las descargas de emuladores son operaciones separadas que inicia el usuario. La instalación automática incluye los núcleos oficiales de RetroArch. No incluye juegos, BIOS, firmware ni claves.

## Qué necesitas preparar aparte

- **PS2 / PCSX2:** importa una BIOS `.bin` extraída de tu PS2. Nexo la coloca en `bios`, la selecciona si no hay otra elección y prepara el primer arranque y el mando. Nexo no la incluye. Consulta la [guía oficial de BIOS](https://pcsx2.net/docs/setup/bios/).
- **PS3 / RPCS3:** descarga `PS3UPDAT.PUP` desde la [página oficial de Sony](https://www.playstation.com/en-us/support/hardware/ps3/system-software/) e impórtalo. Nexo abre RPCS3 con `--installfw`; completa la instalación en su ventana. Algunos juegos digitales necesitan también sus archivos de licencia. Nexo no incluye firmware ni licencias y no instala PKG.
- **PSP / PPSSPP:** tus juegos y la configuración del mando. No necesita una BIOS externa para jugar; conserva la carpeta completa del emulador y sus recursos. [FAQ oficial](https://www.ppsspp.org/docs/faq/).
- **GameCube/Wii / Dolphin:** tus juegos y configurar el mando de GameCube o el Wiimote emulado/real. Los contenidos que requieren una NAND o instalación en el sistema Wii se preparan dentro de Dolphin; Nexo no aporta esos datos.
- **PS4 / shadPS4:** juegos extraídos y los módulos del sistema que requiera su versión/juego, obtenidos de tu consola siguiendo la [guía oficial](https://github.com/shadps4-emu/shadPS4/wiki/I.-Quick-start-%5BUsers%5D). Nexo no incluye módulos, claves ni juegos. La build SDL instalada usa Big Picture al pulsar «Abrir emulador».
- **Retro / RetroArch:** la instalación automática descarga RetroArch y sus núcleos. Una instalación existente se conserva sin reinstalar; si le faltan núcleos, añádelos desde RetroArch. Algunos sistemas/núcleos necesitan BIOS o firmware adicional en el directorio `system`; esos archivos no vienen con Nexo.
- **Switch / Eden:** importa `prod.keys`, opcionalmente `title.keys`, y el ZIP de firmware de tu consola. Se guardan bajo `user\keys` y `user\nand\system\Contents\registered`. Después elige el mando una vez en Eden. Nexo no descarga claves ni firmware de Switch.
- **PS5 / Nova5:** Nexo instala la última versión de Nova5 desde su publicación oficial y abre los juegos con sus ajustes. Soporte experimental: que un juego aparezca no garantiza que funcione.
- **GBC / GBA / mGBA:** copia tus ROMs en la carpeta de cada portátil. Comparten una instalación de mGBA; la BIOS de GBA es opcional y no se solicita.
- **Xbox / xemu:** importa MCPX `.bin` de 512 bytes y una BIOS flash `.bin` compatible con xemu de 256/512 KiB o 1 MiB. La descarga de xemu incluye el disco duro inicial oficial con el dashboard libre del proyecto; también puedes importar tu `.qcow2` autónomo. Una BIOS retail sin modificar no sirve: consulta los [archivos requeridos por xemu](https://xemu.app/docs/required-files/). Nexo comprueba estructura/tamaño, no puede certificar una BIOS por su tamaño.
- **Xbox 360 / Xenia Canary:** no necesita BIOS. Conserva completos los juegos extraídos y las carpetas `.data` que acompañan a los contenedores GOD. La compatibilidad varía por juego y Xenia Canary se muestra como experimental.

Que Nexo detecte un ejecutable no significa que ese emulador esté configurado o que el PC cumpla sus requisitos. Cada uno puede necesitar controladores gráficos y componentes de ejecución propios, además del .NET Framework utilizado por Nexo.

| Carpeta | Emulador reconocido | Juegos que busca Nexo | Aspecto en la biblioteca |
| --- | --- | --- | --- |
| `Retro` | RetroArch | `.nes`, `.sfc`, `.smc`, `.gba`, `.gb`, `.gbc`, `.md`, `.gen`, `.smd`, `.n64`, `.z64`, `.v64`, `.pce`, `.sms`, `.gg` | Cartucho |
| `PS2` | PCSX2 | `.iso`, `.chd`, `.cso`, `.bin`, `.gz` | Caja de DVD |
| `PS3` | RPCS3 | Carpetas con `PS3_GAME\USRDIR\EBOOT.BIN` o `USRDIR\EBOOT.BIN`; ISO descifrada para extraer | Caja de Blu-ray |
| `PSP` | PPSSPP | `.iso`, `.cso`, `.pbp`, `.chd`, `.elf` | UMD |
| `GameCube-Wii` | Dolphin | `.iso`, `.gcm`, `.wbfs`, `.rvz`, `.ciso`, `.gcz`, `.wad`, `.dol`, `.elf` | Minidisco |
| `PS4` | shadPS4 | Carpeta con `eboot.bin` y `sce_sys\param.sfo` o `param.json`; PKG fake/de desarrollo para instalar | Disco |
| `Switch` | Ryujinx, Eden, Citron, yuzu | `.nsp`, `.xci`, `.nca`, `.nro` | Tarjeta de juego |
| `PS5` | Nova5 | Carpeta con `eboot.bin` y `sce_sys\param.json` | Disco, experimental |
| `GBC` | mGBA (compartido con GBA) | `.gbc`, `.gb` | Cartucho |
| `GBA` | mGBA | `.gba` | Cartucho |
| `Xbox` | xemu | `.iso` en formato XISO | Caja de DVD |
| `Xbox360` | Xenia Canary | `.iso`, `.xex`, juegos STFS/GOD sin extensión | Caja de DVD, experimental |

Por ejemplo:

```text
C:\Nexo\Nexo.exe
C:\Nexo\Emuladores\PS2\PCSX2\pcsx2-qt.exe
C:\Nexo\Juegos\PS2\Mi juego.iso
C:\Nexo\Juegos\PS2\Mi juego.png
C:\Nexo\Juegos\PS3\Mi juego\PS3_GAME\USRDIR\EBOOT.BIN
C:\Nexo\Juegos\PS4\Mi juego\eboot.bin
C:\Nexo\Juegos\PS4\Mi juego\sce_sys\param.sfo
```

**Elegir ejecutable** permite indicar manualmente un `.exe`, incluso fuera de la instalación. Tiene prioridad sobre la detección automática. Quitar esa selección vuelve a buscar dentro de `Emuladores\<Id>`. Si un ejecutable elegido desaparece, Nexo intenta detectar otro en esa carpeta.

Los juegos de PS3 se ejecutan extraídos: una ISO aparece como archivo pendiente y se prepara con **Descomprimir**. Nexo no ofrece arranque directo de ISO de PS3 ni instala PKG de PS3. Los archivos de actualizaciones, DLC y algunos NCA de Switch no son juegos arrancables por sí solos, aunque compartan extensión. Las extensiones visibles no garantizan compatibilidad con cada versión del emulador. PS5 está marcada como experimental; Switch 2 no se incluye.

**Retro:** las ROMs solo aparecen si está instalado un núcleo compatible en `cores`, junto a `retroarch.exe`. Nexo prueba, por orden: NES `mesen/fceumm/nestopia`; SNES `snes9x/bsnes`; GBA `mgba/vba_next`; GB/GBC `gambatte/sameboy/mgba`; Mega Drive `genesis_plus_gx/picodrive`; N64 `mupen64plus_next/parallel_n64`; PC Engine `mednafen_pce_fast/mednafen_pce`; SMS `genesis_plus_gx/picodrive`; GG `genesis_plus_gx/gearsystem`. Los archivos deben llamarse `<núcleo>_libretro.dll`.

## GBC, GBA, Xbox y Xbox 360

Las cuatro consolas utilizan los mismos botones de instalación, selección de ejecutable, arte y extracción de ZIP/7z/RAR. GBC y GBA buscan primero su ejecutable elegido o su instalación propia; si no existe, reutilizan la de la otra portátil. Una instalación nueva va bajo `Emuladores\GBA\Nexo-GBA` (o `Nexo-GBC` si se solicitó desde GBC); ambas comparten ajustes y mantienen sus juegos en carpetas separadas. No se trasladan instalaciones anteriores ni se duplican descargas. `SystemStatus.EmulatorFolder` indica la carpeta realmente utilizada.

Descargas fijadas:

| Emulador | Publicación oficial | Portable / configuración |
| --- | --- | --- |
| mGBA | [0.10.5](https://github.com/mgba-emu/mgba/releases/tag/0.10.5), `mGBA-0.10.5-win64.7z` | `portable.ini`, `config.ini` junto al exe |
| xemu | [v0.8.138](https://github.com/xemu-project/xemu/releases/tag/v0.8.138), `xemu-0.8.138-windows-x86_64.zip` | `xemu.toml` junto al exe |
| HDD inicial de xemu | [xemu-dashboard v20260516-0955](https://github.com/xemu-project/xemu-dashboard/releases/tag/v20260516-0955), `xbox_hdd.qcow2` | `system\xbox_hdd.qcow2`, dashboard libre del proyecto |
| Xenia Canary | [02d2cb5](https://github.com/xenia-canary/xenia-canary-releases/releases/tag/02d2cb5), `xenia_canary_windows_.zip` | `portable.txt`, `xenia-canary.config.toml` |

La instalación consulta la publicación oficial y verifica su SHA256 cuando está publicado. Además fija las huellas de xemu (`bb6d8b3a82ca514a162ebd0f237a912143238bfc635311f9835768eb2ceedba4`), su HDD (`00d7df7a2bc235f8801764f00b7f40e194d1e392f7a9619d6b2396c89770f6dd`) y Xenia (`1adbb2c1c69cbd5fabadb43d03b4c407db3b245de922e11aba699867c68d0796`). Una discrepancia detiene la instalación. El paquete y el HDD se preparan juntos en un temporal; no se publica una instalación a medias. Instalar de nuevo conserva el emulador existente y, por tanto, sus partidas/HDD.

En Xbox, **Importar archivos** acepta MCPX, BIOS flash y opcionalmente un HDD propio. Copia en `system\mcpx_1.0.bin`, `system\flash.bin` y `system\xbox_hdd.qcow2`, conserva los originales y guarda respaldo de archivos sustituidos. Solo completa rutas vacías del TOML con rutas relativas, sin reemplazar las elegidas manualmente. `SetupMessages` indica qué falta. Los HDD con archivo base externo, datos externos o cifrado se rechazan. Los discos de juego deben ser XISO: las imágenes Redump completas requieren conversión fuera de Nexo.

En Xbox 360 se reconocen contenedores sin extensión por su cabecera STFS/GOD y tipo de contenido; no por un nombre supuesto. Partidas, perfiles y DLC no aparecen como juegos independientes. Cuando existe `default.xex`, se usa el nombre de su carpeta y se omiten otros módulos `.xex` de esa misma carpeta. Las carpetas `.data` del GOD deben conservarse completas. El reconocimiento de metadatos no verifica íntegramente todos los bloques de un juego.

Al lanzar juegos se añaden `-f` en mGBA, `-full-screen -dvd_path <disco>` en xemu y `--fullscreen <juego>` en Xenia; **Abrir emulador** no añade estas opciones. Las plantillas personalizadas de `nexo.json` siguen teniendo prioridad.

Mandos: mGBA conserva los perfiles de fábrica y añade perfiles SDL para los nombres `Xbox 360 Controller` y `Xbox One Controller` si todavía no hay ajustes de gamepad guardados. Si existe un perfil personalizado, se conserva completo. xemu utiliza su asignación SDL automática al primer puerto libre (`auto_bind` de fábrica); no cambia puertos ya configurados. Xenia prepara `HID.hid = "xinput"` al detectar un mando XInput, únicamente si falta ese ajuste o sigue en `any`; otro controlador elegido se conserva. Sin mando conectado se espera al siguiente lanzamiento. Un dispositivo con otro nombre/perfil puede necesitar una selección manual dentro del emulador.

| Techo de calidad (limitado además por pantalla) | Bajo | Medio | Alto | Ultra |
| --- | --- | --- | --- | --- |
| xemu, `display.quality.surface_scale` | 1× | 2× | 3× | 4× |
| Xenia D3D12, `GPU.draw_resolution_scale_x/y` | 1× | 2× | 3× | 3× |
| mGBA, `ports.qt` | Escalado entero, sin suavizado | Igual | Igual | Igual |

mGBA usa `lockIntegerScaling=1` y `resampleVideo=0`, sin modificar velocidad ni añadir shaders. Xenia elige D3D12 solo si el backend sigue en `any`. Su techo automático de 3× es una decisión de este perfil, no una afirmación sobre el límite absoluto de todas las versiones. Como en las demás consolas, únicamente se actualizan valores de fábrica o con procedencia verificable de Nexo; se conservan los personalizados. La lectura TOML contempla los comentarios que añade Xenia y las comillas simples; formatos ambiguos quedan intactos. No se crean configuraciones por juego. El nivel de GPU no mide los FPS ni garantiza que todos los títulos sostengan la resolución elegida.

Las portadas se buscan por nombre en los repositorios oficiales de miniaturas de libretro: [GBC](https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Color), [GBA](https://github.com/libretro-thumbnails/Nintendo_-_Game_Boy_Advance), [Xbox](https://github.com/libretro-thumbnails/Microsoft_-_Xbox) y [Xbox 360](https://github.com/libretro-thumbnails/Microsoft_-_Xbox_360). Los `.gb` de GBC usan el repositorio de Game Boy. No todos los juegos tienen portada o el mismo nombre; el arte local tiene prioridad. Se mantiene el mínimo HD de fondos, sin descargar snaps pequeños. Se leen Title ID de XEX/STFS, pero no se ha integrado otra fuente de arte fiable por ese ID.

**Compatibilidad de Xbox:** la UI debe llamar a `Task<bool> NexoService.RefreshXboxCompatibilityAsync(CancellationToken ct = default)` fuera del escaneo. Si devuelve `true`, reescanea para recibir el nuevo `GameEntry.Compatibility`. Se usa `https://xemu.app/compat.json`, generado por el [sitio oficial](https://github.com/xemu-project/xemu-website/blob/master/generate.py), con caché semanal en `Juegos\Xbox\.nexo-cache\xemu-compatibility.json` y copia utilizable sin red. El escáner lee el Title ID del certificado de `default.xbe` dentro de la XISO sin extraerla. Mapea Broken→Nothing, Intro→Intro, Starts→Ingame, Playable→Playable y Perfect→Perfect, con explicación española y enlace al informe. La lista resumida no publica fecha, versión ni OS; esos campos quedan vacíos. IDs alternativos no presentes en ella quedan Desconocido. Xbox 360 queda Desconocido: no se hacen búsquedas de issues por cada juego ni se supone compatibilidad.

Referencias de configuración: mGBA 0.10.5 [portable](https://github.com/mgba-emu/mgba/blob/0.10.5/src/core/config.c), [opciones Qt](https://github.com/mgba-emu/mgba/blob/0.10.5/src/platform/qt/Window.cpp), [perfiles](https://github.com/mgba-emu/mgba/blob/0.10.5/src/platform/qt/InputProfile.cpp) y [CLI](https://github.com/mgba-emu/mgba/blob/0.10.5/src/feature/commandline.c); xemu [CLI](https://xemu.app/docs/cli/) y [esquema v0.8.138](https://github.com/xemu-project/xemu/blob/v0.8.138/config_spec.yml); Xenia [fullscreen en el commit fijado](https://github.com/xenia-canary/xenia-canary/blob/02d2cb5cc4bfbf047a3ee136923c263ae11f8356/src/xenia/app/emulator_window.cc), [entrada/portable](https://github.com/xenia-canary/xenia-canary/blob/canary_experimental/src/xenia/app/xenia_main.cc), [resolución](https://github.com/xenia-canary/xenia-canary/blob/canary_experimental/src/xenia/gpu/texture_cache.cc) y [TOML](https://github.com/xenia-canary/xenia-canary/blob/canary_experimental/src/xenia/config.cc). En Xenia se consultaron fullscreen y gpu_flags.cc del commit exacto; los otros archivos se contrastaron con la rama oficial disponible y la configuración del binario instalado (véase Gráficos automáticos). Las pruebas de Nexo simulan descargas, metadatos y configuraciones, sin ejecutar los emuladores ni medir rendimiento real.

## Juegos comprimidos

### Instalar PKG de PS4

Coloca el PKG base en una carpeta de `Juegos\PS4`, con un único parche completo en esa misma carpeta y los DLC en ella o en una subcarpeta `DLC`. El base aparece como pendiente. Su acción de instalar utiliza la API existente `ExtractArchiveAsync(ArchiveEntry, IProgress<long>, CancellationToken)`; no cambia el contrato de la interfaz. Los parches y DLC válidos no aparecen como juegos independientes. Los PKG truncados o con metadatos inválidos aparecen con su aviso. `Ready` solo confirma la cabecera y el PARAM.SFO: la comprobación del descifrado/PFS se hace al instalar.

Nexo incluye un lector propio de PKG/PFS; esta operación no utiliza `tar` ni descarga herramientas. Admite PFS de 32 bits, firmado o sin firmas, AES-XTS, imágenes anidadas y compresión PFSC/zlib. Utiliza únicamente los parámetros numéricos públicos de los paquetes fake/de desarrollo compatibles. Un paquete que no se pueda descifrar, una variante no admitida o un parche delta sin un EBOOT completo provoca un error y conserva los originales. No descifra paquetes comerciales con claves privadas ni certifica la autenticidad de un paquete. Se verifica el checksum zlib de los bloques comprimidos; no se presenta esta verificación como validación de todas las firmas PFS/PKG.

Algunos PKG fake recompuestos conservan indicadores y digests de claves que ya no describen su imagen. Nexo intenta primero la derivación que declara el bit 61 (`new crypt`). Si falla la estructura del PFS, prueba una sola vez la derivación antigua con la misma EKPFS obtenida mediante RSA fake válido, sin adivinar passcodes. El digest de la clave derivada 1 puede estar desactualizado incluso sin ser cero: su discrepancia no basta para rechazar un paquete, pero se siguen exigiendo padding RSA válido, límites/inodos/directorios PFS, imagen interior, EBOOT y PARAM.SFO. Una cancelación no inicia el intento alternativo; un fallo conserva el PKG. Las fórmulas y el significado del digest se contrastaron con [Crypto.cs](https://github.com/maxton/LibOrbisPkg/blob/master/LibOrbisPkg/Util/Crypto.cs) y [Pkg.CheckEkpfs](https://github.com/maxton/LibOrbisPkg/blob/master/LibOrbisPkg/PKG/Pkg.cs); el [lector histórico de shadPS4](https://github.com/shadps4-emu/shadPS4/blob/v.0.5.0/src/core/file_format/pkg.cpp) empleaba directamente la derivación antigua. Esto no certifica todos los datos del juego: las comprobaciones de bloques continúan durante la extracción antes de publicar o reciclar.

El lote incluye exclusivamente el Title ID del base. Los PKG válidos de otros títulos permanecen intactos; una incoherencia entre la cabecera y el PARAM.SFO, o un paquete ilegible en la carpeta del lote, detiene la operación. Dos bases del mismo título, varios parches o DLC repetidos requieren dejar una sola versión antes de instalar. Se examinan como máximo 512 paquetes y 50 000 archivos por imagen PFS.

La instalación crea una carpeta contenedora nueva junto al PKG base:

```text
Juegos\PS4\Mi carpeta\CUSA09072 - instalado\
  CUSA09072\eboot.bin
  CUSA09072\sce_sys\param.sfo
  CUSA09072-UPDATE\...
  addons\CUSA09072\<Content ID del DLC>\sce_sys\param.sfo
```

Si el destino ya existe, añade ` (2)`, etc.; no actualiza ni sobrescribe una instalación anterior. El parche queda como hermano `-UPDATE`, que shadPS4 superpone al juego base. Antes de lanzar un juego con estos DLC, Nexo ejecuta sin ventana `shadPS4.exe --set-addon-folder "...\addons"`, espera su resultado y después lanza el juego con `-g`. Esa opción guarda la carpeta global de addons y termina el proceso, por lo que no se combina con `-g`. La ruta se calcula cada vez desde el juego, para que siga funcionando al mover Nexo. Fuente: [CLI oficial 0.20.0](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/main.cpp), [superposición de actualizaciones](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/core/file_sys/fs.cpp) y [lectura de addons](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/core/libraries/app_content/app_content.cpp).

Se calcula el espacio **descomprimido de todo el lote**, más margen. Base, parche y DLC se escriben en un temporal del mismo disco, con progreso en bytes y cancelación. Se rechazan rutas peligrosas, enlaces, ciclos, nombres repetidos y lecturas fuera de los límites. Tras comprobar tamaños, archivos, Title ID, Content ID, categoría, título del PARAM.SFO y presencia de EBOOT en base/parche, se publica el contenedor mediante un único movimiento de carpeta. Solo entonces se envían los PKG instalados a la Papelera. Si falla el reciclaje, se conserva la instalación verificada y se informa del paquete pendiente; no hay borrado permanente alternativo. Cancelar antes de publicar elimina el temporal y conserva todos los PKG.

Referencias de interoperabilidad: [lector histórico de shadPS4 0.5.0](https://github.com/shadps4-emu/shadPS4/blob/v.0.5.0/src/core/file_format/pkg.cpp) (GPL-2.0-or-later), [LibOrbisPkg](https://github.com/maxton/LibOrbisPkg) (LGPL-3.0), incluidos sus [parámetros públicos de claves fake/de desarrollo](https://github.com/maxton/LibOrbisPkg/blob/master/LibOrbisPkg/Util/Keys.cs). No se incorporan sus implementaciones ni bibliotecas: el código de lectura, límites, extracción y transacción se escribió para Nexo a partir de las estructuras del formato y las operaciones criptográficas estándar de .NET.

Comprobación de solo lectura del lote local de FighterZ: 36 PKG, 143 archivos y 26 314 615 557 bytes de contenido instalado (aproximadamente 24,5 GiB). Se pudieron listar y leer el principio y el final de cada archivo; no se extrajo ni se modificó el lote real, ni se probó el juego. Las pruebas de instalación completa utilizan paquetes sintéticos en el proyecto.

### Contenedores de Switch incompletos o inválidos

Los `.nsp` y `.xci` que pasan la comprobación estructural siguen apareciendo como juegos. Si están incompletos o su cabecera no es válida, aparecen como avisos en `GameScanResult.Archives`, usando el contrato existente: `ArchiveState.Incomplete` o `Unreadable`, tamaño real en `Size`, tamaño declarado en `ExpectedSize` cuando se conoce y una explicación en español en `Message`. Nunca se marcan `Ready`: la acción es abrir su carpeta, no extraerlos. `ExtractArchiveAsync` rechaza NSP, XCI, NSZ y NCZ incluso si se le proporciona un estado `Ready` inventado. Al lanzar un NSP/XCI se repite la comprobación para detectar cambios posteriores al escaneo.

Para **NSP**, se comprueban la firma `PFS0`, sus entradas de 24 bytes y la tabla de nombres. El final esperado es `16 + 24 * entradas + tamañoNombres + max(offset + tamaño)`, calculado con enteros de 64 bits y control de desbordamiento. Se rechazan tablas vacías o excesivas (más de 50 000 entradas o 4 MiB de nombres), nombres inválidos/duplicados y rangos solapados. Los nombres están limitados a 1024 bytes y deben terminar en cero. Las entradas pueden estar desordenadas y tener huecos. Si solo está truncada la tabla, `ExpectedSize` indica el mínimo necesario para leerla; el mensaje dice «al menos», porque aún no se conoce el tamaño de todo el contenido.

Para **XCI**, se lee la cabecera de 512 bytes: firma `HEAD` en `0x100`, último bloque válido en `0x118` y coherencia de los límites de la cabecera HFS0 raíz. El final esperado es `(últimoBloque + 1) * 512`. No se exige la capacidad nominal del cartucho, que puede incluir relleno; se admiten imágenes recortadas al final de sus datos válidos. No se recorre ni verifica el contenido HFS0. Referencias de formato: [estructura XCI de hactool](https://github.com/SciresM/hactool/blob/master/xci.h) y [conversión del tamaño a bytes](https://github.com/SciresM/hactool/blob/master/xci.c).

**NSZ/NCZ no son compatibles con Eden 0.2.1.** Se muestran como `Unreadable` con «NSZ comprimido: no compatible con Eden 0.2.1», también cuando un archivo llamado `.nsp` contiene entradas `.ncz`. Una tabla PFS0 truncada tiene prioridad y se muestra como descarga incompleta. Nexo no convierte ni descomprime estos formatos; requiere NSP/XCI sin esa compresión. La comprobación se basa en el [cargador de Eden 0.2.1](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/loader/loader.cpp), sin cargador NSZ/NCZ, y su [lector NSP](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/file_sys/submission_package.cpp), que busca contenidos `.nca` y no transforma NCZ. Esta restricción de Nexo se aplica a toda la estantería Switch; no presume soporte diferente en otros emuladores elegidos.

Estas son comprobaciones rápidas de estructura y tamaño. No se leen claves, no se descifran ni verifican hashes de los NCA y no se garantiza que sea un juego base arrancable. Una cabecera NCA incorrecta también puede tener otras causas; superar esta comprobación no certifica que el contenido esté íntegro ni que las claves sean compatibles. Los `.nca` y `.nro` independientes mantienen su tratamiento anterior.

### Extracción de ZIP, 7z, RAR e ISO de PS3

También puedes colocar una **ISO descifrada de PS3** en `Juegos\PS3`: aparece con el mismo flujo pendiente de los comprimidos. Al pulsar **Descomprimir**, se extrae en una carpeta con su nombre, se comprueba el resultado y después se solicita enviar la ISO a la Papelera. El siguiente escaneo muestra el juego con el título de `PARAM.SFO` y `ICON0.PNG` cuando existan. Las ISO de PS2/PSP/Dolphin conservan su tratamiento de juego directo.

La comprobación inicial de PS3 lee la firma `PlayStation3` en 0x800, el PVD ISO9660 (`CD001` en 0x8001, bloques de 2048 bytes), su tamaño de volumen y los directorios hasta `PS3_GAME/USRDIR/EBOOT.BIN`. Lee solo cuatro bytes del EBOOT: deben ser `SCE\0`. No recorre los 6 GB de una imagen al mostrar la biblioteca. Si el volumen declarado supera el tamaño real, indica **Incomplete** con el tamaño esperado; si el EBOOT no tiene esa cabecera, indica **Unreadable** y «ISO cifrada: RPCS3 necesita una ISO descifrada (o la clave del disco)». Nexo no aplica claves ni descifra imágenes. Una cabecera válida es una comprobación parcial, no garantiza que todos los sectores estén íntegros o descifrados; un EBOOT dañado también puede fallar esa comprobación.

Se comprueban las dos representaciones endian de los campos, los límites de los extents y las entradas duplicadas del recorrido. Se leen como máximo tres directorios de 4 MiB y 50 000 entradas; formatos no reconocidos, archivos EBOOT multi-extent o intercalados quedan **Unreadable**. Durante la extracción se valida además el listado completo de `tar`, con las mismas restricciones de rutas y enlaces que ZIP/7z, y se vuelve a comprobar el EBOOT extraído antes de publicar o reciclar. Un error conserva la ISO.

Los archivos `.7z`, `.zip` y `.rar` dentro de `Juegos\<Id>` se detectan como **pendientes de descomprimir**, separados de los juegos que ya se pueden abrir. La comprobación inicial solo lee la cabecera y, para ZIP, el final del archivo: no extrae datos. Un 7z truncado muestra el tamaño descargado y el esperado; un ZIP sin su cierre muestra «Descarga incompleta». `Ready` significa que pasó esta comprobación rápida, no que se haya comprobado todo su contenido.

La extracción utiliza `tar.exe` incluido en Windows (probado con bsdtar 3.8.9 en Windows 11). Lista y valida las entradas, comprueba el espacio libre para el tamaño descomprimido más un margen, y escribe dentro de un temporal `.nexo-extract-<guid>`. Rechaza rutas absolutas, `..`, enlaces, nombres incompatibles o duplicados en Windows y archivos cuyo contenido no puede validar. Los archivos con contraseña o divididos en varias partes pueden requerir preparación por separado. No descarga herramientas.

Después de comprobar todos los archivos y sus tamaños, mueve el resultado a `Juegos\<Id>\<nombre del comprimido>`. Si ese nombre existe, usa ` (2)`, ` (3)`, etc., sin sobrescribirlo. Solo entonces solicita enviar el comprimido a la **Papelera**, con los diálogos de Windows. Si Windows ofrece borrado permanente porque no puede usar la Papelera, cancela. Ante un error de reciclaje se conservan tanto la carpeta extraída como el comprimido y se explica qué ocurrió.

Puedes cancelar antes de que se publique la carpeta final: se elimina el temporal y se conserva el comprimido. El progreso indica bytes escritos, no un porcentaje sobre el tamaño comprimido. Si la extracción falla, el original se conserva y el temporal se limpia; si Windows impide la limpieza, el mensaje indica su ruta. Los temporales no aparecen en la biblioteca.

Para la interfaz: `GameScanResult.Archives` contiene `ArchiveEntry` con `SystemId`, `Path`, `Title`, `Size`, `ExpectedSize`, `State` y `Message`. `NexoService.ExtractArchiveAsync(archive, progress, ct)` devuelve la carpeta final; los errores se comunican mediante `NexoException` y la cancelación mediante `OperationCanceledException`. Al terminar hay que reescanear. El reciclaje comienza después de publicar y ya no se interrumpe por cancelación. Las pruebas usan tar real para 7z/ZIP y un sustituto controlado de Papelera para comprobar el orden de publicación, reciclaje y manejo de errores sin tocar la Papelera personal.

## Portadas, fondos y títulos

Para un archivo `Mi juego.iso`, puedes añadir `Mi juego.png`, `.jpg` o `.jpeg` al lado; también se busca `covers\Mi juego.png` dentro de la misma carpeta. El fondo se llama `Mi juego.bg.png` o `.bg.jpg`. El nombre conserva las etiquetas de región del archivo, aunque la interfaz las quite del título mostrado.

En juegos-carpeta puedes usar una imagen con el nombre de la carpeta o `cover.png` dentro de ella. Si no hay portada propia, PS3 usa `PS3_GAME\ICON0.PNG` —o `ICON0.PNG` junto a `USRDIR`— y PS4/PS5 usan `sce_sys\icon0.png`. Se admiten imágenes de hasta 32 MB; la interfaz debe poder decodificar su contenido.

Los fondos locales se toman de `PS3_GAME\PIC1.PNG` (alternativa `PIC0.PNG`) en PS3, y de `sce_sys\pic1.png`/`pic0.png` en PS4/PS5. En PSP se leen `PSP_GAME/ICON0.PNG` y `PIC1.PNG`/`PIC0.PNG` directamente del ISO9660, sin extraer el disco entero. CSO, PBP y CHD no tienen lector de arte incrustado en esta versión: pueden usar imágenes externas o búsqueda por nombre.

**Solo se devuelven fondos HD:** ancho mínimo 1280 y alto mínimo 720. Se leen las dimensiones de la cabecera PNG/JPEG sin decodificar la imagen durante el escaneo. Los PIC1 de PSP de 480×272 y los snaps antiguos de 512×384 se conservan en disco, pero nunca aparecen como `BackgroundPath`. Ya no se descargan `Named_Snaps` de libretro. Si no hay fondo HD, se devuelve null; la interfaz puede componer la portada nítida sobre un degradado.

La prioridad es: **sidecar HD junto al juego → PIC1/PIC0 HD propio → fondo HD descargado → fondo generado HD**. Para un generado usa `Juegos\<Id>\covers\…\<stem>.ai.png` (también admite JPEG), reflejando las subcarpetas del juego. Los sidecars del usuario son `<stem>.bg.png|jpg` junto al juego o en su subcarpeta `covers`; la carpeta central `Juegos\<Id>\covers` se considera caché y queda después del PIC1 propio. PSP guarda su icono como `.icon.png` y su fondo HD extraído como `.embedded.bg.png`. El icono de PSP o `ICON0` de PS3 queda como respaldo: no impide buscar una caja real en línea. Si hay un fondo pequeño ocupando el destino de una descarga, el nuevo HD se guarda como `.bg.hd.png|jpg` sin borrar el anterior.

Cuando la interfaz pide el arte de un juego visible, Nexo busca primero las imágenes locales y después las que falten en Internet. No descarga durante el escaneo de juegos. Guarda las imágenes en `Juegos\<Id>\covers\<nombre>.png|jpg` y `<nombre>.bg.png|jpg`. Para juegos dentro de subcarpetas refleja esas subcarpetas bajo `covers`, evitando mezclar ediciones de igual nombre. Puedes sustituir estos archivos manualmente; Nexo no sobrescribe imágenes existentes. Borrar una imagen de caché permite pedirla de nuevo.

Fuentes utilizadas, sin cuentas ni claves de API:

| Sistema | Identificación y fuente |
| --- | --- |
| PS2 | Serial de `SYSTEM.CNF` del ISO; portada plana de [xlenore/ps2-covers](https://github.com/xlenore/ps2-covers). Alternativa: libretro por nombre exacto. |
| PS3 | `TITLE_ID` del SFO; [GameTDB](https://www.gametdb.com/PS3/Downloads), por región, incluso si ya hay ICON0. Prueba `coverHQ`, `coverM` y `cover`, en ese orden. Alternativa: libretro por nombre de carpeta. |
| GameCube/Wii | ID6 y magic del disco; GameTDB por región, con intentos `coverHQ`, `coverM`, `cover`. Lee ISO/GCM, RVZ, WBFS (primera ranura) y CISO. Otros formatos pueden dejar `Platform` en null. Alternativa: libretro por nombre. |
| Retro | Nombre No-Intro completo y sistema según extensión; [libretro-thumbnails](https://github.com/libretro-thumbnails/libretro-thumbnails), solo `Named_Boxarts`. |
| PSP | ISO local primero; alternativa por nombre Redump exacto en el repositorio PSP de libretro. No se ha verificado un servicio estable de portadas por UMD ID. |
| PS4 | Icono/fondo local; alternativa por nombre de carpeta en libretro. |
| Switch | Imágenes locales o añadidas por el usuario; no se consulta ninguna fuente en línea. |
| PS5 | Imágenes locales o añadidas por el usuario. |

Los repositorios por nombre están en el [catálogo de libretro](https://github.com/libretro-thumbnails/libretro-thumbnails/blob/master/.gitmodules). No se hacen coincidencias aproximadas: renombrar un archivo puede impedir encontrar su portada. No todas las fuentes tienen cada juego o imagen HD. Las portadas ya descargadas se conservan; para renovar una pequeña con la búsqueda HQ, retira su archivo de caché (no el ICON0 del juego) y vuelve a pedir el arte.

En Wii/GC no se ha confirmado que existan las variantes planas `coverHQ`/`coverM`: sus fallos se cachean y se pasa a `cover`. El formato HQ documentado en [USB Loader GX](https://github.com/wiidev/usbloadergx/blob/enhanced/source/settings/CSettings.cpp) es `coverfullHQ`, una carátula completa con contraportada y lomo; Nexo no la aplica como portada frontal ni inventa un recorte. PS3 sí tiene `coverHQ` verificado en una instalación real.

Switch no tiene fuente de carátulas en línea en Nexo: usa las imágenes que el usuario coloque junto al juego o en `covers` (`<stem>.png|jpg`, `<stem>.bg.png|jpg`) y el fondo generado `.ai.png` si existe. [Nlib documenta banners y capturas por Title ID](https://api.nlib.cc/nx), pero no se ha verificado su estabilidad ni sus condiciones de uso, y no se ha añadido al descargador.

Para el arte de Switch, Nexo no descifra NCA de control ni lee `prod.keys`, claves de título o licencias: el icono incrustado está cifrado y no se extrae.

Para desactivar toda consulta de arte por Internet, añade **`"downloadArtwork": false`** en la raíz de `nexo.json`, conservando `version` y `systems`. Por defecto es true. El arte local, el ISO de PSP y la caché ya descargada siguen disponibles sin red. Solo se envía a los proveedores el identificador o el nombre del juego: no se suben juegos, rutas locales, firmware ni claves.

Las descargas usan HTTPS con dominios concretos, sin cookies ni credenciales; solo admiten hasta tres redirecciones al mismo host. Límite de 8 MiB por imagen, 8192 píxeles por lado y 32 megapíxeles, comprobación PNG/JPEG y decodificación antes de publicar mediante archivo temporal. Timeout de 12 segundos por solicitud y hasta tres transferencias simultáneas. Un 404 se recuerda siete días; los fallos transitorios, treinta minutos, en `covers\…\.nexo-art`. Eliminar esos archivos `.miss` fuerza reintentar. Cancelar no guarda un fallo. No se siguen junctions para guardar imágenes.

Estas imágenes pertenecen a sus respectivos autores y editores; las fuentes se consultan para la biblioteca personal y no se incorporan al instalador. Los fondos atmosféricos generados por IA se añaden como `.ai.png`: Nexo no contiene un generador ni requiere suscripción a una IA. Los fondos de ejemplo preparados durante el desarrollo se guardan en `arte-generado/`, que no forma parte del repositorio ni del instalador.

Contrato para la interfaz (constructores anteriores y `Compatibility` conservados):

```csharp
// GameEntry:
public string BackgroundPath { get; } // null si no hay fondo
public string Platform { get; }       // "GameCube", "Wii" o null
// NexoService:
public Task<GameEntry> EnsureArtAsync(GameEntry game, CancellationToken ct);
```

El escaneo ya devuelve fondos locales existentes y la plataforma. `EnsureArtAsync` devuelve **una nueva entrada**: hay que sustituir la correspondiente por `SystemId` y `Path`, y descartar resultados de una selección anterior si el usuario ha cambiado de juego. Se puede cancelar al salir de la vista. La operación no bloquea el hilo de WPF; pide solo las piezas visibles. Fallos de red o de acceso a la caché conservan el arte disponible; una ruta de juego externa/inexistente se rechaza y la cancelación se propaga. Las pruebas de red usan respuestas simuladas, no los servicios reales.

Los títulos de PS3/PS4 se leen de `PARAM.SFO` cuando existe. PS4/PS5 también admiten `param.json`, incluido `localizedParameters` y su idioma predeterminado. Si los metadatos están dañados, se usa el nombre de la carpeta. Para archivos se quitan la extensión, etiquetas comunes de región y marcas entre corchetes.

## Créditos de las fotos

Fotografías de consolas procedentes de **Wikimedia Commons**. Los enlaces llevan a la ficha de cada archivo, con su autoría, historial y licencia:

| Sistema | Archivo original | Autoría y licencia |
| --- | --- | --- |
| Retro | [SNES-Model-2-Set.png](https://commons.wikimedia.org/wiki/File:SNES-Model-2-Set.png) | Evan-Amos; dominio público |
| PS2 | [PS2-Fat-Console-Set.png](https://commons.wikimedia.org/wiki/File:PS2-Fat-Console-Set.png) | Evan-Amos; dominio público |
| PS3 | [Sony-PlayStation-3-4001B-wController-noBG.png](https://commons.wikimedia.org/wiki/File:Sony-PlayStation-3-4001B-wController-noBG.png) | Evan-Amos; dominio público |
| PSP | [Sony-PSP-1000-Body.png](https://commons.wikimedia.org/wiki/File:Sony-PSP-1000-Body.png) | Evan-Amos; dominio público |
| GameCube/Wii | [GameCube-Console-Set.png](https://commons.wikimedia.org/wiki/File:GameCube-Console-Set.png) | Evan-Amos; dominio público |
| PS4 | [PS4-Console-wDS4.png](https://commons.wikimedia.org/wiki/File:PS4-Console-wDS4.png) | Evan-Amos; dominio público |
| Switch | [Nintendo-Switch-wJoyCons-BlRd-Standing-FL.png](https://commons.wikimedia.org/wiki/File:Nintendo-Switch-wJoyCons-BlRd-Standing-FL.png) | Evan-Amos, modificado por Gunnar.offel; dominio público |
| PS5 | [PlayStation 5 and DualSense with transparent background.png](https://commons.wikimedia.org/wiki/File:PlayStation_5_and_DualSense_with_transparent_background.png) | Fotografía de Osh33m; eliminación del fondo por Soberian; [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/) |
| GBC | [Nintendo-Game-Boy-Color-FL.png](https://commons.wikimedia.org/wiki/File:Nintendo-Game-Boy-Color-FL.png) | Evan-Amos; dominio público |
| GBA | [Nintendo-Game-Boy-Advance-Purple-FL.png](https://commons.wikimedia.org/wiki/File:Nintendo-Game-Boy-Advance-Purple-FL.png) | Evan-Amos; dominio público |
| Xbox | [Xbox-Console-Set.png](https://commons.wikimedia.org/wiki/File:Xbox-Console-Set.png) | Evan-Amos; dominio público |
| Xbox360 | [Xbox-360-Pro-wController.png](https://commons.wikimedia.org/wiki/File:Xbox-360-Pro-wController.png) | Evan-Amos; fondo eliminado; dominio público |

Las fotos se presentan escaladas en el carrusel de Nexo. La copia de PS5 y las adaptaciones de esa imagen conservan **CC BY-SA 4.0**, con estos créditos y el enlace a la licencia. La atribución no implica que sus autores respalden Nexo.

## Mando y navegación

Nexo admite mandos compatibles con XInput. La cruceta y el stick izquierdo devuelven direcciones; A/B/X/Y, LB/RB, Start y Back devuelven pulsaciones individuales. Mantener una dirección repite tras 380 ms y luego cada 140 ms. Se aplica una zona muerta al stick y se restablece el estado al desconectar el mando. Se usa `xinput1_4.dll` con alternativa `xinput9_1_0.dll`, sin descargar controladores.

La interfaz permite elegir consola con arrastre, rueda, teclado y mando; bajar a la estantería de juegos y volver a las consolas. Los controles concretos se muestran en pantalla.

## Ajustes y argumentos de lanzamiento

`nexo.json` es opcional. Nexo lo crea cuando eliges manualmente un ejecutable. No lo sobrescribe si está mal formado. Para editarlo, cierra Nexo primero. Un ejemplo válido:

```json
{
  "version": 1,
  "systems": {
    "PS2": {
      "executable": "C:\\Nexo\\Emuladores\\PS2\\pcsx2-qt.exe",
      "gameArguments": ["-fullscreen", "--", "{game}"]
    },
    "PS4": {
      "gameArguments": ["-g", "{game}"]
    }
  }
}
```

`gameArguments` es una lista de argumentos, **no una cadena de comandos**: no añadas comillas alrededor de `{game}`. Nexo cita cada argumento correctamente para Windows. `{game}` es el archivo del juego o su `eboot.bin`; `{gameFolder}` es la carpeta que contiene ese archivo; `{core}` es la ruta del núcleo de RetroArch seleccionado. El lanzamiento del emulador sin juego no utiliza esta lista. No se invoca una shell.

Plantillas predeterminadas:

| Emulador | Argumentos para un juego |
| --- | --- |
| RetroArch | `-f -L {core} {game}` |
| PCSX2 | `-fullscreen -- {game}` |
| RPCS3 | `--no-gui --fullscreen {game}` |
| PPSSPP | `--fullscreen {game}` |
| Dolphin | `-b -C Dolphin.Display.Fullscreen=True -e {game}` |
| shadPS4 SDL | `--fullscreen true -g {game}` |
| Eden 0.2.1 | `-f -g {game}` |
| Ryujinx | `{game}` |
| Citron / yuzu | `-g {game}` |
| KytyPS5 | `--fullscreen --game {game}` |

Comprobación del 24/10/2026 contra los seis ejecutables de `C:\Nexo`: PCSX2 2.8.2, RPCS3 0.0.43-20393, PPSSPP 1.20.4, Dolphin 2609, shadPS4 SDL 0.20.0 y RetroArch 1.22.2. Se inspeccionaron las cadenas incrustadas de los binarios y, cuando hizo falta, el código oficial de la versión. No se ejecutaron juegos ni se validó su rendimiento.

- PCSX2 confirma `-fullscreen`, `--` y el archivo posicional en su ayuda incrustada. `-batch` y `-nogui` son opciones adicionales; no se fuerzan.
- Dolphin admite `-e` para cargar el archivo. Se añade `-b` al lanzar juegos para ocultar su biblioteca y terminar al detener la emulación; no hace falta para cargar el juego. Para conservar la ventana principal, configura `gameArguments` como `["-e", "{game}"]`. «Abrir emulador» sigue sin argumentos para permitir configurarlo. Fuentes: [opciones de Dolphin 2609](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/UICommon/CommandLineParse.cpp) y [cierre de la sesión](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/DolphinQt/MainWindow.cpp).
- RPCS3 acepta la ruta posicional. Al lanzar un juego, Nexo usa `--no-gui --fullscreen`: el binario instalado indica que la segunda opción exige la primera. «Abrir emulador» conserva su GUI sin esos argumentos. Para jugar con su biblioteca visible y respetar su ajuste de ventana, puedes configurar `gameArguments` como `["{game}"]`.
- PPSSPP usa el archivo posicional, confirmado por las cadenas de arranque del binario y el [parser de PPSSPP 1.20.4](https://github.com/hrydgard/ppsspp/blob/v1.20.4/UI/NativeApp.cpp).
- shadPS4 SDL 0.20.0 admite `-g`, `--game` o ruta posicional. Puede recibir una carpeta (añade `eboot.bin`) o el ejecutable; Nexo conserva `-g {game}` con la ruta completa al `eboot.bin`. «Abrir emulador» usa `-b` para Big Picture: sin argumentos esta build solo muestra un aviso. Fuentes: [CLI 0.20.0](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/main.cpp) y [resolución de la carpeta](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/emulator.cpp). El intento de `--help` en el entorno restringido de comprobación terminó con `0xC0000409`, sin salida; su arranque real sigue pendiente de comprobación fuera de ese entorno.
- RetroArch confirma `-L`/`--libretro` y el archivo posicional en su ayuda incrustada. Se encontraron los **16 nombres de núcleo** usados por el mapa de Nexo, entre los 197 DLL instalados.

La detección encontró los seis ejecutables en sus subcarpetas reales, incluidos Dolphin y RetroArch. Puede repetirse en modo de solo lectura, tras compilar Core en Release, con `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\Inspect-InstalledEmulators.ps1`: muestra rutas, SHA256, cadenas relevantes y comprueba los núcleos sin arrancar emuladores ni crear carpetas en la instalación. `-SkipStrings` omite las cadenas.

Las opciones nuevas de Eden se contrastaron con la fuente 0.2.1 y las de KytyPS5 con su proyecto local; no se ejecutaron juegos para comprobar el resultado visual. Si tu compilación usa otros argumentos, sobreescribe `gameArguments` según su ayuda local. Para ambos sistemas el campo web continúa vacío.

### Pantalla completa al jugar

Las plantillas predeterminadas anteriores se aplican solo a `LaunchGame`. `LaunchEmulator` sigue sin argumentos, excepto shadPS4 SDL, que conserva `-b` para abrir Big Picture. Nexo no escribe preferencias de pantalla completa en INI/TOML/YAML ni necesita registros de procedencia para esta función: utiliza opciones de ejecución. Un `gameArguments` personalizado sustituye por completo la plantilla; Nexo no le añade flags. Cada emulador puede conservar preferencias que el usuario cambie durante su sesión.

- **Eden 0.2.1:** el [parser de MainWindow](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/yuzu/main_window.cpp) reconoce `-f` y `-g`. No se presupone el alias largo `--fullscreen`, ni se aplica este cambio a Ryujinx/Citron/yuzu sin una versión contrastada.
- **PCSX2 2.8.2:** [QtHost](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2-qt/QtHost.cpp) reconoce `-fullscreen`; se conserva `--` antes del juego.
- **Dolphin 2609:** `-C` y `--config` reciben `<System>.<Section>.<Key>=<Value>`. Su [capa de CLI](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/UICommon/CommandLineParse.cpp) no guarda estos valores. [El nombre de System::Main es Dolphin](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Common/Config/Config.cpp), y [la clave es Display/Fullscreen](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Core/Config/MainSettings.cpp). Se envían `-C` y `Dolphin.Display.Fullscreen=True` como argumentos separados.
- **RPCS3 0.0.43-20393:** sus cadenas locales confirman `--fullscreen` y que requiere `--no-gui`; coincide con el [parser oficial disponible](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/rpcs3.cpp). No se pudo recuperar la revisión exacta `06ae1af5` desde el servicio web; no se presenta como verificación completa de esa fuente. No hace falta modificar `GuiConfigs\CurrentSettings.ini`.
- **PPSSPP 1.20.4:** el [arranque Win32](https://github.com/hrydgard/ppsspp/blob/v1.20.4/Windows/main.cpp) procesa `--fullscreen` antes de crear la ventana y excluye ese cambio del guardado de ajustes.
- **shadPS4 SDL 0.20.0:** su [CLI](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/main.cpp) requiere `--fullscreen true` (dos argumentos), no un flag booleano sin valor.
- **RetroArch 1.22.2:** su [parser](https://github.com/libretro/RetroArch/blob/v1.22.2/retroarch.c) acepta `-f`; se mantiene `-L` y el núcleo elegido.
- **KytyPS5:** lectura de su `src\main.cpp`: la ayuda anuncia `--fullscreen` sin bordes y `FLAGS` lo enlaza con `ConfigOptions::fullscreen_enabled`. No se modificó ese proyecto.

Los ajustes se escriben mediante un archivo temporal y sustitución atómica en el mismo directorio. Un JSON inválido se conserva y produce un error en español, tanto al arrancar como al intentar guardar. Las rutas elegidas manualmente son absolutas: si mueves la instalación, puedes volver a elegirlas; la detección de las carpetas propias sigue funcionando.

## Alcance de la búsqueda

La detección de emuladores recorre hasta cuatro niveles de subcarpetas, 10 000 entradas o aproximadamente 350 ms por sistema. El escaneo de juegos recorre hasta diez niveles, 50 000 entradas u ocho segundos. Las bibliotecas muy grandes pueden quedar incompletas; usa carpetas menos profundas. El escaneo puede cancelarse. No se siguen enlaces simbólicos ni junctions, incluidos los de las rutas de instalación. El tiempo de una operación concreta del sistema de archivos puede superar el presupuesto.

## Actualizar o desinstalar

**Actualización automática.** Unos segundos después de abrirse, Nexo consulta `https://api.github.com/repos/oscargaldamez137-sys/nexo/releases/latest` (como máximo 15 s; sin red no avisa de nada). Si la etiqueta `vX.Y.Z` es más nueva que la versión de `Nexo.exe` y la publicación incluye `Instalar-Nexo.exe`:

1. La barra superior muestra «Descargando Nexo X.Y.Z · %». El instalador se guarda en `%TEMP%\Nexo-Update` y se comprueban su tamaño y la huella SHA-256 que publica GitHub (`digest`); si no coinciden, no se instala.
2. Se extrae sin ejecutarlo (`Instalar-Nexo.exe /Q /T:<carpeta> /C`, modo silencioso de IExpress) para obtener `instalar.ps1` y `Nexo.zip`.
3. Cuando Nexo no está descomprimiendo, instalando un emulador ni importando archivos, no está minimizado y no hay un juego lanzado desde Nexo en primer plano, empieza una cuenta atrás de 8 segundos. **B** en el mando o **Esc** la dejan para cuando cierres Nexo; pulsar el aviso instala en el momento.
4. Nexo se cierra y `instalar.ps1 -Destination <esta carpeta> -NonInteractive -NoShortcuts -WaitSeconds 60` espera a que termine de cerrarse, sustituye el programa sin ventanas, conserva `Emuladores`, `Juegos` y `nexo.json` y vuelve a abrir Nexo.

Si la carpeta no permite escribir, si esta copia no se instaló con el instalador (no tiene `nexo-installed-files.txt`) o si el intento anterior no llegó a instalar la versión nueva, Nexo no lo reintenta en bucle: muestra un aviso que abre la página de descargas. El motivo de un fallo queda en `%TEMP%\Nexo-Update\error.txt` y la versión anterior se vuelve a abrir. No hay pantalla de ajustes, así que la comprobación no se puede desactivar.

También puedes actualizar a mano: ejecuta el instalador y elige la misma carpeta. Cierra Nexo antes. Se reemplazan los archivos del programa y se conservan emuladores, juegos, portadas y `nexo.json`. Los LEEME existentes tampoco se sobrescriben.

Usa **Desinstalar Nexo** en el menú Inicio o `Desinstalar Nexo.vbs` en la carpeta instalada. **No** es la opción predeterminada a la pregunta sobre tus datos: desinstala solo los archivos registrados del programa. Elegir **Sí** solicita enviar `Emuladores`, `Juegos` y `nexo.json` a la **Papelera de Windows**, con sus diálogos de confirmación. Si Windows ofrece borrado permanente porque no puede usar la Papelera, cancela ese diálogo para conservarlos. Ante cancelación o error, Nexo interrumpe la desinstalación sin reintentar un borrado permanente; algún elemento anterior puede estar ya en la Papelera. **Cancelar** en la pregunta inicial no hace cambios. La carpeta puede permanecer si contiene datos u otros archivos.

## Integración de la interfaz

`NexoService.PrepareControllers(id)` devuelve `ControllerSetupResult` con `Changed` y `Message`. `LaunchEmulator` y `LaunchGame` también preparan los controles antes de abrir el proceso y dejan el resultado en `LastControllerSetup`. Un fallo de preparación no impide lanzar. Muestra **Message**: `Changed` también puede indicar que se creó una carpeta portable, sin asignar un mando (por ejemplo, Eden).

`NexoService.PrepareGraphics(id)` devuelve `GraphicsSetupResult` con `Changed`, `Message` y `Tier` (`Bajo`, `Medio`, `Alto` o `Ultra`). Ambos lanzamientos preparan primero mandos y después gráficos, dejando este último resultado en `LastGraphicsSetup`. Un fallo gráfico tampoco impide abrir el proceso. `service.GpuInfo` expone `Name`, `VramBytes` (long), `Tier`, `Vendor` (`NVIDIA`, `AMD`, `Intel` o `Desconocido`), `IsIntegrated`, `MonitorWidth` y `MonitorHeight`. La detección se realiza una vez por instancia de servicio; vuelve a abrir Nexo tras cambiar el modo del monitor. `Changed` puede incluir la creación del modo portable: presenta también `Message`.

## Gráficos automáticos

Al abrir un emulador o juego, Nexo prepara el motor, la resolución y la calidad según GPU y pantalla. Solo cambia opciones ausentes, de fábrica o cuyo valor todavía coincida con el último que escribió Nexo. Se respetan los ajustes personales y los perfiles por juego. Cierra el emulador antes de prepararlos: un emulador abierto podría guardar encima. Las escrituras son atómicas; los errores se comunican sin bloquear el lanzamiento.

La VRAM dedicada se lee como 64 bits de `HardwareInformation.qwMemorySize` en el registro (no del DWORD `Win32_VideoController.AdapterRAM`). Se elige la GPU con más VRAM. Bajo: integrada, desconocida o <3 GB; Medio: 3–<6 GB; Alto: ≥6 GB dedicada. Ultra: ≥10 GB y NVIDIA RTX 3070–3090/4070–4090/5070–5090 o AMD RX 6800–6900/7700–7900/9070, incluidas variantes que cumplan la memoria. Una RTX 3060/4060 Ti de mucha memoria no se clasifica como Ultra solo por sus GB. Son presupuestos de Nexo, no benchmarks ni garantías de FPS; influyen CPU, juego, driver y compatibilidad del emulador.

**Motor:** PCSX2, Dolphin y xemu pasan a Vulkan si el controlador de esa GPU declara Vulkan 1.3 o posterior. Nexo consulta `vulkan-1.dll` de System32, enumera los dispositivos físicos y exige coincidencia de nombre con la GPU seleccionada; no abre ventanas ni crea superficies. Una consulta fallida, un dispositivo diferente o un driver antiguo no autoriza a forzarlo: se conserva Auto en PCSX2, D3D11 en Dolphin y OpenGL en xemu. Es una comprobación de API, no de todas las extensiones ni una medición que demuestre que Vulkan será el más rápido en todos los juegos. Los motores elegidos manualmente permanecen intactos. PPSSPP, RPCS3 y Eden ya tienen Vulkan por defecto en estas builds de Windows. Xenia conserva el perfil D3D12.

**Pantalla:** se enumera explícitamente el dispositivo conectado marcado como principal por Windows y se lee su modo actual con `EnumDisplaySettingsW`, en píxeles físicos, no unidades WPF ni resolución máxima del panel. Se refresca en cada consulta de `NexoService.GpuInfo`, incluida la preparación previa a lanzar. Se exponen además `MonitorDeviceName`, `MonitorRefreshRate` (0 si desconocida) y `SupportsVulkan` (confirmación del umbral 1.3 usado por el perfil). La frecuencia se informa; no se traslada a la velocidad emulada ni a un parche de FPS.

En un equipo con dos pantallas activas (una ultrapanorámica y otra de 16:9 marcada como principal, ambas de 1440 píxeles de alto), Windows devuelve el modo de cada una: un ancho menor del esperado no es una conversión DPI de la ultrapanorámica, sino la otra pantalla. Nexo sigue la principal configurada en Windows; no cambia su selección. Con la misma altura útil, ambas producen el mismo presupuesto. No se escriben esas dimensiones fijas en ningún perfil.

La imagen se encaja en la pantalla conservando su proporción. Se redondea hacia arriba al factor admitido más próximo, limitado por la GPU. Solo Ultra añade supersampling, con objetivo de 1,5 veces la altura útil; las barras laterales de 21:9 no cuentan como píxeles de detalle del juego. Este redondeo puede superar algo la altura, incluso sin supersampling. Las alturas nominales son PS2 360 (etiquetas de su UI; varía por juego), Dolphin 528 (EFB antes de recorte), PS3/Switch/Xenia 720, PSP 272 y Xbox 480. Sin pantalla identificada se usa un objetivo de 1080p.

| Escala a 1440p | Bajo | Medio | Alto | Ultra |
| --- | --- | --- | --- | --- |
| PCSX2 | 3× | 4× | 4× | 6× |
| Dolphin | 3× | 3× | 3× | 5× |
| RPCS3 | 150 % | 200 % | 200 % | 300 % |
| PPSSPP | 4× | 6× | 6× | 8× |
| Eden | 1× | 2× | 2× | 3× |
| xemu | 1× | 2× | 3× | 4× |
| Xenia | 1× | 2× | 2× | 3× |

Los techos Bajo/Medio/Alto/Ultra, antes de limitar por pantalla, son PS2 3/4/6/10×, Dolphin 3/4/5/8×, RPCS3 150/200/250/300 %, PSP 4/6/8/10×, Eden 1/2/3/3× (4× en Ultra ≥16 GB), Xbox 1/2/3/4× y Xenia 1/2/3/3×. Son techos de este perfil, no los límites absolutos del emulador. Por ejemplo, Ultra a 1080p elige PS2 5×, a 1440p 6× y a 2160p 9×. Eden a 1440p se queda en 3× incluso con 16 GB; 4× solo cuando la pantalla lo justifica y hay presupuesto. Con 12 GB de VRAM el techo de Eden es 3×: 4× exigiría dieciséis veces los píxeles nativos, frente a nueve a 3×.

Ejemplo de resultado en un equipo de nivel Ultra (GPU NVIDIA de 12 GB, pantalla de 1440p), si los valores son de fábrica o gestionados por Nexo:

| Sistema / emulador | Motor | Resolución | AF / AA | Otras opciones |
| --- | --- | --- | --- | --- |
| Retro / RetroArch 1.22.2 | Driver actual y negociación con el núcleo | Escala entera | Según núcleo | Sin suavizado; shaders personales intactos. |
| PS2 / PCSX2 2.8.2 | Vulkan (`Renderer=14`) | 6× | AF 16× | `hw_mipmap=true`, `mipmap=true`; `filter=2` (PS2), `TriFilter=-1` (Auto); caché; parches panorámicos y sin entrelazado. |
| PS3 / RPCS3 0.0.43-20393 | Vulkan | 300 % | AF 16×; AA del juego | Intérprete de shaders durante recompilación asíncrona y caché de disco, ya predeterminados. |
| PSP / PPSSPP 1.20.4 | Vulkan | 8× | AF 16× | Caché y ubershaders de vértices/fragmentos, ya predeterminados. |
| GameCube/Wii / Dolphin 2609 | Vulkan | 5× | AF 16×; MSAA 4× | Ubershaders híbridos, caché y precarga antes de empezar; sin widescreen hack. |
| PS4 / shadPS4 SDL 0.20.0 | Vulkan propio | La del juego | La del juego | No se alteran buffers, FSR, RCAS, caché experimental ni vblank. |
| Switch / Eden 0.2.1 | Vulkan | 3× | AF 16× | Cachés de shaders y del driver; sin activar el hack de shaders asíncronos. |
| PS5 / KytyPS5 | Vulkan del proyecto | La del proyecto/juego | La del proyecto | Se conserva su configuración experimental. |
| GBC / mGBA 0.10.5 | OpenGL de presentación | Escalado entero | Sin suavizado | Sin shaders añadidos ni cambios de velocidad. |
| GBA / mGBA 0.10.5 | OpenGL de presentación | Escalado entero | Sin suavizado | Comparte instalación y opciones de mGBA con GBC. |
| Xbox / xemu 0.8.138 | Vulkan | 4× | La del juego | Caché de shaders de fábrica (`perf.cache_shaders=true`). |
| Xbox 360 / Xenia Canary 02d2cb5 | D3D12 | 3×/3× | La del juego | Caché de disco y opciones de compilación existentes, sin nuevos hacks. |

No todos los ajustes llamados «máximos» mejoran la imagen sin efectos secundarios. PCSX2 mantiene muestreo PS2 y trilineal Auto: forzar filtrado altera sprites que el juego pidió sin filtrar; el mipmapping completo ya está activado. En Dolphin `ShaderCompilationMode=2` usa ubershaders híbridos; no se elige 3, que salta dibujos. `WaitForShadersBeforeStarting=true` puede aumentar la espera inicial. MSAA es 1/2/4/4 por nivel y se conserva un SSAA personalizado. El escalado de texturas, hacks de velocidad y frame skipping no se activan.

RetroArch negocia el contexto gráfico con cada núcleo (`driver_switch_enable`, predeterminado true); no se sustituye globalmente el driver ni un preset GLSL/Slang personalizado. Un driver de presentación no aumenta por sí solo la resolución interna de todos los núcleos. mGBA 0.10.5 ofrece OpenGL/Qt, no un backend Vulkan/D3D12 equivalente. El escalado entero sin suavizado ya da píxeles nítidos: no se añade un shader que cambie el dibujo original.

**shadPS4:** `internal_screen_width/height` inicializa la resolución comunicada al juego por VideoOut. `sceVideoOutSetBufferAttribute` recibe las dimensiones de los buffers que el propio juego solicita. Cambiar aquel valor a 3440×1440 no implementa un escalado universal, ni garantiza soporte ultrapanorámico; queda intacto. Su caché de pipelines está desactivada de fábrica y no se activa globalmente: hay un [reporte abierto de interferencia con parches de shaders](https://github.com/shadps4-emu/shadPS4/issues/5416), no una reproducción nuestra. Kyty se auditó solo leyendo el proyecto local: se conserva su motor Vulkan y su configuración experimental.

**Xenia:** su fuente del commit fijado advierte que `anisotropic_override=5` (16×) puede causar artefactos, y que la compilación asíncrona puede mostrar defectos transitorios. Por la condición de conservar compatibilidad no se fuerzan esos overrides ni se cambia la elección existente. RPCS3 tampoco tiene un máximo universal «sin errores» por encima de 200 %: existen excepciones por juego; Nexo conserva las bases y los perfiles particulares.

Se escriben `inis\PCSX2.ini`, `User\Config\GFX.ini` y `Dolphin.ini`, `config\config.yml` de RPCS3 (raíz solo para instalaciones antiguas detectadas), `memstick\PSP\SYSTEM\ppsspp.ini`, `user\config\qt-config.ini`, `retroarch.cfg`, `config.ini` de mGBA, `xemu.toml` y `xenia-canary.config.toml`. Cada archivo conserva su registro `<archivo>.nexo-graphics.json`. El registro guarda el último valor por opción; si el usuario lo cambia, Nexo renuncia a gestionarla. Restablecer fábrica o borrar la configuración permite reaplicar. Perder el registro no autoriza a sobrescribir valores personalizados. Primero se confirma el archivo y después el registro; si falla este último, la siguiente ejecución no se atribuye falsamente el nuevo valor.

El perfil nuevo actualiza recibos antiguos válidos: por ejemplo, PS2 8×→6× y Dolphin 6×→5× a 1440p. Un 8× manual o sin recibo se conserva. Un cambio manual idéntico al último valor de Nexo es indistinguible de no haberlo editado. Se mantienen las protecciones contra junctions, configuraciones ambiguas, datos globales ocultos al activar portable y rutas redirigidas. No se crean configuraciones por juego.

API compatible: `PrepareGraphics(id)` y `LastGraphicsSetup` mantienen `Changed`, `Message`, `Tier`. `GpuInfo` añade `MonitorDeviceName`, `MonitorRefreshRate` y `SupportsVulkan`; la UI puede seguir usando las propiedades anteriores. Los resultados no aseguran que el emulador haya arrancado con éxito ni que el juego alcance sus FPS objetivo.

Fuentes y límites de verificación:
- [PCSX2 2.8.2: claves/defaults](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2/Pcsx2Config.cpp) y [enums](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2/Config.h).
- [Dolphin 2609: gráficos](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Core/Config/GraphicsSettings.cpp), [ShaderCompilationMode](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/VideoCommon/VideoConfig.h), [Core.GFXBackend](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Core/Config/MainSettings.cpp) y [backend de fábrica en Windows](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/VideoCommon/VideoBackendBase.cpp).
- [PPSSPP 1.20.4](https://github.com/hrydgard/ppsspp/blob/v1.20.4/Core/Config.cpp): la clave real es **`GraphicsBackend`**, no `GPUBackend`; Windows usa `3 (VULKAN)`. `InternalResolution=1` es fábrica en Windows; también se admite el antiguo 0 automático. `AnisotropyLevel=4` es 16×.
- [Eden 0.2.1: ajustes](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/common/settings.h), [enums](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/common/settings_enums.h) y [grupo Renderer](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/frontend_common/config.cpp). `resolution_setup=3/6/7/8` corresponde a 1×/2×/3×/4×; anisotropía 5 es 16×. Al escribir, se usa `\default=false`.
- [xemu v0.8.138](https://github.com/xemu-project/xemu/blob/v0.8.138/config_spec.yml), [mGBA 0.10.5: motores](https://github.com/mgba-emu/mgba/blob/0.10.5/src/platform/qt/Display.cpp), [RetroArch 1.22.2: defaults](https://github.com/libretro/RetroArch/blob/v1.22.2/config.def.h) y [negociación del contexto](https://github.com/libretro/RetroArch/blob/v1.22.2/runloop.c).
- [shadPS4 0.20.0: ajustes](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/core/emulator_settings.h), [VideoOut](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/core/libraries/videoout/video_out.cpp) y [driver](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/core/libraries/videoout/driver.cpp).
- [Xenia 02d2cb5: AF/async](https://github.com/xenia-canary/xenia-canary/blob/02d2cb5/src/xenia/gpu/gpu_flags.cc). El TOML generado por el binario instalado confirma las claves y advertencias. Los otros archivos gráficos de ese commit no se recuperaron con el servicio web; el backend/escalado existente conserva el contraste previo con la rama oficial y las pruebas reales comunicadas por el usuario.
- RPCS3: la revisión exacta `06ae1af5` sigue sin recuperarse mediante el servicio web. Se contrastó la [fuente oficial disponible](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Emu/system_config.h) con el archivo generado por el build instalado: `Renderer: Vulkan`, `Shader Mode: Async Recompiler with Shader Interpreter`, `Disable On-Disk Shader Cache: false`. No se afirma verificación de fuente exacta donde no la hubo. Las pruebas de Nexo validan archivos/procedencia y fallos; no ejecutan juegos ni miden rendimiento.

## Mandos y configuración portable

Conecta el mando antes de abrir un juego. Nexo prepara el jugador 1 si falta su archivo de controles o reconoce exactamente el teclado de fábrica. Una asignación, sensibilidad o formato desconocido se conserva. No hay una marca de «ya configurado»: si borras el archivo, se puede preparar otra vez. Cierra el emulador antes de cambiar o restaurar sus controles.

| Emulador | Qué prepara Nexo |
| --- | --- |
| PCSX2 2.8.2 | `portable.ini` e `inis\PCSX2.ini`. Usa el puerto XInput que lee Nexo, con botones, dos sticks, gatillos y vibración. Habilita SDL. Sin XInput conectado, usa `SDL-0`, compatible con el primer mando reconocido por SDL, incluidos muchos mandos PlayStation. |
| Dolphin 2609 | `portable.txt`, `User`, `GCPadNew.ini` y `WiimoteNew.ini`. Con XInput conectado configura GameCube y un Wiimote + Nunchuk: puntero en stick derecho, agitar con B. Solo sustituye el perfil completo reconocido de fábrica; conserva cualquier personalización. Algunos juegos Wii requieren otro tipo de control. |
| RPCS3 0.0.43 | `config\input_configs\global\Default.yml`, jugador 1 con XInput. Conserva los demás jugadores, perfiles por juego y selecciones de perfiles personalizadas. Sin XInput conectado espera a la siguiente preparación. |
| PPSSPP 1.20.4 | Crea `memstick` si falta. Conserva el mapeo nativo de Windows (teclado, XInput y pad). Si existe `installed.txt`, mantiene esa memoria y avisa: crear `memstick` no anula esa redirección. |
| RetroArch 1.22.2 | Conserva su autodetección y los perfiles de `autoconfig`; no sustituye ajustes ni remapeos propios. |
| shadPS4 0.20.0 SDL / KytyPS5 | Conserva el mapeo nativo SDL Gamepad. |
| Eden 0.2.1 / familia yuzu | Crea `user` junto al ejecutable. Hay que seleccionar el mando una vez en Controles, jugador 1. Eden necesita el GUID SDL y los botones/ejes reales; no existe un motor `engine:xinput` en la versión comprobada. |

Nexo sigue usando XInput para navegar. Un mando PlayStation que Windows no exponga como XInput puede funcionar directamente en un emulador con SDL, pero aún no sirve para navegar por Nexo. Para Dolphin se puede elegir su dispositivo SDL en la configuración del emulador; su nombre depende del modelo. En RPCS3 se puede seleccionar DualShock 4, DualSense o SDL en **Pads**. No se inventan nombres de dispositivos ni GUID.

Con varios mandos, la preparación inicial de PCSX2/Dolphin/RPCS3 usa el puerto XInput observado por Nexo si continúa conectado, o el primero conectado si todavía no se ha navegado. Las asignaciones ya guardadas se conservan, incluso si cambian los puertos al reconectar o mover Nexo a otro PC. PPSSPP asigna XInput 0 de fábrica; RetroArch y los emuladores SDL tienen su propio orden. Por tanto, no se garantiza selección idéntica con varios mandos o cambios de puerto: selecciona el dispositivo en el emulador en esos casos. La comprobación con el mando físico se hace por separado de las pruebas de archivos.

La preparación solo escribe junto a ejecutables situados en la carpeta `Emuladores` del sistema correspondiente. Un ejecutable elegido fuera sigue pudiendo abrirse, sin reconfigurarlo. Antes de activar portable, si se encuentran datos en las ubicaciones habituales del perfil de Windows, Nexo avisa y conserva su ubicación. Para migrar una instalación usada, traslada primero sus datos completos a la carpeta portable siguiendo las instrucciones del emulador; no basta con copiar los controles, pues también puede haber partidas. Un `portable.txt` de PCSX2 con ruta personalizada también se conserva.

Formatos contrastados con las fuentes oficiales: [PCSX2 2.8.2, mapeo de teclado](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2/Input/InputManager.cpp), [XInput](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2/Input/XInputSource.cpp), [Dolphin 2609](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Core/HW/GCPadEmu.cpp), [PPSSPP 1.20.4](https://github.com/hrydgard/ppsspp/blob/v1.20.4/Core/KeyMap.cpp), [RetroArch 1.22.2](https://github.com/libretro/RetroArch/blob/v1.22.2/retroarch.cfg), [shadPS4 0.20.0](https://github.com/shadps4-emu/shadPS4/blob/v.0.20.0/src/sdl_window.cpp) y [Eden 0.2.1](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/input_common/main.cpp). En RPCS3 se contrastaron los nombres con el binario instalado `20393-06ae1af5` y las fuentes oficiales de [configuración](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Emu/Io/pad_config.h) y [XInput](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/Input/xinput_pad_handler.cpp); no se pudo recuperar la revisión exacta `06ae1af5` desde el servicio web, así que no se presenta como una verificación completa de esa revisión. En KytyPS5 se leyó el código local de `src/graphics/presentation/window/window.cpp`: abre mandos con `SDL_OpenGamepad` y entrega botones, ejes y gatillos al controlador.

Para automatizar Eden sin selección inicial haría falta una enumeración compatible con su SDL2 (GUID normalizado, índice entre mandos iguales y mapeo físico de cada botón/eje). Net48 puede llamar a una DLL nativa por P/Invoke, pero Nexo no incluye esa DLL y esta compilación de Eden la enlaza sin ofrecer una DLL junto al ejecutable. Esta función no carga bibliotecas arbitrarias ni fabrica un GUID a partir del número de puerto XInput.

**Xbox 360 en Eden 0.2.1:** el GUID de un dispositivo del backend XInput sí se construye de manera determinista. El [backend XInput de SDL2](https://github.com/libsdl-org/SDL/blob/SDL2/src/joystick/windows/SDL_xinputjoystick.c) usa bus, VID/PID, versión y subtipo, con la firma `x`; puede obtener datos mediante `XInputGetCapabilitiesEx`. También cede el dispositivo a HIDAPI o RawInput si esos backends ya lo gestionan. Por eso el nombre «Xbox 360 Controller» o el par 045e/028e no identifican por sí solos el GUID que Eden verá. Esta referencia describe SDL2; no prueba qué revisión exacta de SDL2 enlaza el binario instalado.

Eden 0.2.1 usa [SDL2](https://github.com/eden-emulator/mirror/blob/v0.2.1/externals/CMakeLists.txt). Su [driver](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/input_common/drivers/sdl_driver.cpp) elimina del GUID el CRC del nombre, asigna `port` entre mandos del mismo GUID y obtiene las asignaciones físicas de botones/ejes desde SDL. Ese `port` no es necesariamente el índice XInput. No basta con calcular un GUID genérico y escribir `player_0_*` para garantizar el mismo mando y mapeo en cualquier PC.

Por ahora, conecta el mando y, dentro de **Controles** de Eden, elige **Jugador 1**, un **Pro Controller** y el dispositivo **Xbox 360 Controller**; aplica el mapeo automático y guarda. Es una preparación inicial que se conserva en la configuración portable, aunque cambios de dispositivo/backend pueden exigir repetirla. Propuesta futura, sin implementar: enumerar con una SDL2 compatible, obtener GUID/puerto/bindings reales y solo entonces generar el perfil si sigue usando el teclado de fábrica. No se cambia ahora `player_0_*` ni se fuerza otro backend SDL.

## Diagnóstico de firmware de Eden

En Eden **v0.2.1**, `GetFirmwareVersionImpl` busca el título `0100000000000809` de tipo Data en la NAND del sistema, abre su RomFS y lee el archivo `file`. Solo si no obtiene ese RomFS sintetiza el archivo de versión. No compara la versión encontrada con un máximo de 22.0.0 en esa ruta. Fuente: [lectura de SystemVersion](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/hle/service/set/system_settings_server.cpp).

La versión sintetizada usa las constantes HLE **22.0.0** y emite `Using hardcoded firmware version`. Ese mensaje significa que no pudo usar el RomFS de SystemVersion instalado; no demuestra por sí solo que las claves sean antiguas ni que 22.0.0 sea el máximo firmware compatible. El objetivo HLE y la versión de un firmware instalado son cosas distintas. No se ha confirmado compatibilidad completa con 23.0.1. Fuentes: [versión de reserva](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/file_sys/system_archive/system_version.cpp), [constantes HLE](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/hle/api_version.h).

La ubicación esperada es `user\nand\system\Contents\registered` en portable. Copiar muchos NCA allí no prueba que se hayan podido registrar: el lector de NCA requiere las claves de área correspondientes a su generación, y la caché descarta metadatos que no puede interpretar. Un fallo puede deberse a claves incompatibles, archivos incompletos, falta del contenido/metadatos de SystemVersion o formato no admitido. La etiqueta «keys v22.5.0» no es una versión formal del archivo `prod.keys` ni permite concluir automáticamente que todos los NCA de un firmware posterior necesitan otra generación de clave. Fuentes: [ubicación de la NAND](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/file_sys/bis_factory.cpp), [validación de claves NCA](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/file_sys/content_archive.cpp), [registro del contenido](https://github.com/eden-emulator/mirror/blob/v0.2.1/src/core/file_sys/registered_cache.cpp).

`PrepareControllers("Switch")`, si el ejecutable es `eden.exe`, añade a `Message` un aviso cuando el último `user\log\eden_log.txt` contiene ese fallback. Menciona la fecha del archivo para no presentarlo como una prueba nueva; si se corrigieron las claves o la instalación después, hay que volver a arrancar Eden y generar otro log. La lectura se limita al último MiB, no utiliza logs rotados y no lee, muestra ni modifica claves. Si el log contiene además códigos explícitos de claves ausentes, se informa de ello sin atribuirlos necesariamente al firmware (podrían ser de un juego). No se asegura «keys < firmware» a partir del número 22.0.0. La ausencia del aviso tampoco certifica compatibilidad. Este diagnóstico no cambia `Changed` ni impide lanzar.

## Biblioteca para la interfaz

### Compatibilidad local de los juegos

`GameEntry.Compatibility` nunca es nulo, también al usar el constructor anterior. Su contrato es:

```csharp
public sealed class GameCompatibility {
    public string Status { get; }
    public string Date { get; }
    public string RecommendedUpdate { get; }
    public string Explanation { get; }
    public string UpdateHint { get; }
}
// GameEntry gana: public GameCompatibility Compatibility { get; }
```

`Status` es `Playable`, `Ingame`, `Intro`, `Loadable`, `Nothing` o `Desconocido`; PS2 también puede devolver `Menu` y `Perfect`. `Date` contiene `yyyy-MM-dd` o null. `RecommendedUpdate` conserva la versión de la base, por ejemplo `01.02`, o null. `Explanation` siempre contiene una explicación en español; `UpdateHint` es null si no hay advertencia de actualización. Un estado desconocido no significa incompatible. Las clasificaciones son históricas y no garantizan el rendimiento ni el resultado en el PC actual.

Para **PS3**, Nexo lee `TITLE_ID` del `PARAM.SFO` junto a `USRDIR` y consulta `GuiConfigs\compat_database.dat` junto al ejecutable de RPCS3 (objeto JSON `results`). Compara `update` con `APP_VER` de `dev_hdd0\game\<ID>\PARAM.SFO`. Si falta la actualización, avisa; si hay una versión anterior, indica cuál; si es igual o posterior, no avisa. Una revisión de disco que ya incluya esa versión también evita el aviso. Un SFO instalado ilegible produce «no se pudo comprobar», sin afirmar que falte la actualización. Esta consulta supone la ubicación portable estándar de `dev_hdd0`; una ruta VFS personalizada no se resuelve aquí.

**`update` es la última versión disponible en esa base, no la versión con la que se probó la compatibilidad.** RPCS3 la guarda como `latest_version` en su [lector oficial de compatibilidad](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/rpcs3qt/game_compatibility.cpp). Por eso Nexo dice «La base de RPCS3 indica la actualización 1.02; no la tienes instalada», sin afirmar que instalarla resuelva los bloqueos ni que fuera la versión de la prueba histórica.

Para **PS2**, consulta `resources\GameIndex.yaml` junto a PCSX2 y obtiene el serial desde `BOOT2` en `SYSTEM.CNF`, leyendo solo el PVD ISO9660, el directorio raíz (máximo 4 MiB) y ese archivo (máximo 64 KiB). No extrae la ISO. La [escala oficial de PCSX2 2.8.2](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2/Docs/GameIndex.md) es 0 desconocido, 1 Nothing, 2 Intro, 3 Menu, 4 Ingame, 5 Playable y 6 Perfect. CHD, CSO, BIN, GZ, ISO sin un ISO9660 reconocido, serial ausente o catálogo ambiguo quedan desconocidos; el juego sigue apareciendo.

Las bases de PS2/PS3 se leen una sola vez por escaneo (máximo 16 MiB cada una) y se refrescan en el siguiente, sin descargas. Si faltan o están dañadas, la biblioteca sigue disponible sin clasificación. PS4 utiliza la caché descrita a continuación; los demás sistemas quedan desconocidos. **Nexo nunca crea perfiles gráficos por juego ni modifica `config_database.dat`**: RPCS3 conserva sus ajustes automáticos de compatibilidad y cualquier perfil personalizado existente.

### Compatibilidad de PS4, también antes de instalar un PKG

La fuente es el [JSON de las releases del repositorio oficial de compatibilidad](https://github.com/shadps4-compatibility/shadps4-game-compatibility/releases/latest/download/compatibility_data.json), el mismo que utiliza el [lanzador Qt](https://github.com/shadps4-emu/shadps4-qtlauncher/blob/main/src/qt_gui/compatibility_info.cpp). No se usa la API de búsqueda de issues ni se envían nombres o IDs de la biblioteca: se descarga la base completa.

El escaneo de PS4 lee `TITLE_ID` de `sce_sys\param.sfo` para juegos extraídos y del PARAM.SFO validado del PKG base para pendientes. No necesita que el emulador esté instalado. La consulta se limita al CUSA exacto; no se presume que otra región tenga el mismo resultado. Sin ID, informe o caché válida devuelve `Desconocido`. PS5/Kyty no participa. PS4 se marca además como sistema experimental en `GameSystem.Experimental`.

`os-windows` tiene prioridad, aunque su resultado sea peor o más antiguo. Si falta, se utiliza el informe más reciente de otro sistema reconocido y se indica expresamente en `Explanation`. El mapeo es `nothing → Nothing`, `boots → Loadable`, `menus → Menu`, `ingame → Ingame`, `playable → Playable`. **`Ingame` no significa jugable:** puede tener problemas que impidan jugar o completar el juego. Para PS4, el distintivo `Loadable` debe decir «Arranca, sin llegar al menú»: `boots` puede mostrar imagen o sonido, así que la etiqueta genérica «no se ve» de PS3 no es exacta aquí. [Definiciones oficiales](https://github.com/shadps4-compatibility/shadps4-game-compatibility#shadps4-game-compatibility).

Contrato adicional, manteniendo los constructores anteriores mediante parámetros opcionales:

```csharp
// ArchiveEntry: nunca devuelve Compatibility null.
public string TitleId { get; }                 // CUSAxxxxx, o null
public GameCompatibility Compatibility { get; }

// GameCompatibility, compartido por GameEntry y ArchiveEntry:
public string EmulatorVersion { get; }         // versión/milestone indicada, o null
public string OperatingSystem { get; }         // Windows, Linux, macOS, No indicado, o null
public string ReportUrl { get; }               // enlace al issue oficial, o null

// NexoService: devuelve true cuando publicó una caché nueva.
public Task<bool> RefreshPs4CompatibilityAsync(CancellationToken ct = default);
```

**Integración de UI necesaria:** muestra primero `ScanGamesDetailedAsync("PS4")` y llama a `RefreshPs4CompatibilityAsync` como operación separada al abrir PS4 o al iniciar Nexo; no esperes a la descarga para presentar la biblioteca. Si devuelve `true`, reescanea y sustituye las entradas de juegos y PKG que sigan visibles. La UI debe observar la tarea y gestionar `OperationCanceledException` al cerrar. El Core no inicia descargas desde el escaneo ni lanza tareas de red ocultas. El método comprueba la caducidad: puedes llamarlo al volver a PS4 sin descargar en cada visita. Para la caja PKG usa `archive.Compatibility.Status` y `.Explanation`, como en los juegos; `Message` conserva los avisos de integridad/instalación.

La caché portable vive en `Juegos\PS4\.nexo-cache\shadps4-compatibility.json`. Se renueva tras siete días y se conserva indefinidamente para consulta sin conexión. Un error de red, permisos o JSON no reemplaza la copia anterior; el refresco devuelve `false` y retrasa una hora el siguiente intento durante esa sesión. La cancelación solicitada se propaga y permite reintentar. Sin copia, se muestra desconocido. Las solicitudes de refresco se serializan; el escaneo no espera a ellas. La descarga acepta solo HTTPS, el repositorio indicado y los hosts de assets de GitHub, como máximo cinco peticiones/redirecciones, 8 MiB y 20 segundos. Se valida el JSON antes de reemplazar el archivo atómicamente.

**Límite de la fuente:** el [generador oficial consultado](https://github.com/shadps4-compatibility/shadps4-game-compatibility/actions/runs/23677957663/workflow) exporta `status`, `version`, `last_tested` e `issue_number`, sin las etiquetas `issues: …`. Por eso no se atribuyen «fallos gráficos» o «audio» a un juego cuando el JSON no los detalla: `Explanation` invita a consultar `ReportUrl`. El lector admite etiquetas opcionales si se añaden al formato y traduce gráficos, audio, bloqueos, cierres, rendimiento y controles. No se hacen consultas adicionales por juego para completar esos campos.

Aunque se llame `last_tested`, el generador lo obtiene de `updated_at` del issue: `Date` se muestra como **actualización del informe**, no como fecha certificada de una prueba. `EmulatorVersion` procede del milestone del informe, no del emulador instalado. `RecommendedUpdate` y `UpdateHint` permanecen null en PS4: la base no proporciona ese dato. Las clasificaciones no garantizan que un juego arranque en la configuración actual.

Verificación de esta implementación: fuentes del lanzador y generador oficiales, y pruebas con JSON/HTTP simulados, sin red real. La descarga de la release no pudo ejecutarse desde el entorno restringido de desarrollo; queda para la comprobación de integración desde la sesión con acceso a Internet.

### Ruta gráfica de RPCS3 y archivos antiguos de Nexo

Las instalaciones actuales usan `config\config.yml`. Si ese archivo existe tiene prioridad; si solo existe una configuración antigua real en la raíz, se usa `config.yml`. Una instalación nueva usa `config\config.yml`.

El archivo raíz erróneo de versiones anteriores de Nexo solo se retira si contiene exactamente los dos ajustes `Video/Resolution Scale` y `Video/Anisotropic Filter Override`, ambos coincidentes con su recibo `.nexo-graphics.json`. Se prepara primero la configuración actual y su recibo, respetando los valores personalizados; después se comprueba otra vez que el archivo antiguo no cambió y se retiran ese archivo y su recibo. Una raíz sin recibo, editada, con comentarios u otros datos se conserva. No se escribe en `config\custom_configs`.

### Propuesta pendiente: instalar una actualización PKG de PS3

No implementado: una acción «Instalar actualización de PS3» podría elegir un `.pkg` local, comprobar su existencia y cabecera, y abrir `rpcs3.exe --installpkg "ruta.pkg"` con su carpeta como directorio de trabajo. RPCS3 muestra la instalación y valida el paquete; después se reescanea la biblioteca para actualizar `UpdateHint`. Devolver un PID solo confirmaría el arranque del proceso, no que se haya instalado correctamente. No se descargarían paquetes ni se modificarían perfiles por juego.

El [parser oficial disponible de RPCS3](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/rpcs3.cpp) admite `--installpkg` con ruta y rechaza esa instalación con `--no-gui`. El binario local 0.0.43-20393 contiene la ayuda «Forces the emulator to install this pkg file.» y ese rechazo. No se ejecutó el emulador ni se pudo recuperar la revisión exacta `06ae1af5` desde el servicio web: esto es contraste de fuente disponible y cadenas locales, no una prueba de instalación PKG.

Cada `GameSystem.GamesHint` expone las instrucciones de formatos de su carpeta de juegos. `NexoService.ScanGamesDetailedAsync(id, ct)` devuelve un `GameScanResult` con `Games`, `IgnoredCount` y `Reason`. `IgnoredCount` cuenta únicamente ROMs reconocidas de Retro que no tienen un núcleo compatible instalado junto al ejecutable detectado o elegido. No cuenta portadas, archivos desconocidos ni juegos fuera de los límites del recorrido. `Reason` es un mensaje en español o `null` cuando no hay exclusiones. Puede haber juegos visibles y ROMs excluidas a la vez: la interfaz debe mostrar el aviso también en ese caso. La biblioteca vacía sin exclusiones puede mostrar `GamesHint`.

`ScanGamesAsync(id, ct)` conserva su contrato y devuelve la misma lista de juegos. Ambos métodos comparten un solo recorrido por llamada; la interfaz debe usar uno u otro, sin llamar a ambos para la misma actualización. Las ISO de PS3 se exponen en `GameScanResult.Archives`, con el mismo `ArchiveEntry` y `ExtractArchiveAsync` que ZIP/7z/RAR. Solo después de extraer pasan a `Games`; RPCS3 continúa recibiendo el EBOOT, sin cambiar su CLI.

## Compilar sin Internet

Se necesitan un SDK de .NET con MSBuild, el targeting pack de **.NET Framework 4.8** y Windows PowerShell 5.1. En este PC se utiliza el SDK 10.0.302 para compilar aplicaciones dirigidas a Framework 4.8; el SDK no hace falta en el PC donde se instala Nexo.

Desde PowerShell, en la carpeta del proyecto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

El script usa únicamente el directorio del proyecto como fuente de paquetes y desactiva la auditoría de NuGet. No instala paquetes. Genera **`dist\Instalar Nexo.exe`** mediante IExpress, incluido en Windows, con la versión de `<Version>` de `src\Nexo\Nexo.csproj` en sus propiedades de archivo. El programa compilado se prepara en `dist\package\app` (se vacía en cada compilación) y se empaqueta en `dist\package\Nexo.zip`; no es un entregable aparte. La plantilla SED generada queda en `dist\package\Nexo.sed`. Para publicarlo en GitHub, renómbralo a `Instalar-Nexo.exe` (ver «Publicar una versión nueva» en README.md).

Al final ejecuta las pruebas de Core y una prueba de instalación, actualización y desinstalación en una carpeta temporal **dentro del proyecto**, conservando datos de muestra. Las pruebas no crean accesos directos, no abren webs y no lanzan emuladores reales. Si falla una prueba, el script termina con error. La comprobación visual de la interfaz y los mandos físicos se realiza por separado.

El prototipo anterior de Python/Edge está conservado en el commit `9052a8a`. No forma parte de la aplicación nativa.

## Preparar una instalación nueva: contrato de Core

La interfaz puede conectar estos métodos sin bloquear el hilo de WPF. No se inicia ninguna descarga al escanear o lanzar un juego.

```csharp
Task<EmulatorInstallResult> InstallEmulatorAsync(string systemId,
    IProgress<SetupProgress> progress, CancellationToken ct);
Task<SystemFilesResult> ImportSystemFilesAsync(string systemId,
    string[] paths, CancellationToken ct = default);

// Propiedades públicas, de solo lectura:
SetupProgress: string Stage; long BytesCompleted; long? TotalBytes;
EmulatorInstallResult: bool Changed; string Message; SystemStatus Status;
SystemFilesResult: bool Changed; string Message; SystemStatus Status;
SystemStatus.SetupMessages: IReadOnlyList<string>;
```

El progreso se refiere a la fase/paquete indicado en `Stage`, no al total de todos los paquetes. `TotalBytes == null` significa progreso indeterminado. Una cancelación devuelve `OperationCanceledException`; los fallos de instalación/importación se explican con `NexoException`. Las operaciones se serializan. La publicación final no se cancela a medias. Después de completar el asistente de RPCS3 hay que refrescar `GetStatus`, pues iniciar `--installfw` no equivale a instalar el firmware.

Las versiones están fijadas a las comprobadas: PCSX2 2.8.2, RPCS3 0.0.43-20393, PPSSPP 1.20.4, Dolphin 2609, shadPS4 SDL 0.20.0, RetroArch 1.22.2 y Eden 0.2.1 MSVC x64. Fuentes: publicaciones oficiales de [PCSX2](https://github.com/PCSX2/pcsx2/releases/tag/v2.8.2), [RPCS3](https://github.com/RPCS3/rpcs3-binaries-win/releases), [PPSSPP](https://github.com/hrydgard/ppsspp/releases/tag/v1.20.4), [shadPS4](https://github.com/shadps4-emu/shadPS4/releases/tag/v.0.20.0), [Dolphin](https://dolphin-emu.org/download/), [buildbot de RetroArch](https://buildbot.libretro.com/stable/1.22.2/windows/x86_64/) y [Eden](https://git.eden-emu.dev/eden-emu/eden/releases/tag/v0.2.1). No se consultan mirrors ajenos ni forks alternativos. KytyPS5 no se descarga.

Se consulta el `digest` SHA256 de los assets oficiales de GitHub cuando existe; RPCS3 además exige su archivo `.sha256`. Un hash distinto o un fallo al consultar esa publicación cancela la instalación. Los paquetes sin huella disponible se identifican como tales en el progreso: su procedencia se basa en HTTPS del servidor oficial, no se afirma una verificación criptográfica independiente. Hay límite de 4 GiB por descarga, 30 minutos por transferencia, 50 000 entradas y 16 GiB expandidos; se comprueba el espacio libre. Las redirecciones solo pueden llegar a los servidores oficiales/CDN enumerados en Core.

La extracción utiliza `tar.exe` por stdout y escribe cada ruta validada desde Core: rechaza rutas absolutas, `..`, enlaces, junctions, nombres duplicados y tamaños incoherentes. Comprueba un PE x64 con el nombre de ejecutable esperado. Todo se prepara bajo `.nexo-extract-*` y se publica de una vez como `Emuladores\<Id>\Nexo-<Id>`. Un emulador detectado previamente se conserva; no se fusionan instalaciones ni se actualizan automáticamente. Los paquetes temporales se eliminan al terminar o cancelar. Se crean los marcadores/carpetas portables de PCSX2, Dolphin, PPSSPP y Eden; mandos y gráficos se preparan al lanzar, cuando se puede consultar el mando conectado.

**Requisitos del equipo:** estas descargas son x64 y mantienen los requisitos de CPU/GPU de cada emulador. Se necesita un `tar.exe` de Windows capaz de leer sus 7z/ZIP; Windows antiguo puede necesitar actualizarlo. Dolphin, entre otros, requiere el [redistribuible Visual C++ x64 oficial de Microsoft](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Nexo no instala ese runtime ni controladores del sistema. Cambiar de PC no garantiza que todos los juegos o emuladores sean compatibles con su hardware.

### BIOS, firmware y claves

Cierra el emulador antes de importar BIOS o datos Switch. Se valida el lote antes de publicarlo; se rechazan enlaces y se conservan los archivos sustituidos con sufijo `.nexo-backup-<guid>`. Las claves se copian literalmente y nunca se incluyen en mensajes. Los backups también contienen datos privados: trátalos como los originales. Si falla una publicación se restauran los elementos anteriores. Ningún archivo fuente del usuario se borra.

- **PS2:** comprueba tamaño de ROM (4–8 MiB), tabla ROMDIR y ROMVER. Una selección anterior o carpeta BIOS personalizada no se cambia. Sin BIOS válida no se marca el asistente como terminado. Con BIOS válida se escribe su nombre relativo, `Folders/Bios=bios`, `UI/SettingsVersion=1` y `SetupWizardIncomplete=false`, conservando controles y otros ajustes. No se copia un INI preconfigurado de otro equipo. PCSX2 admite ajustes ausentes con sus defaults; su [comprobación de versión](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2/VMManager.cpp) y [arranque/asistente](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2-qt/QtHost.cpp) no exigen cientos de claves. La [selección BIOS](https://github.com/PCSX2/pcsx2/blob/v2.8.2/pcsx2-qt/Settings/BIOSSettingsWidget.cpp) usa `Filenames/BIOS`.
- **PS3:** acepta un `.pup` con cabecera PUP y abre `rpcs3.exe --installfw <archivo>` con argumentos citados. El binario instalado contiene esa opción; coincide con el [parser oficial disponible](https://github.com/RPCS3/rpcs3/blob/master/rpcs3/rpcs3.cpp). La revisión exacta `06ae1af5` sigue sin estar accesible desde el servicio web. No se combina con `--no-gui`, que el parser rechaza para instalar. El estado comprueba `dev_flash/sys/external/liblv2.sprx`, no certifica integridad de todo el firmware ni rutas VFS personalizadas. Sony sí publica el PUP en su [página oficial](https://www.playstation.com/en-us/support/hardware/ps3/system-software/); se deja como descarga desde esa página porque el enlace al binario cambia con cada actualización, sin fijar una URL antigua en Nexo.
- **Switch:** importa `prod.keys`/`title.keys` y ZIP con NCA, permitiendo una carpeta contenedora. Valida sintaxis acotada de claves, incluidos RSA/keyblobs, sin atribuirles una versión de firmware. El ZIP sustituye `registered` completo y conserva el anterior como backup para evitar mezclar versiones. Solo se comprueba estructura/tamaño mínimo de NCA: el contenido está cifrado y debe validarlo Eden con las claves. La presencia de ambos no certifica su compatibilidad; `SetupMessages` incorpora el diagnóstico del último log de Eden. Una etiqueta como «keys 22.5.0» no permite compararla de forma fiable con «firmware 23.0.1».

### Auditoría de mandos y portabilidad

`IR/Relative Input` es correcto en Dolphin 2609: lo declara el grupo [Cursor](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/InputCommon/ControllerEmu/ControlGroup/Cursor.cpp). Se corrigió la detección de fábrica: mantener `Buttons/A` y `Device` ya no basta; se contrastan el resto de claves presentes y los controles esenciales con [Wiimote::LoadDefaults](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Core/HW/WiimoteEmu/WiimoteEmu.cpp) y [Nunchuk::LoadDefaults](https://github.com/dolphin-emu/dolphin/blob/2609/Source/Core/Core/HW/WiimoteEmu/Extension/Nunchuk.cpp). Se conserva una extensión o fuente de control modificada. Un perfil nuevo usa calibración circular del stick, sin heredar la calibración cuadrada del teclado. La fuente del primer Wiimote ya es emulada por defecto.

Eden continúa necesitando la selección inicial del mando. Se volvió a comprobar `GetGUID`, normalización CRC y `port` en su fuente 0.2.1, así como la delegación XInput→HIDAPI/RawInput de SDL2. El cálculo determinista del GUID de **un backend concreto** no garantiza el dispositivo/backend/mapeo que Eden elige en otro PC. No se escribe un `player_0_*` inventado; la alternativa requiere enumerar dispositivos con una SDL compatible.

Los ejecutables elegidos dentro de `Emuladores` se guardan ahora como rutas relativas en `nexo.json`. Los absolutos antiguos siguen siendo legibles y se normalizan al volver a guardar una selección. Una instalación nueva no contiene rutas personales del equipo donde se preparó. Las selecciones externas explícitas, incluido un KytyPS5 fuera de Nexo, se conservan pero `SetupMessages` avisa que no viajan con la instalación. Los argumentos personalizados pueden seguir conteniendo rutas absolutas: son del usuario y no se reescriben.

Las pruebas de instalación usan respuestas HTTP y paquetes mínimos simulados, sin red real. Comprueban hashes, destinos hostiles, redirecciones, cancelación, rollback, conservación de configuraciones y primer arranque con BIOS sintética. La detección de los siete emuladores instalados y la BIOS real se comprobó adicionalmente en solo lectura; no se ejecutaron emuladores ni se modificó `C:\Nexo`.
