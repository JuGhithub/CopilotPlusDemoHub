using System.Runtime.InteropServices;
using CopilotPlusDemoHub.Core;
using Xunit;

namespace DemoHub.Tests;

public class SiliconDetectorTests
{
    [Fact]
    public void SiliconDetector_Properties_AreNotNullOrEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(SiliconDetector.CpuName));
        Assert.False(string.IsNullOrWhiteSpace(SiliconDetector.GpuName));
        Assert.False(string.IsNullOrWhiteSpace(SiliconDetector.NpuName));
        Assert.True(SiliconDetector.EstimatedNpuTops >= 40);
        Assert.False(string.IsNullOrWhiteSpace(SiliconDetector.RecommendedLlmBackend));
        Assert.False(string.IsNullOrWhiteSpace(SiliconDetector.RecommendedVisionEngine));
    }

    [Fact]
    public void SiliconDetector_Architecture_MatchesRuntimeArchitecture()
    {
        var expectedArch = RuntimeInformation.ProcessArchitecture;
        Assert.Equal(expectedArch, SiliconDetector.Architecture);
    }
}
