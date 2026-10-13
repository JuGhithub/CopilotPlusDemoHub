using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class FinancialView : UserControl
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

    public FinancialView(FoundryClient foundryClient, LocalizationService localizer, Func<string> getDeviceFunc, Func<string> getEffectiveDeviceFunc)
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
        LblFinTitle.Text = _localizer.Get("Financial_Title");
        LblFinSub.Text = _localizer.Get("Financial_Subtitle");
        LblCardCredit.Text = _localizer.CurrentLanguage == "en" ? "CORPORATE CREDIT APPLICATION" : "DOSSIER DE CRÉDIT ENTREPRISE";
        LblCompany.Text = _localizer.Get("Financial_CompanyTitle");
        LblCompanySub1.Text = _localizer.Get("Financial_CompanySub1");
        LblCompanySub2.Text = _localizer.Get("Financial_CompanySub2");
        LblFinDataLabel.Text = _localizer.CurrentLanguage == "en" ? "Financial metrics & signals" : "Données financières & signaux";
        LblInstructionLabel.Text = _localizer.CurrentLanguage == "en" ? "Committee instruction" : "Instruction comité";
        BtnRun.Content = "▶ " + (_localizer.CurrentLanguage == "en" ? "Evaluate Risk (Local AI)" : "Évaluer le risque (IA Locale)");
        LblSynthesisTitle.Text = _localizer.CurrentLanguage == "en" ? "CREDIT & RISK COMMITTEE SUMMARY NOTE" : "NOTE DE SYNTHÈSE COMITÉ DES CRÉDITS & RISQUE";
        bool isEn = _localizer.CurrentLanguage == "en";
        LblLatencyHeader.Text = isEn ? "CHRONO / DURATION" : "CHRONO / DURÉE";
        if (LblTokensHeader != null) LblTokensHeader.Text = isEn ? "TOKENS PRODUCED" : "JETONS GÉNÉRÉS";
        if (TxtStep1 != null) TxtStep1.Text = isEn ? "1. Dossier Ingestion" : "1. Ingestion Dossier";
        if (TxtStep2 != null) TxtStep2.Text = isEn ? "2. Silicon Prefill" : "2. Prefill Silicium";
        if (TxtStep3 != null) TxtStep3.Text = isEn ? "3. Live Reasoning" : "3. Raisonnement & Rédaction";
        if (TxtStep4 != null) TxtStep4.Text = isEn ? "4. Enclave Validated" : "4. Confinement Validé";
        if (LblLiveStatus != null && !_isRunning)
        {
            LblLiveStatus.Text = isEn
                ? "Ready • Waiting to launch financial risk evaluation"
                : "Prêt • En attente de lancement de l'évaluation financière";
        }

        RefreshDeviceUI();
        LblBankingHeader.Text = isEn ? "BANKING SECRECY" : "SECRET BANCAIRE";
        LblBankingBadge.Text = _localizer.Get("Financial_ZeroTrustBadge");
        LblNotice.Text = "🔒 " + (isEn
            ? "Business & Banking Secrecy: Balance sheets and financial statements remain strictly confined to local device silicon."
            : "Secret d'affaires & Bancaire : Les bilans, ratios et états financiers restent strictement confinés au silicium local de ce poste.");
        if (TxtBtnViewSourceDoc != null) TxtBtnViewSourceDoc.Text = isEn ? "Source Dossier" : "Dossier Source";
        if (BtnViewSourceDoc != null) BtnViewSourceDoc.ToolTip = _localizer.Get("Financial_BtnViewSourceDoc");
        if (TxtDocHelp != null) TxtDocHelp.Text = isEn ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = isEn 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = isEn
                ? "Model: Phi-4-mini (Risk & CSRD Audit 3.8B)"
                : "Modèle : Phi-4-mini (Audit Risque & CSRD 3.8B)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = isEn
                    ? "Confidential financial analysis and ESG/CSRD scoring executed on local silicon with zero data leakage."
                    : "Analyse financière confidentielle et scoring ESG/CSRD exécutés sur silicium local sans fuite de données.";
            }
        }

        if (LblQuickQuestions != null) LblQuickQuestions.Text = isEn ? "Quick questions for demo:" : "Questions rapides pour la démo :";
        if (TxtChipSolvency != null) TxtChipSolvency.Text = isEn ? "📈 Solvency & Ratios" : "📈 Solvabilité & Ratios";
        if (TxtChipWcr != null) TxtChipWcr.Text = isEn ? "⏳ Export WCR Strain" : "⏳ Tension BFR Export";
        if (TxtChipCommittee != null) TxtChipCommittee.Text = isEn ? "⚖️ Committee & Collateral" : "⚖️ Avis Comité & Garanties";
        if (TxtChipBdF != null) TxtChipBdF.Text = isEn ? "🏛️ BdF Rating (3+)" : "🏛️ Notation BdF 3+";

        string currentData = TxtFinancialData.Text.Trim();
        if (string.IsNullOrEmpty(currentData) || currentData.StartsWith("Chiffre") || currentData.StartsWith("Revenue"))
        {
            TxtFinancialData.Text = _localizer.Get("Financial_DefaultData");
        }

        string currentInst = TxtInstruction.Text.Trim();
        if (string.IsNullOrEmpty(currentInst) || currentInst.StartsWith("Évaluer") || currentInst.StartsWith("Evaluate"))
        {
            TxtInstruction.Text = _localizer.Get("Financial_DefaultInstruction");
        }

        if (string.IsNullOrWhiteSpace(TxtOutput.Text) ||
            TxtOutput.Text.StartsWith("Cliquez sur") ||
            TxtOutput.Text.StartsWith("Click on"))
        {
            TxtOutput.Text = _localizer.Get("Financial_EmptyOutput");
        }
    }

    private void BtnChipSolvency_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Assess corporate solvency: equity ratio, net debt leverage (2.76x), debt service coverage, and structural balance."
            : "Évaluer la solvabilité globale : autonomie financière, levier d'endettement net (2,76x), capacité de remboursement et équilibre du haut de bilan.";
    }

    private void BtnChipWcr_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Analyze working capital requirement (WCR) deterioration: export customer payment delay extension (+45 days), liquidity strain, and cash flow impact."
            : "Analyser la dégradation du BFR : allongement des délais de règlement clients à l'export (+45 jours), tension de trésorerie et risque d'impasse à court terme.";
    }

    private void BtnChipCommittee_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Formulate motivated credit committee opinion on the €2.5M overdraft request, required collateral/guarantees, and covenant terms."
            : "Formuler la recommandation motivée pour le comité des engagements sur la facilité de caisse de 2,5 M€, les garanties exigibles et les covenants.";
    }

    private void BtnChipBdF_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Evaluate Banque de France rating (3+): probability of default, risk premium impact, and eligibility for refinancing."
            : "Analyser la cotation Banque de France (3+) : probabilité de défaillance, impact sur la prime de risque et éligibilité au refinancement.";
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        BtnRun.IsEnabled = false;
        PrgBusy.Visibility = Visibility.Visible;
        _isRunning = true;
        bool isEn = _localizer.CurrentLanguage == "en";

        string financialData = TxtFinancialData.Text.Trim();
        string instruction = TxtInstruction.Text.Trim();
        string targetDevice = _getDeviceFunc();
        string initialEffective = _getEffectiveDeviceFunc();
        string displayChip = targetDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ? initialEffective : targetDevice;

        _stopwatch = Stopwatch.StartNew();
        _tokenCount = 0;
        _firstTokenTimeSeconds = 0;
        _hasReceivedFirstToken = false;

        LblLatency.Text = "0.0 s";
        LblThroughput.Text = isEn ? $"⚡ {displayChip} Prefill..." : $"⚡ Pré-calcul {displayChip}...";
        LblTokens.Text = "0 tok";

        SetPipelineStep(1, isEn
            ? "📁 Ingesting corporate dossier: 70 lines of certified balance sheets & BdF 3+ rating loaded"
            : "📁 Ingestion du dossier : 70 lignes de bilan comptable, liasse fiscale & notation BdF 3+ chargées");

        TxtOutput.Text = isEn
            ? $"⚡ Initializing Phi-4-mini on local {displayChip}...\n📊 Parsing balance sheets and corporate tax filings (SIREN 849 201 932)...\n"
            : $"⚡ Initialisation de Phi-4-mini sur le {displayChip} local...\n📊 Analyse des bilans comptables et de la liasse fiscale (SIREN 849 201 932)...\n";

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
                ? $"⚡ Silicon prefill active on {effective} (compiling ~450 tokens context)..."
                : $"⚡ Silicium local actif ({effective}) : Pré-calcul du contexte (450 tokens)...");

            string systemPrompt = isEn
                ? "You are a senior financial analyst and credit committee secretary. Provide an executive flash credit note in 4 parts:\n" +
                  "1. Solvency diagnosis (Autonomy, net leverage 2.76x)\n" +
                  "2. Ratios & WCR strain (+45d export delay, cash 420k€)\n" +
                  "3. Risk matrix & collateral (Titanium stock pledge, BdF 3+ rating)\n" +
                  "4. Committee decision (Approve €2.5M overdraft under covenant terms)\n\n" +
                  "Directives: Ultra-concise bullet points, exact figures, direct executive tone. Strictly no introduction or conversational filler.\n\n" +
                  _localizer.Get("Language_ResponseInstruction")
                : "Vous êtes un analyste financier senior et secrétaire de comité des engagements bancaires. Rédigez une note de synthèse exécutive flash en 4 parties concises :\n" +
                  "1. Diagnostic de solvabilité (Autonomie, levier net 2,76x)\n" +
                  "2. Ratios & Tension BFR (+45 jours export, trésorerie 420 k€)\n" +
                  "3. Matrice de risques & garanties (Stock titane gagé, cotation BdF 3+)\n" +
                  "4. Décision motivée du comité (Accord facilité 2,5 M€ sous conditions de covenants)\n\n" +
                  "Directives : Puces ultra-concises, chiffres exacts, style exécutif direct. Aucun verbiage d'introduction.\n\n" +
                  _localizer.Get("Language_ResponseInstruction");

            // Charger l'intégralité du mémorandum financier et des bilans certifiés
            string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Dossier_Credit_Entreprise_849201932.txt");
            string candidate = direct;
            if (!File.Exists(candidate))
            {
                string? dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string test = Path.Combine(dir, "Data", "Documents", "Dossier_Credit_Entreprise_849201932.txt");
                    if (File.Exists(test)) { candidate = test; break; }
                    dir = Path.GetDirectoryName(dir);
                }
            }
            string fullCreditDossier = File.Exists(candidate) ? File.ReadAllText(candidate) : financialData;

            string userPrompt = $"DOSSIER DE CRÉDIT & BILANS :\n==================================================\n{fullCreditDossier}\n==================================================\n\nINSTRUCTION DU COMITÉ :\n{instruction}";

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
                                ? $"✍️ Live analysis streaming (First token at {_firstTokenTimeSeconds:F2}s)..."
                                : $"✍️ Rédaction de la synthèse en temps réel (1er jeton à {_firstTokenTimeSeconds:F2}s)...");
                        }

                        _tokenCount += Math.Max(1, chunk.Length / 4);
                        TxtOutput.AppendText(chunk);
                        ScrollerOutput.ScrollToEnd();

                        string fullText = TxtOutput.Text;
                        double elapsed = Math.Max(0.1, (_stopwatch?.Elapsed.TotalSeconds ?? 1) - _firstTokenTimeSeconds);
                        double currentTps = _tokenCount / elapsed;

                        if (fullText.Contains("Recommandation") || fullText.Contains("Avis motivé") || fullText.Contains("Recommendation") || fullText.Contains("Décision"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [4/4 Committee Decision] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [4/4 Décision Comité] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Risque") || fullText.Contains("Matrice") || fullText.Contains("Garantie") || fullText.Contains("Risk"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [3/4 Risk Matrix & Collateral] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [3/4 Matrice Risques & Garanties] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Ratio") || fullText.Contains("BFR") || fullText.Contains("Liquidité") || fullText.Contains("Liquidity"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [2/4 Financial Ratios & Export WCR] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [2/4 Ratios & Tension BFR] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Diagnostic") || fullText.Contains("Solvabilité") || fullText.Contains("Solvency"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [1/4 Solvency Diagnosis] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [1/4 Diagnostic Solvabilité] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
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
                    ? $"✅ Financial note completed ({result.CompletionTokens} tokens in {result.LatencyMilliseconds / 1000.0:F1}s) • Banking secrecy strictly preserved 🔒"
                    : $"✅ Note financière terminée avec succès ({result.CompletionTokens} jetons en {result.LatencyMilliseconds / 1000.0:F1}s) • Secret bancaire préservé 🔒");
            });

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Finance & Analyse de Risque",
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
                TxtOutput.Text = $"Erreur lors de l'analyse financière locale : {ex.Message}";
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
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_financial", _localizer?.CurrentLanguage);
    }

    private void BtnViewSourceDoc_Click(object sender, RoutedEventArgs e)
    {
        string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Dossier_Credit_Entreprise_849201932.txt");
        string candidate = direct;
        if (!File.Exists(candidate))
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string test = Path.Combine(dir, "Data", "Documents", "Dossier_Credit_Entreprise_849201932.txt");
                if (File.Exists(test)) { candidate = test; break; }
                dir = Path.GetDirectoryName(dir);
            }
        }

        string content = File.Exists(candidate) ? File.ReadAllText(candidate) : "";
        bool isEn = _localizer?.CurrentLanguage == "en";
        SourceDocumentViewerWindow.ShowDocument(
            owner: Window.GetWindow(this),
            title: isEn ? "Credit Application & Balance Sheets • SIREN 849 201 932" : "Dossier de Crédit & Liasse Fiscale • SIREN 849 201 932",
            content: content,
            format: "TXT",
            filePath: File.Exists(candidate) ? candidate : null,
            localizer: _localizer,
            icon: "📊"
        );
    }
}
