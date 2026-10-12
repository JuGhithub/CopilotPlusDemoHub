using System.Text.Json;
using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.Demos.Insurance;

public sealed class InsuranceDemo
{
    private const string ScenarioName =
        "Insurance Assistant";

    private const string PreferredModel =
        "phi-4-mini";

    private readonly FoundryClient _foundryClient;
    private readonly LocalizationService _localizer;
    private readonly string? _preferredDevice;

    public InsuranceDemo(
        FoundryClient foundryClient,
        LocalizationService localizer,
        string? preferredDevice = null)
    {
        _foundryClient =
            foundryClient ??
            throw new ArgumentNullException(
                nameof(foundryClient));

        _localizer =
            localizer ??
            throw new ArgumentNullException(
                nameof(localizer));

        _preferredDevice = preferredDevice;
    }

    public async Task RunAsync()
    {
        SafeClear();

        DrawHeader();

        try
        {
            bool serverAvailable =
                await _foundryClient.IsServerAvailableAsync();

            if (!serverAvailable)
            {
                DisplayError(
                    _localizer.Get(
                        "Insurance_FoundryUnavailable"),
                    _localizer.Get(
                        "Insurance_VerifyServer"));

                return;
            }

            FoundryModel selectedModel =
                await _foundryClient.GetBestModelAsync(
                    PreferredModel,
                    _preferredDevice);

            DisplayRuntime(
                selectedModel);

            string claimPath =
                FindClaimPath();

            if (!File.Exists(claimPath))
            {
                DisplayError(
                    _localizer.Get(
                        "Insurance_DocumentNotFound"),
                    claimPath);

                return;
            }

            string claimJson =
                await File.ReadAllTextAsync(
                    claimPath);

            if (string.IsNullOrWhiteSpace(claimJson))
            {
                DisplayError(
                    _localizer.Get(
                        "Insurance_DocumentEmpty"),
                    claimPath);

                return;
            }

            DisplayClaimInformation(
                claimPath,
                claimJson);

            string question =
                ReadValueOrDefault(
                    _localizer.Get(
                        "Insurance_QuestionLabel"),
                    _localizer.Get(
                        "Insurance_DefaultQuestion"));

            string languageInstruction =
                _localizer.Get(
                    "Language_ResponseInstruction");

            string systemPrompt =
                InsurancePromptBuilder.BuildSystemPrompt(
                    languageInstruction);

            string userPrompt =
                InsurancePromptBuilder.BuildUserPrompt(
                    claimJson,
                    question,
                    languageInstruction);

            Console.WriteLine();

            Console.WriteLine(
                _localizer.Get(
                    "Insurance_DataNotice"));

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                _localizer.Get(
                    "Insurance_GuidanceTitle"));

            Console.ResetColor();

            Console.WriteLine(
                "==================================================");

            var spinner =
                new ConsoleSpinner(
                    _localizer.Get(
                        "Insurance_Analyzing"));

            spinner.Start();
            bool isFirstChunk = true;

            FoundryInferenceResult result;

            try
            {
                result =
                    await _foundryClient.AskStreamingAsync(
                        selectedModel,
                        systemPrompt,
                        userPrompt,
                        chunk =>
                        {
                            if (isFirstChunk)
                            {
                                spinner.Clear();
                                isFirstChunk = false;
                            }

                            Console.Write(chunk);
                        },
                        temperature: 0.1,
                        maxTokens: 850);
            }
            catch
            {
                spinner.Clear();
                throw;
            }

            if (isFirstChunk)
            {
                spinner.Clear();

                Console.WriteLine(
                    _localizer.Get(
                        "Insurance_NoGuidance"));
            }

            Console.WriteLine();

            Console.WriteLine(
                "==================================================");

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                _localizer.Get(
                    "Insurance_NoticeTitle"));

            Console.ResetColor();

            Console.WriteLine(
                _localizer.Get(
                    "Insurance_Notice"));

            DisplayLocalizedTelemetry(
                result);

            string telemetryFile =
                await _foundryClient.SaveTelemetryAsync(
                    result,
                    ScenarioName);

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.DarkGray;

            Console.WriteLine(
                _localizer.Get(
                    "Telemetry_Log"));

            Console.ResetColor();

            Console.WriteLine(
                "--------------------------------------------------");

            Console.WriteLine(
                telemetryFile);

