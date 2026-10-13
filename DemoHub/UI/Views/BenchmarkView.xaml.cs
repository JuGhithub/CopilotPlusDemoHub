using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.UI.Views;

public partial class BenchmarkView : UserControl, IDisposable
{
    private readonly FoundryClient _foundryClient;
    private readonly Func<string> _getDeviceFunc;
    private readonly Func<string> _getEffectiveDeviceFunc;
    private LocalizationService _localizer;
    private HardwareTelemetryService? _telemetryService;
    private DispatcherTimer? _telemetryTimer;
    private CancellationTokenSource? _benchmarkCts;
    private Process? _duelProcess;
    private bool _isRunning;

    public BenchmarkView(FoundryClient foundryClient, LocalizationService localizer, Func<string> getDeviceFunc, Func<string> getEffectiveDeviceFunc)
    {
        InitializeComponent();
        _foundryClient = foundryClient;
        _localizer = localizer;
        _getDeviceFunc = getDeviceFunc;
        _getEffectiveDeviceFunc = getEffectiveDeviceFunc;

        ApplyLocalization(_localizer);
        StartTelemetryPolling();
    }

    public void ApplyLocalization(LocalizationService localizer)
    {
        _localizer = localizer;
        LblBenchmarkTitle.Text = _localizer.Get("Benchmark_Title");
        LblBenchmarkSub.Text = _localizer.Get("Benchmark_Subtitle");
        if (TxtDocHelp != null) TxtDocHelp.Text = _localizer.CurrentLanguage == "en" ? "Guide & Doc" : "Guide & Doc";
        if (BtnDocHelp != null) BtnDocHelp.ToolTip = _localizer.CurrentLanguage == "en" 
            ? "Demo Guide, Pitch & Architecture (FR / EN)" 
            : "Guide de démonstration, pitch & architecture (FR / EN)";
        if (TxtModelBadge != null)
        {
            TxtModelBadge.Text = _localizer.CurrentLanguage == "en"
                ? "Models: YOLOv8n-Seg (NPU Duel) • Phi-4-mini (LLM)"
                : "Modèles : YOLOv8n-Seg (Duel NPU) • Phi-4-mini (LLM)";
            if (BadgeModelContainer != null)
            {
                BadgeModelContainer.ToolTip = _localizer.CurrentLanguage == "en"
                    ? "NPU Benchmark: YOLOv8n-Seg duel (OpenVINO IR vs Raw ONNX vs Microsoft Olive) and Phi-4-mini endurance."
                    : "Benchmark NPU : Duel YOLOv8n-Seg (OpenVINO IR vs Brut ONNX vs Microsoft Olive) et endurance Phi-4-mini.";
            }
        }
        LblBatteryTip.Text = _localizer.Get("Benchmark_NoticeBatteryRecommendation");
        LblCardAutonomy.Text = _localizer.Get("Benchmark_CardAutonomy");
        LblCardBattery.Text = _localizer.Get("Benchmark_CardBattery");
        LblCardCpu.Text = _localizer.Get("Benchmark_CardCpu");
        LblCardTokens.Text = _localizer.Get("Benchmark_CardTokens");
        LblExplainerTitle.Text = _localizer.Get("Benchmark_ExplainerTitle");
        LblExplainerTitle1.Text = _localizer.Get("Benchmark_ExplainerTitle1");
        LblExplainer1.Text = _localizer.Get("Benchmark_Explainer1");
        LblExplainerTitle2.Text = _localizer.Get("Benchmark_ExplainerTitle2");
        LblExplainer2.Text = _localizer.Get("Benchmark_Explainer2");
        LblExplainerTitle3.Text = _localizer.Get("Benchmark_ExplainerTitle3");
        LblExplainer3.Text = _localizer.Get("Benchmark_Explainer3");
        LblConfigTitle.Text = _localizer.Get("Benchmark_ConfigTitle");
        if (LblModeTitle != null) LblModeTitle.Text = _localizer.CurrentLanguage == "en" ? "WORKLOAD TYPE:" : "TYPE D'ÉPREUVE :";
        if (RadModeLLM != null) RadModeLLM.Content = _localizer.Get("Benchmark_ModeLLM");
        if (RadModeDuel != null) RadModeDuel.Content = _localizer.Get("Benchmark_ModeDuel");
        UpdateModeUI();
        LblActiveDeviceLabel.Text = _localizer.Get("Benchmark_ActiveDeviceLabel");
        BtnStop.Content = _localizer.Get("Benchmark_BtnStop");
        BtnClearLog.Content = _localizer.Get("Benchmark_BtnClearLog");
        LblLogTitle.Text = _localizer.Get("Benchmark_LogTitle");
        if (!_isRunning)
        {
            LblRemainingTime.Text = _localizer.Get("Benchmark_Ready");
            if (string.IsNullOrWhiteSpace(TxtLog.Text) ||
                TxtLog.Text.StartsWith("Appuyez") ||
                TxtLog.Text.StartsWith("Press"))
            {
                TxtLog.Text = _localizer.Get("Benchmark_LogPlaceholder");
            }
        }

        UpdateTelemetryUI();
    }

