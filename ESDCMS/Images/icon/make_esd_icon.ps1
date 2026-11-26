# ESD susceptibility symbol (the app's original icon) redrawn as vectors, without the "ESD" text.
# One set of coordinates (256 x 256 units) -> esd.svg, and the same shapes drawn by WPF -> preview PNG + multi-size .ico.
# Outside the yellow disc is transparent.
# Usage: powershell.exe -STA -File make_esd_icon.ps1 <outDir>
param([string]$OutDir)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$YELLOW = '#FFDD00'
$TRI    = 'M128,38 L209.4,179 L46.6,179 Z'                 # black triangle (drawn with a round-joined 8-unit stroke)
$CLIP   = 'M128,50 L200,174 L56,174 Z'                     # inner area for the hand and the bar
$HAND   = 'M190,96 L156,120 C146,124 132,126 122,128 L104,132 C98,134 98,141 104,142 L120,140 L98,156 C93,160 96,167 102,165 L122,152 L108,166 C104,171 109,176 114,173 L132,160 L124,172 C122,177 128,180 132,176 L146,163 L142,172 C141,177 147,178 150,174 L166,150 L198,122 Z'
$SEPS   = @()     # lines between the curled fingers
$BAR    = @{ X1 = 78; Y1 = 84; X2 = 206; Y2 = 160 }       # the slash: black 22 wide under yellow 11 wide

# ---------------------------------------------------------------- SVG
$svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">
  <!-- ESD susceptibility symbol, redrawn for ESDCMS (ESD Compliance Monitoring System). Transparent outside the disc. -->
  <defs>
    <clipPath id="inner"><path d="$CLIP"/></clipPath>
  </defs>
  <circle cx="128" cy="128" r="124" fill="$YELLOW"/>
  <circle cx="128" cy="128" r="109" fill="none" stroke="#000" stroke-width="9"/>
  <path d="$TRI" fill="#000" stroke="#000" stroke-width="8" stroke-linejoin="round"/>
  <g clip-path="url(#inner)">
    <path d="$HAND" fill="$YELLOW"/>
$(($SEPS | ForEach-Object { "    <path d=""$_"" fill=""none"" stroke=""#000"" stroke-width=""3.5"" stroke-linecap=""round""/>" }) -join "`n")
    <line x1="$($BAR.X1)" y1="$($BAR.Y1)" x2="$($BAR.X2)" y2="$($BAR.Y2)" stroke="#000" stroke-width="22"/>
    <line x1="$($BAR.X1)" y1="$($BAR.Y1)" x2="$($BAR.X2)" y2="$($BAR.Y2)" stroke="$YELLOW" stroke-width="11"/>
  </g>
</svg>
"@
[IO.File]::WriteAllText((Join-Path $OutDir 'esd.svg'), $svg, (New-Object System.Text.UTF8Encoding($false)))

# ---------------------------------------------------------------- WPF drawing of the same shapes
$yb = New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($YELLOW)); $yb.Freeze()
$black = [Windows.Media.Brushes]::Black
function New-IconBitmap([int]$size) {
  $dv = New-Object Windows.Media.DrawingVisual; $dc = $dv.RenderOpen()
  $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / 256.0), ($size / 256.0)))
  $dc.DrawEllipse($yb, $null, [Windows.Point]::new(128, 128), 124, 124)
  $dc.DrawEllipse($null, (New-Object Windows.Media.Pen $black, 9), [Windows.Point]::new(128, 128), 109, 109)
  $tp = New-Object Windows.Media.Pen $black, 8; $tp.LineJoin = 'Round'
  $dc.DrawGeometry($black, $tp, [Windows.Media.Geometry]::Parse($TRI))
  $dc.PushClip([Windows.Media.Geometry]::Parse($CLIP))
  $dc.DrawGeometry($yb, $null, [Windows.Media.Geometry]::Parse($HAND))
  $sp = New-Object Windows.Media.Pen $black, 3.5; $sp.StartLineCap = 'Round'; $sp.EndLineCap = 'Round'
  foreach ($g in $SEPS) { $dc.DrawGeometry($null, $sp, [Windows.Media.Geometry]::Parse($g)) }
  $a = [Windows.Point]::new($BAR.X1, $BAR.Y1); $b = [Windows.Point]::new($BAR.X2, $BAR.Y2)
  $dc.DrawLine((New-Object Windows.Media.Pen $black, 22), $a, $b)
  $dc.DrawLine((New-Object Windows.Media.Pen $yb, 11), $a, $b)
  $dc.Pop(); $dc.Pop(); $dc.Close()
  $rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($dv); $rtb
}
function Get-Png($bmp) {
  $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = New-Object IO.MemoryStream; $enc.Save($ms); return , $ms.ToArray()
}
function Get-Dib($bmp, [int]$size) {          # 32-bit BGRA DIB (straight alpha, bottom-up) + empty AND mask
  $conv = New-Object Windows.Media.Imaging.FormatConvertedBitmap $bmp, ([Windows.Media.PixelFormats]::Bgra32), $null, 0
  $stride = $size * 4; $px = New-Object byte[] ($stride * $size); $conv.CopyPixels($px, $stride, 0)
  $ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
  $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
  $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
  for ($y = $size - 1; $y -ge 0; $y--) { $w.Write($px, $y * $stride, $stride) }
  $w.Write((New-Object byte[] ([int]([math]::Ceiling($size / 32.0) * 4) * $size)))
  $w.Flush(); return , $ms.ToArray()
}
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($s in $sizes) { $bm = New-IconBitmap $s; if ($s -ge 256) { $images.Add((Get-Png $bm)) } else { $images.Add((Get-Dib $bm $s)) } }
$path = Join-Path $OutDir 'esd.ico'
$fs = [IO.File]::Create($path); $w = New-Object IO.BinaryWriter $fs
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $len = $images[$i].Length
  $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$len); $w.Write([int]$offset); $offset += $len
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
"esd.ico: $($sizes.Count) sizes, $((Get-Item $path).Length) bytes"

# preview: 256 / 64 / 48 / 32 / 24 / 16 on light and dark
$dv = New-Object Windows.Media.DrawingVisual; $dc = $dv.RenderOpen()
$dc.DrawRectangle([Windows.Media.Brushes]::WhiteSmoke, $null, [Windows.Rect]::new(0, 0, 700, 300))
$dc.DrawRectangle((New-Object Windows.Media.SolidColorBrush ([Windows.Media.Color]::FromRgb(0x20, 0x20, 0x20))), $null, [Windows.Rect]::new(0, 300, 700, 300))
foreach ($row in 0, 1) {
  $x = 22; $y = $row * 300 + 22
  $dc.DrawImage((New-IconBitmap 256), [Windows.Rect]::new($x, $y, 256, 256)); $x += 280
  foreach ($s in 64, 48, 32, 24, 16) { $dc.DrawImage((New-IconBitmap $s), [Windows.Rect]::new($x, $y + 96, $s, $s)); $x += $s + 26 }
}
$dc.Close()
$rtb = New-Object Windows.Media.Imaging.RenderTargetBitmap 700, 600, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32); $rtb.Render($dv)
[IO.File]::WriteAllBytes((Join-Path $OutDir 'esd_preview.png'), (Get-Png $rtb))
"preview written"
