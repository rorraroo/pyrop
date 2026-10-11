param(
    [string]$Icon = (Join-Path $PSScriptRoot 'pyrop.ico')
)
$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Icon)) { throw "Icon not found at $Icon. Run build-icon.ps1 first." }

$installDir = Join-Path $env:LOCALAPPDATA 'Pyrop'
New-Item -ItemType Directory -Force $installDir | Out-Null
$installedIcon = Join-Path $installDir 'pyrop.ico'
Copy-Item $Icon $installedIcon -Force

$editor = @(
    "$env:LOCALAPPDATA\Programs\Microsoft VS Code\Code.exe",
    "$env:ProgramFiles\Microsoft VS Code\Code.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $editor) { $editor = "$env:WINDIR\System32\notepad.exe" }

$classes = 'HKCU:\Software\Classes'
$progId = 'Pyrop.Script'

function Set-Default($path, $value) {
    New-Item -Path $path -Force | Out-Null
    Set-ItemProperty -Path $path -Name '(default)' -Value $value
}

Set-Default "$classes\.prp" $progId
Set-ItemProperty -Path "$classes\.prp" -Name 'PerceivedType' -Value 'text'
Set-ItemProperty -Path "$classes\.prp" -Name 'Content Type' -Value 'text/plain'

Set-Default "$classes\$progId" 'Pyrop Script'
Set-Default "$classes\$progId\DefaultIcon" "`"$installedIcon`""
Set-Default "$classes\$progId\shell" 'open'
Set-Default "$classes\$progId\shell\open\command" "`"$editor`" `"%1`""
Set-Default "$classes\$progId\shell\run" 'Run with Pyrop'
Set-ItemProperty -Path "$classes\$progId\shell\run" -Name 'Icon' -Value "`"$installedIcon`""
Set-Default "$classes\$progId\shell\run\command" "`"$env:WINDIR\System32\cmd.exe`" /c pyrop `"%1`" & echo. & pause"

Add-Type -Namespace Pyrop -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, System.IntPtr item1, System.IntPtr item2);'
[Pyrop.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Registered .prp files: icon $installedIcon, opens in $editor, 'Run with Pyrop' in the context menu."
