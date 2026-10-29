[CmdletBinding()]
param(
    [string]$Destination = 'C:\Nexo',
    [switch]$NonInteractive,
    [switch]$NoShortcuts,
    [switch]$NoLaunch,
    # Actualización automática desde Nexo: segundos que se espera a que Nexo se cierre solo.
    [ValidateRange(0, 600)][int]$WaitSeconds = 0,
    # Actualización automática: archivo donde se deja el error, porque no hay ventana que lo muestre.
    [string]$LogPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

function Assert-LocalDirectory([string]$Path) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path.Contains('"')) { throw 'Elige una ruta absoluta en una unidad local.' }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($full.Length -le 3) { throw 'Elige una carpeta, no la raíz de una unidad.' }
    $current = $full
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'No se admiten enlaces ni junctions en la ruta de instalación.' }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
    return $full
}

function Assert-Child([string]$Root, [string]$Relative) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':')) { throw 'Ruta de paquete no válida.' }
    $full = [IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if (-not $full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'El paquete intenta escribir fuera de su carpeta.' }
    [void](Assert-LocalDirectory $full)
    return $full
}

function Choose-Destination([string]$Initial) {
    $form = New-Object Windows.Forms.Form
    $form.Text = 'Instalar Nexo'
    $form.ClientSize = New-Object Drawing.Size(560, 195)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $label = New-Object Windows.Forms.Label
    $label.Text = "Elige dónde instalar Nexo.`r`nLos emuladores y juegos se guardarán dentro de esta carpeta."
    $label.Location = New-Object Drawing.Point(20, 20)
    $label.Size = New-Object Drawing.Size(520, 45)
    $input = New-Object Windows.Forms.TextBox
    $input.Text = $Initial
    $input.Location = New-Object Drawing.Point(20, 78)
    $input.Size = New-Object Drawing.Size(420, 28)
    $browse = New-Object Windows.Forms.Button
    $browse.Text = 'Examinar...'
    $browse.Location = New-Object Drawing.Point(450, 76)
    $browse.Size = New-Object Drawing.Size(90, 28)
    $browse.Add_Click({
        $dialog = New-Object Windows.Forms.FolderBrowserDialog
        $dialog.Description = 'Carpeta de instalación de Nexo'
        if (Test-Path -LiteralPath $input.Text -PathType Container) { $dialog.SelectedPath = $input.Text }
        if ($dialog.ShowDialog($form) -eq 'OK') { $input.Text = $dialog.SelectedPath }
        $dialog.Dispose()
    })
    $install = New-Object Windows.Forms.Button
    $install.Text = 'Instalar'
    $install.Location = New-Object Drawing.Point(340, 140)
    $install.Size = New-Object Drawing.Size(95, 32)
    $install.DialogResult = 'OK'
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancelar'
    $cancel.Location = New-Object Drawing.Point(445, 140)
    $cancel.Size = New-Object Drawing.Size(95, 32)
    $cancel.DialogResult = 'Cancel'
    $form.Controls.AddRange(@($label, $input, $browse, $install, $cancel))
    $form.AcceptButton = $install
    $form.CancelButton = $cancel
    try { if ($form.ShowDialog() -eq 'OK') { return $input.Text }; return $null }
    finally { $form.Dispose() }
}

function Write-Shortcut([string]$Path, [string]$Target, [string]$Arguments, [string]$WorkingDirectory) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = (Join-Path $WorkingDirectory 'Nexo.exe') + ',0'
    $shortcut.Description = 'Nexo - tus consolas y juegos'
    $shortcut.Save()
}

