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
using System.Windows.Media.Imaging;
using CopilotPlusDemoHub.Core;
using Microsoft.Win32;

namespace CopilotPlusDemoHub.UI.Views;

public partial class MagicEraserStudioView : UserControl, IDisposable
{
    private readonly FoundryClient _foundryClient;
    private LocalizationService _localizer;
    private readonly Func<string>? _getSelectedDevice;
    private readonly Func<string>? _getEffectiveDevice;
    private readonly Action<string>? _setSelectedDevice;
    private bool _isSyncingDevice = false;
    private readonly HardwareTelemetryService _telemetryService = new();

    private string _currentImagePath = string.Empty;
    private string _originalImagePath = string.Empty;
    private string _activeMaskPath = string.Empty;
    private string _lastTargetName = string.Empty;

    private readonly List<string> _historyStack = new();
    private Process? _activeProcess;

    private Process? _daemonProcess;
    private StreamWriter? _daemonWriter;
    private StreamReader? _daemonReader;
    private string _daemonDevice = string.Empty;
    private string _daemonEngine = string.Empty;
    private readonly object _daemonLock = new();

    private void EnsureDaemonRunning(string targetDevice, string targetEngine)
    {
        lock (_daemonLock)
        {
            if (_daemonProcess != null && !_daemonProcess.HasExited &&
                _daemonDevice == targetDevice && _daemonEngine == targetEngine)
            {
                return;
            }

            KillDaemon();

            try
            {
                string scriptPath = FindRootFile("eraser_service.py");
                string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string args = $"\"{scriptPath}\" --action daemon --device {targetDevice} --engine {targetEngine}";

                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = args,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardInputEncoding = System.Text.Encoding.UTF8
                };

                _daemonProcess = Process.Start(psi);
                if (_daemonProcess != null)
                {
                    _daemonWriter = _daemonProcess.StandardInput;
                    _daemonReader = _daemonProcess.StandardOutput;
                    _daemonDevice = targetDevice;
                    _daemonEngine = targetEngine;

                    // Read initial ready handshake
                    _daemonReader.ReadLine();
                }
            }
            catch
            {
                KillDaemon();
            }
        }
    }

    private void KillDaemon()
    {
        lock (_daemonLock)
        {
            try
            {
                if (_daemonWriter != null)
                {
                    _daemonWriter.WriteLine("quit");
                    _daemonWriter.Flush();
                }
                if (_daemonProcess != null && !_daemonProcess.HasExited)
                {
                    _daemonProcess.Kill();
                    _daemonProcess.Dispose();
                }
            }
            catch { }
            finally
            {
                _daemonProcess = null;
                _daemonWriter = null;
                _daemonReader = null;
                _daemonDevice = string.Empty;
                _daemonEngine = string.Empty;
            }
        }
    }

    private (double Watts, double Joules, string Label, System.Windows.Media.Brush Brush, string Tooltip) GetSiliconPowerMetrics(string device, int inferMs)
    {
        string devUpper = (device ?? "").ToUpperInvariant();

        // 1. Check if real battery discharge rate is active via ACPI
        double liveWatts = 0;
        try
        {
            var snap = _telemetryService.GetCurrentSnapshot();
            if (snap != null && snap.DischargeRateWatts > 1.0)
            {
                liveWatts = snap.DischargeRateWatts;
            }
        }
        catch { }

        // 2. Active power profiles for Intel Lunar Lake architecture during AI inference:
        // - NPU (Intel AI Boost): isolated ~2.0W - 2.5W ultra-low power compute block
        // - GPU (Intel Arc 140V Xe2): ~14.0W - 18.0W graphic compute slice
        // - CPU (Intel Core Ultra 7): ~24.0W - 28.0W full package AVX-VNNI compute
        double watts;
        string label;
        System.Windows.Media.Brush brush;
        string tooltip;

        bool isEn = _localizer?.CurrentLanguage == "en";

        if (devUpper.Contains("GPU"))
        {
            watts = liveWatts > 10.0 ? liveWatts : 16.5;
            brush = (System.Windows.Media.Brush)FindResource("GpuAmberBrush");
            label = isEn ? "Modéré (~16.5W)" : "Modérée (~16.5W)";
            tooltip = isEn
                ? $"GPU Power Draw: ~{watts:F1} W during inference. {SiliconDetector.GpuName} delivers heavy graphics throughput at moderate energy cost."
                : $"Consommation GPU : ~{watts:F1} W durant l'inférence. Le {SiliconDetector.GpuName} offre une forte puissance graphique pour une consommation modérée.";
        }
        else if (devUpper.Contains("CPU"))
        {
            watts = liveWatts > 15.0 ? liveWatts : 26.0;
            brush = (System.Windows.Media.Brush)FindResource("CpuIndigoBrush");
            label = isEn ? "High (~26.0W)" : "Élevée (~26.0W)";
            tooltip = isEn
                ? $"CPU Power Draw: ~{watts:F1} W during inference. CPU execution uses multi-core x86 vector units, drawing significant package power."
                : $"Consommation CPU : ~{watts:F1} W durant l'inférence. L'exécution CPU sollicite les cœurs x86 et le package complet, entraînant une forte décharge.";
        }
        else // NPU
        {
            watts = (liveWatts > 1.0 && liveWatts < 6.0) ? liveWatts : 2.2;
            brush = (System.Windows.Media.Brush)FindResource("NpuGreenBrush");
            label = isEn ? "Ultra-low power (~2.2W)" : "Ultra-sobre (~2.2W)";
            tooltip = isEn
                ? $"NPU Power Draw: ~{watts:F1} W during inference. {SiliconDetector.NpuName} consumes up to 8x less energy than GPU, preserving battery life."
                : $"Consommation NPU : ~{watts:F1} W durant l'inférence. Le {SiliconDetector.NpuName} consomme jusqu'à 8x moins d'énergie qu'un GPU et préserve la batterie.";
        }

        double joules = watts * (inferMs / 1000.0);
        return (watts, joules, label, brush, tooltip);
    }

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

    public MagicEraserStudioView(
        FoundryClient foundryClient,
        LocalizationService localizer,
        Func<string>? getSelectedDevice = null,
        Func<string>? getEffectiveDevice = null,
        Action<string>? setSelectedDevice = null)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;
        _getSelectedDevice = getSelectedDevice;
        _getEffectiveDevice = getEffectiveDevice;
        _setSelectedDevice = setSelectedDevice;

        if (SiliconDetector.Vendor != SiliconVendor.Intel && RadEngineOlive != null)
        {
            RadEngineOlive.IsChecked = true;
        }
        RefreshDeviceUI();

        // Default to desk sample with coffee cup and phone
        string defaultSample = FindRootFile(System.IO.Path.Combine("Data", "Images", "desk_sample.jpg"));
        LoadImage(defaultSample, isNewSource: true);
        ApplyLocalization(_localizer);

        Unloaded += (s, e) => KillDaemon();
    }

    public void RefreshDeviceUI()
    {
        if (_isSyncingDevice) return;
        _isSyncingDevice = true;
        try
        {
            string eff = _getEffectiveDevice?.Invoke() ?? "NPU";
            if (eff.Equals("GPU", StringComparison.OrdinalIgnoreCase))
            {
                if (RadDeviceGpu != null && RadDeviceGpu.IsChecked != true) RadDeviceGpu.IsChecked = true;
            }
            else if (eff.Equals("CPU", StringComparison.OrdinalIgnoreCase))
            {
                if (RadDeviceCpu != null && RadDeviceCpu.IsChecked != true) RadDeviceCpu.IsChecked = true;
            }
            else
            {
                if (RadDeviceNpu != null && RadDeviceNpu.IsChecked != true) RadDeviceNpu.IsChecked = true;
            }

            if (LblStatus != null && _localizer != null)
            {
                string dev = GetActiveDevice();
                LblStatus.Text = string.Format(_localizer.Get("Eraser_StatusConfigured"), dev);
            }
        }
        finally
        {
            _isSyncingDevice = false;
        }
    }

    public string GetActiveDevice()
    {
        if (RadDeviceGpu?.IsChecked == true) return "GPU";
        if (RadDeviceCpu?.IsChecked == true) return "CPU";
        if (RadDeviceNpu?.IsChecked == true) return "NPU";

        string dev = _getEffectiveDevice?.Invoke() ?? "NPU";
        return string.IsNullOrWhiteSpace(dev) ? "NPU" : dev.ToUpperInvariant();
    }

    public string GetActiveEngine()
    {
        if (RadEngineOlive?.IsChecked == true) return "onnx_olive";
        if (RadEngineONNX?.IsChecked == true) return "onnx";
        return "openvino";
    }

    private void Engine_Checked(object sender, RoutedEventArgs e)
    {
        KillDaemon();
        if (LblStatus != null)
        {
            string eng = GetActiveEngine() switch
            {
                "onnx_olive" => "ONNX Olive FP16",
                "onnx" => "ONNX DirectML",
                _ => SiliconDetector.Vendor == SiliconVendor.Intel ? "Intel OpenVINO" : "OpenVINO Native"
            };
            string dev = GetActiveDevice();
            LblStatus.Text = $"Moteur {eng} actif sur {dev}";
        }
    }

    private void Device_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingDevice) return;
        KillDaemon();
        string dev = GetActiveDevice();
        _setSelectedDevice?.Invoke(dev);
        if (LblStatus != null && _localizer != null)
        {
            LblStatus.Text = string.Format(_localizer.Get("Eraser_StatusConfigured"), dev);
        }
    }

    private void LoadImage(string filePath, bool isNewSource = false)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            ImgDisplay.Source = bitmap;
            _currentImagePath = filePath;

            if (isNewSource)
            {
                _originalImagePath = filePath;
                _historyStack.Clear();
                BtnUndo.IsEnabled = false;
                BadgeHistoryCount.Visibility = Visibility.Collapsed;
                BadgeSpeed.Visibility = Visibility.Collapsed;
                BadgePower.Visibility = Visibility.Collapsed;
            }

            BannerConfirm.Visibility = Visibility.Collapsed;
            if (_localizer != null)
            {
                LblCanvasInfo.Text = string.Format(_localizer.Get("Eraser_CanvasInfo"), System.IO.Path.GetFileName(filePath), bitmap.PixelWidth, bitmap.PixelHeight);
                LblStatus.Text = _localizer.Get("Eraser_StatusInitial");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible de charger l'image : {ex.Message}", "Erreur Image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisplayImageFile(string filePath)
    {
        if (!File.Exists(filePath)) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            ImgDisplay.Source = bitmap;
        }
        catch { }
    }

    private void BtnImportImage_Click(object sender, RoutedEventArgs e)
    {
        var ofd = new OpenFileDialog
        {
            Title = _localizer.Get("Eraser_ImportDialogTitle"),
            Filter = "Images (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp|Tous les fichiers (*.*)|*.*"
        };

        if (ofd.ShowDialog() == true)
        {
            LoadImage(ofd.FileName, isNewSource: true);
        }
    }

    private void BtnSampleDesk_Click(object sender, RoutedEventArgs e)
    {
        string p = FindRootFile(System.IO.Path.Combine("Data", "Images", "desk_sample.jpg"));
        TxtPrompt.Text = _localizer.Get("Eraser_DefaultPromptDesk");
        LoadImage(p, isNewSource: true);
    }

    private void BtnSampleBeach_Click(object sender, RoutedEventArgs e)
    {
        string p = FindRootFile(System.IO.Path.Combine("Data", "Images", "beach_sample.jpg"));
        TxtPrompt.Text = _localizer.Get("Eraser_DefaultPromptBeach");
        LoadImage(p, isNewSource: true);
    }

    private void BtnSampleTie_Click(object sender, RoutedEventArgs e)
    {
        string p = FindRootFile(System.IO.Path.Combine("Data", "Images", "portrait_homme.jpg"));
        TxtPrompt.Text = _localizer.Get("Eraser_DefaultPromptTie");
        LoadImage(p, isNewSource: true);
    }

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string text)
        {
            string clean = text.Replace("☕", "").Replace("📱", "").Replace("👔", "").Replace("👥", "").Replace("💻", "").Replace("🌿", "").Trim();
            TxtPrompt.Text = clean;
            RunDetect(prompt: clean);
        }
    }

    private void TxtPrompt_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunDetect(prompt: TxtPrompt.Text.Trim());
        }
    }

    private void BtnDetect_Click(object sender, RoutedEventArgs e)
    {
        RunDetect(prompt: TxtPrompt.Text.Trim());
    }

    private void ImgDisplay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath) || !File.Exists(_currentImagePath)) return;
        if (ImgDisplay.Source is not BitmapSource bs) return;

        Point pos = e.GetPosition(ImgDisplay);
        double actualW = ImgDisplay.ActualWidth;
        double actualH = ImgDisplay.ActualHeight;
        if (actualW <= 0 || actualH <= 0) return;

        // Account for Uniform Stretch inside the container
        double imageRatio = (double)bs.PixelWidth / bs.PixelHeight;
        double viewRatio = actualW / actualH;

        double renderW, renderH, offsetX, offsetY;
        if (viewRatio > imageRatio)
        {
            renderH = actualH;
            renderW = actualH * imageRatio;
            offsetX = (actualW - renderW) / 2.0;
            offsetY = 0;
        }
        else
        {
            renderW = actualW;
            renderH = actualW / imageRatio;
            offsetX = 0;
            offsetY = (actualH - renderH) / 2.0;
        }

        double imgX = (pos.X - offsetX) / renderW;
        double imgY = (pos.Y - offsetY) / renderH;

        if (imgX >= 0.0 && imgX <= 1.0 && imgY >= 0.0 && imgY <= 1.0)
        {
            RunDetect(prompt: "", clickX: imgX, clickY: imgY);
        }
    }

    private async void RunDetect(string prompt = "", double? clickX = null, double? clickY = null)
    {
        if (string.IsNullOrEmpty(_currentImagePath) || !File.Exists(_currentImagePath))
        {
            MessageBox.Show(_localizer.Get("Eraser_ImageRequired"), _localizer.Get("Eraser_ImageRequiredTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string scriptPath = FindRootFile("eraser_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string outDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Images", "outputs");
        Directory.CreateDirectory(outDir);

        string previewPath = System.IO.Path.Combine(outDir, $"preview_{DateTime.Now:HHmmss}.jpg");
        string maskPath = System.IO.Path.Combine(outDir, $"mask_{DateTime.Now:HHmmss}.png");
        string targetDevice = GetActiveDevice();
        string targetEngine = GetActiveEngine();

        string args = $"\"{scriptPath}\" --action detect --image \"{_currentImagePath}\" --device {targetDevice} --engine {targetEngine} --out_preview \"{previewPath}\" --out_mask \"{maskPath}\"";
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            args += $" --prompt \"{prompt}\"";
        }
        if (clickX.HasValue && clickY.HasValue)
        {
            args += $" --click_x {clickX.Value.ToString("F4", CultureInfo.InvariantCulture)} --click_y {clickY.Value.ToString("F4", CultureInfo.InvariantCulture)}";
        }

        BtnDetect.IsEnabled = false;
        OverlayStatus.Visibility = Visibility.Visible;
        TxtOverlayMessage.Text = _localizer.Get("Eraser_DetectOverlayMsg");
        string engineDisplay = targetEngine switch
        {
            "onnx_olive" => "ONNX Olive FP16",
            "onnx" => "ONNX DirectML",
            _ => SiliconDetector.Vendor == SiliconVendor.Intel ? "Intel OpenVINO" : "OpenVINO Native"
        };
        TxtOverlaySub.Text = $"{engineDisplay} • {targetDevice}";

        JsonElement resultObj = default;
        bool hasResult = false;
        string lastErr = string.Empty;

        await Task.Run(() =>
        {
            // 1. Try persistent daemon for ultra-low latency (<200ms)
            try
            {
                EnsureDaemonRunning(targetDevice, targetEngine);
                if (_daemonProcess != null && !_daemonProcess.HasExited && _daemonWriter != null && _daemonReader != null)
                {
                    lock (_daemonLock)
                    {
                        var reqObj = new
                        {
                            action = "detect",
                            image = _currentImagePath,
                            prompt = prompt,
                            click_x = clickX,
                            click_y = clickY,
                            out_preview = previewPath,
                            out_mask = maskPath,
                            device = targetDevice,
                            engine = targetEngine
                        };
                        string jsonReq = JsonSerializer.Serialize(reqObj);
                        _daemonWriter.WriteLine(jsonReq);
                        _daemonWriter.Flush();

                        string? respLine = _daemonReader.ReadLine();
                        if (!string.IsNullOrWhiteSpace(respLine))
                        {
                            using var doc = JsonDocument.Parse(respLine);
                            var root = doc.RootElement.Clone();
                            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "detect_result")
                            {
                                resultObj = root;
                                hasResult = true;
                                return;
                            }
                            else if (root.TryGetProperty("type", out var errType) && errType.GetString() == "error")
                            {
                                lastErr = root.GetProperty("message").GetString() ?? "";
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
                KillDaemon();
            }

            // 2. Fallback to standard one-shot process
            try
            {
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

                _activeProcess = Process.Start(psi);
                if (_activeProcess != null)
                {
                    var errTask = _activeProcess.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = _activeProcess.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement.Clone();
                            if (root.TryGetProperty("type", out var typeProp))
                            {
                                string type = typeProp.GetString() ?? "";
                                if (type == "detect_result")
                                {
                                    resultObj = root;
                                    hasResult = true;
                                }
                                else if (type == "error")
                                {
                                    lastErr = root.GetProperty("message").GetString() ?? "";
                                }
                            }
                        }
                        catch { }
                    }
                    _activeProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                lastErr = ex.Message;
            }
            finally
            {
                try { _activeProcess?.Dispose(); } catch { }
                _activeProcess = null;
            }
        });

        OverlayStatus.Visibility = Visibility.Collapsed;
        BtnDetect.IsEnabled = true;

        if (hasResult)
        {
            ProcessDetectResult(resultObj);
        }
        else
        {
            LblStatus.Text = !string.IsNullOrEmpty(lastErr) ? $"⚠️ {lastErr}" : "Aucun objet correspondant trouvé.";
        }
    }

    private void ProcessDetectResult(JsonElement root)
    {
        bool found = root.TryGetProperty("found", out var fProp) && fProp.GetBoolean();
        if (!found)
        {
            string msg = root.TryGetProperty("message", out var mProp) ? mProp.GetString() ?? "" : "Aucun objet détachable détecté.";
            LblStatus.Text = $"⚠️ {msg}";
            BannerConfirm.Visibility = Visibility.Collapsed;
            return;
        }

        string targetName = (_localizer.CurrentLanguage == "en" && root.TryGetProperty("target_name_en", out var enProp))
            ? enProp.GetString() ?? ""
            : (root.GetProperty("target_name_fr").GetString() ?? "Objet sélectionné");
        double conf = root.GetProperty("confidence").GetDouble();
        string previewPath = root.GetProperty("preview_path").GetString() ?? "";
        string maskPath = root.GetProperty("mask_path").GetString() ?? "";
        int inferMs = root.GetProperty("infer_time_ms").GetInt32();
        string device = root.TryGetProperty("device", out var devProp) ? devProp.GetString() ?? "NPU" : "NPU";
        string deviceName = root.TryGetProperty("device_name", out var dnProp) ? dnProp.GetString() ?? "" : "";

        _activeMaskPath = maskPath;
        _lastTargetName = targetName;

        // Show the cyan glowing contour preview
        if (File.Exists(previewPath))
        {
            DisplayImageFile(previewPath);
        }

        // Show Confirmation Banner
        TxtConfirmTitle.Text = string.Format(_localizer.Get("Eraser_ConfirmTitle"), targetName, (int)(conf * 100), device);
        BannerConfirm.Visibility = Visibility.Visible;

        string displayHw = string.IsNullOrEmpty(deviceName) ? device : $"{device} ({deviceName})";
        var power = GetSiliconPowerMetrics(device, inferMs);

        TxtInferSpeed.Text = $"⚡ {inferMs} ms • 🔋 ~{power.Watts:F1} W • {displayHw}";
        BadgeSpeed.ToolTip = power.Tooltip;
        BadgeSpeed.Visibility = Visibility.Visible;

        TxtPower.Text = power.Joules < 0.1
            ? $"🌱 {power.Joules:F3} J ({power.Label})"
            : $"⚡ {power.Joules:F2} J ({power.Label})";
        TxtPower.Foreground = power.Brush;
        BadgePower.ToolTip = power.Tooltip;
        BadgePower.Visibility = Visibility.Visible;

        _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
            demoName: "Gomme Magique - Détection",
            requestedSilicon: GetActiveDevice(),
            effectiveSilicon: device,
            siliconDeviceName: displayHw,
            latencyMs: inferMs,
            customPowerWatts: power.Watts
        );

        LblStatus.Text = _localizer.CurrentLanguage == "en"
            ? $"Object '{targetName}' successfully outlined in cyan on {device} ({inferMs} ms • ~{power.Watts:F1} W)! Click 'Confirm Removal' to erase it."
            : $"Objet '{targetName}' détouré en cyan avec succès sur {device} ({inferMs} ms • ~{power.Watts:F1} W) ! Cliquez sur « Confirmer la Suppression » pour l'effacer.";
    }

    private async void BtnConfirmErase_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_activeMaskPath) || !File.Exists(_activeMaskPath))
        {
            MessageBox.Show(_localizer.Get("Eraser_MaskNotFound"), "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string scriptPath = FindRootFile("eraser_service.py");
        string workingDir = System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        string outDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Images", "outputs");
        Directory.CreateDirectory(outDir);

        string erasedOutputPath = System.IO.Path.Combine(outDir, $"erased_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
        string targetDevice = GetActiveDevice();

        BtnConfirmErase.IsEnabled = false;
        BtnCancelSelection.IsEnabled = false;
        OverlayStatus.Visibility = Visibility.Visible;
        TxtOverlayMessage.Text = string.Format(_localizer.Get("Eraser_ErasingOverlayMsg"), _lastTargetName);
        TxtOverlaySub.Text = string.Format(_localizer.Get("Eraser_DetectOverlaySub"), targetDevice);

        string finalResultPath = string.Empty;
        int eraseInferMs = 0;
        string eraseDevice = targetDevice;
        string lastErr = string.Empty;

        await Task.Run(() =>
        {
            // 1. Try persistent daemon for ultra-low latency (<50ms)
            try
            {
                EnsureDaemonRunning(targetDevice, GetActiveEngine());
                if (_daemonProcess != null && !_daemonProcess.HasExited && _daemonWriter != null && _daemonReader != null)
                {
                    lock (_daemonLock)
                    {
                        var reqObj = new
                        {
                            action = "erase",
                            image = _currentImagePath,
                            mask = _activeMaskPath,
                            output = erasedOutputPath,
                            device = targetDevice
                        };
                        string jsonReq = JsonSerializer.Serialize(reqObj);
                        _daemonWriter.WriteLine(jsonReq);
                        _daemonWriter.Flush();

                        string? respLine = _daemonReader.ReadLine();
                        if (!string.IsNullOrWhiteSpace(respLine))
                        {
                            using var doc = JsonDocument.Parse(respLine);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "erase_result")
                            {
                                finalResultPath = root.GetProperty("output_path").GetString() ?? erasedOutputPath;
                                eraseInferMs = root.GetProperty("infer_time_ms").GetInt32();
                                if (root.TryGetProperty("device", out var devProp))
                                {
                                    eraseDevice = devProp.GetString() ?? targetDevice;
                                }
                                return;
                            }
                        }
                    }
                }
            }
            catch
            {
                KillDaemon();
            }

            // 2. Fallback to standard one-shot process
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = PythonPathHelper.GetPythonExe(),
                    Arguments = $"\"{scriptPath}\" --action erase --image \"{_currentImagePath}\" --mask \"{_activeMaskPath}\" --output \"{erasedOutputPath}\" --device {targetDevice}",
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                _activeProcess = Process.Start(psi);
                if (_activeProcess != null)
                {
                    var errTask = _activeProcess.StandardError.ReadToEndAsync();
                    string? line;
                    while ((line = _activeProcess.StandardOutput.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "erase_result")
                            {
                                finalResultPath = root.GetProperty("output_path").GetString() ?? erasedOutputPath;
                                eraseInferMs = root.GetProperty("infer_time_ms").GetInt32();
                                if (root.TryGetProperty("device", out var devProp))
                                {
                                    eraseDevice = devProp.GetString() ?? targetDevice;
                                }
                            }
                        }
                        catch { }
                    }
                    _activeProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                lastErr = ex.Message;
            }
            finally
            {
                try { _activeProcess?.Dispose(); } catch { }
                _activeProcess = null;
            }
        });

        OverlayStatus.Visibility = Visibility.Collapsed;
        BtnConfirmErase.IsEnabled = true;
        BtnCancelSelection.IsEnabled = true;
        BannerConfirm.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrEmpty(finalResultPath) && File.Exists(finalResultPath))
        {
            // Push previous state to history
            _historyStack.Add(_currentImagePath);
            BtnUndo.IsEnabled = true;
            TxtHistoryCount.Text = string.Format(_localizer.Get("Eraser_ModificationsCount"), _historyStack.Count, _historyStack.Count > 1 ? "s" : "");
            BadgeHistoryCount.Visibility = Visibility.Visible;

            // Display erased result
            _currentImagePath = finalResultPath;
            DisplayImageFile(finalResultPath);

            var power = GetSiliconPowerMetrics(eraseDevice, eraseInferMs);

            TxtInferSpeed.Text = $"⚡ {eraseInferMs} ms • 🔋 ~{power.Watts:F1} W • Inpainting ({eraseDevice})";
            BadgeSpeed.ToolTip = power.Tooltip;
            BadgeSpeed.Visibility = Visibility.Visible;

            TxtPower.Text = power.Joules < 0.1
                ? $"🌱 {power.Joules:F3} J ({power.Label})"
                : $"⚡ {power.Joules:F2} J ({power.Label})";
            TxtPower.Foreground = power.Brush;
            BadgePower.ToolTip = power.Tooltip;
            BadgePower.Visibility = Visibility.Visible;

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Gomme Magique - Effacement",
                requestedSilicon: targetDevice,
                effectiveSilicon: eraseDevice,
                siliconDeviceName: $"Inpainting ({eraseDevice})",
                latencyMs: eraseInferMs,
                customPowerWatts: power.Watts
            );

            LblStatus.Text = _localizer.CurrentLanguage == "en"
                ? $"✅ Object '{_lastTargetName}' erased successfully in {eraseInferMs} ms on {eraseDevice} (~{power.Watts:F1} W • {power.Joules:F3} J)! Background has been reconstructed."
                : $"✅ Objet '{_lastTargetName}' effacé avec succès en {eraseInferMs} ms sur {eraseDevice} (~{power.Watts:F1} W • {power.Joules:F3} J) ! L'arrière-plan a été reconstitué.";
        }
        else
        {
            LblStatus.Text = $"⚠️ Échec de l'effacement : {lastErr}";
        }
    }

    private void BtnCancelSelection_Click(object sender, RoutedEventArgs e)
    {
        BannerConfirm.Visibility = Visibility.Collapsed;
        _activeMaskPath = string.Empty;

        // Restore active image without cyan preview
        if (File.Exists(_currentImagePath))
        {
            DisplayImageFile(_currentImagePath);
        }
        LblStatus.Text = _localizer.Get("Eraser_SelectionCancelled");
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_historyStack.Count == 0) return;

        string previousState = _historyStack[^1];
        _historyStack.RemoveAt(_historyStack.Count - 1);

        if (File.Exists(previousState))
        {
            _currentImagePath = previousState;
            DisplayImageFile(previousState);
            BannerConfirm.Visibility = Visibility.Collapsed;

            BtnUndo.IsEnabled = _historyStack.Count > 0;
            if (_historyStack.Count > 0)
            {
                TxtHistoryCount.Text = string.Format(_localizer.Get("Eraser_ModificationsCount"), _historyStack.Count, _historyStack.Count > 1 ? "s" : "");
            }
            else
            {
                BadgeHistoryCount.Visibility = Visibility.Collapsed;
            }

            LblStatus.Text = _localizer.Get("Eraser_RollbackDone");
        }
    }

    private void BtnResetOriginal_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_originalImagePath) || !File.Exists(_originalImagePath)) return;

        if (_historyStack.Count > 0)
        {
            var res = MessageBox.Show(
                _localizer.Get("Eraser_ResetConfirmMsg"),
                _localizer.Get("Eraser_ResetConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            if (res != MessageBoxResult.Yes) return;
        }

        LoadImage(_originalImagePath, isNewSource: true);
        LblStatus.Text = _localizer.Get("Eraser_ResetDone");
    }

    private void BtnSaveImage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentImagePath) || !File.Exists(_currentImagePath)) return;

        var sfd = new SaveFileDialog
        {
            Title = _localizer.Get("Eraser_SaveDialogTitle"),
            FileName = $"retouche_magique_{DateTime.Now:yyyyMMdd_HHmmss}.jpg",
            Filter = "Image JPEG (*.jpg)|*.jpg|Image PNG (*.png)|*.png"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                File.Copy(_currentImagePath, sfd.FileName, overwrite: true);
                MessageBox.Show(_localizer.Get("Eraser_SaveSuccess"), _localizer.Get("Eraser_SaveTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        LblEraserTitle.Text = _localizer.Get("Eraser_Title");
        LblEraserSub.Text = _localizer.Get("Eraser_Subtitle");
        BtnImportImage.Content = _localizer.Get("Eraser_BtnImportImage");
        BtnSampleDesk.Content = _localizer.Get("Eraser_BtnSampleDesk");
        BtnSampleBeach.Content = _localizer.Get("Eraser_BtnSampleBeach");
        BtnSampleTie.Content = _localizer.Get("Eraser_BtnSampleTie");
        LblEraserSiliconLabel.Text = _localizer.Get("Eraser_LabelSilicon");
        LblEraserPromptLabel.Text = _localizer.Get("Eraser_LabelPrompt");
        BtnDetect.Content = _localizer.Get("Eraser_BtnDetect");
        LblEraserSuggestionsLabel.Text = _localizer.Get("Eraser_SuggestionsLabel");
        ChipCup.Content = _localizer.Get("Eraser_ChipCup");
        ChipPhone.Content = _localizer.Get("Eraser_ChipPhone");
        ChipTie.Content = _localizer.Get("Eraser_ChipTie");
        ChipLaptop.Content = _localizer.Get("Eraser_ChipLaptop");
        ChipTourists.Content = _localizer.Get("Eraser_ChipTourists");
        LblEraserClickHint.Text = _localizer.Get("Eraser_ClickHint");
        BtnConfirmErase.Content = _localizer.Get("Eraser_BtnConfirm");
        BtnCancelSelection.Content = _localizer.Get("Eraser_BtnCancel");
        LblConfirmSub.Text = _localizer.Get("Eraser_ConfirmSub");
        BtnUndo.Content = _localizer.Get("Eraser_BtnRollback");
        BtnResetOriginal.Content = _localizer.Get("Eraser_BtnReset");
        LblTechTitle.Text = _localizer.Get("Eraser_TechTitle");
        LblTechDesc.Text = _localizer.Get("Eraser_TechDesc");
        if (TxtDocHelp != null) TxtDocHelp.Text = _localizer.CurrentLanguage == "en" ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = _localizer.CurrentLanguage == "en" 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = _localizer.CurrentLanguage == "en"
                ? "Models: YOLOv8n-Seg (Segmentation) + Inpainting"
                : "Modèles : YOLOv8n-Seg (Segmentation) + Inpainting";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = _localizer.CurrentLanguage == "en"
                    ? "YOLOv8n-Seg object segmentation (OpenVINO IR / ONNX / Olive) combined with background reconstruction."
                    : "Segmentation d'objets YOLOv8n-Seg (OpenVINO IR / ONNX / Olive) combinée à la reconstruction d'arrière-plan.";
            }
        }

        if (TxtPrompt.Text == "enlève la tasse" || TxtPrompt.Text == "remove the cup")
        {
            TxtPrompt.Text = _localizer.Get("Eraser_DefaultPromptDesk");
        }

        if (string.IsNullOrWhiteSpace(LblStatus.Text) ||
            LblStatus.Text.StartsWith("Prêt") ||
            LblStatus.Text.StartsWith("Ready") ||
            LblStatus.Text.StartsWith("Photo prête") ||
            LblStatus.Text.StartsWith("Photo ready"))
        {
            LblStatus.Text = _localizer.Get("Eraser_StatusInitial");
        }

        if (!string.IsNullOrEmpty(_currentImagePath) && File.Exists(_currentImagePath))
        {
            try
            {
                var bmp = new BitmapImage(new Uri(_currentImagePath, UriKind.Absolute));
                LblCanvasInfo.Text = string.Format(_localizer.Get("Eraser_CanvasInfo"), System.IO.Path.GetFileName(_currentImagePath), bmp.PixelWidth, bmp.PixelHeight);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        try { _telemetryService.Dispose(); } catch { }
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
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_eraser", _localizer?.CurrentLanguage);
    }
}
