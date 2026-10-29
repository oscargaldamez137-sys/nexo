[CmdletBinding()]
param([switch]$NonInteractive, [switch]$NoShortcuts)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$installRoot = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')

function Safe-InstalledPath([string]$Relative) {
    if (-not $Relative -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')) { throw 'Ruta de desinstalación no válida.' }
    $full = [IO.Path]::GetFullPath((Join-Path $installRoot $Relative))
    if ($installRoot.Length -le 3 -or -not $full.StartsWith($installRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'La ruta sale de la instalación.' }
    for ($path = $full; $path; $path = [IO.Path]::GetDirectoryName($path)) {
        if ((Test-Path -LiteralPath $path) -and (((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'No se borrarán enlaces ni junctions.' }
    }
    return $full
}

try {
    $removeData = $false
    if (-not $NonInteractive) {
        $answer = [Windows.Forms.MessageBox]::Show("¿Quieres enviar también TODOS tus emuladores, juegos y ajustes a la Papelera?`r`n`r`nSí: desinstala el programa y envía esos datos a la Papelera.`r`nNo: desinstala el programa y CONSERVA tus datos.`r`nCancelar: no hace cambios.", 'Desinstalar Nexo', 'YesNoCancel', 'Warning', 'Button2')
        if ($answer -eq 'Cancel') { exit 0 }
        $removeData = $answer -eq 'Yes'
    }
    $targetExe = Join-Path $installRoot 'Nexo.exe'
    foreach ($process in (Get-Process -Name Nexo -ErrorAction SilentlyContinue)) {
        $runningPath = $null
        try { $runningPath = $process.Path } catch { }
        if ($runningPath -and $runningPath.Equals($targetExe, [StringComparison]::OrdinalIgnoreCase)) { throw 'Cierra Nexo antes de desinstalarlo.' }
    }
    $manifest = Safe-InstalledPath 'nexo-installed-files.txt'
    if (-not (Test-Path -LiteralPath $manifest)) { throw 'Falta el registro de archivos instalados. No se borrará nada automáticamente.' }
    # Validate the complete manifest before deleting its first entry.
    $owned = @()
    foreach ($relative in [IO.File]::ReadAllLines($manifest)) {
        if ($relative -match '^(Emuladores|Juegos)([\\/]|$)' -or $relative -ieq 'nexo.json') { throw 'El registro no debe incluir datos personales.' }
        $checked = Safe-InstalledPath $relative
        $normalized = $checked.Substring($installRoot.Length + 1)
        if ($normalized -match '^(Emuladores|Juegos)([\\/]|$)' -or $normalized -ieq 'nexo.json') { throw 'El registro no debe incluir datos personales.' }
        $owned += $checked
    }
    $dataPaths = @()
    if ($removeData) {
        foreach ($relative in @('Emuladores', 'Juegos', 'nexo.json')) {
            $path = Safe-InstalledPath $relative
            if (Test-Path -LiteralPath $path -PathType Container) {
                # Do not traverse any junction even when deleting data was confirmed.
                $queue = New-Object 'Collections.Generic.Queue[string]'
                $queue.Enqueue($path)
                while ($queue.Count -gt 0) {
                    foreach ($item in (Get-ChildItem -LiteralPath $queue.Dequeue() -Force)) {
                        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Hay un enlace o junction entre los datos. No se borrarán automáticamente.' }
                        if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) }
                    }
                }
            }
            $dataPaths += $path
        }
    }
    # Recycle personal data BEFORE removing the program. If Windows cancels or
    # fails, keep the uninstaller available; never retry with permanent deletion.
    if ($dataPaths.Count -gt 0) {
        Add-Type -AssemblyName Microsoft.VisualBasic
        foreach ($path in $dataPaths) {
            if (Test-Path -LiteralPath $path -PathType Container) {
                [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory($path,
                    [Microsoft.VisualBasic.FileIO.UIOption]::AllDialogs,
                    [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
                    [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
            } elseif (Test-Path -LiteralPath $path -PathType Leaf) {
                [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($path,
                    [Microsoft.VisualBasic.FileIO.UIOption]::AllDialogs,
                    [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin,
                    [Microsoft.VisualBasic.FileIO.UICancelOption]::ThrowException)
            }
        }
    }
    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        $desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Nexo.lnk'
        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nexo'
        foreach ($path in @($desktopLink, (Join-Path $menu 'Nexo.lnk'), (Join-Path $menu 'Desinstalar Nexo.lnk'))) {
            if (Test-Path -LiteralPath $path) {
                $shortcut = $shell.CreateShortcut($path)
                if ($shortcut.TargetPath -ieq $targetExe -or $shortcut.Arguments -eq ('"' + (Join-Path $installRoot 'Desinstalar Nexo.vbs') + '"')) { Remove-Item -LiteralPath $path -Force }
            }
        }
        if ((Test-Path -LiteralPath $menu) -and @(Get-ChildItem -LiteralPath $menu -Force).Count -eq 0) { Remove-Item -LiteralPath $menu }
    }
    foreach ($path in $owned) { if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force } }
    Remove-Item -LiteralPath $manifest -Force
    if (-not $NonInteractive) { [void][Windows.Forms.MessageBox]::Show('Nexo se ha desinstalado. Los datos se han tratado según tu elección.', 'Nexo', 'OK', 'Information') }
}
catch {
    if ($NonInteractive) { throw }
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'No se pudo desinstalar Nexo', 'OK', 'Error')
    exit 1
}
