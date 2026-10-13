using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.Demos.Engineering;

namespace CopilotPlusDemoHub.UI.Views;

public partial class EngineeringView : UserControl
{
    private readonly FoundryClient _foundryClient;
    private readonly Func<string> _getDeviceFunc;
    private readonly Func<string> _getEffectiveDeviceFunc;
    private LocalizationService _localizer;
    private string _manualContent = string.Empty;
    private string? _currentDocPath = null;

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

    public EngineeringView(FoundryClient foundryClient, LocalizationService localizer, Func<string> getDeviceFunc, Func<string> getEffectiveDeviceFunc)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;
        _getDeviceFunc = getDeviceFunc;
        _getEffectiveDeviceFunc = getEffectiveDeviceFunc;
        LoadManualContent();
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
        LblThroughputHeader.Text = _localizer.CurrentLanguage == "en" ? $"SPEED ({chipTag})" : $"VITESSE ({chipTag})";
    }

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        bool isEn = _localizer.CurrentLanguage == "en";
        LblEngTitle.Text = _localizer.Get("Engineering_Title");
        LblEngSub.Text = _localizer.Get("Engineering_Subtitle");
        LblKnowledgeBase.Text = _localizer.Get("Engineering_KnowledgeTitle");
        LblDocTitle.Text = _localizer.Get("Engineering_DocTitle");
        LblDocSub.Text = _localizer.Get("Engineering_DocSub");
        LblQuestionLabel.Text = _localizer.Get("Engineering_QuestionLabel");
        LblSuggestionsLabel.Text = isEn ? "Suggested questions:" : "Questions suggérées :";
        BtnSuggestion1.Content = _localizer.Get("Engineering_Suggestion1");
        BtnSuggestion2.Content = _localizer.Get("Engineering_Suggestion2");
        BtnSuggestion3.Content = _localizer.Get("Engineering_Suggestion3");
        if (BtnSuggestion4 != null) BtnSuggestion4.Content = isEn ? "Cleanroom & HEPA H14" : "Filtres HEPA & Salle Propre";
        BtnRun.Content = "▶ " + (isEn ? "Query Documentation" : "Interroger la documentation");
        BtnImportDoc.Content = isEn ? "📁 Import Document" : "📁 Importer un document";
        BtnResetDoc.Content = isEn ? "🔄 Default Manual" : "🔄 Manuel CVC";
        LblGuidanceTitle.Text = _localizer.Get("Engineering_GuidanceTitle");
        LblLatencyHeader.Text = isEn ? "CHRONO / DURATION" : "CHRONO / DURÉE";
        if (LblTokensHeader != null) LblTokensHeader.Text = isEn ? "TOKENS PRODUCED" : "JETONS GÉNÉRÉS";
        if (TxtStep1 != null) TxtStep1.Text = isEn ? "1. Internal Manual RAG" : "1. RAG Manuel Interne";
        if (TxtStep2 != null) TxtStep2.Text = isEn ? "2. Silicon Prefill" : "2. Prefill Silicium";
        if (TxtStep3 != null) TxtStep3.Text = isEn ? "3. Reasoning & Citations" : "3. Raisonnement & Citations";
        if (TxtStep4 != null) TxtStep4.Text = isEn ? "4. Enclave Preserved" : "4. Données Préservées";
        if (LblLiveStatus != null && !_isRunning)
        {
            LblLiveStatus.Text = isEn
                ? "Ready • Waiting for engineering query"
                : "Prêt • En attente de question d'ingénierie";
        }

        RefreshDeviceUI();
        LblSourceDoc.Text = _localizer.Get("Engineering_SourceDoc");
        LblNotice.Text = "ℹ️ " + _localizer.Get("Engineering_DataNotice");
        if (TxtDocHelp != null) TxtDocHelp.Text = _localizer.CurrentLanguage == "en" ? "Guide & Doc" : "Guide & Doc";
        if (TxtBtnViewSourceDoc != null) TxtBtnViewSourceDoc.Text = _localizer.CurrentLanguage == "en" ? "View Manual" : "Voir le Manuel";
        if (BtnViewSourceDoc != null) BtnViewSourceDoc.ToolTip = _localizer.CurrentLanguage == "en" ? "View full source technical manual (TXT)" : "Consulter l'intégralité du manuel ou document technique indexé";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = _localizer.CurrentLanguage == "en" 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = _localizer.CurrentLanguage == "en"
                ? "Models: BGE-Small (Embeddings) + Phi-4-mini (RAG)"
                : "Modèles : BGE-Small (Embeddings) + Phi-4-mini (RAG)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = _localizer.CurrentLanguage == "en"
                    ? "Local RAG pipeline: BGE-Small semantic vectorization (OpenVINO) and Phi-4-mini engineering synthesis."
                    : "Pipeline RAG local : Vectorisation sémantique BGE-Small (OpenVINO) et synthèse d'ingénierie Phi-4-mini.";
            }
        }

        // Update default question if user hasn't typed a custom one
        string currentQ = TxtQuestion.Text.Trim();
        if (string.IsNullOrEmpty(currentQ) ||
            currentQ.StartsWith("Quelles sont") ||
            currentQ.StartsWith("What are"))
        {
            TxtQuestion.Text = _localizer.Get("Engineering_DefaultQuestion");
        }

        if (string.IsNullOrWhiteSpace(TxtOutput.Text) ||
            TxtOutput.Text.StartsWith("Posez une") ||
            TxtOutput.Text.StartsWith("Submit a") ||
            TxtOutput.Text.StartsWith("Cliquez sur") ||
            TxtOutput.Text.StartsWith("Click on"))
        {
            TxtOutput.Text = _localizer.Get("Engineering_EmptyOutput");
        }
    }

    private void Suggestion1_Click(object sender, RoutedEventArgs e)
    {
        TxtQuestion.Text = _localizer.Get("Engineering_Question1");
    }

    private void Suggestion2_Click(object sender, RoutedEventArgs e)
    {
        TxtQuestion.Text = _localizer.Get("Engineering_Question2");
    }

    private void Suggestion3_Click(object sender, RoutedEventArgs e)
    {
        TxtQuestion.Text = _localizer.Get("Engineering_Question3");
    }

    private void Suggestion4_Click(object sender, RoutedEventArgs e)
    {
        TxtQuestion.Text = _localizer.CurrentLanguage == "en"
            ? "What are the cleanroom ISO 7 air filtration specifications and replacement intervals for HEPA H14 terminal filters?"
            : "Quelles sont les spécifications de filtration d'air en salle propre ISO 7 et la périodicité de remplacement des filtres HEPA H14 ?";
    }

    private void LoadManualContent()
    {
        try
        {
            string candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Manuel_Maintenance_CVC_Industriel.txt");
            if (!File.Exists(candidate))
            {
                string? dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string test = Path.Combine(dir, "Data", "Documents", "Manuel_Maintenance_CVC_Industriel.txt");
                    if (File.Exists(test)) { candidate = test; break; }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            if (!File.Exists(candidate))
            {
                string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Demos", "Engineering", "SampleManual.txt");
                if (File.Exists(sample)) candidate = sample;
            }

            if (File.Exists(candidate))
            {
                _manualContent = File.ReadAllText(candidate);
                _currentDocPath = candidate;
            }
            else
            {
                _manualContent = "# Manuel Technique CVC\nSpécifications de maintenance industrielle.";
                _currentDocPath = null;
            }
        }
        catch
        {
            _manualContent = "# Manuel Technique CVC\nErreur lors de la lecture locale.";
            _currentDocPath = null;
        }
    }

    private void BtnViewSourceDoc_Click(object sender, RoutedEventArgs e)
    {
        SourceDocumentViewerWindow.ShowDocument(
            owner: Window.GetWindow(this),
            title: LblDocTitle.Text,
            content: _manualContent,
            format: "TXT",
            filePath: _currentDocPath,
            localizer: _localizer,
            icon: "📖"
        );
    }

    private void BtnImportDoc_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = _localizer.CurrentLanguage == "en" ? "Select technical document" : "Sélectionner un document technique",
            Filter = "Documents texte (*.txt;*.md;*.log;*.json)|*.txt;*.md;*.log;*.json|Tous les fichiers (*.*)|*.*",
            Multiselect = false
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                string filePath = openFileDialog.FileName;
                string content = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(content))
                {
                    MessageBox.Show(
                        _localizer.CurrentLanguage == "en" ? "The selected file is empty." : "Le fichier sélectionné est vide.",
                        "Copilot+ Demo Hub",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                _manualContent = content;
                _currentDocPath = filePath;
                string fileName = Path.GetFileName(filePath);
                int lineCount = content.Split('\n').Length;
                double sizeKb = new FileInfo(filePath).Length / 1024.0;

                LblDocTitle.Text = $"📄 {fileName}";
                LblDocSub.Text = _localizer.CurrentLanguage == "en"
                    ? $"Custom document • {lineCount} lines ({sizeKb:F1} KB) • Locally indexed"
                    : $"Document importé • {lineCount} lignes ({sizeKb:F1} Ko) • Indexé localement";
                LblSourceDoc.Text = fileName;
                TxtOutput.Text = _localizer.CurrentLanguage == "en"
                    ? $"✅ Document '{fileName}' successfully loaded ({lineCount} lines).\nYou can now ask any question about this document above."
                    : $"✅ Document '{fileName}' chargé avec succès ({lineCount} lignes).\nPosez votre question ci-dessus pour interroger ce document en local sur le NPU.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la lecture du document : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnResetDoc_Click(object sender, RoutedEventArgs e)
    {
        LoadManualContent();
        LblDocTitle.Text = _localizer.Get("Engineering_DocTitle");
        LblDocSub.Text = _localizer.Get("Engineering_DocSub");
        LblSourceDoc.Text = _localizer.Get("Engineering_SourceDoc");
        TxtOutput.Text = _localizer.Get("Engineering_EmptyOutput");
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        BtnRun.IsEnabled = false;
        PrgBusy.Visibility = Visibility.Visible;
        _isRunning = true;
        bool isEn = _localizer.CurrentLanguage == "en";

        string question = TxtQuestion.Text.Trim();
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
            ? "📁 Querying RAG technical manual: 150+ lines indexed, semantic search active"
            : "📁 Recherche RAG dans le manuel technique interne : 150+ lignes indexées chargées");

        TxtOutput.Text = isEn
            ? $"⚡ Initializing Phi-4-mini on local {displayChip}...\n📖 Searching indexed HVAC technical manual and specifications...\n"
            : $"⚡ Initialisation de Phi-4-mini sur le {displayChip} local...\n📖 Recherche dans le manuel technique CVC indexé et extraction des tolérances...\n";

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
                ? $"⚡ Silicon prefill active on {effective} (compiling technical context)..."
                : $"⚡ Silicium local actif ({effective}) : Pré-calcul du contexte documentaire...");

            string languageInstruction = _localizer.Get("Language_ResponseInstruction")
                                        + "\n\nDirectives: Ultra-concise executive bullet points with exact specifications, threshold metrics and precise document citations. Strictly no intro or conversational filler.";

            string systemPrompt = EngineeringPromptBuilder.BuildSystemPrompt(languageInstruction);

            string userPrompt = EngineeringPromptBuilder.BuildUserPrompt(
                _manualContent,
                question,
                languageInstruction);

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
                                ? $"✍️ Live engineering synthesis streaming (First token at {_firstTokenTimeSeconds:F2}s)..."
                                : $"✍️ Rédaction technique en temps réel (1er jeton à {_firstTokenTimeSeconds:F2}s)...");
                        }

                        _tokenCount += Math.Max(1, chunk.Length / 4);
                        TxtOutput.AppendText(chunk);
                        ScrollerOutput.ScrollToEnd();

                        string fullText = TxtOutput.Text;
                        double elapsed = Math.Max(0.1, (_stopwatch?.Elapsed.TotalSeconds ?? 1) - _firstTokenTimeSeconds);
                        double currentTps = _tokenCount / elapsed;

                        if (fullText.Contains("Section") || fullText.Contains("Chapitre") || fullText.Contains("Manuel") || fullText.Contains("Page") || fullText.Contains("Ref"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [3/3 Document Citations & References] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [3/3 Citations Manuel HVAC v2.4] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Norme") || fullText.Contains("ISO") || fullText.Contains("Tolérance") || fullText.Contains("Seuil") || fullText.Contains("Valeur"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [2/3 Technical Tolerances & Standards] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [2/3 Spécifications & Tolérances] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Réponse") || fullText.Contains("Procédure") || fullText.Contains("Consigne") || fullText.Contains("Recommandation") || fullText.Contains("Protocole"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [1/3 Engineering Analysis & Procedure] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [1/3 Analyse & Recommandations RAG] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
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
                LblThroughputHeader.Text = isEn ? $"SPEED ({chipTag})" : $"VITESSE ({chipTag})";
                LblTokens.Text = $"{result.CompletionTokens} tok";

                SetPipelineStep(4, isEn
                    ? $"✅ Engineering query completed ({result.CompletionTokens} tokens in {result.LatencyMilliseconds / 1000.0:F1}s) • Confined on local device 🛡️"
                    : $"✅ Réponse d'ingénierie terminée ({result.CompletionTokens} jetons en {result.LatencyMilliseconds / 1000.0:F1}s) • Données confinées sur ce PC 🛡️");
            });

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Ingénierie & RAG Technique",
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
                TxtOutput.Text = $"Erreur lors de l'analyse locale : {ex.Message}";
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
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_engineering", _localizer?.CurrentLanguage);
    }
}
