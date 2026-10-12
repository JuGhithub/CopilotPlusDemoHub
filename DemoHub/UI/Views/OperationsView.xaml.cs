using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.Demos.Operations;

namespace CopilotPlusDemoHub.UI.Views;

public partial class OperationsView : UserControl
{
    private readonly FoundryClient _foundryClient;
    private readonly Func<string> _getDeviceFunc;
    private readonly Func<string> _getEffectiveDeviceFunc;
    private LocalizationService _localizer;

    private Stopwatch? _stopwatch;
    private DispatcherTimer? _liveTimer;
    private int _tokenCount = 0;
    private double _firstTokenTimeSeconds = 0;
    private bool _hasReceivedFirstToken = false;
    private bool _isRunning = false;

    private static readonly SolidColorBrush BrushStepInactiveBg = new(Color.FromRgb(0x1E, 0x29, 0x3B));
    private static readonly SolidColorBrush BrushStepInactiveFg = new(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly SolidColorBrush BrushStepActiveBg = new(Color.FromRgb(0x0C, 0x4A, 0x6E));
    private static readonly SolidColorBrush BrushStepActiveFg = new(Color.FromRgb(0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush BrushStepDoneBg = new(Color.FromRgb(0x06, 0x4E, 0x3B));
    private static readonly SolidColorBrush BrushStepDoneFg = new(Color.FromRgb(0x10, 0xB9, 0x81));

    public OperationsView(FoundryClient foundryClient, LocalizationService localizer, Func<string> getDeviceFunc, Func<string> getEffectiveDeviceFunc)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;
        _getDeviceFunc = getDeviceFunc;
        _getEffectiveDeviceFunc = getEffectiveDeviceFunc;

        ApplyLocalization(_localizer);
    }

    private void SetPipelineStep(int activeStep, string description)
    {
        Dispatcher.Invoke(() =>
        {
            if (LblLiveStatus != null) LblLiveStatus.Text = description;

            UpdateStepPill(PillStep1, TxtStep1, activeStep, 1);
            UpdateStepPill(PillStep2, TxtStep2, activeStep, 2);
            UpdateStepPill(PillStep3, TxtStep3, activeStep, 3);
            UpdateStepPill(PillStep4, TxtStep4, activeStep, 4);
        });
    }

    private static void UpdateStepPill(Border? pill, TextBlock? text, int currentActiveStep, int pillStepNumber)
    {
        if (pill == null || text == null) return;

        if (currentActiveStep == pillStepNumber)
        {
            pill.Background = BrushStepActiveBg;
            text.Foreground = BrushStepActiveFg;
        }
        else if (currentActiveStep > pillStepNumber)
        {
            pill.Background = BrushStepDoneBg;
            text.Foreground = BrushStepDoneFg;
        }
        else
        {
            pill.Background = BrushStepInactiveBg;
            text.Foreground = BrushStepInactiveFg;
        }
    }

    public void RefreshDeviceUI()
    {
        string raw = _getDeviceFunc();
        string effective = _getEffectiveDeviceFunc();
        string chipTag = raw.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ? $"Auto: {effective}" : effective;
        LblThroughputHeader.Text = _localizer.CurrentLanguage == "en" ? $"SPEED ({chipTag})" : $"DÉBIT ({chipTag})";
    }

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        bool isEn = _localizer.CurrentLanguage == "en";
        LblOperationsTitle.Text = _localizer.Get("Operations_Title");
        LblOperationsSub.Text = _localizer.Get("Operations_Subtitle");
        LblCardIncident.Text = _localizer.Get("Operations_FieldIncident");
        LblEquipment.Text = _localizer.Get("Operations_Equipment");
        LblAlarmCode.Text = _localizer.Get("Operations_AlarmCode");
        LblSymptoms.Text = _localizer.Get("Operations_ObservedSymptoms");
        BtnRun.Content = "▶ " + (isEn ? "Run Local Analysis" : "Lancer l'analyse locale");
        LblGuidanceTitle.Text = _localizer.Get("Operations_GuidanceTitle");
        LblLatencyHeader.Text = isEn ? "CHRONO / DURATION" : "CHRONO / DURÉE";
        if (LblTokensHeader != null) LblTokensHeader.Text = isEn ? "TOKENS PRODUCED" : "JETONS GÉNÉRÉS";
        if (TxtStep1 != null) TxtStep1.Text = isEn ? "1. Telemetry Ingestion" : "1. Ingestion Télémétrie";
        if (TxtStep2 != null) TxtStep2.Text = isEn ? "2. Silicon Prefill" : "2. Prefill Silicium";
        if (TxtStep3 != null) TxtStep3.Text = isEn ? "3. Diagnostic & Procedure" : "3. Diagnostic & Procédure";
        if (TxtStep4 != null) TxtStep4.Text = isEn ? "4. Safety Validated" : "4. Validation Sécurité";
        if (LblLiveStatus != null && !_isRunning)
        {
            LblLiveStatus.Text = isEn
                ? "Ready • Waiting to launch incident telemetry analysis"
                : "Prêt • En attente de lancement de l'analyse d'incident";
        }

        RefreshDeviceUI();
        LblPrivacyBadge.Text = _localizer.Get("Operations_PrivacyBadge");
        LblSafetyNotice.Text = "⚠️ " + _localizer.Get("Operations_SafetyNoticeTitle") + " : " + _localizer.Get("Operations_SafetyNotice");
        if (TxtDocHelp != null) TxtDocHelp.Text = isEn ? "Guide & Doc" : "Guide & Doc";
        if (TxtBtnViewSourceDoc != null) TxtBtnViewSourceDoc.Text = isEn ? "Source Sheet" : "Fiche Source";
        if (BtnViewSourceDoc != null) BtnViewSourceDoc.ToolTip = isEn ? "View full source incident telemetry file (JSON)" : "Consulter la fiche télémétrique complète de l'incident (JSON)";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = isEn 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = isEn
                ? "Model: Phi-4-mini (Instruct 3.8B • INT4 NPU/GPU)"
                : "Modèle : Phi-4-mini (Instruct 3.8B • INT4 NPU/GPU)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = isEn
                    ? "On-device INT4 quantized Phi-4-mini 3.8B SLM running locally on NPU or GPU via Microsoft Foundry Local."
                    : "Modèle SLM Phi-4-mini 3.8B quantifié INT4 exécuté localement sur NPU ou GPU via Microsoft Foundry Local.";
            }
        }

