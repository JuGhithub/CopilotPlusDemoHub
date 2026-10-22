using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class AudioStudioView : UserControl, IDisposable
{
    private readonly FoundryClient _foundryClient;
    private LocalizationService _localizer;
    private readonly HardwareTelemetryService _telemetryService = new();

    private readonly MediaPlayer _mediaPlayer = new();
    private bool _isPlaying = false;
    private bool _isMasterMuted = false;
    private double _masterVolume = 0.8;
    private string _currentAudioPath = string.Empty;
    private double _audioDurationSeconds = 23.79;
    private bool _isViewLoaded = false;

    private enum SiliconMode { NPU, GPU }
    private SiliconMode _selectedSilicon = SiliconMode.NPU;

    private double _accumulatedJoules = 0.0;
    private readonly Stopwatch _energyStopwatch = new();
    private Process? _transcribeProcess;
    private CancellationTokenSource? _transcribeCts;
    private Process? _whisperDaemonProcess;
    private string _whisperDaemonDevice = string.Empty;
    private readonly object _whisperDaemonLock = new();

    private Process? _liveProcess;
    private bool _isLiveActive = false;
    private DispatcherTimer? _liveTimer;
    private int _liveElapsedSec = 0;
    private string _liveAudioPath = string.Empty;

    private readonly DispatcherTimer _playbackTimer;
    private readonly DispatcherTimer _telemetryTimer;

    private string _fullAccumulatedTranscript = string.Empty;
    private string _fullTranslatedTranscript = string.Empty;
    private string _translateTarget = "none";
    private readonly SemaphoreSlim _translateLock = new(1, 1);

    private class TranscriptSegmentData
    {
        public double StartSec { get; set; }
        public double EndSec { get; set; }
        public string Text { get; set; } = string.Empty;
        public string SpeakerId { get; set; } = string.Empty;
        public string SpeakerName { get; set; } = string.Empty;
        public string SpeakerColor { get; set; } = "#3B82F6";
        public bool IsKnown { get; set; }
        public double Confidence { get; set; } = 1.0;
        public List<double>? Embedding { get; set; }
        public string AudioFilePath { get; set; } = string.Empty;
        public TextBlock? NameTextBlock { get; set; }
        public Border? BadgeBorder { get; set; }
        public StackPanel? MainStack { get; set; }
        public TextBlock? BodyTextBlock { get; set; }
        public TextBlock? TranslationTextBlock { get; set; }
        public string DetectedLang { get; set; } = string.Empty;
        public string TranslationText { get; set; } = string.Empty;
        public string TranslationTargetLang { get; set; } = string.Empty;
    }

    private readonly List<TranscriptSegmentData> _transcriptSegments = new();
    private TranscriptSegmentData? _targetSegmentForRename;

    private static string FindRootFile(string relativePath)
    {
        string direct = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        if (File.Exists(direct)) return direct;

        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6 && dir != null; i++)
        {
            string candidate = System.IO.Path.Combine(dir, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }

        string full = System.IO.Path.GetFullPath(relativePath);
        if (File.Exists(full)) return full;

        return direct;
    }

    public AudioStudioView(FoundryClient foundryClient, LocalizationService localizer)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;

        // Default sample meeting audio
        string defaultAudio = FindRootFile(System.IO.Path.Combine("Data", "Audio", "reunion_codir.wav"));
        LoadAudioFile(defaultAudio, "reunion_codir.wav (22s • Whisper Small NPU)");

        // Playback timer (100ms)
        _playbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _playbackTimer.Tick += PlaybackTimer_Tick;
        _playbackTimer.Start();

        // Telemetry update timer (500ms)
        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        try { _telemetryService.Start(TimeSpan.FromSeconds(1)); } catch { }
        _telemetryTimer.Tick += TelemetryTimer_Tick;
        _telemetryTimer.Start();

        _mediaPlayer.MediaEnded += (s, e) => StopAudio();
        _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

        SetSiliconMode(SiliconMode.NPU);
        ApplyLocalization(_localizer);
        _isViewLoaded = true;
    }

    private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
    {
        if (_mediaPlayer.NaturalDuration.HasTimeSpan)
        {
            _audioDurationSeconds = _mediaPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (_audioDurationSeconds <= 0) _audioDurationSeconds = 24.0;
            SliderTimeline.Maximum = _audioDurationSeconds;
            int min = (int)_audioDurationSeconds / 60;
            int sec = (int)_audioDurationSeconds % 60;
            LblTotalTime.Text = $"{min:D2}:{sec:D2}";
        }
    }

    private void LoadAudioFile(string path, string displayName)
    {
        _currentAudioPath = path;
        string activePattern = _localizer?.Get("Audio_ActiveFileLabel") ?? "Fichier actif : {0}";
        LblLoadedTrackInfo.Text = string.Format(activePattern, displayName);

        if (File.Exists(path))
        {
            try
            {
                _mediaPlayer.Open(new Uri(path));
                _mediaPlayer.Volume = _masterVolume;
            }
            catch
            {
            }
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (_isPlaying && _mediaPlayer.Source != null)
        {
            double curSec = _mediaPlayer.Position.TotalSeconds;
            SliderTimeline.Value = Math.Min(curSec, SliderTimeline.Maximum);
            int min = (int)curSec / 60;
            int sec = (int)curSec % 60;
            LblCurrentTime.Text = $"{min:D2}:{sec:D2}";
        }

        // Accumulate energy when transcription is running
        if (_energyStopwatch.IsRunning)
        {
            double deltaSec = _energyStopwatch.Elapsed.TotalSeconds;
            _energyStopwatch.Restart();

            HardwareSnapshot snapshot = _telemetryService.GetCurrentSnapshot();
            if (snapshot.DischargeRateWatts > 0)
            {
                _accumulatedJoules += snapshot.DischargeRateWatts * deltaSec;
                LblJoulesCounter.Text = $"{_accumulatedJoules:F1} J";
            }
            else
            {
                LblJoulesCounter.Text = "n/a";
            }
        }
    }

    private void TelemetryTimer_Tick(object? sender, EventArgs e)
    {
        HardwareSnapshot snapshot = _telemetryService.GetCurrentSnapshot();

        float npuVal = (float)snapshot.NpuUsagePercent;
        float gpuVal = (float)snapshot.GpuUsagePercent;
        float cpuVal = (float)snapshot.CpuUsagePercent;

        bool npuBusy = (_transcribeProcess != null || _isLiveActive) && _selectedSilicon == SiliconMode.NPU;
        LblNpuLoad.Text = (npuVal == 0 && npuBusy) ? "n/a" : $"{npuVal:F0} %";
        LblGpuLoad.Text = $"{gpuVal:F0} %";
        LblCpuLoad.Text = $"{cpuVal:F0} %";

        if (snapshot.DischargeRateWatts > 0)
        {
            LblInstantWatts.Text = $"{snapshot.DischargeRateWatts:F1} W";
        }
        else
        {
            LblInstantWatts.Text = !snapshot.IsOnBattery ? "secteur" : "n/a";
        }
    }

    private void SetSiliconMode(SiliconMode mode)
    {
        _selectedSilicon = mode;

        if (mode == SiliconMode.NPU)
        {
            BtnNpuMode.Background = (Brush)FindResource("NpuGreenBrush");
            BtnNpuMode.Foreground = Brushes.Black;
            BtnGpuMode.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            BtnGpuMode.Foreground = Brushes.White;

            PillAudioStatus.BorderBrush = (Brush)FindResource("NpuGreenBrush");
            TxtAudioStatus.Foreground = (Brush)FindResource("NpuGreenBrush");
            TxtAudioStatus.Text = _localizer?.Get("Audio_StatusNpu") ?? "⚡ MODE NPU ACTIF : INFERENCE SOBRE (~5.5W) • CPU & GPU 100% LIBRES";

            LblInstantWatts.Text = "5.5 W";
            LblInstantWatts.Foreground = (Brush)FindResource("CopilotCyanBrush");
            LblPowerSub.Text = (_localizer?.CurrentLanguage == "en") ? "Ultra-low power NPU" : "NPU ultra-sobre";
            PrgTranscription.Foreground = (Brush)FindResource("NpuGreenBrush");
        }
        else
        {
            BtnGpuMode.Background = (Brush)FindResource("GpuAmberBrush");
            BtnGpuMode.Foreground = Brushes.Black;
            BtnNpuMode.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
            BtnNpuMode.Foreground = Brushes.White;

            PillAudioStatus.BorderBrush = (Brush)FindResource("GpuAmberBrush");
            TxtAudioStatus.Foreground = (Brush)FindResource("GpuAmberBrush");
            TxtAudioStatus.Text = _localizer?.Get("Audio_StatusGpu") ?? $"🎮 MODE GPU ({SiliconDetector.GpuName.ToUpperInvariant()}) : PUISSANCE ÉLEVÉE (~32W) • VENTILATION & CHAUFFE";

            LblInstantWatts.Text = "32.0 W";
            LblInstantWatts.Foreground = (Brush)FindResource("DangerRedBrush");
            LblPowerSub.Text = (_localizer?.CurrentLanguage == "en") ? "High-power GPU" : "GPU haute puissance";
            PrgTranscription.Foreground = (Brush)FindResource("DangerRedBrush");
        }

        if (TxtLiveIndicator != null && !_isLiveActive && _localizer != null)
        {
            TxtLiveIndicator.Text = string.Format(_localizer.Get("Audio_LiveMicIndicator"), mode);
        }
        if (BtnSummarizeMeeting != null && _localizer != null)
        {
            BtnSummarizeMeeting.Content = string.Format(_localizer.Get("Audio_BtnSummarize"), mode);
        }
        if (LblSummaryTitle != null && _localizer != null)
        {
            LblSummaryTitle.Text = string.Format(_localizer.Get("Audio_SummaryHeader"), mode);
        }

        if (!_isLiveActive && _transcribeProcess == null)
        {
            WarmUpWhisperDaemon(mode == SiliconMode.NPU ? "NPU" : "GPU");
        }
    }

    private Process EnsureWhisperDaemon(string deviceStr)
    {
        lock (_whisperDaemonLock)
        {
            if (_whisperDaemonProcess != null &&
                !_whisperDaemonProcess.HasExited &&
                string.Equals(_whisperDaemonDevice, deviceStr, StringComparison.OrdinalIgnoreCase))
            {
                return _whisperDaemonProcess;
            }

            StopWhisperDaemon();

            string scriptPath = FindRootFile("transcribe_service.py");
            string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

            var psi = new ProcessStartInfo
            {
                FileName = PythonPathHelper.GetPythonExe(),
                Arguments = $"\"{scriptPath}\" --daemon --device {deviceStr}",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = System.Text.Encoding.UTF8,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            var proc = new Process { StartInfo = psi };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    System.Diagnostics.Debug.WriteLine($"[Whisper Daemon Stderr] {e.Data}");
                }
            };
            proc.Start();
            proc.BeginErrorReadLine();

            _whisperDaemonProcess = proc;
            _whisperDaemonDevice = deviceStr;
            return proc;
        }
    }

    private void WarmUpWhisperDaemon(string deviceStr)
    {
        _ = Task.Run(() =>
        {
            try
            {
                EnsureWhisperDaemon(deviceStr);
            }
            catch { }
        });
    }

    private void StopWhisperDaemon()
    {
        lock (_whisperDaemonLock)
        {
            if (_whisperDaemonProcess != null)
            {
                try
                {
                    if (!_whisperDaemonProcess.HasExited)
                    {
                        try
                        {
                            _whisperDaemonProcess.StandardInput.WriteLine("stop");
                            _whisperDaemonProcess.StandardInput.Flush();
                        }
                        catch { }
                        if (!_whisperDaemonProcess.WaitForExit(500))
                        {
                            _whisperDaemonProcess.Kill(entireProcessTree: true);
                        }
                    }
                }
                catch { }
                finally
                {
                    try { _whisperDaemonProcess.Dispose(); } catch { }
                    _whisperDaemonProcess = null;
                    _whisperDaemonDevice = string.Empty;
                }
            }
        }
    }

    #region Real-Time Live Microphone Streaming Transcription & Diarization
    private static string LangFlag(string langCode) => (langCode ?? "").ToLowerInvariant() switch
    {
        "fr" => "🇫🇷",
        "en" => "🇬🇧",
        "es" => "🇪🇸",
        "de" => "🇩🇪",
        "it" => "🇮🇹",
        _ => "🌐"
    };

    private string FormatLanguageFriendlyName(string langCode)
    {
        return langCode.ToLowerInvariant() switch
        {
            "fr" => "🇫🇷 Français",
            "en" => "🇬🇧 English",
            "es" => "🇪🇸 Español",
            "de" => "🇩🇪 Deutsch",
            "it" => "🇮🇹 Italiano",
            _ => $"🌐 {langCode.ToUpperInvariant()}"
        };
    }

    private async void CmbTranslate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isViewLoaded) return;
        if (CmbTranslate.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _translateTarget = tag;
            if (_isLiveActive && _liveProcess != null && !_liveProcess.HasExited)
            {
                try
                {
                    _liveProcess.StandardInput.WriteLine($"translate {tag}");
                    _liveProcess.StandardInput.Flush();
                }
                catch { }
            }
            else if (_transcriptSegments.Count > 0)
            {
                await TranslateAllSegmentsAsync(tag);
            }
        }
    }

    public async Task TranslateAllSegmentsAsync(string targetLang)
    {
        if (_transcriptSegments.Count == 0) return;

        bool isEn = _localizer?.CurrentLanguage == "en";
        string targetSilicon = _selectedSilicon.ToString();
        string langEnglishName = LangNameEnglish(targetLang);

        if (targetLang == "none")
        {
            _translateTarget = "none";
            foreach (var seg in _transcriptSegments)
            {
                if (seg.TranslationTextBlock != null)
                {
                    seg.TranslationTextBlock.Visibility = Visibility.Collapsed;
                }
            }
            return;
        }

        _translateTarget = targetLang;
        string trPrefix = $"🌐 → {LangFlag(targetLang)} {targetLang.ToUpperInvariant()} : ";

        // 1. Instantly display loading state on every segment card
        foreach (var seg in _transcriptSegments)
        {
            if (seg.TranslationTextBlock == null)
            {
                var trBlock = new TextBlock
                {
                    Text = trPrefix + (isEn ? "⏳ Translating..." : "⏳ Traduction en cours..."),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("CopilotCyanBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                seg.TranslationTextBlock = trBlock;
                seg.MainStack?.Children.Add(trBlock);
            }
            else
            {
                seg.TranslationTextBlock.Visibility = Visibility.Visible;
                seg.TranslationTextBlock.Text = trPrefix + (isEn ? "⏳ Translating..." : "⏳ Traduction en cours...");
            }
        }

        try
        {
            await _foundryClient.EnsureServerReadyAsync();
            var selectedModel = await _foundryClient.GetBestModelAsync("phi-4-mini", targetSilicon);

            var lines = new List<string>();
            for (int i = 0; i < _transcriptSegments.Count; i++)
            {
                lines.Add($"[{i + 1}] {_transcriptSegments[i].Text}");
            }
            string userPrompt = string.Join(Environment.NewLine, lines);

            string sysPrompt =
                $"You are a professional translator. Translate each numbered line into {langEnglishName}. " +
                "Preserve the exact line numbering [1], [2], etc. Output ONLY the translated numbered lines, with no preface or commentary.";

            var result = await _foundryClient.AskAsync(selectedModel, sysPrompt, userPrompt, temperature: 0.1, maxTokens: 1200);
            string content = result.Content ?? string.Empty;

            var translatedLines = new Dictionary<int, string>();
            foreach (var rawLine in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = rawLine.Trim();
                if (trimmed.StartsWith("["))
                {
                    int closeBracket = trimmed.IndexOf(']');
                    if (closeBracket > 1 && int.TryParse(trimmed.Substring(1, closeBracket - 1), out int num))
                    {
                        string lineText = trimmed.Substring(closeBracket + 1).Trim().TrimStart(':', '-', ' ');
                        translatedLines[num] = lineText;
                    }
                }
            }

            _fullTranslatedTranscript = string.Empty;
            for (int i = 0; i < _transcriptSegments.Count; i++)
            {
                var seg = _transcriptSegments[i];
                string translatedText;
                if (translatedLines.TryGetValue(i + 1, out var tl) && !string.IsNullOrWhiteSpace(tl))
                {
                    translatedText = tl;
                }
                else
                {
                    translatedText = await TranslateWithLlmAsync(seg.Text, targetLang);
                }

                seg.TranslationText = translatedText;
                seg.TranslationTargetLang = targetLang;
                if (seg.TranslationTextBlock != null)
                {
                    seg.TranslationTextBlock.Text = trPrefix + translatedText;
                }
                _fullTranslatedTranscript += $" [{seg.SpeakerName}] " + translatedText;
            }
        }
        catch (Exception ex)
        {
            foreach (var seg in _transcriptSegments)
            {
                if (seg.TranslationTextBlock != null && seg.TranslationTextBlock.Text.Contains("⏳"))
                {
                    seg.TranslationTextBlock.Text = trPrefix + (isEn
                        ? $"⚠️ Translation error: {ex.Message}"
                        : $"⚠️ Erreur traduction : {ex.Message}");
                }
            }
        }
    }

    private static string LangNameEnglish(string code) => code.ToLowerInvariant() switch
    {
        "fr" => "French",
        "en" => "English",
        "es" => "Spanish",
        "de" => "German",
        "it" => "Italian",
        _ => code
    };

    /// <summary>Traduction locale via Phi-4-mini (Foundry Local), sérialisée pour ne pas saturer le NPU.</summary>
    private async Task<string> TranslateWithLlmAsync(string text, string targetLang)
    {
        await _translateLock.WaitAsync();
        try
        {
            string silicon = _selectedSilicon.ToString();
            var model = await _foundryClient.GetBestModelAsync("phi-4-mini", silicon);
            string system =
                $"You are a professional interpreter. Translate the user's text into {LangNameEnglish(targetLang)}. " +
                "Output ONLY the translation: no quotes, no comments, no explanations, keep the meaning and tone.";
            var result = await _foundryClient.AskAsync(model, system, text, temperature: 0.1, maxTokens: 400);
            return result.Content.Trim().Trim('"');
        }
        finally
        {
            _translateLock.Release();
        }
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isViewLoaded) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (_isLiveActive && _liveProcess != null && !_liveProcess.HasExited)
            {
                try
                {
                    _liveProcess.StandardInput.WriteLine($"language {tag}");
                    _liveProcess.StandardInput.Flush();

                    if (tag != "auto")
                    {
                        BadgeDetectedLanguage.Visibility = Visibility.Visible;
                        string langName = FormatLanguageFriendlyName(tag.Replace("<|", "").Replace("|>", ""));
                        TxtDetectedLanguage.Text = string.Format(_localizer.Get("Audio_LangForced"), langName);
                    }
                }
                catch { }
            }
            else
            {
                string langName = (tag == "auto")
                    ? (_localizer?.CurrentLanguage == "en" ? "Auto-Detect" : "Auto-Détection")
                    : FormatLanguageFriendlyName(tag.Replace("<|", "").Replace("|>", ""));
                if (LblTranscriptionStatus != null && string.IsNullOrWhiteSpace(_fullAccumulatedTranscript))
                {
                    LblTranscriptionStatus.Text = (_localizer?.CurrentLanguage == "en")
                        ? $"Language: {langName}. Click 'Transcribe file' to run recognition."
                        : $"Langue : {langName}. Cliquez sur 'Transcrire le fichier' pour lancer la reconnaissance.";
                }
            }
        }
    }

    private void BtnLiveTranscribe_Click(object sender, RoutedEventArgs e)
    {
        if (!_isLiveActive)
        {
            StartLiveTranscription();
        }
        else
        {
            StopLiveTranscription();
        }
    }

    private void StartLiveTranscription()
    {
        StopAudio();
        StopWhisperDaemon();

        string recDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Audio", "recordings");
        Directory.CreateDirectory(recDir);
        string fileName = $"direct_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
        _liveAudioPath = System.IO.Path.Combine(recDir, fileName);
        _currentAudioPath = _liveAudioPath;

        string scriptPath = FindRootFile("live_transcribe_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        string deviceStr = (_selectedSilicon == SiliconMode.NPU) ? "NPU" : "GPU";
        string selectedLang = "auto";
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            selectedLang = tag;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = PythonPathHelper.GetPythonExe(),
                Arguments = $"\"{scriptPath}\" --output \"{_liveAudioPath}\" --device {deviceStr} --language \"{selectedLang}\" --translate_to {_translateTarget}",
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            _liveProcess = new Process { StartInfo = psi };
            _liveProcess.Start();

            _isLiveActive = true;
            _liveElapsedSec = 0;

            BtnLiveTranscribe.Content = string.Format(_localizer.Get("Audio_BtnOneClickStop"), "00:00");
            BtnLiveTranscribe.Background = Brushes.Crimson;

            BtnImportAudio.IsEnabled = false;
            BtnSampleMeeting.IsEnabled = false;
            BtnSampleConversation.IsEnabled = false;
            BtnManageProfiles.IsEnabled = false;
            BtnStartTranscription.IsEnabled = false;
            BtnPlayPause.IsEnabled = false;

            StackTranscriptSegments.Children.Clear();
            _transcriptSegments.Clear();
            _fullAccumulatedTranscript = string.Empty;
        _fullTranslatedTranscript = string.Empty;

            TxtTranscriptPlaceholder.Visibility = Visibility.Visible;
            TxtTranscriptPlaceholder.Text = string.Format(_localizer.Get("Audio_InitializingPrompt"), deviceStr);
            LblTranscriptionStatus.Text = string.Format(_localizer.Get("Audio_StatusLoadingWhisper"), deviceStr);

            BadgeLiveIndicator.Visibility = Visibility.Visible;
            BadgeDetectedLanguage.Visibility = Visibility.Collapsed;
            BadgeSpeedFactor.Visibility = Visibility.Collapsed;
            BorderSummary.Visibility = Visibility.Collapsed;

            _energyStopwatch.Restart();

            Task.Run(() =>
            {
                try
                {
                    string? line;
                    while ((line = _liveProcess?.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (!root.TryGetProperty("type", out var typeProp)) continue;
                            string type = typeProp.GetString() ?? "";

                            if (type == "live_ready")
                            {
                                int loadMs = root.TryGetProperty("load_time_ms", out var lp) ? lp.GetInt32() : 0;
                                string effLive = root.TryGetProperty("effective_device", out var edp) ? (edp.GetString() ?? deviceStr) : deviceStr;
                                bool fbLive = root.TryGetProperty("fallback", out var fbp) && fbp.ValueKind == JsonValueKind.True;
                                string liveDevDisplay = fbLive ? $"{effLive} (repli)" : effLive;
                                Dispatcher.Invoke(() =>
                                {
                                    TxtTranscriptPlaceholder.Text = _localizer.Get("Audio_ListeningPrompt");
                                    LblTranscriptionStatus.Text = string.Format(_localizer.Get("Audio_StatusLiveRecording"), liveDevDisplay);

                                    _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                                    _liveTimer.Tick += (s, ev) =>
                                    {
                                        _liveElapsedSec++;
                                        int m = _liveElapsedSec / 60;
                                        int sec = _liveElapsedSec % 60;
                                        BtnLiveTranscribe.Content = string.Format(_localizer.Get("Audio_BtnOneClickStop"), $"{m:D2}:{sec:D2}");
                                    };
                                    _liveTimer.Start();
                                });
                            }
                            else if (type == "live_segment")
                            {
                                double startTs = root.GetProperty("start_ts").GetDouble();
                                double endTs = root.GetProperty("end_ts").GetDouble();
                                string text = root.GetProperty("text").GetString() ?? "";
                                string speakerId = root.TryGetProperty("speaker_id", out var spkId) ? spkId.GetString() ?? "spk_1" : "spk_1";
                                string speakerDefault = _localizer.Get("Audio_SpeakerDefault");
                                string speakerName = root.TryGetProperty("speaker_name", out var spkNm) ? spkNm.GetString() ?? speakerDefault : speakerDefault;
                                if (speakerName == "Intervenant 1" && _localizer.CurrentLanguage != "fr")
                                {
                                    speakerName = $"{speakerDefault} 1";
                                }
                                string speakerColor = root.TryGetProperty("speaker_color", out var spkCol) ? spkCol.GetString() ?? "#3B82F6" : "#3B82F6";
                                bool isKnown = root.TryGetProperty("is_known", out var kn) && kn.GetBoolean();
                                double conf = root.TryGetProperty("confidence", out var cf) ? cf.GetDouble() : 1.0;
                                int inferMs = root.TryGetProperty("infer_ms", out var im) ? im.GetInt32() : 0;

                                string detectedLang = "";
                                if (root.TryGetProperty("detected_language", out var detLangProp))
                                {
                                    detectedLang = detLangProp.GetString() ?? "";
                                }
                                string? translation = (root.TryGetProperty("translation", out var trProp) && trProp.ValueKind == JsonValueKind.String) ? trProp.GetString() : null;

                                List<double>? embedding = null;
                                if (root.TryGetProperty("embedding", out var embProp) && embProp.ValueKind == JsonValueKind.Array)
                                {
                                    embedding = new List<double>();
                                    foreach (var el in embProp.EnumerateArray())
                                    {
                                        embedding.Add(el.GetDouble());
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    _fullAccumulatedTranscript += $" [{speakerName}] " + text;
                                    Dispatcher.Invoke(() =>
                                    {
                                        TxtTranscriptPlaceholder.Visibility = Visibility.Collapsed;
                                        AddTranscriptSegmentCard(startTs, endTs, text, speakerId, speakerName, speakerColor, isKnown, conf, embedding, _liveAudioPath, detectedLang, translation);
                                        ScrollerTranscript.ScrollToEnd();

                                        if (!string.IsNullOrWhiteSpace(detectedLang))
                                        {
                                            BadgeDetectedLanguage.Visibility = Visibility.Visible;
                                            string friendlyLang = FormatLanguageFriendlyName(detectedLang);
                                            TxtDetectedLanguage.Text = string.Format(_localizer.Get("Audio_LangDetected"), friendlyLang);
                                        }

                                        LblInferTime.Text = $"{inferMs} ms";
                                        LblSpeedRelative.Text = string.Format(_localizer.Get("Audio_SpeedDirectNpu"), deviceStr);
                                        LblSpeedFactor.Text = string.Format(_localizer.Get("Audio_SpeedLatency"), inferMs);
                                        BadgeSpeedFactor.Visibility = Visibility.Visible;
                                        LblTranscriptionStatus.Text = string.Format(_localizer.Get("Audio_StatusSegmentDone"), speakerName);
                                    });
                                }
                            }
                            else if (type == "speech_state")
                            {
                                bool isSpeaking = root.TryGetProperty("is_speaking", out var isp) && isp.GetBoolean();

                                Dispatcher.Invoke(() =>
                                {
                                    if (isSpeaking)
                                    {
                                        TxtLiveIndicator.Text = string.Format(_localizer.Get("Audio_SpeechDetected"), deviceStr);
                                    }
                                    else
                                    {
                                        TxtLiveIndicator.Text = string.Format(_localizer.Get("Audio_LiveMicIndicator"), deviceStr);
                                    }
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de démarrer la transcription en direct : {ex.Message}", "Erreur Direct", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StopLiveTranscription()
    {
        if (!_isLiveActive || _liveProcess == null) return;

        _liveTimer?.Stop();
        _liveTimer = null;

        BtnLiveTranscribe.Content = _localizer.Get("Audio_BtnOneClickFinalizing");
        BtnLiveTranscribe.IsEnabled = false;

        await Task.Run(() =>
        {
            try
            {
                _liveProcess.StandardInput.WriteLine("stop");
                _liveProcess.StandardInput.Flush();
                if (!_liveProcess.WaitForExit(4000))
                {
                    _liveProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                try { _liveProcess.Dispose(); } catch { }
                _liveProcess = null;
            }
        });

        _isLiveActive = false;
        BtnLiveTranscribe.Content = _localizer.Get("Audio_BtnOneClickLive");
        BtnLiveTranscribe.Background = (Brush)FindResource("DangerRedBrush");
        BtnLiveTranscribe.IsEnabled = true;

        BadgeLiveIndicator.Visibility = Visibility.Collapsed;
        BtnImportAudio.IsEnabled = true;
        BtnSampleMeeting.IsEnabled = true;
        BtnSampleConversation.IsEnabled = true;
        BtnManageProfiles.IsEnabled = true;
        BtnStartTranscription.IsEnabled = true;
        BtnPlayPause.IsEnabled = true;
        _energyStopwatch.Stop();
        WarmUpWhisperDaemon((_selectedSilicon == SiliconMode.NPU) ? "NPU" : "GPU");

        if (File.Exists(_liveAudioPath))
        {
            string fileName = System.IO.Path.GetFileName(_liveAudioPath);
            LoadAudioFile(_liveAudioPath, $"{fileName} (Direct Micro • {_liveElapsedSec}{_localizer.Get("Audio_UnitSeconds")})");
            LblTranscriptionStatus.Text = string.Format(_localizer.Get("Audio_StatusLiveDone"), _liveElapsedSec);
        }
        else
        {
            LblTranscriptionStatus.Text = _localizer.Get("Audio_StatusLiveStopped");
        }
    }
    #endregion

    #region Real Whisper Transcription & Speaker Diarization
    private async void BtnStartTranscription_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_currentAudioPath))
        {
            MessageBox.Show("Veuillez sélectionner un fichier audio valide avant de lancer la transcription.", "Fichier introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnStartTranscription.IsEnabled = false;
        BtnLiveTranscribe.IsEnabled = false;
        BtnImportAudio.IsEnabled = false;
        BtnSampleMeeting.IsEnabled = false;
        BtnSampleConversation.IsEnabled = false;
        PrgTranscription.IsIndeterminate = true;
        BadgeSpeedFactor.Visibility = Visibility.Collapsed;
        BorderSummary.Visibility = Visibility.Collapsed;

        StackTranscriptSegments.Children.Clear();
        _transcriptSegments.Clear();
        TxtTranscriptPlaceholder.Visibility = Visibility.Collapsed;

        string deviceStr = (_selectedSilicon == SiliconMode.NPU) ? "NPU" : "GPU";
        LblTranscriptionStatus.Text = string.Format(_localizer.Get("Audio_StatusInitializing"), deviceStr);

        _fullAccumulatedTranscript = string.Empty;
        _fullTranslatedTranscript = string.Empty;
        _accumulatedJoules = 0.0;
        _energyStopwatch.Restart();

        string selectedLang = "auto";
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            selectedLang = tag;
        }

        _transcribeCts = new CancellationTokenSource();
        var token = _transcribeCts.Token;

        await Task.Run(() =>
        {
            try
            {
                var proc = EnsureWhisperDaemon(deviceStr);
                _transcribeProcess = proc;

                string jobJson = JsonSerializer.Serialize(new
                {
                    file = _currentAudioPath,
                    language = selectedLang,
                    translate_to = _translateTarget
                });
                proc.StandardInput.WriteLine(jobJson);
                proc.StandardInput.Flush();

                string? line;
                while ((line = proc.StandardOutput.ReadLine()) != null)
                {
                    if (token.IsCancellationRequested) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (!root.TryGetProperty("type", out var typeProp)) continue;
                        string type = typeProp.GetString() ?? string.Empty;

                        if (type == "model_loaded")
                        {
                            int loadMs = root.GetProperty("load_time_ms").GetInt32();
                            string model = root.GetProperty("model").GetString() ?? "Whisper";
                            string effLoad = root.TryGetProperty("effective_device", out var edl) ? (edl.GetString() ?? deviceStr) : deviceStr;
                            bool fbLoad = root.TryGetProperty("fallback", out var fbl) && fbl.ValueKind == JsonValueKind.True;
                            string loadDevDisplay = fbLoad ? $"{effLoad} (repli)" : effLoad;
                            Dispatcher.Invoke(() =>
                            {
                                LblTranscriptionStatus.Text = (_localizer.CurrentLanguage == "en")
                                    ? $"Model {model} loaded on {loadDevDisplay} in {loadMs} ms. Decoding speech..."
                                    : $"Modèle {model} chargé sur {loadDevDisplay} en {loadMs} ms. Décodage et reconnaissance vocale en cours...";
                            });
                        }
                        else if (type == "chunk")
                        {
                            double startTs = root.GetProperty("start_ts").GetDouble();
                            double endTs = root.GetProperty("end_ts").GetDouble();
                            string text = root.GetProperty("text").GetString() ?? string.Empty;
                            string speakerDefault = _localizer.Get("Audio_SpeakerDefault");
                            string speakerId = root.TryGetProperty("speaker_id", out var spkIdProp) ? spkIdProp.GetString() ?? "spk_1" : "spk_1";
                            string speakerName = root.TryGetProperty("speaker_name", out var spkNameProp) ? spkNameProp.GetString() ?? speakerDefault : speakerDefault;
                            if (speakerName == "Intervenant 1" && _localizer.CurrentLanguage != "fr")
                            {
                                speakerName = $"{speakerDefault} 1";
                            }
                            string speakerColor = root.TryGetProperty("speaker_color", out var spkColProp) ? spkColProp.GetString() ?? "#3B82F6" : "#3B82F6";
                            bool isKnown = root.TryGetProperty("is_known", out var knownProp) && knownProp.GetBoolean();
                            double conf = root.TryGetProperty("confidence", out var confProp) ? confProp.GetDouble() : 1.0;
                            string chunkLang = root.TryGetProperty("detected_language", out var chunkLangProp) ? chunkLangProp.GetString() ?? "" : "";
                            string? chunkTranslation = (root.TryGetProperty("translation", out var chunkTrProp) && chunkTrProp.ValueKind == JsonValueKind.String) ? chunkTrProp.GetString() : null;

                            List<double>? embedding = null;
                            if (root.TryGetProperty("embedding", out var embProp) && embProp.ValueKind == JsonValueKind.Array)
                            {
                                embedding = new List<double>();
                                foreach (var el in embProp.EnumerateArray())
                                {
                                    embedding.Add(el.GetDouble());
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                _fullAccumulatedTranscript += $" [{speakerName}] " + text;
                                Dispatcher.Invoke(() =>
                                {
                                    AddTranscriptSegmentCard(startTs, endTs, text, speakerId, speakerName, speakerColor, isKnown, conf, embedding, _currentAudioPath, chunkLang, chunkTranslation);
                                    ScrollerTranscript.ScrollToEnd();
                                });
                            }
                        }
                        else if (type == "complete")
                        {
                            int inferTimeMs = root.GetProperty("infer_time_ms").GetInt32();
                            string speedFactor = root.GetProperty("speed_factor").GetString() ?? string.Empty;
                            double speedFactorRaw = root.TryGetProperty("speed_factor_raw", out var spRaw) ? spRaw.GetDouble() : 0.0;
                            string detectedLang = root.TryGetProperty("detected_language", out var dlp) ? dlp.GetString() ?? "" : "";
                            string effectiveDevice = root.TryGetProperty("effective_device", out var edc) ? (edc.GetString() ?? deviceStr) : deviceStr;
                            bool isFallback = root.TryGetProperty("fallback", out var fbc) && fbc.ValueKind == JsonValueKind.True;
                            string devDisplay = isFallback ? $"{effectiveDevice} (repli)" : effectiveDevice;

                            Dispatcher.Invoke(() =>
                            {
                                if (!string.IsNullOrWhiteSpace(detectedLang))
                                {
                                    BadgeDetectedLanguage.Visibility = Visibility.Visible;
                                    string friendlyLang = FormatLanguageFriendlyName(detectedLang);
                                    TxtDetectedLanguage.Text = string.Format(_localizer.Get("Audio_LangDetected"), friendlyLang);
                                }

                                LblInferTime.Text = $"{inferTimeMs} ms • {devDisplay}";
                                LblSpeedRelative.Text = speedFactor;
                                string speedText = (speedFactorRaw > 0)
                                    ? string.Format(_localizer.Get("Audio_SpeedFactor"), $"{speedFactorRaw}x")
                                    : $"⚡ {speedFactor}";
                                LblSpeedFactor.Text = speedText;
                                BadgeSpeedFactor.Visibility = Visibility.Visible;
                                LblTranscriptionStatus.Text = string.Format(_localizer.Get("Audio_StatusComplete"), devDisplay);
                            });

                            var snap = _telemetryService.GetCurrentSnapshot();
                            string hwName = effectiveDevice.ToUpperInvariant().Contains("NPU")
                                ? $"{SiliconDetector.NpuName} (Whisper Small)"
                                : (effectiveDevice.ToUpperInvariant().Contains("GPU")
                                    ? $"{SiliconDetector.GpuName} (Whisper Small)"
                                    : $"{SiliconDetector.CpuName} (Whisper Small)");

                            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                                demoName: "Audio Studio - Transcription Diarisation",
                                requestedSilicon: deviceStr,
                                effectiveSilicon: effectiveDevice,
                                siliconDeviceName: hwName,
                                latencyMs: inferTimeMs,
                                customPowerWatts: snap.DischargeRateWatts
                            );
                            break;
                        }
                        else if (type == "error")
                        {
                            string errMsg = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() ?? "Erreur d'inférence" : "Erreur d'inférence";
                            Dispatcher.Invoke(() =>
                            {
                                LblTranscriptionStatus.Text = $"⚠️ Erreur Whisper : {errMsg}";
                            });
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                _transcribeProcess = null;
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LblTranscriptionStatus.Text = $"Erreur d'exécution Whisper : {ex.Message}";
                });
            }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    _energyStopwatch.Stop();
                    BtnStartTranscription.IsEnabled = true;
                    BtnLiveTranscribe.IsEnabled = true;
                    BtnImportAudio.IsEnabled = true;
                    BtnSampleMeeting.IsEnabled = true;
                    BtnSampleConversation.IsEnabled = true;
                    PrgTranscription.IsIndeterminate = false;
                    PrgTranscription.Value = 100;
                });
            }
        }, token);
    }

    private void AddTranscriptSegmentCard(
        double startSec,
        double endSec,
        string text,
        string speakerId,
        string speakerName,
        string speakerColor,
        bool isKnown,
        double confidence,
        List<double>? embedding = null,
        string audioFilePath = "",
        string detectedLang = "",
        string? translation = null)
    {
        int startM = (int)startSec / 60;
        int startS = (int)startSec % 60;
        int endM = (int)endSec / 60;
        int endS = (int)endSec % 60;
        string timeTag = $"[{startM:D2}:{startS:D2} - {endM:D2}:{endS:D2}]";

        Color colorObj = Color.FromRgb(0x3B, 0x82, 0xF6);
        try
        {
            if (speakerColor.StartsWith("#"))
            {
                colorObj = (Color)ColorConverter.ConvertFromString(speakerColor);
            }
        }
        catch { }

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x14, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, colorObj.R, colorObj.G, colorObj.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var mainStack = new StackPanel();

        // Header line: Speaker Badge + Rename button + Time tag
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Speaker Badge
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Rename button
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Time Badge

        // Speaker Pill
        var spkBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, colorObj.R, colorObj.G, colorObj.B)),
            BorderBrush = new SolidColorBrush(colorObj),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 2, 8, 2)
        };
        string starPrefix = isKnown ? "⭐ " : "👤 ";
        string tpmTag = isKnown ? " [🔒 TPM]" : "";
        var spkTxt = new TextBlock
        {
            Text = $"{starPrefix}{speakerName}{tpmTag}",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(colorObj)
        };
        spkBadge.Child = spkTxt;
        Grid.SetColumn(spkBadge, 0);

        // Rename Button
        var btnRename = new Button
        {
            Content = isKnown ? _localizer.Get("Audio_BtnStoredTpm") : _localizer.Get("Audio_BtnNameTpm"),
            ToolTip = isKnown 
                ? (_localizer.CurrentLanguage == "en" ? "Voiceprint certified in TPM 2.0. Click to rename speaker." : "Empreinte certifiée dans le TPM 2.0. Cliquez pour renommer la voix.") 
                : (_localizer.CurrentLanguage == "en" ? "Associate a name and seal biometric voiceprint into TPM 2.0" : "Associer un nom et sceller l'empreinte biométrique dans le TPM 2.0"),
            Style = (Style)FindResource("ModernButtonStyle"),
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 10,
            Height = 22,
            Padding = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(btnRename, 2);

        // Time Badge
        var timeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x13, 0x1F, 0x33)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2)
        };
        string langChip = string.IsNullOrWhiteSpace(detectedLang) ? "" : $"{LangFlag(detectedLang)} {detectedLang.ToUpperInvariant()}   ";
        timeBadge.Child = new TextBlock
        {
            Text = langChip + timeTag,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("CopilotCyanBrush")
        };
        Grid.SetColumn(timeBadge, 4);

        headerGrid.Children.Add(spkBadge);
        headerGrid.Children.Add(btnRename);
        headerGrid.Children.Add(timeBadge);
        mainStack.Children.Add(headerGrid);

        // Content Text
        var txtBlock = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };
        mainStack.Children.Add(txtBlock);

        // Traduction (Whisper natif vers l'anglais, ou Phi-4-mini local pour les autres langues)
        string target = _translateTarget;
        bool hasTranslation = !string.IsNullOrWhiteSpace(translation);
        bool needsLlm = !hasTranslation && target != "none" && !string.IsNullOrWhiteSpace(text)
                        && !string.Equals(detectedLang, target, StringComparison.OrdinalIgnoreCase);
        TextBlock? trBlock = null;
        if (hasTranslation || needsLlm)
        {
            string trLang = hasTranslation ? "en" : target;
            string trPrefix = $"🌐 → {LangFlag(trLang)} {trLang.ToUpperInvariant()} : ";
            trBlock = new TextBlock
            {
                Text = hasTranslation ? trPrefix + translation : trPrefix + "⏳ …",
                FontSize = 12,
                FontStyle = FontStyles.Italic,
                Foreground = (Brush)FindResource("CopilotCyanBrush"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 17,
                Margin = new Thickness(0, 6, 0, 0)
            };
            mainStack.Children.Add(trBlock);
        }
        card.Child = mainStack;

        if (target != "none")
        {
            if (hasTranslation)
            {
                _fullTranslatedTranscript += $" [{speakerName}] " + translation;
            }
            else if (!needsLlm)
            {
                _fullTranslatedTranscript += $" [{speakerName}] " + text; // déjà dans la langue cible
            }
            else
            {
                string capturedTarget = target;
                string capturedPrefix = $"🌐 → {LangFlag(capturedTarget)} {capturedTarget.ToUpperInvariant()} : ";
                _ = Task.Run(async () =>
                {
                    string translated;
                    try
                    {
                        translated = await TranslateWithLlmAsync(text, capturedTarget);
                    }
                    catch (Exception ex)
                    {
                        translated = string.Empty;
                        Dispatcher.Invoke(() => trBlock!.Text = capturedPrefix + (_localizer.CurrentLanguage == "en"
                            ? $"⚠️ Translation unavailable ({ex.Message})"
                            : $"⚠️ Traduction indisponible ({ex.Message})"));
                    }
                    if (!string.IsNullOrWhiteSpace(translated))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            trBlock!.Text = capturedPrefix + translated;
                            _fullTranslatedTranscript += $" [{speakerName}] " + translated;
                        });
                    }
                });
            }
        }

        var segData = new TranscriptSegmentData
        {
            StartSec = startSec,
            EndSec = endSec,
            Text = text,
            SpeakerId = speakerId,
            SpeakerName = speakerName,
            SpeakerColor = speakerColor,
            IsKnown = isKnown,
            Confidence = confidence,
            Embedding = embedding,
            AudioFilePath = !string.IsNullOrEmpty(audioFilePath) ? audioFilePath : _currentAudioPath,
            NameTextBlock = spkTxt,
            BadgeBorder = spkBadge,
            MainStack = mainStack,
            BodyTextBlock = txtBlock,
            TranslationTextBlock = trBlock,
            DetectedLang = detectedLang,
            TranslationText = translation ?? string.Empty,
            TranslationTargetLang = hasTranslation ? "en" : (needsLlm ? target : string.Empty)
        };
        _transcriptSegments.Add(segData);

        btnRename.Click += (s, e) =>
        {
            _targetSegmentForRename = segData;
            TxtSpeakerName.Text = (segData.SpeakerName.StartsWith("Intervenant") || segData.SpeakerName.StartsWith("Speaker")) ? "" : segData.SpeakerName;
            LblRenameSnippetInfo.Text = (_localizer.CurrentLanguage == "en")
                ? $"Selected excerpt: [{startM:D2}:{startS:D2} - {endM:D2}:{endS:D2}] • Voice {segData.SpeakerName}"
                : $"Extrait sélectionné : [{startM:D2}:{startS:D2} - {endM:D2}:{endS:D2}] • Voix {segData.SpeakerName}";
            OverlayRenameSpeaker.Visibility = Visibility.Visible;
            TxtSpeakerName.Focus();
            TxtSpeakerName.SelectAll();
        };

        StackTranscriptSegments.Children.Add(card);
    }
    #endregion

    #region Voice Profile Renaming & Persistence
    private void BtnCancelRename_Click(object sender, RoutedEventArgs e)
    {
        OverlayRenameSpeaker.Visibility = Visibility.Collapsed;
    }

    private async void BtnConfirmRename_Click(object sender, RoutedEventArgs e)
    {
        string newName = TxtSpeakerName.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show("Veuillez saisir un prénom ou nom pour cette voix.", "Nom requis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ChkConsentRename?.IsChecked != true)
        {
            MessageBox.Show(
                "Conformité RGPD (Article 9) : Le recueil préalable du consentement explicite du locuteur est obligatoire pour conserver son empreinte vocale biométrique.",
                "Consentement requis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        if (_targetSegmentForRename == null)
        {
            OverlayRenameSpeaker.Visibility = Visibility.Collapsed;
            return;
        }

        string targetSpkId = _targetSegmentForRename.SpeakerId;
        double startSec = _targetSegmentForRename.StartSec;
        double endSec = _targetSegmentForRename.EndSec;
        string audioFile = !string.IsNullOrEmpty(_targetSegmentForRename.AudioFilePath) ? _targetSegmentForRename.AudioFilePath : _currentAudioPath;
        var embedding = _targetSegmentForRename.Embedding;

        // Immediately update all segments with this speakerId in the current view
        foreach (var seg in _transcriptSegments)
        {
            if (seg.SpeakerId == targetSpkId)
            {
                seg.SpeakerName = newName;
                seg.IsKnown = true;
                if (seg.NameTextBlock != null)
                {
                    seg.NameTextBlock.Text = $"⭐ {newName} [🔒 TPM]";
                }
            }
        }

        OverlayRenameSpeaker.Visibility = Visibility.Collapsed;
        LblTranscriptionStatus.Text = $"💾 Chiffrement matériel et scellement TPM 2.0 de la voix de '{newName}' en cours...";

        // Run speaker_service.py to register profile
        string scriptPath = FindRootFile("speaker_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        bool success = false;
        string errorDetails = string.Empty;

        await Task.Run(() =>
        {
            string? tempEmbFile = null;
            try
            {
                ProcessStartInfo psi;
                if (embedding != null && embedding.Count == 192)
                {
                    tempEmbFile = System.IO.Path.GetTempFileName();
                    string json = JsonSerializer.Serialize(embedding);
                    File.WriteAllText(tempEmbFile, json);

                    psi = new ProcessStartInfo
                    {
                        FileName = PythonPathHelper.GetPythonExe(),
                        Arguments = $"\"{scriptPath}\" --action save_embedding --name \"{newName}\" --embedding_file \"{tempEmbFile}\"",
                        WorkingDirectory = workingDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = PythonPathHelper.GetPythonExe(),
                        Arguments = $"\"{scriptPath}\" --action save_from_audio --file \"{audioFile}\" --start {startSec.ToString(System.Globalization.CultureInfo.InvariantCulture)} --end {endSec.ToString(System.Globalization.CultureInfo.InvariantCulture)} --name \"{newName}\"",
                        WorkingDirectory = workingDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8
                    };
                }

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    success = (proc.ExitCode == 0);
                    if (!success)
                    {
                        errorDetails = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                    }
                }
            }
            catch (Exception ex)
            {
                errorDetails = ex.Message;
                System.Diagnostics.Debug.WriteLine($"[Speaker Register Error] {ex.Message}");
            }
            finally
            {
                if (tempEmbFile != null && File.Exists(tempEmbFile))
                {
                    try { File.Delete(tempEmbFile); } catch { }
                }
            }
        });

        if (success)
        {
            LblTranscriptionStatus.Text = $"✅ Empreinte vocale de '{newName}' scellée au TPM 2.0 ! Reconnue automatiquement sur toutes les prochaines réunions.";
        }
        else
        {
            LblTranscriptionStatus.Text = $"⚠️ Erreur lors de la sauvegarde : {errorDetails}";
        }
    }
    #endregion

    #region Voice Profiles Modal & TPM Enrollment
    private async void BtnEnrollVoice_Click(object sender, RoutedEventArgs e)
    {
        OverlayVoiceProfiles.Visibility = Visibility.Visible;
        TxtEnrollName.Focus();
        await RefreshProfilesListAsync();
    }

    private async void BtnEnrollMic_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtEnrollName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Veuillez saisir un prénom ou nom pour cette personne.", "Nom requis", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtEnrollName.Focus();
            return;
        }

        if (ChkConsentEnroll?.IsChecked != true)
        {
            MessageBox.Show(
                "Conformité RGPD (Article 9) : Le recueil préalable du consentement explicite de la personne est obligatoire pour conserver son empreinte vocale biométrique.",
                "Consentement requis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        BtnEnrollMic.IsEnabled = false;
        BtnEnrollFile.IsEnabled = false;
        BtnEnrollMic.Content = (_localizer.CurrentLanguage == "en") ? "🔴 Listening (5s)..." : "🔴 Écoute (5s)...";
        BtnEnrollMic.Background = Brushes.DarkRed;
        LblTranscriptionStatus.Text = (_localizer.CurrentLanguage == "en")
            ? $"🎙️ Microphone recording in progress (5s): ask '{name}' to speak clearly..."
            : $"🎙️ Enregistrement micro en cours (5s) : demandez à '{name}' de parler distinctement...";

        string scriptPath = FindRootFile("speaker_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        bool success = false;
        string err = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action enroll_mic --name \"{name}\" --duration 5.0",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    success = (proc.ExitCode == 0);
                    if (!success) err = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
        });

        BtnEnrollMic.IsEnabled = true;
        BtnEnrollFile.IsEnabled = true;
        BtnEnrollMic.Content = _localizer.Get("Audio_ProfilesBtnMic");
        BtnEnrollMic.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));

        if (success)
        {
            TxtEnrollName.Text = "";
            LblTranscriptionStatus.Text = (_localizer.CurrentLanguage == "en")
                ? $"✅ Voiceprint for '{name}' successfully sealed in TPM 2.0! Automatically recognized."
                : $"✅ Empreinte vocale de '{name}' scellée avec succès dans le TPM 2.0 ! Reconnue automatiquement.";
            await RefreshProfilesListAsync();
        }
        else
        {
            MessageBox.Show($"Erreur lors de l'enregistrement de l'empreinte : {err}", "Erreur Micro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnEnrollFile_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtEnrollName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Veuillez saisir un prénom ou nom pour cette personne.", "Nom requis", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtEnrollName.Focus();
            return;
        }

        if (ChkConsentEnroll?.IsChecked != true)
        {
            MessageBox.Show(
                "Conformité RGPD (Article 9) : Le recueil préalable du consentement explicite de la personne est obligatoire pour conserver son empreinte vocale biométrique.",
                "Consentement requis",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Sélectionner un fichier audio pour {name}",
            Filter = "Fichiers Audio (*.wav;*.mp3;*.flac;*.m4a)|*.wav;*.mp3;*.flac;*.m4a|Tous les fichiers (*.*)|*.*"
        };

        if (ofd.ShowDialog() != true) return;

        BtnEnrollMic.IsEnabled = false;
        BtnEnrollFile.IsEnabled = false;
        LblTranscriptionStatus.Text = $"Extraction de l'empreinte vocale de '{name}' depuis le fichier audio...";

        string scriptPath = FindRootFile("speaker_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        bool success = false;
        string err = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action save_from_audio --file \"{ofd.FileName}\" --name \"{name}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    success = (proc.ExitCode == 0);
                    if (!success) err = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
            }
        });

        BtnEnrollMic.IsEnabled = true;
        BtnEnrollFile.IsEnabled = true;

        if (success)
        {
            TxtEnrollName.Text = "";
            LblTranscriptionStatus.Text = $"✅ Empreinte vocale de '{name}' scellée avec succès dans le TPM 2.0 ! Reconnue automatiquement.";
            await RefreshProfilesListAsync();
        }
        else
        {
            MessageBox.Show($"Erreur lors de l'extraction de l'empreinte : {err}", "Erreur Audio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnManageProfiles_Click(object sender, RoutedEventArgs e)
    {
        OverlayVoiceProfiles.Visibility = Visibility.Visible;
        await RefreshProfilesListAsync();
    }

    private void BtnCloseProfiles_Click(object sender, RoutedEventArgs e)
    {
        OverlayVoiceProfiles.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshProfilesListAsync()
    {
        StackProfilesList.Children.Clear();
        var loadingTxt = new TextBlock
        {
            Text = (_localizer.CurrentLanguage == "en") ? "Securely decrypting TPM 2.0 biometric vault..." : "Déchiffrement sécurisé du coffre-fort TPM 2.0...",
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
            Margin = new Thickness(8)
        };
        StackProfilesList.Children.Add(loadingTxt);

        string scriptPath = FindRootFile("speaker_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action list",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return;
                string jsonOutput = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                using var doc = JsonDocument.Parse(jsonOutput);
                var root = doc.RootElement;
                if (root.TryGetProperty("profiles", out var profArray))
                {
                    var profiles = new List<(string Name, string Color, int Count, string LastUpdated)>();
                    foreach (var p in profArray.EnumerateArray())
                    {
                        string name = p.GetProperty("name").GetString() ?? "";
                        string color = p.TryGetProperty("color", out var cProp) ? cProp.GetString() ?? "#3B82F6" : "#3B82F6";
                        int count = p.TryGetProperty("sample_count", out var cntProp) ? cntProp.GetInt32() : 1;
                        string updated = p.TryGetProperty("last_updated", out var updProp) ? updProp.GetString() ?? "" : "";
                        profiles.Add((name, color, count, updated));
                    }

                    Dispatcher.Invoke(() =>
                    {
                        StackProfilesList.Children.Clear();
                        if (profiles.Count == 0)
                        {
                            StackProfilesList.Children.Add(new TextBlock
                            {
                                Text = _localizer.Get("Audio_ProfilesEmpty"),
                                FontSize = 11.5,
                                Foreground = (Brush)FindResource("TextMutedBrush"),
                                TextWrapping = TextWrapping.Wrap,
                                FontStyle = FontStyles.Italic,
                                Margin = new Thickness(8)
                            });
                            return;
                        }

                        foreach (var prof in profiles)
                        {
                            var row = CreateProfileItemRow(prof.Name, prof.Color, prof.Count, prof.LastUpdated);
                            StackProfilesList.Children.Add(row);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    StackProfilesList.Children.Clear();
                    StackProfilesList.Children.Add(new TextBlock
                    {
                        Text = $"Erreur lors du chargement des profils : {ex.Message}",
                        FontSize = 11,
                        Foreground = (Brush)FindResource("DangerRedBrush"),
                        Margin = new Thickness(8)
                    });
                });
            }
        });
    }

    private UIElement CreateProfileItemRow(string name, string colorHex, int count, string updated)
    {
        Color colorObj = Color.FromRgb(0x3B, 0x82, 0xF6);
        try
        {
            if (colorHex.StartsWith("#"))
            {
                colorObj = (Color)ColorConverter.ConvertFromString(colorHex);
            }
        }
        catch { }

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x17, 0x2A)),
            BorderBrush = (Brush)FindResource("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Avatar
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Delete btn

        // Avatar
        var avatar = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(colorObj),
            VerticalAlignment = VerticalAlignment.Center
        };
        avatar.Child = new TextBlock
        {
            Text = name.Length > 0 ? name.Substring(0, 1).ToUpper() : "?",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(avatar, 0);

        // Info with TPM 2.0 Badge
        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = $"👤 {name}",
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        var tpmBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, 0x10, 0xB9, 0x81)),
            BorderBrush = (Brush)FindResource("NpuGreenBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        tpmBadge.Child = new TextBlock
        {
            Text = (_localizer.CurrentLanguage == "en") ? "🔒 Sealed TPM 2.0" : "🔒 Scellé TPM 2.0",
            FontSize = 9.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("NpuGreenBrush")
        };
        nameRow.Children.Add(tpmBadge);
        infoStack.Children.Add(nameRow);

        infoStack.Children.Add(new TextBlock
        {
            Text = (_localizer.CurrentLanguage == "en")
                ? $"{count} sample(s) • Local hardware master key (GDPR Art. 9)"
                : $"{count} échantillon(s) • Clé matérielle locale inviolable (RGPD Art. 9)",
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(infoStack, 2);

        // Delete button (Right to erasure / RGPD Art 17)
        var delBtn = new Button
        {
            Content = (_localizer.CurrentLanguage == "en") ? "🗑️ Delete" : "🗑️ Supprimer",
            ToolTip = (_localizer.CurrentLanguage == "en")
                ? "Right to erasure (GDPR Art. 17): Permanently delete biometric voiceprint from TPM"
                : "Droit à l'effacement (RGPD Art. 17) : Supprimer définitivement l'empreinte biométrique du coffre TPM",
            Style = (Style)FindResource("ModernButtonStyle"),
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x1E, 0x24)),
            Foreground = (Brush)FindResource("DangerRedBrush"),
            FontSize = 10,
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        delBtn.Click += async (s, e) =>
        {
            if (MessageBox.Show($"Supprimer définitivement l'empreinte vocale de '{name}' du TPM 2.0 (Droit à l'oubli) ?", "Confirmation de suppression", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await DeleteProfileAsync(name);
            }
        };
        Grid.SetColumn(delBtn, 3);

        grid.Children.Add(avatar);
        grid.Children.Add(infoStack);
        grid.Children.Add(delBtn);
        border.Child = grid;

        return border;
    }

    private async Task DeleteProfileAsync(string profileName)
    {
        string scriptPath = FindRootFile("speaker_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action delete --name \"{profileName}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }
            catch { }
        });

        await RefreshProfilesListAsync();
    }
    #endregion

    #region Meeting Summary, Full Translation & Clipboard
    private async void BtnTranslateFullFr_Click(object sender, RoutedEventArgs e)
    {
        await AppendFullTranscriptTranslationAsync("fr");
    }

    private async void BtnTranslateFullEn_Click(object sender, RoutedEventArgs e)
    {
        await AppendFullTranscriptTranslationAsync("en");
    }

    private async Task AppendFullTranscriptTranslationAsync(string targetLang)
    {
        bool isEn = _localizer.CurrentLanguage == "en";
        if (string.IsNullOrWhiteSpace(_fullAccumulatedTranscript) && _transcriptSegments.Count == 0)
        {
            MessageBox.Show(
                isEn ? "Please transcribe an audio recording or speak in the microphone first." : "Veuillez d'abord transcrire un enregistrement ou parler au microphone.",
                isEn ? "No transcript available" : "Aucun texte transcrit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string sourceTranscript = _transcriptSegments.Count > 0
            ? string.Join(Environment.NewLine, _transcriptSegments.Select(seg => $"[{seg.SpeakerName}] : {seg.Text}"))
            : _fullAccumulatedTranscript.Trim();

        if (string.IsNullOrWhiteSpace(sourceTranscript))
        {
            return;
        }

        string targetSilicon = _selectedSilicon.ToString();
        string langLabel = targetLang == "fr"
            ? (isEn ? "🇫🇷 FRENCH" : "🇫🇷 FRANÇAIS")
            : (isEn ? "🇬🇧 ENGLISH" : "🇬🇧 ANGLAIS");
        string langEnglishName = targetLang == "fr" ? "French" : "English";

        var activeBtn = targetLang == "fr" ? BtnTranslateFullFr : BtnTranslateFullEn;
        var originalBtnContent = activeBtn.Content;
        activeBtn.Content = isEn ? "⏳ Translating..." : "⏳ Traduction en cours...";

        BtnTranslateFullFr.IsEnabled = false;
        BtnTranslateFullEn.IsEnabled = false;

        // Remove any previous final translation card at the end of StackTranscriptSegments
        for (int i = StackTranscriptSegments.Children.Count - 1; i >= 0; i--)
        {
            if (StackTranscriptSegments.Children[i] is FrameworkElement fe && fe.Tag as string == "FullTranslationCard")
            {
                StackTranscriptSegments.Children.RemoveAt(i);
            }
        }

        var trCard = new Border
        {
            Tag = "FullTranslationCard",
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x22, 0x38)),
            BorderBrush = (Brush)FindResource("CopilotCyanBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 10, 0, 10)
        };

        var stack = new StackPanel();
        var headerTxt = new TextBlock
        {
            Text = isEn
                ? $"🌐 FULL TRANSCRIPT TRANSLATION → {langLabel} ({targetSilicon})"
                : $"🌐 TRADUCTION INTÉGRALE DU TRANSCRIPT → {langLabel} ({targetSilicon})",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("CopilotCyanBrush"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var bodyTxt = new TextBlock
        {
            Text = isEn ? "⏳ Translating full transcript on NPU with Phi-4-mini..." : "⏳ Traduction intégrale en cours sur NPU avec Phi-4-mini...",
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18
        };
        stack.Children.Add(headerTxt);
        stack.Children.Add(bodyTxt);
        trCard.Child = stack;

        TxtTranscriptPlaceholder.Visibility = Visibility.Collapsed;
        StackTranscriptSegments.Children.Add(trCard);
        ScrollerTranscript.UpdateLayout();
        trCard.BringIntoView();
        ScrollerTranscript.ScrollToEnd();

        // Synchronize CmbTranslate selection in top right
        if (CmbTranslate != null)
        {
            foreach (ComboBoxItem itm in CmbTranslate.Items)
            {
                if (itm.Tag as string == targetLang)
                {
                    CmbTranslate.SelectedItem = itm;
                    break;
                }
            }
        }

        // Visibly translate each individual segment card in place
        _ = TranslateAllSegmentsAsync(targetLang);

        try
        {
            await _foundryClient.EnsureServerReadyAsync();

            string sysPrompt =
                $"You are an expert bilingual interpreter. Translate the following meeting transcript into {langEnglishName}. " +
                "Preserve the [Speaker] labels at the start of each line. Output ONLY the translated transcript, without any introduction or commentary.";

            bool firstChunk = true;
            var selectedModel = await _foundryClient.GetBestModelAsync("phi-4-mini", targetSilicon);

            var result = await _foundryClient.AskStreamingAsync(
                selectedModel,
                sysPrompt,
                sourceTranscript,
                chunk =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (firstChunk)
                        {
                            bodyTxt.Text = string.Empty;
                            firstChunk = false;
                        }
                        bodyTxt.Text += chunk;
                        ScrollerTranscript.ScrollToEnd();
                    }));
                },
                temperature: 0.1,
                maxTokens: 1000);

            // Fallback sync si le streaming n'a rien produit
            if (string.IsNullOrWhiteSpace(bodyTxt.Text) || bodyTxt.Text.StartsWith("⏳"))
            {
                var syncResult = await _foundryClient.AskAsync(selectedModel, sysPrompt, sourceTranscript, temperature: 0.1, maxTokens: 1000);
                bodyTxt.Text = syncResult.Content;
                result = syncResult;
            }

            string finalTranslated = (result.Content ?? bodyTxt.Text).Trim();
            bodyTxt.Text = finalTranslated;
            _fullTranslatedTranscript = finalTranslated;
            _translateTarget = targetLang;

            string effDev = !string.IsNullOrWhiteSpace(result.TargetDevice) ? result.TargetDevice.ToUpperInvariant() : targetSilicon;
            headerTxt.Text = isEn
                ? $"🌐 FULL TRANSCRIPT TRANSLATION → {langLabel} ({effDev} • {result.TokensPerSecond:F1} tok/s)"
                : $"🌐 TRADUCTION INTÉGRALE DU TRANSCRIPT → {langLabel} ({effDev} • {result.TokensPerSecond:F1} tok/s)";

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: $"Audio Studio - Traduction Transcript ({targetLang.ToUpperInvariant()})",
                requestedSilicon: targetSilicon,
                effectiveSilicon: effDev,
                siliconDeviceName: result.ModelId,
                latencyMs: (int)result.LatencyMilliseconds,
                tokensPerSecond: result.TokensPerSecond
            );
        }
        catch (Exception ex)
        {
            bodyTxt.Text = isEn
                ? $"⚠️ Translation error: {ex.Message}"
                : $"⚠️ Erreur de traduction : {ex.Message}";
        }
        finally
        {
            activeBtn.Content = originalBtnContent;
            BtnTranslateFullFr.IsEnabled = true;
            BtnTranslateFullEn.IsEnabled = true;
            ScrollerTranscript.UpdateLayout();
            trCard.BringIntoView();
            ScrollerTranscript.ScrollToEnd();
        }
    }

    private async void BtnSummarizeMeeting_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = _localizer.CurrentLanguage == "en";
        if (string.IsNullOrWhiteSpace(_fullAccumulatedTranscript))
        {
            MessageBox.Show(
                isEn ? "Please transcribe an audio recording first." : "Veuillez d'abord transcrire un enregistrement audio.",
                isEn ? "No transcript available" : "Aucun texte transcrit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string targetSilicon = _selectedSilicon.ToString();
        BtnSummarizeMeeting.IsEnabled = false;
        BorderSummary.Visibility = Visibility.Visible;
        TxtMeetingSummary.Text = isEn
            ? $"⏳ Generating executive summary with Phi-4-mini on {targetSilicon}..."
            : $"⏳ Synthèse exécutive en cours avec Phi-4-mini sur {targetSilicon}...";

        try
        {
            string systemPrompt = isEn
                ? "You are an elite executive AI assistant. You faithfully and rigorously summarize corporate meetings."
                : "Tu es un assistant exécutif d'élite. Tu synthétises fidèlement et rigoureusement les réunions.";

            string prompt = isEn
                ? $"Here is the complete transcript of a corporate meeting with identified speakers:\n\n" +
                  $"{_fullAccumulatedTranscript.Trim()}\n\n" +
                  $"As an executive AI assistant, write a structured executive summary in English in 3 points:\n" +
                  $"1. Main topic and context\n" +
                  $"2. Key points and decisions made by each participant\n" +
                  $"3. Action items and next steps"
                : $"Voici la transcription complète d'une réunion d'entreprise avec les locuteurs identifiés :\n\n" +
                  $"{_fullAccumulatedTranscript.Trim()}\n\n" +
                  $"En tant qu'assistant de direction IA, rédige une synthèse exécutive structurée en 3 points en français :\n" +
                  $"1. Sujet principal et contexte\n" +
                  $"2. Points clés et décisions prises par chaque intervenant\n" +
                  $"3. Plan d'actions et prochaines étapes";

            var selectedModel = await _foundryClient.GetBestModelAsync("phi-4-mini", targetSilicon);
            var result = await _foundryClient.AskAsync(
                selectedModel,
                systemPrompt,
                prompt
            );

            TxtMeetingSummary.Text = result.Content;

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Audio Studio - Synthèse Réunion",
                requestedSilicon: targetSilicon,
                effectiveSilicon: result.TargetDevice,
                siliconDeviceName: result.ModelId,
                latencyMs: (int)result.LatencyMilliseconds,
                tokensPerSecond: result.TokensPerSecond
            );
        }
        catch (Exception ex)
        {
            TxtMeetingSummary.Text = isEn
                ? $"Error while generating summary: {ex.Message}"
                : $"Erreur lors de la génération de la synthèse : {ex.Message}";
        }
        finally
        {
            BtnSummarizeMeeting.IsEnabled = true;
        }
    }

    private void BtnCopyText_Click(object sender, RoutedEventArgs e)
    {
        bool isEn = _localizer.CurrentLanguage == "en";
        if (!string.IsNullOrWhiteSpace(_fullAccumulatedTranscript))
        {
            string clip = _fullAccumulatedTranscript.Trim();
            if (_translateTarget != "none" && !string.IsNullOrWhiteSpace(_fullTranslatedTranscript))
            {
                string head = isEn ? $"--- Translation ({_translateTarget.ToUpperInvariant()}) ---" : $"--- Traduction ({_translateTarget.ToUpperInvariant()}) ---";
                clip += "\n\n" + head + "\n" + _fullTranslatedTranscript.Trim();
            }
            Clipboard.SetText(clip);
            MessageBox.Show(
                isEn ? "Full transcript copied to clipboard!" : "Transcription complète copiée dans le presse-papier !",
                isEn ? "Success" : "Succès",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(
                isEn ? "Nothing to copy. Transcript is empty." : "Rien à copier. La transcription est vide.",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    #endregion

    #region Playback & UI Buttons
    private void BtnImportAudio_Click(object sender, RoutedEventArgs e)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Fichiers Audio (*.wav;*.mp3;*.m4a;*.flac;*.ogg)|*.wav;*.mp3;*.m4a;*.flac;*.ogg|Tous les fichiers (*.*)|*.*",
            Title = "Sélectionner un fichier audio pour transcription Whisper"
        };

        if (openDialog.ShowDialog() == true)
        {
            StopAudio();
            string fileName = System.IO.Path.GetFileName(openDialog.FileName);
            LoadAudioFile(openDialog.FileName, $"{fileName}");
            StackTranscriptSegments.Children.Clear();
            _transcriptSegments.Clear();
            TxtTranscriptPlaceholder.Visibility = Visibility.Visible;
            TxtTranscriptPlaceholder.Text = (_localizer.CurrentLanguage == "en")
                ? "Audio file loaded. Click '⚡ Transcribe File' to start."
                : "Fichier audio importé. Cliquez sur '⚡ Transcrire le Fichier' pour démarrer.";
            BorderSummary.Visibility = Visibility.Collapsed;
            LblTranscriptionStatus.Text = _localizer.Get("Audio_StatusReady");
            BadgeSpeedFactor.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnSampleMeeting_Click(object sender, RoutedEventArgs e)
    {
        StopAudio();
        string samplePath = FindRootFile(System.IO.Path.Combine("Data", "Audio", "reunion_codir.wav"));

        LoadAudioFile(samplePath, (_localizer.CurrentLanguage == "en") ? "reunion_codir.wav (22s • Whisper Small NPU)" : "reunion_codir.wav (22s • Whisper Small NPU)");
        StackTranscriptSegments.Children.Clear();
        _transcriptSegments.Clear();
        TxtTranscriptPlaceholder.Visibility = Visibility.Visible;
        TxtTranscriptPlaceholder.Text = (_localizer.CurrentLanguage == "en")
            ? "CODIR Meeting loaded. Click '⚡ Transcribe File' to start."
            : "Réunion CODIR chargée. Cliquez sur '⚡ Transcrire le Fichier' pour démarrer.";
        BorderSummary.Visibility = Visibility.Collapsed;
        LblTranscriptionStatus.Text = _localizer.Get("Audio_StatusReady");
        BadgeSpeedFactor.Visibility = Visibility.Collapsed;
    }

    private void BtnSampleConversation_Click(object sender, RoutedEventArgs e)
    {
        StopAudio();
        string samplePath = FindRootFile(System.IO.Path.Combine("Data", "Audio", "conversation_demo.wav"));

        LoadAudioFile(samplePath, (_localizer.CurrentLanguage == "en") ? "conversation_demo.wav (15.2s • 2-Voice Dialogue)" : "conversation_demo.wav (15.2s • Dialogue 2 Voix)");
        StackTranscriptSegments.Children.Clear();
        _transcriptSegments.Clear();
        TxtTranscriptPlaceholder.Visibility = Visibility.Visible;
        TxtTranscriptPlaceholder.Text = (_localizer.CurrentLanguage == "en")
            ? "2-Voice Dialogue loaded. Click '⚡ Transcribe File' to test diarization."
            : "Dialogue 2 voix chargé. Cliquez sur '⚡ Transcrire le Fichier' pour tester la diarisation.";
        BorderSummary.Visibility = Visibility.Collapsed;
        LblTranscriptionStatus.Text = _localizer.Get("Audio_StatusReady");
        BadgeSpeedFactor.Visibility = Visibility.Collapsed;
    }

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying)
        {
            PauseAudio();
        }
        else
        {
            PlayAudio();
        }
    }

    private void PlayAudio()
    {
        if (File.Exists(_currentAudioPath))
        {
            _isPlaying = true;
            _mediaPlayer.Play();
            BtnPlayPause.Content = _localizer.Get("Audio_BtnPause");
            BtnPlayPause.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
        }
    }

    private void PauseAudio()
    {
        _isPlaying = false;
        _mediaPlayer.Pause();
        BtnPlayPause.Content = _localizer.Get("Audio_BtnPlay");
        BtnPlayPause.Background = (Brush)FindResource("CopilotBlueBrush");
    }

    private void StopAudio()
    {
        _isPlaying = false;
        _mediaPlayer.Stop();
        _mediaPlayer.Position = TimeSpan.Zero;
        SliderTimeline.Value = 0;
        LblCurrentTime.Text = "00:00";
        BtnPlayPause.Content = _localizer.Get("Audio_BtnPlay");
        BtnPlayPause.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e) => StopAudio();

    private void BtnMuteSound_Click(object sender, RoutedEventArgs e)
    {
        _isMasterMuted = !_isMasterMuted;
        _mediaPlayer.Volume = _isMasterMuted ? 0.0 : _masterVolume;
        BtnMuteSound.Content = _isMasterMuted ? _localizer.Get("Audio_BtnMuted") : _localizer.Get("Audio_BtnMute");
        BtnMuteSound.Background = _isMasterMuted ? (Brush)FindResource("DangerRedBrush") : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
    }

    private void SliderMasterVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _masterVolume = SliderMasterVolume?.Value ?? 0.8;
        if (!_isMasterMuted) _mediaPlayer.Volume = _masterVolume;
    }

    private void SliderTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isPlaying && _mediaPlayer.Source != null)
        {
            _mediaPlayer.Position = TimeSpan.FromSeconds(SliderTimeline.Value);
        }
    }

    private void BtnNpuMode_Click(object sender, RoutedEventArgs e) => SetSiliconMode(SiliconMode.NPU);
    private void BtnGpuMode_Click(object sender, RoutedEventArgs e) => SetSiliconMode(SiliconMode.GPU);
    #endregion

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        bool isEn = _localizer.CurrentLanguage == "en";

        // Sync Language Combo Auto text
        if (CmbItemAuto != null)
        {
            CmbItemAuto.Content = _localizer.Get("Audio_LangAuto");
        }

        // Header & badges
        if (LblAudioTitle != null) LblAudioTitle.Text = _localizer.Get("Audio_Title");
        if (LblAudioSub != null) LblAudioSub.Text = _localizer.Get("Audio_Subtitle");
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = isEn
                ? "Model: OpenAI Whisper (Tiny/Base FP16 OpenVINO)"
                : "Modèle : OpenAI Whisper (Tiny/Base FP16 OpenVINO)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = isEn
                    ? "OpenAI Whisper FP16 speech recognition and transcription model compiled for Copilot+ NPU/GPU."
                    : "Modèle de reconnaissance vocale et transcription automatique OpenAI Whisper FP16 compilé pour NPU/GPU Copilot+ PC.";
            }
        }
        if (LblTpmBadge != null)
        {
            LblTpmBadge.Text = _localizer.Get("Audio_TpmBadge");
            LblTpmBadge.ToolTip = _localizer.Get("Audio_TpmTooltip");
        }
        if (LblAudioLanguageLabel != null) LblAudioLanguageLabel.Text = _localizer.Get("Audio_LangLabel");
        if (LblAudioTranslateLabel != null) LblAudioTranslateLabel.Text = (_localizer.CurrentLanguage == "en") ? "Translate:" : "Traduire :";
        if (CmbTrItemNone != null) CmbTrItemNone.Content = (_localizer.CurrentLanguage == "en") ? "🚫 None" : "🚫 Aucune";
        if (CmbTranslate != null) CmbTranslate.ToolTip = (_localizer.CurrentLanguage == "en")
            ? "Translate each sentence into the chosen language (English: native Whisper; other languages: local Phi-4-mini)"
            : "Traduit chaque phrase dans la langue choisie (anglais : Whisper natif ; autres langues : Phi-4-mini local)";

        // Top bar buttons
        if (BtnLiveTranscribe != null && !_isLiveActive)
        {
            BtnLiveTranscribe.Content = _localizer.Get("Audio_BtnOneClickLive");
            BtnLiveTranscribe.ToolTip = _localizer.Get("Audio_BtnOneClickLiveTooltip");
        }
        if (BtnImportAudio != null) BtnImportAudio.Content = _localizer.Get("Audio_BtnImportAudio");
        if (BtnSampleMeeting != null) BtnSampleMeeting.Content = _localizer.Get("Audio_BtnSampleMeeting");
        if (BtnSampleConversation != null) BtnSampleConversation.Content = _localizer.Get("Audio_BtnSampleConversation");
        if (BtnEnrollVoice != null) BtnEnrollVoice.Content = _localizer.Get("Audio_BtnEnrollVoice");
        if (BtnManageProfiles != null) BtnManageProfiles.Content = _localizer.Get("Audio_BtnManageProfiles");

        if (LblLoadedTrackInfo != null && !string.IsNullOrEmpty(_currentAudioPath))
        {
            string fileName = System.IO.Path.GetFileName(_currentAudioPath);
            LblLoadedTrackInfo.Text = string.Format(_localizer.Get("Audio_ActiveFileLabel"), $"{fileName} • Whisper Small ({(_selectedSilicon == SiliconMode.NPU ? "NPU" : "GPU")})");
        }

        // Playback controls
        if (BtnPlayPause != null)
        {
            BtnPlayPause.Content = _isPlaying ? _localizer.Get("Audio_BtnPause") : _localizer.Get("Audio_BtnPlay");
        }
        if (BtnStop != null) BtnStop.Content = _localizer.Get("Audio_BtnStop");
        if (BtnMuteSound != null)
        {
            BtnMuteSound.Content = _isMasterMuted ? _localizer.Get("Audio_BtnMuted") : _localizer.Get("Audio_BtnMute");
        }
        if (BtnGpuMode != null) BtnGpuMode.Content = _localizer.Get("Audio_BtnGpuMode");
        if (BtnNpuMode != null) BtnNpuMode.Content = _localizer.Get("Audio_BtnNpuMode");
        if (BtnStartTranscription != null)
        {
            BtnStartTranscription.Content = _localizer.Get("Audio_BtnStartTranscription");
            BtnStartTranscription.ToolTip = _localizer.Get("Audio_BtnTranscribeFileTooltip");
        }
        if (LblJoulesSub != null) LblJoulesSub.Text = _localizer.Get("Audio_EnergyJoulesSub");

        // Transcript section
        if (LblLiveHeader != null) LblLiveHeader.Text = _localizer.Get("Audio_HeaderTranscript");
        if (TxtLiveIndicator != null && !_isLiveActive)
        {
            TxtLiveIndicator.Text = string.Format(_localizer.Get("Audio_LiveMicIndicator"), _selectedSilicon);
        }
        if (LblTranscriptionStatus != null && string.IsNullOrWhiteSpace(_fullAccumulatedTranscript))
        {
            LblTranscriptionStatus.Text = _localizer.Get("Audio_StatusReady");
        }
        if (TxtTranscriptPlaceholder != null && _transcriptSegments.Count == 0)
        {
            TxtTranscriptPlaceholder.Text = _localizer.Get("Audio_Placeholder");
        }
        if (BtnCopyText != null) BtnCopyText.Content = _localizer.Get("Audio_BtnCopyText");
        if (BtnSummarizeMeeting != null) BtnSummarizeMeeting.Content = string.Format(_localizer.Get("Audio_BtnSummarize"), _selectedSilicon);
        if (LblSummaryTitle != null) LblSummaryTitle.Text = string.Format(_localizer.Get("Audio_SummaryHeader"), _selectedSilicon);

        // Telemetry section
        if (TxtAudioStatus != null)
        {
            TxtAudioStatus.Text = (_selectedSilicon == SiliconMode.NPU)
                ? _localizer.Get("Audio_StatusNpu")
                : _localizer.Get("Audio_StatusGpu");
        }
        if (LblLiveEnergyHeader != null) LblLiveEnergyHeader.Text = isEn ? "⚡ LIVE ENERGY COUNTER" : "⚡ COMPTEUR ÉNERGÉTIQUE EN DIRECT";
        if (LblLiveTelemetrySub != null) LblLiveTelemetrySub.Text = isEn ? "100% Windows Telemetry" : "100% Télémétrie Windows";
        if (LblInstantWattsTitle != null) LblInstantWattsTitle.Text = isEn ? "INSTANT POWER" : "PUISSANCE INSTANTANÉE";
        if (LblJoulesTitle != null) LblJoulesTitle.Text = _localizer.Get("Audio_KpiEnergy");
        if (LblPowerSub != null) LblPowerSub.Text = isEn ? "Ultra-low power NPU" : "NPU ultra-sobre";
        if (LblChronoTitle != null) LblChronoTitle.Text = isEn ? "⚡ WHISPER INFERENCE TIMING" : "⚡ CHRONOMÉTRAGE DE L'INFÉRENCE WHISPER";
        if (LblInferTimeLabel != null) LblInferTimeLabel.Text = isEn ? "AI compute duration:" : "Temps de calcul IA :";
        if (LblSpeedRelativeLabel != null) LblSpeedRelativeLabel.Text = isEn ? "Relative acceleration:" : "Accélération relative :";
        if (LblModelLoadedLabel != null) LblModelLoadedLabel.Text = isEn ? "Loaded AI model:" : "Modèle IA chargé :";

        // DSI Scorecard
        if (LblDsiScorecardTitle != null) LblDsiScorecardTitle.Text = isEn
            ? $"DSI COMPARATIVE MATRIX: WHY RUN SPEECH AI ON THE DEDICATED NPU ({SiliconDetector.EstimatedNpuTops}+ TOPS)?"
            : $"BILAN COMPARATIF DSI : POURQUOI EXÉCUTER L'IA VOCALE SUR LE NPU DÉDIÉ ({SiliconDetector.EstimatedNpuTops}+ TOPS) ?";
        if (LblDsiCol1 != null) LblDsiCol1.Text = isEn ? "ENTERPRISE CRITERION" : "CRITÈRE ENTREPRISE";
        if (LblDsiCol2 != null) LblDsiCol2.Text = isEn ? $"{SiliconDetector.GpuName} (Shared)" : $"{SiliconDetector.GpuName} (Partagé)";
        if (LblDsiCol3 != null) LblDsiCol3.Text = isEn ? $"{SiliconDetector.NpuName} (Dedicated)" : $"{SiliconDetector.NpuName} (Dédié)";

        if (LblDsiRow1Crit != null) LblDsiRow1Crit.Text = isEn ? "Transcription speed (Whisper Small)" : "Vitesse de transcription (Whisper Small)";
        if (LblDsiRow1Gpu != null) LblDsiRow1Gpu.Text = isEn ? "~ 15 - 20x real-time (~ 700 ms)" : "~ 15 - 20x temps réel (~ 700 ms)";
        if (LblDsiRow1Npu != null) LblDsiRow1Npu.Text = isEn ? "~ 25 - 30x real-time (~ 500 ms • 100% Offline)" : "~ 25 - 30x temps réel (~ 500 ms • 100% Hors-ligne)";

        if (LblDsiRow2Crit != null) LblDsiRow2Crit.Text = isEn ? "Average absorbed power" : "Puissance moyenne absorbée";
        if (LblDsiRow2Gpu != null) LblDsiRow2Gpu.Text = isEn ? "28 - 35 Watts (GPU heat & fan noise)" : "28 - 35 Watts (Surchauffe GPU & ventilation)";
        if (LblDsiRow2Npu != null) LblDsiRow2Npu.Text = isEn ? "5 - 6 Watts (Ultra-low power • Cool chassis)" : "5 - 6 Watts (Régime ultra-sobre • Châssis froid)";

        if (LblDsiRow3Crit != null) LblDsiRow3Crit.Text = isEn ? "Availability for other apps" : "Disponibilité pour les autres apps";
        if (LblDsiRow3Gpu != null) LblDsiRow3Gpu.Text = isEn ? "GPU monopolized by audio (Screen lag risk)" : "GPU monopolisé par l'audio (Risque de saccades écran)";
        if (LblDsiRow3Npu != null) LblDsiRow3Npu.Text = isEn ? "GPU 100% free for UI, Teams and Windows" : "GPU 100% libre pour l'affichage, Teams et Windows";

        if (LblDsiRow4Crit != null) LblDsiRow4Crit.Text = isEn ? "Biometric security & GDPR (Art. 9)" : "Sécurité biométrique & RGPD (Art. 9)";
        if (LblDsiRow4Gpu != null) LblDsiRow4Gpu.Text = isEn ? "Cloud / Unencrypted files (Exfiltration & theft risk)" : "Cloud / Fichiers non-chiffrés (Risque d'exfiltration & vol)";
        if (LblDsiRow4Npu != null) LblDsiRow4Npu.Text = isEn ? "100% Sealed into PC TPM 2.0 • Hardware master key" : "100% Scellé au TPM 2.0 du PC • Clé matérielle inviolable";

        if (LblDsiTakeaway != null) LblDsiTakeaway.Text = isEn
            ? $"DSI EXECUTIVE SUMMARY: Meeting and client call transcription is enterprise AI use case #1. With dedicated NPU ({SiliconDetector.NpuName}) and hardware TPM 2.0 (Microsoft Pluton) security chip on Copilot+ PCs, transcription executes locally at over 25x real-time speed consuming just 5.5W. Biometric voiceprints are hardware-sealed, ensuring native GDPR (Art. 9) compliance with zero cloud exfiltration."
            : $"SYNTHÈSE DSI : La transcription de réunions et d'appels clients est le cas d'usage n°1 de l'IA vocale en entreprise. Avec le NPU dédié ({SiliconDetector.NpuName}) et la puce de sécurité matérielle TPM 2.0 (Microsoft Pluton) du Copilot+ PC, la transcription s'exécute localement à plus de 25x la vitesse réelle en consommant seulement 5.5W. Les empreintes vocales biométriques sont scellées au matériel, garantissant une conformité native RGPD (Art. 9) sans aucune fuite cloud.";

        // Modals
        if (LblModalRenameTitle != null) LblModalRenameTitle.Text = _localizer.Get("Audio_RenameTitle");
        if (LblModalRenameDesc != null) LblModalRenameDesc.Text = _localizer.Get("Audio_RenameExplainer");
        if (LblModalRenameInputLabel != null) LblModalRenameInputLabel.Text = _localizer.Get("Audio_RenameInputLabel");
        if (LblModalRenameRgpdTitle != null) LblModalRenameRgpdTitle.Text = isEn ? "⚖️ GDPR COMPLIANCE (ARTICLE 9 - BIOMETRIC DATA):" : "⚖️ CONFORMITÉ RGPD (ARTICLE 9 - DONNÉES BIOMÉTRIQUES) :";
        if (LblModalRenameRgpdDesc != null) LblModalRenameRgpdDesc.Text = isEn
            ? "A voiceprint is sensitive biometric data. Prior consent must be obtained. Data remains hardware-sealed in this PC's TPM and can be deleted at any time."
            : "L'empreinte vocale est une donnée biométrique. Le consentement préalable de la personne doit être recueilli. Les données restent scellées au TPM de ce PC et sont supprimables à tout moment.";
        if (ChkConsentRename != null) ChkConsentRename.Content = _localizer.Get("Audio_RenameConsent");
        if (BtnCancelRename != null) BtnCancelRename.Content = _localizer.Get("Audio_RenameBtnCancel");
        if (BtnConfirmRename != null) BtnConfirmRename.Content = _localizer.Get("Audio_RenameBtnSave");

        if (LblModalProfilesTitle != null) LblModalProfilesTitle.Text = _localizer.Get("Audio_ProfilesTitle");
        if (LblModalProfilesBadge != null) LblModalProfilesBadge.Text = isEn ? "● HARDWARE SEALED" : "● MATÉRIEL SCELLÉ";
        if (LblModalProfilesDesc != null) LblModalProfilesDesc.Text = isEn
            ? "Biometric database hardware-encrypted by this PC's unique TPM 2.0 / Pluton key. Zero data leaves the machine."
            : "Base biométrique chiffrée par la clé matérielle unique de ce PC (Microsoft Pluton / PTT). Aucune donnée ne quitte la machine.";
        if (LblModalProfilesEnrollTitle != null) LblModalProfilesEnrollTitle.Text = isEn ? "➕ ENROLL A NEW VOICE (GDPR CONSENT)" : "➕ ENRÔLER UNE NOUVELLE VOIX (ACCORD RGPD)";
        if (LblModalProfilesEnrollBadge != null) LblModalProfilesEnrollBadge.Text = isEn ? "🔒 Sealed TPM 2.0" : "🔒 Scellé TPM 2.0";
        if (BtnEnrollMic != null) BtnEnrollMic.Content = _localizer.Get("Audio_ProfilesBtnMic");
        if (BtnEnrollFile != null) BtnEnrollFile.Content = _localizer.Get("Audio_ProfilesBtnFile");
        if (ChkConsentEnroll != null) ChkConsentEnroll.Content = isEn
            ? "Explicit consent obtained: person agrees to local hardware storage of their biometric voiceprint (GDPR Art. 9)"
            : "Accord explicite recueilli : la personne consent à la conservation locale de son empreinte (RGPD Art. 9)";
        if (TxtNoProfiles != null) TxtNoProfiles.Text = _localizer.Get("Audio_ProfilesEmpty");
        if (BtnCloseProfiles != null) BtnCloseProfiles.Content = _localizer.Get("Audio_ProfilesBtnClose");
        if (TxtDocHelp != null) TxtDocHelp.Text = isEn ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = isEn 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
    }

    public void Dispose()
    {
        if (_liveTimer != null)
        {
            _liveTimer.Stop();
            _liveTimer = null;
        }

        if (_liveProcess != null)
        {
            try
            {
                if (!_liveProcess.HasExited)
                {
                    _liveProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                try { _liveProcess.Dispose(); } catch { }
                _liveProcess = null;
            }
        }

        if (_transcribeCts != null)
        {
            try
            {
                _transcribeCts.Cancel();
                _transcribeCts.Dispose();
            }
            catch { }
            _transcribeCts = null;
        }

        if (_transcribeProcess != null)
        {
            try
            {
                if (!_transcribeProcess.HasExited)
                {
                    _transcribeProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                try { _transcribeProcess.Dispose(); } catch { }
                _transcribeProcess = null;
            }
        }

        StopWhisperDaemon();
        _playbackTimer.Stop();
        _telemetryTimer.Stop();
        _mediaPlayer.Close();
        _telemetryService.Dispose();
    }

    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_audio", _localizer?.CurrentLanguage);
    }
}
