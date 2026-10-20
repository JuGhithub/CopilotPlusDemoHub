<#
.SYNOPSIS
    Copilot+ PC Demo Hub - Installation Automatisée des Prérequis (.PS1)
.DESCRIPTION
    Installe et configure automatiquement l'ensemble des socles logiciels, dépendances
    et modèles d'IA nécessaires pour exécuter Copilot+ Demo Hub en local.
#>

[CmdletBinding()]
param()

$Host.UI.RawUI.WindowTitle = "Copilot+ PC Demo - Installation des Prérequis"

# Auto-élévation en Administrateur si nécessaire pour LongPathsEnabled et winget
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[INFO] Elévation des privilèges requise pour la configuration système..." -ForegroundColor Yellow
    try {
        Start-Process powershell -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        exit
    } catch {
        Write-Host "[ATTENTION] Lancement en tant qu'administrateur refusé. Certaines étapes peuvent nécessiter des droits élevés." -ForegroundColor Yellow
    }
}

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "         Copilot+ PC Demo - Installation des Prérequis (PowerShell)   " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# -------------------------------------------------------------------
# 0. Support des chemins longs Windows (LongPathsEnabled)
# -------------------------------------------------------------------
Write-Host "[0/7] Optimisation du système de fichiers Windows..." -ForegroundColor Cyan
try {
    $regKey = "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem"
    $currentVal = (Get-ItemProperty -Path $regKey -Name "LongPathsEnabled" -ErrorAction SilentlyContinue).LongPathsEnabled
    if ($currentVal -eq 1) {
        Write-Host "  [OK] Support des chemins longs de fichiers déjà activé." -ForegroundColor Green
    } else {
        Write-Host "  Activation des chemins longs [requis pour PyTorch et OpenVINO]..." -ForegroundColor Yellow
        Set-ItemProperty -Path $regKey -Name "LongPathsEnabled" -Value 1 -Type DWord -Force
        Write-Host "  [OK] Chemins longs activés avec succès." -ForegroundColor Green
    }
} catch {
    Write-Host "  [INFO] Impossible de modifier la clé LongPathsEnabled. Relancez en tant qu'administrateur si nécessaire." -ForegroundColor DarkGray
}

# -------------------------------------------------------------------
# 1. Vérification du gestionnaire winget
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[1/7] Vérification du gestionnaire de paquets winget..." -ForegroundColor Cyan
if (Get-Command winget -ErrorAction SilentlyContinue) {
    Write-Host "  [OK] Gestionnaire winget détecté." -ForegroundColor Green
} else {
    Write-Host "  [!] ERREUR : winget n'est pas disponible dans votre session." -ForegroundColor Red
    Write-Host "      Installez 'Programme d'installation d'application' depuis le Microsoft Store." -ForegroundColor Yellow
    pause
    exit 1
}

# -------------------------------------------------------------------
# 2. Vérification et installation de .NET 8.0 SDK
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[2/7] Vérification de .NET 8.0 SDK..." -ForegroundColor Cyan
$dotnetVer = $null
try { $dotnetVer = & dotnet --version 2>$null } catch {}

if ($dotnetVer -and $dotnetVer.StartsWith("8.")) {
    Write-Host "  [OK] .NET SDK détecté : $dotnetVer" -ForegroundColor Green
} else {
    Write-Host "  Installation de .NET 8.0 SDK via winget..." -ForegroundColor Yellow
    & winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
    $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"
    $dotnetVer = & dotnet --version 2>$null
    Write-Host "  [OK] .NET SDK configuré : $dotnetVer" -ForegroundColor Green
}

# -------------------------------------------------------------------
# 3. Vérification et installation de Python 3.12
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[3/7] Vérification de Python 3.12..." -ForegroundColor Cyan
$pythonVer = $null
try { $pythonVer = & python --version 2>$null } catch {}

if ($pythonVer -and $pythonVer -match "3\.1[0-3]") {
    Write-Host "  [OK] Python détecté : $pythonVer" -ForegroundColor Green
} else {
    Write-Host "  Installation de Python 3.12 via winget..." -ForegroundColor Yellow
    & winget install Python.Python.3.12 --accept-source-agreements --accept-package-agreements
    
    # Mise à jour dynamique du PATH PowerShell
    $pyPaths = @(
        "$env:ProgramFiles\Python312",
        "$env:ProgramFiles\Python312\Scripts",
        "$env:LOCALAPPDATA\Programs\Python\Python312",
        "$env:LOCALAPPDATA\Programs\Python\Python312\Scripts"
    )
    foreach ($p in $pyPaths) {
        if ((Test-Path $p) -and ($env:PATH -notmatch [regex]::Escape($p))) {
            $env:PATH = "$p;$env:PATH"
        }
    }
    $pythonVer = & python --version 2>$null
    Write-Host "  [OK] Python configuré : $pythonVer" -ForegroundColor Green
}

