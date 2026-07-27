param(
    [string]$Source = "docs\assets\sadpsx-icon.png",
    [string]$Destination = "docs\assets\sadpsx.ico"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $repositoryRoot $Source
$destinationPath = Join-Path $repositoryRoot $Destination
$sizes = @(16, 24, 32, 48, 64, 128, 256)

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Source image not found: $sourcePath"
}

Add-Type -AssemblyName System.Drawing

$sourceImage = [System.Drawing.Image]::FromFile($sourcePath)
$pngFrames = [System.Collections.Generic.List[byte[]]]::new()

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $stream = [System.IO.MemoryStream]::new()

        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality =
                [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.SmoothingMode =
                [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $pngFrames.Add($stream.ToArray())
        }
        finally {
            $stream.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

$destinationDirectory = Split-Path -Parent $destinationPath
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

$output = [System.IO.File]::Open(
    $destinationPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write)
$writer = [System.IO.BinaryWriter]::new($output)

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$pngFrames.Count)

    $dataOffset = 6 + (16 * $pngFrames.Count)
    for ($index = 0; $index -lt $pngFrames.Count; $index++) {
        $size = $sizes[$index]
        $encodedSize = if ($size -eq 256) { 0 } else { $size }
        $frame = $pngFrames[$index]

        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Length)
        $writer.Write([uint32]$dataOffset)

        $dataOffset += $frame.Length
    }

    foreach ($frame in $pngFrames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
    $output.Dispose()
}

Write-Host "Icon created: $destinationPath"
