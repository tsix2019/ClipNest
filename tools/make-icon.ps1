# Generates src/ClipNest.App/Assets/ClipNest.ico (multi-size, PNG-compressed) and Logo.png.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'src/ClipNest.App/Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Squircle background with a blue -> violet gradient
    $pad = [Math]::Max(0.5, $s * 0.04)
    $bg = New-RoundedPath $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.23)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 64, 140, 255)), ([System.Drawing.Color]::FromArgb(255, 139, 92, 246)), 45.0
    $g.FillPath($grad, $bg)

    # Back card (stack effect)
    $back = New-RoundedPath ($s * 0.30) ($s * 0.20) ($s * 0.46) ($s * 0.50) ($s * 0.07)
    $backBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(110, 255, 255, 255))
    $g.FillPath($backBrush, $back)

    # Front card
    $front = New-RoundedPath ($s * 0.22) ($s * 0.30) ($s * 0.46) ($s * 0.50) ($s * 0.07)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillPath($white, $front)

    # Colored header strip on the front card (like a ClipNest card)
    if ($s -ge 20) {
        $g.SetClip($front)
        $hdr = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 159, 10))
        $g.FillRectangle($hdr, $s * 0.22, $s * 0.30, $s * 0.46, $s * 0.12)
        $g.ResetClip()
    }

    # Text lines
    if ($s -ge 32) {
        $line = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 120, 120, 140))
        foreach ($i in 0..2) {
            $w = if ($i -eq 2) { 0.20 } else { 0.32 }
            $l = New-RoundedPath ($s * 0.29) ($s * (0.50 + $i * 0.085)) ($s * $w) ($s * 0.035) ($s * 0.017)
            $g.FillPath($line, $l)
        }
    }

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $bmp.Save((Join-Path $assets 'Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# ICO container: ICONDIR + ICONDIRENTRY[n] + PNG payloads
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'ClipNest.ico'), $out.ToArray())
Write-Host "Wrote $(Join-Path $assets 'ClipNest.ico')"
