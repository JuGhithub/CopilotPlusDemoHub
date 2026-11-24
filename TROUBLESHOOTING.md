# Guide de Dépannage et Résolution des Incidents (Troubleshooting)

Ce document rassemble les solutions aux problèmes fréquemment rencontrés lors de l'installation ou de l'exécution de **CopilotPlusDemoHub**.

---

### 1. Prise en charge des chemins longs sous Windows (`LongPathsEnabled`)

**Symptôme** : Échec lors de la création de l'environnement virtuel Python ou de l'installation de certains packages (`FileNotFoundError` ou `PathTooLongException`).

**Solution** :
Activez la gestion des chemins longs dans le Registre Windows via une session PowerShell avec privilèges administrateur :

```powershell
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1
```

---

### 2. Périphérique NPU non détecté ou repli automatique sur CPU/GPU

**Symptôme** : L'interface affiche `⚠️ Fallback (CPU)` ou `⚠️ Fallback (GPU)` au lieu de confirmer l'utilisation d'Intel AI Boost.

**Vérifications** :
1. Assurez-vous d'utiliser une machine dotée d'un processeur compatible (Intel Core Ultra avec NPU).
2. Vérifiez la présence du pilote NPU dans le Gestionnaire de périphériques sous la section **Périphériques de calcul** (*Intel(R) AI Boost*).
3. Mettez à jour le pilote d'accélération neuronale (Intel NPU Driver / VPUX) via Windows Update ou le portail constructeur.
4. Lancez le diagnostic système intégré :
   ```powershell
   python scripts/verify_installation.py --json
   ```

---

### 3. Autorisations de la Webcam et du Microphone

**Symptôme** : Les modules de vision interactive ou de reconnaissance vocale renvoient une erreur d'accès au flux d'entrée.

**Solution** :
1. Ouvrez les **Paramètres Windows** > **Confidentialité et sécurité**.
2. Dans **Autorisations pour les applications**, sélectionnez **Caméra** et **Microphone**.
3. Assurez-vous que l'option **Autoriser les applications de bureau à accéder à votre caméra/microphone** est activée.

---

### 4. Déploiement en Environnement Hors Ligne (Air-Gapped)

**Symptôme** : Les scripts d'initialisation échouent car la machine de démonstration n'a pas accès à Internet.

**Procédure recommandée** :
1. Préparez au préalable l'archive complète des modèles sur une machine connectée :
   ```powershell
   python download_models.py --verify-integrity
   ```
2. Copiez l'environnement virtuel `.venv` et le dossier `models` sur le support amovible sécurisé.
3. Exécutez l'application en mode hors ligne :
   ```powershell
   .\scripts\lancer_application.ps1
   ```
