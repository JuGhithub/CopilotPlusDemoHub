# Journal des Modifications (Changelog)

Toutes les évolutions notables de ce projet sont consignées dans ce document.

## [1.1.0] - 2026-11-24

### Ajouts
- **Contrat RuntimeEvidence** : Normalisation de la télémétrie d'exécution pour certifier l'usage effectif du NPU face aux replis GPU/CPU.
- **IPC JSON Lines demohub/1.0** : Gestionnaire unifié de processus Python C# (`PythonProcessManager`) séparant stdout structuré et stderr diagnostique.
- **Module partagé demohub_common** : Protocoles d'émission d'états et introspection matérielle centralisés.
- **Contrôle d'intégrité de la supply-chain** : Vérification des empreintes SHA-256 des modèles téléchargés via `download_models.py`.
- **Intégration SBOM et audit de sécurité** : Génération d'un inventaire SBOM SPDX 2.3 et ajout de l'analyse automatisée `pip-audit` dans la CI GitHub Actions.
- **Suite de tests automatisés** : Tests unitaires .NET 8 (xUnit) et tests fonctionnels Python (pytest) couvrant la non-rémanence du caviardage PDF et la cryptographie DPAPI.

### Modifications
- Clarification technique et suppression des allégations non vérifiées dans la documentation (`README.md`, `README.fr.md`).
- Modularisation du script d'installation (`Installer_Prerequis.ps1`) avec options granulaires `-CheckOnly`, `-InstallRuntime`, `-DownloadModels`, et `-BuildApplication`.
- Tolérance des dépendances d'accélération matérielle lors de l'exécution en environnement d'intégration continue sans NPU physique.

## [1.0.0] - 2026-09-01

### Version Initiale
- Démonstrateur interactif WPF sous .NET 8 pour PC Copilot+.
- Intégration de modèles d'IA locaux pour la vision (YOLOv8-Seg), la parole (Whisper) et la détection OCR.
- Télémétrie système pour l'observation des ressources processeur et accélérateur graphique.
