using System;
using System.Text.Json.Serialization;

namespace CopilotPlusDemoHub.Core;

/// <summary>
/// Preuve d'exécution matérielle normalisée (Runtime Evidence).
/// Certifie la traçabilité complète de l'accélérateur réellement alloué,
/// des périphériques rapportés par le runtime, de l'Execution Provider et des latences.
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

    [JsonPropertyName("runtime_reported_device")]
    public string RuntimeReportedDevice { get; init; } = "";

    [JsonPropertyName("execution_provider")]
    public string ExecutionProvider { get; init; } = "OpenVINO";

    [JsonPropertyName("fallback_used")]
    public bool FallbackUsed { get; init; } = false;

    /// <summary>
    /// Niveau de preuve : Verified (runtime rapporte formellement le silicium),
    /// Corroborated (runtime + compteur matériel concordants),
    /// Configured (uniquement demandé), Inferred (déduit), Unknown.
    /// </summary>
    [JsonPropertyName("evidence_level")]
    public string EvidenceLevel { get; init; } = "Unknown";

    [JsonPropertyName("evidence_sources")]
    public string[] EvidenceSources { get; init; } = [];

    [JsonPropertyName("compile_time_ms")]
    public double CompileTimeMs { get; init; } = 0.0;

    [JsonPropertyName("warmup_time_ms")]
    public double WarmupTimeMs { get; init; } = 0.0;

    [JsonPropertyName("inference_ms")]
    public double InferenceMs { get; init; } = 0.0;

    [JsonPropertyName("end_to_end_ms")]
    public double EndToEndMs { get; init; } = 0.0;

    /// <summary>
    /// Vrai uniquement si le NPU physique est formellement confirmé comme périphérique actif
    /// avec un niveau de preuve Verified ou Corroborated.
    /// </summary>
    [JsonIgnore]
    public bool IsNpuActive =>
        (EvidenceLevel.Equals("Verified", StringComparison.OrdinalIgnoreCase) ||
         EvidenceLevel.Equals("Corroborated", StringComparison.OrdinalIgnoreCase)) &&
        (ActiveDevice.Contains("NPU", StringComparison.OrdinalIgnoreCase) ||
         RuntimeReportedDevice.Contains("NPU", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Badge d'état visuel pour l'interface utilisateur WPF.
    /// </summary>
    [JsonIgnore]
    public string StatusBadge => IsNpuActive
        ? "⚡ NPU Intel AI Boost Actif (Vérifié)"
        : (FallbackUsed ? $"⚠️ Fallback ({ActiveDevice})" : $"⚙️ {ActiveDevice} ({EvidenceLevel})");
}
