<#
.SYNOPSIS
  Draws the Memento app icon (the wordmark's ring with an accent dot) and writes memento.ico.

.DESCRIPTION
  The ring and dot follow the header wordmark in design/DESIGN.md section 3: a ring in `text`
  (#1D1C1A) with a 2/22 stroke and a centred dot in `accent` (#C2410C) at 10/22 of the ring.
  They sit on a round plate in the light ground colour (#EFEDE8) so the dark ring stays visible
  on dark taskbars. Every size is drawn natively (no downscaling) and stored as PNG inside the ICO.

  The generated memento.ico is committed; run this script again only when the mark changes:
    powershell -NoProfile -ExecutionPolicy Bypass -File build/icons/New-MementoIcon.ps1
#>
[CmdletBinding()]
param(
  [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
if (-not $OutputPath) {
  $OutputPath = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'memento.ico'
}
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$plate = [System.Drawing.ColorTranslator]::FromHtml('#EFEDE8')
$ink = [System.Drawing.ColorTranslator]::FromHtml('#1D1C1A')
$accent = [System.Drawing.ColorTranslator]::FromHtml('#C2410C')

function New-IconFrame([int] $size) {
  $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bitmap)
  try {
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $center = $size / 2.0
    $plateBrush = New-Object System.Drawing.SolidBrush($plate)
    $g.FillEllipse($plateBrush, 0.0, 0.0, [single]$size, [single]$size)
    $plateBrush.Dispose()

    $ringOuter = $size * 0.70
    $stroke = [Math]::Max(1.5, $ringOuter * 2.0 / 22.0)
    $ringDiameter = $ringOuter - $stroke
    $pen = New-Object System.Drawing.Pen($ink, [single]$stroke)
    $g.DrawEllipse($pen, [single]($center - $ringDiameter / 2), [single]($center - $ringDiameter / 2), [single]$ringDiameter, [single]$ringDiameter)
    $pen.Dispose()

    $dot = $ringOuter * 10.0 / 22.0
    $dotBrush = New-Object System.Drawing.SolidBrush($accent)
    $g.FillEllipse($dotBrush, [single]($center - $dot / 2), [single]($center - $dot / 2), [single]$dot, [single]$dot)
    $dotBrush.Dispose()
  }
  finally {
    $g.Dispose()
  }

  $stream = New-Object System.IO.MemoryStream
  $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
  $bitmap.Dispose()
  return , $stream.ToArray()
}

$frames = foreach ($size in $sizes) { , (New-IconFrame $size) }

$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)
$writer.Write([UInt16]0)              # reserved
$writer.Write([UInt16]1)              # type: icon
$writer.Write([UInt16]$sizes.Count)   # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $size = $sizes[$i]
  $data = $frames[$i]
  $dimension = if ($size -ge 256) { 0 } else { $size }
  $writer.Write([byte]$dimension)     # width (0 = 256)
  $writer.Write([byte]$dimension)     # height
  $writer.Write([byte]0)              # palette size
  $writer.Write([byte]0)              # reserved
  $writer.Write([UInt16]1)            # colour planes
  $writer.Write([UInt16]32)           # bits per pixel
  $writer.Write([UInt32]$data.Length)
  $writer.Write([UInt32]$offset)
  $offset += $data.Length
}
foreach ($data in $frames) { $writer.Write($data) }
$writer.Flush()

$directory = Split-Path -Parent $OutputPath
if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
[System.IO.File]::WriteAllBytes($OutputPath, $output.ToArray())
$writer.Dispose()
Write-Host "Wrote $OutputPath ($($sizes -join ', ') px)"
