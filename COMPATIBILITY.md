# Matrice de Compatibilité Matérielle & Silicium — Copilot+ Demo Hub

Ce document définit précisément les niveaux de validation, les moteurs d'exécution et les mécanismes de repli (fallback) pour chaque architecture matérielle.

---

## 1. Niveaux de Support Silicium

| Architecture | Plateforme / Puce | Niveau de Support | Accélérateur Principal | Runtime IA | Statut de Validation |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Intel x86_64** | **Core Ultra Série 2 (Lunar Lake)**<br>*(ex: 258V, 268V, 288V)* | **Plateforme de Référence (Natif)** | **Intel AI Boost (48 TOPS)** + iGPU Arc 140V | OpenVINO IR natif + DirectML | **100% Validé & Mesuré sur banc physique** |
| **Intel x86_64** | **Core Ultra Série 1 (Meteor Lake / Arrow Lake)**<br>*(ex: 155H, 165H)* | **Compatible** | Intel AI Boost (11 TOPS) + iGPU Arc | OpenVINO IR + DirectML | Validé sous réserve des derniers pilotes VPUX Intel |
| **AMD x86_64** | **Ryzen AI Série 300 (Strix Point)**<br>*(ex: AI 9 HX 370)* | **Expérimental (Hybride)** | GPU AMD Radeon 890M / 880M | ONNX DirectML + Microsoft Olive FP16 / Fallback CPU | Fonctionnel via DirectML GPU ; pilote NPU XDNA en cours de stabilisation amont |
| **Qualcomm ARM64** | **Snapdragon X Elite / Plus**<br>*(ex: X1E-80, X1P)* | **Expérimental (Hybride)** | GPU Qualcomm Adreno | ONNX DirectML + binaire autonome natif `win-arm64` | Inférence DirectML GPU active ; compilation QNN spécifique requise pour NPU Hexagon |
| **Universel** | **Tout PC Windows 11 64-bit sans NPU** | **Mode Dégradé** | GPU DirectX 12 / CPU hôte multithread | DirectML / CPU AVX-512 / ARM64 NEON | Repli transparent garanti sans crash |

---

## 2. Matrice Fonctionnelle par Démonstration Métier

| Studio / Démonstration | Moteur / Modèle | Intel Lunar Lake (NPU) | Intel Arc (GPU) | AMD Ryzen (DirectML) | Qualcomm Snapdragon (DirectML) | Fallback CPU |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Assistant Phi-4** | `phi-4-mini-instruct` | **NPU OpenVINO** | GPU OpenVINO | DirectML | DirectML | CPU FP32 |
| **Gomme Magique** | `yolov8n-seg` | **NPU OpenVINO (84 ms)** | DirectML (120 ms) | DirectML | DirectML | CPU (4 500 ms) |
| **Contrôle d'Accès** | `yolov8s-world` | **NPU OpenVINO (33 ms)** | DirectML (70 ms) | DirectML | DirectML | CPU (320 ms) |
| **Transcription Audio** | `whisper-tiny` / `whisper-base` | **NPU OpenVINO** | DirectML / CPU | DirectML / CPU | DirectML / CPU | CPU OpenVINO |
| **Biométrie Vocale** | `voxceleb` (ECAPA-TDNN) | **CPU / DirectML** | DirectML | DirectML | DirectML | CPU |
| **Studio PDF (OCR)** | `ch_PP-OCRv4` | **NPU Intel (26 ms)** | GPU DirectML (140V) | GPU DirectML | GPU DirectML | CPU ONNX |
| **Studio Avatar** | `lcm_dreamshaper_8bit` | **NPU Copilot+** | GPU DirectML | GPU DirectML | GPU DirectML | CPU dégradé |

---

## 3. Règle d'Attribution et de Preuve Silicium

Dans toute l'application :
1. **Périphérique Demandé vs Périphérique Actif** : L'interface affiche toujours le composant matériel qui a **effectivement exécuté l'inférence** (`active_device`), et non celui initialement sollicité si un repli automatique a eu lieu.
2. **Pas de NPU garanti sans validation** : Si un opérateur d'un modèle n'est pas supporté par le compilateur NPU du fondeur (ex: dimension dynamique ou opération non implémentée), le runtime bascule sur GPU ou CPU et le statut `fallback_used: true` est tracé dans la télémétrie de flotte.
