#requires -Version 5.1
<#
.SYNOPSIS
  Construit le programme d'installation FileWheel-Setup-<version>.exe (installer / mettre a jour / desinstaller).

.DESCRIPTION
  1. verifie le SDK .NET 10 (propose de l'installer avec winget s'il manque) ;
  2. compile FileWheel en version autonome (aucun runtime .NET a installer chez l'utilisateur) ;
  3. verifie NSIS (propose de l'installer avec winget s'il manque) ;
  4. produit FileWheel-Setup-<version>.exe a la racine du dossier, puis le lance si -Lancer.

.PARAMETER Lancer
  Lance l'installation (ou la mise a jour) des que le .exe est pret.
#>
[CmdletBinding()]
param([switch]$Lancer)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root       = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $Root 'publish'
$Nsi        = Join-Path $PSScriptRoot 'FileWheel.nsi'

function Write-Step($t) { Write-Host ""; Write-Host "==> $t" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "    $t" -ForegroundColor Green }
function Stop-Here($m)  { Write-Host ""; Write-Host "ERREUR : $m" -ForegroundColor Red; Write-Host ""; exit 1 }
function Ask($q) { $a = Read-Host "    $q [O/n]"; return (-not $a) -or ($a -match '^[oOyY]') }
function Refresh-Path { $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User') }

# --------------------------------------------------------------------- version
[xml]$props = Get-Content (Join-Path $Root 'Directory.Build.props') -Raw -Encoding UTF8
$node = $props.SelectSingleNode('//Version')
$Version = if ($node) { $node.InnerText.Trim() } else { '' }
if (-not $Version) { Stop-Here "Version introuvable dans Directory.Build.props." }
$Out = Join-Path $Root "FileWheel-Setup-$Version.exe"
Write-Host ""
Write-Host " FileWheel $Version — construction du programme d'installation" -ForegroundColor White

# --------------------------------------------------------------------- SDK .NET 10
Write-Step "SDK .NET 10"
function Find-Dotnet {
    $c = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $d = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $d) { return $d }
    return $null
}
function Test-Sdk10($d) { if (-not $d) { return $false }; return [bool](& $d --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }) }
$dotnet = Find-Dotnet
if (-not (Test-Sdk10 $dotnet)) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { Stop-Here "Installez le SDK .NET 10 (https://dotnet.microsoft.com/download/dotnet/10.0) puis relancez." }
    if (-not (Ask "Le SDK .NET 10 est nécessaire. L'installer avec winget ?")) { Stop-Here "SDK .NET 10 manquant." }
    & winget install --id Microsoft.DotNet.SDK.10 -e --source winget --accept-package-agreements --accept-source-agreements
    Refresh-Path
    $dotnet = Find-Dotnet
    if (-not (Test-Sdk10 $dotnet)) { Stop-Here "SDK .NET 10 toujours introuvable : fermez cette fenêtre et relancez." }
}
Write-Ok ((& $dotnet --list-sdks | Where-Object { $_ -match '^10\.' } | Select-Object -Last 1))
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

# --------------------------------------------------------------------- compilation
Write-Step "Compilation de FileWheel (version autonome, sans runtime à installer)"
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
& $dotnet publish (Join-Path $Root 'src\FileWheel.App\FileWheel.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o $PublishDir
if ($LASTEXITCODE -ne 0) { Stop-Here "La compilation a échoué (messages ci-dessus)." }
if (-not (Test-Path (Join-Path $PublishDir 'FileWheel.exe'))) { Stop-Here "FileWheel.exe n'a pas été produit." }
$sizeMb = [math]::Round(((Get-ChildItem $PublishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Ok "Compilation réussie ($sizeMb Mo avant compression)"

# --------------------------------------------------------------------- NSIS
Write-Step "Outil de création d'installateur (NSIS)"
function Find-Makensis {
    $c = Get-Command makensis -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    foreach ($p in @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe", "$env:LOCALAPPDATA\Programs\NSIS\makensis.exe")) {
        if ($p -and (Test-Path $p)) { return $p }
    }
    return $null
}
$makensis = Find-Makensis
if (-not $makensis) {
    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) { Stop-Here "Installez NSIS (https://nsis.sourceforge.io) puis relancez." }
    if (-not (Ask "NSIS (gratuit) est nécessaire pour fabriquer le .exe. L'installer avec winget ?")) { Stop-Here "NSIS manquant." }
    & winget install --id NSIS.NSIS -e --source winget --accept-package-agreements --accept-source-agreements
    Refresh-Path
    $makensis = Find-Makensis
    if (-not $makensis) { Stop-Here "NSIS toujours introuvable : fermez cette fenêtre et relancez." }
}
Write-Ok $makensis

# --------------------------------------------------------------------- installateur
Write-Step "Création de FileWheel-Setup-$Version.exe"
if (Test-Path $Out) { Remove-Item $Out -Force }
& $makensis /V2 "/DVERSION=$Version" "/DPUBLISH_DIR=$PublishDir" "/DOUTFILE=$Out" `
    "/DICON=$(Join-Path $Root 'assets\filewheel.ico')" "/DSIDE_BITMAP=$(Join-Path $Root 'assets\installer-side.bmp')" $Nsi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $Out)) { Stop-Here "La création de l'installateur a échoué (messages ci-dessus)." }
$setupMb = [math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Ok "$Out ($setupMb Mo)"

Write-Host ""
Write-Host "Prêt. Ce fichier unique installe, met à jour ou désinstalle FileWheel :" -ForegroundColor Green
Write-Host "  $Out"
Write-Host "  Vous pouvez le copier sur un autre PC Windows 10/11 64 bits : rien d'autre à installer."
Write-Host ""

if ($Lancer) {
    Start-Process -FilePath $Out
} else {
    Start-Process explorer.exe -ArgumentList "/select,`"$Out`""
}
exit 0
