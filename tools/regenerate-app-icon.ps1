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
$top    = [System.Drawing.Color]::FromArgb(0xFF, 0x3D, 0xDF, 0xAE)
$bottom = [System.Drawing.Color]::FromArgb(0xFF, 0x1A, 0x9F, 0x7B)

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

    # Sound-wave bars in white — five vertical capsules of varying heights.
    $white = [System.Drawing.Color]::White
    $barWidth = [single]($size * 0.09)
    $pen   = New-Object System.Drawing.Pen $white, $barWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $midY = [single]($size / 2.0)
    $gap  = [single]($size * 0.04)
    $heights = @(0.32, 0.54, 0.74, 0.54, 0.32)

    $n = $heights.Count
    $totalWidth = $n * $barWidth + ($n - 1) * $gap
    $firstX = ($size - $totalWidth) / 2.0 + $barWidth / 2.0

    for ($i = 0; $i -lt $n; $i++) {
        $x = [single]($firstX + $i * ($barWidth + $gap))
        $halfH = [single]($heights[$i] * $size / 2.0)
        $g.DrawLine($pen, $x, $midY - $halfH, $x, $midY + $halfH)
    }

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
