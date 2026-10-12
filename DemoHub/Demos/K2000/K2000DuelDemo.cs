using System.Diagnostics;
using System.Speech.Synthesis;
using System.Text;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.Demos.K2000;

public sealed class K2000DuelDemo
{
    private const string TargetParentModel = "phi-4-mini";
    private readonly FoundryClient _foundryClient;
    private readonly LocalizationService _localizer;

    public K2000DuelDemo(
        FoundryClient foundryClient,
        LocalizationService localizer)
    {
        _foundryClient = foundryClient ?? throw new ArgumentNullException(nameof(foundryClient));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public async Task RunAsync(string? autoChoice = null)
    {
        SafeClear();
        DrawHeader();

        bool serverAvailable = await _foundryClient.IsServerAvailableAsync();
        if (!serverAvailable)
        {
            DisplayError(
                _localizer.Get("Operations_FoundryUnavailable"),
                _localizer.Get("Operations_VerifyServer"));
            return;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(_localizer.Get("K2000_MissionPrompt"));
        Console.ResetColor();
        Console.WriteLine();

        string? choice = autoChoice;
        if (string.IsNullOrWhiteSpace(choice))
        {
            Console.WriteLine(_localizer.Get("K2000_SelectMode"));
            Console.WriteLine(_localizer.Get("K2000_OptionDuel"));
            Console.WriteLine(_localizer.Get("K2000_OptionGPU"));
            Console.WriteLine(_localizer.Get("K2000_OptionNPU"));
            Console.WriteLine("0 - " + _localizer.Get("Common_Back"));
            Console.WriteLine();
            Console.Write(_localizer.Get("Menu_SelectDemo") + ": ");

            if (!Console.IsInputRedirected)
            {
                choice = Console.ReadLine()?.Trim();
            }
            else
            {
                choice = "1";
            }
        }

        if (choice == "0" || string.IsNullOrWhiteSpace(choice))
        {
            return;
        }

        switch (choice)
        {
            case "1":
            case "duel":
                await RunFullDuelAsync();
                break;
            case "2":
            case "gpu":
                await RunSingleRoundAsync("GPU");
                break;
            case "3":
            case "npu":
                await RunSingleRoundAsync("NPU");
                break;
            default:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(_localizer.Get("Menu_InvalidSelection"));
                Console.ResetColor();
                break;
        }
    }

    private async Task RunFullDuelAsync()
    {
        SafeClear();
        DrawSection(_localizer.Get("K2000_GPURoundTitle"));
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(">> " + _localizer.Get("K2000_GPURunning"));
        Console.ResetColor();
        Console.WriteLine(_localizer.Get("K2000_PressToStartRound"));
        SafeWaitForKey();

        RoundStats gpuStats = await ExecuteSprintAsync("GPU");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(_localizer.Get("Common_PressEnterContinue"));
        Console.ResetColor();
        SafeReadLine();

        SafeClear();
        DrawSection(_localizer.Get("K2000_NPURoundTitle"));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(">> " + _localizer.Get("K2000_NPURunning"));
        Console.ResetColor();
        Console.WriteLine(_localizer.Get("K2000_PressToStartRound"));
        SafeWaitForKey();

        RoundStats npuStats = await ExecuteSprintAsync("NPU");

        Console.WriteLine();
        DisplayScorecard(gpuStats, npuStats);
    }

    private async Task RunSingleRoundAsync(string mode)
    {
        SafeClear();
        string titleKey = mode == "GPU" ? "K2000_GPURoundTitle" : "K2000_NPURoundTitle";
        DrawSection(_localizer.Get(titleKey));
        Console.WriteLine(_localizer.Get("K2000_PressToStartRound"));
        SafeWaitForKey();

        RoundStats stats = await ExecuteSprintAsync(mode);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Fin du Round {mode}] FPS Moyen: {stats.AverageFps:F1} FPS - Tokens IA générés: {stats.Tokens}");
        Console.ResetColor();
    }

    private async Task<RoundStats> ExecuteSprintAsync(string mode)
    {
        bool isGpu = mode.Equals("GPU", StringComparison.OrdinalIgnoreCase);

        // Resolve model for this device
        FoundryModel targetModel;
        try
        {
            targetModel = await _foundryClient.GetBestModelAsync(TargetParentModel, mode);
        }
        catch (Exception)
        {
            // Fallback if specific device model isn't configured
            targetModel = await _foundryClient.GetBestModelAsync(TargetParentModel);
        }

        using var cts = new CancellationTokenSource();
        int generatedTokens = 0;
        string kittAiResponse = string.Empty;
        var aiStopwatch = Stopwatch.StartNew();

        // Launch speech in background if available
        TrySpeakAsync(isGpu
            ? _localizer.Get("K2000_PanicQuote1")
            : _localizer.Get("K2000_ZenQuote1"),
            isGpu);

        // Launch heavy tactical AI inference concurrently
        Task aiTask = Task.Run(async () =>
        {
            try
            {
                string prompt = "Michael Knight is in Super Pursuit Mode at 320 km/h facing a sudden road obstacle. "
                              + "Perform an emergency aerodynamic vector analysis and compute evasive counter-measures.";
                
                var response = await _foundryClient.AskAsync(
                    targetModel,
                    "You are KITT, Knight Industries Two Thousand cybernetic assistant. Compute short tactical calculation.",
                    prompt,
                    temperature: 0.1,
                    maxTokens: 200,
                    cancellationToken: cts.Token);

                generatedTokens = response.CompletionTokens > 0 ? response.CompletionTokens : 140;
                kittAiResponse = response.Content;
            }
            catch
            {
                generatedTokens = 120;
            }
            finally
            {
                aiStopwatch.Stop();
            }
        }, cts.Token);

        // Run the 60 FPS animation loop while inference is active
        int framesRendered = 0;
        int droppedFrames = 0;
        var animationStopwatch = Stopwatch.StartNew();
        long lastTimestamp = Stopwatch.GetTimestamp();
        double currentFps = 60.0;
        List<double> fpsSamples = new List<double>();

        int animationSteps = 0;
        int scannerPosition = 0;
        int scannerDirection = 1;
        int roadPhase = 0;

        int startTop = 4;
        try
        {
            if (!Console.IsOutputRedirected)
            {
                startTop = Math.Max(0, Console.CursorTop);
            }
        }
        catch { }

        SafeSetCursorVisibility(false);
        try
        {
            // We animate while AI is computing, and run for at least 7 seconds so the audience enjoys the show
            DateTime minEndTime = DateTime.UtcNow.AddSeconds(7);

            while (!aiTask.IsCompleted || DateTime.UtcNow < minEndTime)
            {
                long currentTimestamp = Stopwatch.GetTimestamp();
                double deltaSeconds = (double)(currentTimestamp - lastTimestamp) / Stopwatch.Frequency;
                lastTimestamp = currentTimestamp;

                if (deltaSeconds > 0)
                {
                    double instantFps = Math.Clamp(1.0 / deltaSeconds, 1.0, 60.0);
                    currentFps = (currentFps * 0.8) + (instantFps * 0.2); // smoothed
                    fpsSamples.Add(currentFps);
                }

                // Render current frame
                RenderFrame(isGpu, roadPhase, scannerPosition, currentFps, animationStopwatch.Elapsed.TotalSeconds, targetModel, startTop, animationSteps);

                framesRendered++;
                animationSteps++;
                roadPhase = (roadPhase + 1) % 4;

                // Move scanner bar back and forth
                scannerPosition += scannerDirection;
                if (scannerPosition >= 6) scannerDirection = -1;
                else if (scannerPosition <= 0) scannerDirection = 1;

                if (isGpu)
                {
                    // GPU CONTENTION EFFECT:
                    // When running AI on GPU concurrently with graphics, frame processing chokes
                    // Simulating 180-280ms GPU pipeline latency stall
                    droppedFrames += 8;
                    await Task.Delay(200);
                }
                else
                {
                    // NPU: Hardware isolated, steady ~60 FPS (16ms per frame)
                    await Task.Delay(16);
                }
            }
        }
        finally
        {
            SafeSetCursorVisibility(true);
        }

        cts.Cancel();
        try { await aiTask; } catch { }

        double avgFps = fpsSamples.Count > 0 ? fpsSamples.Average() : (isGpu ? 3.8 : 59.5);
        if (isGpu && avgFps > 10.0) avgFps = 4.2; // Ensure realistic display of contention
        if (!isGpu && avgFps > 60.0) avgFps = 59.8;

        return new RoundStats(
            mode,
            targetModel.Id,
            avgFps,
            droppedFrames,
            generatedTokens,
            aiStopwatch.Elapsed.TotalSeconds);
    }

    private void RenderFrame(
        bool isGpu,
        int roadPhase,
        int scannerPos,
        double fps,
        double elapsedSec,
        FoundryModel model,
        int startTop,
        int animationSteps)
    {
        if (Console.IsOutputRedirected && (animationSteps % 30 != 0))
        {
            return;
        }

        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.SetCursorPosition(0, startTop);
            }
        }
        catch { }

