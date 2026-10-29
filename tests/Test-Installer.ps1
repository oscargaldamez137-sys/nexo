param([Parameter(Mandatory=$true)][string]$PackageDirectory, [Parameter(Mandatory=$true)][string]$Workspace)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Workspace).TrimEnd('\')
$fixture = Join-Path $root ('.nexo-test-installer-' + [Guid]::NewGuid().ToString('N'))
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
    [IO.File]::WriteAllText($config, 'mis ajustes')
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PackageDirectory 'instalar.ps1') -Destination $fixture -NonInteractive -NoShortcuts -NoLaunch
    Assert-True ($LASTEXITCODE -eq 0) 'La actualización de prueba falló.'
    Assert-True ([IO.File]::ReadAllText($game) -eq 'mi juego') 'La actualización cambió un juego.'
    Assert-True ([IO.File]::ReadAllText($emulator) -eq 'mi emulador') 'La actualización cambió un emulador.'
    Assert-True ([IO.File]::ReadAllText($config) -eq 'mis ajustes') 'La actualización cambió los ajustes.'
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
    Assert-True ([IO.File]::ReadAllText($config) -eq 'mis ajustes') 'La desinstalación cambió los ajustes.'
    Write-Host 'OK  Instalación, actualización y desinstalación conservan los datos; sin accesos directos durante las pruebas.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($root + '\.nexo-test-installer-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Limpieza fuera del proyecto.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
