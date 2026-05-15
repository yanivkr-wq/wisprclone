# preview-icon-options.ps1 — renders 4 candidate app-icon designs as
# 256×256 PNGs into a tmp folder so the user can pick visually before we
# commit + ship via OTA.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root "tmp\icon-previews"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -Path $outDir -ItemType Directory | Out-Null

# Shared brand colors
$top    = [System.Drawing.Color]::FromArgb(0xFF, 0x3D, 0xDF, 0xAE)
$bottom = [System.Drawing.Color]::FromArgb(0xFF, 0x1A, 0x9F, 0x7B)
$white  = [System.Drawing.Color]::White

function New-Canvas {
    param([int]$size)
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Background gradient circle
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $p0   = New-Object System.Drawing.Point 0, 0
    $p1   = New-Object System.Drawing.Point 0, $size
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $p0, $p1, $top, $bottom
    $g.FillEllipse($brush, $rect)
    $brush.Dispose()

    # Top highlight
    $highlight = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 255, 255, 255))
    $g.FillEllipse($highlight, [single]($size * 0.18), [single]($size * 0.10), [single]($size * 0.64), [single]($size * 0.30))
    $highlight.Dispose()

    return @{ Bitmap = $bmp; Graphics = $g }
}

function Draw-EqualizerBars {
    param($g, [int]$size)
    $barWidth = [single]($size * 0.09)
    $pen = New-Object System.Drawing.Pen $white, $barWidth
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    $midY = [single]($size / 2.0)
    $gap = [single]($size * 0.04)
    $heights = @(0.32, 0.54, 0.74, 0.54, 0.32)
    $n = $heights.Count
    $totalW = $n * $barWidth + ($n - 1) * $gap
    $firstX = ($size - $totalW) / 2.0 + $barWidth / 2.0
    for ($i = 0; $i -lt $n; $i++) {
        $x = [single]($firstX + $i * ($barWidth + $gap))
        $halfH = [single]($heights[$i] * $size / 2.0)
        $g.DrawLine($pen, $x, $midY - $halfH, $x, $midY + $halfH)
    }
    $pen.Dispose()
}

function Draw-SpeechBubble {
    param($g, [int]$size)
    # Rounded square bubble with 3 dots inside + small tail
    $bubbleW = [single]($size * 0.62)
    $bubbleH = [single]($size * 0.45)
    $bubbleX = [single](($size - $bubbleW) / 2.0)
    $bubbleY = [single]($size * 0.22)
    $r = [single]($size * 0.10)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($bubbleX,                $bubbleY,                $d, $d, 180, 90)
    $path.AddArc($bubbleX + $bubbleW - $d,$bubbleY,                $d, $d, 270, 90)
    $path.AddArc($bubbleX + $bubbleW - $d,$bubbleY + $bubbleH - $d,$d, $d,   0, 90)
    $path.AddArc($bubbleX,                $bubbleY + $bubbleH - $d,$d, $d,  90, 90)
    $path.CloseFigure()

    # Tail (small triangle)
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

    # Three dots
    $dotR = [single]($size * 0.045)
    $cx = $bubbleX + $bubbleW / 2.0
    $cy = $bubbleY + $bubbleH / 2.0
    $spacing = [single]($size * 0.14)
    $accent = New-Object System.Drawing.SolidBrush $top  # use brand top color for dots
    foreach ($dx in @(-1, 0, 1)) {
        $g.FillEllipse($accent, [single]($cx + $dx * $spacing - $dotR), [single]($cy - $dotR), [single]($dotR * 2), [single]($dotR * 2))
    }
    $fill.Dispose()
    $accent.Dispose()
}

function Draw-MicV2 {
    param($g, [int]$size)
    # Sleeker mic: solid capsule, no cradle - just stem + base. Plus tiny
    # sound waves emanating to the right.
    $white_pen = New-Object System.Drawing.Pen $white, ([single]($size * 0.06))
    $white_pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $white_pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $fill = New-Object System.Drawing.SolidBrush $white

    $midX = [single]($size / 2.0)
    # Capsule
    $capW = [single]($size * 0.30)
    $capH = [single]($size * 0.50)
    $capX = $midX - $capW / 2.0
    $capY = [single]($size * 0.18)
    $r = $capW / 2.0
    $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($capX,            $capY,            $d, $d, 180, 90)
    $path.AddArc($capX + $capW - $d,$capY,            $d, $d, 270, 90)
    $path.AddArc($capX + $capW - $d,$capY + $capH - $d,$d, $d,   0, 90)
    $path.AddArc($capX,            $capY + $capH - $d,$d, $d,  90, 90)
    $path.CloseFigure()
    $g.FillPath($fill, $path)
    $path.Dispose()

    # Short stem + base
    $stemY1 = [single]($size * 0.72)
    $stemY2 = [single]($size * 0.82)
    $g.DrawLine($white_pen, $midX, $stemY1, $midX, $stemY2)
    $baseHalf = [single]($size * 0.12)
    $g.DrawLine($white_pen, $midX - $baseHalf, $stemY2, $midX + $baseHalf, $stemY2)

    $white_pen.Dispose()
    $fill.Dispose()
}

function Draw-LetterW {
    param($g, [int]$size)
    # Big stylised W in white
    $fontSize = [single]($size * 0.62)
    $font = New-Object System.Drawing.Font "Segoe UI", $fontSize, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush $white
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center

    $rect = New-Object System.Drawing.RectangleF 0, ([single]($size * -0.02)), $size, $size
    $g.DrawString("W", $font, $brush, $rect, $fmt)

    $font.Dispose()
    $brush.Dispose()
    $fmt.Dispose()
}

function Save-Option {
    param([string]$name, [scriptblock]$draw)
    $c = New-Canvas -size 256
    & $draw $c.Graphics 256
    $c.Graphics.Dispose()
    $path = Join-Path $outDir "$name.png"
    $c.Bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $c.Bitmap.Dispose()
    Write-Host "  $path" -ForegroundColor Green
}

Save-Option -name "A_equalizer_bars (current v0.2.4)" -draw { param($g,$sz) Draw-EqualizerBars $g $sz }
Save-Option -name "B_speech_bubble_dots"              -draw { param($g,$sz) Draw-SpeechBubble  $g $sz }
Save-Option -name "C_mic_v2_sleek"                    -draw { param($g,$sz) Draw-MicV2          $g $sz }
Save-Option -name "D_letter_W"                        -draw { param($g,$sz) Draw-LetterW        $g $sz }

Write-Host ""
Write-Host "Open this folder to compare:" -ForegroundColor Cyan
Write-Host "  $outDir" -ForegroundColor Cyan
