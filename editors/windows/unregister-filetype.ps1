$ErrorActionPreference = 'Stop'

$classes = 'HKCU:\Software\Classes'
$progId = 'Pyrop.Script'

if ((Test-Path "$classes\.prp") -and (Get-ItemProperty -Path "$classes\.prp").'(default)' -eq $progId) {
    Remove-Item "$classes\.prp" -Recurse -Force
}
if (Test-Path "$classes\$progId") {
    Remove-Item "$classes\$progId" -Recurse -Force
}
Remove-Item (Join-Path $env:LOCALAPPDATA 'Pyrop') -Recurse -Force -ErrorAction SilentlyContinue

Add-Type -Namespace Pyrop -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, int flags, System.IntPtr item1, System.IntPtr item2);'
[Pyrop.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host 'Removed the .prp file association.'
