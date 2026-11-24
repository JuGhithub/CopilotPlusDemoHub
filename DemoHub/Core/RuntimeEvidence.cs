using System;
using System.Text.Json.Serialization;

namespace CopilotPlusDemoHub.Core;

/// <summary>
/// Preuve d'exécution matérielle normalisée (Runtime Evidence)
/// Fournit la traçabilité complète de l'accélérateur réellement utilisé,
/// de l'Execution Provider, de la précision et des latences distinctes.
/// </summary>
public record RuntimeEvidence
{
    [JsonPropertyName("runtime")]
    public string Runtime { get; init; } = "OpenVINO";

    [JsonPropertyName("runtime_version")]
    public string RuntimeVersion { get; init; } = "";

    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("precision")]
    public string Precision { get; init; } = "FP16";

    [JsonPropertyName("requested_device")]
    public string RequestedDevice { get; init; } = "NPU";

    [JsonPropertyName("available_devices")]
    public string[] AvailableDevices { get; init; } = [];

    [JsonPropertyName("active_device")]
    public string ActiveDevice { get; init; } = "CPU";

    [JsonPropertyName("execution_provider")]
    public string ExecutionProvider { get; init; } = "OpenVINO";

    [JsonPropertyName("fallback_used")]
    public bool FallbackUsed { get; init; } = false;

    [JsonPropertyName("compile_time_ms")]
    public double CompileTimeMs { get; init; } = 0.0;

    [JsonPropertyName("warmup_time_ms")]
    public double WarmupTimeMs { get; init; } = 0.0;

    [JsonPropertyName("inference_ms")]
    public double InferenceMs { get; init; } = 0.0;

    [JsonPropertyName("end_to_end_ms")]
    public double EndToEndMs { get; init; } = 0.0;

    /// <summary>
    /// Vrai uniquement si le NPU physique est formellement confirmé comme périphérique actif.
    /// </summary>
    [JsonIgnore]
    public bool IsNpuActive => ActiveDevice.Contains("NPU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Badge d'état visuel pour l'interface utilisateur WPF.
    /// </summary>
    [JsonIgnore]
    public string StatusBadge => IsNpuActive
        ? "⚡ NPU Intel AI Boost Actif"
        : (FallbackUsed ? $"⚠️ Fallback ({ActiveDevice})" : $"⚙️ {ActiveDevice}");
}
