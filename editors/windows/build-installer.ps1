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

& (Join-Path $root 'editors\vscode\pack.ps1')

& $compiler "/DAppVersion=$version" "/DExtensionId=$($manifest.publisher).$($manifest.name)" (Join-Path $PSScriptRoot 'pyrop.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

Write-Host "Created $(Join-Path $root "publish\pyrop-setup-$version.exe")"
