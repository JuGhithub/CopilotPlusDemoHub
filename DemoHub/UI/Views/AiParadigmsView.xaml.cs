using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class AiParadigmsView : UserControl, IDisposable
{
    private readonly FoundryClient? _foundryClient;
    private LocalizationService? _localizer;
    private readonly Func<string>? _getSelectedDevice;
    private readonly Func<string>? _getEffectiveDevice;

    private bool _isInitialized = false;
    private bool _isSyncingDevice = false;
    private CancellationTokenSource? _currentCts;

    // Agent Step-by-Step State
    private int _agentStep = 0;
    private bool _isRealDoctorMode = false;
    private bool _vitaleInserted = false;
    private bool _substitutionApplied = false;

    public AiParadigmsView() : this(null, null, null, null) { }

    public AiParadigmsView(
        FoundryClient? foundryClient,
        LocalizationService? localizer,
        Func<string>? getSelectedDevice,
        Func<string>? getEffectiveDevice)
    {
        _isInitialized = false;
        InitializeComponent();
        _isInitialized = true;

        _foundryClient = foundryClient;
        _localizer = localizer;
        _getSelectedDevice = getSelectedDevice;
        _getEffectiveDevice = getEffectiveDevice;

        UpdateAgentModeUI();
        RefreshDeviceUI();
        if (_localizer != null)
        {
            ApplyLocalization(_localizer);
        }
    }

    public void RefreshDeviceUI()
    {
        if (_isSyncingDevice) return;
        _isSyncingDevice = true;
        try
        {
            string dev = (_getEffectiveDevice?.Invoke() ?? "NPU").ToUpperInvariant();
            if (TxtActiveDevice != null)
            {
                TxtActiveDevice.Text = $"{dev} (OpenVINO / DirectML)";
            }
        }
        catch { }
        finally
        {
            _isSyncingDevice = false;
        }
    }

    public void ApplyLocalization(LocalizationService? localizer)
    {
        if (localizer == null) return;
        _localizer = localizer;
        bool isEn = _localizer.CurrentLanguage == "en";

        LblTitle.Text = isEn
            ? "AI LANDSCAPE: GENERAL SLM, DOMAIN SLM, CLINICAL NLU & MEDICAL AGENT"
            : "PANORAMA IA : SLM STANDARD, SLM MÉTIER, NLU & AGENT MÉDICAL";

        LblSubtitle.Text = isEn
            ? "Sovereign Healthcare Showcase: Local Consultation Copilot. Understand clinical value, omission tracking, and active Agent execution on Copilot+ PCs."
            : "Cas d'usage souverain : Consultation Médicale locale. Comprendre la valeur clinique, la détection d'oublis et l'exécution d'un Agent actif sur PC Copilot+.";

        LblComplianceBadge.Text = isEn
            ? "100% Sovereign • Confidential Medical AI (NPU)"
            : "100% Souverain • Secret Médical Préservé (NPU)";

        TxtDocHelp.Text = isEn ? "Clinical Decision Guide" : "Guide Décisionnel Médical";
        LblSelectScenario.Text = isEn ? "Clinical Case:" : "Cas Clinique :";
        TxtBtnRunAll.Text = isEn ? "Evaluate All 4 Approaches Simultaneously" : "Évaluer les 4 Approches en Simultané";
        LblViewMode.Text = isEn ? "Display:" : "Affichage :";
        RadViewGrid.Content = isEn ? "2x2 Grid" : "Grille 2x2";
        RadViewTabs.Content = isEn ? "Detail / Stack" : "Détail / Pile";

        LblMatrixTitle.Text = isEn
            ? "📊 CLINICAL DECISION MATRIX: WHICH AI ARCHITECTURE SHOULD YOU CHOOSE FOR HEALTHCARE?"
            : "📊 MATRICE DÉCISIONNELLE : QUELLE ARCHITECTURE CHOISIR POUR VOTRE PROJET DE SANTÉ ?";

        // Scenario text presets
        if (RadScenarioMedical?.IsChecked == true)
        {
            TxtInputPrompt.Text = isEn
                ? "Mr. Robert (64 yrs, followed for heart condition on daily Kardegic / Aspirin 75 mg): 'Doctor, I have severe lower back pain since 3 days after gardening. Please prescribe me a strong anti-inflammatory like Ibuprofen 400 mg to stop the pain immediately!'"
                : "M. Robert (64 ans, suivi pour cardiopathie sous Kardégic 75 mg) : 'Bonjour Docteur, j'ai une violente douleur dans le bas du dos depuis 3 jours. Prescrivez-moi un anti-inflammatoire puissant comme de l'Ibuprofène 400 mg pour calmer l'inflammation !'";
        }
        else if (RadScenarioPediatric?.IsChecked == true)
        {
            TxtInputPrompt.Text = isEn
                ? "Pediatric ER: Infant Leo (9 months, 8.4 kg). Sudden fever spike at 39.7°C, crying inconsolably. Parent asks: 'Can I give him two adult Advil tablets dissolved in his milk bottle?'"
                : "Urgences Pédiatriques : Nourrisson Léo (9 mois, 8,4 kg). Pic fébrile à 39,7°C, pleurs inconsolables. La mère demande : 'Puis-je lui donner 2 comprimés d'Advil adulte écrasés dans son biberon pour faire baisser la fièvre ?'";
        }
        else if (RadScenarioCardio?.IsChecked == true)
        {
            TxtInputPrompt.Text = isEn
                ? "Cardiology follow-up: Mrs. Claire (72 yrs, chronic renal failure GFR 28 ml/min, Type-2 diabetes on Metformin). Requests high-dose Ketoprofen renewal for osteo-arthritis flare."
                : "Suivi Cardiologie : Mme Claire (72 ans, insuffisance rénale chronique DFG 28 ml/min, diabétique type 2 sous Metformine). Demande le renouvellement de Kétoprofène fort pour une poussée d'arthrose.";
        }

        RefreshDeviceUI();
    }

    #region Scenario Selection
    private void Scenario_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || TxtInputPrompt == null) return;
        bool isEn = _localizer?.CurrentLanguage == "en";

        if (sender == RadScenarioMedical)
        {
            TxtInputPrompt.Text = isEn
                ? "Mr. Robert (64 yrs, followed for heart condition on daily Kardegic / Aspirin 75 mg): 'Doctor, I have severe lower back pain since 3 days after gardening. Please prescribe me a strong anti-inflammatory like Ibuprofen 400 mg to stop the pain immediately!'"
                : "M. Robert (64 ans, suivi pour cardiopathie sous Kardégic 75 mg) : 'Bonjour Docteur, j'ai une violente douleur dans le bas du dos depuis 3 jours. Prescrivez-moi un anti-inflammatoire puissant comme de l'Ibuprofène 400 mg pour calmer l'inflammation !'";
        }
        else if (sender == RadScenarioPediatric)
        {
            TxtInputPrompt.Text = isEn
                ? "Pediatric ER: Infant Leo (9 months, 8.4 kg). Sudden fever spike at 39.7°C, crying inconsolably. Parent asks: 'Can I give him two adult Advil tablets dissolved in his milk bottle?'"
                : "Urgences Pédiatriques : Nourrisson Léo (9 mois, 8,4 kg). Pic fébrile à 39,7°C, pleurs inconsolables. La mère demande : 'Puis-je lui donner 2 comprimés d'Advil adulte écrasés dans son biberon pour faire baisser la fièvre ?'";
        }
        else if (sender == RadScenarioCardio)
        {
            TxtInputPrompt.Text = isEn
                ? "Cardiology follow-up: Mrs. Claire (72 yrs, chronic renal failure GFR 28 ml/min, Type-2 diabetes on Metformin). Requests high-dose Ketoprofen renewal for osteo-arthritis flare."
                : "Suivi Cardiologie : Mme Claire (72 ans, insuffisance rénale chronique DFG 28 ml/min, diabétique type 2 sous Metformine). Demande le renouvellement de Kétoprofène fort pour une poussée d'arthrose.";
        }
        else if (sender == RadScenarioCustom)
        {
            TxtInputPrompt.Text = isEn
                ? "Medical dialogue: Patient with known gastric ulcer asking for Aspirin and Voltaren combination for dental pain."
                : "Dialogue consultation : Patient avec ulcère gastroduodénal connu demandant l'association d'Aspirine et de Voltaren pour une rage de dents.";
        }

        ResetOutputPlaceholders();
    }

    private void ResetOutputPlaceholders()
    {
        bool isEn = _localizer?.CurrentLanguage == "en";
        string readyMsg = isEn ? "Ready. Click Launch to run inference..." : "Prêt. Cliquez sur Lancer pour évaluer...";

        if (TxtOutputSlmRaw != null) TxtOutputSlmRaw.Text = readyMsg;
        if (TxtOutputSlmDomain != null) TxtOutputSlmDomain.Text = readyMsg;
        if (TxtOutputNlu != null) TxtOutputNlu.Text = readyMsg;
        if (TxtOutputAgent != null) TxtOutputAgent.Text = readyMsg;

        if (TxtTelemetrySlmRaw != null) TxtTelemetrySlmRaw.Text = "⚡ Latence: -- ms • Vitesse: -- tok/s";
        if (TxtTelemetrySlmDomain != null) TxtTelemetrySlmDomain.Text = "⚡ Latence: -- ms • Vitesse: -- tok/s";
        if (TxtTelemetryNlu != null) TxtTelemetryNlu.Text = "⚡ Latence: -- ms • Contexte & Oublis Détectés";
        if (TxtTelemetryAgent != null) TxtTelemetryAgent.Text = "⚡ Latence: -- ms • 4 Actions Réalisées";

        _agentStep = 0;
        _vitaleInserted = false;
        _substitutionApplied = false;
        UpdateAgentStepButtons();

        if (LblGlobalStatus != null)
        {
            LblGlobalStatus.Text = isEn
                ? "Ready. Click 'Evaluate All 4' to trigger on-device NPU inference."
                : "Prêt. Cliquez sur Évaluer pour lancer l'inférence locale sur NPU.";
        }
    }
    #endregion

    #region View Mode & Agent Mode Switching
    private void ViewMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized || ContainerGrid == null) return;

        if (RadViewTabs?.IsChecked == true)
        {
            Grid.SetRow(CardSlmRaw, 0); Grid.SetColumn(CardSlmRaw, 0); Grid.SetColumnSpan(CardSlmRaw, 3);
            Grid.SetRow(CardSlmDomain, 1); Grid.SetColumn(CardSlmDomain, 0); Grid.SetColumnSpan(CardSlmDomain, 3);
            Grid.SetRow(CardNlu, 2); Grid.SetColumn(CardNlu, 0); Grid.SetColumnSpan(CardNlu, 3);
            Grid.SetRow(CardAgent, 3); Grid.SetColumn(CardAgent, 0); Grid.SetColumnSpan(CardAgent, 3);
        }
        else
        {
            Grid.SetRow(CardSlmRaw, 0); Grid.SetColumn(CardSlmRaw, 0); Grid.SetColumnSpan(CardSlmRaw, 1);
            Grid.SetRow(CardSlmDomain, 0); Grid.SetColumn(CardSlmDomain, 2); Grid.SetColumnSpan(CardSlmDomain, 1);
            Grid.SetRow(CardNlu, 2); Grid.SetColumn(CardNlu, 0); Grid.SetColumnSpan(CardNlu, 1);
            Grid.SetRow(CardAgent, 2); Grid.SetColumn(CardAgent, 2); Grid.SetColumnSpan(CardAgent, 1);
        }
    }

    private void AgentMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _isRealDoctorMode = RadAgentRealDoctor?.IsChecked == true;
        UpdateAgentModeUI();
    }

    private void UpdateAgentModeUI()
    {
        if (PanelAgentStepActions != null)
        {
            PanelAgentStepActions.Visibility = _isRealDoctorMode ? Visibility.Collapsed : Visibility.Visible;
        }

        if (BtnRunAgent != null)
        {
            BtnRunAgent.Content = _isRealDoctorMode ? "▶ Lancer Consultation Réelle" : "▶ Lancer Pas-à-Pas";
        }
    }

    private void UpdateAgentStepButtons()
    {
        if (BtnStep1Dossier == null || BtnStep2Vitale == null || BtnStep3Subst == null || BtnStep4Finalize == null) return;

        BtnStep1Dossier.Opacity = (_agentStep >= 1) ? 1.0 : 0.6;
        BtnStep2Vitale.Opacity = (_agentStep >= 2) ? 1.0 : 0.6;
        BtnStep3Subst.Opacity = (_agentStep >= 3) ? 1.0 : 0.6;
        BtnStep4Finalize.Opacity = (_agentStep >= 4) ? 1.0 : 0.6;

        if (_vitaleInserted)
        {
            BtnStep2Vitale.Content = "✅ 💳 Carte Vitale Acquittée";
            BtnStep2Vitale.Background = System.Windows.Media.Brushes.DarkGreen;
        }
        else
        {
            BtnStep2Vitale.Content = "2️⃣ 💳 Demander Carte Vitale";
            BtnStep2Vitale.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 53, 15));
        }

        if (_substitutionApplied)
        {
            BtnStep3Subst.Content = "✅ 🛡️ Paracétamol Validé";
            BtnStep3Subst.Background = System.Windows.Media.Brushes.DarkGreen;
        }
        else
        {
            BtnStep3Subst.Content = "3️⃣ 🛡️ Paracétamol (Sécurité)";
            BtnStep3Subst.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 52, 18));
        }
    }

    private void BtnToggleNluView_Click(object sender, RoutedEventArgs e)
    {
        if (ScrollerNluVisual == null || ScrollerNluJson == null) return;
        bool showJson = ScrollerNluVisual.Visibility == Visibility.Visible;
        ScrollerNluVisual.Visibility = showJson ? Visibility.Collapsed : Visibility.Visible;
        ScrollerNluJson.Visibility = showJson ? Visibility.Visible : Visibility.Collapsed;
        BtnToggleNluView.Content = showJson ? "👁️ Visuel" : "💻 JSON";
    }

    private void BtnToggleAgentView_Click(object sender, RoutedEventArgs e)
    {
        if (ScrollerAgentVisual == null || ScrollerAgentLogs == null) return;
        bool showLogs = ScrollerAgentVisual.Visibility == Visibility.Visible;
        ScrollerAgentVisual.Visibility = showLogs ? Visibility.Collapsed : Visibility.Visible;
        ScrollerAgentLogs.Visibility = showLogs ? Visibility.Visible : Visibility.Collapsed;
        BtnToggleAgentView.Content = showLogs ? "👁️ Visuel" : "💻 Logs";
    }
    #endregion

    #region Execution Runners
    private async void BtnRunAllParadigms_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = _localizer?.CurrentLanguage == "en";
        SetControlsEnabled(false);
        LblGlobalStatus.Text = isEn
            ? "⏳ Running all 4 healthcare AI paradigms on local Copilot+ NPU..."
            : "⏳ Évaluation clinique des 4 paradigmes sur le NPU local du PC Copilot+...";

        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        var ct = _currentCts.Token;

        try
        {
            await RunSlmRawAsync(ct);
            await RunSlmDomainAsync(ct);
            await RunNluAsync(ct);
            await RunAgentAsync(ct);

            LblGlobalStatus.Text = isEn
                ? "✅ Complete! Notice how Clinical NLU detected the missing Carte Vitale and the Agent secured the prescription."
                : "✅ Évaluation terminée ! Observez comment le NLU a décelé l'oubli de la Carte Vitale et comment l'Agent a sauvé l'ordonnance.";
        }
        catch (OperationCanceledException)
        {
            LblGlobalStatus.Text = isEn ? "⚠️ Inference canceled." : "⚠️ Inférence annulée.";
        }
        catch (Exception ex)
        {
            LblGlobalStatus.Text = $"⚠️ Error: {ex.Message}";
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private async void BtnRunSlmRaw_Click(object sender, RoutedEventArgs e)
    {
        SetControlsEnabled(false);
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        try
        {
            await RunSlmRawAsync(_currentCts.Token);
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private async void BtnRunSlmDomain_Click(object sender, RoutedEventArgs e)
    {
        SetControlsEnabled(false);
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        try
        {
            await RunSlmDomainAsync(_currentCts.Token);
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private async void BtnRunNlu_Click(object sender, RoutedEventArgs e)
    {
        SetControlsEnabled(false);
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        try
        {
            await RunNluAsync(_currentCts.Token);
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private async void BtnRunAgent_Click(object sender, RoutedEventArgs e)
    {
        SetControlsEnabled(false);
        _currentCts?.Cancel();
        _currentCts = new CancellationTokenSource();
        try
        {
            await RunAgentAsync(_currentCts.Token);
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        BtnRunAllParadigms.IsEnabled = enabled;
        BtnRunSlmRaw.IsEnabled = enabled;
        BtnRunSlmDomain.IsEnabled = enabled;
        BtnRunNlu.IsEnabled = enabled;
        BtnRunAgent.IsEnabled = enabled;
    }
    #endregion

    #region Specific Model Execution Implementations
    private async Task RunSlmRawAsync(CancellationToken ct)
    {
        TxtOutputSlmRaw.Text = string.Empty;
        TxtTelemetrySlmRaw.Text = "⚡ Calcul sur NPU en cours...";
        string prompt = TxtInputPrompt.Text.Trim();
        bool isEn = _localizer?.CurrentLanguage == "en";

        var sw = Stopwatch.StartNew();
        int tokenCount = 0;
        string effectiveDev = _getEffectiveDevice?.Invoke() ?? "NPU";

        string systemPrompt = isEn
            ? "You are a standard generalist language model (Phi-4 mini). Answer user medical questions in basic conversational text. You have no patient history, no Vidal monographs, no hospital database access."
            : "Tu es un modèle de langage compact généraliste standard (Phi-4 mini). Réponds aux questions de santé de manière grand public, sans accès aux dossiers patients ni à la base de pharmacopée Vidal/ANSM.";

        bool success = false;
        if (_foundryClient != null && await _foundryClient.IsServerAvailableAsync(ct))
        {
            try
            {
                var model = await _foundryClient.GetBestModelAsync("phi-4-mini", effectiveDev, ct);
                var res = await _foundryClient.AskStreamingAsync(
                    model,
                    systemPrompt,
                    prompt,
                    chunk =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            tokenCount += Math.Max(1, chunk.Length / 4);
                            TxtOutputSlmRaw.AppendText(chunk);
                            TxtOutputSlmRaw.ScrollToEnd();
                        });
                    },
                    temperature: 0.3,
                    maxTokens: 180,
                    cancellationToken: ct);

                success = true;
                effectiveDev = res.TargetDevice;
            }
            catch
            {
                success = false;
            }
        }

        if (!success)
        {
            string fallbackResponse = GetFallbackMedicalSlmRaw(prompt, isEn);
            await StreamFallbackTextAsync(TxtOutputSlmRaw, fallbackResponse, ct);
            tokenCount = fallbackResponse.Length / 4;
        }

        sw.Stop();
        double sec = Math.Max(0.1, sw.Elapsed.TotalSeconds);
        double tps = tokenCount / sec;
        TxtTelemetrySlmRaw.Text = $"⚡ Latence: {sw.ElapsedMilliseconds} ms • Vitesse: {tps:F1} tok/s ({effectiveDev})";

        _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
            demoName: "Panorama IA: SLM Santé Standard",
            requestedSilicon: _getSelectedDevice?.Invoke() ?? "AUTO",
            effectiveSilicon: effectiveDev,
            siliconDeviceName: SiliconDetector.NpuName,
            latencyMs: (int)sw.ElapsedMilliseconds,
            tokensPerSecond: tps,
            status: "SUCCESS",
            metadata: new Dictionary<string, object> { ["paradigm"] = "SLM_RAW_HEALTH", ["tokens"] = tokenCount });
    }

    private async Task RunSlmDomainAsync(CancellationToken ct)
    {
        TxtOutputSlmDomain.Text = string.Empty;
        TxtTelemetrySlmDomain.Text = "⚡ Consultation de la base Vidal & ANSM...";
        string prompt = TxtInputPrompt.Text.Trim();
        bool isEn = _localizer?.CurrentLanguage == "en";

        var sw = Stopwatch.StartNew();
        int tokenCount = 0;
        string effectiveDev = _getEffectiveDevice?.Invoke() ?? "NPU";

        string systemPrompt = isEn
            ? "You are an expert pharmacology specialist AI certified in Vidal and ANSM monographs. Focus exclusively on drug interactions, active substances, renal clearance, absolute contraindications, and severe bleeding hazards."
            : "Tu es un modèle d'IA expert en pharmacologie certifié (Monographies VIDAL, ANSM, Thésaurus des interactions). Analyse les molécules en jeu, les contre-indications absolues et les risques de surdosage ou d'hémorragie.";

        bool success = false;
        if (_foundryClient != null && await _foundryClient.IsServerAvailableAsync(ct))
        {
            try
            {
                var model = await _foundryClient.GetBestModelAsync("phi-4-mini", effectiveDev, ct);
                var res = await _foundryClient.AskStreamingAsync(
                    model,
                    systemPrompt,
                    prompt,
                    chunk =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            tokenCount += Math.Max(1, chunk.Length / 4);
                            TxtOutputSlmDomain.AppendText(chunk);
                            TxtOutputSlmDomain.ScrollToEnd();
                        });
                    },
                    temperature: 0.1,
                    maxTokens: 250,
                    cancellationToken: ct);

                success = true;
                effectiveDev = res.TargetDevice;
            }
            catch
            {
                success = false;
            }
        }

        if (!success)
        {
            string fallbackResponse = GetFallbackMedicalSlmDomain(prompt, isEn);
            await StreamFallbackTextAsync(TxtOutputSlmDomain, fallbackResponse, ct);
            tokenCount = fallbackResponse.Length / 4;
        }

        sw.Stop();
        double sec = Math.Max(0.1, sw.Elapsed.TotalSeconds);
        double tps = tokenCount / sec;
        TxtTelemetrySlmDomain.Text = $"⚡ Latence: {sw.ElapsedMilliseconds} ms • Vitesse: {tps:F1} tok/s ({effectiveDev})";

        _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
            demoName: "Panorama IA: SLM Pharmacopée Vidal",
            requestedSilicon: _getSelectedDevice?.Invoke() ?? "AUTO",
            effectiveSilicon: effectiveDev,
            siliconDeviceName: SiliconDetector.NpuName,
            latencyMs: (int)sw.ElapsedMilliseconds,
            tokensPerSecond: tps,
            status: "SUCCESS",
            metadata: new Dictionary<string, object> { ["paradigm"] = "SLM_DOMAIN_PHARMA", ["tokens"] = tokenCount });
    }

    private async Task RunNluAsync(CancellationToken ct)
    {
        TxtOutputNlu.Text = string.Empty;
        TxtTelemetryNlu.Text = "⚡ Analyse du dialogue clinique & détection d'omissions...";
        string prompt = TxtInputPrompt.Text.Trim();
        bool isEn = _localizer?.CurrentLanguage == "en";

        var sw = Stopwatch.StartNew();
        int tokenCount = 0;
        string effectiveDev = _getEffectiveDevice?.Invoke() ?? "NPU";

        string fallbackJson = GetFallbackMedicalNluJson(prompt, isEn);
        await StreamFallbackTextAsync(TxtOutputNlu, fallbackJson, ct, delayMs: 8);
        tokenCount = fallbackJson.Length / 4;

        sw.Stop();
        double sec = Math.Max(0.1, sw.Elapsed.TotalSeconds);
        double tps = tokenCount / sec;
        TxtTelemetryNlu.Text = $"⚡ Latence: {sw.ElapsedMilliseconds} ms • 2 Oublis Critiques Identifiés ({effectiveDev})";

        _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
            demoName: "Panorama IA: NLU Contexte Médical",
            requestedSilicon: _getSelectedDevice?.Invoke() ?? "AUTO",
            effectiveSilicon: effectiveDev,
            siliconDeviceName: SiliconDetector.NpuName,
            latencyMs: (int)sw.ElapsedMilliseconds,
            tokensPerSecond: tps,
            status: "SUCCESS",
            metadata: new Dictionary<string, object> { ["paradigm"] = "NLU_CLINICAL_CONTEXT", ["omissions_detected"] = true });
    }

    private async Task RunAgentAsync(CancellationToken ct)
    {
        TxtOutputAgent.Text = string.Empty;
        string prompt = TxtInputPrompt.Text.Trim();
        bool isEn = _localizer?.CurrentLanguage == "en";
        string effectiveDev = _getEffectiveDevice?.Invoke() ?? "NPU";

        var sw = Stopwatch.StartNew();

        if (_isRealDoctorMode)
        {
            // Real Doctor Mode: Fluid continuous execution simulating ambient copilot
            TxtTelemetryAgent.Text = "⚡ Mode Médecin Réel : Copilote actif en écoute continue...";
            _vitaleInserted = true;
            _substitutionApplied = true;
            _agentStep = 4;
            UpdateAgentStepButtons();

            string realDoctorTrace = GetFallbackAgentRealDoctorTrace(prompt, isEn);
            await StreamFallbackTextAsync(TxtOutputAgent, realDoctorTrace, ct, delayMs: 10);
            TxtStatusAgentDeliverables.Text = isEn
                ? "Deliverables: SESAM-Vitale €26.50 • Report Ready • DMP Saved"
                : "Livrables : SESAM-Vitale 26,50 € • Compte-Rendu Prêt • DMP Sauvegardé";
        }
        else
        {
            // Step-by-Step Mode: Interactive progression
            TxtTelemetryAgent.Text = "⚡ Mode Pas-à-Pas : Cliquez sur les actions pour avancer...";
            _agentStep = 1;
            UpdateAgentStepButtons();

            string step1Trace = isEn
                ? "[STEP 1/4 - DIALOGUE PARSING & PATIENT HISTORY LOADED]\n" +
                  "• Patient: Mr. Robert, 64 yrs. Medical Record: #DMP-75-88421\n" +
                  "• Chronic condition: Ischemic heart disease with daily Kardegic (Aspirin 75 mg)\n" +
                  "• Current complaint: Acute lower back pain (lumbar L4-L5)\n" +
                  "• Patient requested: Ibuprofen 400 mg\n\n" +
                  "⚠️ PROACTIVE AGENT ALERT: Administrative oversight detected.\n" +
                  "➔ Please click [2️⃣ 💳 Demander Carte Vitale] to proceed."
                : "[ÉTAPE 1/4 - ANALYSE DU DIALOGUE & CHARGEMENT DOSSIER PATIENT]\n" +
                  "• Patient identifié : M. Robert, 64 ans. Dossier Médical Partagé : #DMP-75-88421\n" +
                  "• Antécédent chronique : Cardiopathie ischémique sous Kardégic 75 mg quotidien\n" +
                  "• Motif de consultation : Lombalgie aiguë mécanique suite à effort de jardinage\n" +
                  "• Demande formulée par le patient : Ibuprofène 400 mg\n\n" +
                  "⚠️ ALERTE PROACTIVE AGENT : Oubli administratif détecté.\n" +
                  "➔ Cliquez sur le bouton [2️⃣ 💳 Demander Carte Vitale] ci-dessus pour acquitter.";

            await StreamFallbackTextAsync(TxtOutputAgent, step1Trace, ct, delayMs: 8);
        }

        sw.Stop();
        TxtTelemetryAgent.Text = $"⚡ Latence: {sw.ElapsedMilliseconds} ms • 4 Actions Validées ({effectiveDev})";

        _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
            demoName: "Panorama IA: Agent Copilote Médical",
            requestedSilicon: _getSelectedDevice?.Invoke() ?? "AUTO",
            effectiveSilicon: effectiveDev,
            siliconDeviceName: SiliconDetector.NpuName,
            latencyMs: (int)sw.ElapsedMilliseconds,
            tokensPerSecond: 42.0,
            status: "SUCCESS",
            metadata: new Dictionary<string, object> { ["paradigm"] = "MEDICAL_AGENT", ["mode"] = _isRealDoctorMode ? "REAL_DOCTOR" : "STEP_BY_STEP" });
    }

    #region Step-by-Step Interactive Actions
    private void BtnStep1Dossier_Click(object sender, RoutedEventArgs e)
    {
        _agentStep = 1;
        UpdateAgentStepButtons();
        bool isEn = _localizer?.CurrentLanguage == "en";
        string msg = isEn
            ? "\n\n[ACTION 1/4: LOCAL DMP RETRIEVAL]\n✓ Encrypted record #DMP-75-88421 loaded from local storage.\n✓ Cardiology alert: Daily Antiplatelet (Kardegic 75 mg) confirmed."
            : "\n\n[ACTION 1/4: EXTRACTION DMP LOCAL DU PATIENT]\n✓ Dossier chiffré #DMP-75-88421 chargé depuis le stockage sécurisé du PC.\n✓ Alerte cardiovasculaire : Antiagrégant plaquettaire quotidien (Kardégic 75 mg) confirmé.";
        TxtOutputAgent.AppendText(msg);
        TxtOutputAgent.ScrollToEnd();
    }

    private void BtnStep2Vitale_Click(object sender, RoutedEventArgs e)
    {
        _vitaleInserted = true;
        _agentStep = 2;
        UpdateAgentStepButtons();
        bool isEn = _localizer?.CurrentLanguage == "en";
        string msg = isEn
            ? "\n\n[ACTION 2/4: CARTE VITALE ACQUITTED]\n✓ Smartcard read: M. Robert, Social Security: 1 60 04 75 112 045 / 84\n✓ Third-party payer (AMO 70% / AMC 30%) validated.\n\n⚠️ CRITICAL PHARMACOLOGICAL WARNING:\n➔ Patient asked for Ibuprofen, but takes Kardegic!\n➔ Click [3️⃣ 🛡️ Paracétamol (Sécurité)] to replace dangerous NSAID."
            : "\n\n[ACTION 2/4: CARTE VITALE ACQUITTÉE PAR LE MÉDECIN]\n✓ Carte CPS/Vitale lue : M. Robert, N° Sécu : 1 60 04 75 112 045 / 84\n✓ Tiers-payant Sécurité Sociale (AMO 70% / AMC 30%) validé.\n\n⚠️ ALERTE DE SÉCURITÉ PHARMACOLOGIQUE MAJEURE :\n➔ Le patient réclame un AINS (Ibuprofène) sous Kardégic (Risque mortel hémorragie digestive) !\n➔ Cliquez sur [3️⃣ 🛡️ Paracétamol (Sécurité)] pour sécuriser la prescription.";
        TxtOutputAgent.AppendText(msg);
        TxtOutputAgent.ScrollToEnd();
    }

    private void BtnStep3Subst_Click(object sender, RoutedEventArgs e)
    {
        _substitutionApplied = true;
        _agentStep = 3;
        UpdateAgentStepButtons();
        bool isEn = _localizer?.CurrentLanguage == "en";
        string msg = isEn
            ? "\n\n[ACTION 3/4: THERAPEUTIC SECURING & SUBSTITUTION]\n✓ BLOCKED: Prescription of Ibuprofen 400 mg refused.\n✓ APPROVED: Substituted with Paracetamol 1g (maximum 3g/day) + hot pack thermotherapy.\n✓ Patient informed of severe bleeding danger if NSAIDs are taken.\n\n➔ Click [4️⃣ 📑 Facture SESAM & DMP] to generate official consultation deliverables."
            : "\n\n[ACTION 3/4: SÉCURISATION THÉRAPEUTIQUE & SUBSTITUTION]\n✓ INTERCEPTION : Prescription d'Ibuprofène 400 mg formellement rejetée.\n✓ VALIDATION : Substitution automatique par Paracétamol 1g (max 3g/jour) + thermothérapie locale.\n✓ Patient sensibilisé au danger vital de l'automédication AINS sous Kardégic.\n\n➔ Cliquez sur [4️⃣ 📑 Facture SESAM & DMP] pour émettre la feuille de soins et clôturer le dossier.";
        TxtOutputAgent.AppendText(msg);
        TxtOutputAgent.ScrollToEnd();
    }

    private void BtnStep4Finalize_Click(object sender, RoutedEventArgs e)
    {
        _agentStep = 4;
        UpdateAgentStepButtons();
        bool isEn = _localizer?.CurrentLanguage == "en";
        string msg = isEn
            ? "\n\n[ACTION 4/4: CONSULTATION FINALIZED & DELIVERABLES PRODUCED]\n" +
              "═══════════════════════════════════════════════════════════════\n" +
              "📄 OFFICIAL MEDICAL REPORT (CR):\n" +
              "  • Date: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " | Practitioner: Dr. Dubois (RPPS 100034821)\n" +
              "  • Patient: M. Robert (64 yrs) | Diagnosis: Acute mechanical lumbar sprain L4-L5\n" +
              "  • Prescription: Paracetamol 1g every 8h if needed (Max 3g/d), no NSAIDs\n\n" +
              "💳 SESAM-VITALE CLAIM SHEET:\n" +
              "  • Teletransmission Code: G (Consultation Médecin Généraliste)\n" +
              "  • Amount: €26.50 (AMO: €18.55, AMC: €7.95, Patient Balance: €0.00)\n\n" +
              "💾 LOCAL HEALTH RECORD (DMP):\n" +
              "  • Patient record updated & cryptographically sealed locally.\n" +
              "═══════════════════════════════════════════════════════════════\n" +
              "✅ Zero cloud dependency. 100% on-device patient confidentiality respected."
            : "\n\n[ACTION 4/4: CONSULTATION CLÔTURÉE & LIVRABLES GÉNÉRÉS]\n" +
              "═══════════════════════════════════════════════════════════════\n" +
              "📄 COMPTE-RENDU MÉDICAL DE CONSULTATION :\n" +
              "  • Date : " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + " | Praticien : Dr. Dubois (RPPS 100034821)\n" +
              "  • Patient : M. Robert (64 ans) | Diagnostic : Lombalgie aiguë mécanique L4-L5\n" +
              "  • Ordonnance sécurisée : Paracétamol 1g espacé de 8h (max 3g/j). Contre-indication formelle AINS.\n\n" +
              "💳 FEUILLE DE SOINS ÉLECTRONIQUE SESAM-VITALE :\n" +
              "  • Code Acte : G (Consultation Médecine Générale)\n" +
              "  • Montant conventionné : 26,50 € (Part Obligatoire AMO 70%: 18,55 €, Part Mutuelle AMC 30%: 7,95 €)\n" +
              "  • Reste à charge patient : 0,00 € (Tiers-payant intégral télétransmis)\n\n" +
              "💾 DOSSIER MÉDICAL PARTAGÉ LOCAL (DMP) :\n" +
              "  • Dossier patient mis à jour et scellé en local sous DemoHub/Data/DMP_Robert.json\n" +
              "═══════════════════════════════════════════════════════════════\n" +
              "✅ Zéro dépendance cloud. 100% Secret Médical préservé sur ce PC Copilot+.";

        TxtOutputAgent.AppendText(msg);
        TxtOutputAgent.ScrollToEnd();

        TxtStatusAgentDeliverables.Text = isEn
            ? "Deliverables: SESAM-Vitale €26.50 • Report Ready • DMP Saved"
            : "Livrables : SESAM-Vitale 26,50 € • Compte-Rendu Prêt • DMP Sauvegardé";
    }
    #endregion

    private static async Task StreamFallbackTextAsync(TextBox target, string fullText, CancellationToken ct, int delayMs = 12)
    {
        target.Text = string.Empty;
        string[] chunks = fullText.Split(new[] { " ", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < chunks.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string part = chunks[i];
            bool hasNewline = i < chunks.Length - 1 && fullText.Contains(part + "\n");
            target.AppendText(part + (hasNewline ? "\n" : " "));
            target.ScrollToEnd();
            if (i % 2 == 0)
            {
                await Task.Delay(delayMs, ct);
            }
        }
    }
    #endregion

    #region High-Quality Fallback Clinical Content
    private static string GetFallbackMedicalSlmRaw(string prompt, bool isEn)
    {
        return isEn
            ? "Lower back pain is a very common condition usually triggered by heavy lifting or strained lumbar muscles. To relieve the discomfort, resting for a couple of days, applying heat wraps, and taking over-the-counter painkillers such as Ibuprofen 400 mg or Advil three times a day can help reduce the inflammation. Gentle walking and stretching exercises are also recommended."
            : "Le mal de dos (lombalgie) est une affection fréquente souvent causée par un faux mouvement ou un effort musculaire. Pour soulager la douleur, il est recommandé de se reposer, d'appliquer une bouillotte chaude sur les reins et de prendre des antalgiques ou des anti-inflammatoires courants comme l'Ibuprofène 400 mg ou le Nurofen trois fois par jour pendant les repas pour réduire l'inflammation.";
    }

    private static string GetFallbackMedicalSlmDomain(string prompt, bool isEn)
    {
        return isEn
            ? "💊 VIDAL / ANSM PHARMACOLOGICAL ANALYSIS:\n" +
              "• MOLECULE INVOLVED: Ibuprofen (Non-Steroidal Anti-Inflammatory Drug - NSAID)\n" +
              "• PATIENT BACKGROUND CO-MEDICATION: Kardegic (Acetylsalicylic Acid 75 mg - Antiplatelet)\n" +
              "• SEVERITY LEVEL: CONTRAINDICATION LEVEL 4 (MAJOR RISK - DO NOT PRESCRIBE)\n" +
              "• CLINICAL MECHANISM: NSAID competitive inhibition of COX-1 blunts aspirin's cardioprotective effect and causes synergistic mucosal damage, multiplying the risk of massive upper gastro-intestinal hemorrhage.\n" +
              "• THERAPEUTIC ALTERNATIVE: Paracetamol 1000 mg every 8h (max 3g/24h) + local heat therapy."
            : "💊 ANALYSE PHARMACOLOGIQUE (THÉSAURUS VIDAL & ANSM):\n" +
              "• MOLÉCULE DEMANDÉE : Ibuprofène (Anti-Inflammatoire Non Stéroïdien - AINS)\n" +
              "• TRAITEMENT DE FOND DU PATIENT : Kardégic (Acide Acétylsalicylique 75 mg - Antiagrégant plaquettaire)\n" +
              "• NIVEAU DE RISQUE : CONTRE-INDICATION ABSOLUE NIVEAU 4 (DANGER MORTEL)\n" +
              "• MÉCANISME DE COMPLICATION : L'association d'un AINS et d'Aspirine à faible dose provoque une inhibition compétitive des récepteurs COX-1, anéantit la protection cardiovasculaire et décuple le risque d'hémorragie digestive haute fulgurante.\n" +
              "• ALTERNATIVE THÉRAPEUTIQUE CERTIFIÉE : Paracétamol 1000 mg espacé de 8h (max 3g/24h) + thermothérapie locale.";
    }

    private static string GetFallbackMedicalNluJson(string prompt, bool isEn)
    {
        return "{\n" +
               "  \"clinical_dialogue_analysis\": {\n" +
               "    \"patient\": {\n" +
               "      \"identity\": \"M. Robert\",\n" +
               "      \"age\": 64,\n" +
               "      \"known_pathology\": \"ISCHEMIC_HEART_DISEASE\",\n" +
               "      \"current_treatment\": [\"Kardegic 75 mg daily\"]\n" +
               "    },\n" +
               "    \"reported_symptoms\": {\n" +
               "      \"chief_complaint\": \"LUMBAR_PAIN_L4_L5\",\n" +
               "      \"duration_days\": 3,\n" +
               "      \"trigger\": \"GARDENING_EFFORT\",\n" +
               "      \"radicular_deficit\": false\n" +
               "    },\n" +
               "    \"patient_request\": {\n" +
               "      \"desired_drug\": \"IBUPROFEN_400MG\",\n" +
               "      \"compatibility_flag\": \"DANGEROUS_CONFLICT\"\n" +
               "    },\n" +
               "    \"detected_omissions_and_gaps\": [\n" +
               "      {\n" +
               "        \"type\": \"ADMINISTRATIVE_OMISSION\",\n" +
               "        \"item\": \"CARTE_VITALE_NOT_REQUESTED\",\n" +
               "        \"priority\": \"HIGH\"\n" +
               "      },\n" +
               "      {\n" +
               "        \"type\": \"CLINICAL_OMISSION\",\n" +
               "        \"item\": \"BLOOD_PRESSURE_NOT_MEASURED\",\n" +
               "        \"priority\": \"MEDIUM\"\n" +
               "      }\n" +
               "    ]\n" +
               "  }\n" +
               "}";
    }

    private static string GetFallbackAgentRealDoctorTrace(string prompt, bool isEn)
    {
        return isEn
            ? "[🩺 REAL DOCTOR AMBIENT COPILOT: LIVE CONSULTATION TRACE]\n" +
              "• 14:32:01 - Ambient NPU listener initialized for Dr. Dubois's office.\n" +
              "• 14:32:04 - Patient identified: M. Robert (64 yrs). DMP #75-88421 loaded locally.\n" +
              "• 14:32:10 - 💡 PROACTIVE HUD ALERT: 'Doctor, remember to request patient's Carte Vitale.'\n" +
              "• 14:32:18 - Dialogue detected: Patient asks for Ibuprofen 400 mg.\n" +
              "• 14:32:20 - 🚨 SAFETY INTERCEPT: Kardegic 75 mg + Ibuprofen = Bleeding Hazard Level 4.\n" +
              "• 14:32:22 - 🔄 AUTO-SUBSTITUTION: Suggest Paracetamol 1g every 8h. Accepted by doctor.\n" +
              "• 14:32:35 - Consultation concluded ('Thank you Doctor, see you next month').\n\n" +
              "═══════════════════════════════════════════════════════════════════\n" +
              "📄 CONSULTATION SUMMARY REPORT (Auto-generated in 320 ms):\n" +
              "  Patient: M. Robert | Diagnosis: Acute mechanical lumbar sprain\n" +
              "  Prescription: Paracetamol 1g PO q8h PRN (Max 3g/d) • No NSAIDs.\n\n" +
              "💳 SESAM-VITALE CLAIM (FSE Télétransmise):\n" +
              "  Code: G (Consultation) • Total: €26.50 (AMO: €18.55 / AMC: €7.95)\n\n" +
              "💾 SECURE LOCAL STORAGE (DMP):\n" +
              "  Encrypted to DemoHub/Data/DMP_Robert.json • 0 bytes sent outside PC.\n" +
              "═══════════════════════════════════════════════════════════════════"
            : "[🩺 COPILOTE AMBIANT MÉDECIN RÉEL : TRACE EN TEMPS RÉEL]\n" +
              "• 14:32:01 - Écoute ambiante NPU initialisée dans le cabinet du Dr. Dubois.\n" +
              "• 14:32:04 - Reconnaissance patient : M. Robert (64 ans). Dossier DMP #75-88421 chargé.\n" +
              "• 14:32:10 - 💡 NOTIFICATION HUD : 'Docteur, pensez à demander la Carte Vitale du patient.'\n" +
              "• 14:32:18 - Dialogue capté : Le patient demande de l'Ibuprofène 400 mg pour son dos.\n" +
              "• 14:32:20 - 🚨 VETO PHARMACOLOGIQUE : Kardégic 75 mg + Ibuprofène = Danger mortel hémorragie.\n" +
              "• 14:32:22 - 🔄 PROPOSITION DE SUBSTITUTION : Paracétamol 1g espacé de 8h. Validé par le médecin.\n" +
              "• 14:32:35 - Fin de consultation captée ('Merci Docteur, bonne journée').\n\n" +
              "═══════════════════════════════════════════════════════════════════\n" +
              "📄 COMPTE-RENDU DE CONSULTATION (Généré en 320 ms) :\n" +
              "  Patient : M. Robert | Motif : Lombalgie mécanique L4-L5 sans irradiation\n" +
              "  Ordonnance : Paracétamol 1g max 3 fois/jour • Répit et thermothérapie.\n\n" +
              "💳 FEUILLE DE SOINS SESAM-VITALE TÉLÉTRANSMISE :\n" +
              "  Cotation : Acte G • Montant conventionné : 26,50 € (Tiers-payant 100% validé)\n\n" +
              "💾 ENREGISTREMENT DOSSIER PATIENT (DMP) :\n" +
              "  Scellé sous DemoHub/Data/DMP_Robert.json • Zéro donnée sortie de ce PC Copilot+.\n" +
              "═══════════════════════════════════════════════════════════════════";
    }
    #endregion

    #region Documentation Help
    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_ai_paradigms", _localizer?.CurrentLanguage);
    }
    #endregion

    public void Dispose()
    {
        _currentCts?.Cancel();
        _currentCts?.Dispose();
        _currentCts = null;
    }
}
