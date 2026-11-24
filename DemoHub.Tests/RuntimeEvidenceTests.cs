using System.Text.Json;
using CopilotPlusDemoHub.Core;
using Xunit;

namespace DemoHub.Tests;

public class RuntimeEvidenceTests
{
    [Fact]
    public void RuntimeEvidence_WhenNpuActive_SetsIsNpuActiveAndBadgeCorrectly()
    {
        var evidence = new RuntimeEvidence
        {
            Runtime = "OpenVINO",
            Model = "yolov8n-seg",
            Precision = "FP16",
            RequestedDevice = "NPU",
            AvailableDevices = ["CPU", "GPU", "NPU"],
            ActiveDevice = "NPU.0",
            ExecutionProvider = "OpenVINO VPUX",
            FallbackUsed = false,
            EvidenceLevel = "Verified",
            CompileTimeMs = 1200.0,
            InferenceMs = 34.2,
            EndToEndMs = 52.0
        };

        Assert.True(evidence.IsNpuActive);
        Assert.Contains("NPU Intel AI Boost Actif (Vérifié)", evidence.StatusBadge);
        Assert.False(evidence.FallbackUsed);
        Assert.Equal(34.2, evidence.InferenceMs);
    }

    [Fact]
    public void RuntimeEvidence_WhenNpuNotVerified_DoesNotShowVerifiedBadge()
    {
        var evidence = new RuntimeEvidence
        {
            Runtime = "OpenVINO",
            Model = "yolov8n-seg",
            RequestedDevice = "NPU",
            ActiveDevice = "NPU",
            EvidenceLevel = "Unknown"
        };

        Assert.False(evidence.IsNpuActive);
        Assert.Contains("Unknown", evidence.StatusBadge);
    }

    [Fact]
    public void RuntimeEvidence_WhenFallbackToCpu_SetsFallbackBadge()
    {
        var evidence = new RuntimeEvidence
        {
            Runtime = "OpenVINO",
            Model = "yolov8s-world",
            RequestedDevice = "NPU",
            ActiveDevice = "CPU",
            FallbackUsed = true
        };

        Assert.False(evidence.IsNpuActive);
        Assert.Contains("Fallback", evidence.StatusBadge);
        Assert.Contains("CPU", evidence.StatusBadge);
    }

    [Fact]
    public void RuntimeEvidence_SerializationRoundtrip_PreservesAllFields()
    {
        var evidence = new RuntimeEvidence
        {
            Runtime = "ONNXRuntime",
            RuntimeVersion = "1.20.0",
            Model = "ch_PP-OCRv4_rec_infer",
            Precision = "FP16",
            RequestedDevice = "GPU",
            AvailableDevices = ["CPU", "DmlExecutionProvider"],
            ActiveDevice = "DmlExecutionProvider",
            ExecutionProvider = "DirectML",
            EvidenceLevel = "Verified",
            EvidenceSources = ["DirectML Adapter"],
            InferenceMs = 18.5,
            EndToEndMs = 24.1
        };

        var json = JsonSerializer.Serialize(evidence);
        var restored = JsonSerializer.Deserialize<RuntimeEvidence>(json);

        Assert.NotNull(restored);
        Assert.Equal("ONNXRuntime", restored.Runtime);
        Assert.Equal("DmlExecutionProvider", restored.ActiveDevice);
        Assert.Equal("Verified", restored.EvidenceLevel);
        Assert.Equal(18.5, restored.InferenceMs);
    }
}
