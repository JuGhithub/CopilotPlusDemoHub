# 🚀 Copilot+ PC Demo Hub

Une suite complète de démonstrations d'Intelligence Artificielle 100% locales et souveraines, optimisées pour les **PC Copilot+** équipés de processeurs **Intel Core Ultra Série 2 (Lunar Lake)** et de l'accélérateur **NPU Intel AI Boost (40+ TOPS)**.

---

## 🌟 Vue d'ensemble des Démos

Le **Demo Hub** regroupe plusieurs cas d'usage métiers concrets illustrant la puissance du traitement local sans aucun appel cloud :

| Démo | Modèles & Technologies | Accélérateur Matériel | Description |
| :--- | :--- | :--- | :--- |
| **🏢 Assistant Métier** | LLM Phi-4-mini (Instruct), OpenVINO GenAI / Foundry Local | **NPU** / **iGPU Arc** | 4 personas métiers (Finance, Opérations, Ingénierie, Assurance) avec pipeline de raisonnement en 4 étapes et télémétrie en direct. |
| **🪄 Gomme Magique Studio** | YOLOv8n-Seg (OpenVINO IR & Daemon IPC) | **NPU Intel AI Boost** | Détection d'objets et segmentation sémantique temps réel (~84ms) avec suppression instantanée. |
| **🛡️ Contrôle d'Accès Zero-Trust** | YOLOv8s-World (OpenVINO), Flux Webcam direct | **NPU** / **GPU** | Détection multi-classes en direct (badges, casques, lunettes, tasses) avec matrice de sécurité. |
| **🎙️ Audio Studio & Locuteurs** | Whisper (Tiny/Base FP16 OV), VoxCeleb Biométrie | **NPU** / **CPU** | Transcription Speech-to-Text ultra-rapide et identification biométrique d'empreintes vocales. |
| **📄 PDF & OCR Intelligent** | PyMuPDF (Moteur vectoriel), RapidOCR (DirectML) | **GPU DirectML** / **CPU** | Recherche sémantique, anonymisation automatique de données sensibles et OCR de documents scannés. |
| **🎨 Studio Avatar & Diffusion** | LCM DreamShaper (Latent Consistency Model 8-bit) | **NPU** / **GPU** | Génération et stylisation d'avatars en quelques secondes. |
| **⚡ Benchmark Énergétique** | Télémétrie hardware temps réel | **NPU vs GPU vs CPU** | Analyse comparative de consommation, latence et débit (tok/s). |

---

## ⚡ Guide Rapide de Prise en Main (Pour vos collègues)

### Option A : Déploiement Développeur (Recommandé)

1. **Cloner le dépôt Git :**
   ```powershell
   git clone https://github.com/<VOTRE-ORGANISATION-OU-USER>/CopilotPlusDemoHub.git
   cd CopilotPlusDemoHub
   ```

2. **Placer le dossier des modèles `models/` :**
   - Téléchargez l'archive `models.zip` depuis l'onglet **Releases** de ce dépôt (ou récupérez-la sur le partage d'équipe / OneDrive).
   - Décompressez-la à la racine du projet dans un dossier nommé `models/`.

3. **Installer les prérequis en 1 clic :**
   - Faites un clic droit sur [`Installer_Prerequis.bat`](Installer_Prerequis.bat) > **Exécuter en tant qu'administrateur**.
   - Le script configure automatiquement :
     - Le support des chemins longs Windows (`LongPathsEnabled`).
     - Le **SDK .NET 8.0** et **Python 3.12** via `winget`.
     - Les dépendances Python (`requirements.txt`).
     - Le service **Microsoft Foundry Local CLI** et le modèle Phi-4.
     - La compilation initiale de l'application WPF.
     - Un diagnostic complet de conformité (`scripts/verify_installation.py`).

4. **Lancer la démonstration :**
   - Double-cliquez sur [`Lancer_DemoHub.bat`](Lancer_DemoHub.bat).

---

### Option B : Version Binaire "Prête à l'emploi" (Sans Visual Studio)

Pour les collègues non-développeurs souhaitant simplement exécuter la démo lors de présentations clients :
1. Rendez-vous sur la section **Releases** du dépôt GitHub.
2. Téléchargez l'archive complète de la dernière version (contenant `DemoHub.exe` précompilé et les modèles).
3. Décompressez et lancez `Lancer_DemoHub.bat`.

---

## 💻 Configuration Matérielle Recommandée

- **Processeur** : Intel Core Ultra Série 2 (Intel Core Ultra 7 258V / 268V / Ultra 9 288V) ou Série 1 (Meteor Lake)
- **NPU** : Intel AI Boost (40 à 48 TOPS - label Copilot+ PC)
- **Pilotes** : Intel NPU Compute Driver (VPUX 32.0+) & Pilote graphique Intel Arc à jour
- **Mémoire Vive** : 16 Go à 32 Go LPDDR5X
- **Système** : Windows 11 version 24H2 (Build 26100+)

Consultez le guide complet dans [`GUIDE_INSTALLATION.md`](GUIDE_INSTALLATION.md) pour le détail pas à pas et la résolution des incidents.

---

## 📁 Architecture du Projet

```text
├── DemoHub/                 # Application WPF C# .NET 8 (Interface principale, Télémétrie, Démos)
├── Data/                    # Échantillons de test (Documents, Audios, Photos d'accidents/sinistres)
├── Config/ & Prompts/       # Configurations d'inférence et prompts d'analyse métier
├── scripts/                 # Scripts d'audit d'installation et d'optimisation Olive
├── *.py                     # Microservices Python (Vision YOLO, Whisper, OCR, Avatar, Eraser)
├── Installer_Prerequis.bat  # Script d'installation automatisée en 1 clic (Winget + Pip + Foundry)
├── Lancer_DemoHub.bat       # Script de lancement avec préchauffage matériel (NPU / GPU)
├── Compiler_DemoHub.bat     # Script de compilation Release .NET 8
├── requirements.txt         # Dépendances Python (OpenVINO, PyTorch, PyMuPDF, etc.)
└── GUIDE_INSTALLATION.md    # Manuel technique complet de déploiement
```

---

## 🔒 Confidentialité & Souveraineté des Données

Toutes les inférences (LLM, vision par ordinateur, transcription audio et segmentation) s'exécutent **100% hors ligne** sur le silicium local du PC. Aucune donnée, photo ou enregistrement n'est envoyé vers des serveurs externes.
