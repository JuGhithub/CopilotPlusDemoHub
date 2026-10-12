using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using CopilotPlusDemoHub.Core;
using CopilotPlusDemoHub.Demos.Insurance;

namespace CopilotPlusDemoHub.UI.Views;

public partial class InsuranceView : UserControl
{
    private readonly FoundryClient _foundryClient;
    private readonly Func<string> _getDeviceFunc;
    private readonly Func<string> _getEffectiveDeviceFunc;
    private LocalizationService _localizer;
    private DamagePhotoInfo? _currentPhoto;
    private List<DamagePhotoInfo> _samplePhotos = new();

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

    public InsuranceView(FoundryClient foundryClient, LocalizationService localizer, Func<string> getDeviceFunc, Func<string> getEffectiveDeviceFunc)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;
        _getDeviceFunc = getDeviceFunc;
        _getEffectiveDeviceFunc = getEffectiveDeviceFunc;

        _samplePhotos = DamagePhotoAnalyzer.GetDefaultSamplePhotos();
        if (_samplePhotos.Count > 0)
        {
            SelectPhoto(_samplePhotos[0]);
        }

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
        bool isEn = _localizer?.CurrentLanguage == "en";
        LblThroughputHeader.Text = isEn ? $"SPEED ({chipTag})" : $"DÉBIT ({chipTag})";
    }

    private void SelectPhoto(DamagePhotoInfo? photo)
    {
        _currentPhoto = photo;
        var cyanBrush = (Brush)FindResource("CopilotCyanBrush");
        var darkBrush = new SolidColorBrush(Color.FromRgb(0x16, 0x20, 0x32));
        var activeBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        var dimBorder = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55));

        if (photo == null)
        {
            ImgPhotoPreview.Source = null;
            TxtPhotoTitle.Text = _localizer?.CurrentLanguage == "en" ? "No photo selected" : "Aucune photo sélectionnée";
            TxtPhotoSub.Text = _localizer?.CurrentLanguage == "en" ? "Analysis will rely on written claim only" : "L'analyse reposera uniquement sur le constat écrit";

            BtnSamplePhoto1.Background = darkBrush;
            BtnSamplePhoto1.BorderBrush = dimBorder;
            BtnSamplePhoto2.Background = darkBrush;
            BtnSamplePhoto2.BorderBrush = dimBorder;
            BtnRemovePhoto.Background = activeBrush;
            BtnRemovePhoto.BorderBrush = cyanBrush;
        }
        else
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(photo.FilePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                ImgPhotoPreview.Source = bmp;
            }
            catch
            {
                ImgPhotoPreview.Source = null;
            }

            TxtPhotoTitle.Text = photo.Title;
            TxtPhotoSub.Text = photo.Description;

            bool isP1 = _samplePhotos.Count > 0 && photo.FilePath == _samplePhotos[0].FilePath;
            bool isP2 = _samplePhotos.Count > 1 && photo.FilePath == _samplePhotos[1].FilePath;

            BtnSamplePhoto1.Background = isP1 ? activeBrush : darkBrush;
            BtnSamplePhoto1.BorderBrush = isP1 ? cyanBrush : dimBorder;
            BtnSamplePhoto2.Background = isP2 ? activeBrush : darkBrush;
            BtnSamplePhoto2.BorderBrush = isP2 ? cyanBrush : dimBorder;
            BtnRemovePhoto.Background = darkBrush;
            BtnRemovePhoto.BorderBrush = dimBorder;
        }
    }

    private void BtnUploadPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*",
            Title = _localizer?.CurrentLanguage == "en" ? "Select Claim Photo" : "Sélectionner une photo du sinistre"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var custom = DamagePhotoAnalyzer.AnalyzeCustomPhoto(dlg.FileName);
                SelectPhoto(custom);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Photo Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnSamplePhoto1_Click(object sender, RoutedEventArgs e)
    {
        if (_samplePhotos.Count > 0) SelectPhoto(_samplePhotos[0]);
    }

    private void BtnSamplePhoto2_Click(object sender, RoutedEventArgs e)
    {
        if (_samplePhotos.Count > 1) SelectPhoto(_samplePhotos[1]);
    }

    private void BtnRemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        SelectPhoto(null);
    }

    private void BtnChipIrsa_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Evaluate liability under traffic convention rules (IRSA Cas 10), establish subrogation grounds against third-party insurer."
            : "Établir les responsabilités conventionnelles selon le barème IRSA (Cas 10) et formaliser les recours contre l'assureur du tiers.";
    }

    private void BtnChipPhotos_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Cross-reference the amicable claim declaration against photographic damage evidence. Assess kinematic consistency and identify any discrepancies."
            : "Confronter la déclaration du constat amiable aux preuves photographiques des dommages. Vérifier la cohérence cinématique et auditer les divergences.";
    }

    private void BtnChipAdas_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Analyze ADAS radar and sensor damages visible on photos and telemetry. Outline workshop calibration requirements (static target + dynamic drive)."
            : "Auditer les dommages sur les capteurs et le radar ADAS constatés sur les photos. Préciser les exigences d'étalonnage en atelier (mire statique + dynamique).";
    }

    private void BtnChipCost_Click(object sender, RoutedEventArgs e)
    {
        TxtInstruction.Text = _localizer.CurrentLanguage == "en"
            ? "Estimate preliminary repair costs (bodywork, bumper, ADAS calibration) and define indemnification next steps."
            : "Estimer le chiffrage prévisionnel des réparations (carrosserie, bouclier, capteurs ADAS) et formuler le plan d'action d'indemnisation.";
    }

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        bool isEn = _localizer.CurrentLanguage == "en";
        LblInsuranceTitle.Text = _localizer.Get("Insurance_Title");
        LblInsuranceSub.Text = _localizer.Get("Insurance_Subtitle");
        LblCardClaim.Text = _localizer.Get("Insurance_ClaimSummaryTitle");
        LblClaimHeader.Text = _localizer.Get("Insurance_ClaimHeader");
        LblClaimInsured.Text = _localizer.Get("Insurance_ClaimInsured");
        LblClaimVehicle.Text = _localizer.Get("Insurance_ClaimVehicle");
        LblCircumstancesLabel.Text = _localizer.Get("Insurance_Circumstances");
        LblInstructionLabel.Text = _localizer.Get("Insurance_QuestionLabel");
        BtnRun.Content = "▶ " + (isEn ? "Qualify Claim (Local AI)" : "Qualifier le sinistre (IA Locale)");
        LblGuidanceTitle.Text = _localizer.Get("Insurance_GuidanceTitle");
        LblLatencyHeader.Text = isEn ? "STOPWATCH / DURATION" : "CHRONO / DURÉE";
        LblTokensHeader.Text = isEn ? "GENERATED TOKENS" : "JETONS GÉNÉRÉS";
        LblComplianceHeader.Text = isEn ? "GDPR COMPLIANCE" : "CONFORMITÉ RGPD";
        RefreshDeviceUI();
        LblComplianceBadge.Text = _localizer.Get("Insurance_ZeroTrustBadge");
        LblNotice.Text = "🛡️ " + _localizer.Get("Insurance_DataNotice");
        if (TxtStep1 != null) TxtStep1.Text = isEn ? "1. Claim & Photos" : "1. Constat & Photos";
        if (TxtStep2 != null) TxtStep2.Text = isEn ? "2. Silicon Prefill" : "2. Prefill Silicium";
        if (TxtStep3 != null) TxtStep3.Text = isEn ? "3. Liability & Recovery" : "3. Qualification & Recours";
        if (TxtStep4 != null) TxtStep4.Text = isEn ? "4. Claim Qualified" : "4. Dossier Qualifié";
        if (!_isRunning && LblLiveStatus != null)
        {
            LblLiveStatus.Text = isEn
                ? "Ready • Waiting to start on-device claim qualification"
                : "Prêt • En attente de lancement de la qualification de sinistre";
        }
        if (TxtDocHelp != null) TxtDocHelp.Text = isEn ? "Guide & Doc" : "Guide & Doc";
        if (TxtBtnViewSourceDoc != null) TxtBtnViewSourceDoc.Text = isEn ? "Source Claim" : "Dossier Source";
        if (BtnViewSourceDoc != null) BtnViewSourceDoc.ToolTip = isEn ? "View full source claim declaration file (JSON)" : "Consulter la déclaration officielle et le constat amiable complet (JSON)";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = isEn 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";

        if (LblPhotoHeader != null) LblPhotoHeader.Text = isEn ? "PHOTO / DAMAGE EVIDENCE" : "CONSTAT PHOTO / DOMMAGES";
        if (TxtBtnUploadPhoto != null) TxtBtnUploadPhoto.Text = isEn ? "Import..." : "Importer...";
        if (TxtSamplePhoto1 != null) TxtSamplePhoto1.Text = isEn ? "Ex. 1 (Rear impact)" : "Ex. 1 (Choc arrière)";
        if (TxtSamplePhoto2 != null) TxtSamplePhoto2.Text = isEn ? "Ex. 2 (ADAS radar)" : "Ex. 2 (Radar ADAS)";
        if (TxtRemovePhoto != null) TxtRemovePhoto.Text = isEn ? "❌ No photo" : "❌ Sans photo";
        if (LblQuickQuestions != null) LblQuickQuestions.Text = isEn ? "Quick questions for demo:" : "Questions rapides pour la démo :";
        if (TxtChipIrsa != null) TxtChipIrsa.Text = isEn ? "⚖️ IRSA Recourse" : "⚖️ Recours IRSA";
        if (TxtChipPhotos != null) TxtChipPhotos.Text = isEn ? "📸 Photos vs Declaration" : "📸 Audit Photos vs Déclaration";
        if (TxtChipAdas != null) TxtChipAdas.Text = isEn ? "📡 Sensors & ADAS" : "📡 Capteurs & ADAS";
        if (TxtChipCost != null) TxtChipCost.Text = isEn ? "💶 Quoting & Recovery" : "💶 Chiffrage & Recours";

        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = isEn
                ? "Models: Local Vision + Phi-4-mini (Costing)"
                : "Modèles : Vision Locale + Phi-4-mini (Chiffrage)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = isEn
                    ? "Damage photo inspection coupled with claim qualification and repair quoting by Phi-4-mini on NPU."
                    : "Inspection des photos de dommages couplée à la qualification et au chiffrage des réparations par Phi-4-mini sur NPU.";
            }
        }

        string currentCirc = TxtCircumstances.Text.Trim();
        if (string.IsNullOrEmpty(currentCirc) || currentCirc.StartsWith("Véhicule") || currentCirc.StartsWith("Insured vehicle"))
        {
            TxtCircumstances.Text = _localizer.Get("Insurance_DefaultCircumstances");
        }

        string currentInst = TxtInstruction.Text.Trim();
        if (string.IsNullOrEmpty(currentInst) || currentInst.StartsWith("Qualifier") || currentInst.StartsWith("Qualify"))
        {
            TxtInstruction.Text = _localizer.Get("Insurance_DefaultInstruction");
        }

        if (string.IsNullOrWhiteSpace(TxtOutput.Text) ||
            TxtOutput.Text.StartsWith("Cliquez sur") ||
            TxtOutput.Text.StartsWith("Click on"))
        {
            TxtOutput.Text = _localizer.Get("Insurance_EmptyOutput");
        }
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = _localizer.CurrentLanguage == "en";
        BtnRun.IsEnabled = false;
        PrgBusy.Visibility = Visibility.Visible;
        TxtOutput.Text = isEn
            ? "⚡ Analyzing claim declaration & photo evidence on local NPU..."
            : "⚡ Analyse du constat amiable & expertise photo sur le NPU local...";
        LblLatency.Text = "0 ms";
        LblThroughput.Text = "...";
        LblTokens.Text = "0 tok";

        _isRunning = true;
        _tokenCount = 0;
        _firstTokenTimeSeconds = 0;
        _hasReceivedFirstToken = false;

        _stopwatch = Stopwatch.StartNew();
        _liveTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _liveTimer.Tick += (s, args) =>
        {
            if (_stopwatch != null && _isRunning)
            {
                long elapsedMs = _stopwatch.ElapsedMilliseconds;
                double elapsedSec = _stopwatch.Elapsed.TotalSeconds;
                LblLatency.Text = $"{elapsedMs} ms ({elapsedSec:F1}s)";

                if (_hasReceivedFirstToken && elapsedSec > _firstTokenTimeSeconds)
                {
                    double genTime = elapsedSec - _firstTokenTimeSeconds;
                    if (genTime > 0.05)
                    {
                        double liveTps = _tokenCount / genTime;
                        LblThroughput.Text = $"{liveTps:F1} tok/s";
                        LblTokens.Text = $"⚡ {_tokenCount} tok";
                    }
                }
                else
                {
                    LblThroughput.Text = isEn ? "TTFT prefill..." : "Prefill NPU...";
                }
            }
        };
        _liveTimer.Start();

        SetPipelineStep(1, isEn
            ? "📁 Ingesting claim declaration & photo inspection evidence..."
            : "📁 Ingestion dossier sinistre : Constat amiable & expertise photo...");

        string circumstances = TxtCircumstances.Text.Trim();
        string instruction = TxtInstruction.Text.Trim();
        string targetDevice = _getDeviceFunc();

        try
        {
            var selectedModel = await _foundryClient.GetBestModelAsync("phi-4-mini", targetDevice);
            string effective = selectedModel.TargetDevice ?? _getEffectiveDeviceFunc();
            string chipTag = targetDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ? $"Auto: {effective}" : effective;

            SetPipelineStep(2, isEn
                ? $"⚡ Silicon prefill active on {effective} (cross-referencing statement vs damage photos)..."
                : $"⚡ Silicium local actif ({effective}) : Confrontation déclarative vs visuelle...");

            string languageInstruction = _localizer.Get("Language_ResponseInstruction");
            string executiveDirective = isEn
                ? "Direct executive tone, concise bullet points, exact technical and financial figures. No intro or conversational filler."
                : "Ton exécutif direct, puces synthétiques, chiffres techniques et financiers précis. Aucun verbiage d'introduction.";
            string systemPrompt = InsurancePromptBuilder.BuildSystemPrompt(languageInstruction) + "\n\n" + executiveDirective;

            // Charger l'intégralité du dossier source officiel
            string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Dossier_Sinistre_SIN-2026-8942.json");
            string candidate = direct;
            if (!File.Exists(candidate))
            {
                string? dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6 && dir != null; i++)
                {
                    string test = Path.Combine(dir, "Data", "Documents", "Dossier_Sinistre_SIN-2026-8942.json");
                    if (File.Exists(test)) { candidate = test; break; }
                    dir = Path.GetDirectoryName(dir);
                }
            }

            string claimJson = File.Exists(candidate) ? File.ReadAllText(candidate) : $@"{{
  ""numero_sinistre"": ""SIN-2026-8942"",
  ""assure"": ""Marc DUPONT"",
  ""vehicule"": ""Modèle SUV compact (2024)"",
  ""tiers"": ""Martin"",
  ""vehicule_tiers"": ""Fourgonnette utilitaire (secteur livraison)"",
  ""circonstances"": ""{circumstances.Replace("\"", "\\\"")}""
}}";

            string userPrompt = InsurancePromptBuilder.BuildUserPrompt(
                claimJson,
                instruction,
                languageInstruction,
                _currentPhoto);

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
                                ? $"✍️ Claim qualification streaming (First token at {_firstTokenTimeSeconds:F2}s)..."
                                : $"✍️ Rédaction de la qualification en temps réel (1er jeton à {_firstTokenTimeSeconds:F2}s)...");
                        }

                        _tokenCount += Math.Max(1, chunk.Length / 4);
                        TxtOutput.AppendText(chunk);
                        ScrollerOutput.ScrollToEnd();

                        string fullText = TxtOutput.Text;
                        double elapsed = Math.Max(0.1, (_stopwatch?.Elapsed.TotalSeconds ?? 1) - _firstTokenTimeSeconds);
                        double currentTps = _tokenCount / elapsed;

                        if (fullText.Contains("Plan d'action") || fullText.Contains("Mesures") || fullText.Contains("Action Plan") || fullText.Contains("Next Steps"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [4/4 Action Plan & Indemnification] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [4/4 Plan d'action & Indemnisation] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Dommage") || fullText.Contains("Technique") || fullText.Contains("Chiffrage") || fullText.Contains("Damage") || fullText.Contains("Cost") || fullText.Contains("ADAS"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [3/4 Damage Inspection & ADAS Radar] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [3/4 Dégâts, Radar ADAS & Chiffrage] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Responsabilit") || fullText.Contains("IRSA") || fullText.Contains("Recours") || fullText.Contains("Liability") || fullText.Contains("Subrogation"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [2/4 Liability & IRSA Recourse] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [2/4 Responsabilité & Recours IRSA] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
                        }
                        else if (fullText.Contains("Synthèse") || fullText.Contains("Identification") || fullText.Contains("Summary") || fullText.Contains("Sinistre"))
                        {
                            LblLiveStatus.Text = isEn
                                ? $"✍️ [1/4 Claim & Evidence Reconciliation] • {_tokenCount} tokens ({currentTps:F1} tok/s)"
                                : $"✍️ [1/4 Synthèse & Rapprochement Déclaratif] • {_tokenCount} jetons ({currentTps:F1} tok/s)";
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
                    ? $"✅ Claim qualified successfully ({result.CompletionTokens} tokens in {result.LatencyMilliseconds / 1000.0:F1}s) • 100% GDPR compliant 🔒"
                    : $"✅ Dossier sinistre qualifié ({result.CompletionTokens} jetons en {result.LatencyMilliseconds / 1000.0:F1}s) • 100% conforme RGPD 🔒");
            });

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Assurance & Sinistres",
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
                TxtOutput.Text = $"Erreur lors de la qualification locale : {ex.Message}";
                LblLiveStatus.Text = $"⚠️ Erreur d'analyse : {ex.Message}";
            });
        }
        finally
        {
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
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_insurance", _localizer?.CurrentLanguage);
    }

    private void BtnViewSourceDoc_Click(object sender, RoutedEventArgs e)
    {
        string direct = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Documents", "Dossier_Sinistre_SIN-2026-8942.json");
        string candidate = direct;
        if (!File.Exists(candidate))
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6 && dir != null; i++)
            {
                string test = Path.Combine(dir, "Data", "Documents", "Dossier_Sinistre_SIN-2026-8942.json");
                if (File.Exists(test)) { candidate = test; break; }
                dir = Path.GetDirectoryName(dir);
            }
        }

        string content = File.Exists(candidate) ? File.ReadAllText(candidate) : "{}";
        bool isEn = _localizer?.CurrentLanguage == "en";
        SourceDocumentViewerWindow.ShowDocument(
            owner: Window.GetWindow(this),
            title: isEn ? "Claim File • Ref. SIN-2026-8942" : "Dossier de Sinistre • Réf. SIN-2026-8942",
            content: content,
            format: "JSON",
            filePath: File.Exists(candidate) ? candidate : null,
            localizer: _localizer,
            icon: "📁"
        );
    }
}
