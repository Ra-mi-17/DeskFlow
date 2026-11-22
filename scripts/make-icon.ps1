# Generates assets/icon.png (256x256) for DeskFlow.
Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::Transparent)

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $p.AddArc($x, $y, $r * 2, $r * 2, 180, 90)
  $p.AddArc($x + $w - $r * 2, $y, $r * 2, $r * 2, 270, 90)
  $p.AddArc($x + $w - $r * 2, $y + $h - $r * 2, $r * 2, $r * 2, 0, 90)
  $p.AddArc($x, $y + $h - $r * 2, $r * 2, $r * 2, 90, 90)
  $p.CloseFigure()
  return $p
}

# Background: indigo -> cyan gradient rounded square
$bg = RoundRect 8 8 240 240 56
$rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 108, 92, 255)), ([System.Drawing.Color]::FromArgb(255, 40, 205, 220)), 45
$g.FillPath($grad, $bg)

# Soft highlight
$hl = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(36, 255, 255, 255))
$g.FillEllipse($hl, -60, -110, 300, 240)

# Loop arc
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 255, 255, 255)), 16
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'
$g.DrawArc($pen, 52, 52, 152, 152, 200, 300)
# Arrow head at end of arc
$arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
$arrow.AddPolygon(@(
  (New-Object System.Drawing.PointF 66, 60),
  (New-Object System.Drawing.PointF 106, 70),
  (New-Object System.Drawing.PointF 74, 100)
))
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
$g.FillPath($white, $arrow)

# Play triangle
$tri = New-Object System.Drawing.Drawing2D.GraphicsPath
$tri.AddPolygon(@(
  (New-Object System.Drawing.PointF 108, 92),
  (New-Object System.Drawing.PointF 172, 128),
  (New-Object System.Drawing.PointF 108, 164)
))
$g.FillPath([System.Drawing.Brushes]::White, $tri)

$assets = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$bmp.Save((Join-Path $assets 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()

# Multi-size .ico (PNG-compressed entries)
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
  $b = New-Object System.Drawing.Bitmap $s, $s
  $gg = [System.Drawing.Graphics]::FromImage($b)
  $gg.InterpolationMode = 'HighQualityBicubic'; $gg.SmoothingMode = 'AntiAlias'; $gg.PixelOffsetMode = 'HighQuality'
  $gg.DrawImage($bmp, 0, 0, $s, $s); $gg.Dispose()
  $ms = New-Object System.IO.MemoryStream
  $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
  , @($s, $ms.ToArray())
}
$fs = [System.IO.File]::Create((Join-Path $assets 'icon.ico'))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
  $s = $f[0]; $data = $f[1]
  $dim = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
  $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Close()
$bmp.Dispose()
Write-Output "saved icon.png / icon.ico to $assets"
