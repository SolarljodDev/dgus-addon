# Генерирует dgusplus.ico (16…256 px, PNG внутри ICO): синий скруглённый квадрат с «D+».
param([string]$Out = (Join-Path (Split-Path $PSScriptRoot) 'dgusplus.ico'))
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $r = [Math]::Max(2, [int]($s * 0.22)); $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $s), ([System.Drawing.Color]::FromArgb(61, 126, 255)), ([System.Drawing.Color]::FromArgb(36, 88, 214))
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float]($s * 0.46)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString('D+', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ($s * 0.02), $s, $s), $sf)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($Out); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close(); "OK -> $Out"
