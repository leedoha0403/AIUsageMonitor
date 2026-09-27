# Builds UsageMonitorWpf/Assets/AppIcon.ico (+ AppIcon.png) from the source artwork.
# Large sizes (>= 40px) are the artwork cropped to its rounded square; small sizes (<= 32px, tray/title bar)
# are redrawn as simple vector shapes of the same design so they stay crisp.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1 -Source <image> [-Preview <png>]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [string]$OutDir = "",
    [string]$Preview = ""
)

Add-Type -AssemblyName PresentationCore, WindowsBase, PresentationFramework
$ErrorActionPreference = "Stop"
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\UsageMonitorWpf\Assets" }
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Color($hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Frozen($brush) { $brush.Freeze(); $brush }

# ---- source artwork: crop to the rounded square (bounding box of non-black pixels)
$decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create([Uri](Resolve-Path $Source).Path, "None", "OnLoad")
$src = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap $decoder.Frames[0], ([System.Windows.Media.PixelFormats]::Bgra32), $null, 0
$w = $src.PixelWidth; $h = $src.PixelHeight
$px = New-Object byte[] ($w * $h * 4); $src.CopyPixels($px, $w * 4, 0)
$minX = $w; $maxX = 0; $minY = $h; $maxY = 0
for ($y = 0; $y -lt $h; $y += 2) {
    for ($x = 0; $x -lt $w; $x += 2) {
        $i = ($y * $w + $x) * 4
        if (($px[$i] + $px[$i + 1] + $px[$i + 2]) -gt 45) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$crop = New-Object System.Windows.Media.Imaging.CroppedBitmap $src, (New-Object System.Windows.Int32Rect $minX, $minY, ($maxX - $minX + 1), ($maxY - $minY + 1))
$crop.Freeze()

function Render([int]$size, [scriptblock]$draw) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, "HighQuality")
    $dc = $visual.RenderOpen()
    & $draw $dc $size
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    $rtb
}

$drawArtwork = {
    param($dc, $s)
    $radius = $s * 0.215
    $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry (New-Object System.Windows.Rect 0, 0, $s, $s), $radius, $radius))
    $dc.DrawImage($crop, (New-Object System.Windows.Rect -0.5, -0.5, ($s + 1), ($s + 1)))
    $dc.Pop()
}

function ArcGeometry($cx, $cy, $r, [double]$fromDeg, [double]$toDeg) {
    $a0 = $fromDeg * [Math]::PI / 180; $a1 = $toDeg * [Math]::PI / 180
    $p0 = New-Object System.Windows.Point ($cx + $r * [Math]::Cos($a0)), ($cy + $r * [Math]::Sin($a0))
    $p1 = New-Object System.Windows.Point ($cx + $r * [Math]::Cos($a1)), ($cy + $r * [Math]::Sin($a1))
    $geo = New-Object System.Windows.Media.StreamGeometry
    $ctx = $geo.Open()
    $ctx.BeginFigure($p0, $false, $false)
    $ctx.ArcTo($p1, (New-Object System.Windows.Size $r, $r), 0, (($toDeg - $fromDeg) -gt 180), "Clockwise", $true, $false)
    $ctx.Close()
    $geo
}

$drawVector = {
    param($dc, $s)
    $radius = $s * 0.22
    $bg = New-Object System.Windows.Media.LinearGradientBrush
    $bg.StartPoint = "0,0"; $bg.EndPoint = "1,1"
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color "#16433C"), 0))
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color "#0E1D22"), 0.55))
    $bg.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color "#0B1418"), 1))
    $edge = New-Object System.Windows.Media.Pen (Frozen (New-Object System.Windows.Media.SolidColorBrush (Color "#6630C49E"))), 1
    $dc.DrawRoundedRectangle($bg, $edge, (New-Object System.Windows.Rect 0.5, 0.5, ($s - 1), ($s - 1)), $radius, $radius)

    $cx = $s * 0.5; $cy = $s * 0.5
    $r = if ($s -le 20) { $s * 0.345 } else { $s * 0.30 }
    $t = [Math]::Max(1.6, $s * 0.095)
    $dim = New-Object System.Windows.Media.Pen (Frozen (New-Object System.Windows.Media.SolidColorBrush (Color "#35696B"))), $t
    $dim.StartLineCap = "Round"; $dim.EndLineCap = "Round"
    $dc.DrawGeometry($null, $dim, (ArcGeometry $cx $cy $r 190 250))
    $bright = New-Object System.Windows.Media.LinearGradientBrush
    $bright.StartPoint = "1,0"; $bright.EndPoint = "0,1"
    $bright.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color "#6CF7CF"), 0))
    $bright.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color "#2FD1A8"), 0.6))
    $bright.GradientStops.Add((New-Object System.Windows.Media.GradientStop (Color "#1C9C86"), 1))
    $pen = New-Object System.Windows.Media.Pen $bright, $t
    $pen.StartLineCap = "Round"; $pen.EndLineCap = "Round"
    $dc.DrawGeometry($null, $pen, (ArcGeometry $cx $cy $r -78 176))

    $colors = "#2F7C78", "#34B592", "#6FF2C4"
    if ($s -le 20) {
        # Tray sizes: whole-pixel bars with a 1px gap so the three bars stay distinct.
        $bw = [Math]::Max(2, [Math]::Round($s * 0.125)); $gap = 1
        $x0 = [Math]::Round($s / 2 - (3 * $bw + 2 * $gap) / 2)
        $bottom = [Math]::Round($s * 0.70)
        $heights = [Math]::Round($s * 0.19), [Math]::Round($s * 0.31), [Math]::Round($s * 0.44)
        for ($k = 0; $k -lt 3; $k++) {
            $fill = Frozen (New-Object System.Windows.Media.SolidColorBrush (Color $colors[$k]))
            $dc.DrawRectangle($fill, $null, (New-Object System.Windows.Rect ($x0 + $k * ($bw + $gap)), ($bottom - $heights[$k]), $bw, $heights[$k]))
        }
    }
    else {
        $bottom = $s * 0.665; $bw = [Math]::Max(1.6, $s * 0.105); $br = $bw * 0.3
        $bars = @(@(0.37, 0.15), @(0.50, 0.23), @(0.63, 0.32))
        for ($k = 0; $k -lt 3; $k++) {
            $x = $s * $bars[$k][0] - $bw / 2; $bh = $s * $bars[$k][1]
            $fill = Frozen (New-Object System.Windows.Media.SolidColorBrush (Color $colors[$k]))
            $dc.DrawRoundedRectangle($fill, $null, (New-Object System.Windows.Rect $x, ($bottom - $bh), $bw, $bh), $br, $br)
        }
    }
}

