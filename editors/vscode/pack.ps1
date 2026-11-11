$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = $PSScriptRoot
$manifest = Get-Content (Join-Path $root 'package.json') -Raw | ConvertFrom-Json
$output = Join-Path $root "$($manifest.name)-$($manifest.version).vsix"

$files = @('package.json', 'extension.js', 'language-configuration.json', 'README.md') +
    (Get-ChildItem (Join-Path $root 'syntaxes') -File | ForEach-Object { "syntaxes/$($_.Name)" }) +
    (Get-ChildItem (Join-Path $root 'icons') -File -Recurse | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })

$vsixManifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Language="en-US" Id="$($manifest.name)" Version="$($manifest.version)" Publisher="$($manifest.publisher)" />
    <DisplayName>$($manifest.displayName)</DisplayName>
    <Description xml:space="preserve">$($manifest.description)</Description>
    <Categories>$($manifest.categories -join ',')</Categories>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$($manifest.engines.vscode)" />
    </Properties>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
    <Asset Type="Microsoft.VisualStudio.Services.Icons.Default" Path="extension/$($manifest.icon)" Addressable="true" />
  </Assets>
</PackageManifest>
"@

$contentTypes = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension=".js" ContentType="application/javascript" />
  <Default Extension=".json" ContentType="application/json" />
  <Default Extension=".md" ContentType="text/markdown" />
  <Default Extension=".png" ContentType="image/png" />
  <Default Extension=".svg" ContentType="image/svg+xml" />
  <Default Extension=".txt" ContentType="text/plain" />
  <Default Extension=".vsixmanifest" ContentType="text/xml" />
</Types>
"@

function Add-TextEntry($archive, $name, $text) {
    $writer = New-Object System.IO.StreamWriter($archive.CreateEntry($name).Open(), (New-Object System.Text.UTF8Encoding $false))
    $writer.Write($text)
    $writer.Dispose()
}

if (Test-Path $output) { Remove-Item $output }
$archive = [System.IO.Compression.ZipFile]::Open($output, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Add-TextEntry $archive '[Content_Types].xml' $contentTypes
    Add-TextEntry $archive 'extension.vsixmanifest' $vsixManifest
    foreach ($file in $files) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $file), "extension/$file") | Out-Null
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Created $output"
