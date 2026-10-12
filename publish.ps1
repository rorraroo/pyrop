param(
    [string]$Runtime,
    [string]$Output = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'

if (-not $Runtime) {
    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $system = if ($env:OS -eq 'Windows_NT') { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
    $Runtime = "$system-$architecture"
}

dotnet publish (Join-Path $PSScriptRoot 'Pyrop.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=true `
    -p:EnableCompressionInSingleFile=true `
    --output $Output

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Standalone pyrop for $Runtime is in $Output"
