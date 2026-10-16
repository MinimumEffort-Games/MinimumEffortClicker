# Cuts the UI art in assets/ui/source into the separate images the Windows app loads (assets/ui/generated).
# Re-runnable: the generated folder is rebuilt each time and the sources are never changed. The keyboard and
# controller sheets are generated art, not an exact grid, so every icon is found by its own outline: the opaque
# shapes are labelled, the largest ones are taken in reading order, and loose specks around them are left out.
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;

public static class UiArtShapes
{
    /// <summary>Labels the 8-connected shapes whose alpha is at least threshold. Returns one entry per shape:
    /// minX, minY, maxX, maxY, area, label.</summary>
    public static List<int[]> Shapes(byte[] bgra, int w, int h, int threshold, int[] labels)
    {
        var shapes = new List<int[]>();
        var stack = new Stack<int>();
        int next = 0;
        for (int start = 0; start < w * h; start++)
        {
            if (labels[start] != 0 || bgra[start * 4 + 3] < threshold) continue;
            int label = ++next, minX = w, minY = h, maxX = 0, maxY = 0, area = 0;
            labels[start] = label; stack.Push(start);
            while (stack.Count > 0)
            {
                int p = stack.Pop(), x = p % w, y = p / w; area++;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int q = ny * w + nx;
                        if (labels[q] != 0 || bgra[q * 4 + 3] < threshold) continue;
                        labels[q] = label; stack.Push(q);
                    }
            }
            shapes.Add(new[] { minX, minY, maxX, maxY, area, label });
        }
        return shapes;
    }

    /// <summary>The shape with the largest area.</summary>
    public static int[] Largest(List<int[]> shapes)
    {
        int[] best = shapes[0];
        foreach (var s in shapes) if (s[4] > best[4]) best = s;
        return best;
    }

    /// <summary>One rectangle around every shape, in the same layout as a shape (label -1).</summary>
    public static int[] Bounds(List<int[]> shapes)
    {
        var b = new[] { int.MaxValue, int.MaxValue, 0, 0, 0, -1 };
        foreach (var s in shapes) { b[0] = Math.Min(b[0], s[0]); b[1] = Math.Min(b[1], s[1]); b[2] = Math.Max(b[2], s[2]); b[3] = Math.Max(b[3], s[3]); }
        return b;
    }

    /// <summary>Copies one shape with its soft edge: pixels within grow of the shape keep their colour and alpha,
    /// everything else in the rectangle (a neighbour's speck or shadow) becomes transparent.</summary>
    public static byte[] Extract(byte[] bgra, int w, int h, int[] labels, int[] shape, int grow, out int cw, out int ch, out int cx, out int cy)
    {
        cx = Math.Max(0, shape[0] - grow); cy = Math.Max(0, shape[1] - grow);
        int x1 = Math.Min(w - 1, shape[2] + grow), y1 = Math.Min(h - 1, shape[3] + grow);
        cw = x1 - cx + 1; ch = y1 - cy + 1;
        var mask = new bool[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++) mask[y * cw + x] = labels[(cy + y) * w + cx + x] == shape[5];
        // Square dilation, done as a horizontal then a vertical pass.
        var wide = new bool[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                for (int d = -grow; d <= grow && !wide[y * cw + x]; d++)
                { int nx = x + d; if (nx >= 0 && nx < cw && mask[y * cw + nx]) wide[y * cw + x] = true; }
        var keep = new bool[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                for (int d = -grow; d <= grow && !keep[y * cw + x]; d++)
                { int ny = y + d; if (ny >= 0 && ny < ch && wide[ny * cw + x]) keep[y * cw + x] = true; }
        var crop = new byte[cw * ch * 4];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
                if (keep[y * cw + x]) Array.Copy(bgra, ((cy + y) * w + cx + x) * 4, crop, (y * cw + x) * 4, 4);
        return crop;
    }
}
'@

$source = Join-Path $Root 'assets/ui/source'
$output = Join-Path $Root 'assets/ui/generated'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
foreach ($folder in 'keys', 'controller', 'actions', 'devices') { New-Item -ItemType Directory -Force (Join-Path $output $folder) | Out-Null }

function Read-Pixels([string]$path) {
    $image = [Drawing.Bitmap]::FromFile($path)
    try {
        $bitmap = [Drawing.Bitmap]::new($image.Width, $image.Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($bitmap); $g.DrawImage($image, 0, 0, $image.Width, $image.Height); $g.Dispose()
    } finally { $image.Dispose() }
    $rect = [Drawing.Rectangle]::new(0, 0, $bitmap.Width, $bitmap.Height)
    $data = $bitmap.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = [byte[]]::new($data.Stride * $bitmap.Height)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bitmap.UnlockBits($data)
    $result = [pscustomobject]@{ Bytes = $bytes; Width = $bitmap.Width; Height = $bitmap.Height }
    $bitmap.Dispose(); $result
}

# Writes BGRA bytes as a PNG, scaled down (never up) so the longer side is at most $max pixels.
function Write-Png([byte[]]$bytes, [int]$width, [int]$height, [string]$path, [int]$max = 0) {
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $data = $bitmap.LockBits([Drawing.Rectangle]::new(0, 0, $width, $height), [Drawing.Imaging.ImageLockMode]::WriteOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    $bitmap.UnlockBits($data)
    if ($max -gt 0 -and [Math]::Max($width, $height) -gt $max) {
        $scale = $max / [Math]::Max($width, $height)
        $small = [Drawing.Bitmap]::new([int][Math]::Round($width * $scale), [int][Math]::Round($height * $scale), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($small)
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $g.DrawImage($bitmap, 0, 0, $small.Width, $small.Height); $g.Dispose()
        $bitmap.Dispose(); $bitmap = $small
    }
    $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
}

# Cuts a sheet into its icons, in reading order, and names them by $names (one entry per icon, rows of $columns).
function Split-Sheet([string]$sheet, [string[]]$names, [int]$columns, [string]$folder, [int]$max = 0) {
    $pixels = Read-Pixels (Join-Path $source $sheet)
    $labels = [int[]]::new($pixels.Width * $pixels.Height)
    $shapes = [UiArtShapes]::Shapes($pixels.Bytes, $pixels.Width, $pixels.Height, 160, $labels)
    $sorted = @($shapes | Sort-Object { $_[4] } -Descending)
    if ($sorted.Count -lt $names.Count) { throw "$sheet has $($sorted.Count) shapes; $($names.Count) icons were expected." }
    $icons = $sorted[0..($names.Count - 1)]
    $smallestIcon = $icons[-1][4]; $largestSpeck = if ($sorted.Count -gt $names.Count) { $sorted[$names.Count][4] } else { 0 }
    # Rows: sort by centre height, then each run of $columns by centre across.
    $byRow = @($icons | Sort-Object { ($_[1] + $_[3]) / 2 })
    $ordered = for ($r = 0; $r -lt $names.Count; $r += $columns) { $byRow[$r..($r + $columns - 1)] | Sort-Object { ($_[0] + $_[2]) / 2 } }
    $i = 0
    foreach ($shape in $ordered) {
        $cw = 0; $ch = 0; $cx = 0; $cy = 0
        $crop = [UiArtShapes]::Extract($pixels.Bytes, $pixels.Width, $pixels.Height, $labels, $shape, 3, [ref]$cw, [ref]$ch, [ref]$cx, [ref]$cy)
        Write-Png $crop $cw $ch (Join-Path $output "$folder/$($names[$i]).png") $max
        $i++
    }
    "{0}: {1} icons (smallest {2} px), largest speck left out {3} px" -f $sheet, $names.Count, $smallestIcon, $largestSpeck
}

# Keyboard sheet in reading order (see key-map.json), named after the app's key names (SequenceKeys) so the app
# finds a keycap from the key it is showing. Keys the app cannot press keep a name for the layout only.
$keyNames = @(
    'Escape', 'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10', 'F11', 'F12',
    'Backtick', '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 'Minus', 'Equals',
    'Tab', 'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P', 'Left bracket', 'Right bracket',
    'Caps Lock', 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L', 'Semicolon', 'Quote', 'Backslash',
    'Left Shift', 'Z', 'X', 'C', 'V', 'B', 'N', 'M', 'Comma', 'Period', 'Slash', 'Right Shift', 'Enter',
    'Left Ctrl', 'Left Win', 'Left Alt', 'Space', 'Right Alt', 'Right Win', 'Menu', 'Right Ctrl', 'Backspace', 'Print Screen', 'Scroll Lock', 'Pause', 'Insert',
    'Delete', 'Home', 'End', 'Page Up', 'Page Down', 'Up', 'Left', 'Down', 'Right', 'Num Lock', 'Numpad /', 'Numpad *', 'Numpad -',
    'Numpad 7', 'Numpad 8', 'Numpad 9', 'Numpad +', 'Numpad 4', 'Numpad 5', 'Numpad 6', 'Numpad 1', 'Numpad 2', 'Numpad 3', 'Numpad Enter', 'Numpad 0', 'Numpad .')
# Same rule as KeyArt.FileName in the app.
function Get-KeyFileName([string]$name) {
    $name.ToLowerInvariant().Replace('/', 'slash').Replace('*', 'star').Replace('+', 'plus').Replace('-', 'minus').Replace('.', 'period').Replace(' ', '-')
}
Split-Sheet 'keyboard-sheet.png' @($keyNames | ForEach-Object { Get-KeyFileName $_ }) 13 'keys'

$controllerNames = @(
    'a', 'b', 'x', 'y', 'lb', 'rb',
    'lt', 'rt', 'ls', 'rs', 'l3', 'r3',
    'dpad-up', 'dpad-down', 'dpad-left', 'dpad-right', 'dpad', 'guide',
    'view-symbol', 'menu-symbol', 'share-symbol', 'view', 'menu', 'share',
    'ls-up', 'ls-down', 'ls-left', 'ls-right', 'lb-rb', 'lt-rt')
Split-Sheet 'xbox-sheet.png' $controllerNames 6 'controller' 128

# Single images: trimmed to what is drawn (the largest shape for the devices, every visible pixel for the action
# icons, whose strokes are separate pieces) and scaled to a size the app never has to scale up.
function Trim-Image([string]$file, [string]$target, [int]$max, [switch]$LargestShape) {
    $pixels = Read-Pixels (Join-Path $source $file)
    $labels = [int[]]::new($pixels.Width * $pixels.Height)
    $shapes = [UiArtShapes]::Shapes($pixels.Bytes, $pixels.Width, $pixels.Height, 24, $labels)
    $shape = if ($LargestShape) { [UiArtShapes]::Largest($shapes) } else { [UiArtShapes]::Bounds($shapes) }
    $x0 = [Math]::Max(0, $shape[0] - 2); $y0 = [Math]::Max(0, $shape[1] - 2)
    $w = [Math]::Min($pixels.Width - 1, $shape[2] + 2) - $x0 + 1; $h = [Math]::Min($pixels.Height - 1, $shape[3] + 2) - $y0 + 1
    if ($LargestShape) {
        $cw = 0; $ch = 0; $cx = 0; $cy = 0
        $crop = [UiArtShapes]::Extract($pixels.Bytes, $pixels.Width, $pixels.Height, $labels, $shape, 2, [ref]$cw, [ref]$ch, [ref]$cx, [ref]$cy)
        Write-Png $crop $cw $ch (Join-Path $output $target) $max
    } else {
        $crop = [byte[]]::new($w * $h * 4)
        for ($y = 0; $y -lt $h; $y++) { [Array]::Copy($pixels.Bytes, (($y0 + $y) * $pixels.Width + $x0) * 4, $crop, $y * $w * 4, $w * 4) }
        Write-Png $crop $w $h (Join-Path $output $target) $max
    }
}
Trim-Image 'mouse.png' 'devices/mouse.png' 192 -LargestShape
Trim-Image 'keyboard.png' 'devices/keyboard.png' 384 -LargestShape
foreach ($icon in Get-ChildItem (Join-Path $source 'actions') -Filter *.png) { Trim-Image "actions/$($icon.Name)" "actions/$($icon.Name)" 128 }
"Written to $output"
