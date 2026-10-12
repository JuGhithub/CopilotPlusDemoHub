using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.Demos.Operations;

public sealed class OperationsDemo
{
    private const string ScenarioName =
        "Operations Assistant";

    private const string PreferredModel =
        "phi-4-mini";

    private readonly FoundryClient _foundryClient;
    private readonly LocalizationService _localizer;
    private readonly string? _preferredDevice;

    public OperationsDemo(
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

        bool serverAvailable =
            await _foundryClient.IsServerAvailableAsync();

        if (!serverAvailable)
        {
            DisplayError(
                _localizer.Get(
                    "Operations_FoundryUnavailable"),
                _localizer.Get(
                    "Operations_VerifyServer"));

            return;
        }

        FoundryModel selectedModel =
            await _foundryClient.GetBestModelAsync(
                PreferredModel,
                _preferredDevice);

        DisplayRuntime(
            selectedModel);

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Operations_FieldIncident"));

        Console.WriteLine(
            "==================================================");

        string equipment =
            ReadValueOrDefault(
                _localizer.Get(
                    "Operations_Equipment"),
                _localizer.Get(
                    "Operations_DefaultEquipment"));

        string alarmCode =
            ReadValueOrDefault(
                _localizer.Get(
                    "Operations_AlarmCode"),
                _localizer.Get(
                    "Operations_DefaultAlarmCode"));

        string symptoms =
            ReadValueOrDefault(
                _localizer.Get(
                    "Operations_ObservedSymptoms"),
                _localizer.Get(
                    "Operations_DefaultSymptoms"));

        string systemPrompt =
            BuildLocalizedSystemPrompt();

        string userPrompt =
            OperationsPromptBuilder.BuildUserPrompt(
                equipment,
                alarmCode,
                symptoms);

        DisplayIncident(
            equipment,
            alarmCode,
            symptoms);

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Operations_DataNotice"));

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Green;

        Console.WriteLine(
            _localizer.Get(
                "Operations_GuidanceTitle"));

        Console.ResetColor();

        Console.WriteLine(
            "==================================================");

        try
        {
            FoundryInferenceResult result;

            var spinner =
                new ConsoleSpinner(
                    _localizer.Get(
                        "Operations_Analyzing"));

            spinner.Start();
            bool isFirstChunk = true;

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
                        temperature: 0.2,
                        maxTokens: 700);
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
                        "Operations_NoGuidance"));
            }

            Console.WriteLine();

            Console.WriteLine(
                "==================================================");

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                _localizer.Get(
                    "Operations_SafetyNoticeTitle"));

            Console.ResetColor();

            Console.WriteLine(
                _localizer.Get(
                    "Operations_SafetyNotice"));

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
        }
        catch (FoundryClientException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Operations_FoundryError"),
                exception.Message);
        }
        catch (HttpRequestException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Operations_ConnectionError"),
                exception.Message);
        }
        catch (TaskCanceledException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Operations_TimeoutError"),
                exception.Message);
        }
        catch (Exception exception)
        {
            DisplayError(
                _localizer.Get(
                    "Operations_GenericError"),
                exception.Message);
        }
    }

    private string BuildLocalizedSystemPrompt()
    {
        string basePrompt =
            OperationsPromptBuilder.BuildSystemPrompt();

        string languageInstruction =
            _localizer.Get(
                "Language_ResponseInstruction");

        return
            basePrompt +
            Environment.NewLine +
            Environment.NewLine +
            "IMPORTANT LANGUAGE INSTRUCTION:" +
            Environment.NewLine +
            languageInstruction;
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
                "Operations_Title"));

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Operations_Subtitle"));

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Operations_Description"));
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

    private void DisplayIncident(
        string equipment,
        string alarmCode,
        string symptoms)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            _localizer.Get(
                "Operations_IncidentSummary"));

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Operations_Equipment")) +
            equipment);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Operations_AlarmCode")) +
            alarmCode);

        Console.WriteLine(
            _localizer.Get(
                "Operations_ObservedSymptoms") +
            " :");

        string[] symptomList =
            symptoms.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        foreach (string symptom in symptomList)
        {
            Console.WriteLine(
                "  � " +
                symptom);
        }

        Console.WriteLine(
            "--------------------------------------------------");
    }

    private void DisplayResponse(
        string content)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Green;

        Console.WriteLine(
            _localizer.Get(
                "Operations_GuidanceTitle"));

        Console.ResetColor();

        Console.WriteLine(
            "==================================================");

        if (string.IsNullOrWhiteSpace(content))
        {
            Console.WriteLine(
                _localizer.Get(
                    "Operations_NoGuidance"));
        }
        else
        {
            Console.WriteLine(
                content.Trim());
        }

        Console.WriteLine(
            "==================================================");

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Yellow;

        Console.WriteLine(
            _localizer.Get(
                "Operations_SafetyNoticeTitle"));

        Console.ResetColor();

        Console.WriteLine(
            _localizer.Get(
                "Operations_SafetyNotice"));
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