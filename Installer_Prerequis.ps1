<#
.SYNOPSIS
    Copilot+ PC Demo Hub - Installation & Configuration Modulaire des Prérequis
.DESCRIPTION
    Installe et configure l'environnement local de démonstration de manière modulaire,
    isolée dans un environnement virtuel (.venv) et idempotente.
.PARAMETER CheckOnly
    Exécute uniquement le diagnostic d'installation complet sans rien modifier.
.PARAMETER InstallRuntime
    Installe les socles logiciels (.NET SDK, Python, Foundry, environnement virtuel et dépendances).
.PARAMETER DownloadModels
    Télécharge et vérifie l'intégrité cryptographique SHA-256 de tous les modèles d'IA locaux.
.PARAMETER BuildApplication
    Compile l'application WPF DemoHub en mode Release.
#>

[CmdletBinding()]
param(
    [switch]$CheckOnly = $false,
    [switch]$InstallRuntime = $false,
    [switch]$DownloadModels = $false,
    [switch]$BuildApplication = $false
)

$Host.UI.RawUI.WindowTitle = "Copilot+ PC Demo - Installation des Prérequis"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

# Si aucun commutateur spécifique n'est passé, on exécute toutes les étapes séquentielles
$RunAll = (-not $CheckOnly -and -not $InstallRuntime -and -not $DownloadModels -and -not $BuildApplication)

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "         Copilot+ PC Demo - Installation des Prérequis                " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

# -------------------------------------------------------------------
# Diagnostic seul
# -------------------------------------------------------------------
if ($CheckOnly) {
    Write-Host "[MODE] Exécution du diagnostic d'intégrité seul..." -ForegroundColor Yellow
    $py = if (Test-Path "$ScriptDir\.venv\Scripts\python.exe") { "$ScriptDir\.venv\Scripts\python.exe" } else { "python" }
    & $py "$ScriptDir\scripts\verify_installation.py"
    exit $LASTEXITCODE
}

# -------------------------------------------------------------------
# 0. Contrôle de l'espace disque disponible
# -------------------------------------------------------------------
$drive = (Get-Item $ScriptDir).PSDrive.Name
$freeSpaceGB = [math]::Round(((Get-PSDrive $drive).Free / 1GB), 1)
Write-Host "Disque $drive`: $freeSpaceGB Go libres disponibles." -ForegroundColor DarkGray
if ($freeSpaceGB -lt 12.0) {
    Write-Host "[ATTENTION] Moins de 12 Go disponibles ($freeSpaceGB Go). Le téléchargement des modèles SLM et diffusion requiert environ 15 Go." -ForegroundColor Yellow
}

# -------------------------------------------------------------------
# 1. Configuration système (LongPathsEnabled)
# -------------------------------------------------------------------
if ($RunAll -or $InstallRuntime) {
    Write-Host "`n[1/6] Vérification du support des chemins longs Windows..." -ForegroundColor Cyan
    try {
        $regKey = "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem"
        $currentVal = (Get-ItemProperty -Path $regKey -Name "LongPathsEnabled" -ErrorAction SilentlyContinue).LongPathsEnabled
        if ($currentVal -eq 1) {
            Write-Host "  [OK] Chemins longs déjà activés." -ForegroundColor Green
        } else {
            Set-ItemProperty -Path $regKey -Name "LongPathsEnabled" -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue
            Write-Host "  [OK] Chemins longs activés." -ForegroundColor Green
        }
    } catch {
        Write-Host "  [INFO] Modification de LongPathsEnabled ignorée (privilèges administrateur non requis si déjà actif)." -ForegroundColor DarkGray
    }

    # -------------------------------------------------------------------
    # 2. Runtimes .NET 8 et Python 3.12
    # -------------------------------------------------------------------
    Write-Host "`n[2/6] Vérification des socles logiciels (.NET 8 & Python 3.12)..." -ForegroundColor Cyan

    # .NET SDK
    $dotnetVer = $null
    try { $dotnetVer = & dotnet --version 2>$null } catch {}
    if ($dotnetVer -and $dotnetVer.StartsWith("8.")) {
        Write-Host "  [OK] .NET SDK détecté : $dotnetVer" -ForegroundColor Green
    } else {
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            Write-Host "  Installation de .NET 8.0 SDK via winget..." -ForegroundColor Yellow
            & winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
            $env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"
        } else {
            Write-Host "  [ATTENTION] dotnet 8.0 introuvable et winget indisponible." -ForegroundColor Yellow
        }
    }

    # Python
    $pythonVer = $null
    try { $pythonVer = & python --version 2>$null } catch {}
    if ($pythonVer -and $pythonVer -match "3\.1[0-3]") {
        Write-Host "  [OK] Python système détecté : $pythonVer" -ForegroundColor Green
    } else {
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            Write-Host "  Installation de Python 3.12 via winget..." -ForegroundColor Yellow
            & winget install Python.Python.3.12 --accept-source-agreements --accept-package-agreements
        }
    }

    # -------------------------------------------------------------------
    # 3. Isolation dans un environnement virtuel .venv
    # -------------------------------------------------------------------
    Write-Host "`n[3/6] Configuration de l'environnement virtuel (.venv) et dépendances..." -ForegroundColor Cyan
    $venvDir = "$ScriptDir\.venv"
    $venvPy = "$venvDir\Scripts\python.exe"

    if (-not (Test-Path $venvPy)) {
        Write-Host "  Création de l'environnement virtuel (.venv hermétique)..." -ForegroundColor Yellow
        & python -m venv "$venvDir"
        if ($LASTEXITCODE -ne 0) {
            throw "Échec de création du venv Python avec code $LASTEXITCODE"
        }
    }

    $pyExec = if (Test-Path $venvPy) { $venvPy } else { "python" }
    Write-Host "  [OK] Interpréteur Python actif : $pyExec" -ForegroundColor Green

    Write-Host "  Mise à niveau de pip et installation déterministe..." -ForegroundColor Yellow
    & $pyExec -m pip install --upgrade pip --quiet

    $isArm64 = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64
    $lockFile = if ($isArm64 -and (Test-Path "$ScriptDir\requirements-win-arm64.lock")) {
        "$ScriptDir\requirements-win-arm64.lock"
    } elseif (Test-Path "$ScriptDir\requirements-win-x64.lock") {
        "$ScriptDir\requirements-win-x64.lock"
    } elseif (Test-Path "$ScriptDir\requirements.lock") {
        "$ScriptDir\requirements.lock"
    } else {
        "$ScriptDir\requirements.txt"
    }

    Write-Host "  Architecture hôte : $(if ($isArm64) { 'ARM64' } else { 'x64' }) -> Dépendances : $(Split-Path $lockFile -Leaf)" -ForegroundColor DarkGray
    & $pyExec -m pip install -r "$lockFile" --quiet
    if ($LASTEXITCODE -ne 0) {
        throw "L'installation des dépendances Python depuis $(Split-Path $lockFile -Leaf) a échoué (code de sortie $LASTEXITCODE)."
    }

    # Foundry Local CLI
    if (Get-Command foundry -ErrorAction SilentlyContinue) {
        Write-Host "  [OK] Microsoft Foundry Local CLI détecté." -ForegroundColor Green
    } elseif (Get-Command winget -ErrorAction SilentlyContinue) {
        Write-Host "  Installation de Microsoft Foundry Local CLI via winget..." -ForegroundColor Yellow
        & winget install Microsoft.FoundryLocal --accept-source-agreements --accept-package-agreements
    }
}

