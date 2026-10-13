using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotPlusDemoHub.Core;

public sealed class FoundryClient : IDisposable
{
    private const string DefaultBaseUrl =
        "http://127.0.0.1:52594";

    private const string ModelsEndpoint =
        "/v1/models";

    private const string ChatCompletionsEndpoint =
        "/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public FoundryClient(
        string? baseUrl = null)
    {
        string effectiveUrl =
            ResolveBaseUrl(baseUrl);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(
                effectiveUrl.TrimEnd('/')),

            Timeout = TimeSpan.FromMinutes(10)
        };

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    private static string ResolveBaseUrl(string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl) && baseUrl != DefaultBaseUrl)
        {
            return baseUrl;
        }

        string? envUrl =
            Environment.GetEnvironmentVariable("FOUNDRY_BASE_URL")
            ?? Environment.GetEnvironmentVariable("FOUNDRY_URL");

        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl;
        }

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "foundry.exe",
                Arguments = "server status -o json",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (process.Start())
            {
                string json = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1500);

                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("webUrls", out var urls) &&
                        urls.GetArrayLength() > 0)
                    {
                        string? firstUrl = urls[0].GetString();
                        if (!string.IsNullOrWhiteSpace(firstUrl))
                        {
                            return firstUrl;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback to default port if discovery fails
        }

        return !string.IsNullOrWhiteSpace(baseUrl)
            ? baseUrl
            : DefaultBaseUrl;
    }

    public string BaseUrl
    {
        get
        {
            return _httpClient.BaseAddress?.ToString()
                ?? string.Empty;
        }
    }

    public async Task<bool> IsServerAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            using HttpResponseMessage response =
                await _httpClient.GetAsync(
                    ModelsEndpoint,
                    cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<FoundryModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using HttpResponseMessage response =
            await _httpClient.GetAsync(
                ModelsEndpoint,
                cancellationToken);

        string responseJson =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new FoundryClientException(
                "Foundry Local could not return the loaded models.",
                (int)response.StatusCode,
                responseJson);
        }

        ModelsApiResponse? modelsResponse =
            JsonSerializer.Deserialize<ModelsApiResponse>(
                responseJson,
                _jsonOptions);

        if (modelsResponse == null ||
            modelsResponse.Data == null)
        {
            throw new FoundryClientException(
                "The Foundry Local models response is invalid.");
        }

        var models =
            new List<FoundryModel>();

        foreach (ModelApiItem modelItem in modelsResponse.Data)
        {
            if (string.IsNullOrWhiteSpace(modelItem.Id))
            {
                continue;
            }

            models.Add(
                new FoundryModel
                {
                    Id = modelItem.Id,
                    Parent = modelItem.Parent ?? string.Empty,
                    Owner = modelItem.OwnedBy ?? string.Empty,
                    TargetDevice =
                        DetectTargetDevice(modelItem.Id),
                    ExecutionProvider =
                        DetectExecutionProvider(modelItem.Id)
                });
        }

        return models;
    }

    public async Task<IReadOnlyList<string>> GetAvailableDevicesAsync(
        string? preferredParentModel = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        IReadOnlyList<FoundryModel> availableModels =
            await GetModelsAsync(cancellationToken);

        IEnumerable<FoundryModel> candidates = availableModels;

        if (!string.IsNullOrWhiteSpace(preferredParentModel))
        {
            List<FoundryModel> preferredCandidates =
                availableModels
                    .Where(
                        model =>
                            string.Equals(
                                model.Parent,
                                preferredParentModel,
                                StringComparison.OrdinalIgnoreCase) ||
                            model.Id.Contains(
                                preferredParentModel,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (preferredCandidates.Count > 0)
            {
                candidates = preferredCandidates;
            }
        }

        return candidates
            .Select(model => model.TargetDevice)
            .Where(device => !string.IsNullOrWhiteSpace(device) && !device.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetDevicePriority)
            .ToList();
    }

    public async Task<FoundryModel> GetBestModelAsync(
        string? preferredParentModel = null,
        string? preferredDevice = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        IReadOnlyList<FoundryModel> availableModels =
            await GetModelsAsync(cancellationToken);

        if (availableModels.Count == 0)
        {
            throw new FoundryClientException(
                "Foundry Local does not expose any loaded model.");
        }

        IEnumerable<FoundryModel> candidates =
            availableModels;

        if (!string.IsNullOrWhiteSpace(
                preferredParentModel))
        {
            List<FoundryModel> preferredCandidates =
                availableModels
                    .Where(
                        model =>
                            string.Equals(
                                model.Parent,
                                preferredParentModel,
                                StringComparison.OrdinalIgnoreCase) ||
                            model.Id.Contains(
                                preferredParentModel,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (preferredCandidates.Count > 0)
            {
                candidates = preferredCandidates;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredDevice) &&
            !preferredDevice.Equals("AUTO", StringComparison.OrdinalIgnoreCase))
        {
            List<FoundryModel> deviceCandidates =
                candidates
                    .Where(
                        model =>
                            string.Equals(
                                model.TargetDevice,
                                preferredDevice,
                                StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (deviceCandidates.Count > 0)
            {
                candidates = deviceCandidates;
            }
            else
            {
                // Fallback matériel universel : si le device demandé (ex: NPU) n'a pas de modèle chargé,
                // on bascule de manière fluide sur le meilleur modèle disponible (GPU puis CPU) au lieu de lever une exception.
                System.Diagnostics.Debug.WriteLine(
                    $"[FoundryClient] Périphérique '{preferredDevice}' indisponible pour ce modèle. Repli automatique sur le matériel actif.");
            }
        }

        FoundryModel? selectedModel =
            candidates
                .OrderBy(
                    model =>
                        GetDevicePriority(
                            model.TargetDevice))
                .ThenBy(
                    model => model.Id,
                    StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

        if (selectedModel == null)
        {
            throw new FoundryClientException(
                "No compatible Foundry Local model was found.");
        }

        return selectedModel;
    }

    public async Task<FoundryInferenceResult> AskAsync(
        string systemPrompt,
        string userPrompt,
        string? preferredParentModel = "phi-4-mini",
        string? preferredDevice = null,
        double temperature = 0.2,
        int maxTokens = 700,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException(
                "The system prompt cannot be empty.",
                nameof(systemPrompt));
        }

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException(
                "The user prompt cannot be empty.",
                nameof(userPrompt));
        }

        FoundryModel selectedModel =
            await GetBestModelAsync(
                preferredParentModel,
                preferredDevice,
                cancellationToken);

        return await AskAsync(
            selectedModel,
            systemPrompt,
            userPrompt,
            temperature,
            maxTokens,
            cancellationToken);
    }

    public async Task<FoundryInferenceResult> AskStreamingAsync(
        string systemPrompt,
        string userPrompt,
        Action<string> onChunkReceived,
        string? preferredParentModel = "phi-4-mini",
        string? preferredDevice = null,
        double temperature = 0.2,
        int maxTokens = 700,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException(
                "The system prompt cannot be empty.",
                nameof(systemPrompt));
        }

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException(
                "The user prompt cannot be empty.",
                nameof(userPrompt));
        }

        if (onChunkReceived == null)
        {
            throw new ArgumentNullException(
                nameof(onChunkReceived));
        }

        FoundryModel selectedModel =
            await GetBestModelAsync(
                preferredParentModel,
                preferredDevice,
                cancellationToken);

        return await AskStreamingAsync(
            selectedModel,
            systemPrompt,
            userPrompt,
            onChunkReceived,
            temperature,
            maxTokens,
            cancellationToken);
    }

    public static async Task<bool> TryLoadModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "foundry.exe",
                    Arguments = $"model load {modelId}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            if (proc.Start())
            {
                await proc.WaitForExitAsync(cancellationToken);
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }

    public static async Task<bool> TryStartServerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "foundry.exe",
                    Arguments = "server start",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            if (proc.Start())
            {
                await proc.WaitForExitAsync(cancellationToken);
                return proc.ExitCode == 0;
            }
        }
        catch { }
        return false;
    }

    public async Task<FoundryInferenceResult> AskAsync(
        FoundryModel model,
        string systemPrompt,
        string userPrompt,
        double temperature = 0.2,
        int maxTokens = 700,
        CancellationToken cancellationToken = default,
        bool isRetry = false)
    {
        ThrowIfDisposed();

        if (model == null)
        {
            throw new ArgumentNullException(
                nameof(model));
        }

        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new ArgumentException(
                "The selected model does not contain an ID.",
                nameof(model));
        }

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException(
                "The system prompt cannot be empty.",
                nameof(systemPrompt));
        }

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException(
                "The user prompt cannot be empty.",
                nameof(userPrompt));
        }

        var request = new ChatCompletionApiRequest
        {
            Model = model.Id,
            Temperature = temperature,
            MaxTokens = maxTokens,
            Stream = false,
            Messages = new List<ChatMessageApiItem>
            {
                new ChatMessageApiItem
                {
                    Role = "system",
                    Content = systemPrompt
                },
                new ChatMessageApiItem
                {
                    Role = "user",
                    Content = userPrompt
                }
            }
        };

        string requestJson =
            JsonSerializer.Serialize(
                request,
                _jsonOptions);

        using var requestContent =
            new StringContent(
                requestJson,
                Encoding.UTF8,
                "application/json");

        DateTime startedUtc =
            DateTime.UtcNow;

        var stopwatch =
            Stopwatch.StartNew();

        HttpResponseMessage? response = null;

        try
        {
            response =
                await _httpClient.PostAsync(
                    ChatCompletionsEndpoint,
                    requestContent,
                    cancellationToken);

            string responseJson =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                if (!isRetry && responseJson.Contains("not loaded", StringComparison.OrdinalIgnoreCase))
                {
                    bool loaded = await TryLoadModelAsync(model.Id, cancellationToken);
                    if (loaded)
                    {
                        return await AskAsync(model, systemPrompt, userPrompt, temperature, maxTokens, cancellationToken, isRetry: true);
                    }
                }

                throw new FoundryClientException(
                    "Foundry Local returned an inference error.",
                    (int)response.StatusCode,
                    responseJson);
            }

            ChatCompletionApiResponse? completion =
                JsonSerializer.Deserialize<ChatCompletionApiResponse>(
                    responseJson,
                    _jsonOptions);

            if (completion == null)
            {
                throw new FoundryClientException(
                    "The Foundry Local response could not be parsed.");
            }

            string answer =
                ExtractAnswer(completion);

            int promptTokens =
                completion.Usage?.PromptTokens ?? 0;

            int completionTokens =
                completion.Usage?.CompletionTokens ?? 0;

            int totalTokens =
                completion.Usage?.TotalTokens ?? 0;

            double elapsedSeconds =
                stopwatch.Elapsed.TotalSeconds;

            double tokensPerSecond =
                elapsedSeconds > 0 &&
                completionTokens > 0
                    ? completionTokens / elapsedSeconds
                    : 0;

            string returnedModelId =
                string.IsNullOrWhiteSpace(completion.Model)
                    ? model.Id
                    : completion.Model;

            return new FoundryInferenceResult
            {
                Successful = true,
                Content = answer,
                ModelId = returnedModelId,
                ParentModel = model.Parent,
                TargetDevice =
                    DetectTargetDevice(returnedModelId),
                ExecutionProvider =
                    DetectExecutionProvider(returnedModelId),
                InferenceLocation = "Local device",
                EndpointType = "Loopback",
                StartedUtc = startedUtc,
                CompletedUtc = DateTime.UtcNow,
                LatencyMilliseconds =
                    stopwatch.ElapsedMilliseconds,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                TokensPerSecond =
                    Math.Round(tokensPerSecond, 1),
                RawResponseJson = responseJson
            };
        }
        finally
        {
            response?.Dispose();

            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }
        }
    }

    public async Task<FoundryInferenceResult> AskStreamingAsync(
        FoundryModel model,
        string systemPrompt,
        string userPrompt,
        Action<string> onChunkReceived,
        double temperature = 0.2,
        int maxTokens = 700,
        CancellationToken cancellationToken = default,
        bool isRetry = false)
    {
        ThrowIfDisposed();

        if (model == null)
        {
            throw new ArgumentNullException(
                nameof(model));
        }

        if (string.IsNullOrWhiteSpace(model.Id))
        {
            throw new ArgumentException(
                "The selected model does not contain an ID.",
                nameof(model));
        }

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException(
                "The system prompt cannot be empty.",
                nameof(systemPrompt));
        }

        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException(
                "The user prompt cannot be empty.",
                nameof(userPrompt));
        }

        if (onChunkReceived == null)
        {
            throw new ArgumentNullException(
                nameof(onChunkReceived));
        }

        var request = new ChatCompletionApiRequest
        {
            Model = model.Id,
            Temperature = temperature,
            MaxTokens = maxTokens,
            Stream = true,
            Messages = new List<ChatMessageApiItem>
            {
                new ChatMessageApiItem
                {
                    Role = "system",
                    Content = systemPrompt
                },
                new ChatMessageApiItem
                {
                    Role = "user",
                    Content = userPrompt
                }
            }
        };

        string requestJson =
            JsonSerializer.Serialize(
                request,
                _jsonOptions);

        using var requestMessage =
            new HttpRequestMessage(
                HttpMethod.Post,
                ChatCompletionsEndpoint)
            {
                Content = new StringContent(
                    requestJson,
                    Encoding.UTF8,
                    "application/json")
            };

        DateTime startedUtc =
            DateTime.UtcNow;

        var stopwatch =
            Stopwatch.StartNew();

        HttpResponseMessage? response = null;

        try
        {
            response =
                await _httpClient.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                stopwatch.Stop();

                if (!isRetry && errorBody.Contains("not loaded", StringComparison.OrdinalIgnoreCase))
                {
                    bool loaded = await TryLoadModelAsync(model.Id, cancellationToken);
                    if (loaded)
                    {
                        return await AskStreamingAsync(model, systemPrompt, userPrompt, onChunkReceived, temperature, maxTokens, cancellationToken, isRetry: true);
                    }
                }

                throw new FoundryClientException(
                    "Foundry Local returned an inference error.",
                    (int)response.StatusCode,
                    errorBody);
            }

            using var responseStream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using var reader =
                new StreamReader(
                    responseStream,
                    Encoding.UTF8);

            var accumulatedContent =
                new StringBuilder();

            var rawResponseBuilder =
                new StringBuilder();

            string returnedModelId =
                model.Id;

            int promptTokens = 0;
            int completionTokens = 0;
            int totalTokens = 0;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException)
                {
                    if (accumulatedContent.Length > 0)
                    {
                        // Some content was already streamed to the UI; finish gracefully
                        break;
                    }

                    if (!isRetry)
                    {
                        bool loaded = await TryLoadModelAsync(model.Id, cancellationToken);
                        if (loaded)
                        {
                            return await AskStreamingAsync(model, systemPrompt, userPrompt, onChunkReceived, temperature, maxTokens, cancellationToken, isRetry: true);
                        }
                    }

                    throw new FoundryClientException(
                        $"Le modèle local '{model.Id}' n'a pas pu démarrer dans Microsoft Foundry Local (flux interrompu avant le premier jeton).\n" +
                        "Le modèle n'était probablement pas encore chargé en mémoire ou en cours d'allocation NPU/GPU.\n" +
                        "💡 Si le problème persiste, basculez sur un autre accélérateur dans la barre supérieure.",
                        ex);
                }

                if (line == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                rawResponseBuilder.AppendLine(line);

                if (!line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string payload =
                    line.Substring("data: ".Length).Trim();

                if (payload.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                try
                {
                    ChatCompletionApiResponse? chunk =
                        JsonSerializer.Deserialize<ChatCompletionApiResponse>(
                            payload,
                            _jsonOptions);

                    if (chunk != null)
                    {
                        if (!string.IsNullOrWhiteSpace(chunk.Model))
                        {
                            returnedModelId = chunk.Model;
                        }

                        if (chunk.Choices != null && chunk.Choices.Count > 0)
                        {
                            ChatChoiceApiItem choice =
                                chunk.Choices[0];

                            string delta =
                                choice.Delta?.Content ??
                                choice.Message?.Content ??
                                string.Empty;

                            if (!string.IsNullOrEmpty(delta))
                            {
                                accumulatedContent.Append(delta);
                                onChunkReceived(delta);
                            }
                        }

                        if (chunk.Usage != null)
                        {
                            if (chunk.Usage.PromptTokens > 0)
                            {
                                promptTokens = chunk.Usage.PromptTokens;
                            }

                            if (chunk.Usage.CompletionTokens > 0)
                            {
                                completionTokens = chunk.Usage.CompletionTokens;
                            }

                            if (chunk.Usage.TotalTokens > 0)
                            {
                                totalTokens = chunk.Usage.TotalTokens;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore non-json frames if any
                }
            }

            stopwatch.Stop();

            double elapsedSeconds =
                stopwatch.Elapsed.TotalSeconds;

            if (completionTokens == 0 && accumulatedContent.Length > 0)
            {
                completionTokens = Math.Max(1, accumulatedContent.Length / 4);
                totalTokens = promptTokens + completionTokens;
            }

            double tokensPerSecond =
                elapsedSeconds > 0 && completionTokens > 0
                    ? completionTokens / elapsedSeconds
                    : 0;

            return new FoundryInferenceResult
            {
                Successful = true,
                Content = accumulatedContent.ToString(),
                ModelId = returnedModelId,
                ParentModel = model.Parent,
                TargetDevice =
                    DetectTargetDevice(returnedModelId),
                ExecutionProvider =
                    DetectExecutionProvider(returnedModelId),
                InferenceLocation = "Local device",
                EndpointType = "Loopback",
                StartedUtc = startedUtc,
                CompletedUtc = DateTime.UtcNow,
                LatencyMilliseconds =
                    stopwatch.ElapsedMilliseconds,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                TokensPerSecond =
                    Math.Round(tokensPerSecond, 1),
                RawResponseJson = rawResponseBuilder.ToString()
            };
        }
        finally
        {
            response?.Dispose();

            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }
        }
    }

    public async Task<string> SaveTelemetryAsync(
        FoundryInferenceResult result,
        string scenarioName,
        string logDirectory = "Logs",
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (result == null)
        {
            throw new ArgumentNullException(
                nameof(result));
        }

        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            scenarioName = "Unknown scenario";
        }

        string fullLogDirectory =
            Path.GetFullPath(logDirectory);

        Directory.CreateDirectory(
            fullLogDirectory);

        string timestamp =
            DateTime.UtcNow.ToString(
                "yyyyMMdd-HHmmss");

        string safeScenarioName =
            CreateSafeFileName(scenarioName);

        string telemetryFile =
            Path.Combine(
                fullLogDirectory,
                safeScenarioName +
                "-telemetry-" +
                timestamp +
                ".json");

        string responseFile =
            Path.Combine(
                fullLogDirectory,
                safeScenarioName +
                "-response-" +
                timestamp +
                ".json");

        var telemetry = new FoundryTelemetryRecord
        {
            TimestampUtc = DateTime.UtcNow,
            Scenario = scenarioName,
            Successful = result.Successful,
            ModelId = result.ModelId,
            ParentModel = result.ParentModel,
            TargetDevice = result.TargetDevice,
            ExecutionProvider = result.ExecutionProvider,
            InferenceLocation = result.InferenceLocation,
            EndpointType = result.EndpointType,
            FoundryBaseUrl = BaseUrl,
            StartedUtc = result.StartedUtc,
            CompletedUtc = result.CompletedUtc,
            LatencyMilliseconds =
                result.LatencyMilliseconds,
            PromptTokens = result.PromptTokens,
            CompletionTokens =
                result.CompletionTokens,
            TotalTokens = result.TotalTokens,
            TokensPerSecond =
                result.TokensPerSecond,
            ResponseCharacters =
                result.Content.Length
        };

        string telemetryJson =
            JsonSerializer.Serialize(
                telemetry,
                _jsonOptions);

        await File.WriteAllTextAsync(
            telemetryFile,
            telemetryJson,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(
                result.RawResponseJson))
        {
            await File.WriteAllTextAsync(
                responseFile,
                result.RawResponseJson,
                cancellationToken);
        }

        return telemetryFile;
    }

    public static void DisplayTelemetry(
        FoundryInferenceResult result)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("LOCAL AI TELEMETRY");
        Console.ResetColor();

        Console.WriteLine(
            "--------------------------------------------------");

        Console.WriteLine(
            "Model              : " +
            result.ModelId);

        Console.WriteLine(
            "Parent model       : " +
            result.ParentModel);

        Console.WriteLine(
            "Target device      : " +
            result.TargetDevice);

        Console.WriteLine(
            "Execution provider : " +
            result.ExecutionProvider);

        Console.WriteLine(
            "Inference location : " +
            result.InferenceLocation);

        Console.WriteLine(
            "Endpoint           : " +
            result.EndpointType);

        Console.WriteLine(
            "Prompt tokens      : " +
            result.PromptTokens);

        Console.WriteLine(
            "Completion tokens  : " +
            result.CompletionTokens);

        Console.WriteLine(
            "Total tokens       : " +
            result.TotalTokens);

        Console.WriteLine(
            "Total latency      : " +
            result.LatencyMilliseconds +
            " ms");

        Console.WriteLine(
            "Average throughput : " +
            result.TokensPerSecond.ToString("F1") +
            " tokens/s");

        Console.WriteLine(
            "--------------------------------------------------");
    }

    private static string ExtractAnswer(
        ChatCompletionApiResponse completion)
    {
        if (completion.Choices == null ||
            completion.Choices.Count == 0)
        {
            return string.Empty;
        }

        ChatChoiceApiItem firstChoice =
            completion.Choices[0];

        if (!string.IsNullOrWhiteSpace(
                firstChoice.Message?.Content))
        {
            return firstChoice.Message.Content;
        }

        if (!string.IsNullOrWhiteSpace(
                firstChoice.Delta?.Content))
        {
            return firstChoice.Delta.Content;
        }

        return string.Empty;
    }

    private static int GetDevicePriority(
        string targetDevice)
    {
        if (targetDevice.Equals(
                "NPU",
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (targetDevice.Equals(
                "GPU",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (targetDevice.Equals(
                "CPU",
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 99;
    }

    private static string DetectTargetDevice(
        string modelId)
    {
        if (modelId.Contains("-npu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("_npu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("/npu", StringComparison.OrdinalIgnoreCase) ||
            modelId.EndsWith("npu", StringComparison.OrdinalIgnoreCase))
        {
            return "NPU";
        }

        if (modelId.Contains("-gpu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("_gpu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("/gpu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("cuda", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("directml", StringComparison.OrdinalIgnoreCase) ||
            modelId.EndsWith("gpu", StringComparison.OrdinalIgnoreCase))
        {
            return "GPU";
        }

        if (modelId.Contains("-cpu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("_cpu", StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains("/cpu", StringComparison.OrdinalIgnoreCase) ||
            modelId.EndsWith("cpu", StringComparison.OrdinalIgnoreCase))
        {
            return "CPU";
        }

        return "Unknown";
    }

    private static string DetectExecutionProvider(
        string modelId)
    {
        if (modelId.Contains(
                "openvino",
                StringComparison.OrdinalIgnoreCase))
        {
            return "OpenVINOExecutionProvider";
        }

        if (modelId.Contains(
                "vitis",
                StringComparison.OrdinalIgnoreCase))
        {
            return "VitisExecutionProvider";
        }

        if (modelId.Contains(
                "qnn",
                StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains(
                "qualcomm",
                StringComparison.OrdinalIgnoreCase))
        {
            return "QualcommExecutionProvider";
        }

        if (modelId.Contains(
                "cuda",
                StringComparison.OrdinalIgnoreCase))
        {
            return "CUDAExecutionProvider";
        }

        if (modelId.Contains(
                "generic-gpu",
                StringComparison.OrdinalIgnoreCase) ||
            modelId.Contains(
                "webgpu",
                StringComparison.OrdinalIgnoreCase))
        {
            return "WebGpuExecutionProvider";
        }

        if (modelId.Contains(
                "cpu",
                StringComparison.OrdinalIgnoreCase))
        {
            return "CPUExecutionProvider";
        }

        return "Reported by Foundry Local";
    }

    private static string CreateSafeFileName(
        string value)
    {
        char[] invalidCharacters =
            Path.GetInvalidFileNameChars();

        var builder =
            new StringBuilder();

        foreach (char character in value)
        {
            if (invalidCharacters.Contains(character))
            {
                builder.Append('-');
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append('-');
            }
            else
            {
                builder.Append(
                    char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(FoundryClient));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }
}

public sealed class FoundryModel
{
    public string Id { get; set; } =
        string.Empty;

    public string Parent { get; set; } =
        string.Empty;

    public string Owner { get; set; } =
        string.Empty;

    public string TargetDevice { get; set; } =
        string.Empty;

    public string ExecutionProvider { get; set; } =
        string.Empty;
}

public sealed class FoundryInferenceResult
{
    public bool Successful { get; set; }

    public string Content { get; set; } =
        string.Empty;

    public string ModelId { get; set; } =
        string.Empty;

    public string ParentModel { get; set; } =
        string.Empty;

    public string TargetDevice { get; set; } =
        string.Empty;

    public string ExecutionProvider { get; set; } =
        string.Empty;

    public string InferenceLocation { get; set; } =
        string.Empty;

    public string EndpointType { get; set; } =
        string.Empty;

    public DateTime StartedUtc { get; set; }

    public DateTime CompletedUtc { get; set; }

    public long LatencyMilliseconds { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }

    public double TokensPerSecond { get; set; }

    public string RawResponseJson { get; set; } =
        string.Empty;
}

public sealed class FoundryClientException : Exception
{
    public FoundryClientException(
        string message)
        : base(message)
    {
    }

    public FoundryClientException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }

    public FoundryClientException(
        string message,
        int statusCode,
        string responseBody)
        : base(
            message +
            Environment.NewLine +
            "HTTP status: " +
            statusCode +
            Environment.NewLine +
            responseBody)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public int? StatusCode { get; }

    public string? ResponseBody { get; }
}

internal sealed class ModelsApiResponse
{
    [JsonPropertyName("data")]
    public List<ModelApiItem> Data { get; set; } =
        new List<ModelApiItem>();
}

internal sealed class ModelApiItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } =
        string.Empty;

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("owned_by")]
    public string? OwnedBy { get; set; }
}

internal sealed class ChatCompletionApiRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } =
        string.Empty;

    [JsonPropertyName("messages")]
    public List<ChatMessageApiItem> Messages { get; set; } =
        new List<ChatMessageApiItem>();

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

internal sealed class ChatMessageApiItem
{
    [JsonPropertyName("role")]
    public string Role { get; set; } =
        string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } =
        string.Empty;
}

internal sealed class ChatCompletionApiResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; } =
        string.Empty;

    [JsonPropertyName("choices")]
    public List<ChatChoiceApiItem> Choices { get; set; } =
        new List<ChatChoiceApiItem>();

    [JsonPropertyName("usage")]
    public UsageApiItem? Usage { get; set; }
}

internal sealed class ChatChoiceApiItem
{
    [JsonPropertyName("message")]
    public ChatMessageApiItem? Message { get; set; }

    [JsonPropertyName("delta")]
    public ChatMessageApiItem? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class UsageApiItem
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

internal sealed class FoundryTelemetryRecord
{
    public DateTime TimestampUtc { get; set; }

    public string Scenario { get; set; } =
        string.Empty;

    public bool Successful { get; set; }

    public string ModelId { get; set; } =
        string.Empty;

    public string ParentModel { get; set; } =
        string.Empty;

    public string TargetDevice { get; set; } =
        string.Empty;

    public string ExecutionProvider { get; set; } =
        string.Empty;

    public string InferenceLocation { get; set; } =
        string.Empty;

    public string EndpointType { get; set; } =
        string.Empty;

    public string FoundryBaseUrl { get; set; } =
        string.Empty;

    public DateTime StartedUtc { get; set; }

    public DateTime CompletedUtc { get; set; }

    public long LatencyMilliseconds { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int TotalTokens { get; set; }

    public double TokensPerSecond { get; set; }

    public int ResponseCharacters { get; set; }
}