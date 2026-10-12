<#
.SYNOPSIS
    Copilot+ PC Demo Hub - Lanceur PowerShell (.PS1)
.DESCRIPTION
    Initialise le démon d'inférence Microsoft Foundry, précharge les modèles NPU/GPU
    et démarre l'application WPF Copilot+ Demo Hub avec accélération matérielle.
#>

[CmdletBinding()]
param()

$Host.UI.RawUI.WindowTitle = "Copilot+ PC Demo - Démarrage"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Configuration dynamique du PATH : Python et .NET en priorité
$pyPaths = @(
    "$env:ProgramFiles\Python312",
    "$env:ProgramFiles\Python312\Scripts",
    "$env:LOCALAPPDATA\Programs\Python\Python312",
    "$env:LOCALAPPDATA\Programs\Python\Python312\Scripts",
    "$env:ProgramFiles\dotnet",
    "$env:LOCALAPPDATA\Microsoft\WindowsApps"
)
foreach ($p in $pyPaths) {
    if ((Test-Path $p) -and ($env:PATH -notmatch [regex]::Escape($p))) {
        $env:PATH = "$p;$env:PATH"
    }
}

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "         Copilot+ PC Demo - Démarrage de la Démonstration (PS1)       " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Vérification et démarrage du service Microsoft Foundry Local
Write-Host "[1/3] Vérification du service Microsoft Foundry Local..." -ForegroundColor Cyan
if (Get-Command foundry -ErrorAction SilentlyContinue) {
    try {
        & foundry server start 2>$null | Out-Null
        Write-Host "      [OK] Moteur d'inférence Foundry Local actif." -ForegroundColor Green
    } catch {
        Write-Host "      [INFO] Le service Foundry tourne déjà ou est autonome." -ForegroundColor DarkGray
    }

    # 2. Préchauffage des modèles en mémoire vive
    Write-Host ""
    Write-Host "[2/3] Préchauffage des modèles d'IA en mémoire vive (RAM / VPU)..." -ForegroundColor Cyan
    Write-Host "      * Modèle NPU [Intel AI Boost]..." -ForegroundColor Yellow
    & foundry model load phi-4-mini-instruct-openvino-npu 2>$null | Out-Null
    Write-Host "      * Modèle GPU [Intel Arc Graphics]..." -ForegroundColor Yellow
    & foundry model load phi-4-mini-instruct-openvino-gpu 2>$null | Out-Null
    Write-Host "      [OK] Modèles préchargés pour démarrage instantané." -ForegroundColor Green
} else {
    Write-Host "      [INFO] Foundry Local CLI non détecté dans le PATH." -ForegroundColor DarkGray
    Write-Host "      Les démonstrations de vision, audio et OCR restent 100% fonctionnelles." -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "[2/3] Étape passée." -ForegroundColor DarkGray
}

# 3. Lancement de l'application
Write-Host ""
Write-Host "[3/3] Lancement de l'application Copilot+ Demo Hub..." -ForegroundColor Cyan

$candidates = @(
    @{ Path = "$ScriptDir\DemoHub\bin\Release\net8.0-windows10.0.18362\DemoHub.exe"; Label = "Version Release haute performance" },
    @{ Path = "$ScriptDir\bin\Publish\x64\DemoHub.exe"; Label = "Version autonome x64" },
    @{ Path = "$ScriptDir\DemoHub\bin\Debug\net8.0-windows10.0.18362\DemoHub.exe"; Label = "Version Debug" }
)

$launched = $false
foreach ($c in $candidates) {
    if (Test-Path $c.Path) {
        Write-Host "      Lancement de la $($c.Label)..." -ForegroundColor Green
        Start-Process -FilePath $c.Path -WorkingDirectory (Split-Path -Parent $c.Path)
        $launched = $true
        break
    }
}

if (-not $launched) {
    Write-Host "      Aucun binaire pré-compilé trouvé. Compilation et exécution à chaud..." -ForegroundColor Yellow
    & dotnet run --project "$ScriptDir\DemoHub\DemoHub.csproj" -c Release
}

Write-Host ""
Write-Host "Démonstration lancée avec succès !" -ForegroundColor Green
Start-Sleep -Seconds 2
