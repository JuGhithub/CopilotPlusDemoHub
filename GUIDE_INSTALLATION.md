# 🚀 Guide d'Installation Complet sur un Autre PC — Copilot+ PC Demo (Multi-Constructeurs)

🌐 **Langue / Language** : **[ 🇫🇷 Français (Actif) ](GUIDE_INSTALLATION.md)** | **[ 🇬🇧 English Installation Guide ](INSTALLATION_GUIDE.md)**  
📖 **Documentation Principale** : **[ 🇫🇷 README en Français ](README.fr.md)** | **[ 🇬🇧 README in English ](README.md)**

Ce guide détaille l'intégralité des prérequis matériels, logiciels, modèles d'IA et pilotes nécessaires pour installer et faire fonctionner **Copilot+ PC Demo** sur n'importe quel PC Copilot+ (**Intel**, **AMD**, ou **Qualcomm Snapdragon**) en mode 100% autonome et local.

---

## 1. 💻 Prérequis Matériels & Silicium Supporté

L'application intègre une détection automatique ([`SiliconDetector.cs`](DemoHub/Core/SiliconDetector.cs)) et supporte les architectures Copilot+ PC :

| Constructeur | Processeur | NPU Dédié (40+ TOPS) | GPU & Accélération | Mode d'Inférence Recommandé |
| :--- | :--- | :--- | :--- | :--- |
| **Intel** (Référence) | Core Ultra Série 2 (Lunar Lake 258V/268V/288V) ou Série 1 | **Intel AI Boost (48 TOPS)** | Intel Arc Graphics (140V Xe2) | OpenVINO IR natif (100% NPU) + DirectML |
| **AMD** (Hybride) | Ryzen AI Série 300 (ex: AI 9 HX 370 / Strix Point) | **AMD Ryzen AI / XDNA 2 (50 TOPS)** | AMD Radeon 890M / 880M | ONNX DirectML + Olive FP16 / Fallback CPU |
| **Qualcomm** (Expérimental) | Snapdragon X Elite / Plus (X1E-80, X1P / ARM64) | **Qualcomm Hexagon (45 TOPS)** | Qualcomm Adreno GPU | ONNX DirectML / QNN + Olive FP16 / Binaire win-arm64 |
| **Universel** | Tout PC Windows 11 64-bit | Émulation logicielle CPU | GPU DirectX 12 compatible | Fallback CPU AVX-512 / ARM64 |

- **Mémoire Vive (RAM)** : **32 Go LPDDR5X** recommandé (16 Go minimum).
- **Stockage** : **SSD NVMe avec 15 à 20 Go libres** pour héberger les modèles d'IA locaux.
- **Périphériques** : Webcam intégrée ou USB + Microphone.
- **Système d'exploitation** : **Windows 11 64-bit version 24H2 (Build 26100+)**.

> 💡 **Pilotes constructeurs recommandés selon votre machine** :
> - **PC Intel** : Installez le dernier pilote NPU VPUX et le pilote Intel Arc via l'[Assistant Pilotes Intel (IDSA)](https://www.intel.fr/content/www/fr/fr/support/detect.html).
> - **PC AMD** : Installez les pilotes AMD Adrenalin et le pilote AMD NPU IPU via l'[AMD Software Installer](https://www.amd.com/fr/support/download/drivers.html).
> - **PC Qualcomm** : Assurez-vous que les mises à jour Windows Update de votre fabricant (Surface, Dell, Lenovo, ASUS...) sont appliquées pour le NPU Hexagon et le GPU Adreno.
> - **Fallback universel contrôlé** : Si aucun NPU n'est présent ou activé pour un workload donné, le Demo Hub bascule automatiquement sur le GPU DirectML puis sur le CPU sans aucun plantage.

---

## 2. ⚡ Méthode 1 : Installation Automatique en 1 Clic (Recommandée)

Si vous clonez le dépôt Git ou copiez le dossier du projet sur un nouveau PC :

