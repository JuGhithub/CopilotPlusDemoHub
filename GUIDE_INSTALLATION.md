# 🚀 Guide d'Installation Complet sur un Autre PC — Copilot+ PC Demo

Ce guide détaille l'intégralité des prérequis matériels, logiciels, modèles d'IA et pilotes nécessaires pour installer et faire fonctionner **Copilot+ PC Demo** sur un nouveau PC en mode 100% autonome et local.

---

## 1. 💻 Prérequis Matériels & Système

| Composant | Spécification Recommandée | Spécification Minimale |
| :--- | :--- | :--- |
| **Processeur** | Intel Core Ultra Série 2 (Lunar Lake ex: 258V / 268V / 288V) | Intel Core Ultra Série 1 (Meteor Lake / Arrow Lake) ou Snapdragon X |
| **NPU (Accélérateur IA)** | **Intel AI Boost (40 à 48 TOPS)** | NPU >= 40 TOPS (Label Copilot+ PC) |
| **GPU** | Intel Arc Graphics (Xe2 intégrée) | GPU compatible DirectX 12 / OpenVINO / DirectML |
| **Mémoire Vive (RAM)** | **32 Go LPDDR5X** | 16 Go |
| **Stockage** | **SSD NVMe avec 20 Go libres** | 15 Go libres |
| **Périphériques** | Webcam intégrée ou USB + Microphone | Webcam & Micro fonctionnels |
| **Système d'exploitation**| **Windows 11 64-bit version 24H2 (Build 26100+)** | Windows 11 23H2 avec mise à jour Copilot+ |

