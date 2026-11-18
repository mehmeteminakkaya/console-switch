# Draws the app icon (a monitor with a two-way arrow) and writes assets\icon.ico with PNG-encoded sizes.
Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 128.0
    # rounded background
    $r = 28 * $k; $x = 4 * $k; $w = 120 * $k
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $x, 2*$r, 2*$r, 180, 90); $path.AddArc($x+$w-2*$r, $x, 2*$r, 2*$r, 270, 90)
    $path.AddArc($x+$w-2*$r, $x+$w-2*$r, 2*$r, 2*$r, 0, 90); $path.AddArc($x, $x+$w-2*$r, 2*$r, 2*$r, 90, 90); $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0,0), (New-Object System.Drawing.PointF $s,$s), ([System.Drawing.Color]::FromArgb(139,123,234)), ([System.Drawing.Color]::FromArgb(58,42,150))
    $g.FillPath($brush, $path)
    # monitor
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 8 * $k))
    $pen.LineJoin = 'Round'
    $g.DrawRectangle($pen, [float](26*$k), [float](30*$k), [float](76*$k), [float](50*$k))
    $g.DrawLine($pen, [float](64*$k), [float](80*$k), [float](64*$k), [float](94*$k))
    $g.DrawLine($pen, [float](46*$k), [float](96*$k), [float](82*$k), [float](96*$k))
    # two-way arrow inside the screen
    $ap = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,217,102)), ([Math]::Max(1.5, 7 * $k))
    $ap.StartCap = 'Round'; $ap.EndCap = 'Round'
    $g.DrawLine($ap, [float](42*$k), [float](55*$k), [float](86*$k), [float](55*$k))
    $g.DrawLine($ap, [float](42*$k), [float](55*$k), [float](52*$k), [float](45*$k))
    $g.DrawLine($ap, [float](42*$k), [float](55*$k), [float](52*$k), [float](65*$k))
    $g.DrawLine($ap, [float](86*$k), [float](55*$k), [float](76*$k), [float](45*$k))
    $g.DrawLine($ap, [float](86*$k), [float](55*$k), [float](76*$k), [float](65*$k))
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $PSScriptRoot 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $pngs += ,@($s, $ms.ToArray())
    $g.Dispose(); $bmp.Dispose()
}
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $s = $p[0]; $data = $p[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$data.Length); $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($p in $pngs) { $bw.Write([byte[]]$p[1]) }
[System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon.ico'), $out.ToArray())
Write-Output "wrote icon.ico and icon.png"
