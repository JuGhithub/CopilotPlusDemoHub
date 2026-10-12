<#
.SYNOPSIS
    Copilot+ PC Demo Hub - Compilation .NET 8 (.PS1)
.DESCRIPTION
    Compile l'application WPF en mode Release, Debug ou Publication autonome exportable.
#>

[CmdletBinding()]
param(
    [ValidateSet("1", "2", "3")]
    [string]$Mode
)

$Host.UI.RawUI.WindowTitle = "Copilot+ PC Demo - Compilation .NET 8"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$env:PATH = "$env:ProgramFiles\dotnet;$env:LOCALAPPDATA\Microsoft\WindowsApps;$env:PATH"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "            Copilot+ PC Demo - Compilation .NET 8 (PS1)               " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# Vérification du SDK
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[ERREUR] .NET SDK 8.0 est introuvable dans votre PATH." -ForegroundColor Red
    Write-Host "Veuillez exécuter '.\Installer_Prerequis.ps1' pour installer les prérequis." -ForegroundColor Yellow
    pause
    exit 1
}

Write-Host "[1/3] Arrêt des processus de démonstration en cours d'exécution..." -ForegroundColor Cyan
Get-Process -Name "DemoHub" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (-not $Mode) {
    Write-Host ""
    Write-Host "[2/3] Choisissez le mode de compilation :" -ForegroundColor Cyan
    Write-Host "  [1] Compilation Release (Recommandé - Haute performance)" -ForegroundColor Green
    Write-Host "  [2] Compilation Debug (Mode développement / diagnostic)" -ForegroundColor Yellow
    Write-Host "  [3] Publication complète autonome (Dossier exportable bin\Publish\x64)" -ForegroundColor Magenta
    Write-Host ""
    $choice = Read-Host "Votre choix (1, 2 ou 3) [Défaut: 1]"
    $Mode = if ([string]::IsNullOrWhiteSpace($choice)) { "1" } else { $choice.Trim() }
}

Write-Host ""
Write-Host "[3/3] Exécution de la compilation..." -ForegroundColor Cyan

switch ($Mode) {
    "1" {
        Write-Host "Compilation en mode Release..." -ForegroundColor Yellow
        & dotnet build "$ScriptDir\DemoHub\DemoHub.csproj" -c Release
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "[SUCCÈS] Binaire généré dans :" -ForegroundColor Green
            Write-Host "  DemoHub\bin\Release\net8.0-windows10.0.18362\DemoHub.exe" -ForegroundColor White
        } else {
            Write-Host "[ERREUR] Échec de la compilation." -ForegroundColor Red
        }
    }
    "2" {
        Write-Host "Compilation en mode Debug..." -ForegroundColor Yellow
        & dotnet build "$ScriptDir\DemoHub\DemoHub.csproj" -c Debug
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "[SUCCÈS] Binaire généré dans :" -ForegroundColor Green
            Write-Host "  DemoHub\bin\Debug\net8.0-windows10.0.18362\DemoHub.exe" -ForegroundColor White
        } else {
            Write-Host "[ERREUR] Échec de la compilation." -ForegroundColor Red
        }
    }
    "3" {
        Write-Host "Publication autonome Release [x64]..." -ForegroundColor Yellow
        & dotnet publish "$ScriptDir\DemoHub\DemoHub.csproj" -c Release -r win-x64 --no-self-contained -o "$ScriptDir\bin\Publish\x64"
        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "[SUCCÈS] Application autonome publiée dans :" -ForegroundColor Green
            Write-Host "  bin\Publish\x64\DemoHub.exe" -ForegroundColor White
        } else {
            Write-Host "[ERREUR] Échec de la publication." -ForegroundColor Red
        }
    }
}

Write-Host ""
pause
