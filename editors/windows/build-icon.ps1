param(
    [string]$Svg = (Join-Path $PSScriptRoot '..\vscode\icons\pyrop.svg'),
    [string]$Ico = (Join-Path $PSScriptRoot 'pyrop.ico'),
    [string]$Png128 = (Join-Path $PSScriptRoot '..\vscode\icons\pyrop-128.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge is required to render the SVG.' }

$sizes = @(256, 128, 48, 32, 24, 16)
$work = Join-Path ([IO.Path]::GetTempPath()) "pyrop-icon-$([guid]::NewGuid())"
New-Item -ItemType Directory $work | Out-Null
try {
    $svgUri = ([Uri](Resolve-Path $Svg).Path).AbsoluteUri
    $x = 0
    $images = foreach ($size in $sizes) {
        "<img src='$svgUri' style='position:absolute;left:${x}px;top:0;width:${size}px;height:${size}px'>"
        $x += $size + 8
    }
    Set-Content (Join-Path $work 'icon.html') "<html><body style='margin:0;background:transparent'>$($images -join '')</body></html>" -Encoding utf8
    $shot = Join-Path $work 'shot.png'
    Start-Process -FilePath $edge -Wait -ArgumentList @(
        '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
        '--default-background-color=00000000', "--window-size=$x,256",
        "--screenshot=$shot", "--user-data-dir=$(Join-Path $work 'profile')",
        ([Uri](Join-Path $work 'icon.html')).AbsoluteUri)
    if (-not (Test-Path $shot)) { throw 'Rendering failed.' }

    $sheet = [System.Drawing.Bitmap]::FromFile($shot)
    $pngs = @{}
    try {
        $x = 0
        foreach ($size in $sizes) {
            $frame = $sheet.Clone((New-Object System.Drawing.Rectangle $x, 0, $size, $size), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $stream = New-Object System.IO.MemoryStream
            $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frame.Dispose()
            $pngs[$size] = $stream.ToArray()
            $x += $size + 8
        }
    }
    finally {
        $sheet.Dispose()
    }

    [IO.File]::WriteAllBytes($Png128, $pngs[128])

    $icoSizes = $sizes | Where-Object { $_ -ne 128 }
    $output = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $output
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$icoSizes.Count)
    $offset = 6 + 16 * $icoSizes.Count
    foreach ($size in $icoSizes) {
        $dimension = if ($size -ge 256) { 0 } else { $size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$pngs[$size].Length); $writer.Write([uint32]$offset)
        $offset += $pngs[$size].Length
    }
    foreach ($size in $icoSizes) { $writer.Write($pngs[$size]) }
    $writer.Flush()
    [IO.File]::WriteAllBytes($Ico, $output.ToArray())
    $writer.Dispose()
    Write-Host "Created $Ico and $Png128"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
