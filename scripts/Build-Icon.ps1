param([Parameter(Mandatory)][string]$InputPath, [Parameter(Mandatory)][string]$OutputPath)
# Deterministic PNG-to-multi-size ICO conversion; no artwork alterations.
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputPath) { throw 'Choose a new icon filename; existing artwork is preserved.' }
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($InputPath))
$frames = @()
try {
    foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
        $bitmap = [Drawing.Bitmap]::new($size,$size,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $scale = [Math]::Min($size/$source.Width,$size/$source.Height)
            $width = [single]($source.Width*$scale); $height = [single]($source.Height*$scale)
            $graphics.DrawImage($source,[single](($size-$width)/2),[single](($size-$height)/2),$width,$height)
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size=$size; Bytes=$stream.ToArray() }
            if ($size -in @(16,32,48)) {
                $preview = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) ([IO.Path]::GetFileNameWithoutExtension($OutputPath)+"-$size.png")
                $bitmap.Save($preview,[Drawing.Imaging.ImageFormat]::Png)
            }
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $file = [IO.File]::Open([IO.Path]::GetFullPath($OutputPath),[IO.FileMode]::CreateNew)
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16*$frames.Count
        foreach ($frame in $frames) {
            $encodedSize = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$encodedSize); $writer.Write([byte]$encodedSize)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    } finally { $writer.Dispose() }
} finally { $source.Dispose() }
