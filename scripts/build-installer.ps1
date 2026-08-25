# Construit Setup.msi : publie l'appli en self-contained single-file (aucune dépendance .NET
# à installer séparément sur la machine cible) puis génère le .msi avec WiX v5 (CLI `wix`,
# installé via `dotnet tool install --global wix --version 5.0.2` — volontairement pas v6/v7,
# qui imposent l'acceptation d'une EULA "Open Source Maintenance Fee", voir
# memory/installer_msi_setup.md).
#
# Usage :
#   pwsh scripts/build-installer.ps1                  # version 1.1.0
#   pwsh scripts/build-installer.ps1 -Version 1.2.0
param(
    [string]$Version = "1.1.0"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$csproj = Join-Path $repoRoot "src\Partition2MuseScore\Partition2MuseScore.csproj"
$publishDir = Join-Path $repoRoot "installer\bin\publish"
$outputMsi = Join-Path $repoRoot "installer\bin\Partition2MuseScoreSetup.msi"

if (Test-Path $publishDir) {
    Write-Host "→ Nettoyage de l'ancien dossier de publication..."
    Remove-Item -Recurse -Force $publishDir
}

Write-Host "→ Publication self-contained single-file (win-x64)..."
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish a échoué."
    exit 1
}

Write-Host "→ Génération du .msi avec WiX..."
wix build (Join-Path $repoRoot "installer\Package.wxs") `
    -d "PublishDir=$publishDir" -d "AppVersion=$Version" `
    -arch x64 -o $outputMsi
if ($LASTEXITCODE -ne 0) {
    Write-Error "wix build a échoué."
    exit 1
}

Write-Host "✓ Setup.msi généré : $outputMsi"
