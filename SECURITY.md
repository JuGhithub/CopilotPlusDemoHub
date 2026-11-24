# Politique de Sécurité & Confidentialité — Copilot+ Demo Hub

## 1. Engagement de Confidentialité
Copilot+ Demo Hub est un démonstrateur technique conçu pour exécuter des traitements d'intelligence artificielle en local sur l'appareil de l'utilisateur :
- Les données traitées (audio, transcriptions, documents PDF, vidéos et images) ne sont pas transmises à des serveurs cloud tiers par l'application.
- Les empreintes biométriques générées lors des sessions d'Audio Studio sont chiffrées localement à l'aide de l'API Windows Data Protection (DPAPI), liée à la clé matérielle de la session utilisateur Windows active.

## 2. Signalement d'une Vulnérabilité
Si vous découvrez une vulnérabilité de sécurité ou un défaut de protection des données dans ce projet :
1. **Ne pas ouvrir de ticket GitHub public.**
2. Envoyez un rapport détaillé par courriel au mainteneur ou utilisez l'onglet GitHub *Security Advisories* privé du dépôt.
3. Veuillez inclure :
   - La description de la vulnérabilité ;
   - Les étapes ou le script permettant de reproduire le problème ;
   - Les versions d'environnement testées (OS, runtime .NET, version Python).

## 3. Précautions d'Exécution & Périmètre
- **Assistance au caviardage** : Le module d'audit PDF et d'anonymisation constitue une assistance algorithmique locale. Il ne se substitue pas à une relecture humaine avant diffusion d'un document confidentiel.
- **Micro-services locaux** : Les scripts Python communiquent avec l'application hôte WPF via des flux standards stdin/stdout d'arrière-plan sans écoute sur port réseau distant.
- **Fourniture des modèles** : Les modèles d'IA proviennent de dépôts vérifiés (Hugging Face / Microsoft / OpenVINO). L'intégrité des poids peut être vérifiée via `python scripts/download_models.py --verify-integrity`.
