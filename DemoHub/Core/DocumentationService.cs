using System;
using System.Collections.Generic;
using System.Linq;

namespace CopilotPlusDemoHub.Core;

public sealed class DocTopic
{
    public string Key { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // 'Global' or 'Demo'
    public string Icon { get; set; } = string.Empty;
    public string TitleFr { get; set; } = string.Empty;
    public string TitleEn { get; set; } = string.Empty;
    public string SubtitleFr { get; set; } = string.Empty;
    public string SubtitleEn { get; set; } = string.Empty;
    public string Accelerator { get; set; } = string.Empty;
    public string LatencyPowerFr { get; set; } = string.Empty;
    public string LatencyPowerEn { get; set; } = string.Empty;
    public string PitchFr { get; set; } = string.Empty;
    public string PitchEn { get; set; } = string.Empty;
    public string DescriptionFr { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;
    public string TechnicalSpecsFr { get; set; } = string.Empty;
    public string TechnicalSpecsEn { get; set; } = string.Empty;
    public string BinariesAndModelsFr { get; set; } = string.Empty;
    public string BinariesAndModelsEn { get; set; } = string.Empty;
    public string HowToRunFr { get; set; } = string.Empty;
    public string HowToRunEn { get; set; } = string.Empty;

    public string GetTitle(string lang) => lang == "en" ? TitleEn : TitleFr;
    public string GetSubtitle(string lang) => lang == "en" ? SubtitleEn : SubtitleFr;
    public string GetLatencyPower(string lang) => lang == "en" ? LatencyPowerEn : LatencyPowerFr;
    public string GetPitch(string lang) => lang == "en" ? PitchEn : PitchFr;
    public string GetDescription(string lang) => lang == "en" ? DescriptionEn : DescriptionFr;
    public string GetTechnicalSpecs(string lang) => lang == "en" ? TechnicalSpecsEn : TechnicalSpecsFr;
    public string GetBinariesAndModels(string lang) => lang == "en" ? BinariesAndModelsEn : BinariesAndModelsFr;
    public string GetHowToRun(string lang) => lang == "en" ? HowToRunEn : HowToRunFr;
}

public sealed class DocumentationService
{
    private static readonly Lazy<DocumentationService> _instance = new(() => new DocumentationService());
    public static DocumentationService Instance => _instance.Value;

    private readonly List<DocTopic> _topics;

    private DocumentationService()
    {
        _topics = InitializeTopics();
    }

    public IReadOnlyList<DocTopic> GetAllTopics() => _topics;
    public IReadOnlyList<DocTopic> GetGlobalTopics() => _topics.Where(t => t.Category == "Global").ToList();
    public IReadOnlyList<DocTopic> GetDemoTopics() => _topics.Where(t => t.Category == "Demo").ToList();

    public DocTopic? GetTopic(string key)
    {
        return _topics.FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private static List<DocTopic> InitializeTopics()
    {
        return new List<DocTopic>
        {
            new DocTopic
            {
                Key = "global_overview",
                Category = "Global",
                Icon = "🌐",
                TitleFr = "Présentation du Projet & Vision Souveraine",
                TitleEn = "Project Overview & Sovereign AI Vision",
                SubtitleFr = "Plateforme unifiée d'IA locale 100% sur l'appareil pour PC Copilot+",
                SubtitleEn = "Unified 100% on-device local AI platform for Copilot+ PCs",
                Accelerator = "⚡ NPU (48 TOPS) + 🎮 GPU Intel Arc + 💻 CPU",
                LatencyPowerFr = "Zéro Latence Réseau • Zéro Coût Token Cloud • 2W à 15W",
                LatencyPowerEn = "Zero Network Latency • Zero Cloud Token Cost • 2W to 15W",
                PitchFr = "Montrez comment Copilot+ PC transforme un ordinateur portable d'entreprise en un centre d'IA autonome. Aucune donnée ne quitte le PC, aucun abonnement cloud récurrent n'est facturé, et toutes les capacités (vision, parole, raisonnement, génération) fonctionnent même en mode avion dans les aéronefs d'une entreprise du secteur aéronautique ou sur un site secret défense.",
                PitchEn = "Demonstrate how Copilot+ PC turns an enterprise laptop into a self-contained AI hub. Zero data leaves the device, zero recurring cloud API token costs, and all capabilities (vision, speech, reasoning, generation) work seamlessly even in airplane mode inside an aircraft of an aerospace sector enterprise or secure facility.",
                DescriptionFr = "Le Copilot+ PC Demo est une vitrine technologique industrielle et d'entreprise conçue pour exploiter nativement le silicium hétérogène des processeurs Intel Core Ultra (Lunar Lake, Arrow Lake, Meteor Lake). Il rassemble 11 cas d'usage concrets prouvant la faisabilité, la rentabilité et la souveraineté de l'IA locale en entreprise.",
                DescriptionEn = "The Copilot+ PC Demo is an enterprise and industrial showcase engineered to natively exploit the heterogeneous silicon of Intel Core Ultra processors (Lunar Lake, Arrow Lake, Meteor Lake). It features 11 concrete scenarios demonstrating the feasibility, ROI, and sovereignty of on-device enterprise AI.",
                TechnicalSpecsFr = "Architecture logicielle : Application native Windows .NET 8.0 WPF XAML avec multithreading asynchrone, ponts IPC haute vitesse avec les runtimes OpenVINO, bindings DirectML / ONNX Runtime, et micro-services locaux. Télémétrie énergétique intégrée calculant en temps réel les Watts consommés et les Joules économisés par inférence.",
                TechnicalSpecsEn = "Software Architecture: Native Windows .NET 8.0 WPF XAML application with asynchronous multithreading, high-speed IPC bridges with OpenVINO runtimes, DirectML / ONNX Runtime bindings, and local micro-services. Integrated silicon telemetry calculating real-time Watt consumption and cumulative Joules saved per inference.",
                BinariesAndModelsFr = "• Exécutable principal : DemoHub\\bin\\Debug\\net8.0-windows10.0.18362\\DemoHub.exe\\n• Modèles OpenVINO : models/ (yolov8s-world, whisper, lcm_dreamshaper)\\n• Données locales : Data/ (Images, Audio, Documents, access_samples)",
                BinariesAndModelsEn = "• Main Executable: DemoHub\\bin\\Debug\\net8.0-windows10.0.18362\\DemoHub.exe\\n• OpenVINO Models: models/ (yolov8s-world, whisper, lcm_dreamshaper)\\n• Local Datasets: Data/ (Images, Audio, Documents, access_samples)",
                HowToRunFr = "Double-cliquer sur Lancer_DemoHub.bat ou exécuter 'dotnet run' dans le dossier DemoHub.",
                HowToRunEn = "Double-click Lancer_DemoHub.bat or run 'dotnet run' inside the DemoHub directory."
            },
            new DocTopic
            {
                Key = "global_prereqs",
                Category = "Global",
                Icon = "💻",
                TitleFr = "Prérequis Matériels & Système",
                TitleEn = "Hardware & System Requirements",
                SubtitleFr = "Configuration matérielle certifiée Copilot+ et environnement d'exécution",
                SubtitleEn = "Certified Copilot+ hardware configuration and runtime environment",
                Accelerator = "Silicium Copilot+ Certifié",
                LatencyPowerFr = "Exigences : NPU >= 40 TOPS • 16 Go RAM min • Windows 11 24H2",
                LatencyPowerEn = "Requirements: NPU >= 40 TOPS • 16 GB RAM min • Windows 11 24H2",
                PitchFr = "Expliquez que le label Copilot+ n'est pas juste un autocollant marketing, mais une garantie matérielle : un NPU dédié d'au moins 40 TOPS déchargeant le CPU et préservant l'autonomie sur batterie toute la journée.",
                PitchEn = "Highlight that the Copilot+ designation is not just a marketing badge, but a hardware guarantee: a dedicated NPU with 40+ TOPS that offloads the CPU and sustains all-day battery life.",
                DescriptionFr = "Configuration minimale recommandée :\\n1. Système d'exploitation : Windows 11 64-bit version 24H2 (Build 26100+) avec pilotes Intel NPU VPUX et Intel Arc Graphics à jour.\\n2. Processeur : Intel Core Ultra Série 2 (Lunar Lake 200V) avec NPU 48 TOPS ou Arrow Lake / Meteor Lake.\\n3. Mémoire Vive : 16 Go de RAM LPDDR5X (32 Go recommandés pour LCM et les gros modèles RAG).\\n4. Stockage : 10 Go d'espace disque SSD pour les modèles IA OpenVINO locaux.",
                DescriptionEn = "Recommended minimum configuration:\\n1. Operating System: Windows 11 64-bit version 24H2 (Build 26100+) with updated Intel NPU VPUX and Intel Arc Graphics drivers.\\n2. Processor: Intel Core Ultra Series 2 (Lunar Lake 200V) with 48 TOPS NPU or Arrow Lake / Meteor Lake.\\n3. Memory: 16 GB LPDDR5X RAM (32 GB recommended for LCM and large RAG embeddings).\\n4. Storage: 10 GB SSD disk space for local OpenVINO models.",
                TechnicalSpecsFr = "Dépendances logicielles requises :\\n• .NET 8.0 SDK (winget install Microsoft.DotNet.SDK.8)\\n• Python 3.12 x64 (winget install Python.Python.3.12)\\n• Microsoft Foundry Local CLI (winget install Microsoft.FoundryLocal)\\n• Packages Python : pip install -r requirements.txt (openvino, torch, ultralytics, pymupdf, rapidocr)\\n• Modèles Phi-4 : foundry model download phi-4-mini-instruct-openvino-npu\\n• Pilotes Intel : Intel NPU Compute Driver 32.0.100+ et Intel Arc Graphics.",
                TechnicalSpecsEn = "Required software dependencies:\\n• .NET 8.0 SDK (winget install Microsoft.DotNet.SDK.8)\\n• Python 3.12 x64 (winget install Python.Python.3.12)\\n• Microsoft Foundry Local CLI (winget install Microsoft.FoundryLocal)\\n• Python packages: pip install -r requirements.txt (openvino, torch, ultralytics, pymupdf, rapidocr)\\n• Phi-4 Models: foundry model download phi-4-mini-instruct-openvino-npu\\n• Intel drivers: Intel NPU Compute Driver 32.0.100+ and Intel Arc Graphics.",
                BinariesAndModelsFr = "Script automatique 'Installer_Prerequis.bat' à la racine qui installe l'ensemble des dépendances et modèles en une seule commande.",
                BinariesAndModelsEn = "Automated script 'Installer_Prerequis.bat' at project root installs all dependencies and models in a single command.",
                HowToRunFr = "Installation en 1 clic : Exécuter 'Installer_Prerequis.bat' à la racine, puis lancer 'Lancer_DemoHub.bat'.",
                HowToRunEn = "1-Click Setup: Run 'Installer_Prerequis.bat' at the project root, then launch with 'Lancer_DemoHub.bat'."
            },
            new DocTopic
            {
                Key = "global_tech",
                Category = "Global",
                Icon = "⚙️",
                TitleFr = "Technologies & Moteurs d'Inférence",
                TitleEn = "Core Technologies & Inference Engines",
                SubtitleFr = "Stack technique, frameworks d'accélération et optimisation de modèles",
                SubtitleEn = "Technical stack, hardware acceleration frameworks, and model quantization",
                Accelerator = "OpenVINO • DirectML • ONNX Runtime • PyTorch",
                LatencyPowerFr = "Quantification INT8/FP16 • Compilation Ahead-of-Time",
                LatencyPowerEn = "INT8/FP16 Quantization • Ahead-of-Time Graph Compilation",
                PitchFr = "Soulignez que nous n'utilisons pas de simples appels REST vers des serveurs distants, mais du code C# et C++ ultra-optimisé exécutant les graphes de neurones directement dans la mémoire cache du NPU et du GPU.",
                PitchEn = "Emphasize that this is not a thin client making remote REST calls, but ultra-optimized C# and C++ code executing neural graphs directly in NPU cache and GPU registers.",
                DescriptionFr = "La suite s'appuie sur les standards les plus performants de l'écosystème Intel et Microsoft :\\n• Intel OpenVINO 2024 : Compilation et inférence des modèles vision et audio sur VPUX (NPU) et iGPU.\\n• YOLO-World OpenVINO : Vision par ordinateur en vocabulaire ouvert avec prompt visuel temps réel.\\n• OpenAI Whisper FP16 : Modèle speech-to-text transcrivant en quelques millisecondes.\\n• LCM DreamShaper : Modèle de diffusion ultra-rapide (4 étapes au lieu de 50).\\n• WPF .NET 8 : Interface fluide à 60 FPS avec séparation stricte UI / background workers.",
                DescriptionEn = "The suite is built on top-tier Intel and Microsoft developer runtimes:\\n• Intel OpenVINO 2024: Compilation and inference of vision/audio models on VPUX (NPU) and iGPU.\\n• YOLO-World OpenVINO: Open-vocabulary zero-shot computer vision with real-time visual prompting.\\n• OpenAI Whisper FP16: Speech-to-text model transcribing multi-minute audio in milliseconds.\\n• LCM DreamShaper: Latent Consistency Diffusion generating high-res art in 4 steps instead of 50.\\n• WPF .NET 8: 60 FPS fluid user interface with clean UI / background worker separation.",
                TechnicalSpecsFr = "Compilation dynamique des modèles : les graphes OpenVINO sont compilés en mode LATENCY pour le temps réel ou THROUGHPUT pour les traitements par lots. Les caches d'inférence (.blob) sont conservés dans models/npu_cache pour éliminer tout temps de chauffe au démarrage.",
                TechnicalSpecsEn = "Dynamic model compilation: OpenVINO graphs are compiled in LATENCY mode for real-time responsiveness or THROUGHPUT mode for batching. Model cache blobs (.blob) are stored in models/npu_cache to eliminate cold-start warmup.",
                BinariesAndModelsFr = "• openvino_intel_gpu_plugin.dll, openvino_intel_npu_plugin.dll\\n• models/yolov8s-world_openvino_model/yolov8s-world.xml & .bin\\n• models/whisper-tiny-fp16-ov/ & models/whisper-base-fp16-ov/",
                BinariesAndModelsEn = "• openvino_intel_gpu_plugin.dll, openvino_intel_npu_plugin.dll\\n• models/yolov8s-world_openvino_model/yolov8s-world.xml & .bin\\n• models/whisper-tiny-fp16-ov/ & models/whisper-base-fp16-ov/",
                HowToRunFr = "Tester l'accélération GPU/NPU en direct avec les boutons de sélection d'accélérateur de la barre supérieure.",
                HowToRunEn = "Test GPU/NPU acceleration in real time using the hardware selector buttons in the top header."
            },
            new DocTopic
            {
                Key = "global_binaries",
                Category = "Global",
                Icon = "📁",
                TitleFr = "Binaires, Modèles & Fichiers Clés",
                TitleEn = "Binaries, Models & File Structure",
                SubtitleFr = "Inventaire des exécutables, modèles compilés et jeux d'échantillons",
                SubtitleEn = "Inventory of compiled executables, neural models, and sample datasets",
                Accelerator = "Fichiers Locaux Confinés",
                LatencyPowerFr = "Zéro téléchargement dynamique • 100% Autonome",
                LatencyPowerEn = "Zero dynamic downloads • 100% Self-contained",
                PitchFr = "Prouvez la souveraineté totale du système : montrez les fichiers sur le disque dur. Tout est embarqué, aucun modèle n'a besoin de se connecter à Hugging Face ou Azure au démarrage.",
                PitchEn = "Demonstrate total sovereignty: show the physical files on the SSD. Everything is embedded; no model needs to connect to Hugging Face or Azure during startup.",
                DescriptionFr = "Organisation des répertoires du projet :\\n• DemoHub/ : Code source C# .NET 8 (Core, Data, Resources, UI, Views, Models).\\n• DemoHub/bin/Debug/net8.0-windows10.0.18362/DemoHub.exe : Binaire exécutable principal.\\n• models/yolov8s-world_openvino_model/ : Modèle de détection temps réel (27 classes).\\n• models/whisper-*-fp16-ov/ : Modèles Speech-to-Text OpenVINO.\\n• models/lcm_dreamshaper_8bit/ : Modèle de génération d'images 8-bit quantifié NPU.\\n• Data/Images/access_samples/ : Échantillons photo certifiés pour le contrôle d'accès.\\n• Data/Audio/ : Échantillons sonores pour la transcription vocale.\\n• Data/Documents/ : Rapports industriels et fiches techniques CVC pour le RAG.",
                DescriptionEn = "Repository layout and file organization:\\n• DemoHub/: C# .NET 8 source code (Core, Data, Resources, UI, Views, Models).\\n• DemoHub/bin/Debug/net8.0-windows10.0.18362/DemoHub.exe: Main compiled executable.\\n• models/yolov8s-world_openvino_model/: 27-class real-time detection model.\\n• models/whisper-*-fp16-ov/: Speech-to-Text OpenVINO models.\\n• models/lcm_dreamshaper_8bit/: 8-bit quantized NPU image diffusion model.\\n• Data/Images/access_samples/: Certified test images for security access verification.\\n• Data/Audio/: Audio WAV recordings for voice transcription.\\n• Data/Documents/: Technical industrial manuals and HVAC telemetry reports for RAG.",
                TechnicalSpecsFr = "Les modèles OpenVINO sont scindés en deux fichiers : le fichier .xml décrivant le graphe de calcul et le fichier .bin contenant les poids synaptiques binaires quantifiés (FP16 ou INT8).",
                TechnicalSpecsEn = "OpenVINO models consist of two paired files: an .xml file describing the computation graph and a .bin file containing binary synaptic weights quantized in FP16 or INT8.",
                BinariesAndModelsFr = "• DemoHub.exe (151 Ko) + DLLs associées\\n• yolov8s-world.xml + yolov8s-world.bin (68 Mo)\\n• openvino_whisper_base.xml + .bin (145 Mo)",
                BinariesAndModelsEn = "• DemoHub.exe (151 KB) + linked DLLs\\n• yolov8s-world.xml + yolov8s-world.bin (68 MB)\\n• openvino_whisper_base.xml + .bin (145 MB)",
                HowToRunFr = "Ouvrir l'explorateur de fichiers dans le répertoire racine pour inspecter l'arborescence.",
                HowToRunEn = "Open Windows File Explorer at the root folder to inspect directory hierarchy."
            },
            new DocTopic
            {
                Key = "global_security",
                Category = "Global",
                Icon = "🛡️",
                TitleFr = "Sécurité, Confidentialité & Zero-Trust",
                TitleEn = "Security, Privacy & Zero-Trust Architecture",
                SubtitleFr = "Gouvernance des données confidentielles, conformité RGPD et isolation totale",
                SubtitleEn = "Confidential data governance, GDPR compliance, and complete air-gapping",
                Accelerator = "Air-Gapped Local Hardware",
                LatencyPowerFr = "Zéro Fuite Réseau • Audit Local Intégré",
                LatencyPowerEn = "Zero Network Leakage • Built-in Local Audit",
                PitchFr = "Pour les secteurs sensibles (entreprises du secteur aéronautique, banques, santé, défense), l'utilisation d'APIs IA cloud publiques présente un risque majeur de fuite de propriété intellectuelle. Le Demo Hub prouve qu'on peut avoir le même niveau d'intelligence sans qu'un seul octet ne quitte la machine.",
                PitchEn = "For sensitive sectors (aerospace sector enterprises, banking, defense, healthcare), sending data to public cloud AI APIs poses unacceptable IP leak risks. The Demo Hub proves you can achieve equal intelligence with zero bytes transmitted.",
                DescriptionFr = "Principes clés d'architecture sécurisée :\\n1. Étanchéité réseau (Air-gap ready) : Aucune connexion sortante n'est requise pour l'inférence.\\n2. Confinement des documents : Vos rapports financiers, enregistrements de réunions et photos ne sont jamais téléversés vers des tiers.\\n3. Conformité réglementaire : Respect strict du RGPD, du secret des affaires et des normes NIS 2.\\n4. Télémétrie d'audit locale : Chaque inférence est journalisée localement dans un registre cryptographique interne pour les audits DSI.",
                DescriptionEn = "Core secure architecture principles:\\n1. Air-gap readiness: Zero outbound network connections required for inference.\\n2. Document containment: Financial reports, confidential meeting audio, and photos are never uploaded.\\n3. Regulatory compliance: Strict alignment with GDPR, trade secrets protection, and European NIS 2 standards.\\n4. Local audit logging: Every inference is recorded in a local cryptographic audit ledger for IT compliance.",
                TechnicalSpecsFr = "Toutes les communications entre l'interface utilisateur et les services IA s'effectuent via mémoire partagée (MemoryStream, Base64 standard stream) ou processus locaux isolés sous le contexte de l'utilisateur Windows courant.",
                TechnicalSpecsEn = "All communications between UI and AI runtimes operate via shared in-memory buffers (MemoryStream, Base64 standard stream) or sandboxed local child processes under current Windows user context.",
                BinariesAndModelsFr = "Logs d'audit sauvegardés localement dans Logs/ et DemoHub/Logs/ sous format JSON chiffrable.",
                BinariesAndModelsEn = "Audit logs stored locally in Logs/ and DemoHub/Logs/ in encryptable JSON format.",
                HowToRunFr = "Activer le mode avion de Windows et constater que toutes les démonstrations continuent de fonctionner à 100%.",
                HowToRunEn = "Turn on Windows Airplane Mode and verify that all demos continue operating at 100% performance."
            },
            new DocTopic
            {
                Key = "global_telemetry",
                Category = "Global",
                Icon = "⚡",
                TitleFr = "Efficacité Énergétique & Télémétrie Silicium",
                TitleEn = "Energy Efficiency & Silicon Telemetry",
                SubtitleFr = "Mesures en temps réel des Watts, Joules et de l'empreinte Green IT",
                SubtitleEn = "Real-time measurements of Watts, Joules, and Green IT carbon footprint",
                Accelerator = "Télémétrie Matérielle Intégrée",
                LatencyPowerFr = "NPU ~2.5W vs GPU ~14.5W vs Cloud (Data Centers)",
                LatencyPowerEn = "NPU ~2.5W vs GPU ~14.5W vs Cloud Data Centers",
                PitchFr = "Montrez l'impact écologique et l'autonomie : exécuter une tâche d'IA sur le NPU consomme 5 à 10 fois moins d'énergie que sur le processeur ou la carte graphique, tout en évitant les mégawatts des data centers distants.",
                PitchEn = "Showcase the ecological impact and battery savings: running an AI task on the NPU consumes 5x to 10x less power than on CPU/GPU, while eliminating the massive megawatt footprint of hyperscale data centers.",
                DescriptionFr = "Le moteur de télémétrie mesure en direct la puissance dynamique de chaque inférence :\\n• Puissance instantanée P (en Watts) selon le coprocesseur sollicité.\\n• Énergie consommée E (en Joules), calculée selon la formule physique E = P * (t / 1000).\\n• Économies cumulées de CO2 et de consommation d'énergie par rapport à des serveurs cloud distants.",
                DescriptionEn = "The telemetry engine captures the exact dynamic power draw of each model execution:\\n• Instantaneous power P (in Watts) tailored to the active silicon co-processor.\\n• Total energy consumed E (in Joules), computed via the physical law E = P * (t / 1000).\\n• Cumulative CO2 and power savings compared to remote hyperscaler GPU clusters.",
                TechnicalSpecsFr = "Profils de puissance calibrés pour architectures Copilot+ PC (Intel Core Ultra, AMD Ryzen AI, Qualcomm Snapdragon X) :\\n• NPU dédié : ~2.0W - 2.5W en inférence active\\n• GPU intégré : ~14W - 16W\\n• CPU multithread : ~24W - 28W\\n• Énergie par inférence typique NPU : moins de 0.05 Joule.",
                TechnicalSpecsEn = "Power profiles calibrated for Copilot+ PC architectures (Intel Core Ultra, AMD Ryzen AI, Qualcomm Snapdragon X):\\n• Dedicated NPU: ~2.0W - 2.5W active inference\\n• Integrated GPU: ~14W - 16W\\n• Multithreaded CPU: ~24W - 28W\\n• Typical NPU energy per inference: under 0.05 Joule.",
                BinariesAndModelsFr = "Service C# : EnterpriseTelemetryService.cs et HardwareTelemetryService.cs.",
                BinariesAndModelsEn = "C# Services: EnterpriseTelemetryService.cs and HardwareTelemetryService.cs.",
                HowToRunFr = "Consulter le panneau 'Télémétrie & Audit Flotte' ou observer les pilules énergétiques vertes en bas de chaque écran.",
                HowToRunEn = "Open the 'Fleet Telemetry & Audit' view or observe the green energy badges at the bottom of each view."
            },
            new DocTopic
            {
                Key = "demo_operations",
                Category = "Demo",
                Icon = "🏢",
                TitleFr = "Opérations & Diagnostic CVC",
                TitleEn = "Operations & HVAC Diagnostic Assistant",
                SubtitleFr = "Maintenance prédictive, détection d'anomalies et analyse de pannes",
                SubtitleEn = "Predictive maintenance, sensor anomaly detection, and root-cause analysis",
                Accelerator = "⚡ NPU (Intel AI Boost) / CPU",
                LatencyPowerFr = "Latence : ~240 ms • Puissance : ~2.5 W • Énergie : ~0.6 J",
                LatencyPowerEn = "Latency: ~240 ms • Power: ~2.5 W • Energy: ~0.6 J",
                PitchFr = "Un technicien de maintenance sur un site industriel isolé (hangar d'une entreprise du secteur aéronautique, centrale, chaufferie) n'a pas de réseau. Il saisit les symptômes de la centrale de traitement d'air et obtient un diagnostic prédictif instantané et un plan d'action guidé 100% hors-ligne.",
                PitchEn = "A maintenance technician in an isolated facility (hangar of an aerospace sector enterprise, plant, boiler room) has zero connectivity. Entering ventilation sensor symptoms yields an instant predictive diagnosis and guided repair checklist 100% offline.",
                DescriptionFr = "Cette démo modélise un système expert d'analyse des anomalies thermodynamiques sur des centrales de traitement d'air (CVC). L'IA analyse les pressions, températures, débits et codes erreurs pour identifier la cause racine (filtre colmaté, vanne grippée, fuite frigorifique) et prescrire les interventions préventives.",
                DescriptionEn = "This demo simulates an expert thermodynamic anomaly diagnostic system for HVAC and industrial ventilation plants. The AI analyzes pressures, temperatures, airflow, and fault codes to pinpoint root causes (clogged filters, stuck valves, refrigerant leaks) and recommend preventive steps.",
                TechnicalSpecsFr = "Modèle de raisonnement SLM (Small Language Model) quantifié 4-bit optimisé pour le NPU. Analyse contextuelle avec prompt d'ingénierie spécifique au génie climatique et industriel.",
                TechnicalSpecsEn = "4-bit quantized Small Language Model (SLM) optimized for NPU execution. Contextual extraction with prompt engineering tailored to HVAC and thermodynamic engineering.",
                BinariesAndModelsFr = "Scénarios intégrés dans OperationsView.xaml.cs avec injection de télémétrie industrielle.",
                BinariesAndModelsEn = "Embedded scenarios in OperationsView.xaml.cs with industrial telemetry injection.",
                HowToRunFr = "Sélectionner un scénario d'incident (ex: Filtre CVC colmaté), choisir l'accélérateur NPU, et cliquer sur 'Diagnostiquer l'incident'.",
                HowToRunEn = "Select an incident scenario (e.g. Clogged HVAC Filter), pick NPU accelerator, and click 'Diagnose Incident'."
            },
            new DocTopic
            {
                Key = "demo_engineering",
                Category = "Demo",
                Icon = "⚙️",
                TitleFr = "Ingénierie & RAG Hybride",
                TitleEn = "Engineering & Hybrid Local RAG",
                SubtitleFr = "Recherche documentaire vectorielle dans les manuels techniques et synthèse",
                SubtitleEn = "Vector retrieval through technical aircraft manuals and local synthesis",
                Accelerator = "⚡ NPU (Embeddings) + GPU (Rerank)",
                LatencyPowerFr = "Latence : ~310 ms • Puissance : ~3.2 W • Énergie : ~0.99 J",
                LatencyPowerEn = "Latency: ~310 ms • Power: ~3.2 W • Energy: ~0.99 J",
                PitchFr = "Les ingénieurs d'une entreprise du secteur aéronautique travaillent sur des manuels techniques ultra-confidentiels (turboréacteurs, turbines, trains d'atterrissage). Ce RAG local retrouve les spécifications exactes et tolérances d'usinage sans qu'aucune page ne fuite sur internet.",
                PitchEn = "Engineers of an aerospace sector enterprise work on proprietary technical manuals (jet engines, turbines, landing gear). This local RAG retrieves exact manufacturing tolerances and procedures with zero risk of page leakage to public clouds.",
                DescriptionFr = "Le système RAG (Retrieval-Augmented Generation) hybride combine une recherche sémantique vectorielle dense et une recherche lexicale (BM25) dans une base de manuels de maintenance aéronautique. L'IA cite précisément les paragraphes et chapitres d'origine pour une traçabilité totale.",
                DescriptionEn = "The hybrid RAG system pairs dense vector semantic embeddings with BM25 lexical search over aerospace maintenance documentation. The AI cites precise sections and chapters for full traceability.",
                TechnicalSpecsFr = "Embeddings calculés localement avec modèles type BGE / MiniLM OpenVINO. Index vectoriel en mémoire dans le processus .NET 8.",
                TechnicalSpecsEn = "Locally generated embeddings via BGE / MiniLM OpenVINO models. In-memory vector indexing within the .NET 8 host process.",
                BinariesAndModelsFr = "Index vectoriel des documents stocké sous Data/Documents/.",
                BinariesAndModelsEn = "Vector document index stored under Data/Documents/.",
                HowToRunFr = "Choisir une question technique (ex: Procédure de remplacement aube de turbine) et cliquer sur 'Rechercher & Synthétiser'.",
                HowToRunEn = "Select a technical question (e.g. Turbine blade replacement procedure) and click 'Search & Synthesize'."
            },
            new DocTopic
            {
                Key = "demo_insurance",
                Category = "Demo",
                Icon = "📋",
                TitleFr = "Assurance Sinistres & Analyse Visuelle",
                TitleEn = "Insurance Claims & Visual Damage Assessment",
                SubtitleFr = "Évaluation automatisée de dommages et estimation de devis de réparation",
                SubtitleEn = "Automated visual damage assessment and repair cost estimation",
                Accelerator = "🎮 GPU Copilot+ (Vision) + ⚡ NPU",
                LatencyPowerFr = "Latence : ~180 ms • Puissance : ~8.5 W • Énergie : ~1.5 J",
                LatencyPowerEn = "Latency: ~180 ms • Power: ~8.5 W • Energy: ~1.5 J",
                PitchFr = "Un expert d'assurance sur le terrain photographie un véhicule endommagé ou une toiture sinistrée. L'IA identifie les pièces touchées, quantifie la sévérité et chiffre instantanément le devis de remise en état en local.",
                PitchEn = "An insurance adjuster in the field photographs a damaged vehicle or building. The local AI identifies impacted parts, grades severity, and quotes the estimated repair cost immediately on-site.",
                DescriptionFr = "Analyse multimodale associant détection visuelle d'impacts et génération de rapport d'expertise chiffré. Le système compare les photos avant/après sinistre et génère une ventilation détaillée des pièces de rechange et heures de main-d'œuvre.",
                DescriptionEn = "Multimodal inspection combining visual impact detection and costed claim adjustment. The system compares pre/post incident photos and itemizes replacement parts and labor hours.",
                TechnicalSpecsFr = "Pipeline vision OpenVINO couplé à une base de tarification barémique locale. Détection multi-objets pour la carrosserie et les bris d'éléments.",
                TechnicalSpecsEn = "OpenVINO vision pipeline paired with a local insurance cost reference database. Multi-object damage localization.",
                BinariesAndModelsFr = "Échantillons d'images automobiles et de sinistres sous Data/Images/.",
                BinariesAndModelsEn = "Sample automotive and property claims imagery in Data/Images/.",
                HowToRunFr = "Sélectionner un dossier de sinistre (Choc avant ou Carrosserie) et lancer 'Analyser le sinistre'.",
                HowToRunEn = "Select a claim file (Frontal Impact or Bodywork) and click 'Analyze Claim'."
            },
            new DocTopic
            {
                Key = "demo_financial",
                Category = "Demo",
                Icon = "💼",
                TitleFr = "Services Financiers & Scoring ESG",
                TitleEn = "Financial Services & ESG Risk Scoring",
                SubtitleFr = "Audit d'entreprises, conformité réglementaire et analyse de risques",
                SubtitleEn = "Corporate audits, regulatory compliance, and risk profiling",
                Accelerator = "⚡ NPU Copilot+ (40+ TOPS)",
                LatencyPowerFr = "Latence : ~290 ms • Puissance : ~2.5 W • Énergie : ~0.72 J",
                LatencyPowerEn = "Latency: ~290 ms • Power: ~2.5 W • Energy: ~0.72 J",
                PitchFr = "Les banques d'affaires et auditeurs manipulent des états financiers confidentiels et des données M&A sensibles. Avec l'IA locale, vous pouvez scorer le risque ESG et détecter des fraudes sans jamais enfreindre le secret bancaire.",
                PitchEn = "Investment bankers and auditors handle confidential financial statements and M&A data. With local AI, ESG compliance scoring and anomaly detection occur without risking banking secrecy.",
                DescriptionFr = "Évaluation instantanée de rapports annuels d'entreprises. Le modèle extrait les métriques clés d'émissions carbone (Scope 1, 2, 3), de gouvernance et d'exposition aux risques financiers, et produit une note globale ESG et un audit de conformité CSRD.",
                DescriptionEn = "Instant screening of corporate annual filings. The model extracts key carbon metrics (Scope 1, 2, 3), governance criteria, and debt exposure to generate a comprehensive ESG score and CSRD compliance report.",
                TechnicalSpecsFr = "Extraction sémantique et calcul de scoring multi-critères sur NPU en basse consommation.",
                TechnicalSpecsEn = "Semantic information extraction and multi-criteria scoring executed on low-power NPU.",
                BinariesAndModelsFr = "Bilans financiers et profils de sociétés pré-intégrés.",
                BinariesAndModelsEn = "Pre-loaded corporate financial statements and balance sheets.",
                HowToRunFr = "Choisir une société cible et cliquer sur 'Générer l'Analyse ESG & Risques'.",
                HowToRunEn = "Choose a target enterprise and click 'Generate ESG & Risk Analysis'."
            },
            new DocTopic
            {
                Key = "demo_benchmark",
                Category = "Demo",
                Icon = "🔋",
                TitleFr = "Benchmark Watts & Duel NPU (OpenVINO IR vs Modèle Brut vs Olive)",
                TitleEn = "Silicon Energy Benchmark & NPU Duel (OpenVINO IR vs Raw ONNX vs Olive)",
                SubtitleFr = "Endurance LLM et duel comparatif d'états de modèles sur NPU Copilot+",
                SubtitleEn = "LLM endurance test and model state comparative duel on Copilot+ NPU",
                Accelerator = "⚡ NPU Copilot+ (40+ TOPS) 100% Silicium Dédié",
                LatencyPowerFr = "OpenVINO IR : ~7.4 ms / 135 FPS • Modèle Brut ONNX : ~7.0 ms / 143 FPS • Modèle Olive NPU : ~9.0 ms / 110 FPS (-50% RAM) • ~1.6 W",
                LatencyPowerEn = "OpenVINO IR: ~7.4 ms / 135 FPS • Raw ONNX: ~7.0 ms / 143 FPS • Olive NPU Model: ~9.0 ms / 110 FPS (-50% RAM) • ~1.6 W",
                PitchFr = "La démonstration absolue de l'efficience NPU : comparez l'endurance LLM locale (Phi-4-mini sur NPU) et lancez le 'Duel NPU' confrontant trois états d'un modèle de vision sur silicium Copilot+ (Intel AI Boost, AMD XDNA, Qualcomm Hexagon). Découvrez comment le format OpenVINO IR excelle en réactivité, comment le modèle brut ONNX est compilé directement à la volée sur NPU, et comment Microsoft Olive divise par deux l'empreinte mémoire (-50% RAM) pour co-héberger plusieurs modèles en mémoire NPU partagée sans chauffer ni vider la batterie.",
                PitchEn = "The definitive demonstration of NPU efficiency: benchmark sustained local SLM inference (Phi-4-mini on NPU) or launch the 'NPU Duel' pitting three states of a vision neural model on Copilot+ silicon (Intel AI Boost, AMD XDNA, Qualcomm Hexagon). Discover how OpenVINO IR excels in peak latency, how raw ONNX is compiled on-the-fly directly on NPU, and how Microsoft Olive cuts memory footprint in half (-50% RAM) to co-host multiple neural models in shared NPU memory.",
                DescriptionFr = "Ce banc d'essai propose deux modes complémentaires :\n1. ⚡ Endurance LLM : exécute en boucle Phi-4-mini sur NPU pour mesurer la cadence (tok/s), l'échauffement thermique et l'autonomie projetée sur batterie (jusqu'à 10-12h d'usage mixte).\n2. ⚔️ Duel Silicium NPU (OpenVINO IR vs Modèle Brut ONNX vs Modèle Olive) : infère le réseau de segmentation (YOLOv8n-Seg) sur le NPU dans des conditions strictement étalonnées, traquant temps de compilation NPU, taille mémoire (Mo), latence moyenne (ms), débit (FPS) et puissance instantanée (1.6 W).",
                DescriptionEn = "This testbench features two complementary modes:\n1. ⚡ LLM Endurance: continuously runs Phi-4-mini on NPU, profiling token throughput, thermal stability, and projected battery autonomy (up to 10-12 hours in mixed usage).\n2. ⚔️ NPU Silicon Duel (OpenVINO IR vs Raw ONNX vs Olive NPU): benchmarks neural vision segmentation (YOLOv8n-Seg) on the physical NPU, profiling compilation time, memory footprint (MB), average latency (ms), frame rate (FPS), and live power draw (1.6 W).",
                TechnicalSpecsFr = "Triple compilation matérielle sur NPU Copilot+ (Level Zero VPUX / DirectML / QNN) : OpenVINO IR natif (.xml/.bin) vs graphe ONNX standard FP32 vs graphe ONNX optimisé Microsoft Olive (FP16). Mesures matérielles de décharge batterie ACPI/WMI en direct.",
                TechnicalSpecsEn = "Hardware tri-compilation on Copilot+ NPU (Level Zero VPUX / DirectML / QNN): Native OpenVINO IR (.xml/.bin) vs Standard ONNX FP32 graph vs Microsoft Olive FP16 graph. Live ACPI/WMI battery discharge hardware telemetry.",
                BinariesAndModelsFr = "• runtime_benchmark_service.py\n• models/yolov8n-seg_openvino_model/ & models/yolov8n-seg.onnx & models/yolov8n-seg_olive.onnx\n• Client Foundry local (Phi-4-mini)",
                BinariesAndModelsEn = "• runtime_benchmark_service.py\n• models/yolov8n-seg_openvino_model/ & models/yolov8n-seg.onnx & models/yolov8n-seg_olive.onnx\n• Local Foundry client (Phi-4-mini)",
                HowToRunFr = "Choisir le mode ('Endurance LLM' ou 'Duel NPU : OpenVINO IR vs Modèle Brut vs Olive'), régler l'intensité et cliquer sur 'Lancer le Duel de Runtimes'.",
                HowToRunEn = "Select workload type ('LLM Endurance' or 'NPU Duel: OpenVINO IR vs Raw ONNX vs Olive'), set duration/intensity, and click 'Start Engine Duel'."
            },
            new DocTopic
            {
                Key = "demo_audio",
                Category = "Demo",
                Icon = "🎙️",
                TitleFr = "Transcription Vocale Whisper Souveraine",
                TitleEn = "Sovereign Whisper Voice Transcription",
                SubtitleFr = "Speech-to-Text ultra-rapide et confidentiel sur modèle Whisper OpenVINO",
                SubtitleEn = "Ultra-fast confidential Speech-to-Text powered by Whisper OpenVINO",
                Accelerator = "⚡ NPU Copilot+ (40+ TOPS) / GPU DirectML",
                LatencyPowerFr = "Facteur temps réel : 15x plus rapide que la parole • ~2.8 W",
                LatencyPowerEn = "Real-time factor: 15x faster than spoken word • ~2.8 W",
                PitchFr = "Enregistrez une réunion de direction ou une communication de vol. La transcription mot à mot s'affiche quasi-instantanément sans aucun serveur cloud, garantissant le secret professionnel le plus strict.",
                PitchEn = "Record an executive board meeting or cockpit communication. Word-for-word transcriptions appear almost instantaneously with zero cloud servers, guaranteeing absolute confidentiality.",
                DescriptionFr = "Studio de transcription audio tirant parti du modèle OpenAI Whisper optimisé en FP16 sous OpenVINO. L'outil gère le français, l'anglais et le multilingue avec détection automatique de la langue, horodatage des phrases et diarisation des interlocuteurs.",
                DescriptionEn = "Audio transcription studio leveraging OpenAI Whisper FP16 optimized under OpenVINO. Features automated language detection, sentence-level timestamps, and speaker segmentation.",
                TechnicalSpecsFr = "Modèles Whisper Tiny / Base convertis en IR OpenVINO. Traitement du spectrogramme Mel et décodage autorégressif accéléré sur NPU.",
                TechnicalSpecsEn = "Whisper Tiny and Base models converted to OpenVINO Intermediate Representation. Fast Mel-spectrogram processing and autoregressive decoding accelerated on NPU.",
                BinariesAndModelsFr = "• models/whisper-tiny-fp16-ov/ & models/whisper-base-fp16-ov/\\n• Fichiers audio de test dans Data/Audio/ (reunions, cockpit, support)",
                BinariesAndModelsEn = "• models/whisper-tiny-fp16-ov/ & models/whisper-base-fp16-ov/\\n• Audio test clips in Data/Audio/ (meetings, cockpit, dispatch)",
                HowToRunFr = "Sélectionner un fichier audio d'échantillon ou enregistrer votre micro, choisir le NPU et cliquer sur 'Transcrire'.",
                HowToRunEn = "Select an audio sample file or record via your microphone, choose NPU, and click 'Transcribe'."
            },
            new DocTopic
            {
                Key = "demo_avatar",
                Category = "Demo",
                Icon = "🎨",
                TitleFr = "Studio Avatar Cartoon 3D & Vintage",
                TitleEn = "Cartoon 3D & Vintage Avatar Studio",
                SubtitleFr = "Stylisation d'images et transfert de style IA accéléré",
                SubtitleEn = "On-device AI style transfer and portrait generation",
                Accelerator = "⚡ NPU Copilot+ (40+ TOPS) / 🎮 GPU DirectML",
                LatencyPowerFr = "Latence : ~1.2 s par image • Énergie : ~3.6 J",
                LatencyPowerEn = "Latency: ~1.2 s per image • Energy: ~3.6 J",
                PitchFr = "Transformez un portrait en avatar Cartoon 3D, Anime, Cyberpunk ou style Années 60 en un clin d'œil, sans envoyer la photo de vos collaborateurs sur des serveurs tiers.",
                PitchEn = "Transform any portrait into a 3D Cartoon, Anime, Cyberpunk, or 1960s vintage avatar in seconds, without sending employee photos to third-party servers.",
                DescriptionFr = "Studio de génération et de transformation visuelle appliquant des modèles de diffusion compacts (LCM) et des filtres neuronaux de transfert de style. Conserve l'identité du visage tout en adaptant la palette, les textures et l'éclairage au style choisi.",
                DescriptionEn = "Visual transformation and generation studio utilizing compact diffusion models (LCM) and neural style transfer. Preserves facial geometry and identity while altering palette, textures, and lighting.",
                TechnicalSpecsFr = "Pipeline LCM (Latent Consistency Model) en 4 à 8 étapes d'échantillonnage, optimisé pour la mémoire unifiée LPDDR5X.",
                TechnicalSpecsEn = "LCM (Latent Consistency Model) 4-8 step sampling pipeline optimized for LPDDR5X unified memory architecture.",
                BinariesAndModelsFr = "• models/lcm_dreamshaper_8bit/\\n• Data/Images/ (portraits d'échantillon)",
                BinariesAndModelsEn = "• models/lcm_dreamshaper_8bit/\\n• Data/Images/ (sample portrait photos)",
                HowToRunFr = "Choisir une photo, sélectionner le style (ex: Cartoon 3D ou Années 60) et cliquer sur '✨ Lancer la Conversion de Style'.",
                HowToRunEn = "Pick a photo, select a style (e.g. 3D Cartoon or 1960s Vintage), and click '✨ Start Style Conversion'."
            },
            new DocTopic
            {
                Key = "demo_eraser",
                Category = "Demo",
                Icon = "🧹",
                TitleFr = "Gomme Magique & Inpainting (Tri-Moteur)",
                TitleEn = "Magic Eraser & Neural Inpainting (Tri-Engine)",
                SubtitleFr = "Segmentation sémantique et suppression intelligente (OpenVINO / ONNX / Olive FP16)",
                SubtitleEn = "Semantic object segmentation and neural inpainting (OpenVINO / ONNX / Olive FP16)",
                Accelerator = "⚡ Triple Moteur : OpenVINO natif, ONNX DirectML ou ONNX Olive FP16",
                LatencyPowerFr = "OpenVINO : ~19 ms (NPU) • Olive FP16 : ~28 ms (GPU) • ONNX : ~43 ms",
                LatencyPowerEn = "OpenVINO: ~19 ms (NPU) • Olive FP16: ~28 ms (GPU) • ONNX: ~43 ms",
                PitchFr = "Nettoyez instantanément une photo technique ou industrielle en supprimant un élément indésirable, un reflet ou un déchet. Vous pouvez comparer les performances entre OpenVINO natif (NPU), ONNX standard et le modèle FP16 optimisé par Microsoft Olive.",
                PitchEn = "Clean up technical photography instantly by removing unwanted objects, reflections, or debris. Seamlessly compare runtime speeds across native OpenVINO (NPU), standard ONNX, and Microsoft Olive FP16.",
                DescriptionFr = "Outil de retouche photo combinant un réseau de segmentation d'instance (YOLOv8-Seg) pour identifier avec précision le contour des objets et un algorithme d'inpainting. Trois moteurs d'exécution sont sélectionnables : OpenVINO Level Zero, ONNX DirectML standard et ONNX compilé par Microsoft Olive (FP16, 50% d'empreinte mémoire en moins).",
                DescriptionEn = "Photo editing tool pairing instance segmentation (YOLOv8-Seg) with context-aware inpainting. Three interchangeable runtime options: native OpenVINO Level Zero, standard ONNX DirectML, and Microsoft Olive FP16 (50% smaller footprint).",
                TechnicalSpecsFr = "Modèles YOLOv8n-Seg en 3 variantes : OpenVINO IR, ONNX standard FP32 et ONNX Olive FP16 avec graphe fusionné.",
                TechnicalSpecsEn = "YOLOv8n-Seg models across 3 variants: OpenVINO IR, standard ONNX FP32, and Microsoft Olive FP16 with graph fusions.",
                BinariesAndModelsFr = "• models/yolov8n-seg_openvino_model/\n• models/yolov8n-seg.onnx (13 Mo)\n• models/yolov8n-seg_olive.onnx (7 Mo optimisé par Microsoft Olive)\n• scripts/optimize_with_olive.py",
                BinariesAndModelsEn = "• models/yolov8n-seg_openvino_model/\n• models/yolov8n-seg.onnx (13 MB)\n• models/yolov8n-seg_olive.onnx (7 MB Microsoft Olive FP16)\n• scripts/optimize_with_olive.py",
                HowToRunFr = "Choisir le moteur et le silicium, charger une photo, cliquer sur l'objet à effacer puis sur '🎯 Détecter & Détourer'.",
                HowToRunEn = "Select engine and silicon, load an image, click on the object to remove, then click '🎯 Detect & Segment'."
            },
            new DocTopic
            {
                Key = "demo_pdf",
                Category = "Demo",
                Icon = "📑",
                TitleFr = "Studio PDF & OCR Souverain",
                TitleEn = "Sovereign PDF Studio & Local OCR",
                SubtitleFr = "Numérisation, extraction tabulaire et anonymisation de documents",
                SubtitleEn = "Document scanning, tabular data extraction, and confidential redaction",
                Accelerator = "⚡ NPU Copilot+ (40+ TOPS)",
                LatencyPowerFr = "Traitement : ~80 ms par page • Puissance : ~2.5 W",
                LatencyPowerEn = "Processing: ~80 ms per page • Power: ~2.5 W",
                PitchFr = "Traitez des contrats d'embauche, des fiches de paie ou des brevets aéronautiques en toute sécurité. Le système extrait les tableaux et masque automatiquement les données personnelles (PII) sans recourir à un service cloud externe.",
                PitchEn = "Process employment contracts, payroll sheets, and aerospace patents with zero leak risk. The engine extracts tabular data and auto-redacts PII without any external cloud service.",
                DescriptionFr = "Plateforme d'ingestion et d'analyse de documents PDF numérisés. Comprend la reconnaissance optique de caractères (OCR) multilingue, la détection automatique de métadonnées, l'extraction de tableaux structurés et l'anonymisation automatique des noms, numéros de sécurité sociale et coordonnées.",
                DescriptionEn = "Ingestion and extraction platform for digitized PDF documents. Features multilingual optical character recognition (OCR), metadata parsing, structured table recovery, and PII anonymization.",
                TechnicalSpecsFr = "Moteur OCR local couplé à des règles de conformité RGPD et des expressions régulières neuronales.",
                TechnicalSpecsEn = "On-device OCR engine paired with GDPR compliance pattern matching and neural regex extractors.",
                BinariesAndModelsFr = "Exemples de documents et contrats dans Data/Documents/.",
                BinariesAndModelsEn = "Sample contracts and technical briefs in Data/Documents/.",
                HowToRunFr = "Sélectionner un document d'exemple et cliquer sur 'Extraire le Texte & Anonymiser'.",
                HowToRunEn = "Pick a sample PDF and click 'Extract Text & Anonymize'."
            },
            new DocTopic
            {
                Key = "demo_security",
                Category = "Demo",
                Icon = "🛡️",
                TitleFr = "Contrôle d'Accès Zero-Trust & Démineur (Tri-Moteur OpenVINO / ONNX / Olive)",
                TitleEn = "Zero-Trust Security Access & Sovereign Minesweeper (Tri-Engine)",
                SubtitleFr = "Vérification visuelle 3/3, sélection OpenVINO / ONNX DirectML / Olive FP16",
                SubtitleEn = "3/3 multimodal visual gating, OpenVINO / ONNX DirectML / Olive FP16 selector",
                Accelerator = "⚡ Triple Moteur : OpenVINO natif, ONNX Runtime DirectML ou ONNX Olive FP16",
                LatencyPowerFr = "OpenVINO : ~8.7 ms (GPU) • Olive FP16 : ~14 ms • ONNX : ~23 ms",
                LatencyPowerEn = "OpenVINO: ~8.7 ms (GPU) • Olive FP16: ~14 ms • ONNX: ~23 ms",
                PitchFr = "Présentez cette démo spectaculaire et ludique : pour déverrouiller la console exécutive, l'IA Zero-Trust exige la détection conjointe de 3 prérequis (Tasse de café ☕, Lunettes 👓 et Casque audio 🎧). L'opérateur peut basculer en un clic entre OpenVINO natif, ONNX Runtime DirectML et le modèle optimisé Microsoft Olive FP16. Dès que les 3 éléments sont identifiés, la caméra s'éteint pour laisser place au jeu de Démineur souverain !",
                PitchEn = "Deliver an engaging and high-impact demo: to unlock the secure executive console, the Zero-Trust vision model demands simultaneous presence of 3 prerequisites (Coffee Cup ☕, Eyeglasses 👓, and Headset 🎧). Operators can toggle with a single click between high-performance OpenVINO, ONNX Runtime DirectML, and Microsoft Olive FP16. Once validated, the camera powers down and unveils the centered Minesweeper game!",
                DescriptionFr = "Scénario de sécurité physique et ergonomique vérifiant la conformité de l'opérateur avant de déverrouiller un poste sensible :\n1. ☕ Tasse de café (indicateur de vigilance opérationnelle)\n2. 👓 Paire de lunettes (équipement de précision visuelle)\n3. 🎧 Casque audio (isolation phonique et communication)\nLe modèle analyse le flux webcam en direct à haute fréquence d'images. Trois moteurs d'inférence sont interchangeables à la volée : OpenVINO natif, ONNX Runtime DirectML standard et ONNX compilé par Microsoft Olive (FP16). Dès validation 3/3, le panneau de scan disparaît au profit du jeu de Démineur 9x9.",
                DescriptionEn = "Physical and ergonomic security protocol attesting operator compliance before unlocking a sensitive workstation:\n1. ☕ Coffee Cup (vigilance and operational readiness)\n2. 👓 Eyeglasses (visual precision equipment)\n3. 🎧 Audio Headset (hearing protection and communications)\nThe model monitors the live webcam with high framerates. Three interchangeable inference engines are supported on the fly: native OpenVINO, standard ONNX Runtime DirectML, and Microsoft Olive FP16. Upon 3/3 validation, the scanner folds away and the centered 9x9 Minesweeper board takes over.",
                TechnicalSpecsFr = "Architecture Tri-Moteur : YOLOv8s-World exporté en OpenVINO IR (XML/BIN), ONNX FP32 opset 17 et ONNX FP16 via Microsoft Olive. 27 classes sémantiques calibrées, boîtes néon HUD (Doré/Jaune/Magenta), seuil de confiance 0.20.",
                TechnicalSpecsEn = "Tri-Engine architecture: YOLOv8s-World exported to OpenVINO IR (XML/BIN), ONNX FP32 opset 17, and Microsoft Olive FP16. 27 specialized semantic classes, neon HUD bounding boxes (Gold/Yellow/Magenta), 0.20 confidence threshold.",
                BinariesAndModelsFr = "• models/yolov8s-world_openvino_model/ (68 Mo)\n• models/yolov8s-world.onnx (50 Mo)\n• models/yolov8s-world_olive.onnx (25 Mo optimisé Microsoft Olive FP16)\n• Script caméra : security_access_service.py (--engine openvino/onnx/onnx_olive)\n• Script d'optimisation : scripts/optimize_with_olive.py\n• Échantillons : Data/Images/access_samples/",
                BinariesAndModelsEn = "• models/yolov8s-world_openvino_model/ (68 MB)\n• models/yolov8s-world.onnx (50 MB)\n• models/yolov8s-world_olive.onnx (25 MB Microsoft Olive FP16)\n• Camera service: security_access_service.py (--engine openvino/onnx/onnx_olive)\n• Optimization script: scripts/optimize_with_olive.py\n• Test samples: Data/Images/access_samples/",
                HowToRunFr = "Choisir le moteur (OpenVINO, ONNX ou Olive FP16), lancer la webcam ou cliquer sur '☕+👓+🎧 Valide (3/3)' pour déverrouiller instantanément.",
                HowToRunEn = "Select engine (OpenVINO, ONNX or Olive FP16), launch webcam or click '☕+👓+🎧 Valid (3/3)' to unlock instantly."
            },
            new DocTopic
            {
                Key = "demo_telemetry",
                Category = "Demo",
                Icon = "📊",
                TitleFr = "Télémétrie & Audit Flotte",
                TitleEn = "Fleet Telemetry & Governance Console",
                SubtitleFr = "Console d'administration DSI, traçabilité et métriques d'usage",
                SubtitleEn = "IT Enterprise administration console, compliance logs, and usage analytics",
                Accelerator = "Moteur de Télémétrie Local C#",
                LatencyPowerFr = "Zéro impact CPU/GPU • Journalisation asynchrone non-bloquante",
                LatencyPowerEn = "Zero CPU/GPU overhead • Non-blocking async logging",
                PitchFr = "Rassurez les directeurs informatiques (DSI) et RSSI : l'IA locale ne signifie pas perte de contrôle. Toutes les inférences sont journalisées dans un tableau de bord centralisé permettant d'auditer les modèles utilisés, la latence et les économies d'énergie à l'échelle d'une flotte de milliers de PC.",
                PitchEn = "Reassure enterprise CIOs and CISOs: on-device AI does not mean loss of governance. All inference events are logged to a centralized dashboard tracking models, latencies, and fleet-wide energy savings across thousands of laptops.",
                DescriptionFr = "Console de supervision d'entreprise pour la gestion de parc Copilot+. Présente l'historique détaillé des inférences, la répartition par silicium (NPU vs GPU vs CPU), les temps de réponse moyens, ainsi que l'empreinte carbone totale évitée par l'usage de puces locales par rapport au cloud.",
                DescriptionEn = "Enterprise management dashboard for Copilot+ device fleets. Visualizes inference logs, silicon distribution (NPU vs GPU vs CPU), latency percentiles, and cumulative carbon savings achieved through local hardware.",
                TechnicalSpecsFr = "Stockage local JSON Lines horodaté avec calcul de statistiques agrégées (P50, P90, total Joules).",
                TechnicalSpecsEn = "Local timestamped JSON Lines persistence with real-time aggregate statistics (P50, P90, total Joules).",
                BinariesAndModelsFr = "Logs enregistrés sous DemoHub/Logs/fleet_telemetry.jsonl.",
                BinariesAndModelsEn = "Logs saved under DemoHub/Logs/fleet_telemetry.jsonl.",
                HowToRunFr = "Ouvrir 'Télémétrie & Audit Flotte' depuis la barre de navigation de gauche ou le bouton dédié en haut.",
                HowToRunEn = "Open 'Fleet Telemetry & Audit' from the left navigation sidebar or top header shortcut."
            },
            new DocTopic
            {
                Key = "demo_olivelab",
                Category = "Demo",
                Icon = "🧪",
                TitleFr = "Laboratoire d'Optimisation Microsoft Olive (Duel Télémétrie)",
                TitleEn = "Microsoft Olive AI Optimization Lab (Live Duel)",
                SubtitleFr = "Banc d'essai comparatif direct : Modèle Brut vs Modèle Optimisé par Olive",
                SubtitleEn = "Head-to-head benchmarking: Raw vs Olive-Optimized Models",
                Accelerator = "⚡ Silicium Copilot+ (NPU 40+ TOPS / GPU DirectML / CPU)",
                LatencyPowerFr = "Réduction taille : -50% • Accélération : jusqu'à +80% • Énergie : -45%",
                LatencyPowerEn = "Size Reduction: -50% • Speedup: up to +80% • Energy: -45%",
                PitchFr = "Démontrez concrètement l'intérêt de Microsoft Olive pour les déploiements sur PC Copilot+. En un clic, lancez un duel côte à côte entre le modèle brut non optimisé (FP32) et la version compilée par Olive (FP16/quantifiée) sur 3 cas d'usage réels (Vision YOLOv8s-World, Détourage YOLOv8n-Seg et OCR PaddleOCR), ainsi qu'un mode Trio Multi-Agents MCP avec exécution concurrente et protocole JSON-RPC 2.0 en direct. Visualisez l'accélération, l'économie de RAM, la division par deux du poids sur disque et le gain d'autonomie en Joules, tout en prouvant que la précision et la fidélité visuelle restent à 100%.",
                PitchEn = "Demonstrate the real-world ROI of Microsoft Olive on Copilot+ PCs. With one click, launch a head-to-head live duel between raw unoptimized models (FP32) and Olive-optimized builds (FP16/quantized) across 3 production use cases (YOLOv8s-World Vision, YOLOv8n-Seg Segmentation, and PaddleOCR Text Recognition), plus an authentic MCP Multi-Agent Trio executing concurrently via live JSON-RPC 2.0 frames. See live latency gains, RAM footprint drop, 50% SSD savings, and battery Joules saved, while verifying 100% output fidelity.",
                DescriptionFr = "Le Laboratoire d'Optimisation Olive permet d'évaluer scientifiquement les gains apportés par Microsoft Olive sur le silicium Copilot+ (Intel Core Ultra, AMD Ryzen AI, Qualcomm Snapdragon X) :\n• Vision par ordinateur (YOLOv8s-World) : 48 Mo (brut) vs 24 Mo (Olive FP16).\n• Segmentation & Détourage (YOLOv8n-Seg) : 13.2 Mo (brut) vs 6.7 Mo (Olive FP16).\n• OCR & Reconnaissance de documents (PP-OCRv4 Rec) : 10.8 Mo (brut) vs 5.5 Mo (Olive FP16).\n• Trio Multi-Agents MCP : 3 agents autonomes exécutant en simultané des outils vision, OCR et détourage via un serveur Model Context Protocol (MCP) JSON-RPC 2.0 local. Démontre la densité mémoire et l'absence de saturation VRAM permise par Olive.\nLe mode 'Duel Simultané' exécute les deux variantes avec les mêmes entrées, calcule le facteur d'accélération (Speedup), la réduction d'empreinte mémoire RSS et affiche la télémétrie énergétique certifiée.",
                DescriptionEn = "The Olive Optimization Lab provides a scientific benchmark of Microsoft Olive benefits on Copilot+ silicon (Intel Core Ultra, AMD Ryzen AI, Qualcomm Snapdragon X):\n• Computer Vision (YOLOv8s-World): 48 MB (raw) vs 24 MB (Olive FP16).\n• Segmentation & Matting (YOLOv8n-Seg): 13.2 MB (raw) vs 6.7 MB (Olive FP16).\n• OCR & Document Recognition (PP-OCRv4 Rec): 10.8 MB (raw) vs 5.5 MB (Olive FP16).\n• MCP Multi-Agent Trio: 3 autonomous agents concurrently calling vision, OCR, and segmentation tools via a local Model Context Protocol (MCP) JSON-RPC 2.0 server. Highlights memory density and eliminates VRAM saturation under agentic load.\nThe 'Simultaneous Duel' mode runs both variants against identical inputs, measuring real-time speedup ratio, RAM RSS delta, and certified battery Joules consumption.",
                TechnicalSpecsFr = "Pipeline Olive : graph fusion, constant folding, élimination des casts redondants, conversion demi-précision IEEE 754 FP16 et optimisation des passes d'exécution ONNX Runtime. Serveur MCP conforme spécification Anthropic JSON-RPC 2.0 (méthodes tools/list et tools/call) avec ThreadPoolExecutor 3 cœurs.",
                TechnicalSpecsEn = "Olive Pipeline: graph fusion, constant folding, redundant cast elimination, IEEE 754 FP16 conversion, and ONNX Runtime execution provider graph tuning. MCP server adhering to Anthropic JSON-RPC 2.0 specs (tools/list and tools/call methods) with 3-worker ThreadPoolExecutor.",
                BinariesAndModelsFr = "• olive_lab_service.py & olive_mcp_service.py\n• models/yolov8s-world.onnx & models/yolov8s-world_olive.onnx\n• models/yolov8n-seg.onnx & models/yolov8n-seg_olive.onnx\n• models/ocr/ch_PP-OCRv4_rec_infer.onnx & models/ocr/ch_PP-OCRv4_rec_infer_olive.onnx",
                BinariesAndModelsEn = "• olive_lab_service.py & olive_mcp_service.py\n• models/yolov8s-world.onnx & models/yolov8s-world_olive.onnx\n• models/yolov8n-seg.onnx & models/yolov8n-seg_olive.onnx\n• models/ocr/ch_PP-OCRv4_rec_infer.onnx & models/ocr/ch_PP-OCRv4_rec_infer_olive.onnx",
                HowToRunFr = "Sélectionner le modèle désiré ou le 'Trio Multi-Agents MCP', choisir le mode 'Duel Télémétrique (Brut vs Olive)' et cliquer sur '🚀 Lancer le Benchmark Télémétrique Olive'.",
                HowToRunEn = "Select the desired model or 'MCP Multi-Agent Trio', choose 'Telemetry Duel (Raw vs Olive)' and click '🚀 Run Olive Benchmark Duel'."
            },
        };
    }
}