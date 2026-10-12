using CopilotPlusDemoHub.Core;

namespace CopilotPlusDemoHub.Demos.Engineering;

public sealed class EngineeringDemo
{
    private const string ScenarioName =
        "Engineering Assistant";

    private const string PreferredModel =
        "phi-4-mini";

    private readonly FoundryClient _foundryClient;
    private readonly LocalizationService _localizer;
    private readonly string? _preferredDevice;

    public EngineeringDemo(
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
                        "Engineering_FoundryUnavailable"),
                    _localizer.Get(
                        "Engineering_VerifyServer"));

                return;
            }

            FoundryModel selectedModel =
                await _foundryClient.GetBestModelAsync(
                    PreferredModel,
                    _preferredDevice);

            DisplayRuntime(
                selectedModel);

            string manualPath =
                FindManualPath();

            if (!File.Exists(manualPath))
            {
                DisplayError(
                    _localizer.Get(
                        "Engineering_DocumentNotFound"),
                    manualPath);

                return;
            }

            string documentation =
                await File.ReadAllTextAsync(
                    manualPath);

            if (string.IsNullOrWhiteSpace(documentation))
            {
                DisplayError(
                    _localizer.Get(
                        "Engineering_DocumentEmpty"),
                    manualPath);

                return;
            }

            DisplayDocumentationInformation(
                manualPath,
                documentation);

            string question =
                ReadValueOrDefault(
                    _localizer.Get(
                        "Engineering_QuestionLabel"),
                    _localizer.Get(
                        "Engineering_DefaultQuestion"));

            string languageInstruction =
                _localizer.Get(
                    "Language_ResponseInstruction");

            string systemPrompt =
                EngineeringPromptBuilder.BuildSystemPrompt(
                    languageInstruction);

            string userPrompt =
                EngineeringPromptBuilder.BuildUserPrompt(
                    documentation,
                    question,
                    languageInstruction);

            DisplayQuestion(
                question);

            Console.WriteLine();

            Console.WriteLine(
                _localizer.Get(
                    "Engineering_DataNotice"));

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Green;

            Console.WriteLine(
                _localizer.Get(
                    "Engineering_GuidanceTitle"));

            Console.ResetColor();

            Console.WriteLine(
                "==================================================");

            var spinner =
                new ConsoleSpinner(
                    _localizer.Get(
                        "Engineering_Analyzing"));

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
                        temperature: 0.0,
                        maxTokens: 800);
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
                        "Engineering_NoGuidance"));
            }

            Console.WriteLine();

            Console.WriteLine(
                "==================================================");

            Console.WriteLine();

            Console.ForegroundColor =
                ConsoleColor.Yellow;

            Console.WriteLine(
                _localizer.Get(
                    "Engineering_NoticeTitle"));

            Console.ResetColor();

            Console.WriteLine(
                _localizer.Get(
                    "Engineering_Notice"));

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
                    "Engineering_Completed"));

            Console.ResetColor();
        }
        catch (FoundryClientException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Engineering_FoundryError"),
                exception.Message);
        }
        catch (HttpRequestException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Engineering_ConnectionError"),
                exception.Message);
        }
        catch (TaskCanceledException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Engineering_TimeoutError"),
                exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Engineering_DocumentAccessError"),
                exception.Message);
        }
        catch (IOException exception)
        {
            DisplayError(
                _localizer.Get(
                    "Engineering_DocumentReadError"),
                exception.Message);
        }
        catch (Exception exception)
        {
            DisplayError(
                _localizer.Get(
                    "Engineering_GenericError"),
                exception.Message);
        }
    }

    private static string FindManualPath()
    {
        string currentDirectoryPath =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "Demos",
                "Engineering",
                "SampleManual.txt");

        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        string applicationDirectoryPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "Demos",
                "Engineering",
                "SampleManual.txt");

        if (File.Exists(applicationDirectoryPath))
        {
            return applicationDirectoryPath;
        }

        string directApplicationPath =
            Path.Combine(
                AppContext.BaseDirectory,
                "SampleManual.txt");

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
                "Engineering_Title"));

        Console.WriteLine(
            "==================================================");

        Console.ResetColor();

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Engineering_Subtitle"));

        Console.WriteLine();

        Console.WriteLine(
            _localizer.Get(
                "Engineering_Description"));
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

    private void DisplayDocumentationInformation(
        string manualPath,
        string documentation)
    {
        var manualFile =
            new FileInfo(
                manualPath);

        int lineCount =
            documentation.Split(
                new string[]
                {
                    "\r\n",
                    "\n"
                },
                StringSplitOptions.None).Length;

        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            _localizer.Get(
                "Engineering_KnowledgeTitle"));

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Engineering_Document")) +
            manualFile.Name);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Engineering_DocumentLocation")) +
            _localizer.Get(
                "Engineering_LocalFile"));

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Engineering_DocumentLines")) +
            lineCount);

        Console.WriteLine(
            FormatLabel(
                _localizer.Get(
                    "Engineering_DocumentSize")) +
            manualFile.Length +
            " bytes");

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

    private void DisplayQuestion(
        string question)
    {
        Console.WriteLine();

        Console.ForegroundColor =
            ConsoleColor.Cyan;

        Console.WriteLine(
            _localizer.Get(
                "Engineering_QuestionSummary"));

        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            question);

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
                "Engineering_GuidanceTitle"));

        Console.ResetColor();

        Console.WriteLine(
            "==================================================");

        if (string.IsNullOrWhiteSpace(content))
        {
            Console.WriteLine(
                _localizer.Get(
                    "Engineering_NoGuidance"));
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
                "Engineering_NoticeTitle"));

        Console.ResetColor();

        Console.WriteLine(
            _localizer.Get(
                "Engineering_Notice"));
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