# Limites Connues, Périmètre & Hypothèses — Copilot+ Demo Hub

Ce document recense les limites techniques, opérationnelles et architecturales de Copilot+ Demo Hub afin de garantir la transparence vis-à-vis des évaluateurs techniques et des directeurs des systèmes d'information (DSI).

---

## 1. Dépendances Réseau & Phase d'Installation

1. **Accès Internet requis lors de l'installation** :
   - L'installation initiale (`Installer_Prerequis.ps1`) requiert un accès réseau sortant pour :
     - Télécharger les runtimes (.NET 8 SDK, Python 3.12, Foundry Local via `winget`) ;
     - Télécharger les 106 dépendances pip verrouillées dans `requirements.lock` ;
     - Télécharger les poids des modèles d'IA pré-entraînés depuis Hugging Face (`~15 Go` au total).
2. **Fonctionnement 100% Hors-Ligne après Installation** :
   - Une fois l'étape d'initialisation achevée (`models/` peuplé et `.venv` configuré), **l'application s'exécute à 100% en local et hors-ligne**, sans aucun appel d'API vers des services cloud tiers.

---

## 2. Latence à Froid & Précompilation NPU

1. **Premier démarrage (Cold Start)** :
   - Les modèles OpenVINO ciblant le NPU Intel AI Boost (ex: Whisper, YOLO, PP-OCRv4) subissent une étape de compilation matérielle lors de leur tout premier chargement (`~2 à 5 secondes` par modèle).
   - Les blobs compilés sont automatiquement mis en cache dans `models/npu_cache/` pour rendre les démarrages ultérieurs quasi-instantanés (`< 100 ms`).
2. **Mémoire Vive (RAM)** :
   - L'exécution simultanée des démos et des daemons résidents est optimisée pour une configuration avec **32 Go de RAM LPDDR5X unifiée**.
   - Sur les machines équipées de 16 Go de RAM, le système d'exploitation peut paginer temporairement lors du chargement simultané du modèle SLM Phi-4 et du modèle de diffusion Avatar LCM.

---

## 3. Assistance au Caviardage Documentaire (PDF Studio)

1. **Aide Algorithmique vs Décision Juridique** :
   - Le module de caviardage PDF utilise une détection conjointe par expressions régulières (IBAN, NIR, SIREN, emails) et par SLM local (`Phi-4-mini`).
   - Bien que le caviardage appliqué soit **définitif et irréversible** (suppression vectorielle du texte sous aplat noir), **une vérification humaine qualifiée demeure indispensable** avant toute diffusion d'un document confidentiel.
   - L'outil ne dispense pas d'un audit de conformité légale formel selon les règles de gouvernance RGPD de l'entreprise.

---

## 4. Biométrie Vocale Locuteur (Audio Studio)

1. **Démonstration Didactique vs Authentification Forte** :
   - Le modèle de biométrie vocale (`VoxCeleb ECAPA-TDNN`) compare des vecteurs d'embeddings (192D) par similarité cosinus.
   - Il s'agit d'un démonstrateur de reconnaissance et diarisation du locuteur. Il ne comporte pas de mécanisme anti-rejeu acoustique de classe bancaire (liveness detection vocale).
   - Les empreintes sont chiffrées localement via l'API Windows DPAPI sous la session de l'utilisateur actif.

---

## 5. Télémétrie Énergétique

1. **Estimation Dérivée vs Compteur Matériel Dédié** :
   - Les valeurs affichées en Watts et Joules sont calculées à partir des compteurs de charge du processeur et des modèles de puissance thermique estimés du fondeur.
   - Elles ne constituent pas une mesure physique de laboratoire (qui nécessiterait une sonde de puissance matérielle externe sur rail d'alimentation PCIe/SoC).
