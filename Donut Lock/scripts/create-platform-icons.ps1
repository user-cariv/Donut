$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'Photos\donut lock logo.jpeg'
$windowsAssets = Join-Path $root 'windows\Assets'
$macAssets = Join-Path $root 'macOS\Assets'
New-Item -ItemType Directory -Force -Path $windowsAssets, $macAssets | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $windowsAssets 'donut-lock-logo.jpeg') -Force
Copy-Item -LiteralPath $source -Destination (Join-Path $macAssets 'donut-lock-logo.jpeg') -Force

function New-PngBytes([System.Drawing.Image]$Image, [int]$Size) {
    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawImage($Image, 0, 0, $Size, $Size)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    return ,$bytes
}

$image = [System.Drawing.Image]::FromFile($source)
$icoSizes = @(16, 24, 32, 48, 64, 128, 256)
$icoEntries = foreach ($size in $icoSizes) { [pscustomobject]@{ Size = $size; Bytes = (New-PngBytes $image $size) } }
$icoPath = Join-Path $windowsAssets 'DonutLock.ico'
$icoStream = [System.IO.File]::Create($icoPath)
$writer = New-Object System.IO.BinaryWriter($icoStream)
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$icoEntries.Count)
$offset = 6 + (16 * $icoEntries.Count)
foreach ($entry in $icoEntries) {
    $dimension = if ($entry.Size -eq 256) { [byte]0 } else { [byte]$entry.Size }
    $writer.Write($dimension); $writer.Write($dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$entry.Bytes.Length); $writer.Write([UInt32]$offset)
    $offset += $entry.Bytes.Length
}
foreach ($entry in $icoEntries) { $writer.Write([byte[]]$entry.Bytes) }
$writer.Dispose(); $icoStream.Dispose()

$icnsEntries = @(@{ Size=16; Type='icp4' }, @{ Size=32; Type='icp5' }, @{ Size=64; Type='icp6' }, @{ Size=128; Type='ic07' }, @{ Size=256; Type='ic08' }, @{ Size=512; Type='ic09' }, @{ Size=1024; Type='ic10' })
$chunks = foreach ($entry in $icnsEntries) {
    $png = New-PngBytes $image $entry.Size
    $chunk = New-Object System.IO.MemoryStream
    $chunkWriter = New-Object System.IO.BinaryWriter($chunk)
    $chunkWriter.Write([System.Text.Encoding]::ASCII.GetBytes($entry.Type))
    $chunkWriter.Write([System.Net.IPAddress]::HostToNetworkOrder([int]($png.Length + 8)))
    $chunkWriter.Write([byte[]]$png)
    $chunkWriter.Dispose(); $chunk.Dispose()
    # The memory stream is copied before disposal below.
    [pscustomobject]@{ Bytes = $chunk.ToArray() }
}
$icnsPath = Join-Path $macAssets 'DonutLock.icns'
$icnsStream = [System.IO.File]::Create($icnsPath)
$icnsWriter = New-Object System.IO.BinaryWriter($icnsStream)
$total = 8 + (($chunks | ForEach-Object { $_.Bytes.Length } | Measure-Object -Sum).Sum)
$icnsWriter.Write([System.Text.Encoding]::ASCII.GetBytes('icns'))
$icnsWriter.Write([System.Net.IPAddress]::HostToNetworkOrder([int]$total))
foreach ($chunk in $chunks) { $icnsWriter.Write([byte[]]$chunk.Bytes) }
$icnsWriter.Dispose(); $icnsStream.Dispose(); $image.Dispose()
