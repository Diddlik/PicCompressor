[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\assets\PicCompressor.ico")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()

foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $background = $null
    $path = $null
    $pen = $null
    $stream = $null

    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $margin = [single]($size * 0.055)
        $diameter = [single]($size * 0.36)
        $bounds = [System.Drawing.RectangleF]::new(
            $margin,
            $margin,
            [single]($size - (2 * $margin)),
            [single]($size - (2 * $margin)))

        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $path.AddArc($bounds.Left, $bounds.Top, $diameter, $diameter, 180, 90)
        $path.AddArc($bounds.Right - $diameter, $bounds.Top, $diameter, $diameter, 270, 90)
        $path.AddArc(
            $bounds.Right - $diameter,
            $bounds.Bottom - $diameter,
            $diameter,
            $diameter,
            0,
            90)
        $path.AddArc($bounds.Left, $bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
        $path.CloseFigure()

        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            $bounds,
            [System.Drawing.ColorTranslator]::FromHtml("#6895F4"),
            [System.Drawing.ColorTranslator]::FromHtml("#406BCE"),
            55)
        $graphics.FillPath($background, $path)

        $pen = [System.Drawing.Pen]::new(
            [System.Drawing.Color]::White,
            [single][Math]::Max(1.5, $size * 0.105))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

        $scale = [single]($size / 16)
        $graphics.DrawLines(
            $pen,
            [System.Drawing.PointF[]]@(
                [System.Drawing.PointF]::new(6 * $scale, 3 * $scale),
                [System.Drawing.PointF]::new(3 * $scale, 8 * $scale),
                [System.Drawing.PointF]::new(6 * $scale, 13 * $scale)))
        $graphics.DrawLines(
            $pen,
            [System.Drawing.PointF[]]@(
                [System.Drawing.PointF]::new(10 * $scale, 3 * $scale),
                [System.Drawing.PointF]::new(13 * $scale, 8 * $scale),
                [System.Drawing.PointF]::new(10 * $scale, 13 * $scale)))

        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $pen) { $pen.Dispose() }
        if ($null -ne $background) { $background.Dispose() }
        if ($null -ne $path) { $path.Dispose() }
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$file = [System.IO.File]::Create($resolvedOutput)
$writer = [System.IO.BinaryWriter]::new($file)

try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $sizeByte = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]$sizeByte)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $resolvedOutput
