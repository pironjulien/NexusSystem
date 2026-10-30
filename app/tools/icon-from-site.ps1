# The app's icons come from the Nexus family kit (NexusCommunication/icones, the icons of julienpiron.fr), copied as
# they are, never redrawn. Rule of the kit: 44 px and more, the mark (the pictogram in the medallion of
# julienpiron.fr); under 44 px, the round (the same medallion cut round, transparent corners; the pictogram alone is
# never shown). NexusSystem: the cyan chip with the pi.
# - Assets/nexus<product>-round.png, 96 px (navigation, title bar) and Assets/nexus<product>-mark.png (cards, page
#   headers), for NexusSystem and each product;
# - the package's logos at every scale Windows asks for (scale-100, 125, 150, 200, 400): Square150x150Logo, StoreLogo
#   and Wide310x150Logo from the mark; Square44x44Logo (the small-icon slot) from the round, at every scale and every
#   targetsize (taskbar, Start, Alt+Tab, Explorer), plated, unplated and light-unplated;
# - Assets/nexus.ico for Windows: the round under 44 px, the mark from 48 px (PNG entries).
# -Sheet also writes the small sizes enlarged 8 times, on light and dark, to check how readable they are.
#   pwsh -File tools/icon-from-site.ps1 [-Kit <kit folder>] [-Sheet <sheet.png>]
param(
    [string]$Kit = (Join-Path $PSScriptRoot '..\..\..\NexusCommunication\icones'),
    [string]$Sheet
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $PSScriptRoot '..\src\Nexus.App\Assets'
$names = 'system', 'windows', 'android', 'browser', 'keypad', 'relay', 'agymail'
$scales = 100, 125, 150, 200, 400
$targetSizes = 16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256

function Get-KitImage([string]$Name, [string]$Kind, [int]$Size) {
    # The kit's export at the nearest size at or above, reduced if needed (high quality bicubic, alpha kept).
    $folder = Join-Path $Kit "png\nexus$Name-$Kind"
    $sizes = @(Get-ChildItem -LiteralPath $folder -Filter '*.png' | ForEach-Object { [int]($_.BaseName -split '-')[-1] } | Sort-Object)
    if (-not $sizes.Count) { throw "No PNG in $folder" }
    $best = @($sizes | Where-Object { $_ -ge $Size } | Select-Object -First 1)
    $best = if ($best.Count) { $best[0] } else { $sizes[-1] }
    $source = [Drawing.Image]::FromFile((Join-Path $folder "nexus$Name-$Kind-$best.png"))
    $image = New-Object Drawing.Bitmap $Size, $Size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($image)
    try {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $attributes = New-Object Drawing.Imaging.ImageAttributes
        $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)  # no grey fringe at the edges
        $graphics.DrawImage($source, (New-Object Drawing.Rectangle 0, 0, $Size, $Size), 0, 0, $source.Width, $source.Height,
            [Drawing.GraphicsUnit]::Pixel, $attributes)
    } finally { $graphics.Dispose(); $source.Dispose() }
    return $image
}

function Save-Png([Drawing.Image]$Image, [string]$File) {
    $Image.Save((Join-Path $assets $File), [Drawing.Imaging.ImageFormat]::Png)
    $Image.Dispose()
}

