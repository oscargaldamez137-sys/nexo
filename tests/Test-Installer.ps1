param([Parameter(Mandatory=$true)][string]$PackageDirectory, [Parameter(Mandatory=$true)][string]$Workspace)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Workspace).TrimEnd('\')
$fixture = Join-Path $root ('.nexo-test-installer-' + [Guid]::NewGuid().ToString('N'))
# Carpetas de juegos y emuladores elegidas fuera de la instalación (Opciones › Carpetas, Nexo 1.1.0).
$external = $fixture + '-externo'
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
function Assert-True([bool]$Value, [string]$Message) { if (-not $Value) { throw $Message } }
try {
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PackageDirectory 'instalar.ps1') -Destination $fixture -NonInteractive -NoShortcuts -NoLaunch
    Assert-True ($LASTEXITCODE -eq 0) 'La instalación de prueba falló.'
    Assert-True (Test-Path -LiteralPath (Join-Path $fixture 'Nexo.exe')) 'No se copió Nexo.exe.'
    Assert-True (Test-Path -LiteralPath (Join-Path $fixture 'LEEME.md')) 'No se incluyó la guía de uso.'
    foreach ($id in @('Retro', 'PS2', 'PS3', 'PS4', 'PS5', 'PSP', 'GameCube-Wii', 'Switch', 'GBC', 'GBA', 'Xbox', 'Xbox360')) {
        Assert-True (Test-Path -LiteralPath (Join-Path $fixture ('Juegos\' + $id + '\LEEME.txt'))) "Falta Juegos\$id."
        Assert-True (Test-Path -LiteralPath (Join-Path $fixture ('Emuladores\' + $id + '\LEEME.txt'))) "Falta Emuladores\$id."
    }
    # Actualización automática fallida: sin ventanas, deja el motivo en el registro que lee Nexo.
    $log = Join-Path $fixture 'error-actualizacion.txt'
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $null = & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PackageDirectory 'instalar.ps1') -Destination 'ruta-relativa' -NonInteractive -NoShortcuts -NoLaunch -WaitSeconds 1 -LogPath $log 2>&1
    } finally { $ErrorActionPreference = $previousPreference }
    Assert-True ($LASTEXITCODE -ne 0) 'Una ruta no válida debe fallar.'
    Assert-True ((Test-Path -LiteralPath $log) -and ([IO.File]::ReadAllText($log) -match 'ruta absoluta')) 'La actualización silenciosa no registró el error.'
    $game = Join-Path $fixture 'Juegos\PS2\personal.iso'
    $emulator = Join-Path $fixture 'Emuladores\PS2\personal.exe'
    $config = Join-Path $fixture 'nexo.json'
    [IO.File]::WriteAllText($game, 'mi juego')
    [IO.File]::WriteAllText($emulator, 'mi emulador')
    $externalGame = Join-Path $external 'Juegos\PS2\externo.iso'
    $externalEmulator = Join-Path $external 'Emuladores\PS2\PCSX2\pcsx2-qt.exe'
    foreach ($path in @($externalGame, $externalEmulator)) { [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) }
    [IO.File]::WriteAllText($externalGame, 'juego externo')
    [IO.File]::WriteAllText($externalEmulator, 'emulador externo')
    # Configuración real de 1.1.0 con carpetas externas y opciones cambiadas: actualizar debe conservarla byte a byte.
    $settings = '{"version":1,"systems":{"PS2":{"executable":"Emuladores/PS2/personal.exe"}},"downloadArtwork":false,"sounds":false,"checkUpdates":false,"gamesRoot":' +
        (ConvertTo-Json (Join-Path $external 'Juegos')) + ',"emulatorsRoot":' + (ConvertTo-Json (Join-Path $external 'Emuladores')) + '}'
    [IO.File]::WriteAllText($config, $settings)
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PackageDirectory 'instalar.ps1') -Destination $fixture -NonInteractive -NoShortcuts -NoLaunch
    Assert-True ($LASTEXITCODE -eq 0) 'La actualización de prueba falló.'
    Assert-True ([IO.File]::ReadAllText($game) -eq 'mi juego') 'La actualización cambió un juego.'
    Assert-True ([IO.File]::ReadAllText($emulator) -eq 'mi emulador') 'La actualización cambió un emulador.'
    Assert-True ([IO.File]::ReadAllText($config) -eq $settings) 'La actualización cambió los ajustes.'
    Assert-True ([IO.File]::ReadAllText($externalGame) -eq 'juego externo') 'La actualización tocó la carpeta de juegos externa.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $external 'Juegos\PS2\LEEME.txt'))) 'El instalador no debe escribir en carpetas externas.'
    $manifest = Join-Path $fixture 'nexo-installed-files.txt'
    $originalManifest = [IO.File]::ReadAllText($manifest)
    [IO.File]::WriteAllText($manifest, "Nexo.exe`r`ndummy\..\Juegos\PS2\personal.iso")
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $rejection = & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'desinstalar.ps1') -NonInteractive -NoShortcuts 2>&1
    } finally { $ErrorActionPreference = $previousPreference }
    Assert-True ($LASTEXITCODE -ne 0) 'Un registro manipulado no debe permitir borrar juegos.'
    Assert-True (Test-Path -LiteralPath (Join-Path $fixture 'Nexo.exe')) 'Se borró un archivo antes de validar todo el registro.'
    Assert-True ([IO.File]::ReadAllText($game) -eq 'mi juego') 'El registro manipulado borró un juego.'
    [IO.File]::WriteAllText($manifest, $originalManifest)
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'desinstalar.ps1') -NonInteractive -NoShortcuts
    Assert-True ($LASTEXITCODE -eq 0) 'La desinstalación de prueba falló.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $fixture 'Nexo.exe'))) 'El ejecutable sigue instalado.'
    Assert-True ([IO.File]::ReadAllText($game) -eq 'mi juego') 'La desinstalación cambió un juego.'
    Assert-True ([IO.File]::ReadAllText($emulator) -eq 'mi emulador') 'La desinstalación cambió un emulador.'
    Assert-True ([IO.File]::ReadAllText($config) -eq $settings) 'La desinstalación cambió los ajustes.'
    Assert-True ([IO.File]::ReadAllText($externalGame) -eq 'juego externo') 'La desinstalación tocó la carpeta de juegos externa.'
    Assert-True ([IO.File]::ReadAllText($externalEmulator) -eq 'emulador externo') 'La desinstalación tocó la carpeta de emuladores externa.'
    Assert-True (@(Get-ChildItem -LiteralPath $external -Recurse -Force).Count -eq 7) 'La desinstalación cambió el contenido de las carpetas externas.'
    Write-Host 'OK  Instalación, actualización y desinstalación conservan los datos y las carpetas externas; sin accesos directos durante las pruebas.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($root + '\.nexo-test-installer-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Limpieza fuera del proyecto.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    $resolvedExternal = [IO.Path]::GetFullPath($external)
    if (-not $resolvedExternal.StartsWith($root + '\.nexo-test-installer-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Limpieza fuera del proyecto.' }
    if (Test-Path -LiteralPath $resolvedExternal) { Remove-Item -LiteralPath $resolvedExternal -Recurse -Force }
}