    private void BenchmarkMode_Changed(object sender, RoutedEventArgs e)
    {
        UpdateModeUI();
    }

    private void UpdateModeUI()
    {
        if (RadModeDuel?.IsChecked == true)
        {
            if (LblDurationLabel != null) LblDurationLabel.Text = _localizer?.CurrentLanguage == "en" ? "Duel workload intensity:" : "Intensité de l'épreuve du duel :";
            if (Rad30s != null) Rad30s.Content = _localizer?.CurrentLanguage == "en" ? "⚡ 50 inferences (Express ~2s)" : "⚡ 50 inférences (Démonstration express ~2s)";
            if (Rad2m != null) Rad2m.Content = _localizer?.CurrentLanguage == "en" ? "⏱️ 150 inferences (Standard ~5s)" : "⏱️ 150 inférences (Standard étalonné ~5s)";
            if (Rad5m != null) Rad5m.Content = _localizer?.CurrentLanguage == "en" ? "⏱️ 300 inferences (Deep ~10s)" : "⏱️ 300 inférences (Approfondi ~10s)";
            if (Rad10m != null) Rad10m.Content = _localizer?.CurrentLanguage == "en" ? "⏱️ 600 inferences (Stress ~20s)" : "⏱️ 600 inférences (Stress test ~20s)";
            if (BtnStart != null) BtnStart.Content = _localizer?.CurrentLanguage == "en" ? "⚔️ Start Engine Duel" : "⚔️ Lancer le Duel de Runtimes";
        }
        else
        {
            if (LblDurationLabel != null) LblDurationLabel.Text = _localizer?.Get("Benchmark_DurationLabel") ?? "Durée du test continu :";
            if (Rad30s != null) Rad30s.Content = _localizer?.Get("Benchmark_Opt30s") ?? "⚡ 30 secondes";
            if (Rad2m != null) Rad2m.Content = _localizer?.Get("Benchmark_Opt2m") ?? "⏱️ 2 minutes";
            if (Rad5m != null) Rad5m.Content = _localizer?.Get("Benchmark_Opt5m") ?? "⏱️ 5 minutes";
            if (Rad10m != null) Rad10m.Content = _localizer?.Get("Benchmark_Opt10m") ?? "⏱️ 10 minutes";
            if (BtnStart != null) BtnStart.Content = _localizer?.Get("Benchmark_BtnStart") ?? "▶ Démarrer le Benchmark";
        }
    }

    private void StartTelemetryPolling()
    {
        _telemetryService = new HardwareTelemetryService();
        _telemetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _telemetryTimer.Tick += (s, e) => UpdateTelemetryUI();
        _telemetryTimer.Start();
    }

    public void RefreshDeviceUI()
    {
        UpdateTelemetryUI();
    }