            Console.WriteLine(
                "--------------------------------------------------");

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                _localizer.Get(
                    "Insurance_Completed"));

            Console.ResetColor();
        }
        catch (FoundryClientException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Insurance_FoundryError"),
                exception.Message);
        }
        catch (HttpRequestException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Insurance_ConnectionError"),
                exception.Message);
        }
        catch (TaskCanceledException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Insurance_TimeoutError"),
                exception.Message);
        }
        catch (IOException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Insurance_DocumentReadError"),
                exception.Message);
        }
        catch (Exception exception)
        {
            DisplayError(
                _localizer.Get(
                    "Insurance_GenericError"),
                exception.Message);
        }
    }

    private static string FindClaimPath()
    {
        string currentDirectoryPath =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "Demos",
                "Insurance",
                "SampleClaim.json");

        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        string applicationDirectoryPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Demos",
                "Insurance",
                "SampleClaim.json");

        if (File.Exists(applicationDirectoryPath))
        {
            return applicationDirectoryPath;
        }

        string directApplicationPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "SampleClaim.json");

        if (File.Exists(directApplicationPath))
        {
            return directApplicationPath;
        }

        return currentDirectoryPath;
    }

    private void DrawHeader()
    {
        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            "==================================================");

        Console.WriteLine(
            " " +
            _localizer.Get(
                "Insurance_Title"));

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Insurance_Subtitle"));

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Insurance_Description"));
    }

    private void DisplayRuntime(
        FoundryModel model)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            _localizer.Get(
                "Runtime_Title"));

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_Model")) +
            model.Id);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_ParentModel")) +
            model.Parent);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_TargetDevice")) +
            model.TargetDevice);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_ExecutionProvider")) +
            model.ExecutionProvider);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_InferenceLocation")) +
            _localizer.Get(
                "Runtime_LocalDevice"));

        Console.WriteLine(
            "--------------------------------------------------");
    }

    private void DisplayClaimInformation(
        string claimPath,
        string claimJson)
    {
        var fileInfo =
            new FileInfo(
                claimPath);

        string claimId = "CLM-2026-8942";
        string policyholder = "Sophie Martin";
        string vehicle = "Modèle SUV compact";
        string circumstances = "Collision arrière à l'arrêt au feu rouge";

        try
        {
            using var doc = JsonDocument.Parse(claimJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("claimId", out var idProp))
                claimId = idProp.GetString() ?? claimId;

            if (root.TryGetProperty("policyholder", out var holderProp) &&
                holderProp.TryGetProperty("name", out var nameProp))
                policyholder = nameProp.GetString() ?? policyholder;

            if (root.TryGetProperty("policyholder", out holderProp) &&
                holderProp.TryGetProperty("vehicle", out var vehProp))
            {
                string make = vehProp.TryGetProperty("make", out var m) ? m.GetString() ?? "" : "";
                string model = vehProp.TryGetProperty("model", out var mdl) ? mdl.GetString() ?? "" : "";
                vehicle = $"{make} {model}".Trim();
            }

            if (root.TryGetProperty("incident", out var incProp) &&
                incProp.TryGetProperty("circumstances", out var circProp))
            {
                circumstances = circProp.GetString() ?? circumstances;
            }
        }
        catch
        {
        }

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            _localizer.Get(
                "Insurance_ClaimSummaryTitle"));

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Insurance_ClaimNumber")) +
            claimId);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Insurance_Policyholder")) +
            policyholder);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Insurance_Vehicle")) +
            vehicle);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Insurance_DocumentFile")) +
            fileInfo.Name +
            $" ({fileInfo.Length} bytes)");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Insurance_Circumstances")) +
            circumstances);

        Console.WriteLine(
            "--------------------------------------------------");
    }

    private static string ReadValueOrDefault(
        string label,
        string defaultValue)
    {
        Console.WriteLine();

        Console.Write(
            label +
            " [" +
            defaultValue +
            "]: ");

        string? value =
            Console.ReadLine();

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim();
    }

    private void DisplayLocalizedTelemetry(
        FoundryInferenceResult result)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            _localizer.Get(
                "Telemetry_Title"));

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_Model")) +
            result.ModelId);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_ParentModel")) +
            result.ParentModel);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_TargetDevice")) +
            result.TargetDevice);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_ExecutionProvider")) +
            result.ExecutionProvider);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_InferenceLocation")) +
            _localizer.Get(
                "Runtime_LocalDevice"));

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Runtime_Endpoint")) +
            _localizer.Get(
                "Runtime_Loopback"));

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Telemetry_PromptTokens")) +
            result.PromptTokens);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Telemetry_CompletionTokens")) +
            result.CompletionTokens);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Telemetry_TotalTokens")) +
            result.TotalTokens);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Telemetry_TotalLatency")) +
            result.LatencyMilliseconds +
            " ms");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Telemetry_AverageThroughput")) +
            result.TokensPerSecond.ToString("F1") +
            " tokens/s");

        Console.WriteLine(
            "--------------------------------------------------");
    }

    private void DisplayError(
        string title,
        string details)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Red;

        Console.WriteLine(
            _localizer.Get(
                "Common_Error"));

        Console.WriteLine(
            title);

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            details);

        Console.WriteLine(
            "--------------------------------------------------");
    }

    private static string FormatLabel(
        string label)
    {
        const int LabelWidth = 23;

        if (label.Length >= LabelWidth)
        {
            return label + " : ";
        }

        return label.PadRight(
                   LabelWidth) +
               ": ";
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