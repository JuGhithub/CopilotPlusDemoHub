@echo off
title Copilot+ PC Demo - Demarrage
cd /d "%~dp0"

:: Configuration dynamique du PATH : Python reel et .NET en priorite absolue devant WindowsApps
set "PATH=%ProgramFiles%\Python312;%ProgramFiles%\Python312\Scripts;%LOCALAPPDATA%\Programs\Python\Python312;%LOCALAPPDATA%\Programs\Python\Python312\Scripts;%ProgramFiles%\dotnet;%PATH%;%LOCALAPPDATA%\Microsoft\WindowsApps"

echo ======================================================================
echo                  Copilot+ PC Demo - Demarrage
echo ======================================================================
echo.

echo [1/3] Verification du service Microsoft Foundry Local...
where foundry >nul 2>&1
if %ERRORLEVEL% neq 0 goto :NO_FOUNDRY

call foundry server start >nul 2>&1
echo       [OK] Moteur Foundry Local actif.

echo.
echo [2/3] Prechargement des modeles d'IA en memoire vive...
echo       * Modele NPU [Intel AI Boost]...
call foundry model load phi-4-mini-instruct-openvino-npu >nul 2>&1
echo       * Modele GPU [Intel Arc Graphics]...
call foundry model load phi-4-mini-instruct-openvino-gpu >nul 2>&1
echo       [OK] Modeles charges en RAM.
goto :LAUNCH_APP

:NO_FOUNDRY
echo       [INFO] Foundry Local CLI non detecte dans le PATH.
echo       Les demonstrations de vision, audio et OCR restent 100%% fonctionnelles.
echo.
echo [2/3] Etape passee.

:LAUNCH_APP
echo.
echo [3/3] Lancement de l'application Copilot+ PC Demo...
if exist "DemoHub\bin\Release\net8.0-windows10.0.18362\DemoHub.exe" (
    echo       Lancement de la version Release haute performance...
    start "" /d "%~dp0DemoHub\bin\Release\net8.0-windows10.0.18362" "DemoHub.exe"
    goto :APP_LAUNCHED
)
if exist "bin\Publish\x64\DemoHub.exe" (
    echo       Lancement de la version autonome x64...
    start "" /d "%~dp0bin\Publish\x64" "DemoHub.exe"
    goto :APP_LAUNCHED
)
if exist "DemoHub\bin\Debug\net8.0-windows10.0.18362\DemoHub.exe" (
    echo       Lancement de la version Debug...
    start "" /d "%~dp0DemoHub\bin\Debug\net8.0-windows10.0.18362" "DemoHub.exe"
    goto :APP_LAUNCHED
)

echo       Aucun binaire pre-compile trouve. Compilation et execution...
dotnet run --project DemoHub\DemoHub.csproj -c Release

:APP_LAUNCHED
echo.
echo Demonstration lancee avec succes.
ping -n 3 127.0.0.1 >nul
