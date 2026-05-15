# regenerate-app-icon.ps1
#
# Regenerates src/WisprClone.App/Resources/app.ico to match the design and
# colours currently coded in IconFactory.cs. Pure PowerShell + System.Drawing.
# Run once whenever you change the icon design or colours.

param(
    [string] $OutPath = "src\WisprClone.App\Resources\app.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$abs  = Join-Path $root $OutPath

# ---- Brand colours (mirror IconFactory.cs) ----
$top    = [System.Drawing.Color]::FromArgb(0xFF, 0xA0, 0x4D, 0xEA)
$bottom = [System.Drawing.Color]::FromArgb(0xFF, 0x6A, 0x2D, 0xA8)

function New-MasterBitmap {
    param([int] $size)

    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)

    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # background gradient circle
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $p0   = New-Object System.Drawing.Point 0, 0
    $p1   = New-Object System.Drawing.Point 0, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p0, $p1, $top, $bottom
    $g.FillEllipse($brush, $rect)
    $brush.Dispose()

    # top highlight for depth
    $highlight = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 255, 255, 255))
    $g.FillEllipse($highlight, [single]($size * 0.18), [single]($size * 0.10), [single]($size * 0.64), [single]($size * 0.30))
    $highlight.Dispose()

    # microphone in white
    $white = [System.Drawing.Color]::White
    $fill  = New-Object System.Drawing.SolidBrush $white
    $pen   = New-Object System.Drawing.Pen $white, ([single]($size * 0.07))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $midX = [single]($size / 2.0)

    # Capsule (rounded rectangle)
    $capW = [single]($size * 0.28)
    $capH = [single]($size * 0.46)
    $capX = $midX - $capW / 2.0
    $capY = [single]($size * 0.16)
    $r    = $capW / 2.0
    $d    = $r * 2.0

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($capX,              $capY,              $d, $d, 180, 90)
    $path.AddArc($capX + $capW - $d, $capY,              $d, $d, 270, 90)
    $path.AddArc($capX + $capW - $d, $capY + $capH - $d, $d, $d,   0, 90)
    $path.AddArc($capX,              $capY + $capH - $d, $d, $d,  90, 90)
    $path.CloseFigure()
    $g.FillPath($fill, $path)
    $path.Dispose()

    # U-shape cradle
    $cradleW = [single]($size * 0.52)
    $cradleH = [single]($size * 0.22)
    $cradleX = $midX - $cradleW / 2.0
    $cradleY = [single]($size * 0.52)
    $g.DrawArc($pen, $cradleX, $cradleY, $cradleW, $cradleH, 0, 180)

    # Stem
    $stemTop    = $cradleY + $cradleH / 2.0
    $stemBottom = [single]($size * 0.84)
    $g.DrawLine($pen, $midX, $stemTop, $midX, $stemBottom)

    # Base
    $baseHalf = [single]($size * 0.16)
    $g.DrawLine($pen, $midX - $baseHalf, $stemBottom, $midX + $baseHalf, $stemBottom)

    $fill.Dispose()
    $pen.Dispose()
    $g.Dispose()
    return $bmp
}

function Get-PngBytes {
    param([System.Drawing.Bitmap] $source, [int] $size)

    $resized = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($resized)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $resized.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $resized.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    # Wrap in a no-op array so PowerShell's pipeline can't flatten the byte array.
    return ,$bytes
}

# Render master then downsample to each target size.
$master = New-MasterBitmap -size 256
$sizes  = @(16, 32, 48, 64, 256)
$pngs   = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($sz in $sizes) {
    $bytes = Get-PngBytes -source $master -size $sz
    Write-Host ("  rendered {0}x{0} = {1} bytes" -f $sz, $bytes.Length)
    [void] $pngs.Add($bytes)
}
$master.Dispose()

# ---- Build the .ico in a single MemoryStream ----
$outDir = Split-Path $abs -Parent
if (-not (Test-Path $outDir)) { New-Item -Path $outDir -ItemType Directory -Force | Out-Null }

$out = New-Object System.IO.MemoryStream
$w   = New-Object System.IO.BinaryWriter $out

# ICONDIR
$w.Write([UInt16] 0)
$w.Write([UInt16] 1)
$w.Write([UInt16] $sizes.Count)

# ICONDIRENTRYs
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $bytes = $pngs[$i]
    if ($sz -ge 256) { $w.Write([Byte] 0); $w.Write([Byte] 0) }
    else             { $w.Write([Byte] $sz); $w.Write([Byte] $sz) }
    $w.Write([Byte]  0)               # palette count
    $w.Write([Byte]  0)               # reserved
    $w.Write([UInt16] 1)              # color planes
    $w.Write([UInt16] 32)             # bpp
    $w.Write([UInt32] $bytes.Length)  # image bytes
    $w.Write([UInt32] $offset)        # offset
    $offset += $bytes.Length
}

# Image payloads
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $bytes = $pngs[$i]
    $w.Write($bytes, 0, $bytes.Length)   # explicit overload — no enumeration weirdness
}

$w.Flush()
$icoBytes = $out.ToArray()
$w.Dispose()
$out.Dispose()

[System.IO.File]::WriteAllBytes($abs, $icoBytes)

$kb = [math]::Round($icoBytes.Length / 1024.0, 1)
Write-Host ""
Write-Host "Generated $abs ($kb KB, sizes: $($sizes -join ','))" -ForegroundColor Green
