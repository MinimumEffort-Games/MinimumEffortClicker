param([Parameter(Mandatory)][string]$Destination, [switch]$RoundedBackground)
$ErrorActionPreference = 'Stop'
# Render the owner's exact text, not an illustrated mascot. Windows icons need raster frames.
Add-Type -AssemblyName PresentationCore,WindowsBase
$mark = '¯\_(ツ)_/¯'
$path = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $path) { throw 'Choose a new asset filename.' }
$visual = [Windows.Media.DrawingVisual]::new()
$context = $visual.RenderOpen()
try {
    if ($RoundedBackground) {
        $background = [Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(38,38,38))
        $context.DrawRoundedRectangle($background,[Windows.Media.Pen]::new([Windows.Media.Brushes]::DimGray,3),[Windows.Rect]::new(2,2,252,252),42,42)
    }
    $text = [Windows.Media.FormattedText]::new($mark,[Globalization.CultureInfo]::InvariantCulture,
        [Windows.FlowDirection]::LeftToRight,[Windows.Media.Typeface]::new('Segoe UI'),
        48,[Windows.Media.Brushes]::White,1)
    $scale = $(if ($RoundedBackground) { 216 } else { 238 }) / $text.WidthIncludingTrailingWhitespace
    $context.PushTransform([Windows.Media.TranslateTransform]::new(128-$text.WidthIncludingTrailingWhitespace*$scale/2,128-$text.Height*$scale/2))
    $context.PushTransform([Windows.Media.ScaleTransform]::new($scale,$scale))
    $geometry = $text.BuildGeometry([Windows.Point]::new(0,0))
    # A narrow dark glyph edge keeps the literal text legible on a light taskbar.
    $context.DrawGeometry([Windows.Media.Brushes]::White,[Windows.Media.Pen]::new([Windows.Media.Brushes]::Black,0.9),$geometry)
    $context.Pop(); $context.Pop()
} finally { $context.Close() }
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(256,256,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream = [IO.File]::Open($path,[IO.FileMode]::CreateNew)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
$mark | Set-Content -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($path)) 'brand-mark.txt') -Encoding utf8