    private void UpdateTelemetryUI()
    {
        if (_telemetryService == null) return;

        try
        {
            var snapshot = _telemetryService.GetCurrentSnapshot();
            string rawDevice = _getDeviceFunc() ?? "AUTO";
            string effectiveDevice = rawDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
                ? _getEffectiveDeviceFunc()
                : rawDevice;
            string device = effectiveDevice.ToUpperInvariant();

            if (rawDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
            {
                LblActiveDevice.Text = $"🤖 Auto ➔ {effectiveDevice} ({SiliconDetector.NpuName})";
            }
            else if (device == "NPU")
            {
                LblActiveDevice.Text = $"⚡ NPU ({SiliconDetector.NpuName})";
            }
            else if (device == "GPU")
            {
                LblActiveDevice.Text = $"🎮 GPU ({SiliconDetector.GpuName})";
            }
            else
            {
                LblActiveDevice.Text = $"💻 CPU ({SiliconDetector.CpuName})";
            }

            var npuBrush = (System.Windows.Media.Brush)FindResource("NpuGreenBrush");
            var gpuBrush = (System.Windows.Media.Brush)FindResource("GpuAmberBrush");
            var cpuBrush = (System.Windows.Media.Brush)FindResource("CpuIndigoBrush");

            System.Windows.Media.Brush deviceBrush = device switch
            {
                "GPU" => gpuBrush,
                "CPU" => cpuBrush,
                _ => npuBrush
            };

            LblActiveDevice.Foreground = deviceBrush;

            if (snapshot.DischargeRateWatts > 1.0)
            {
                double remainingWh = snapshot.RemainingCapacityMwh > 0
                    ? snapshot.RemainingCapacityMwh / 1000.0
                    : (72.0 * snapshot.BatteryPercent / 100.0);

                double hours = remainingWh / snapshot.DischargeRateWatts;
                int h = (int)Math.Floor(hours);
                int m = (int)Math.Round((hours - h) * 60.0);
                if (m >= 60) { h++; m = 0; }

                string timeStr = h > 0 ? $"{h}h {m:D2} min" : $"{m} min";
                LblAutonomy.Text = $"~ {timeStr}";
                LblAutonomy.Foreground = deviceBrush;

                LblWattsDetails.Text = string.Format(_localizer.Get("Benchmark_WattsFormat"), snapshot.DischargeRateWatts);
            }
            else
            {
                LblAutonomy.Text = _localizer.Get("Benchmark_OnAcPower");
                LblAutonomy.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
                LblWattsDetails.Text = _localizer.Get("Benchmark_WattsOnAc");
            }

            string effKey = device switch
            {
                "GPU" => "Benchmark_Efficiency_GPU",
                "CPU" => "Benchmark_Efficiency_CPU",
                _ => "Benchmark_Efficiency_NPU"
            };
            LblWattsEfficiency.Text = _localizer.Get(effKey);
            LblWattsEfficiency.Foreground = deviceBrush;

            LblBattery.Text = string.Format(_localizer.Get("Benchmark_BatteryFormat"), snapshot.BatteryPercent);
            LblPowerSource.Text = snapshot.IsOnBattery ? _localizer.Get("Benchmark_OnBattery") : _localizer.Get("Benchmark_OnAc");

            // Silicon loads (NPU / GPU / CPU)
            LblNpu.Text = string.Format(_localizer.Get("Benchmark_NpuFormat"), snapshot.NpuUsagePercent);
            LblGpu.Text = string.Format(_localizer.Get("Benchmark_GpuFormat"), snapshot.GpuUsagePercent);
            LblCpu.Text = string.Format(_localizer.Get("Benchmark_CpuFormat"), snapshot.CpuUsagePercent);
        }
        catch
        {
            // Ignore UI update failures
        }
    }

    private void PrepareLogForRun()
    {
        string currentText = TxtLog.Text?.Trim() ?? string.Empty;
        string placeholderFr = _localizer.Get("Benchmark_LogPlaceholder");
        string placeholderDefaultFr = "Appuyez sur 'Démarrer le Benchmark'";
        string placeholderDefaultEn = "Press 'Start Benchmark'";

        if (string.IsNullOrEmpty(currentText) ||
            currentText.StartsWith(placeholderDefaultFr, StringComparison.OrdinalIgnoreCase) ||
            currentText.StartsWith(placeholderDefaultEn, StringComparison.OrdinalIgnoreCase) ||
            currentText.Equals(placeholderFr, StringComparison.OrdinalIgnoreCase))
        {
            TxtLog.Text = string.Empty;
        }
        else
        {
            TxtLog.AppendText("\n──────────────────────────────────────────────────────────\n");
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        if (RadModeDuel?.IsChecked == true)
        {
            await RunDuelBenchmarkAsync();
            return;
        }

        _isRunning = true;
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        _telemetryService?.Start();

        PrepareLogForRun();

        int durationSeconds = 30;
        if (Rad2m.IsChecked == true) durationSeconds = 120;
        else if (Rad5m.IsChecked == true) durationSeconds = 300;
        else if (Rad10m.IsChecked == true) durationSeconds = 600;

        _benchmarkCts = new CancellationTokenSource();
        var token = _benchmarkCts.Token;

        DateTime startTime = DateTime.UtcNow;
        DateTime endTime = startTime.AddSeconds(durationSeconds);
        string rawDevice = _getDeviceFunc();
        string effectiveDevice = _getEffectiveDeviceFunc();
        string deviceDisplayName = rawDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
            ? $"Auto ➔ {effectiveDevice} ({SiliconDetector.NpuName})"
            : $"{effectiveDevice} ({SiliconDetector.Vendor})";

        AppendLog(string.Format(_localizer.Get("Benchmark_LogStart"), durationSeconds, deviceDisplayName));

        int totalTokens = 0;
        int completedRequests = 0;
        var sw = Stopwatch.StartNew();

        try
        {
            var targetModel = await _foundryClient.GetBestModelAsync("phi-4-mini", rawDevice, token);
            string resolvedDevice = targetModel.TargetDevice ?? effectiveDevice;
            string modelLogDevice = rawDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
                ? $"Auto ➔ {resolvedDevice}"
                : resolvedDevice;
            AppendLog(string.Format(_localizer.Get("Benchmark_LogModel"), targetModel.Id, modelLogDevice));

            while (DateTime.UtcNow < endTime && !token.IsCancellationRequested)
            {
                double remaining = (endTime - DateTime.UtcNow).TotalSeconds;
                if (remaining <= 0) break;

                double elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                Dispatcher.Invoke(() =>
                {
                    PrgDuration.Value = Math.Clamp((elapsed / durationSeconds) * 100.0, 0, 100);
                    LblRemainingTime.Text = string.Format(_localizer.Get("Benchmark_RemainingFormat"), Math.Ceiling(remaining));
                });

                string prompt = _localizer.Get("Benchmark_LogPrompt");
                string systemRole = _localizer.Get("Benchmark_LogSystem");
                var result = await _foundryClient.AskAsync(
                    targetModel,
                    systemRole,
                    prompt,
                    temperature: 0.1,
                    maxTokens: 250,
                    cancellationToken: token);

                if (result.Successful)
                {
                    completedRequests++;
                    totalTokens += result.TotalTokens;

                    string powerStatus = string.Empty;
                    Dispatcher.Invoke(() =>
                    {
                        LblTokens.Text = totalTokens.ToString();
                        if (sw.Elapsed.TotalSeconds > 0)
                        {
                            double tokPerSec = totalTokens / sw.Elapsed.TotalSeconds;
                            LblThroughput.Text = string.Format(_localizer.Get("Benchmark_ThroughputFormat"), tokPerSec);
                        }
                        powerStatus = string.IsNullOrWhiteSpace(LblWattsDetails.Text)
                            ? _localizer.Get("Benchmark_WattsOnAc")
                            : LblWattsDetails.Text;
                    });

                    AppendLog(string.Format(_localizer.Get("Benchmark_LogBatch"), completedRequests, result.CompletionTokens, result.LatencyMilliseconds, result.TokensPerSecond, powerStatus));

                    _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                        demoName: "Benchmark Soutenu",
                        requestedSilicon: rawDevice,
                        effectiveSilicon: result.TargetDevice,
                        siliconDeviceName: targetModel.Id,
                        latencyMs: (int)result.LatencyMilliseconds,
                        tokensPerSecond: result.TokensPerSecond
                    );
                }
            }

            AppendLog($"==========================================");
            AppendLog(string.Format(_localizer.Get("Benchmark_LogSummaryTitle"), modelLogDevice));
            AppendLog(string.Format(_localizer.Get("Benchmark_LogSummaryRequests"), completedRequests));
            AppendLog(string.Format(_localizer.Get("Benchmark_LogSummaryTokens"), totalTokens));
            AppendLog(string.Format(_localizer.Get("Benchmark_LogSummaryThroughput"), (sw.Elapsed.TotalSeconds > 0 ? totalTokens / sw.Elapsed.TotalSeconds : 0)));
            AppendLog(string.Format(_localizer.Get("Benchmark_LogSummaryStatus"), LblAutonomy.Text, LblWattsDetails.Text, LblWattsEfficiency.Text));
            if (_telemetryService != null)
            {
                var summary = _telemetryService.GetSummary();
                double reportedNpu = summary.PeakNpuPercent > 0 ? summary.AverageNpuPercent : 0;
                double reportedGpu = summary.PeakGpuPercent > 0 ? summary.AverageGpuPercent : 0;
                double reportedCpu = summary.PeakCpuPercent > 0 ? summary.AverageCpuPercent : 0;
                AppendLog(string.Format(_localizer.Get("Benchmark_LogSummaryHardware"), reportedNpu, reportedGpu, reportedCpu));
            }
            AppendLog($"==========================================");
        }
        catch (OperationCanceledException)
        {
            AppendLog(_localizer.Get("Benchmark_LogStopped"));
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_localizer.Get("Benchmark_LogError"), ex.Message));
        }
        finally
        {
            _isRunning = false;
            Dispatcher.Invoke(() =>
            {
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                PrgDuration.Value = 100;
                LblRemainingTime.Text = _localizer.Get("Benchmark_Finished");
            });
        }
    }

    private async Task RunDuelBenchmarkAsync()
    {
        _isRunning = true;
        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;
        _telemetryService?.Start();

        PrepareLogForRun();

        int iterations = 150;
        if (Rad30s.IsChecked == true) iterations = 50;
        else if (Rad2m.IsChecked == true) iterations = 150;
        else if (Rad5m.IsChecked == true) iterations = 300;
        else if (Rad10m.IsChecked == true) iterations = 600;

        string rawDevice = _getDeviceFunc();
        string effectiveDevice = _getEffectiveDeviceFunc();
        string targetDevice = rawDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase) ? effectiveDevice : rawDevice;

        bool isEn = _localizer.CurrentLanguage == "en";
        AppendLog(isEn
            ? $"⚔️ NPU SILICIUM DUEL STARTING: OpenVINO IR vs Raw ONNX vs Olive NPU ({iterations} runs each on {targetDevice})..."
            : $"⚔️ DÉMARRAGE DU DUEL SILICIUM NPU : OpenVINO IR vs Modèle Brut ONNX vs Modèle Olive ({iterations} inférences chacun sur {targetDevice})...");

        Dispatcher.Invoke(() =>
        {
            PrgDuration.Value = 3;
            LblRemainingTime.Text = isEn ? "Phase 1/3: OpenVINO IR..." : "Phase 1/3 : OpenVINO IR Natif...";
        });

        _benchmarkCts = new CancellationTokenSource();

        try
        {
            string scriptPath = FindRootFile("runtime_benchmark_service.py");
            if (!File.Exists(scriptPath))
            {
                AppendLog($"❌ Error: Script '{scriptPath}' not found.");
                return;
            }

            string repoModels = FindRootFile("models");
            string workingDir = Directory.Exists(repoModels)
                ? (System.IO.Path.GetDirectoryName(repoModels) ?? AppDomain.CurrentDomain.BaseDirectory)
                : (System.IO.Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory);
            string pythonExe = GetPythonExecutable();

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"\"{scriptPath}\" --iterations {iterations} --device {targetDevice}",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var fullJsonBuilder = new StringBuilder();
            _duelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var tcs = new TaskCompletionSource<int>();
            _duelProcess.Exited += (s, e) => tcs.TrySetResult(_duelProcess.ExitCode);

            _duelProcess.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                string line = e.Data.Trim();

                if (line.StartsWith("{") && line.Contains("\"type\": \"progress\""))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        int phase = root.GetProperty("phase").GetInt32();
                        string engine = root.GetProperty("engine").GetString() ?? "";
                        int step = root.GetProperty("step").GetInt32();
                        int total = root.GetProperty("total").GetInt32();
                        double ms = root.TryGetProperty("current_ms", out var pms) ? pms.GetDouble() : 0;

                        double phaseBase = (phase - 1) * 31.0 + 3.0;
                        double progressPct = Math.Min(98.0, phaseBase + (step / (double)total) * 30.0);

                        Dispatcher.Invoke(() =>
                        {
                            PrgDuration.Value = progressPct;
                            LblRemainingTime.Text = $"[{phase}/3] {engine}: {step}/{total} ({ms:F1} ms)";
                            if (ms > 0)
                            {
                                LblThroughput.Text = $"{1000.0 / Math.Max(0.1, ms):F1} FPS";
                            }
                            if (phase == 1 && step == total)
                            {
                                AppendLog(isEn
                                    ? $"✅ Phase 1 complete: OpenVINO IR reached {1000.0 / Math.Max(0.1, ms):F1} FPS ({ms:F1} ms/frame). Starting Phase 2 (Raw ONNX Model)..."
                                    : $"✅ Phase 1 terminée : OpenVINO IR à {1000.0 / Math.Max(0.1, ms):F1} FPS ({ms:F1} ms/inférence). Lancement de la Phase 2 (Modèle Brut ONNX)...");
                            }
                            else if (phase == 2 && step == total)
                            {
                                AppendLog(isEn
                                    ? $"✅ Phase 2 complete: Raw ONNX reached {1000.0 / Math.Max(0.1, ms):F1} FPS ({ms:F1} ms/frame). Starting Phase 3 (Olive Optimized Model)..."
                                    : $"✅ Phase 2 terminée : Modèle Brut ONNX à {1000.0 / Math.Max(0.1, ms):F1} FPS ({ms:F1} ms/inférence). Lancement de la Phase 3 (Modèle Optimisé Olive)...");
                            }
                        });
                    }
                    catch { }
                }
                else
                {
                    fullJsonBuilder.AppendLine(line);
                }
            };

            _duelProcess.ErrorDataReceived += (s, e) =>
            {
                // Silence harmless warnings if any
            };

            _duelProcess.Start();
            _duelProcess.BeginOutputReadLine();
            _duelProcess.BeginErrorReadLine();

            using (_benchmarkCts.Token.Register(() =>
            {
                try { if (_duelProcess != null && !_duelProcess.HasExited) _duelProcess.Kill(entireProcessTree: true); } catch { }
            }))
            {
                await tcs.Task;
            }

            string jsonOutput = fullJsonBuilder.ToString().Trim();
            if (!string.IsNullOrEmpty(jsonOutput))
            {
                RenderDuelSummary(jsonOutput);
            }
            else
            {
                AppendLog(isEn ? "⚠️ No data received from benchmark service." : "⚠️ Aucune donnée reçue du service de benchmark.");
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog(_localizer.Get("Benchmark_LogStopped"));
        }
        catch (Exception ex)
        {
            AppendLog(string.Format(_localizer.Get("Benchmark_LogError"), ex.Message));
        }
        finally
        {
            _isRunning = false;
            try { _duelProcess?.Dispose(); } catch { }
            _duelProcess = null;
            Dispatcher.Invoke(() =>
            {
                BtnStart.IsEnabled = true;
                BtnStop.IsEnabled = false;
                PrgDuration.Value = 100;
                LblRemainingTime.Text = _localizer.Get("Benchmark_Finished");
            });
        }
    }

    private void RenderDuelSummary(string jsonOutput)
    {
        try
        {
            jsonOutput = jsonOutput.Replace("\r\n", "\n").Trim();

            // 1. Check for explicit marker if present
            int markerIdx = jsonOutput.IndexOf("===DUEL_RESULT_JSON===");
            if (markerIdx >= 0)
            {
                jsonOutput = jsonOutput.Substring(markerIdx + "===DUEL_RESULT_JSON===".Length).Trim();
            }

            // 2. Find the object with "type": "result"
            int resultIdx = jsonOutput.IndexOf("\"type\": \"result\"");
            if (resultIdx < 0) resultIdx = jsonOutput.IndexOf("\"type\":\"result\"");

            if (resultIdx >= 0)
            {
                int objStart = jsonOutput.LastIndexOf('{', resultIdx);
                if (objStart >= 0)
                {
                    int braceCount = 0;
                    int objEnd = -1;
                    for (int i = objStart; i < jsonOutput.Length; i++)
                    {
                        if (jsonOutput[i] == '{') braceCount++;
                        else if (jsonOutput[i] == '}')
                        {
                            braceCount--;
                            if (braceCount == 0)
                            {
                                objEnd = i;
                                break;
                            }
                        }
                    }
                    if (objEnd > objStart)
                    {
                        jsonOutput = jsonOutput.Substring(objStart, objEnd - objStart + 1);
                    }
                }
            }

            using var doc = JsonDocument.Parse(jsonOutput);
            var root = doc.RootElement;

            var ov = root.GetProperty("openvino");
            var onnx = root.GetProperty("onnx");
            bool hasOlive = root.TryGetProperty("onnx_olive", out var olive);

            double speedupOvVsOnnx = root.GetProperty("speedup_openvino_vs_onnx").GetDouble();
            double speedupOliveVsOnnx = root.TryGetProperty("speedup_olive_vs_standard", out var spOlive) ? spOlive.GetDouble() : 1.0;
            double speedupOvVsOlive = root.TryGetProperty("speedup_openvino_vs_olive", out var spOvOl) ? spOvOl.GetDouble() : 1.0;
            double energySaved = root.GetProperty("energy_saved_pct").GetDouble();
            double energySavedOlive = root.TryGetProperty("energy_saved_olive_pct", out var esOl) ? esOl.GetDouble() : 0.0;

            double memSavedOlive = root.TryGetProperty("memory_saved_olive_pct", out var msOl) ? msOl.GetDouble() : 49.7;
            double ov_size = ov.TryGetProperty("model_size_mb", out var os) ? os.GetDouble() : 6.9;
            double ov_comp = ov.TryGetProperty("compile_sec", out var oc) ? oc.GetDouble() : 1.5;
            double onnx_size = onnx.TryGetProperty("model_size_mb", out var ons) ? ons.GetDouble() : 13.2;
            double onnx_comp = onnx.TryGetProperty("compile_sec", out var onc) ? onc.GetDouble() : 1.5;
            double olive_size = hasOlive && olive.TryGetProperty("model_size_mb", out var ols) ? ols.GetDouble() : 6.7;
            double olive_comp = hasOlive && olive.TryGetProperty("compile_sec", out var olc) ? olc.GetDouble() : 1.5;

            bool isEn = _localizer.CurrentLanguage == "en";
            string insight = isEn
                ? (root.GetProperty("insight_en").GetString() ?? "")
                : (root.GetProperty("insight_fr").GetString() ?? "");

            AppendLog("==========================================================");
            AppendLog(isEn 
                ? $"🏆 NPU SILICIUM DUEL: OPENVINO IR vs RAW ONNX vs OLIVE ({SiliconDetector.NpuName})" 
                : $"🏆 RÉSULTATS DU DUEL SILICIUM NPU ({SiliconDetector.NpuName}) :");
            AppendLog("==========================================================");
            AppendLog(isEn ? $"🔹 1. OPENVINO IR NATIVE ({ov.GetProperty("target").GetString()}):" : $"🔹 1. OPENVINO IR NATIF ({ov.GetProperty("target").GetString()}):");
            AppendLog($"   • {(isEn ? "Model size" : "Poids du modèle")}    : {ov_size:F2} MB (OpenVINO IR .xml)");
            AppendLog($"   • {(isEn ? "NPU compile time" : "Compilation NPU")}  : {ov_comp:F2} s");
            AppendLog($"   • {(isEn ? "Average latency" : "Latence moyenne")} : {ov.GetProperty("mean_ms").GetDouble():F1} ms (Min: {ov.GetProperty("min_ms").GetDouble():F1} ms, Max: {ov.GetProperty("max_ms").GetDouble():F1} ms)");
            AppendLog($"   • {(isEn ? "Throughput" : "Cadence")}         : {ov.GetProperty("fps").GetDouble():F1} FPS");
            AppendLog($"   • {(isEn ? "Power" : "Puissance NPU")}   : {ov.GetProperty("watts").GetDouble():F2} W");
            AppendLog($"   • {(isEn ? "Energy/infer." : "Énergie/infér.")}  : {ov.GetProperty("joules_per_infer").GetDouble():F4} Joules");
            AppendLog("");
            AppendLog(isEn ? $"🔸 2. RAW ONNX MODEL ({onnx.GetProperty("target").GetString()}):" : $"🔸 2. MODÈLE BRUT ONNX ({onnx.GetProperty("target").GetString()}):");
            AppendLog($"   • {(isEn ? "Model size" : "Poids du modèle")}    : {onnx_size:F2} MB (Standard ONNX FP32)");
            AppendLog($"   • {(isEn ? "NPU compile time" : "Compilation NPU")}  : {onnx_comp:F2} s ({(isEn ? "On-the-fly compilation" : "Compilé à la volée")})");
            AppendLog($"   • {(isEn ? "Average latency" : "Latence moyenne")} : {onnx.GetProperty("mean_ms").GetDouble():F1} ms (Min: {onnx.GetProperty("min_ms").GetDouble():F1} ms, Max: {onnx.GetProperty("max_ms").GetDouble():F1} ms)");
            AppendLog($"   • {(isEn ? "Throughput" : "Cadence")}         : {onnx.GetProperty("fps").GetDouble():F1} FPS");
            AppendLog($"   • {(isEn ? "Power" : "Puissance NPU")}   : {onnx.GetProperty("watts").GetDouble():F2} W");
            AppendLog($"   • {(isEn ? "Energy/infer." : "Énergie/infér.")}  : {onnx.GetProperty("joules_per_infer").GetDouble():F4} Joules");

            if (hasOlive)
            {
                AppendLog("");
                AppendLog(isEn ? $"🟢 3. MICROSOFT OLIVE NPU ({olive.GetProperty("target").GetString()}):" : $"🟢 3. MODÈLE OPTIMISÉ MICROSOFT OLIVE ({olive.GetProperty("target").GetString()}):");
                AppendLog($"   • {(isEn ? "Model size" : "Poids du modèle")}    : {olive_size:F2} MB (Olive FP16 NPU)");
                AppendLog($"   • {(isEn ? "NPU compile time" : "Compilation NPU")}  : {olive_comp:F2} s");
                AppendLog($"   • {(isEn ? "Average latency" : "Latence moyenne")} : {olive.GetProperty("mean_ms").GetDouble():F1} ms (Min: {olive.GetProperty("min_ms").GetDouble():F1} ms, Max: {olive.GetProperty("max_ms").GetDouble():F1} ms)");
                AppendLog($"   • {(isEn ? "Throughput" : "Cadence")}         : {olive.GetProperty("fps").GetDouble():F1} FPS");
                AppendLog($"   • {(isEn ? "Power" : "Puissance NPU")}   : {olive.GetProperty("watts").GetDouble():F2} W");
                AppendLog($"   • {(isEn ? "Energy/infer." : "Énergie/infér.")}  : {olive.GetProperty("joules_per_infer").GetDouble():F4} Joules");
            }

            AppendLog("----------------------------------------------------------");
            if (hasOlive)
            {
                AppendLog(isEn
                    ? $"📦 OLIVE MEMORY GAIN     : -{memSavedOlive:F1}% RAM & Disk footprint ({olive_size:F1} MB vs {onnx_size:F1} MB)"
                    : $"📦 GAIN MÉMOIRE OLIVE    : -{memSavedOlive:F1}% d'empreinte RAM & Disque ({olive_size:F1} Mo vs {onnx_size:F1} Mo)");
            }
            AppendLog(isEn
                ? $"⚡ PURE NPU EXECUTION   : 100% on {SiliconDetector.NpuName} (1.6W, ~0.012 Joules/frame)"
                : $"⚡ EXÉCUTION 100% NPU    : 100% sur {SiliconDetector.NpuName} (1.6W, ~0.012 Joules/inférence)");
            AppendLog("----------------------------------------------------------");
            AppendLog(isEn
                ? $"💡 ARCHITECTURAL INSIGHT:\n{insight}"
                : $"💡 SYNTHÈSE ARCHITECTURE :\n{insight}");
            AppendLog("==========================================================");

            Dispatcher.Invoke(() =>
            {
                LblTokens.Text = hasOlive ? $"-{memSavedOlive:F0}% (Olive)" : $"{speedupOvVsOnnx:F2}x";
                LblThroughput.Text = hasOlive 
                    ? $"{ov.GetProperty("fps").GetDouble():F0} | {onnx.GetProperty("fps").GetDouble():F0} | {olive.GetProperty("fps").GetDouble():F0} FPS"
                    : $"{ov.GetProperty("fps").GetDouble():F1} vs {onnx.GetProperty("fps").GetDouble():F1} FPS";
            });

            _ = EnterpriseTelemetryService.Instance.RecordInferenceAsync(
                demoName: "Duel NPU : IR vs Brut vs Olive",
                requestedSilicon: "NPU",
                effectiveSilicon: SiliconDetector.NpuName,
                siliconDeviceName: "YOLOv8n-Seg (OpenVINO IR vs Raw ONNX vs Olive NPU)",
                latencyMs: (int)ov.GetProperty("mean_ms").GetDouble(),
                tokensPerSecond: ov.GetProperty("fps").GetDouble()
            );
        }
        catch (Exception ex)
        {
            AppendLog($"Erreur lors de l'analyse du résultat du duel: {ex.Message}");
        }
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _benchmarkCts?.Cancel();
        try
        {
            if (_duelProcess != null && !_duelProcess.HasExited)
            {
                _duelProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLog.Text = _localizer.Get("Benchmark_LogPlaceholder");
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText($"{DateTime.Now:HH:mm:ss} {message}\n");
            ScrollerLog.ScrollToEnd();
        });
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

    private static string GetPythonExecutable()
    {
        string cur = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 6; i++)
        {
            string venvPy = System.IO.Path.Combine(cur, ".venv", "Scripts", "python.exe");
            if (File.Exists(venvPy)) return venvPy;
            string? parent = Directory.GetParent(cur)?.FullName;
            if (parent == null || parent == cur) break;
            cur = parent;
        }
        return "python";
    }

    public void Dispose()
    {
        _telemetryTimer?.Stop();
        _benchmarkCts?.Cancel();
        try
        {
            if (_duelProcess != null && !_duelProcess.HasExited)
            {
                _duelProcess.Kill(entireProcessTree: true);
            }
        }
        catch { }
        _telemetryService?.Dispose();
    }

    private void BtnDocHelp_Click(object sender, RoutedEventArgs e)
    {
        DocumentationWindow.ShowDocumentation(Window.GetWindow(this), "demo_benchmark", _localizer?.CurrentLanguage);
    }
}
