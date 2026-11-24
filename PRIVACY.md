# Politique de Confidentialité et Protection des Données Locales

Le projet **CopilotPlusDemoHub** a été conçu pour respecter une stricte étanchéité des données sur le poste de travail.

## 1. Principe de Fonctionnement Hors Ligne

- Les opérations d'inférence (vision par ordinateur, analyse audio, détection de motifs, grands modèles de langage locaux) s'exécutent entièrement sur les composants de calcul de la machine hôte (CPU, GPU, NPU).
- Aucune donnée soumise par l'utilisateur (images de webcam, fichiers PDF, échantillons vocaux, prompts textuels) n'est transmise à des serveurs distants ou à des services de télémétrie externe pendant les démonstrations.

## 2. Protection des Empreintes Biométriques Vocales

- Les vecteurs d'empreintes générés lors de l'enregistrement de locuteurs sont protégés par l'interface de programmation Windows DPAPI (`CryptProtectData`).
- La clé de chiffrement est dérivée du profil de session Windows local de l'utilisateur. Aucune clé cryptographique statique n'est stockée en clair dans le code source ou la base de registre.

## 3. Caviardage et Destruction des Données Sensibles

- Le module de caviardage PDF applique des opérations destructives sur les flux de contenu cibles.
- Les zones identifiées comme contenant des informations identifiables sont définitivement retirées de l'arbre d'objets du document et de son flux binaire sous-jacent avant enregistrement.

## 4. Données de Télémétrie Matérielle

- Les indicateurs de fréquence, d'occupation de charge et de consommation électrique sont interrogés en temps réel via les compteurs de performance Windows et les bibliothèques d'exécution locales (OpenVINO, DirectML).
- Ces mesures ne sont conservées qu'en mémoire vive pour l'affichage de l'interface utilisateur et ne font l'objet d'aucune exportation externe automatique.
