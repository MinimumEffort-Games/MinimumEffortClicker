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

    /// <summary>
    /// One button of a picture (the labelled mouse, the controller): the face around a point (with its label inside it)
    /// and the cream outline around that face, up to ring pixels deep. Returns 0 outside, 1 face, 2 outline.
    /// </summary>
    public static byte[] Button(byte[] bgra, int w, int h, int seedX, int seedY, int ring)
    {
        Func<int, bool> opaque = p => bgra[p * 4 + 3] >= 128;
        Func<int, int> light = p => (bgra[p * 4] + bgra[p * 4 + 1] * 2 + bgra[p * 4 + 2]) / 4;
        Func<int, int> chroma = p => Math.Max(bgra[p * 4], Math.Max(bgra[p * 4 + 1], bgra[p * 4 + 2])) - Math.Min(bgra[p * 4], Math.Min(bgra[p * 4 + 1], bgra[p * 4 + 2]));
        // A face is anything but the cream outline: charcoal, or a coloured face such as the yellow Y.
        Func<int, bool> dark = p => opaque(p) && !(light(p) >= 130 && chroma(p) < 80);
        // The nearest dark pixel to the given point, so the point does not have to avoid the label.
        int seed = -1;
        for (int r = 0; r < 60 && seed < 0; r++)
            for (int dy = -r; dy <= r && seed < 0; dy++)
                for (int dx = -r; dx <= r && seed < 0; dx++)
                {
                    int x = seedX + dx, y = seedY + dy;
                    if (x >= 0 && y >= 0 && x < w && y < h && dark(y * w + x)) seed = y * w + x;
                }
        if (seed < 0) throw new InvalidOperationException("No dark face near " + seedX + "," + seedY);
        var result = new byte[w * h];
        var stack = new Stack<int>();
        stack.Push(seed); result[seed] = 1;
        while (stack.Count > 0)
        {
            int p = stack.Pop(), x = p % w, y = p / w;
            foreach (int q in new[] { x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1 })
                if (q >= 0 && result[q] == 0 && dark(q)) { result[q] = 1; stack.Push(q); }
        }
        // Holes (the label, the wheel's ridges, light specks) belong to the face: everything not reachable from the
        // image edge without crossing the face.
        var outside = new bool[w * h];
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        void Seed(int p) { if (!outside[p] && result[p] == 0) { outside[p] = true; stack.Push(p); } }
        while (stack.Count > 0)
        {
            int p = stack.Pop(), x = p % w, y = p / w;
            foreach (int q in new[] { x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1 })
                if (q >= 0 && !outside[q] && result[q] == 0) { outside[q] = true; stack.Push(q); }
        }
        for (int p = 0; p < w * h; p++) if (!outside[p]) result[p] = 1;
        // The outline: light pixels reached from the face, at most ring steps out.
        var front = new List<int>();
        for (int p = 0; p < w * h; p++) if (result[p] == 1) front.Add(p);
        for (int step = 0; step < ring && front.Count > 0; step++)
        {
            var next = new List<int>();
            foreach (int p in front)
            {
                int x = p % w, y = p / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int q = ny * w + nx;
                        if (result[q] == 0 && opaque(q) && !dark(q)) { result[q] = 2; next.Add(q); }
                    }
            }
            front = next;
        }
        return result;
    }
    /// <summary>Copies one traced button into a cropped outline mask (white where the outline is) and a cropped
    /// button map (red channel = index where the face or outline is).</summary>
    public static void Paint(byte[] traced, int w, int x0, int y0, int cw, int ch, byte[] ring, byte[] map, byte index)
    {
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                byte m = traced[(y0 + y) * w + x0 + x];
                int o = (y * cw + x) * 4;
                if (m == 2) { ring[o] = ring[o + 1] = ring[o + 2] = ring[o + 3] = 255; }
                if (m > 0) { map[o + 2] = index; map[o + 3] = 255; }
            }
    }
    /// <summary>One quarter of a traced cross or stick ring, by direction from its centre (the middle of its face):
    /// 0 up, 1 down, 2 left, 3 right. Pixels in the other quarters are cleared.</summary>
    public static byte[] Sector(byte[] traced, int w, int h, int sector)
    {
        long sx = 0, sy = 0, n = 0;
        for (int p = 0; p < w * h; p++) if (traced[p] == 1) { sx += p % w; sy += p / w; n++; }
        double cx = (double)sx / n, cy = (double)sy / n;
        var part = new byte[w * h];
        for (int p = 0; p < w * h; p++)
        {
            if (traced[p] == 0) continue;
            double dx = p % w - cx, dy = p / w - cy;
            int s = Math.Abs(dy) >= Math.Abs(dx) ? (dy < 0 ? 0 : 1) : (dx < 0 ? 2 : 3);
            if (s == sector) part[p] = traced[p];
        }
        return part;
    }

    /// <summary>One rectangle around every shape of at least fraction of the largest one's area, so loose specks do
    /// not widen it (layout as a shape, label -1).</summary>
    public static int[] BoundsOfLarge(List<int[]> shapes, double fraction)
    {
        int largest = Largest(shapes)[4];
        return Bounds(shapes.FindAll(s => s[4] >= largest * fraction));
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
foreach ($folder in 'keys', 'controller', 'actions', 'devices', 'brand', 'overlay') { New-Item -ItemType Directory -Force (Join-Path $output $folder) | Out-Null }

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
function Write-Png([byte[]]$bytes, [int]$width, [int]$height, [string]$path, [int]$max = 0, [switch]$Nearest) {
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $data = $bitmap.LockBits([Drawing.Rectangle]::new(0, 0, $width, $height), [Drawing.Imaging.ImageLockMode]::WriteOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    $bitmap.UnlockBits($data)
    if ($max -gt 0 -and [Math]::Max($width, $height) -gt $max) {
        $scale = $max / [Math]::Max($width, $height)
        $small = [Drawing.Bitmap]::new([int][Math]::Round($width * $scale), [int][Math]::Round($height * $scale), [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [Drawing.Graphics]::FromImage($small)
        $g.InterpolationMode = if ($Nearest) { [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor } else { [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic }
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
# The overlay's buttons: the padlock, closed (left of the sheet) and open (right; both also on the Settings lock
# toggle), bring the clicker back (the four corners) and quit (the X).
Split-Sheet 'lock-sheet.png' @('locked', 'unlocked') 2 'overlay' 96
Trim-Image 'overlay-show.png' 'overlay/show.png' 96
Trim-Image 'overlay-quit.png' 'overlay/quit.png' 96
foreach ($icon in Get-ChildItem (Join-Path $source 'actions') -Filter *.png) { Trim-Image "actions/$($icon.Name)" "actions/$($icon.Name)" 128 }

# Pictures to choose a button on (the labelled mouse, the controller): the picture, a mask per button that the app
# colours (green when chosen, red when taken), and a map of which button each pixel belongs to, read by the app to
# know which button was clicked (red channel = the button's map value). Each button is traced from a point on its
# face, in the source picture's pixels. An entry with four names and values is a cross or stick split into
# up, down, left and right; on a stick it is the outer ring, and the stick's own press is a separate entry after it.
function Trace-Picture([string]$file, [string]$prefix, [object[]]$buttons, [int]$ring) {
    $pixels = Read-Pixels (Join-Path $source $file)
    $labels = [int[]]::new($pixels.Width * $pixels.Height)
    # The whole picture, triggers included, which are separate shapes on the controller.
    $body = [UiArtShapes]::BoundsOfLarge([UiArtShapes]::Shapes($pixels.Bytes, $pixels.Width, $pixels.Height, 24, $labels), 0.01)
    $x0 = [Math]::Max(0, $body[0] - 2); $y0 = [Math]::Max(0, $body[1] - 2)
    $cw = [Math]::Min($pixels.Width - 1, $body[2] + 2) - $x0 + 1; $ch = [Math]::Min($pixels.Height - 1, $body[3] + 2) - $y0 + 1
    $picture = [byte[]]::new($cw * $ch * 4)
    for ($y = 0; $y -lt $ch; $y++) { [Array]::Copy($pixels.Bytes, (($y0 + $y) * $pixels.Width + $x0) * 4, $picture, $y * $cw * 4, $cw * 4) }
    Write-Png $picture $cw $ch (Join-Path $output "devices/$prefix.png") 512
    $map = [byte[]]::new($cw * $ch * 4)
    foreach ($button in $buttons) {
        $traced = [UiArtShapes]::Button($pixels.Bytes, $pixels.Width, $pixels.Height, $button[2], $button[3], $ring)
        $parts = [Collections.Generic.List[object[]]]::new()
        if ($button[0] -is [array]) { foreach ($s in 0..3) { $parts.Add(@($button[0][$s], $button[1][$s], [UiArtShapes]::Sector($traced, $pixels.Width, $pixels.Height, $s))) } }
        else { $parts.Add(@($button[0], $button[1], $traced)) }
        foreach ($part in $parts) {
            $mask = [byte[]]::new($cw * $ch * 4)
            [UiArtShapes]::Paint($part[2], $pixels.Width, $x0, $y0, $cw, $ch, $mask, $map, [byte]$part[1])
            Write-Png $mask $cw $ch (Join-Path $output "devices/$prefix-$($part[0]).png") 512
        }
    }
    Write-Png $map $cw $ch (Join-Path $output "devices/$prefix-map.png") 512 -Nearest
}
# Mouse map values: 40 Left, 80 Middle, 120 Right, 160 Side 1 (the "4"), 200 Side 2 (the "5"); see MouseButtonPicker.
Trace-Picture 'mouse-buttons.png' 'mouse-buttons' @(
    @('left', 40, 440, 200), @('middle', 80, 612, 262), @('right', 120, 850, 200), @('side1', 160, 245, 730), @('side2', 200, 245, 520)) 30
# Controller map values: the ControllerButton number times 8; names are the ControllerButton names in lower case.
Trace-Picture 'controller-layout.png' 'controller-layout' @(
    @('lefttrigger', 56, 180, 110), @('righttrigger', 64, 800, 110), @('leftbumper', 40, 180, 255), @('rightbumper', 48, 800, 255),
    @('y', 32, 925, 575), @('x', 24, 840, 672), @('b', 16, 1030, 640), @('a', 8, 940, 770),
    @('view', 72, 510, 650), @('menu', 80, 718, 705), @('share', 168, 600, 785),
    @(@('leftstickup', 'leftstickdown', 'leftstickleft', 'leftstickright'), @(184, 192, 200, 208), 215, 662),
    @('leftstickpress', 88, 265, 640),
    @(@('rightstickup', 'rightstickdown', 'rightstickleft', 'rightstickright'), @(216, 224, 232, 240), 702, 880),
    @('rightstickpress', 96, 750, 860),
    @(@('dpadup', 'dpaddown', 'dpadleft', 'dpadright'), @(104, 112, 120, 128), 440, 860)) 18
# The app icon: one picture from the icon sheet (the rounded square with the shrug and MEC), cut along its outline and
# centred on a transparent square. scripts/Update-AppIcon.ps1 makes the Windows .ico and the Linux PNG from it. The
# point is on the icon, in the sheet's pixels.
$sheet = Read-Pixels (Join-Path $source 'icon-sheet.png')
$sheetLabels = [int[]]::new($sheet.Width * $sheet.Height)
$sheetShapes = [UiArtShapes]::Shapes($sheet.Bytes, $sheet.Width, $sheet.Height, 160, $sheetLabels)
$iconLabel = $sheetLabels[195 * $sheet.Width + 662]
$iconShape = $sheetShapes | Where-Object { $_[5] -eq $iconLabel } | Select-Object -First 1
if ($null -eq $iconShape) { throw 'The app icon was not found on icon-sheet.png at 662, 195.' }
$cw = 0; $ch = 0; $cx = 0; $cy = 0
$crop = [UiArtShapes]::Extract($sheet.Bytes, $sheet.Width, $sheet.Height, $sheetLabels, $iconShape, 3, [ref]$cw, [ref]$ch, [ref]$cx, [ref]$cy)
$side = [Math]::Max($cw, $ch)
$square = [byte[]]::new($side * $side * 4)
$left = [int](($side - $cw) / 2); $top = [int](($side - $ch) / 2)
for ($y = 0; $y -lt $ch; $y++) { [Array]::Copy($crop, $y * $cw * 4, $square, (($top + $y) * $side + $left) * 4, $cw * 4) }
New-Item -ItemType Directory -Force (Join-Path $output 'brand') | Out-Null
Write-Png $square $side $side (Join-Path $output 'brand/app-icon.png')
"Written to $output"
