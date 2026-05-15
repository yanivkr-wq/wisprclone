# preview-bubble-colors.ps1 — renders the speech-bubble icon in 6 color
# variants so the user can pick which gradient to ship.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root "tmp\icon-previews\bubble-colors"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -Path $outDir -ItemType Directory -Force | Out-Null

# 6 color variants. Each tuple = (display name, top color, bottom color).
$variants = @(
    @{ Name = "1_teal_green";   Top = (0x3D,0xDF,0xAE); Bottom = (0x1A,0x9F,0x7B) }
    @{ Name = "2_royal_blue";   Top = (0x5B,0x8F,0xF9); Bottom = (0x2B,0x6C,0xB0) }
    @{ Name = "3_purple";       Top = (0xB0,0x84,0xEE); Bottom = (0x6B,0x46,0xC1) }
    @{ Name = "4_coral_pink";   Top = (0xFF,0x8F,0xA3); Bottom = (0xE1,0x1D,0x74) }
    @{ Name = "5_warm_amber";   Top = (0xFC,0xD3,0x4D); Bottom = (0xD9,0x77,0x06) }
    @{ Name = "6_slate_charcoal"; Top = (0x6B,0x72,0x80); Bottom = (0x1F,0x29,0x37) }
)

function New-Canvas {
    param([int]$size, $top, $bottom)
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $p0   = New-Object System.Drawing.Point 0, 0
    $p1   = New-Object System.Drawing.Point 0, $size
    $tc   = [System.Drawing.Color]::FromArgb(0xFF, $top[0],    $top[1],    $top[2])
    $bc   = [System.Drawing.Color]::FromArgb(0xFF, $bottom[0], $bottom[1], $bottom[2])
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p0, $p1, $tc, $bc
    $g.FillEllipse($brush, $rect)
    $brush.Dispose()

    $highlight = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 255, 255, 255))
    $g.FillEllipse($highlight, [single]($size * 0.18), [single]($size * 0.10), [single]($size * 0.64), [single]($size * 0.30))
    $highlight.Dispose()

    return @{ Bitmap = $bmp; Graphics = $g; TopColor = $tc }
}

function Draw-SpeechBubble {
    param($g, [int]$size, $accentColor)

    $white = [System.Drawing.Color]::White
    $bubbleW = [single]($size * 0.62)
    $bubbleH = [single]($size * 0.45)
    $bubbleX = [single](($size - $bubbleW) / 2.0)
    $bubbleY = [single]($size * 0.22)
    $r = [single]($size * 0.10)

    # Rounded rectangle path
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($bubbleX,                $bubbleY,                $d, $d, 180, 90)
    $path.AddArc($bubbleX + $bubbleW - $d,$bubbleY,                $d, $d, 270, 90)
    $path.AddArc($bubbleX + $bubbleW - $d,$bubbleY + $bubbleH - $d,$d, $d,   0, 90)
    $path.AddArc($bubbleX,                $bubbleY + $bubbleH - $d,$d, $d,  90, 90)
    $path.CloseFigure()

    # Tail
    $tailX1 = [single]($bubbleX + $bubbleW * 0.30)
    $tailX2 = [single]($bubbleX + $bubbleW * 0.55)
    $tailY  = [single]($bubbleY + $bubbleH)
    $tailTip = New-Object System.Drawing.PointF ([single]($bubbleX + $bubbleW * 0.35)), ([single]($tailY + $size * 0.13))
    $p1 = New-Object System.Drawing.PointF $tailX1, $tailY
    $p2 = New-Object System.Drawing.PointF $tailX2, $tailY
    $tailPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tailPath.AddPolygon(@($p1, $p2, $tailTip))

    $fill = New-Object System.Drawing.SolidBrush $white
    $g.FillPath($fill, $path)
    $g.FillPath($fill, $tailPath)
    $path.Dispose()
    $tailPath.Dispose()

    # Three dots in the brand top color
    $dotR = [single]($size * 0.045)
    $cx = $bubbleX + $bubbleW / 2.0
    $cy = $bubbleY + $bubbleH / 2.0
    $spacing = [single]($size * 0.14)
    $accent = New-Object System.Drawing.SolidBrush $accentColor
    foreach ($dx in @(-1, 0, 1)) {
        $g.FillEllipse($accent, [single]($cx + $dx * $spacing - $dotR), [single]($cy - $dotR), [single]($dotR * 2), [single]($dotR * 2))
    }
    $fill.Dispose()
    $accent.Dispose()
}

foreach ($v in $variants) {
    $c = New-Canvas -size 256 -top $v.Top -bottom $v.Bottom
    Draw-SpeechBubble -g $c.Graphics -size 256 -accentColor $c.TopColor
    $c.Graphics.Dispose()
    $path = Join-Path $outDir ("bubble_{0}.png" -f $v.Name)
    $c.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.Bitmap.Dispose()
    Write-Host "  $path" -ForegroundColor Green
}

Write-Host ""
Write-Host "Open this folder to compare:" -ForegroundColor Cyan
Write-Host "  $outDir" -ForegroundColor Cyan
