@echo off
title Copilot+ PC Demo - Creation de Package Release
cd /d "%~dp0"

echo ======================================================================
echo       Copilot+ PC Demo - Creation de Package pour GitHub / Partage
echo ======================================================================
echo.
echo Choisissez le type de package a generer :
echo.
echo   [1] Package Application Seule (Recommande pour GitHub Releases)
echo       Inclut le binaire compile, les scripts, les micro-services Python et les donnees.
echo       Archive legere (~30 Mo) telechargeable en 2 secondes.
echo.
echo   [2] Package Complet Tout-en-Un (Avec modeles locaux)
echo       Inclut en plus tous les modeles OpenVINO et ONNX (~2 Go sans npu_cache).
echo       Ideal pour partage sur cle USB ou OneDrive d'equipe.
echo.
set /p CHOIX="Votre choix (1 ou 2) [Defaut: 1] : "
if "%CHOIX%"=="" set CHOIX=1

if "%CHOIX%"=="1" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\creer_package_release.ps1"
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\creer_package_release.ps1" -IncludeModels
)

echo.
pause
