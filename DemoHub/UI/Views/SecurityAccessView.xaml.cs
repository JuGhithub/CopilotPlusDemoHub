using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class SecurityAccessView : UserControl, IDisposable
{
    private readonly Func<string>? _getEffectiveDevice;
    private readonly Action<string>? _setSelectedDevice;
    private LocalizationService? _localizer;
    private bool _isSyncingDevice = false;

    // Vision scanner state
    private string _currentFeedPath = string.Empty;
    private Process? _activeProcess;
    private Process? _streamProcess;
    private bool _isStreaming = false;
    private int _consecutiveGrantedFrames = 0;

    // Minesweeper constants & state
    private const int ROWS = 9;
    private const int COLS = 9;
    private const int TOTAL_MINES = 10;

    private readonly bool[,] _mines = new bool[ROWS, COLS];
    private readonly bool[,] _revealed = new bool[ROWS, COLS];
    private readonly bool[,] _flagged = new bool[ROWS, COLS];
    private readonly int[,] _adjacent = new int[ROWS, COLS];
    private readonly Button[,] _cellButtons = new Button[ROWS, COLS];

    private readonly DispatcherTimer _gameTimer;
    private int _elapsedSeconds = 0;
    private bool _isGameActive = false;
    private bool _isFirstClick = true;

    public SecurityAccessView() : this(null, null) { }

    public SecurityAccessView(Func<string>? getEffectiveDevice, Action<string>? setSelectedDevice)
    {
        InitializeComponent();
        _getEffectiveDevice = getEffectiveDevice;
        _setSelectedDevice = setSelectedDevice;

        _gameTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gameTimer.Tick += GameTimer_Tick;

        InitMinesweeperBoard();
        RefreshDeviceUI();
    }

    #region Silicon Device Sync
    public void RefreshDeviceUI()
    {
        if (_isSyncingDevice) return;
        _isSyncingDevice = true;
        try
        {
            string eff = _getEffectiveDevice?.Invoke() ?? "GPU";
            if (eff.Equals("CPU", StringComparison.OrdinalIgnoreCase))
            {
                if (RadDeviceCpu != null && RadDeviceCpu.IsChecked != true) RadDeviceCpu.IsChecked = true;
            }
            else
            {
                if (RadDeviceGpu != null && RadDeviceGpu.IsChecked != true) RadDeviceGpu.IsChecked = true;
            }
        }
        finally
        {
            _isSyncingDevice = false;
        }
    }

    private void Device_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingDevice) return;
        string dev = GetSelectedDevice();
        _setSelectedDevice?.Invoke(dev);
    }

    private string GetSelectedDevice()
    {
        if (RadDeviceCpu?.IsChecked == true) return "CPU";
        return "GPU";
    }

    private void Engine_Checked(object sender, RoutedEventArgs e)
    {
        if (_isStreaming)
        {
            StartLiveWebcamStream();
        }
        else if (!string.IsNullOrEmpty(_currentFeedPath))
        {
            _ = RunVerificationAsync(_currentFeedPath, false);
        }
    }

    private string GetSelectedEngine()
    {
        if (RadEngineOlive?.IsChecked == true) return "onnx_olive";
        if (RadEngineONNX?.IsChecked == true) return "onnx";
        return "openvino";
    }
    #endregion

    #region Vision Scanner Execution
    private void BtnScanWebcam_Click(object sender, RoutedEventArgs e)
    {
        if (_isStreaming)
        {
            StopLiveWebcamStream();
        }
        else
        {
            StartLiveWebcamStream();
        }
    }

    private void StartLiveWebcamStream()
    {
        StopLiveWebcamStream();

        string device = GetSelectedDevice();
        string engine = GetSelectedEngine();
        string scriptPath = FindRootFile("security_access_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        _isStreaming = true;
        _consecutiveGrantedFrames = 0;

        bool isEn = _localizer?.CurrentLanguage == "en";
        BtnScanWebcam.Content = isEn ? "⏹️ Stop Webcam" : "⏹️ Arrêter la Webcam";
        BtnScanWebcam.Background = new SolidColorBrush(Color.FromRgb(185, 28, 28)); // Crimson Red
        BadgeLiveStream.Visibility = Visibility.Visible;
        PanelFeedPlaceholder.Visibility = Visibility.Collapsed;
        OverlayScanning.Visibility = Visibility.Collapsed;
        LblFeedSource.Text = isEn ? "Connecting live webcam..." : "Connexion webcam en direct...";

        string args = $"\"{scriptPath}\" --stream --conf 0.20 --device {device} --engine {engine}";

        var psi = new ProcessStartInfo
        {
            FileName = PythonPathHelper.GetPythonExe(),
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        try
        {
            _streamProcess = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _isStreaming = false;
            ResetWebcamButton();
            MessageBox.Show($"Impossible de démarrer le flux webcam : {ex.Message}", "Erreur Webcam", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_streamProcess == null)
        {
            _isStreaming = false;
            ResetWebcamButton();
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                string? line;
                while (_isStreaming && _streamProcess != null && !_streamProcess.HasExited && (line = await _streamProcess.StandardOutput.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith('{')) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("type", out var typeProp))
                        {
                            string type = typeProp.GetString() ?? "";
                            if (type == "live_frame")
                            {
                                bool cup = root.GetProperty("cup_detected").GetBoolean();
                                double cupConf = root.GetProperty("cup_conf").GetDouble();
                                bool glasses = root.GetProperty("glasses_detected").GetBoolean();
                                double glassesConf = root.GetProperty("glasses_conf").GetDouble();
                                bool headset = root.TryGetProperty("headset_detected", out var hp) && hp.GetBoolean();
                                double headsetConf = root.TryGetProperty("headset_conf", out var hcp) ? hcp.GetDouble() : 0.0;
                                bool granted = root.GetProperty("access_granted").GetBoolean();
                                int inferMs = root.GetProperty("infer_time_ms").GetInt32();
                                double fps = root.GetProperty("fps").GetDouble();
                                string b64 = root.GetProperty("frame_b64").GetString() ?? "";
                                string engineName = root.TryGetProperty("engine", out var ep) ? (ep.GetString() ?? "") : "";

                                Dispatcher.Invoke(() =>
                                {
                                    if (!_isStreaming) return;
                                    ProcessLiveFrame(b64, cup, cupConf, glasses, glassesConf, headset, headsetConf, granted, inferMs, fps, device, engineName);
                                });
                            }
                            else if (type == "error")
                            {
                                string msg = root.GetProperty("message").GetString() ?? "Erreur webcam";
                                Dispatcher.Invoke(() =>
                                {
                                    StopLiveWebcamStream();
                                    MessageBox.Show(msg, "Erreur Webcam", MessageBoxButton.OK, MessageBoxImage.Warning);
                                });
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            finally
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isStreaming)
                    {
                        StopLiveWebcamStream();
                    }
                });
            }
        });
    }

    private void ProcessLiveFrame(string b64, bool cup, double cupConf, bool glasses, double glassesConf, bool headset, double headsetConf, bool granted, int inferMs, double fps, string device, string engine = "")
    {
        if (string.IsNullOrEmpty(b64)) return;

        try
        {
            byte[] bytes = Convert.FromBase64String(b64);
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImgScannerFeed.Source = bmp;
        }
        catch { }

        // Badges update
        UpdateComplianceBadges(cup, cupConf, glasses, glassesConf, headset, headsetConf, granted);

        // Hardware & Telemetry
        double watts = device.ToUpperInvariant().Contains("GPU") ? 14.5 : 2.5;
        double joules = watts * (inferMs / 1000.0);

        string engineTag = string.IsNullOrEmpty(engine) ? "" : $" • {engine}";
        TxtInferSpeed.Text = $"⚡ {inferMs} ms • {fps:F0} FPS • {device}{engineTag}";
        BadgeSpeed.Visibility = Visibility.Visible;
        TxtPower.Text = $"🔋 ~{watts:F1} W • 🌱 {joules:F3} J";
        BadgePower.Visibility = Visibility.Visible;

        bool isEn = _localizer?.CurrentLanguage == "en";
        LblFeedSource.Text = isEn ? $"Live Webcam ({fps:F0} FPS)" : $"Webcam Direct ({fps:F0} FPS)";

        if (granted)
        {
            _consecutiveGrantedFrames++;
            if (_consecutiveGrantedFrames >= 2)
            {
                StopLiveWebcamStream();
                UnlockVaultAndStartMinesweeper(cupConf, glassesConf, headsetConf, inferMs, device);
            }
        }
        else
        {
            _consecutiveGrantedFrames = 0;
            if (ViewMinesweeperMode.Visibility != Visibility.Visible)
            {
                LockVault();
            }
        }
    }

    private void StopLiveWebcamStream()
    {
        _isStreaming = false;
        ResetWebcamButton();
        if (BadgeLiveStream != null) BadgeLiveStream.Visibility = Visibility.Collapsed;

        if (_streamProcess != null)
        {
            try
            {
                if (!_streamProcess.HasExited)
                {
                    try
                    {
                        _streamProcess.StandardInput.WriteLine("stop");
                        _streamProcess.StandardInput.Flush();
                    }
                    catch { }
                    _streamProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            finally
            {
                try { _streamProcess?.Dispose(); } catch { }
                _streamProcess = null;
            }
        }

        if (LblFeedSource != null && (LblFeedSource.Text.StartsWith("Webcam") || LblFeedSource.Text.StartsWith("Live")))
        {
            bool isEn = _localizer?.CurrentLanguage == "en";
            LblFeedSource.Text = isEn ? "Webcam stopped" : "Webcam arrêtée";
        }
    }

    private void ResetWebcamButton()
    {
        if (BtnScanWebcam == null) return;
        bool isEn = _localizer?.CurrentLanguage == "en";
        BtnScanWebcam.Content = isEn ? "📹 Start Live Webcam" : "📹 Lancer la Webcam en Direct";
        BtnScanWebcam.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // Cyan/blue
    }

    private void BtnImportImage_Click(object sender, RoutedEventArgs e)
    {
        StopLiveWebcamStream();
        var ofd = new OpenFileDialog
        {
            Title = "Sélectionner une image pour vérification d'accès",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Tous les fichiers (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            _ = RunVerificationAsync(ofd.FileName, isWebcam: false);
        }
    }

    private void BtnSampleAllThree_Click(object sender, RoutedEventArgs e)
    {
        StopLiveWebcamStream();
        string path = FindRootFile(System.IO.Path.Combine("Data", "Images", "access_samples", "sample_all_three.jpg"));
        _ = RunVerificationAsync(path, isWebcam: false);
    }

    private void BtnSampleBoth_Click(object sender, RoutedEventArgs e)
    {
        StopLiveWebcamStream();
        string path = FindRootFile(System.IO.Path.Combine("Data", "Images", "access_samples", "sample_both.jpg"));
        _ = RunVerificationAsync(path, isWebcam: false);
    }

    private void BtnSampleCup_Click(object sender, RoutedEventArgs e)
    {
        StopLiveWebcamStream();
        string path = FindRootFile(System.IO.Path.Combine("Data", "Images", "access_samples", "sample_cup_only.jpg"));
        _ = RunVerificationAsync(path, isWebcam: false);
    }

    private void BtnSampleGlasses_Click(object sender, RoutedEventArgs e)
    {
        StopLiveWebcamStream();
        string path = FindRootFile(System.IO.Path.Combine("Data", "Images", "access_samples", "sample_glasses_only.jpg"));
        _ = RunVerificationAsync(path, isWebcam: false);
    }

    private void BtnSampleNone_Click(object sender, RoutedEventArgs e)
    {
        StopLiveWebcamStream();
        string path = FindRootFile(System.IO.Path.Combine("Data", "Images", "access_samples", "sample_none.jpg"));
        _ = RunVerificationAsync(path, isWebcam: false);
    }

    private async Task RunVerificationAsync(string? imagePath, bool isWebcam)
    {
        StopLiveWebcamStream();
        string device = GetSelectedDevice();
        string engine = GetSelectedEngine();
        string scriptPath = FindRootFile("security_access_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;

        string outDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Images", "outputs");
        Directory.CreateDirectory(outDir);
        string outPath = System.IO.Path.Combine(outDir, $"access_scan_{DateTime.Now:yyyyMMdd_HHmmss}_{device}.jpg");

        // UI State: Scanning Busy
        OverlayScanning.Visibility = Visibility.Visible;
        string engineLabel = engine.Equals("onnx_olive", StringComparison.OrdinalIgnoreCase)
            ? "ONNX Runtime (Olive FP16)"
            : (engine.Equals("onnx", StringComparison.OrdinalIgnoreCase) ? "ONNX Runtime (DirectML)" : (SiliconDetector.Vendor == SiliconVendor.Intel ? "Intel OpenVINO" : "OpenVINO Native"));
        TxtScanningDevice.Text = $"Inférence {engineLabel} sur {device}";
        PanelFeedPlaceholder.Visibility = Visibility.Collapsed;
        BadgeSpeed.Visibility = Visibility.Collapsed;
        BadgePower.Visibility = Visibility.Collapsed;

        bool cupDetected = false;
        double cupConf = 0.0;
        bool glassesDetected = false;
        double glassesConf = 0.0;
        bool headsetDetected = false;
        double headsetConf = 0.0;
        bool accessGranted = false;
        string finalAnnotated = string.Empty;
        int inferMs = 0;
        string engineResult = string.Empty;
        string lastError = string.Empty;

        await Task.Run(() =>
        {
            try
            {
                string args = isWebcam
                    ? $"\"{scriptPath}\" --webcam --out \"{outPath}\" --conf 0.20 --device {device} --engine {engine}"
                    : $"\"{scriptPath}\" --image \"{imagePath}\" --out \"{outPath}\" --conf 0.20 --device {device} --engine {engine}";

                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = args,
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
                    string? line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var typeProp))
                            {
                                string type = typeProp.GetString() ?? "";
                                if (type == "access_verification_result")
                                {
                                    cupDetected = root.GetProperty("cup_detected").GetBoolean();
                                    cupConf = root.GetProperty("cup_conf").GetDouble();
                                    glassesDetected = root.GetProperty("glasses_detected").GetBoolean();
                                    glassesConf = root.GetProperty("glasses_conf").GetDouble();
                                    if (root.TryGetProperty("headset_detected", out var hp))
                                        headsetDetected = hp.GetBoolean();
                                    if (root.TryGetProperty("headset_conf", out var hcp))
                                        headsetConf = hcp.GetDouble();
                                    accessGranted = root.GetProperty("access_granted").GetBoolean();
                                    finalAnnotated = root.GetProperty("annotated_image").GetString() ?? outPath;
                                    inferMs = root.GetProperty("infer_time_ms").GetInt32();
                                    engineResult = root.TryGetProperty("engine", out var er) ? (er.GetString() ?? "") : engineLabel;
                                }
                                else if (type == "error")
                                {
                                    lastError = root.GetProperty("message").GetString() ?? "Erreur inconnue";
                                }
                            }
                        }
                        catch { }
                    }
                    proc.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        });

        OverlayScanning.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(lastError))
        {
            MessageBox.Show($"Erreur lors de l'analyse vision : {lastError}", "Erreur Scanner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Display annotated image feed
        if (File.Exists(finalAnnotated))
        {
            _currentFeedPath = finalAnnotated;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(finalAnnotated, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                ImgScannerFeed.Source = bmp;
                LblFeedSource.Text = isWebcam ? "Flux Webcam Direct" : System.IO.Path.GetFileName(imagePath ?? "");
            }
            catch { }
        }

        // Update Verification Badges
        UpdateComplianceBadges(cupDetected, cupConf, glassesDetected, glassesConf, headsetDetected, headsetConf, accessGranted);

        // Update Silicon Metrics
        double watts = device.ToUpperInvariant().Contains("GPU") ? 14.5 : 2.5;
        double joules = watts * (inferMs / 1000.0);

        string engineTag = string.IsNullOrEmpty(engineResult) ? "" : $" • {engineResult}";
        TxtInferSpeed.Text = $"⚡ {inferMs} ms • {device}{engineTag}";
        BadgeSpeed.Visibility = Visibility.Visible;
        TxtPower.Text = $"🔋 ~{watts:F1} W • 🌱 {joules:F3} J";
        BadgePower.Visibility = Visibility.Visible;

        // Log Enterprise Telemetry
        _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
            demoName: "Contrôle d'Accès Vision IA (Café + Lunettes + Casque)",
            requestedSilicon: device,
            effectiveSilicon: device,
            siliconDeviceName: $"YOLO-World ({engineResult}) - {device}",
            latencyMs: inferMs,
            customPowerWatts: watts
        );

        // Handle Vault Access Transition
        if (accessGranted)
        {
            await Task.Delay(400); // Brief pause to see 3/3 confirmation
            UnlockVaultAndStartMinesweeper(cupConf, glassesConf, headsetConf, inferMs, device);
        }
        else
        {
            LockVault();
        }
    }

    private void UpdateComplianceBadges(bool cup, double cupConf, bool glasses, double glassesConf, bool headset, double headsetConf, bool granted)
    {
        // Cup Badge
        if (cup)
        {
            BadgeCup.Background = new SolidColorBrush(Color.FromRgb(12, 38, 28));
            BadgeCup.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            TxtCupStatus.Text = $"Détectée ({cupConf * 100:F0}%)";
            TxtCupStatus.Foreground = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            IconCupState.Text = "✅";
        }
        else
        {
            BadgeCup.Background = new SolidColorBrush(Color.FromRgb(38, 26, 29));
            BadgeCup.BorderBrush = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            TxtCupStatus.Text = "Non détectée";
            TxtCupStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            IconCupState.Text = "❌";
        }

        // Glasses Badge
        if (glasses)
        {
            BadgeGlasses.Background = new SolidColorBrush(Color.FromRgb(12, 38, 28));
            BadgeGlasses.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            TxtGlassesStatus.Text = $"Détectées ({glassesConf * 100:F0}%)";
            TxtGlassesStatus.Foreground = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            IconGlassesState.Text = "✅";
        }
        else
        {
            BadgeGlasses.Background = new SolidColorBrush(Color.FromRgb(38, 26, 29));
            BadgeGlasses.BorderBrush = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            TxtGlassesStatus.Text = "Non détectées";
            TxtGlassesStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            IconGlassesState.Text = "❌";
        }

        // Headset Badge
        if (headset)
        {
            BadgeHeadset.Background = new SolidColorBrush(Color.FromRgb(12, 38, 28));
            BadgeHeadset.BorderBrush = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            TxtHeadsetStatus.Text = $"Détecté ({headsetConf * 100:F0}%)";
            TxtHeadsetStatus.Foreground = new SolidColorBrush(Color.FromRgb(45, 212, 191));
            IconHeadsetState.Text = "✅";
        }
        else
        {
            BadgeHeadset.Background = new SolidColorBrush(Color.FromRgb(38, 26, 29));
            BadgeHeadset.BorderBrush = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            TxtHeadsetStatus.Text = "Non détecté";
            TxtHeadsetStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            IconHeadsetState.Text = "❌";
        }

        // Prerequisite Checklist Counter & Progress Bar
        int validatedCount = (cup ? 1 : 0) + (glasses ? 1 : 0) + (headset ? 1 : 0);
        BarPrereqProgress.Value = validatedCount;

        if (validatedCount == 3)
        {
            TxtPrereqProgress.Text = "3 / 3 PROTOCOLE VALIDÉ ✅";
            TxtPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            BarPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        else if (validatedCount == 2)
        {
            TxtPrereqProgress.Text = "2 / 3 CONFORMES (1 MANQUANT)";
            TxtPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            BarPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }
        else if (validatedCount == 1)
        {
            TxtPrereqProgress.Text = "1 / 3 CONFORME (2 MANQUANTS)";
            TxtPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60));
            BarPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(249, 115, 22));
        }
        else
        {
            TxtPrereqProgress.Text = "0 / 3 CONFORMES";
            TxtPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            BarPrereqProgress.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }

        // Banner Status
        if (granted)
        {
            BannerAccessDecision.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            BannerAccessDecision.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            IconAccessDecision.Text = "🟢";
            IconAccessDecision.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            TxtAccessDecision.Text = "ACCÈS AUTORISÉ — 3/3 PRÉREQUIS VALIDÉS (CAFÉ + LUNETTES + CASQUE)";
            TxtAccessDecision.Foreground = new SolidColorBrush(Color.FromRgb(167, 243, 208));
        }
        else
        {
            BannerAccessDecision.Background = new SolidColorBrush(Color.FromRgb(38, 26, 29));
            BannerAccessDecision.BorderBrush = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            IconAccessDecision.Text = "🔒";
            IconAccessDecision.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));

            var missing = new List<string>();
            if (!cup) missing.Add("Tasse de café");
            if (!glasses) missing.Add("Lunettes");
            if (!headset) missing.Add("Casque audio");

            TxtAccessDecision.Text = $"ACCÈS RESTREINT — MANQUANT : {string.Join(" & ", missing).ToUpperInvariant()}";
            TxtAccessDecision.Foreground = new SolidColorBrush(Color.FromRgb(254, 202, 202));
        }
    }

    private void UnlockVaultAndStartMinesweeper(double cupConf, double glassesConf, double headsetConf, int inferMs, string device)
    {
        StopLiveWebcamStream(); // Arrêt immédiat de la caméra
        ViewScanningMode.Visibility = Visibility.Collapsed; // Disparition complète de la caméra et du scanner
        ViewMinesweeperMode.Visibility = Visibility.Visible; // Démineur centré au milieu de l'écran

        TxtUnlockedTelemetry.Text = $"☕ Café ({(int)(cupConf * 100)}%) • 👓 Lunettes ({(int)(glassesConf * 100)}%) • 🎧 Casque ({(int)(headsetConf * 100)}%) — Validé sur {device} en {inferMs} ms";
        StartNewGame();
    }

    private void LockVault()
    {
        _gameTimer.Stop();
        _isGameActive = false;
        ViewMinesweeperMode.Visibility = Visibility.Collapsed;
        ViewScanningMode.Visibility = Visibility.Visible;
    }

    private void BtnRelock_Click(object sender, RoutedEventArgs e)
    {
        LockVault();
    }
    #endregion

    #region Minesweeper Game Engine
    private void InitMinesweeperBoard()
    {
        GridMinesweeper.Children.Clear();
        for (int r = 0; r < ROWS; r++)
        {
            for (int c = 0; c < COLS; c++)
            {
                var btn = new Button
                {
                    Width = 34,
                    Height = 34,
                    Margin = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                    BorderThickness = new Thickness(1.5),
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Padding = new Thickness(0),
                    Tag = new Point(r, c)
                };

                btn.Click += CellButton_Click;
                btn.MouseRightButtonUp += CellButton_MouseRightButtonUp;

                _cellButtons[r, c] = btn;
                GridMinesweeper.Children.Add(btn);
            }
        }
    }

    private void StartNewGame()
    {
        _gameTimer.Stop();
        _elapsedSeconds = 0;
        _isGameActive = true;
        _isFirstClick = true;

        TxtMinesTimer.Text = "000";
        TxtMinesCounter.Text = $"{TOTAL_MINES:D3}";
        BtnMinesFace.Content = "🙂";
        BannerGameResult.Visibility = Visibility.Collapsed;

        for (int r = 0; r < ROWS; r++)
        {
            for (int c = 0; c < COLS; c++)
            {
                _mines[r, c] = false;
                _revealed[r, c] = false;
                _flagged[r, c] = false;
                _adjacent[r, c] = 0;

                var btn = _cellButtons[r, c];
                btn.IsEnabled = true;
                btn.Content = "";
                btn.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85));
            }
        }
    }

    private void PlaceMines(int avoidR, int avoidC)
    {
        var rand = new Random();
        int placed = 0;

        while (placed < TOTAL_MINES)
        {
            int r = rand.Next(ROWS);
            int c = rand.Next(COLS);

            // Avoid first clicked cell and its 8 neighbors for a generous safe opening
            if (Math.Abs(r - avoidR) <= 1 && Math.Abs(c - avoidC) <= 1) continue;
            if (_mines[r, c]) continue;

            _mines[r, c] = true;
            placed++;
        }

        // Calculate adjacent numbers
        for (int r = 0; r < ROWS; r++)
        {
            for (int c = 0; c < COLS; c++)
            {
                if (_mines[r, c]) continue;
                int count = 0;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nr = r + dr;
                        int nc = c + dc;
                        if (nr >= 0 && nr < ROWS && nc >= 0 && nc < COLS && _mines[nr, nc])
                        {
                            count++;
                        }
                    }
                }
                _adjacent[r, c] = count;
            }
        }
    }

    private void CellButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isGameActive || sender is not Button btn || btn.Tag is not Point pt) return;
        int r = (int)pt.X;
        int c = (int)pt.Y;

        if (_flagged[r, c] || _revealed[r, c]) return;

        if (_isFirstClick)
        {
            PlaceMines(r, c);
            _isFirstClick = false;
            _gameTimer.Start();
        }

        RevealCell(r, c);
    }

    private void CellButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isGameActive || sender is not Button btn || btn.Tag is not Point pt) return;
        int r = (int)pt.X;
        int c = (int)pt.Y;

        if (_revealed[r, c]) return;

        _flagged[r, c] = !_flagged[r, c];
        btn.Content = _flagged[r, c] ? "🚩" : "";

        // Update remaining mines counter
        int flagCount = 0;
        for (int i = 0; i < ROWS; i++)
            for (int j = 0; j < COLS; j++)
                if (_flagged[i, j]) flagCount++;

        int remaining = TOTAL_MINES - flagCount;
        TxtMinesCounter.Text = remaining >= 0 ? $"{remaining:D3}" : $"-{Math.Abs(remaining):D2}";

        e.Handled = true;
    }

    private void RevealCell(int r, int c)
    {
        if (r < 0 || r >= ROWS || c < 0 || c >= COLS) return;
        if (_revealed[r, c] || _flagged[r, c]) return;

        _revealed[r, c] = true;
        var btn = _cellButtons[r, c];

        if (_mines[r, c])
        {
            // Mine triggered! Game Over
            btn.Content = "💥";
            btn.Background = new SolidColorBrush(Color.FromRgb(153, 27, 27));
            TriggerGameOver(win: false);
            return;
        }

        // Empty or numbered cell
        btn.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        btn.BorderBrush = new SolidColorBrush(Color.FromRgb(30, 41, 59));

        int count = _adjacent[r, c];
        if (count > 0)
        {
            btn.Content = count.ToString();
            btn.Foreground = GetNumberBrush(count);
        }
        else
        {
            btn.Content = "";
            // Flood fill adjacent zeroes and border numbers
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    RevealCell(r + dr, c + dc);
                }
            }
        }

        CheckVictory();
    }

    private void CheckVictory()
    {
        int revealedCount = 0;
        for (int r = 0; r < ROWS; r++)
            for (int c = 0; c < COLS; c++)
                if (_revealed[r, c]) revealedCount++;

        if (revealedCount == (ROWS * COLS - TOTAL_MINES))
        {
            TriggerGameOver(win: true);
        }
    }

    private void TriggerGameOver(bool win)
    {
        _gameTimer.Stop();
        _isGameActive = false;

        if (win)
        {
            BtnMinesFace.Content = "😎";
            BannerGameResult.Visibility = Visibility.Visible;
            BannerGameResult.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            TxtGameResult.Text = $"🎉 VICTOIRE ! Démineur résolu en {_elapsedSeconds} s !";
            TxtGameResult.Foreground = new SolidColorBrush(Color.FromRgb(167, 243, 208));

            // Flag all mines
            for (int r = 0; r < ROWS; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    if (_mines[r, c]) _cellButtons[r, c].Content = "🚩";
                }
            }
            TxtMinesCounter.Text = "000";
        }
        else
        {
            BtnMinesFace.Content = "😵";
            BannerGameResult.Visibility = Visibility.Visible;
            BannerGameResult.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            TxtGameResult.Text = "💥 BOOM ! Mine déclenchée. Cliquez sur 🙂 pour rejouer.";
            TxtGameResult.Foreground = new SolidColorBrush(Color.FromRgb(254, 202, 202));

            // Reveal all other mines
            for (int r = 0; r < ROWS; r++)
            {
                for (int c = 0; c < COLS; c++)
                {
                    if (_mines[r, c] && !_revealed[r, c])
                    {
                        _cellButtons[r, c].Content = "💣";
                        _cellButtons[r, c].Background = new SolidColorBrush(Color.FromRgb(30, 20, 25));
                    }
                }
            }
        }
    }

    private Brush GetNumberBrush(int number) => number switch
    {
        1 => new SolidColorBrush(Color.FromRgb(56, 189, 248)),  // Sky blue
        2 => new SolidColorBrush(Color.FromRgb(52, 211, 153)),  // Green
        3 => new SolidColorBrush(Color.FromRgb(248, 113, 113)), // Red
        4 => new SolidColorBrush(Color.FromRgb(129, 140, 248)), // Indigo
        5 => new SolidColorBrush(Color.FromRgb(251, 146, 60)),  // Orange
        6 => new SolidColorBrush(Color.FromRgb(45, 212, 191)),  // Teal
        7 => new SolidColorBrush(Color.FromRgb(244, 114, 182)), // Pink
        8 => new SolidColorBrush(Color.FromRgb(156, 163, 175)), // Gray
        _ => Brushes.White
    };

    private void GameTimer_Tick(object? sender, EventArgs e)
    {
        _elapsedSeconds++;
        TxtMinesTimer.Text = $"{Math.Min(999, _elapsedSeconds):D3}";
    }

    private void BtnMinesFace_Click(object sender, RoutedEventArgs e)
    {
        StartNewGame();
    }

    private void BtnNewGame_Click(object sender, RoutedEventArgs e)
    {
        StartNewGame();
    }

    private void BtnLaunchWinmine_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string winminePath = System.IO.Path.Combine(systemPath, "winmine.exe");

            if (File.Exists(winminePath))
            {
                Process.Start(new ProcessStartInfo(winminePath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show(
                    "Le jeu de Démineur souverain est déjà 100% opérationnel et interactif directement dans cette interface !",
                    "Démineur Local Copilot+",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors du lancement externe : {ex.Message}",
                "Démineur Copilot+",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }
    #endregion

    #region Helpers & Lifecycle
    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        bool isEn = _localizer?.CurrentLanguage == "en";

        LblAccessTitle.Text = isEn
            ? "🛡️ ZERO-TRUST ACCESS GATE & SOVEREIGN MINESWEEPER"
            : "🛡️ CONTRÔLE D'ACCÈS ZERO-TRUST & DÉMINEUR SOUVERAIN";

        LblAccessSub.Text = isEn
            ? "Multimodal NPU/GPU Authentication: Mandatory joint detection of Coffee Cup ☕ + Eyeglasses 👓 + Headset 🎧"
            : "Authentification multimodale NPU/GPU : Détection conjointe obligatoire Tasse ☕ + Lunettes 👓 + Casque Audio 🎧";

        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = isEn
                ? "Model: YOLOv8s-World (Zero-Trust Vision 27 classes)"
                : "Modèle : YOLOv8s-World (Vision Zéro-Trust 27 classes)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = isEn
                    ? "Real-time YOLOv8s-World object detection (supports OpenVINO IR, raw ONNX, and Microsoft Olive)."
                    : "Détection d'objets en temps réel YOLOv8s-World (supporte OpenVINO IR, ONNX brut et Microsoft Olive).";
            }
        }

        if (_isStreaming)
        {
            BtnScanWebcam.Content = isEn ? "⏹️ Stop Webcam" : "⏹️ Arrêter la Webcam";
        }
        else
        {
            BtnScanWebcam.Content = isEn ? "📹 Start Live Webcam" : "📹 Lancer la Webcam en Direct";
        }

        if (TxtLiveStreamLabel != null)
        {
            TxtLiveStreamLabel.Text = isEn ? "🔴 LIVE WEBCAM • REAL-TIME SCAN" : "🔴 LIVE WEBCAM • SCAN EN TEMPS RÉEL";
        }

        if (BtnSampleAllThree != null)
        {
            BtnSampleAllThree.Content = isEn ? "☕+👓+🎧 Valid (3/3)" : "☕+👓+🎧 Valide (3/3)";
        }
        if (BtnSampleBoth != null)
        {
            BtnSampleBoth.Content = isEn ? "☕+👓 Missing Headset (2/3)" : "☕+👓 Sans Casque (2/3)";
        }
        if (BtnSampleCup != null)
        {
            BtnSampleCup.Content = isEn ? "☕ Cup Only" : "☕ Tasse Seule";
        }
        if (BtnSampleGlasses != null)
        {
            BtnSampleGlasses.Content = isEn ? "👓 Glasses Only" : "👓 Lunettes Seules";
        }
        if (BtnSampleNone != null)
        {
            BtnSampleNone.Content = isEn ? "❌ None" : "❌ Aucun";
        }

        BtnImportImage.Content = isEn ? "📁 Import Photo" : "📁 Importer une Photo";
        BtnNewGame.Content = isEn ? "🔄 New Game" : "🔄 Nouvelle Partie";
        BtnLaunchWinmine.Content = isEn ? "🎮 External Windows Minesweeper" : "🎮 Démineur Windows Externe";
        if (TxtDocHelp != null) TxtDocHelp.Text = isEn ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = isEn 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
    }

    private static string FindRootFile(string relativePath)
    {
        string cur = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            string candidate = System.IO.Path.Combine(cur, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;

            string? parent = Directory.GetParent(cur)?.FullName;
            if (parent == null || parent == cur) break;
            cur = parent;
        }
        return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
    }

    public void Dispose()
    {
        _gameTimer.Stop();
        StopLiveWebcamStream();
        try
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        finally
        {
            try { _activeProcess?.Dispose(); } catch { }
            _activeProcess = null;
        }
    }

    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_security", _localizer?.CurrentLanguage);
    }
    #endregion
}
