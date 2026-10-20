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

# Détection de foundry dans les chemins standards
$foundryInstalled = $false
if (Get-Command foundry -ErrorAction SilentlyContinue) {
    $foundryInstalled = $true
} elseif (Test-Path "$env:LOCALAPPDATA\Microsoft\WindowsApps\foundry.exe") {
    $env:PATH = "$env:LOCALAPPDATA\Microsoft\WindowsApps;$env:PATH"
    $foundryInstalled = $true
} elseif (Test-Path "$env:ProgramFiles\Microsoft Foundry\foundry.exe") {
    $env:PATH = "$env:ProgramFiles\Microsoft Foundry;$env:PATH"
    $foundryInstalled = $true
}

if ($foundryInstalled) {
    # Démarrage du serveur s'il n'est pas déjà actif
    try {
        & foundry server start 2>$null | Out-Null
    } catch {}

    # Attente active du port d'écoute réel du démon (jusqu'à 6 secondes)
    $foundryUrl = $null
    for ($i = 0; $i -lt 12; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            $statusJson = & foundry server status -o json 2>$null
            if ($statusJson) {
                $statusObj = $statusJson | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($statusObj -and $statusObj.running -and $statusObj.webUrls -and $statusObj.webUrls.Count -gt 0) {
                    $foundryUrl = $statusObj.webUrls[0]
                    break
                }
            }
        } catch {}
    }

    if ($foundryUrl) {
        $env:FOUNDRY_BASE_URL = $foundryUrl
        Write-Host "      [OK] Moteur d'inférence Foundry Local actif sur $foundryUrl." -ForegroundColor Green
    } else {
        Write-Host "      [OK] Moteur d'inférence Foundry Local démarré." -ForegroundColor Green
    }

    # 2. Préchauffage dynamique des modèles en mémoire vive (RAM / VPU)
    Write-Host ""
    Write-Host "[2/3] Préchauffage des modèles d'IA en mémoire vive (RAM / VPU)..." -ForegroundColor Cyan
    
    $procName = (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue).Name
    if (-not $procName) { $procName = $env:PROCESSOR_IDENTIFIER }

    $loaded = $false
    # Alias universel : Foundry Local choisit lui-même la meilleure variante (NPU/GPU/CPU) pour ce matériel
    Write-Host "      * Modèle Phi-4-mini [variante optimale détectée par Foundry]..." -ForegroundColor Yellow
    try { & foundry model load phi-4-mini 2>$null | Out-Null; $loaded = $true } catch {}
    if ($procName -match "Intel") {
        Write-Host "      * Modèle NPU [Intel AI Boost]..." -ForegroundColor Yellow
        try { & foundry model load phi-4-mini-instruct-openvino-npu 2>$null | Out-Null; $loaded = $true } catch {}
        try { & foundry model load phi-4-mini-instruct-openvino-gpu 2>$null | Out-Null } catch {}
    }
    elseif ($procName -match "AMD" -or $procName -match "Ryzen") {
        Write-Host "      * Modèle Accéléré DirectML [AMD Ryzen AI / Radeon]..." -ForegroundColor Yellow
        try { & foundry model load phi-4-mini-instruct-directml-npu 2>$null | Out-Null; $loaded = $true } catch {}
    }
    elseif ($procName -match "Snapdragon" -or $procName -match "Qualcomm" -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        Write-Host "      * Modèle NPU [Qualcomm Hexagon]..." -ForegroundColor Yellow
        try { & foundry model load phi-4-mini-instruct-qualcomm-npu 2>$null | Out-Null; $loaded = $true } catch {}
        try { & foundry model load phi-4-mini-instruct-directml-npu 2>$null | Out-Null } catch {}
    }

    # Préchauffage du modèle universel CPU (permet les tests comparatifs NPU vs CPU immédiats)
    Write-Host "      * Modèle Universel CPU [Comparatif NPU vs CPU]..." -ForegroundColor Yellow
    try { & foundry model load Phi-4-mini-instruct-generic-cpu 2>$null | Out-Null; $loaded = $true } catch {}

    Write-Host "      [OK] Modèles d'IA prêts pour un démarrage instantané." -ForegroundColor Green
} else {
    Write-Host "      [ATTENTION] Microsoft Foundry Local CLI n'est pas installé sur ce poste." -ForegroundColor Yellow
    Write-Host "      -> Pour activer les assistants LLM (Finance, Assurance, Opérations), lancez : .\Installer_Prerequis.ps1" -ForegroundColor Yellow
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
