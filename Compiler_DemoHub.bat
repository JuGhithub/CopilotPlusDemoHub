@echo off
title Copilot+ PC Demo - Compilation
cd /d "%~dp0"

set "PATH=%ProgramFiles%\dotnet;%LOCALAPPDATA%\Microsoft\WindowsApps;%PATH%"

echo ======================================================================
echo                Copilot+ PC Demo - Compilation .NET 8
echo ======================================================================
echo.

:: Verification de .NET SDK
dotnet --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [ERREUR] .NET SDK 8.0 est introuvable dans votre PATH.
    echo Veuillez executer 'Installer_Prerequis.bat' pour installer les prerequis.
    echo.
    pause
    exit /b 1
)

echo [1/3] Arret des processus de demonstration eventuellement en cours...
taskkill /F /IM DemoHub.exe >nul 2>&1

echo.
echo [2/3] Choisissez le mode de compilation :
echo   [1] Compilation Release (Recommande - Haute performance)
echo   [2] Compilation Debug (Mode developpement)
echo   [3] Publication complete autonome (Dossier exportable)
echo.
set /p MODE="Votre choix (1, 2 ou 3) [Defaut: 1] : "
if "%MODE%"=="" set MODE=1

echo.
echo [3/3] Execution de la compilation...

if "%MODE%"=="1" (
    echo Compilation en mode Release...
    dotnet build DemoHub\DemoHub.csproj -c Release
    if %ERRORLEVEL% equ 0 (
        echo.
        echo [SUCCES] Binaire genere dans :
        echo   DemoHub\bin\Release\net8.0-windows10.0.18362\DemoHub.exe
    ) else (
        echo [ERREUR] Echec de la compilation.
    )
) else if "%MODE%"=="2" (
    echo Compilation en mode Debug...
    dotnet build DemoHub\DemoHub.csproj -c Debug
    if %ERRORLEVEL% equ 0 (
        echo.
        echo [SUCCES] Binaire genere dans :
        echo   DemoHub\bin\Debug\net8.0-windows10.0.18362\DemoHub.exe
    ) else (
        echo [ERREUR] Echec de la compilation.
    )
) else if "%MODE%"=="3" (
    echo Publication autonome Release [x64]...
    dotnet publish DemoHub\DemoHub.csproj -c Release -r win-x64 --no-self-contained -o bin\Publish\x64
    if %ERRORLEVEL% equ 0 (
        echo.
        echo [SUCCES] Application publiee dans :
        echo   bin\Publish\x64\DemoHub.exe
    ) else (
        echo [ERREUR] Echec de la publication.
    )
) else (
    echo Choix non valide. Compilation Release par defaut...
    dotnet build DemoHub\DemoHub.csproj -c Release
)

echo.
pause
