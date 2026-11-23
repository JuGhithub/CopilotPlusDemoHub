using System;
using System.IO;
using System.Threading.Tasks;
using CopilotPlusDemoHub.Core;
using Xunit;

namespace DemoHub.Tests;

public class EnterpriseTelemetryTests
{
    [Fact]
    public void FleetTelemetryRecord_CalculatesEnergyAndDisplayProperties()
    {
        var record = new FleetTelemetryRecord
        {
            DemoName = "Studio PDF - Reconnaissance OCR",
            ExecutionTimeMs = 2000,
            EstimatedPowerWatts = 2.5,
            EnergyJoules = 5.0,
            EnergySavedVsGpuJoules = 28.0,
            EffectiveSilicon = "NPU",
            MetadataJson = "{\"sample\":true}"
        };

        Assert.True(record.IsSample);
        Assert.Equal("~2.5 W", record.FormattedPower);
        Assert.Equal("5.000 J", record.FormattedEnergy);
        Assert.Equal("+28.000 J", record.FormattedSavedVsGpu);
        Assert.Equal("Studio PDF - Reconnaissance OCR · exemple", record.DisplayDemoName);
    }

    [Fact]
    public void FleetTelemetryRecord_RealMeasurement_IsNotSample()
    {
        var record = new FleetTelemetryRecord
        {
            DemoName = "Audio Studio - Diarisation",
            ExecutionTimeMs = 1500,
            EffectiveSilicon = "NPU",
            MetadataJson = "{}"
        };

        Assert.False(record.IsSample);
        Assert.Equal("Audio Studio - Diarisation", record.DisplayDemoName);
    }
}
