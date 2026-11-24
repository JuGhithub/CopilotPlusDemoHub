using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotPlusDemoHub.Infrastructure.Python;

/// <summary>
/// Contrat de message JSON Lines de communication inter-processus (IPC) demohub/1.0.
/// </summary>
public record JsonLineMessage
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = "demohub/1.0";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "";

    [JsonPropertyName("timestamp")]
    public double Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}

public record ReadyMessage : JsonLineMessage
{
    [JsonPropertyName("service")]
    public string Service { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "ready";
}

public record ProgressMessage : JsonLineMessage
{
    [JsonPropertyName("progress")]
    public double Progress { get; init; } = 0.0;

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public record ResultMessage<T> : JsonLineMessage
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

public record ErrorMessage : JsonLineMessage
{
    [JsonPropertyName("error")]
    public string Error { get; init; } = "";

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("traceback")]
    public string? Traceback { get; init; }
}

/// <summary>
/// Parseur utilitaire pour désérialiser les lignes reçues sur stdout.
/// </summary>
public static class JsonLineParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParse(string line, out JsonElement element, out string type)
    {
        element = default;
        type = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        line = line.Trim();
        if (!line.StartsWith("{") || !line.EndsWith("}"))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement.Clone();

            if (root.TryGetProperty("type", out var typeProp))
            {
                type = typeProp.GetString() ?? string.Empty;
                element = root;
                return true;
            }
        }
        catch (JsonException)
        {
            // Ligne non JSON (ex: logs parasites)
        }

        return false;
    }

    public static T? Deserialize<T>(string line) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(line, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
