# Rebuilds the app icon from the owner's artwork: rounded corners (radius 26 at 256 px), every size Windows uses for
# the exe, the taskbar and the tray, plus the PNGs the Linux package uses. Re-runnable; overwrites only its own outputs.
# The artwork came as WebP; Windows cannot read WebP without an add-on, so its lossless PNG copy is the input here.
param(
    [string]$Source = (Join-Path $PSScriptRoot '../assets/brand/source/shrug-gold.png'),
    [string]$OutputFolder = (Join-Path $PSScriptRoot '../assets/brand/rounded-r26')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$artwork = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
if ($artwork.Width -ne $artwork.Height) { throw "The icon artwork must be square; it is $($artwork.Width) x $($artwork.Height)." }

function New-RoundedIcon([int]$size) {
    # Scale first, then fill an anti-aliased rounded square with the scaled picture, so the corners stay smooth.
    $scaled = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($scaled)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($artwork, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
    $g.Dispose()
    $icon = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($icon)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $radius = [Math]::Max(2.0, $size * 26.0 / 256.0); $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $path.AddArc(0, $size - $d, $d, $d, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.TextureBrush $scaled
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose(); $g.Dispose(); $scaled.Dispose()
    return $icon
}

function Get-PngBytes($bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray(); $stream.Dispose(); return ,$bytes
}

$out = (Resolve-Path $OutputFolder).Path
# Windows picks from these for the exe, title bar, taskbar and tray at 100-200 % display scaling.
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$images = foreach ($size in $sizes) { $bitmap = New-RoundedIcon $size; [pscustomobject]@{ Size = $size; Png = (Get-PngBytes $bitmap) }; $bitmap.Dispose() }

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
[System.IO.File]::WriteAllBytes((Join-Path $out 'shrug-rounded-r26.ico'), $ico.ToArray())
$writer.Dispose()

# Loose PNGs: 256 for the Linux package and app window, 16/32/48 for previews.
foreach ($image in $images | Where-Object Size -in 16, 32, 48) { [System.IO.File]::WriteAllBytes((Join-Path $out "shrug-rounded-r26-$($image.Size).png"), $image.Png) }
[System.IO.File]::WriteAllBytes((Join-Path $out 'shrug-rounded-r26.png'), ($images | Where-Object Size -eq 256).Png)
$artwork.Dispose()
"Wrote $($images.Count) sizes to $out"
