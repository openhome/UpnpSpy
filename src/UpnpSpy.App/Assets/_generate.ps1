# Generate MSIX visual-asset PNGs from the Linn brand source logos.
# Source PNGs (already in this directory):
#   _src-horizontal.png  232 x 64   black-on-transparent horizontal wordmark
#   _src-vertical.png    132 x 180  white-on-transparent stacked wordmark
#
# Square tiles use a centered, padded version of the horizontal logo on a
# black canvas (matches Linn's brand background). Wide and splash tiles use
# the horizontal logo on the same black canvas. The vertical (white-on-
# transparent) source is the one usable on the black canvas; the horizontal
# source is black-on-transparent and would disappear.

Add-Type -AssemblyName System.Drawing

$assetsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = [System.Drawing.Image]::FromFile((Join-Path $assetsDir '_src-vertical.png'))

function New-Logo {
    param(
        [int]$Width,
        [int]$Height,
        [string]$FileName,
        [double]$LogoFraction = 0.7
    )

    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Black background (matches Linn's primary brand colour).
    $g.Clear([System.Drawing.Color]::Black)

    # Scale source to fill LogoFraction of the smaller canvas dimension,
    # preserving aspect ratio.
    $maxDim = [Math]::Min($Width, $Height) * $LogoFraction
    $srcRatio = $src.Width / $src.Height
    if ($srcRatio -ge 1) {
        $drawW = $maxDim
        $drawH = $maxDim / $srcRatio
    } else {
        $drawH = $maxDim
        $drawW = $maxDim * $srcRatio
    }
    $x = ($Width - $drawW) / 2
    $y = ($Height - $drawH) / 2

    $g.DrawImage($src, [System.Drawing.RectangleF]::new($x, $y, $drawW, $drawH))
    $g.Dispose()

    $outPath = Join-Path $assetsDir $FileName
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Output "wrote $outPath ($Width x $Height)"
}

New-Logo -Width 44  -Height 44  -FileName 'Square44x44Logo.png'   -LogoFraction 0.85
New-Logo -Width 150 -Height 150 -FileName 'Square150x150Logo.png' -LogoFraction 0.70
New-Logo -Width 310 -Height 150 -FileName 'Wide310x150Logo.png'   -LogoFraction 0.70
New-Logo -Width 50  -Height 50  -FileName 'StoreLogo.png'         -LogoFraction 0.80
New-Logo -Width 620 -Height 300 -FileName 'SplashScreen.png'      -LogoFraction 0.55

$src.Dispose()