# -------------------------------------------------------------------
# 4. Installation des bibliothèques Python d'IA
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[4/7] Installation des bibliothèques d'IA Python [requirements.txt]..." -ForegroundColor Cyan
$script:InstallProblems = New-Object System.Collections.Generic.List[string]
$isArm = ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") -or ($env:PROCESSOR_ARCHITEW6432 -eq "ARM64")
& python -m pip install --upgrade pip
& python -m pip install -r "$ScriptDir\requirements.txt"
if ($LASTEXITCODE -ne 0) {
    Write-Host "  [!] Échec de l'installation groupée : nouvelle tentative paquet par paquet..." -ForegroundColor Yellow
    foreach ($line in (Get-Content "$ScriptDir\requirements.txt")) {
        $pkg = ($line -split '#')[0].Trim()
        if ($pkg) { & python -m pip install $pkg 2>&1 | Out-Null }
    }
}

# rapidocr-onnxruntime installe 'onnxruntime' (CPU) qui écrase 'onnxruntime-directml' : on force DirectML sur x64
if (-not $isArm) {
    & python -m pip uninstall -y onnxruntime 2>&1 | Out-Null
    & python -m pip install --force-reinstall --no-deps onnxruntime-directml 2>&1 | Out-Null
}

# Vérification réelle des modules indispensables (au lieu de se fier au code retour de pip)
$mods = @("cv2","numpy","onnxruntime","fitz","rapidocr_onnxruntime","sounddevice","soundfile","huggingface_hub","ultralytics","psutil")
if (-not $isArm) { $mods += @("openvino","openvino_genai") }
$missing = @()
foreach ($m in $mods) {
    & python -c "import $m" 2>$null
    if ($LASTEXITCODE -ne 0) { $missing += $m }
}
if ($missing.Count -eq 0) {
    Write-Host "  [OK] Dépendances Python installées et vérifiées." -ForegroundColor Green
} else {
    Write-Host "  [!] Modules Python manquants : $($missing -join ', ')" -ForegroundColor Red
    $script:InstallProblems.Add("Modules Python manquants : $($missing -join ', ')")
}
if ($isArm) {
    Write-Host "  [INFO] Windows ARM64 : OpenVINO n'est pas disponible. Les démos vision/audio/OCR utiliseront ONNX Runtime (CPU/DirectML)." -ForegroundColor Yellow
    $script:InstallProblems.Add("Windows ARM64 : accélération OpenVINO indisponible (modes dégradés)")
}

# -------------------------------------------------------------------
# 5. Microsoft Foundry Local CLI et modèles LLM Phi-4
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[5/7] Vérification de Microsoft Foundry Local CLI et modèles LLM..." -ForegroundColor Cyan

$windowsApps = "$env:LOCALAPPDATA\Microsoft\WindowsApps"
$progFilesFoundry = "$env:ProgramFiles\Microsoft Foundry"
if (Test-Path "$windowsApps\foundry.exe") {
    $env:PATH = "$windowsApps;$env:PATH"
}
if (Test-Path "$progFilesFoundry\foundry.exe") {
    $env:PATH = "$progFilesFoundry;$env:PATH"
}

if (-not (Get-Command foundry -ErrorAction SilentlyContinue) -and -not (Test-Path "$windowsApps\foundry.exe")) {
    Write-Host "  Installation de Microsoft Foundry Local via winget..." -ForegroundColor Yellow
    & winget install Microsoft.FoundryLocal --accept-source-agreements --accept-package-agreements
    
    if (Test-Path "$windowsApps\foundry.exe") {
        $env:PATH = "$windowsApps;$env:PATH"
    }
    if (Test-Path "$progFilesFoundry\foundry.exe") {
        $env:PATH = "$progFilesFoundry;$env:PATH"
    }
}

$foundryReady = (Get-Command foundry -ErrorAction SilentlyContinue) -ne $null
if (-not $foundryReady -and (Test-Path "$windowsApps\foundry.exe")) {
    $foundryReady = $true
}

