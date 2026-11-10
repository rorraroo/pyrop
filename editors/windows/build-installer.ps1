$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$version = ([xml](Get-Content (Join-Path $root 'Pyrop.csproj') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$manifest = Get-Content (Join-Path $root 'editors\vscode\package.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $version) { throw "Pyrop.csproj is at $version but the extension is at $($manifest.version)." }

$compiler = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is required: winget install JRSoftware.InnoSetup' }

dotnet publish (Join-Path $root 'Pyrop.csproj') -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

& (Join-Path $PSScriptRoot 'smoke-test.ps1') -Executable (Join-Path $root 'publish\Pyrop.exe')

& (Join-Path $root 'editors\vscode\pack.ps1')

$setupName = "pyrop-setup-$version.exe"
$staging = Join-Path ([IO.Path]::GetTempPath()) "pyrop-setup-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $attempt = 0
    do {
        $attempt++
        Remove-Item (Join-Path $staging '*') -Force -ErrorAction SilentlyContinue
        & $compiler "/DAppVersion=$version" "/DExtensionId=$($manifest.publisher).$($manifest.name)" "/DOutputFolder=$staging" (Join-Path $PSScriptRoot 'pyrop.iss')
        if ($LASTEXITCODE -ne 0 -and $attempt -lt 3) {
            Write-Host "Inno Setup failed (attempt $attempt of 3), usually because an antivirus scanner held the file; trying again."
            Start-Sleep -Seconds 2
        }
    } while ($LASTEXITCODE -ne 0 -and $attempt -lt 3)
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

    $target = Join-Path $root "publish\$setupName"
    Move-Item (Join-Path $staging $setupName) $target -Force
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Created $target"