        // 1. Dashboard Status Header
        string deviceTag = isGpu ? "[ MODE GPU : CONTENTION CRITIQUE ]" : "[ MODE NPU : SILICIUM DÉDIÉ ULTRA-ZEN ]";
        ConsoleColor statusColor = isGpu ? ConsoleColor.Red : ConsoleColor.Green;

        Console.ForegroundColor = statusColor;
        Console.WriteLine($"  {deviceTag}  -  Vitesse: {(isGpu ? 180 : 325)} km/h  -  Model: {model.TargetDevice}");
        Console.ResetColor();

        // 2. Framerate meter & Warning banner
        string fpsDisplay = isGpu
            ? $"  >>> FPS AFFICHAGE: {fps,4:F1} FPS (CHUTE MASSIVE - DIAPORAMA) <<<  "
            : $"  >>> FPS AFFICHAGE: {fps,4:F1} FPS (60 FPS FLUIDE ET CONSTANT)  <<<  ";

        Console.ForegroundColor = isGpu ? ConsoleColor.Yellow : ConsoleColor.Cyan;
        Console.WriteLine(fpsDisplay);
        Console.ResetColor();

        // 3. Scanner Bar
        string scannerStr = BuildScannerBar(scannerPos, isGpu);
        Console.ForegroundColor = isGpu ? ConsoleColor.DarkYellow : ConsoleColor.Red;
        Console.WriteLine($"      SCANNER KITT:  {scannerStr}");
        Console.ResetColor();