if ($foundryReady) {
    Write-Host "  [OK] Microsoft Foundry Local CLI est prêt." -ForegroundColor Green
    
    # Détection dynamique du processeur (Intel, AMD, Qualcomm)
    $procName = (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue).Name
    if (-not $procName) { $procName = $env:PROCESSOR_IDENTIFIER }
    Write-Host "  Matériel détecté : $procName" -ForegroundColor Cyan
    Write-Host "  Mise en cache des modèles Phi-4 pour Foundry Local..." -ForegroundColor Yellow

    # Modèle CPU universel (garantit 100% de fonctionnement sur toutes les machines)
    Write-Host "  - Téléchargement du modèle de repli universel [CPU]..."
    & foundry model download Phi-4-mini-instruct-generic-cpu

    if ($procName -match "Intel") {
        Write-Host "  - Téléchargement du modèle NPU [Intel AI Boost via OpenVINO]..."
        & foundry model download phi-4-mini-instruct-openvino-npu 2>$null
        Write-Host "  - Téléchargement du modèle GPU [Intel Arc Graphics via OpenVINO]..."
        & foundry model download phi-4-mini-instruct-openvino-gpu 2>$null
    }
    elseif ($procName -match "AMD" -or $procName -match "Ryzen") {
        Write-Host "  - Téléchargement du modèle accéléré DirectML [AMD Ryzen AI / Radeon]..."
        & foundry model download phi-4-mini-instruct-directml-npu 2>$null
    }
    elseif ($procName -match "Snapdragon" -or $procName -match "Qualcomm" -or $env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        Write-Host "  - Téléchargement du modèle NPU [Qualcomm Hexagon]..."
        & foundry model download phi-4-mini-instruct-qualcomm-npu 2>$null
        & foundry model download phi-4-mini-instruct-directml-npu 2>$null
    }
} else {
    Write-Host "  [ATTENTION] Commande 'foundry' indisponible dans ce terminal. Relancez la session après installation." -ForegroundColor Yellow
}

# -------------------------------------------------------------------
# 6. Téléchargement et initialisation de tous les modèles d'IA locaux
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[6/7] Initialisation et téléchargement de tous les modèles d'IA locaux [models/]..." -ForegroundColor Cyan
& python "$ScriptDir\scripts\download_models.py"
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Tous les modèles locaux sont prêts pour l'inférence." -ForegroundColor Green
} else {
    Write-Host "  [!] Des modèles d'IA sont manquants : certaines démos seront indisponibles." -ForegroundColor Red
    Write-Host "      Relancez .\Installer_Prerequis.ps1 (connexion Internet requise) ou : python scripts\download_models.py" -ForegroundColor Yellow
    $script:InstallProblems.Add("Modèles d'IA manquants (voir messages [INCOMPLET] ci-dessus)")
}

# -------------------------------------------------------------------
# 7. Compilation de l'application et diagnostic final
# -------------------------------------------------------------------
Write-Host ""
Write-Host "[7/7] Compilation de l'application en mode Release..." -ForegroundColor Cyan
Get-Process -Name "DemoHub" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

& dotnet build "$ScriptDir\DemoHub\DemoHub.csproj" -c Release
if ($LASTEXITCODE -eq 0) {
    Write-Host "  [OK] Application compilée avec succès dans DemoHub\bin\Release\" -ForegroundColor Green
} else {
    Write-Host "  [!] Erreur lors de la compilation .NET." -ForegroundColor Red
}

Write-Host ""
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "              Exécution du Diagnostic Automatique                     " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
& python "$ScriptDir\scripts\verify_installation.py"

Write-Host ""
if ($script:InstallProblems.Count -gt 0) {
    Write-Host "======================================================================" -ForegroundColor Yellow
    Write-Host "   Installation terminée AVEC DES PROBLÈMES - fonctionnalités limitées" -ForegroundColor Yellow
    foreach ($pb in $script:InstallProblems) { Write-Host "    - $pb" -ForegroundColor Yellow }
    Write-Host "   Corrigez puis relancez : .\Installer_Prerequis.ps1  (reprise sans tout retélécharger)" -ForegroundColor Yellow
    Write-Host "   Diagnostic détaillé : python scripts\verify_installation.py" -ForegroundColor Yellow
    Write-Host "======================================================================" -ForegroundColor Yellow
} else {
    Write-Host "======================================================================" -ForegroundColor Green
    Write-Host "   Installation et configuration terminées avec succès !              " -ForegroundColor Green
    Write-Host "   Pour lancer la démonstration, utilisez : .\Lancer_DemoHub.ps1       " -ForegroundColor White
    Write-Host "======================================================================" -ForegroundColor Green
}
Write-Host ""
pause