        if (LblQuickQuestions != null) LblQuickQuestions.Text = isEn ? "Quick questions for demo:" : "Questions rapides pour la démo :";
        if (TxtChipRootCause != null) TxtChipRootCause.Text = isEn ? "⚡ Root Cause & Telemetry" : "⚡ Cause racine & Télémétrie";
        if (TxtChipBelt != null) TxtChipBelt.Text = isEn ? "🔧 Belt Replacement" : "🔧 Procédure Courroie";
        if (TxtChipVibration != null) TxtChipVibration.Text = isEn ? "🛢️ SKF Grease & ISO Vibrations" : "🛢️ Graisse & Vibrations ISO";
        if (TxtChipLockout != null) TxtChipLockout.Text = isEn ? "🛑 NF C 18-510 Lockout" : "🛑 Sécurité NF C 18-510";

        string currentEq = TxtEquipment.Text.Trim();
        if (string.IsNullOrEmpty(currentEq) || currentEq.StartsWith("Unité") || currentEq.StartsWith("HVAC Air"))
        {
            TxtEquipment.Text = _localizer.Get("Operations_DefaultEquipment");
        }

        string currentAlarm = TxtAlarmCode.Text.Trim();
        if (string.IsNullOrEmpty(currentAlarm) || currentAlarm.StartsWith("ALM-") || currentAlarm.StartsWith("ERR-") || currentAlarm.StartsWith("P1045"))
        {
            TxtAlarmCode.Text = _localizer.Get("Operations_DefaultAlarmCode");
        }