foreach ($name in $names) {
    Remove-Item -LiteralPath (Join-Path $assets "nexus$name-icon.svg") -ErrorAction SilentlyContinue  # pictogram alone, no longer shown
    Save-Png (Get-KitImage $name 'round' 96) "nexus$name-round.png"
    Save-Png (Get-KitImage $name 'mark' 256) "nexus$name-mark.png"
}
# One generation at a time: an unqualified file would clash with its scale-100 variant.
Get-ChildItem -LiteralPath $assets -File | Where-Object { $_.Name -match '^(Square\d+x\d+Logo|StoreLogo|Wide310x150Logo).*\.png$' } | Remove-Item
foreach ($scale in $scales) {
    $k = $scale / 100
    Save-Png (Get-KitImage 'system' 'mark' ([Math]::Round(150 * $k, [MidpointRounding]::ToEven))) "Square150x150Logo.scale-$scale.png"
    Save-Png (Get-KitImage 'system' 'mark' ([Math]::Round(50 * $k, [MidpointRounding]::ToEven))) "StoreLogo.scale-$scale.png"
    Save-Png (Get-KitImage 'system' 'round' ([Math]::Round(44 * $k, [MidpointRounding]::ToEven))) "Square44x44Logo.scale-$scale.png"
    $wide = New-Object Drawing.Bitmap ([int][Math]::Round(310 * $k, [MidpointRounding]::ToEven)), ([int][Math]::Round(150 * $k, [MidpointRounding]::ToEven)), ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $mark = Get-KitImage 'system' 'mark' ([Math]::Round(150 * $k, [MidpointRounding]::ToEven))
    $graphics = [Drawing.Graphics]::FromImage($wide)
    $graphics.DrawImageUnscaled($mark, [int][Math]::Round(80 * $k, [MidpointRounding]::ToEven), 0)
    $graphics.Dispose(); $mark.Dispose()
    Save-Png $wide "Wide310x150Logo.scale-$scale.png"
}
foreach ($size in $targetSizes) {
    foreach ($form in '', '_altform-unplated', '_altform-lightunplated') {
        Save-Png (Get-KitImage 'system' 'round' $size) "Square44x44Logo.targetsize-$size$form.png"
    }
}

# nexus.ico: PNG entries (Windows Vista and later), the round under 44 px, the mark from 48 px.
$entries = @(16, 20, 24, 32, 40 | ForEach-Object { , @($_, 'round') }) + @(48, 64, 256 | ForEach-Object { , @($_, 'mark') })
$blobs = foreach ($entry in $entries) {
    $image = Get-KitImage 'system' $entry[1] $entry[0]
    $stream = New-Object IO.MemoryStream
    $image.Save($stream, [Drawing.Imaging.ImageFormat]::Png); $image.Dispose()
    , @($entry[0], $stream.ToArray())
}
$ico = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$blobs.Count)
$offset = 6 + 16 * $blobs.Count
foreach ($blob in $blobs) {
    $side = if ($blob[0] -ge 256) { 0 } else { $blob[0] }
    $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$blob[1].Length); $writer.Write([uint32]$offset)
    $offset += $blob[1].Length
}
foreach ($blob in $blobs) { $writer.Write([byte[]]$blob[1]) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'nexus.ico'), $ico.ToArray())
$writer.Dispose()

if ($Sheet) {
    # 16, 24, 32 and 48 px as Windows shows them, then enlarged 8 times (each pixel a square), on light and dark.
    $small = 16, 24, 32, 48
    $cell = 48 * 8 + 24
    $page = New-Object Drawing.Bitmap ($cell * $small.Count), ($cell * 2), ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($page)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
    $backgrounds = [Drawing.Color]::FromArgb(243, 243, 243), [Drawing.Color]::FromArgb(32, 32, 32)
    for ($row = 0; $row -lt 2; $row++) {
        for ($col = 0; $col -lt $small.Count; $col++) {
            $size = $small[$col]
            $brush = New-Object Drawing.SolidBrush $backgrounds[$row]
            $graphics.FillRectangle($brush, $col * $cell, $row * $cell, $cell, $cell); $brush.Dispose()
            $kind = if ($size -lt 44) { 'round' } else { 'mark' }
            $icon = Get-KitImage 'system' $kind $size
            $offsetInCell = [int](($cell - $size * 8) / 2)
            $graphics.DrawImage($icon, $col * $cell + $offsetInCell, $row * $cell + $offsetInCell, $size * 8, $size * 8)
            $icon.Dispose()
        }
    }
    $graphics.Dispose()
    $page.Save($Sheet, [Drawing.Imaging.ImageFormat]::Png); $page.Dispose()
}
Write-Host "icons from $Kit"
