using System.Diagnostics;
using System.Text.Json;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.Demos.Benchmark;

public sealed class EnergyBenchmarkDemo
{
    private const string PreferredModel = "phi-4-mini";
    private readonly FoundryClient _foundryClient;
    private readonly LocalizationService _localizer;
    private readonly string? _preferredDevice;

    public EnergyBenchmarkDemo(
        FoundryClient foundryClient,
        LocalizationService localizer,
        string? preferredDevice = null)
    {
        _foundryClient = foundryClient ?? throw new ArgumentNullException(nameof(foundryClient));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _preferredDevice = preferredDevice;
    }

    public async Task RunAsync()
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

        FoundryModel selectedModel;
        try
        {
            selectedModel = await _foundryClient.GetBestModelAsync(PreferredModel, _preferredDevice);
        }
        catch (Exception ex)
        {
            DisplayError(_localizer.Get("Common_Error"), ex.Message);
            return;
        }

        DisplayRuntime(selectedModel);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(_localizer.Get("Benchmark_NoticeBatteryRecommendation"));
        Console.ResetColor();
        Console.WriteLine();

        int durationMinutes = ReadDurationOrDefault(10);
        TimeSpan duration = TimeSpan.FromMinutes(durationMinutes);

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"{_localizer.Get("Benchmark_StartingNotice")} ({durationMinutes} min)...");
        Console.ResetColor();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(_localizer.Get("Benchmark_PressKeyToStart"));
        if (!Console.IsInputRedirected)
        {
            Console.ReadKey(true);
        }

        using var telemetry = new HardwareTelemetryService();
        telemetry.Start(TimeSpan.FromSeconds(1));

        DateTime startTime = DateTime.UtcNow;
        DateTime endTime = startTime + duration;

        int totalPromptTokens = 0;
        int totalCompletionTokens = 0;
        int completedRequests = 0;
        var stopwatch = Stopwatch.StartNew();

        string systemPrompt = "You are an AI benchmark stress workload. Provide an exhaustive, in-depth technical analysis.";
        string userPrompt = "Generate a comprehensive analysis of industrial centrifugal fan aerodynamics, acoustic vibration modes, harmonic damping, and predictive maintenance protocols for multi-stage HVAC ventilation systems.";

        try
        {
            while (DateTime.UtcNow < endTime)
            {
                TimeSpan remaining = endTime - DateTime.UtcNow;
                if (remaining.TotalSeconds <= 0) break;

                HardwareSnapshot snapshot = telemetry.GetCurrentSnapshot();

                RenderLiveDashboard(
                    selectedModel,
                    remaining,
                    duration,
                    snapshot,
                    completedRequests,
                    totalCompletionTokens,
                    stopwatch.Elapsed.TotalSeconds > 0 ? totalCompletionTokens / stopwatch.Elapsed.TotalSeconds : 0);

                var completionResult = await _foundryClient.AskAsync(
                    selectedModel,
                    systemPrompt,
                    userPrompt,
                    temperature: 0.1,
                    maxTokens: 500);

                if (completionResult.Successful)
                {
                    completedRequests++;
                    totalPromptTokens += completionResult.PromptTokens;
                    totalCompletionTokens += completionResult.CompletionTokens;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Benchmark loop interrupted: {ex.Message}]");
            Console.ResetColor();
        }
        finally
        {
            stopwatch.Stop();
            telemetry.Stop();
        }

        HardwareSummary summary = telemetry.GetSummary();

        double totalSeconds = stopwatch.Elapsed.TotalSeconds > 0 ? stopwatch.Elapsed.TotalSeconds : 1;
        double overallTokensPerSec = totalCompletionTokens / totalSeconds;
        int totalTokens = totalPromptTokens + totalCompletionTokens;

        double efficiencyTokensPerWh = summary.EnergyConsumedWattHours > 0
            ? Math.Round(totalTokens / summary.EnergyConsumedWattHours, 1)
            : 0;

        var benchmarkRecord = new BenchmarkResultRecord
        {
            TimestampUtc = DateTime.UtcNow,
            Device = selectedModel.TargetDevice,
            ModelId = selectedModel.Id,
            ExecutionProvider = selectedModel.ExecutionProvider,
            DurationMinutes = durationMinutes,
            ActualDurationSeconds = Math.Round(totalSeconds, 1),
            CompletedRequests = completedRequests,
            PromptTokens = totalPromptTokens,
            CompletionTokens = totalCompletionTokens,
            TotalTokens = totalTokens,
            TokensPerSecond = Math.Round(overallTokensPerSec, 1),
            IsOnBattery = summary.IsOnBattery,
            InitialBatteryPercent = summary.InitialBatteryPercent,
            FinalBatteryPercent = summary.FinalBatteryPercent,
            BatteryPercentDelta = summary.BatteryPercentDelta,
            AverageDischargeWatts = summary.AverageDischargeWatts,
            PeakDischargeWatts = summary.PeakDischargeWatts,
            EnergyConsumedWattHours = summary.EnergyConsumedWattHours,
            AverageCpuPercent = summary.AverageCpuPercent,
            PeakCpuPercent = summary.PeakCpuPercent,
            AverageTemperatureCelsius = summary.AverageTemperatureCelsius,
            PeakTemperatureCelsius = summary.PeakTemperatureCelsius,
            TokensPerWattHour = efficiencyTokensPerWh
        };

        string savedFile = await SaveBenchmarkResultAsync(benchmarkRecord);

        DisplayResults(benchmarkRecord, summary, savedFile);

        await DisplayComparativeTableAsync();
    }

    private static void RenderLiveDashboard(
        FoundryModel model,
        TimeSpan remaining,
        TimeSpan totalDuration,
        HardwareSnapshot snapshot,
        int requests,
        int completionTokens,
        double liveTokensPerSec)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine($"=== BENCHMARK EN COURS [{model.TargetDevice}] • TEMPS RESTANT: {remaining:mm\\:ss} / {totalDuration:mm\\:ss} ===");
        Console.ResetColor();

        string powerStatus = snapshot.IsOnBattery ? "Sur batterie" : "Secteur (AC)";
        string powerDisplay = snapshot.DischargeRateWatts > 0
            ? $"{snapshot.DischargeRateWatts:F1} W"
            : (snapshot.IsOnBattery ? "Mesure..." : "Alimentation secteur");

        Console.WriteLine($"  Batterie      : {snapshot.BatteryPercent}% ({powerStatus}) | Décharge: {powerDisplay}");

        if (snapshot.TemperatureCelsius.HasValue)
        {
            Console.WriteLine($"  Température   : {snapshot.TemperatureCelsius.Value:F1} °C");
        }

        Console.WriteLine($"  Charge CPU    : {snapshot.CpuUsagePercent:F1} %");
        Console.WriteLine($"  Débit moyen   : {liveTokensPerSec:F1} tokens/s");
        Console.WriteLine($"  Tokens IA     : {completionTokens:N0} tokens ({requests} requêtes générées)");
        Console.WriteLine("--------------------------------------------------");
    }

    private void DisplayResults(BenchmarkResultRecord record, HardwareSummary summary, string savedFile)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("==================================================");
        Console.WriteLine(" " + _localizer.Get("Benchmark_ResultsTitle"));
        Console.WriteLine("==================================================");
        Console.ResetColor();
        Console.WriteLine();

        Console.WriteLine($"Accélérateur testé       : {record.Device} ({record.ModelId})");
        Console.WriteLine($"Durée réelle du test     : {record.ActualDurationSeconds:F0} secondes");
        Console.WriteLine($"Requêtes complétées      : {record.CompletedRequests}");
        Console.WriteLine($"Tokens générés (total)   : {record.TotalTokens:N0} (Prompt: {record.PromptTokens:N0} | Réponse: {record.CompletionTokens:N0})");
        Console.WriteLine($"Débit d'inférence moyen  : {record.TokensPerSecond:F1} tokens/s");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("--- MÉTRIQUES ÉNERGIE & REFROIDISSEMENT ---");
        Console.ResetColor();

        Console.WriteLine($"Alimentation             : {(record.IsOnBattery ? "Sur Batterie" : "Secteur (AC)")}");
        Console.WriteLine($"Niveau de batterie       : {record.InitialBatteryPercent}% -> {record.FinalBatteryPercent}% (Perte: -{record.BatteryPercentDelta}%)");
        Console.WriteLine($"Puissance moyenne        : {record.AverageDischargeWatts:F1} Watts (Pic: {record.PeakDischargeWatts:F1} W)");
        Console.WriteLine($"Énergie consommée        : {record.EnergyConsumedWattHours:F2} Watt-heures (Wh)");

        if (record.AverageTemperatureCelsius.HasValue)
        {
            Console.WriteLine($"Température moyenne      : {record.AverageTemperatureCelsius.Value:F1} °C (Pic: {record.PeakTemperatureCelsius:F1} °C)");
        }

        Console.WriteLine($"Charge processeur (CPU)  : {record.AverageCpuPercent:F1} % (Pic: {record.PeakCpuPercent:F1} %)");

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"Indice d'efficience      : {record.TokensPerWattHour:F1} Tokens par Watt-heure");
        Console.ResetColor();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"Rapport sauvegardé sous  : {savedFile}");
        Console.ResetColor();
    }

    private static async Task<string> SaveBenchmarkResultAsync(BenchmarkResultRecord record)
    {
        string logDir = Path.GetFullPath("Logs");
        Directory.CreateDirectory(logDir);

        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string file = Path.Combine(logDir, $"benchmark-{record.Device.ToLowerInvariant()}-{timestamp}.json");

        string json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(file, json);

        return file;
    }

    private static async Task DisplayComparativeTableAsync()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("================================================================================");
        Console.WriteLine(" TABLEAU COMPARATIF D'EFFICACITÉ (NPU vs GPU vs CPU)");
        Console.WriteLine("================================================================================");
        Console.ResetColor();

        string logDir = Path.GetFullPath("Logs");
        var latestPerDevice = new Dictionary<string, BenchmarkResultRecord>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(logDir))
        {
            string[] files = Directory.GetFiles(logDir, "benchmark-*.json");
            foreach (string file in files.OrderByDescending(File.GetCreationTimeUtc))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(file);
                    BenchmarkResultRecord? record = JsonSerializer.Deserialize<BenchmarkResultRecord>(json);
                    if (record != null && !latestPerDevice.ContainsKey(record.Device))
                    {
                        latestPerDevice[record.Device] = record;
                    }
                }
                catch
                {
                }
            }
        }

        Console.WriteLine(string.Format("{0,-25} | {1,-15} | {2,-15} | {3,-15}", "Métrique", "NPU", "GPU", "CPU"));
        Console.WriteLine(new string('-', 78));

        string npuWatts = latestPerDevice.TryGetValue("NPU", out var npu) ? $"{npu.AverageDischargeWatts:F1} W" : "-";
        string gpuWatts = latestPerDevice.TryGetValue("GPU", out var gpu) ? $"{gpu.AverageDischargeWatts:F1} W" : "-";
        string cpuWatts = latestPerDevice.TryGetValue("CPU", out var cpu) ? $"{cpu.AverageDischargeWatts:F1} W" : "-";
        Console.WriteLine(string.Format("{0,-25} | {1,-15} | {2,-15} | {3,-15}", "Puissance moyenne (W)", npuWatts, gpuWatts, cpuWatts));

        string npuBattery = npu != null ? $"-{npu.BatteryPercentDelta}%" : "-";
        string gpuBattery = gpu != null ? $"-{gpu.BatteryPercentDelta}%" : "-";
        string cpuBattery = cpu != null ? $"-{cpu.BatteryPercentDelta}%" : "-";
        Console.WriteLine(string.Format("{0,-25} | {1,-15} | {2,-15} | {3,-15}", "Perte batterie", npuBattery, gpuBattery, cpuBattery));

        string npuTps = npu != null ? $"{npu.TokensPerSecond:F1} tok/s" : "-";
        string gpuTps = gpu != null ? $"{gpu.TokensPerSecond:F1} tok/s" : "-";
        string cpuTps = cpu != null ? $"{cpu.TokensPerSecond:F1} tok/s" : "-";
        Console.WriteLine(string.Format("{0,-25} | {1,-15} | {2,-15} | {3,-15}", "Débit d'inférence", npuTps, gpuTps, cpuTps));

        string npuCpu = npu != null ? $"{npu.AverageCpuPercent:F1} %" : "-";
        string gpuCpu = gpu != null ? $"{gpu.AverageCpuPercent:F1} %" : "-";
        string cpuCpu = cpu != null ? $"{cpu.AverageCpuPercent:F1} %" : "-";
        Console.WriteLine(string.Format("{0,-25} | {1,-15} | {2,-15} | {3,-15}", "Charge CPU système", npuCpu, gpuCpu, cpuCpu));

        string npuEff = npu != null ? $"{npu.TokensPerWattHour:F1}" : "-";
        string gpuEff = gpu != null ? $"{gpu.TokensPerWattHour:F1}" : "-";
        string cpuEff = cpu != null ? $"{cpu.TokensPerWattHour:F1}" : "-";
        Console.WriteLine(string.Format("{0,-25} | {1,-15} | {2,-15} | {3,-15}", "Efficience (Tokens/Wh)", npuEff, gpuEff, cpuEff));

        Console.WriteLine("================================================================================");
    }

    private int ReadDurationOrDefault(int defaultMinutes)
    {
        Console.Write($"{_localizer.Get("Benchmark_DurationPrompt")} [{defaultMinutes}]: ");
        string? input = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultMinutes;
        }

        if (int.TryParse(input.Trim(), out int minutes) && minutes > 0)
        {
            return minutes;
        }

        Console.WriteLine($"Valeur non reconnue. Utilisation de la durée par défaut : {defaultMinutes} min.");
        return defaultMinutes;
    }

    private void DrawHeader()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================");
        Console.WriteLine(" " + _localizer.Get("Benchmark_Title"));
        Console.WriteLine("==================================================");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine(_localizer.Get("Benchmark_Subtitle"));
        Console.WriteLine();
        Console.WriteLine(_localizer.Get("Benchmark_Description"));
    }

    private void DisplayRuntime(FoundryModel model)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(_localizer.Get("Runtime_Title"));
        Console.ResetColor();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine($"Modèle                 : {model.Id}");
        Console.WriteLine($"Accélérateur cible     : {model.TargetDevice}");
        Console.WriteLine($"Fournisseur d'exéc.    : {model.ExecutionProvider}");
        Console.WriteLine("--------------------------------------------------");
    }

    private void DisplayError(string title, string details)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(_localizer.Get("Common_Error"));
        Console.WriteLine(title);
        Console.ResetColor();
        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine(details);
        Console.WriteLine("--------------------------------------------------");
    }

    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
            }
        }
        catch
        {
        }
    }
}

public sealed class BenchmarkResultRecord
{
    public DateTime TimestampUtc { get; set; }
    public string Device { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ExecutionProvider { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public double ActualDurationSeconds { get; set; }
    public int CompletedRequests { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public double TokensPerSecond { get; set; }
    public bool IsOnBattery { get; set; }
    public int InitialBatteryPercent { get; set; }
    public int FinalBatteryPercent { get; set; }
    public double BatteryPercentDelta { get; set; }
    public double AverageDischargeWatts { get; set; }
    public double PeakDischargeWatts { get; set; }
    public double EnergyConsumedWattHours { get; set; }
    public double AverageCpuPercent { get; set; }
    public double PeakCpuPercent { get; set; }
    public double? AverageTemperatureCelsius { get; set; }
    public double? PeakTemperatureCelsius { get; set; }
    public double TokensPerWattHour { get; set; }
}
