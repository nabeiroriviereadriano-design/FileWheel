# Genere les manifestes winget de FileWheel pour une version publiee.
# Usage : .\New-WingetManifest.ps1 -Version 0.3.0 -InstallerUrl https://github.com/.../FileWheel-Setup-0.3.0.exe -Sha256 <empreinte> -OutDir manifests
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $InstallerUrl,
    [Parameter(Mandatory)] [string] $Sha256,
    [string] $OutDir = "winget-manifests",
    [string] $PackageId = "FileWheel.FileWheel"
)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $OutDir "$Version"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$repo = "https://github.com/nabeiroriviereadriano-design/FileWheel"
$schema = "1.10.0"
$utf8 = New-Object System.Text.UTF8Encoding($false)

$versionYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$schema.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $schema
"@

$installerYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$schema.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Version
MinimumOSVersion: 10.0.17763.0
InstallerType: nullsoft
Scope: user
InstallModes:
- interactive
- silent
UpgradeBehavior: install
ProductCode: FileWheel
ReleaseDate: $(Get-Date -Format 'yyyy-MM-dd')
Installers:
- Architecture: x64
  InstallerUrl: $InstallerUrl
  InstallerSha256: $($Sha256.ToUpperInvariant())
ManifestType: installer
ManifestVersion: $schema
"@

$enYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.$schema.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Version
PackageLocale: en-US
Publisher: FileWheel contributors
PublisherUrl: $repo
PublisherSupportUrl: $repo/issues
PackageName: FileWheel
PackageUrl: $repo
License: MIT
LicenseUrl: $repo/blob/main/LICENSE.txt
ShortDescription: Radial action wheel for File Explorer - hold Alt while dragging a file to convert, compress or edit it.
Description: |-
  FileWheel is a tray app for Windows 10/11. Hold Alt while dragging a file in File Explorer or on the Desktop and a wheel appears under the cursor:
  drop the file on a format (PDF to DOCX, PNG to JPG, MP4 to MP3...) to convert it next to the original, or on a tool
  (crop, background, compress, annotate, remove background, recognize text, PDF tools). Local only; originals are never overwritten.
Moniker: filewheel
Tags:
- converter
- drag-and-drop
- file-explorer
- pdf
- image
- ocr
ReleaseNotesUrl: $repo/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: $schema
"@

$frYaml = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.locale.$schema.schema.json
PackageIdentifier: $PackageId
PackageVersion: $Version
PackageLocale: fr-FR
Publisher: FileWheel contributors
PackageName: FileWheel
ShortDescription: Roue d'actions pour l'Explorateur - maintenez Alt en glissant un fichier pour le convertir, le compresser ou le retoucher.
ManifestType: locale
ManifestVersion: $schema
"@

[IO.File]::WriteAllText((Join-Path $dir "$PackageId.yaml"), $versionYaml, $utf8)
[IO.File]::WriteAllText((Join-Path $dir "$PackageId.installer.yaml"), $installerYaml, $utf8)
[IO.File]::WriteAllText((Join-Path $dir "$PackageId.locale.en-US.yaml"), $enYaml, $utf8)
[IO.File]::WriteAllText((Join-Path $dir "$PackageId.locale.fr-FR.yaml"), $frYaml, $utf8)
Write-Host "Manifestes winget : $dir"