1. Ouvrez le dossier du projet dans un terminal **PowerShell** (ou l'Explorateur Windows).
2. Lancez le script PowerShell d'installation :
   ```powershell
   .\Installer_Prerequis.ps1
   ```
   *(Ou double-cliquez sur `Installer_Prerequis.bat`)*
3. Le script effectue automatiquement et séquentiellement toutes les étapes :
   - Activation du support des chemins longs Windows (`LongPathsEnabled`) pour PyTorch et OpenVINO.
   - Vérification et installation de **.NET 8.0 SDK** (via `winget`).
   - Vérification et installation de **Python 3.12** (via `winget`) avec rechargement dynamique du `PATH`.
   - **Isolation stricte de l'environnement Python** : création automatique d'un environnement virtuel `.venv` (`python -m venv .venv --system-site-packages`).
   - Installation déterministe des dépendances d'IA verrouillées dans [`requirements.lock`](requirements.lock) (OpenVINO, PyMuPDF, Torch, YOLO, Olive, RapidOCR, etc.).
   - Installation de **Microsoft Foundry Local CLI** (via `winget`).
   - Téléchargement et mise en cache des variantes du modèle **Phi-4-mini** (NPU, GPU, CPU).
   - **Téléchargement et vérification des modèles locaux** (`models/`) via [`scripts/download_models.py`](scripts/download_models.py) : Whisper (STT Tiny & Base OpenVINO), Biométrie vocale VoxCeleb (ONNX), Vision YOLOv8s-World, Segmentation YOLOv8n-Seg et Studio Avatar LCM DreamShaper.
   - Contrôle d'intégrité cryptographique SHA-256 de la chaîne d'approvisionnement des modèles.
   - Compilation initiale de l'application WPF `DemoHub.exe` en mode Release.
   - **Diagnostic de santé et smoke tests en direct** via `scripts\verify_installation.py`.
4. Une fois terminé, lancez la démo :
   ```powershell
   .\Lancer_DemoHub.ps1
   ```
   *(Ou double-cliquez sur `Lancer_DemoHub.bat`)*

---

## 3. 🔍 Diagnostic & Tests Automatisés

Le projet inclut un outillage complet pour auditer et valider le système :

### 1. Diagnostic Complet & Smoke Tests d'Inférence ([`scripts/verify_installation.py`](scripts/verify_installation.py))
```powershell
# Exécuter le diagnostic via l'environnement virtuel
.venv\Scripts\python.exe scripts\verify_installation.py
```
Le diagnostic valide 8 axes opérationnels :
1. **Système & Windows** : Version de Windows 11, statut du registre `LongPathsEnabled`.
2. **Runtimes** : Détection exacte de Python (>= 3.10), .NET SDK 8.0 et Microsoft Foundry Local.
3. **Moteurs d'IA** : Détection matérielle d'OpenVINO (NPU Intel AI Boost, GPU Arc, CPU) et DirectML.
4. **Packages Python** : Importation et compatibilité de toutes les bibliothèques requises.
5. **Modèles Locaux (`models/`)** : Présence et intégrité de tous les poids neuronaux.
6. **Périphériques Multimédia** : Détection du microphone actif et flux webcam en direct.
7. **Binaires de l'Application** : Vérification des exécutables compilés (`bin/Release`, `bin/Publish`, `bin/Debug`).
8. **Smoke Tests d'Inférence en Direct** :
   - Test réel du coffre-fort biométrique Windows DPAPI matériel (`CryptProtectData`/`CryptUnprotectData`).
   - Test réel d'inférence DirectML sur GPU.
   - Test réel d'inférence native 100% NPU Intel AI Boost.

### 2. Contrôle Supply-Chain & Intégrité Cryptographique des Modèles
```powershell
.venv\Scripts\python.exe scripts\download_models.py --verify-integrity
```
Calcule et valide l'empreinte SHA-256 de chaque modèle local critique pour prévenir toute altération ou corruption de poids.

### 3. Exécution de la Suite de Tests Automatisée
Le projet intègre une suite de tests unitaires et d'intégration validée en CI GitHub Actions :
```powershell
# Tests unitaires C# WPF (8 tests : ProcessRunner, télémétrie, détection silicium)
dotnet test DemoHub.Tests\DemoHub.Tests.csproj -r win-x64

# Tests unitaires Python (9 tests : coffre DPAPI, détection falsification, utilitaires langues FR/EN)
.venv\Scripts\python.exe -m pytest tests/ -v
```

---

## 4. 🛠️ Méthode 2 : Installation Manuelle Pas à Pas

Si vous préférez installer chaque composant manuellement :

### Étape 1 : Socles Logiciels Système (via Winget)
```powershell
# 1. SDK .NET 8.0 (Application C# WPF)
winget install Microsoft.DotNet.SDK.8

# 2. Python 3.12 (Micro-services vision, audio, OCR)
winget install Python.Python.3.12

# 3. Microsoft Foundry Local CLI (LLM local officiel Microsoft)
winget install Microsoft.FoundryLocal
```

---

### Étape 2 : Environnement Virtuel Python & Dépendances Verrouillées
Dans le dossier racine du projet :
```powershell
# 1. Création du venv avec accès aux packages système
python -m venv .venv --system-site-packages

# 2. Activation de l'environnement virtuel
.\.venv\Scripts\Activate.ps1

# 3. Installation déterministe selon l'architecture du PC :
python -m pip install --upgrade pip

# Pour PC x64 (Intel Core Ultra / AMD Ryzen AI) :
python -m pip install -r requirements-win-x64.lock

# Pour PC ARM64 (Qualcomm Snapdragon X Elite / Plus) :
python -m pip install -r requirements-win-arm64.lock
```

---

### Étape 3 : Téléchargement des Modèles LLM Microsoft Foundry Local
```powershell
# Variante NPU (Intel AI Boost via OpenVINO - 2.2 Go)
foundry model download phi-4-mini-instruct-openvino-npu

# Variante GPU (Intel Arc via OpenVINO - 2.2 Go)
foundry model download phi-4-mini-instruct-openvino-gpu

# Variante CPU (Fallback universel - 4.8 Go)
foundry model download Phi-4-mini-instruct-generic-cpu

# Vérification du cache local
foundry status
```

---

### Étape 4 : Modèles Locaux Embarqués dans le Projet
```powershell
python scripts\download_models.py
```
Le dossier [`models/`](models/) sera peuplé avec :
1. **`models/yolov8s-world_openvino_model/`** : Détection visuelle temps réel (tasse, badge, casque, lunettes, personne).
2. **`models/yolov8n-seg_openvino_model/`** : Segmentation spatiale pour la gomme magique.
3. **`models/whisper-tiny-fp16-ov/` & `models/whisper-base-fp16-ov/`** : Transcription Speech-to-Text en français & anglais.
4. **`models/speaker/`** : Réseau neuronal de reconnaissance biométrique vocale du locuteur.
5. **`models/lcm_dreamshaper_8bit/`** : Modèle de diffusion d'images rapide (Latent Consistency Model).
6. **`models/npu_cache/`** : Blobs binaires compilés pour le NPU Intel pour un démarrage immédiat à froid.

---

### Étape 5 : Compilation et Publication Multi-Plateforme

```powershell
# Compilation Release haute performance (x64)
dotnet build DemoHub\DemoHub.csproj -c Release

# Publication autonome pour PC Intel / AMD (win-x64)
dotnet publish DemoHub\DemoHub.csproj -c Release -r win-x64 --no-self-contained -o bin\Publish\x64

# Publication autonome native pour PC Snapdragon X (win-arm64)
dotnet publish DemoHub\DemoHub.csproj -c Release -r win-arm64 --no-self-contained -o bin\Publish\arm64
```

Génération d'un package autonome prêt pour le partage :
```powershell
# Package pour PC x64 (Intel Core Ultra / AMD Ryzen AI)
.\scripts\creer_package_release.ps1 -Runtime win-x64

# Package pour PC ARM64 (Qualcomm Snapdragon X Elite / Plus)
.\scripts\creer_package_release.ps1 -Runtime win-arm64
```

Puis lancez la démonstration :
- Soit via le lanceur optimisé : [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat).
- Soit directement : `dotnet run --project DemoHub\DemoHub.csproj -c Release`

---

## 5. 🧰 Diagnostic & Résolution des Problèmes Fréquents

### 1. Le support des chemins longs est désactivé
- **Symptôme** : Erreur `FileNotFoundError` ou chemin tronqué lors de l'installation de PyTorch ou OpenVINO.
- **Solution** : Ouvrez PowerShell en tant qu'administrateur et exécutez :
  ```powershell
  Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1
  ```

### 2. `foundry` n'est pas reconnu en ligne de commande
- **Cause** : `winget` installe l'exécutable dans `%LOCALAPPDATA%\Microsoft\WindowsApps`.
- **Solution** : Fermez et rouvrez votre invite de commande, ou utilisez directement [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat) qui configure automatiquement le `PATH`.

### 3. Le NPU reste à 0% dans le Gestionnaire des tâches
- **Cause** : Le pilote Intel NPU n'est pas actif ou la charge est exécutée sur GPU/CPU.
- **Solution** : Ouvrez le *Gestionnaire de périphériques > Périphériques de calcul* (ou *Intel(R) AI Boost*), faites un clic droit > *Mettre à jour le pilote*. Vérifiez également que l'onglet NPU apparaît bien dans *Gestionnaire des tâches > Performances*.

### 4. La webcam ne s'ouvre pas dans la démo Contrôle d'Accès
- **Cause** : Autorisation caméra Windows désactivée ou webcam occupée par Teams/Zoom.
- **Solution** : Allez dans *Paramètres Windows > Confidentialité et sécurité > Caméra* et vérifiez que l'accès aux applications de bureau est activé. Fermez Teams/Zoom.

### 5. Erreur « Code 1 » dans la démo Studio Avatar
- **Cause** : Le modèle LCM DreamShaper n'est pas téléchargé dans `models/lcm_dreamshaper_8bit` (non inclus dans git car > 1 Go) ou la bibliothèque `openvino-genai` n'est pas installée.
- **Solution** : Exécutez simplement `python scripts\download_models.py` puis vérifiez avec `python scripts\download_models.py --verify-integrity`.

---

## 6. 📦 Synthèse pour une Clé USB de Déploiement

Pour transporter la démo sur un autre PC sur une clé USB ou un disque externe :
1. Copiez **tout le dossier** `CopilotPlusDemoHub` (qui contient déjà le code source, les modèles `models/`, les fichiers échantillons `Data/` et les scripts `.bat`).
2. Sur le PC cible, branchez la clé USB et copiez le dossier sur le SSD (ex: `C:\Demos\CopilotPlusDemo`).
3. Double-cliquez sur `Installer_Prerequis.bat` (en clic droit > *Exécuter en tant qu'administrateur*).
4. Double-cliquez sur `Lancer_DemoHub.bat`.