        string currentSym = TxtSymptoms.Text.Trim();
        if (string.IsNullOrEmpty(currentSym) ||
            currentSym.StartsWith("Débit") ||
            currentSym.StartsWith("Abnormal") ||
            currentSym.StartsWith("Vibrations") ||
            currentSym.StartsWith("Airflow"))
        {
            TxtSymptoms.Text = _localizer.Get("Operations_DefaultSymptoms");
        }

        if (string.IsNullOrWhiteSpace(TxtOutput.Text) ||
            TxtOutput.Text.StartsWith("En attente") ||
            TxtOutput.Text.StartsWith("Waiting") ||
            TxtOutput.Text.StartsWith("Cliquez sur") ||
            TxtOutput.Text.StartsWith("Click on"))
        {
            TxtOutput.Text = _localizer.Get("Operations_EmptyOutput");
        }
    }

    private void BtnChipRootCause_Click(object sender, RoutedEventArgs e)
    {
        TxtSymptoms.Text = _localizer.CurrentLanguage == "en"
            ? "Airflow deficit -34%; intermittent centrifugal fan trip; high bearing vibration RMS (7.8 mm/s). Identify root causes from sensor telemetry."
            : "Déficit de débit d'air -34%; disjonction intermittente du ventilateur centrifuge; vibrations RMS élevées des paliers (7.8 mm/s). Identifier les causes racines à partir de la télémétrie capteurs.";
    }

    private void BtnChipBelt_Click(object sender, RoutedEventArgs e)
    {
        TxtSymptoms.Text = _localizer.CurrentLanguage == "en"
            ? "Diagnostic and replacement procedure for worn trapezoidal drive belts (optibelt SK SPA 1600): tensioning frequency, alignment check, and run-in protocol."
            : "Procédure de contrôle et remplacement des courroies trapézoïdales (optibelt SK SPA 1600) : fréquence de tension, vérification d'alignement et protocole de rodage.";
    }

    private void BtnChipVibration_Click(object sender, RoutedEventArgs e)
    {
        TxtSymptoms.Text = _localizer.CurrentLanguage == "en"
            ? "Vibration threshold analysis under ISO 10816-3 Class II (7.8 mm/s RMS) and SKF LGMT 2 bearing regreasing specifications (quantity, relubrication intervals)."
            : "Analyse des seuils vibratoires selon ISO 10816-3 Classe II (7.8 mm/s RMS) et spécifications de regraissage des paliers SKF LGMT 2 (quantité, périodicité).";
    }

    private void BtnChipLockout_Click(object sender, RoutedEventArgs e)
    {
        TxtSymptoms.Text = _localizer.CurrentLanguage == "en"
            ? "Emergency electrical isolation and lockout-tagout procedure (NF C 18-510): 5 golden rules, PPE requirements, and verification of zero energy (VAT)."
            : "Procédure de consignation électrique d'urgence et cadenassage (NF C 18-510) : les 5 étapes clés, EPI requis et vérification d'absence de tension (VAT).";
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        BtnRun.IsEnabled = false;
        PrgBusy.Visibility = Visibility.Visible;
        _isRunning = true;
        bool isEn = _localizer.CurrentLanguage == "en";

        string equipment = TxtEquipment.Text.Trim();
        string alarmCode = TxtAlarmCode.Text.Trim();
        string symptoms = TxtSymptoms.Text.Trim();
        string targetDevice = _getDeviceFunc();

        _stopwatch = Stopwatch.StartNew();
        _tokenCount = 0;
        _firstTokenTimeSeconds = 0;
        _hasReceivedFirstToken = false;

        LblLatency.Text = "0.0 s";
        LblThroughput.Text = isEn ? "⚡ NPU Prefill..." : "⚡ Pré-calcul NPU...";
        LblTokens.Text = "0 tok";

        SetPipelineStep(1, isEn
            ? "📁 Ingesting telemetry: Incident sheet P1045, airflow deficit -34% & vibration RMS 7.8 mm/s loaded"
            : "📁 Ingestion télémétrie : Fiche incident P1045, déficit -34% débit & vibration RMS 7.8 mm/s chargées");

        TxtOutput.Text = isEn
            ? "⚡ Initializing Phi-4-mini on local device silicon...\n📊 Ingesting sensor telemetry and HVAC incident report (Alarm P1045)...\n"
            : "⚡ Initialisation de Phi-4-mini sur le Silicium local...\n📊 Analyse des données capteurs et de la fiche incident CVC (Alarme P1045)...\n";

        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(75) };
        _liveTimer.Tick += (s, args) =>
        {
            if (_stopwatch == null) return;
            double elapsedSec = _stopwatch.Elapsed.TotalSeconds;
            LblLatency.Text = $"{elapsedSec:F1} s";

            if (_hasReceivedFirstToken)
            {
                double streamSec = Math.Max(0.05, elapsedSec - _firstTokenTimeSeconds);
                double liveTps = _tokenCount / streamSec;
                LblThroughput.Text = $"{liveTps:F1} tok/s";
                LblTokens.Text = $"{_tokenCount} tok";
            }
        };
        _liveTimer.Start();

        try
        {
            var selectedModel = await _foundryClient.GetBestModelAsync("phi-4-mini", targetDevice);
            string effective = selectedModel.TargetDevice ?? _getEffectiveDeviceFunc();
            string chipTag = targetDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ? $"Auto: {effective}" : effective;

            SetPipelineStep(2, isEn
                ? $"⚡ Silicon prefill active on {effective} (compiling incident telemetry context)..."
                : $"⚡ Silicium local actif ({effective}) : Pré-calcul du contexte télémétrique...");

            string systemPrompt = OperationsPromptBuilder.BuildSystemPrompt()
                                + "\n\nIMPORTANT LANGUAGE INSTRUCTION:\n"
                                + _localizer.Get("Language_ResponseInstruction")
                                + "\n\nDirectives: Ultra-concise executive bullet points with exact metrics (e.g. 7.8 mm/s, -34% airflow, ISO 10816-3, optibelt SK SPA 1600). Strictly no intro or conversational filler.";

            // Charger la fiche télémétrique complète officielle
            string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Fiche_Incident_CVC_P1045.json");
            string candidate = direct;
            if (!File.Exists(candidate))
            {
                string? dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string test = Path.Combine(dir, "Data", "Documents", "Fiche_Incident_CVC_P1045.json");
                    if (File.Exists(test)) { candidate = test; break; }
                    dir = Path.GetDirectoryName(dir);
                }
            }
            string? fullTelemetry = File.Exists(candidate) ? File.ReadAllText(candidate) : null;

            string userPrompt = OperationsPromptBuilder.BuildUserPrompt(equipment, alarmCode, symptoms, fullTelemetry);

            var result = await _foundryClient.AskStreamingAsync(
                selectedModel,
                systemPrompt,
                userPrompt,
                chunk =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_hasReceivedFirstToken)
                        {
                            _hasReceivedFirstToken = true;
                            _firstTokenTimeSeconds = _stopwatch?.Elapsed.TotalSeconds ?? 0;
                            TxtOutput.Text = string.Empty;
                            SetPipelineStep(3, isEn
                                ? $"✍️ Live maintenance guidance streaming (First token at {_firstTokenTimeSeconds:F2}s)..."
                                : $"✍️ Rédaction des consignes en temps réel (1er jeton à {_firstTokenTimeSeconds:F2}s)...");
                        }

                        _tokenCount += Math.Max(1, chunk.Length / 4);
                        TxtOutput.AppendText(chunk);
                        ScrollerOutput.ScrollToEnd();

                        string fullText = TxtOutput.Text;
                        double elapsed = Math.Max(0.1, (_stopwatch?.Elapsed.TotalSeconds ?? 1) - _firstTokenTimeSeconds);
                        double currentTps = _tokenCount / elapsed;

                        if (fullText.Contains("Consignation") || fullText.Contains("18-510") || fullText.Contains("Lockout") || fullText.Contains("Cadenassage"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [4/4 Electrical Safety NF C 18-510] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [4/4 Sécurité Électrique NF C 18-510] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Graisse") || fullText.Contains("10816") || fullText.Contains("Palier") || fullText.Contains("Lubrification") || fullText.Contains("Vibration"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [3/4 ISO 10816 Vibration & SKF Grease] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [3/4 Vibrations ISO 10816 & Graisse SKF] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Courroie") || fullText.Contains("Tension") || fullText.Contains("Belt") || fullText.Contains("Poulie"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [2/4 Drive Belt Tension & Procedure] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [2/4 Procédure Courroie & Tension] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Cause") || fullText.Contains("Racine") || fullText.Contains("Diagnostic") || fullText.Contains("Débit") || fullText.Contains("Root"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [1/4 Root Cause & Airflow Deficit] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [1/4 Cause Racine & Déficit Débit] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                    });
                },
                temperature: 0.0,
                maxTokens: 280);

            _liveTimer?.Stop();
            _liveTimer = null;
            _stopwatch?.Stop();

            Dispatcher.Invoke(() =>
            {
                LblLatency.Text = $"{result.LatencyMilliseconds} ms ({result.LatencyMilliseconds / 1000.0:F1}s)";
                LblThroughput.Text = $"{result.TokensPerSecond:F1} tok/s";
                LblThroughputHeader.Text = isEn ? $"SPEED ({chipTag})" : $"DÉBIT ({chipTag})";
                LblTokens.Text = $"{result.CompletionTokens} tok";

                SetPipelineStep(4, isEn
                    ? $"✅ Maintenance guidance completed ({result.CompletionTokens} tokens in {result.LatencyMilliseconds / 1000.0:F1}s) • 100% on-device 🛡️"
                    : $"✅ Diagnostic maintenance terminé ({result.CompletionTokens} jetons en {result.LatencyMilliseconds / 1000.0:F1}s) • 100% hors-ligne 🛡️");
            });

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Opérations Industrielles",
                requestedSilicon: targetDevice,
                effectiveSilicon: result.TargetDevice,
                siliconDeviceName: result.ModelId,
                latencyMs: (int)result.LatencyMilliseconds,
                tokensPerSecond: result.TokensPerSecond
            );
        }
        catch (Exception ex)
        {
            _liveTimer?.Stop();
            _liveTimer = null;
            _stopwatch?.Stop();

            Dispatcher.Invoke(() =>
            {
                TxtOutput.Text = $"Erreur / Error: {ex.Message}";
                LblLiveStatus.Text = $"⚠️ Erreur d'analyse : {ex.Message}";
            });
        }
        finally
        {
            _liveTimer?.Stop();
            _liveTimer = null;
            _isRunning = false;

            Dispatcher.Invoke(() =>
            {
                BtnRun.IsEnabled = true;
                PrgBusy.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_operations", _localizer?.CurrentLanguage);
    }

    private void BtnViewSourceDoc_Click(object sender, RoutedEventArgs e)
    {
        string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Fiche_Incident_CVC_P1045.json");
        string candidate = direct;
        if (!File.Exists(candidate))
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string test = Path.Combine(dir, "Data", "Documents", "Fiche_Incident_CVC_P1045.json");
                if (File.Exists(test)) { candidate = test; break; }
                dir = Path.GetDirectoryName(dir);
            }
        }

        string content = File.Exists(candidate) ? File.ReadAllText(candidate) : "{}";
        bool isEn = _localizer?.CurrentLanguage == "en";
        SourceDocumentViewerWindow.ShowDocument(
            owner: Window.GetWindow(this),
            title: isEn ? "Telemetry Incident Sheet • Alarm HVAC P1045" : "Fiche d'Incident Télémétrique • Alarme CVC P1045",
            content: content,
            format: "JSON",
            filePath: File.Exists(candidate) ? candidate : null,
            localizer: _localizer,
            icon: "📋"
        );
    }
}