> ⚠️ **Pilotes Intel officiels recommandés** :
> Pour bénéficier de l'accélération matérielle maximale sur le NPU et le GPU, installez les derniers pilotes via l'[Assistant pilotes et support Intel (IDSA)](https://www.intel.fr/content/www/fr/fr/support/detect.html) :
> - **Pilote Intel NPU Compute Driver** (VPUX version 32.0.100 ou supérieure)
> - **Pilote graphique Intel Arc & Iris Xe Graphics**

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
   - Mise à jour de `pip` et installation de toutes les dépendances d'IA listées dans `requirements.txt` (OpenVINO, PyMuPDF, Torch, YOLO, Olive, etc.).
   - Installation de **Microsoft Foundry Local CLI** (via `winget`).
   - Téléchargement et mise en cache des variantes du modèle **Phi-4-mini** (NPU, GPU, CPU).
   - **Téléchargement automatique de 100% des modèles d'IA locaux** (`models/`) via [`scripts/download_models.py`](scripts/download_models.py) : Whisper (STT Tiny & Base OpenVINO), Biométrie vocale VoxCeleb (ONNX), Vision YOLOv8s-World, Segmentation YOLOv8n-Seg et Studio Avatar LCM DreamShaper.
   - Compilation initiale de l'application WPF `DemoHub.exe` en mode Release.
   - **Diagnostic de santé automatique** via `scripts\verify_installation.py`.
4. Une fois terminé, lancez la démo :
   ```powershell
   .\Lancer_DemoHub.ps1
   ```
   *(Ou double-cliquez sur `Lancer_DemoHub.bat`)*

---

## 3. 🔍 Diagnostic & Vérification Automatisée de l'Environnement

Le projet inclut un script d'audit complet de l'installation : [`scripts/verify_installation.py`](scripts/verify_installation.py).

Vous pouvez le lancer à tout moment dans un terminal :
```powershell
python scripts\verify_installation.py
```

### Ce que vérifie le diagnostic :
1. **Système & Windows** : Version de Windows 11, statut du registre `LongPathsEnabled`.
2. **Runtimes** : Détection exacte de Python (>= 3.10), .NET SDK 8.0 et Microsoft Foundry Local.
3. **Moteurs d'IA** : Détection matérielle d'OpenVINO (NPU Intel AI Boost, GPU Arc, CPU) et DirectML.
4. **Packages Python** : Importation et compatibilité de toutes les bibliothèques requises.
5. **Modèles Locaux (`models/`)** : Présence et intégrité de tous les sous-dossiers et fichiers de poids neuronaux.
6. **Périphériques Multimédia** : Détection du microphone actif et flux webcam en direct.
7. **Binaires de l'Application** : Vérification des exécutables compilés (`bin/Release`, `bin/Publish`, `bin/Debug`).

---

## 4. 🛠️ Méthode 2 : Installation Manuelle Pas à Pas

Si vous préférez installer chaque composant manuellement :

### Étape 1 : Socles Logiciels Système (via Winget)

Ouvrez un terminal **PowerShell** ou **Invite de commandes** :

```powershell
# 1. SDK .NET 8.0 (Application C# WPF)
winget install Microsoft.DotNet.SDK.8

# 2. Python 3.12 (Micro-services vision, audio, OCR)
winget install Python.Python.3.12

# 3. Microsoft Foundry Local CLI (LLM local officiel Microsoft)
winget install Microsoft.FoundryLocal
```

---

### Étape 2 : Dépendances Python (IA & Traitement Signal)

Dans le dossier racine du projet, exécutez :

```powershell
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
```

#### Liste des packages clés :
- `openvino` & `openvino-genai` & `openvino-tokenizers` : Exécution neuronale ultra-rapide sur NPU Intel AI Boost et GPU Arc.
- `ultralytics` : Modèles de vision par ordinateur YOLOv8-World (détection ouverte) et YOLOv8-Seg (segmentation).
- `torch` & `torchvision` : Backend de calcul tensoriel local.
- `opencv-python` & `pillow` : Traitement d'image et capture flux webcam temps réel.
- `sounddevice` & `soundfile` : Capture microphone et lecture audio faible latence.
- `pymupdf` : Moteur vectoriel d'analyse et d'édition de documents PDF.
- `rapidocr-onnxruntime` : Extraction OCR locale haute précision pour documents scannés.
- `onnxruntime-directml` : Moteur d'inférence ONNX accéléré par DirectX 12 / DirectML.
- `olive-ai` : Framework Microsoft Olive pour la quantification et l'optimisation continue des modèles ONNX.
- `transformers` & `scipy` : Traitement du langage naturel et biométrie vocale.

---

### Étape 3 : Téléchargement des Modèles LLM Microsoft Foundry Local

Ces modèles sont gérés par le service local de Microsoft (`foundry`) :

```powershell
# Variante NPU (Compilée pour Intel AI Boost via OpenVINO - 2.2 Go)
foundry model download phi-4-mini-instruct-openvino-npu

# Variante GPU (Optimisée pour iGPU Intel Arc via OpenVINO - 2.2 Go)
foundry model download phi-4-mini-instruct-openvino-gpu

# Variante CPU (Fallback universel - 4.8 Go)
foundry model download Phi-4-mini-instruct-generic-cpu
```

Vérifiez que les modèles sont en cache :
```powershell
foundry status
```

---

### Étape 4 : Modèles Locaux Embarqués dans le Projet

Le dossier [`models/`](models/) contient tous les modèles pré-convertis et quantifiés pour l'architecture Intel OpenVINO :

1. **`models/yolov8s-world_openvino_model/`** : Détection visuelle temps réel (tasse, badge, casque, lunettes, personne).
2. **`models/yolov8n-seg_openvino_model/`** : Segmentation spatiale pour la gomme magique.
3. **`models/whisper-tiny-fp16-ov/` & `models/whisper-base-fp16-ov/`** : Transcription Speech-to-Text en français & anglais.
4. **`models/speaker/`** : Réseau neuronal de reconnaissance biométrique vocale du locuteur.
5. **`models/lcm_dreamshaper_8bit/`** : Modèle de diffusion d'images rapide (Latent Consistency Model).
6. **`models/npu_cache/`** : Blobs binaires compilés pour le NPU Intel pour un démarrage immédiat à froid.

---

### Étape 5 : Compilation et Lancement

Vous pouvez compiler le projet soit via le script [`Compiler_DemoHub.bat`](Compiler_DemoHub.bat), soit via la ligne de commande :

```powershell
# Compilation Release haute performance (Recommandee)
dotnet build DemoHub\DemoHub.csproj -c Release

# Publication complète autonome (x64)
dotnet publish DemoHub\DemoHub.csproj -c Release -r win-x64 --no-self-contained -o bin\Publish\x64
```

Puis lancez la démonstration :
- Soit via le lanceur optimisé : [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat) *(qui initialise le démon Foundry et préchauffe les modèles NPU/GPU)*.
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
- **Cause** : Le pilote Intel NPU n'est pas actif ou la version de Windows n'est pas à jour.
- **Solution** : Ouvrez le *Gestionnaire de périphériques > Périphériques de calcul* (ou *Intel(R) AI Boost*), faites un clic droit > *Mettre à jour le pilote*. Vérifiez également que l'onglet NPU apparaît bien dans *Gestionnaire des tâches > Performances*.

### 4. La webcam ne s'ouvre pas dans la démo Contrôle d'Accès
- **Cause** : Autorisation caméra Windows désactivée ou webcam occupée par Teams/Zoom.
- **Solution** : Allez dans *Paramètres Windows > Confidentialité et sécurité > Caméra* et vérifiez que l'accès aux applications de bureau est activé. Fermez Teams/Zoom.

---

## 6. 📦 Synthèse pour une Clé USB de Déploiement

Pour transporter la démo sur un autre PC sur une clé USB ou un disque externe :
1. Copiez **tout le dossier** `CopilotPlusDemoHub` (qui contient déjà le code source, les modèles `models/`, les fichiers échantillons `Data/` et les scripts `.bat`).
2. Sur le PC cible, branchez la clé USB et copiez le dossier sur le SSD (ex: `C:\Demos\CopilotPlusDemo`).
3. Double-cliquez sur `Installer_Prerequis.bat` (idéalement en clic droit > *Exécuter en tant qu'administrateur*).
4. Double-cliquez sur `Lancer_DemoHub.bat`.
