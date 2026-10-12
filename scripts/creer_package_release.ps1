<#
.SYNOPSIS
    Génère un package autonome (archive ZIP) du Copilot+ Demo Hub prêt pour GitHub Releases ou partage collègues.
#>

param(
    [switch]$IncludeModels = $false,
    [string]$OutputDir = "Releases"
)

$ErrorActionPreference = "Stop"

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "         Copilot+ Demo Hub - Génération du Package de Release          " -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host ""

$RootPath = (Get-Item -Path $PSScriptRoot\..).FullName
$Timestamp = Get-Date -Format "yyyyMMdd_HHmm"
$TargetDir = Join-Path $RootPath $OutputDir

if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir | Out-Null
}

$TempStaging = Join-Path $TargetDir "staging_demo_hub"
if (Test-Path $TempStaging) {
    Remove-Item -Recurse -Force $TempStaging
}
New-Item -ItemType Directory -Path $TempStaging | Out-Null

# 1. Compilation & Publication .NET
Write-Host "[1/4] Publication de l'application WPF (.NET 8 Release)..." -ForegroundColor Yellow
$PublishOut = Join-Path $TempStaging "bin\Publish\x64"
dotnet publish "$RootPath\DemoHub\DemoHub.csproj" -c Release -r win-x64 --no-self-contained -o "$PublishOut"
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERREUR] Échec de la compilation/publication .NET." -ForegroundColor Red
    exit 1
}

# 2. Copie des fichiers essentiels
Write-Host "[2/4] Copie des micro-services, scripts et jeux de données..." -ForegroundColor Yellow

$ItemsToCopy = @(
    "Data",
    "Config",
    "Prompts",
    "scripts",
    "avatar_service.py",
    "eraser_service.py",
    "live_transcribe_service.py",
    "olive_lab_service.py",
    "olive_mcp_service.py",
    "pdf_ocr_service.py",
    "record_service.py",
    "runtime_benchmark_service.py",
    "security_access_service.py",
    "speaker_service.py",
    "transcribe_service.py",
    "requirements.txt",
    "GUIDE_INSTALLATION.md",
    "README.md",
    "Installer_Prerequis.bat",
    "Lancer_DemoHub.bat",
    "beach_sample.jpg",
    "capteur_radar_adas.jpg",
    "choc_arriere_suv.jpg",
    "desk_sample.jpg",
    "portrait_femme.jpg",
    "portrait_homme.jpg",
    "sample_all_three.jpg",
    "sample_none.jpg"
)

foreach ($item in $ItemsToCopy) {
    $src = Join-Path $RootPath $item
    if (Test-Path $src) {
        $dest = Join-Path $TempStaging $item
        Copy-Item -Path $src -Destination $dest -Recurse -Force
    }
}

# 3. Inclusion optionnelle des modèles
if ($IncludeModels) {
    Write-Host "[3/4] Inclusion des modèles locaux (models/)..." -ForegroundColor Yellow
    $srcModels = Join-Path $RootPath "models"
    if (Test-Path $srcModels) {
        $destModels = Join-Path $TempStaging "models"
        # Exclure le cache NPU spécifique à la machine locale pour alléger l'archive
        New-Item -ItemType Directory -Path $destModels | Out-Null
        Get-ChildItem -Path $srcModels | Where-Object { $_.Name -ne "npu_cache" } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination (Join-Path $destModels $_.Name) -Recurse -Force
        }
    }
} else {
    Write-Host "[3/4] Modèles exclus du package principal (archive allégée pour GitHub)..." -ForegroundColor DarkGray
}

# 4. Compression ZIP
$ZipName = if ($IncludeModels) { "CopilotPlusDemoHub_Full_v1.0.zip" } else { "CopilotPlusDemoHub_App_v1.0.zip" }
$ZipPath = Join-Path $TargetDir $ZipName

Write-Host "[4/4] Création de l'archive ZIP : $ZipName..." -ForegroundColor Yellow
if (Test-Path $ZipPath) {
    Remove-Item -Force $ZipPath
}

Compress-Archive -Path "$TempStaging\*" -DestinationPath $ZipPath -CompressionLevel Optimal

# Nettoyage
Remove-Item -Recurse -Force $TempStaging

$SizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
Write-Host ""
Write-Host "======================================================================" -ForegroundColor Green
Write-Host " [SUCCÈS] Package généré avec succès !" -ForegroundColor Green
Write-Host " Emplacement : $ZipPath" -ForegroundColor White
Write-Host " Taille : $SizeMB Mo" -ForegroundColor White
Write-Host " Prêt à être attaché à une Release GitHub !" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Green