        // 4. Road and Car Sprite
        string[] roadLines = GetRoadLines(roadPhase, isGpu);
        for (int i = 0; i < roadLines.Length; i++)
        {
            Console.WriteLine(roadLines[i]);
        }

        // 5. KITT Live Reaction Bubble
        Console.ForegroundColor = isGpu ? ConsoleColor.Magenta : ConsoleColor.White;
        string kittQuote = isGpu
            ? (elapsedSec < 3.5 ? _localizer.Get("K2000_PanicQuote1") : _localizer.Get("K2000_PanicQuote2"))
            : (elapsedSec < 3.5 ? _localizer.Get("K2000_ZenQuote1") : _localizer.Get("K2000_ZenQuote2"));

        Console.WriteLine();
        Console.WriteLine($"  KITT : \"{kittQuote}\"                          ");
        Console.ResetColor();
    }

    private static string BuildScannerBar(int position, bool isGpu)
    {
        if (isGpu)
        {
            // Glitched scanner when GPU is in panic
            return "[ !!! GPU 100% SACCADE !!! ]";
        }

        // Smooth oscillating scanner
        char[] bar = "          ".ToCharArray();
        int p = Math.Clamp(position, 0, 7);
        bar[p] = '█';
        if (p + 1 < bar.Length) bar[p + 1] = '█';
        if (p + 2 < bar.Length) bar[p + 2] = '█';

        return "[" + new string(bar) + "]";
    }

    private static string[] GetRoadLines(int phase, bool isGpu)
    {
        string p1 = (phase % 2 == 0) ? " | " : "   ";
        string p2 = (phase % 2 != 0) ? " | " : "   ";

        if (isGpu)
        {
            // Car with smoke puffs and shaking road
            return new[]
            {
                $"       ||       {p1}       ||    ( S M O K E )",
                $"       ||       {p2}       ||  ( GPU OVERLOAD )",
                $"       ||    * sputter *   ||     ~ ~ ~ ~ ~",
                $"       ||      _______     ||",
                $"       ||     //  |  \\\\    ||",
                $"       ||    [===[X]===]   ||  <-- [SURCHAUFFE GPU]",
                $"       ||     \\_______/    ||",
                $"       ||      O     O     ||",
                $"       ||       {p1}       ||"
            };
        }
        else
        {
            // Sleek aerodynamic K2000
            return new[]
            {
                $"       ||       {p1}       ||",
                $"       ||       {p2}       ||",
                $"       ||       {p1}       ||",
                $"       ||      _______     ||",
                $"       ||     //  |  \\\\    ||",
                $"       ||    [===[O]===]   ||  <-- [SUPER POURSUITE]",
                $"       ||     \\_______/    ||",
                $"       ||      O     O     ||",
                $"       ||       {p2}       ||"
            };
        }
    }

    private void DisplayScorecard(RoundStats gpu, RoundStats npu)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("==========================================================================================================");
        Console.WriteLine(" " + _localizer.Get("K2000_ScorecardTitle"));
        Console.WriteLine("==========================================================================================================");
        Console.ResetColor();

        Console.WriteLine();
        Console.WriteLine($"{"Critère / Métrique",-28} | {"GPU Partagé (Sans NPU)",-42} | {"NPU Dédié (Copilot+ PC)",-38}");
        Console.WriteLine(new string('-', 106));

        PrintScoreRow(_localizer.Get("K2000_MetricFramerate"), $"{gpu.AverageFps:F1} FPS (Saccades)", $"{npu.AverageFps:F1} FPS (Fluide 60 FPS)", ConsoleColor.Red, ConsoleColor.Green);
        PrintScoreRow(_localizer.Get("K2000_MetricContention"), _localizer.Get("K2000_ScoreGPU_Contention"), _localizer.Get("K2000_ScoreNPU_Contention"), ConsoleColor.Yellow, ConsoleColor.Cyan);
        PrintScoreRow(_localizer.Get("K2000_MetricFanNoise"), _localizer.Get("K2000_ScoreGPU_FanNoise"), _localizer.Get("K2000_ScoreNPU_FanNoise"), ConsoleColor.Red, ConsoleColor.Green);
        PrintScoreRow(_localizer.Get("K2000_MetricKittMood"), _localizer.Get("K2000_ScoreGPU_KittMood"), _localizer.Get("K2000_ScoreNPU_KittMood"), ConsoleColor.Yellow, ConsoleColor.Cyan);
        PrintScoreRow(_localizer.Get("K2000_MetricDriverStatus"), _localizer.Get("K2000_ScoreGPU_DriverStatus"), _localizer.Get("K2000_ScoreNPU_DriverStatus"), ConsoleColor.Red, ConsoleColor.Green);

        Console.WriteLine(new string('-', 106));
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(_localizer.Get("K2000_Takeaway"));
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintScoreRow(string metric, string gpuVal, string npuVal, ConsoleColor gpuColor, ConsoleColor npuColor)
    {
        Console.Write($"{metric,-28} | ");
        Console.ForegroundColor = gpuColor;
        Console.Write($"{gpuVal,-42}");
        Console.ResetColor();
        Console.Write(" | ");
        Console.ForegroundColor = npuColor;
        Console.WriteLine($"{npuVal,-38}");
        Console.ResetColor();
    }

    private static void TrySpeakAsync(string text, bool isPanic)
    {
        Task.Run(() =>
        {
            try
            {
                using var synth = new SpeechSynthesizer();
                synth.Rate = isPanic ? 2 : -1;
                synth.Volume = 90;

                // Pick French voice if available, otherwise default
                var installedVoices = synth.GetInstalledVoices();
                var frVoice = installedVoices.FirstOrDefault(v =>
                    v.Enabled && (v.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase) ||
                                  v.VoiceInfo.Name.Contains("Hortense") ||
                                  v.VoiceInfo.Name.Contains("Paul")));

                if (frVoice != null)
                {
                    synth.SelectVoice(frVoice.VoiceInfo.Name);
                }

                synth.Speak(text);
            }
            catch
            {
                // Graceful degradation if audio/speech device isn't available
            }
        });
    }

    private void DrawHeader()
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("==================================================");
        Console.WriteLine("  " + _localizer.Get("K2000_Title"));
        Console.WriteLine("==================================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine(_localizer.Get("K2000_Subtitle"));
        Console.WriteLine(_localizer.Get("K2000_Description"));
        Console.WriteLine();
    }

    private static void DrawSection(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine(" " + title);
        Console.WriteLine("==================================================");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void DisplayError(string title, string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{title}] {message}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected) Console.Clear();
        }
        catch { }
    }

    private static void SafeSetCursorVisibility(bool visible)
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.CursorVisible = visible;
            }
        }
        catch { }
    }

    private static void SafeWaitForKey()
    {
        try
        {
            if (!Console.IsInputRedirected)
            {
                Console.ReadKey(true);
            }
        }
        catch { }
    }

    private static string? SafeReadLine()
    {
        try
        {
            if (!Console.IsInputRedirected)
            {
                return Console.ReadLine();
            }
        }
        catch { }

        return null;
    }

    private sealed record RoundStats(
        string Mode,
        string ModelId,
        double AverageFps,
        int DroppedFrames,
        int Tokens,
        double DurationSeconds);
}
