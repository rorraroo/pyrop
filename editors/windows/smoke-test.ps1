param([Parameter(Mandatory)][string]$Executable)

$ErrorActionPreference = 'Stop'

$script = @'
use data library
use files library
use time library
import from data parse_json json_text parse_yaml yaml_text end
import from files save load end
import from time now end
record Note string body int count
def Note note Note "first\r\nsecond" 2
save "smoke.txt" convert string note
write_line convert Note load "smoke.txt"
write_line json_text parse_json "[1, 2.5]"
write_line yaml_text parse_yaml "a: 1"
write_line greater the year of now 2000
'@

$expected = @(
    'Note body "first\r\nsecond" count 2',
    '[',
    '  1,',
    '  2.5',
    ']',
    'a: 1',
    '',
    'true'
) -join "`n"

function Assert-Output([string]$Name, [string[]]$Lines) {
    $actual = ($Lines -join "`n").TrimEnd()
    if ($LASTEXITCODE -ne 0) { throw "$Name exited with $LASTEXITCODE." }
    if ($actual -ne $expected.TrimEnd()) { throw "$Name wrote something unexpected:`n$actual" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) "pyrop-smoke-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $work | Out-Null
$savedPath = $env:PATH
$hidden = 'DOTNET_ROOT', 'DOTNET_HOST_PATH', 'MSBuildSDKsPath', 'MSBUILD_EXE_PATH' | ForEach-Object {
    [pscustomobject]@{ Name = $_; Value = [Environment]::GetEnvironmentVariable($_) }
}
Push-Location $work
try {
    Copy-Item $Executable (Join-Path $work 'pyrop.exe')
    Set-Content -Path (Join-Path $work 'smoke.prp') -Value $script -Encoding ascii
    $hidden | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null) }
    $env:PATH = "$env:WINDIR\System32"
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { throw 'dotnet could not be hidden from the smoke test.' }

    $version = @(& .\pyrop.exe --version)
    if ($LASTEXITCODE -ne 0 -or $version[0] -notmatch '^pyrop \d') { throw "--version gave: $version" }

    Assert-Output 'The script' @(& .\pyrop.exe smoke.prp)

    & .\pyrop.exe --build smoke.prp | Out-Null
    if ($LASTEXITCODE -ne 0) { throw '--build failed.' }
    Assert-Output 'The built program' @(& .\smoke.exe)

    Write-Host 'Smoke test passed: the standalone pyrop works with .NET hidden.'
}
finally {
    Pop-Location
    $env:PATH = $savedPath
    $hidden | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $_.Value) }
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