# ---- ICO writer: PNG entries for >= 64px, 32-bit DIB entries below (widest compatibility)
function StraightBgra($bitmap) {
    $s = $bitmap.PixelWidth
    $data = New-Object byte[] ($s * $s * 4); $bitmap.CopyPixels($data, $s * 4, 0)
    for ($i = 0; $i -lt $data.Length; $i += 4) {
        $a = $data[$i + 3]
        if ($a -gt 0 -and $a -lt 255) {
            for ($c = 0; $c -lt 3; $c++) { $data[$i + $c] = [byte][Math]::Min(255, [Math]::Round($data[$i + $c] * 255.0 / $a)) }
        }
    }
    $data
}

function PngBytes($bitmap) {
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $ms = New-Object System.IO.MemoryStream; $enc.Save($ms); $ms.ToArray()
}

function DibBytes($bitmap) {
    $s = $bitmap.PixelWidth
    $pixels = StraightBgra $bitmap
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]($s * $s * 4 + $maskRow * $s)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $s * 4, $s * 4) }
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush(); $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @{}
foreach ($s in $sizes) { $images[$s] = if ($s -le 32) { Render $s $drawVector } else { Render $s $drawArtwork } }

$entries = foreach ($s in $sizes) { , @($s, $(if ($s -ge 64) { PngBytes $images[$s] } else { DibBytes $images[$s] })) }
$ico = New-Object System.IO.MemoryStream
$iw = New-Object System.IO.BinaryWriter $ico
$iw.Write([int16]0); $iw.Write([int16]1); $iw.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($e in $entries) {
    $s = $e[0]; $bytes = $e[1]
    $iw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $iw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $iw.Write([byte]0); $iw.Write([byte]0); $iw.Write([int16]1); $iw.Write([int16]32)
    $iw.Write([int]$bytes.Length); $iw.Write([int]$offset); $offset += $bytes.Length
}
foreach ($e in $entries) { $iw.Write([byte[]]$e[1]) }
$iw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "AppIcon.ico"), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "AppIcon.png"), (PngBytes $images[256]))
"wrote $(Join-Path $OutDir 'AppIcon.ico') ($($ico.Length) bytes, sizes $($sizes -join ','))"

if ($Preview) {
    # Contact sheet on dark and light backgrounds, each size shown at 1x and 4x.
    $sheetW = 900; $sheetH = 330
    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, "NearestNeighbor")
    $dc = $visual.RenderOpen()
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush (Color "#202020")), $null, (New-Object System.Windows.Rect 0, 0, $sheetW, ($sheetH / 2)))
    $dc.DrawRectangle((New-Object System.Windows.Media.SolidColorBrush (Color "#F3F3F3")), $null, (New-Object System.Windows.Rect 0, ($sheetH / 2), $sheetW, ($sheetH / 2)))
    foreach ($row in 0, 1) {
        $x = 10; $y0 = $row * $sheetH / 2 + 10
        foreach ($s in 16, 20, 24, 32, 48) {
            $dc.DrawImage($images[$s], (New-Object System.Windows.Rect $x, $y0, $s, $s)); $x += $s + 8
        }
        foreach ($s in 16, 24, 32) {
            $dc.DrawImage($images[$s], (New-Object System.Windows.Rect $x, $y0, ($s * 4), ($s * 4))); $x += $s * 4 + 10
        }
        $dc.DrawImage($images[128], (New-Object System.Windows.Rect $x, $y0, 128, 128))
    }
    $dc.Close()
    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $sheetW, $sheetH, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($visual)
    [System.IO.File]::WriteAllBytes($Preview, (PngBytes $rtb))
    "preview $Preview"
}
