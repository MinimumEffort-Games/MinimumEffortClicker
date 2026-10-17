# Builds the app icon from the owner's artwork (assets/ui/generated/brand/app-icon.png, cut from the icon sheet by
# scripts/Build-UiArt.ps1): every size Windows uses for the exe, the title bar, the taskbar and the tray, plus the PNGs
# the Linux package and window use. The artwork already has its own rounded outline on a transparent square, so it is
# only scaled. Re-runnable; overwrites only its own outputs.
param(
    [string]$Source = (Join-Path $PSScriptRoot '../assets/ui/generated/brand/app-icon.png'),
    [string]$OutputFolder = (Join-Path $PSScriptRoot '../assets/brand/app-icon')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$artwork = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
if ($artwork.Width -ne $artwork.Height) { throw "The icon artwork must be square; it is $($artwork.Width) x $($artwork.Height)." }

function New-Icon([int]$size) {
    $icon = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($icon)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($artwork, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()
    return $icon
}

function Get-PngBytes($bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray(); $stream.Dispose(); return ,$bytes
}

New-Item -ItemType Directory -Force $OutputFolder | Out-Null
$out = (Resolve-Path $OutputFolder).Path
# Windows picks from these for the exe, title bar, taskbar and tray at 100-200 % display scaling.
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($size in $sizes) { $bitmap = New-Icon $size; [pscustomobject]@{ Size = $size; Png = (Get-PngBytes $bitmap) }; $bitmap.Dispose() }

# An .ico with PNG-compressed images (supported since Windows Vista).
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $edge = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$image.Png.Length); $writer.Write([uint32]$offset)
    $offset += $image.Png.Length
}
foreach ($image in $images) { $writer.Write($image.Png) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $out 'app-icon.ico'), $ico.ToArray())
$writer.Dispose()

# Loose PNGs: 256 for the Linux package and app window, 16/32/48 for previews.
foreach ($image in $images | Where-Object Size -in 16, 32, 48) { [System.IO.File]::WriteAllBytes((Join-Path $out "app-icon-$($image.Size).png"), $image.Png) }
[System.IO.File]::WriteAllBytes((Join-Path $out 'app-icon.png'), ($images | Where-Object Size -eq 256).Png)
$artwork.Dispose()
"Wrote $($images.Count) sizes to $out"
