<#
.SYNOPSIS
    Copilot+ PC Demo Hub - Générateur de Package de Release (.PS1)
.DESCRIPTION
    Menu interactif permettant de générer une archive ZIP pour GitHub Releases
    (application seule) ou pour partage interne / clé USB (avec modèles).
#>

[CmdletBinding()]
param(
    [ValidateSet("1", "2")]
    [string]$Choix
)

$Host.UI.RawUI.WindowTitle = "Copilot+ PC Demo - Création de Package Release"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "       Copilot+ PC Demo - Création de Package de Release (PS1)        " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

if (-not $Choix) {
    Write-Host "Choisissez le type de package à générer :" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  [1] Package Application Seule (Recommandé pour GitHub Releases)" -ForegroundColor Green
    Write-Host "      Inclut le binaire compilé, les scripts, les microservices Python et les données." -ForegroundColor DarkGray
    Write-Host "      Archive légère (~30 Mo) téléchargeable en 2 secondes." -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  [2] Package Complet Tout-en-Un (Avec modèles locaux)" -ForegroundColor Yellow
    Write-Host "      Inclut en plus tous les modèles OpenVINO et ONNX (~2 Go sans le cache NPU)." -ForegroundColor DarkGray
    Write-Host "      Idéal pour le partage sur clé USB ou OneDrive d'équipe." -ForegroundColor DarkGray
    Write-Host ""
    $inputChoice = Read-Host "Votre choix (1 ou 2) [Défaut: 1]"
    $Choix = if ([string]::IsNullOrWhiteSpace($inputChoice)) { "1" } else { $inputChoice.Trim() }
}

$workerScript = Join-Path $ScriptDir "scripts\creer_package_release.ps1"

if ($Choix -eq "2") {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$workerScript" -IncludeModels
} else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File "$workerScript"
}

Write-Host ""
pause
