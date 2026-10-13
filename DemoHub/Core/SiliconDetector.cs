using System;
using System.Management;
using System.Runtime.InteropServices;

namespace CopilotPlusDemoHub.Core;

public enum SiliconVendor
{
    Intel,
    Amd,
    Qualcomm,
    Unknown
}

public static class SiliconDetector
{
    private static readonly Lazy<SiliconInfo> _info = new(DetectHardware);

    public static SiliconVendor Vendor => _info.Value.Vendor;
    public static Architecture Architecture => _info.Value.Architecture;
    public static string CpuName => _info.Value.CpuName;
    public static string NpuName => _info.Value.NpuName;
    public static string GpuName => _info.Value.GpuName;
    public static string RecommendedLlmBackend => _info.Value.RecommendedLlmBackend;
    public static string RecommendedVisionEngine => _info.Value.RecommendedVisionEngine;
    public static int EstimatedNpuTops => _info.Value.EstimatedNpuTops;

    public sealed class SiliconInfo
    {
        public SiliconVendor Vendor { get; init; } = SiliconVendor.Unknown;
        public Architecture Architecture { get; init; } = Architecture.X64;
        public string CpuName { get; init; } = "Processeur Inconnu";
        public string NpuName { get; init; } = "NPU Dédié (Copilot+ PC)";
        public string GpuName { get; init; } = "GPU Intégré";
        public string RecommendedLlmBackend { get; init; } = "DirectML";
        public string RecommendedVisionEngine { get; init; } = "openvino";
        public int EstimatedNpuTops { get; init; } = 45;
    }

    private static SiliconInfo DetectHardware()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        string cpuName = string.Empty;
        string gpuName = string.Empty;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                cpuName = obj["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(cpuName)) break;
            }
        }
        catch
        {
            cpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "x86_64 Processor";
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    gpuName = name;
                    break;
                }
            }
        }
        catch
        {
            gpuName = "GPU DirectX 12";
        }

        var vendor = SiliconVendor.Unknown;
        string npuName = "NPU Copilot+ PC (40+ TOPS)";
        string recommendedLlm = "DirectML";
        string recommendedVision = "openvino";
        int tops = 45;

        string lowerCpu = cpuName.ToLowerInvariant();
        if (lowerCpu.Contains("intel") || lowerCpu.Contains("core ultra"))
        {
            vendor = SiliconVendor.Intel;
            npuName = lowerCpu.Contains("ultra") && (lowerCpu.Contains("258") || lowerCpu.Contains("268") || lowerCpu.Contains("288") || lowerCpu.Contains("series 2") || lowerCpu.Contains("série 2"))
                ? "Intel AI Boost (48 TOPS - Lunar Lake)"
                : "Intel AI Boost (NPU Copilot+)";
            tops = 48;
            recommendedLlm = "OpenVINO";
            recommendedVision = "openvino";
        }
        else if (lowerCpu.Contains("amd") || lowerCpu.Contains("ryzen"))
        {
            vendor = SiliconVendor.Amd;
            npuName = lowerCpu.Contains("ai 9") || lowerCpu.Contains("strix")
                ? "AMD Ryzen AI (XDNA 2 - 50 TOPS)"
                : "AMD Ryzen AI (XDNA NPU)";
            tops = 50;
            recommendedLlm = "DirectML";
            recommendedVision = "onnx_olive";
        }
        else if (lowerCpu.Contains("snapdragon") || lowerCpu.Contains("qualcomm") || arch == Architecture.Arm64)
        {
            vendor = SiliconVendor.Qualcomm;
            npuName = "Qualcomm Hexagon (45 TOPS)";
            tops = 45;
            recommendedLlm = "DirectML";
            recommendedVision = "onnx_olive";
        }

        return new SiliconInfo
        {
            Vendor = vendor,
            Architecture = arch,
            CpuName = string.IsNullOrEmpty(cpuName) ? "Processeur Copilot+ PC" : cpuName,
            NpuName = npuName,
            GpuName = string.IsNullOrEmpty(gpuName) ? "GPU Compatible DirectML" : gpuName,
            RecommendedLlmBackend = recommendedLlm,
            RecommendedVisionEngine = recommendedVision,
            EstimatedNpuTops = tops
        };
    }
}
