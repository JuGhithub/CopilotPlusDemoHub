@echo off
setlocal enabledelayedexpansion
title Copilot+ PC Demo - Installation des Prerequis
cd /d "%~dp0"

echo ======================================================================
echo          Copilot+ PC Demo - Installation des Prerequis
echo ======================================================================
echo.

:: -------------------------------------------------------------------
:: 0. Verification des droits et activation des chemins longs (LongPaths)
:: -------------------------------------------------------------------
echo [0/6] Optimisation du systeme de fichiers Windows...
reg query "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled 2>nul | find "0x1" >nul
if %ERRORLEVEL% equ 0 (
    echo   [OK] Support des chemins longs de fichiers deja active.
    goto :CHECK_WINGET
)

echo   Activation des chemins longs [requis pour PyTorch et OpenVINO]...
reg add "HKLM\SYSTEM\CurrentControlSet\Control\FileSystem" /v LongPathsEnabled /t REG_DWORD /d 1 /f >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo   [OK] Chemins longs actives avec succes.
) else (
    echo   [INFO] Pour activer les chemins longs de fichiers sans avertissement,
    echo          il est recommande de relancer ce script en tant qu'administrateur.
)

:: -------------------------------------------------------------------
:: 1. Verification de winget
:: -------------------------------------------------------------------
:CHECK_WINGET
echo.
echo [1/6] Verification du gestionnaire de paquets winget...
where winget >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo   [OK] Gestionnaire winget detecte.
    goto :CHECK_DOTNET
)

echo   [!] ERREUR: winget n'est pas disponible dans votre session.
echo       Assurez-vous d'utiliser Windows 11 avec le composant "Programme d'installation d'application"
echo       disponible sur le Microsoft Store.
pause
exit /b 1

:: -------------------------------------------------------------------
:: 2. Verification de .NET 8.0 SDK
:: -------------------------------------------------------------------
:CHECK_DOTNET
echo.
echo [2/6] Verification de .NET 8.0 SDK...
dotnet --version >nul 2>&1
if %ERRORLEVEL% equ 0 goto :DOTNET_OK

echo   Installation de .NET 8.0 SDK via winget...
winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements

:: Actualisation dynamique du PATH
for /f "tokens=2*" %%A in ('reg query "HKLM\System\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "SYS_PATH=%%B"
for /f "tokens=2*" %%A in ('reg query "HKCU\Environment" /v Path 2^>nul') do set "USER_PATH=%%B"
set "PATH=%ProgramFiles%\dotnet;!USER_PATH!;!SYS_PATH!;!PATH!"

:DOTNET_OK
for /f "tokens=*" %%V in ('dotnet --version 2^>nul') do (
    echo   [OK] .NET SDK detecte : %%V
)

:: -------------------------------------------------------------------
:: 3. Verification de Python 3.12
:: -------------------------------------------------------------------
:CHECK_PYTHON
echo.
echo [3/6] Verification de Python 3.12...
python --version >nul 2>&1
if %ERRORLEVEL% equ 0 goto :PYTHON_OK

echo   Installation de Python 3.12 via winget...
winget install Python.Python.3.12 --accept-source-agreements --accept-package-agreements

:: Actualisation dynamique du PATH
for /f "tokens=2*" %%A in ('reg query "HKLM\System\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "SYS_PATH=%%B"
for /f "tokens=2*" %%A in ('reg query "HKCU\Environment" /v Path 2^>nul') do set "USER_PATH=%%B"
set "PATH=%ProgramFiles%\Python312;%ProgramFiles%\Python312\Scripts;%LOCALAPPDATA%\Programs\Python\Python312;%LOCALAPPDATA%\Programs\Python\Python312\Scripts;!USER_PATH!;!SYS_PATH!;!PATH!"

:PYTHON_OK
for /f "tokens=*" %%V in ('python --version 2^>nul') do (
    echo   [OK] Python detecte : %%V
)

:: Re-verification finale de python
python --version >nul 2>&1
if %ERRORLEVEL% equ 0 goto :INSTALL_PIP

echo   [!] AVERTISSEMENT : Python vient d'etre installe mais n'est pas encore visible dans ce terminal.
echo       Veuillez fermer cette fenetre et la relancer pour completer les etapes suivantes.
pause
exit /b 0

:: -------------------------------------------------------------------
:: 4. Installation des packages Python (OpenVINO, PyMuPDF, Torch, YOLO, etc.)
:: -------------------------------------------------------------------
:INSTALL_PIP
echo.
echo [4/6] Installation des bibliotheques d'IA Python [requirements.txt]...
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
if %ERRORLEVEL% neq 0 (
    echo   [!] Erreur lors de l'installation des dependances pip.
) else (
    echo   [OK] Dependances Python installees avec succes.
)

:: -------------------------------------------------------------------
:: 5. Verification de Microsoft Foundry Local CLI
:: -------------------------------------------------------------------
:CHECK_FOUNDRY
echo.
echo [5/6] Verification de Microsoft Foundry Local CLI...
set "PATH=%LOCALAPPDATA%\Microsoft\WindowsApps;!PATH!"
where foundry >nul 2>&1
if %ERRORLEVEL% equ 0 (
    echo   [OK] Microsoft Foundry Local CLI est deja installe.
    goto :DOWNLOAD_MODELS
)

echo   Installation de Microsoft Foundry Local via winget...
winget install Microsoft.FoundryLocal --accept-source-agreements --accept-package-agreements
set "PATH=%LOCALAPPDATA%\Microsoft\WindowsApps;!PATH!"

:DOWNLOAD_MODELS
echo.
echo   Mise en cache des modeles d'IA Phi-4 pour Foundry Local...
echo   - Telechargement du modele NPU [Intel AI Boost]...
call foundry model download phi-4-mini-instruct-openvino-npu
echo   - Telechargement du modele GPU [Intel Arc Graphics]...
call foundry model download phi-4-mini-instruct-openvino-gpu
echo   - Telechargement du modele CPU [Generique]...
call foundry model download Phi-4-mini-instruct-generic-cpu

:: -------------------------------------------------------------------
:: 6. Compilation du projet et diagnostic final
:: -------------------------------------------------------------------
:BUILD_PROJECT
echo.
echo [6/6] Compilation de l'application en mode Release...
taskkill /F /IM DemoHub.exe >nul 2>&1
dotnet build DemoHub\DemoHub.csproj -c Release
if %ERRORLEVEL% neq 0 (
    echo   [!] Erreur lors de la compilation .NET.
) else (
    echo   [OK] Application compilee avec succes dans DemoHub\bin\Release\
)

echo.
echo ======================================================================
echo              Execution du Diagnostic Automatique
echo ======================================================================
python scripts\verify_installation.py

echo.
echo ======================================================================
echo    Installation et configuration terminees !
echo    Pour lancer la demonstration, utilisez : Lancer_DemoHub.bat
echo ======================================================================
echo.
pause