# -------------------------------------------------------------------
# 4. Modèles LLM Microsoft Foundry
# -------------------------------------------------------------------
$pyExec = if (Test-Path "$ScriptDir\.venv\Scripts\python.exe") { "$ScriptDir\.venv\Scripts\python.exe" } else { "python" }

if ($RunAll -or $DownloadModels) {
    Write-Host "`n[4/6] Modèles SLM Microsoft Foundry Local (Phi-4)..." -ForegroundColor Cyan
    if (Get-Command foundry -ErrorAction SilentlyContinue) {
        Write-Host "  Téléchargement du modèle Phi-4 CPU..." -ForegroundColor DarkGray
        & foundry model download Phi-4-mini-instruct-generic-cpu 2>$null

        $procName = (Get-CimInstance Win32_Processor).Name
        if ($procName -match "Intel") {
            Write-Host "  Téléchargement du modèle Phi-4 NPU (Intel AI Boost)..." -ForegroundColor DarkGray
            & foundry model download phi-4-mini-instruct-openvino-npu 2>$null
            & foundry model download phi-4-mini-instruct-openvino-gpu 2>$null
        } else {
            & foundry model download phi-4-mini-instruct-directml-npu 2>$null
        }
    } else {
        Write-Host "  [INFO] foundry CLI non disponible dans ce sous-shell (étape ignorée)." -ForegroundColor DarkGray
    }

    # -------------------------------------------------------------------
    # 5. Modèles Locaux Embarqués (models/) & Vérification SHA-256
    # -------------------------------------------------------------------
    Write-Host "`n[5/6] Initialisation et vérification des modèles locaux (models/)..." -ForegroundColor Cyan
    & $pyExec "$ScriptDir\scripts\download_models.py"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [!] Certains modèles n'ont pas pu être téléchargés." -ForegroundColor Yellow
    }
}

# -------------------------------------------------------------------
# 6. Compilation de l'application et diagnostic final
# -------------------------------------------------------------------
if ($RunAll -or $BuildApplication) {
    Write-Host "`n[6/6] Compilation de l'application WPF DemoHub (.NET 8 Release)..." -ForegroundColor Cyan
    Get-Process -Name "DemoHub" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    & dotnet build "$ScriptDir\DemoHub\DemoHub.csproj" -c Release
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  [OK] Application compilée avec succès." -ForegroundColor Green
    } else {
        Write-Host "  [!] Erreur lors de la compilation .NET." -ForegroundColor Red
    }
}

# -------------------------------------------------------------------
# Diagnostic Final Automatique
# -------------------------------------------------------------------
if ($RunAll) {
    Write-Host "`n======================================================================" -ForegroundColor Cyan
    Write-Host "              Exécution du Diagnostic d'Intégrité Automatique          " -ForegroundColor Cyan
    Write-Host "======================================================================" -ForegroundColor Cyan
    & $pyExec "$ScriptDir\scripts\verify_installation.py"
    $diagExit = $LASTEXITCODE

    Write-Host "`n======================================================================" -ForegroundColor Green
    Write-Host "   Configuration terminée ! Lancez la démo avec : .\Lancer_DemoHub.ps1" -ForegroundColor Green
    Write-Host "======================================================================`n"
    exit $diagExit
}