$stage = $null
try {
    if (-not $NonInteractive) { $Destination = Choose-Destination $Destination; if (-not $Destination) { exit 0 } }
    $installRoot = Assert-LocalDirectory $Destination
    $targetExe = Join-Path $installRoot 'Nexo.exe'
    foreach ($process in (Get-Process -Name Nexo -ErrorAction SilentlyContinue)) {
        $runningPath = $null
        try { $runningPath = $process.Path } catch { }
        if ($runningPath -and $runningPath.Equals($targetExe, [StringComparison]::OrdinalIgnoreCase)) {
            if ($WaitSeconds -gt 0 -and $process.WaitForExit($WaitSeconds * 1000)) { continue }
            throw 'Cierra Nexo antes de actualizar esta instalación.'
        }
    }
    [void][IO.Directory]::CreateDirectory($installRoot)
    $stage = Join-Path $installRoot ('.nexo-install-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    $zipPath = Join-Path $PSScriptRoot 'Nexo.zip'
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.Name) { continue }
            $relative = $entry.FullName.Replace('/', '\')
            $checked = Assert-Child $stage $relative
            $normalized = $checked.Substring($stage.Length + 1)
            if ($normalized -match '^(Emuladores|Juegos)(\\|$)' -or $normalized -ieq 'nexo.json') { throw 'El paquete no debe contener juegos, emuladores ni ajustes.' }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $stage)
    if (-not (Test-Path -LiteralPath (Join-Path $stage 'Nexo.exe')) -or -not (Test-Path -LiteralPath (Join-Path $stage 'Nexo.Core.dll'))) { throw 'El paquete de Nexo está incompleto.' }
    $manifest = New-Object 'Collections.Generic.List[string]'
    foreach ($file in (Get-ChildItem -LiteralPath $stage -Recurse -File)) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $destinationFile = Assert-Child $installRoot $relative
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationFile))
        [IO.File]::Copy($file.FullName, $destinationFile, $true)
        $manifest.Add($relative)
    }
    foreach ($name in @('desinstalar.ps1', 'Desinstalar Nexo.vbs')) {
        [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Assert-Child $installRoot $name), $true)
        $manifest.Add($name)
    }
    $notes = [ordered]@{
        'Retro' = 'ROMs nes, sfc/smc, gba, gb/gbc, md/gen/smd, n64/z64/v64, pce, sms/gg. RetroArch necesita un núcleo compatible instalado en cores.'
        'PS2' = 'Juegos iso, chd, cso, bin o gz. Emulador: PCSX2.'
        'PS3' = 'Juegos extraídos: PS3_GAME\USRDIR\EBOOT.BIN o USRDIR\EBOOT.BIN, con PARAM.SFO e ICON0.PNG. Emulador: RPCS3. Sin ISO ni PKG directos.'
        'PSP' = 'Juegos iso, cso, pbp, chd o elf. Emulador: PPSSPP.'
        'GameCube-Wii' = 'Juegos iso, gcm, wbfs, rvz, ciso, gcz, wad, dol o elf. Emulador: Dolphin.'
        'PS4' = 'Carpeta con eboot.bin y sce_sys\param.sfo o param.json; portada sce_sys\icon0.png. Emulador: shadPS4.'
        'Switch' = 'Juegos nsp, xci, nca o nro. Familias Ryujinx o yuzu (Eden/Citron). Algunos NCA, actualizaciones y DLC no arrancan por sí solos.'
        'PS5' = 'Carpeta con eboot.bin y sce_sys\param.json; portada sce_sys\icon0.png. KytyPS5: soporte experimental.'
        'GBC' = 'ROMs .gbc y .gb. Comparte mGBA portable en Emuladores\GBA; no requiere BIOS.'
        'GBA' = 'ROMs .gba. mGBA compartido con GBC; BIOS opcional.'
        'Xbox' = 'Discos .iso XISO. xemu necesita MCPX, BIOS compatible y HDD .qcow2. Nexo descarga el HDD inicial oficial al instalar xemu.'
        'Xbox360' = 'Discos .iso, juegos extraídos con default.xex y contenedores de juego STFS/GOD sin extensión. Conserva los archivos .data. Xenia Canary, experimental, sin BIOS.'
    }
    foreach ($id in $notes.Keys) {
        foreach ($category in @('Emuladores', 'Juegos')) {
            $directory = Assert-Child $installRoot ($category + '\' + $id)
            [void][IO.Directory]::CreateDirectory($directory)
            $readme = Assert-Child $installRoot ($category + '\' + $id + '\LEEME.txt')
            if (-not (Test-Path -LiteralPath $readme)) {
                $text = if ($category -eq 'Emuladores') { 'Copia aquí el emulador completo, con todas sus subcarpetas, o usa Instalar emulador en Nexo (excepto PS5). GBC y GBA comparten mGBA.' } else { 'Copia aquí tus juegos. Portada opcional: imagen PNG/JPG del mismo nombre junto al juego, o en covers.' }
                [IO.File]::WriteAllText($readme, $text + "`r`n" + $notes[$id], (New-Object Text.UTF8Encoding($true)))
            }
        }
    }
    [IO.File]::WriteAllLines((Assert-Child $installRoot 'nexo-installed-files.txt'), $manifest, (New-Object Text.UTF8Encoding($true)))
    if (-not $NoShortcuts) {
        $desktop = [Environment]::GetFolderPath('DesktopDirectory')
        $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nexo'
        [void][IO.Directory]::CreateDirectory($menu)
        Write-Shortcut (Join-Path $desktop 'Nexo.lnk') $targetExe '' $installRoot
        Write-Shortcut (Join-Path $menu 'Nexo.lnk') $targetExe '' $installRoot
        Write-Shortcut (Join-Path $menu 'Desinstalar Nexo.lnk') (Join-Path $env:SystemRoot 'System32\wscript.exe') ('"' + (Join-Path $installRoot 'Desinstalar Nexo.vbs') + '"') $installRoot
    }
    if (-not $NonInteractive) {
        $answer = [Windows.Forms.MessageBox]::Show("Nexo está instalado en:`r`n$installRoot`r`n`r`n¿Quieres abrirlo ahora?", 'Nexo', 'YesNo', 'Information')
        if ($answer -eq 'Yes' -and -not $NoLaunch) { Start-Process -FilePath $targetExe -WorkingDirectory $installRoot }
    }
    elseif (-not $NoLaunch) { Start-Process -FilePath $targetExe -WorkingDirectory $installRoot }
}
catch {
    if ($NonInteractive) {
        if ($LogPath) {
            try { [IO.File]::WriteAllText($LogPath, ((Get-Date).ToString('s') + ' ' + $_.Exception.Message + "`r`n"), (New-Object Text.UTF8Encoding($true))) } catch { }
        }
        # Una actualización automática fallida no deja al usuario sin Nexo: se reabre la versión que había.
        if ($WaitSeconds -gt 0 -and -not $NoLaunch -and $targetExe -and (Test-Path -LiteralPath $targetExe)) {
            try { Start-Process -FilePath $targetExe -WorkingDirectory $installRoot } catch { }
        }
        throw
    }
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'No se pudo instalar Nexo', 'OK', 'Error')
    exit 1
}
finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        if (-not $resolvedStage.StartsWith($installRoot + '\.nexo-install-', [StringComparison]::OrdinalIgnoreCase)) { throw 'Ruta temporal de limpieza no válida.' }
        [void](Assert-LocalDirectory $resolvedStage)
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
