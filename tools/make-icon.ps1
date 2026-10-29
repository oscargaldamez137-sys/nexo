# Genera src/Nexo/Nexo.ico (256, 64, 48, 32 y 16 px) con System.Drawing. Sin descargas.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot '..\src\Nexo\Nexo.ico'

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0
    # Fondo: cuadrado redondeado con degradado violeta-azul oscuro.
    $r = 8 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2*$r, 2*$r, 180, 90)
    $path.AddArc($size - 2*$r - 0.5, 0, 2*$r, 2*$r, 270, 90)
    $path.AddArc($size - 2*$r - 0.5, $size - 2*$r - 0.5, 2*$r, 2*$r, 0, 90)
    $path.AddArc(0, $size - 2*$r - 0.5, 2*$r, 2*$r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), ([System.Drawing.Color]::FromArgb(255, 0x2A, 0x23, 0x5C)), ([System.Drawing.Color]::FromArgb(255, 0x0B, 0x0D, 0x18))
    $g.FillPath($bg, $path)
    # Halo cian detrás de la letra.
    if ($size -ge 48) {
        for ($i = 16; $i -ge 1; $i--) {
            $glow = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(8, 0x7C, 0xF4, 0xFF)), ([single](3.4 * $s + $i * 0.75 * $s))
            $glow.StartCap = 'Round'; $glow.EndCap = 'Round'; $glow.LineJoin = 'Round'
            $g.DrawLines($glow, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF (9*$s), (23*$s)), (New-Object System.Drawing.PointF (9*$s), (9*$s)), (New-Object System.Drawing.PointF (23*$s), (23*$s)), (New-Object System.Drawing.PointF (23*$s), (9*$s))))
        }
    }
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0x7C, 0xF4, 0xFF)), ([single]([Math]::Max(1.6, 3.4 * $s)))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF (9*$s), (23*$s)), (New-Object System.Drawing.PointF (9*$s), (9*$s)), (New-Object System.Drawing.PointF (23*$s), (23*$s)), (New-Object System.Drawing.PointF (23*$s), (9*$s))))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 256, 64, 48, 32, 16
$frames = foreach ($size in $sizes) { ,(New-Frame $size) }
$stream = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $stream
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Flush()
[System.IO.File]::WriteAllBytes($out, $stream.ToArray())
Write-Output "Icono: $out ($($stream.Length) bytes)"
