# Méthodologie de Benchmark & Mesure de l'Énergie — Copilot+ Demo Hub

Ce document détaille le protocole expérimental et métrologique utilisé dans Copilot+ Demo Hub pour évaluer les performances d'inférence et estimer la consommation énergétique des accélérateurs CPU, GPU et NPU.

---

## 1. Définition des Métriques d'Inférence

Toute mesure de latence dans l'application distingue rigoureusement le temps d'inférence brute du temps de traitement applicatif :

| Métrique | Symbole | Description | Unité |
| :--- | :--- | :--- | :--- |
| **Temps de Compilation** | $T_{\text{compile}}$ | Durée de compilation du graphe de calcul vers le binaire matériel (VPUX/OpenVINO ou DirectML). | Millisecondes (ms) |
| **Préchauffage (Warm-up)** | $T_{\text{warmup}}$ | Première inférence à vide permettant d'initialiser les mémoires caches et le pipe matériel. | Millisecondes (ms) |
| **Inférence Pure** | $T_{\text{inf}}$ | Temps d'exécution effectif du modèle neuronal (`sess.run()` ou `compiled_model()`), hors pré/post-traitement. | Millisecondes (ms) |
| **Latence Bout-en-Bout** | $T_{\text{e2e}}$ | Temps total incluant le décodage d'image/audio, le prétraitement tensoriel, l'inférence et le rendu IHM. | Millisecondes (ms) |

---

## 2. Protocole de Mesure & Statistiques

Pour le duel comparatif et les benchmarks de charge (Laboratoire Microsoft Olive et Télémétrie de Flotte) :
1. **Échauffement préalable** : 10 itérations de préchauffage (warm-ups) non comptabilisées dans les moyennes.
2. **Série statistique** : Échantillon de 30 à 100 inférences consécutives avec la même entrée et le même batch ($N=1$).
3. **Agrégations calculées** :
   - **Médiane ($P_{50}$)** : Temps typique éliminant l'impact des micro-interruptions du noyau Windows.
   - **Percentile 90 ($P_{90}$)** : Temps pire-cas garantissant la stabilité d'interaction en démonstration.
   - **Débit (Tokens/s)** : Nombre effectif de tokens générés par seconde lors des tests LLM/SLM.

---

## 3. Modèle d'Énergie Estimée

### 3.1 Pourquoi une valeur estimée ?
Sur les ordinateurs portables grand public et professionnels, les compteurs matériels standard (ex: interface `Windows.System.Power` ou compteurs WMI) fournissent le taux de décharge global de la batterie du système ou la fréquence des coeurs, mais ne disposent pas d'un shunt de courant physique isolé pour chaque accélérateur NPU/iGPU individuel.

### 3.2 Formule de calcul
L'énergie totale consommée par une inférence est calculée par intégration numérique temporelle :

$$E_{\text{inf}} (\text{Joules}) = P_{\text{estimé}} (\text{Watts}) \times \frac{T_{\text{inf}} (\text{ms})}{1000}$$

Où :
- $P_{\text{estimé}}$ est calculé à partir de la puissance moyenne allouée au composant selon son enveloppe thermique TDP :
  - **NPU Intel AI Boost** : Puissance de fonctionnement estimée à `~2.5 W` en régime stabilisé.
  - **iGPU Intel Arc 140V** : Puissance moyenne estimée à `~15 W` à pleine charge graphique.
  - **CPU Multithread hôte** : Puissance moyenne estimée à `~28 W` lors d'une charge tensorielle complète.
- L'énergie économisée par rapport au CPU ou GPU s'exprime par :
  $$\Delta E = E_{\text{ref}} - E_{\text{NPU}}$$

Cette métrique est clairement libellée dans l'interface et les exports JSON comme **Indicateur Énergétique Estimé** (`estimated_power_watts`, `energy_joules`).
