using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace CopilotPlusDemoHub.Core;

public sealed class HardwareTelemetryService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;

    private static ulong ToUInt64(System.Runtime.InteropServices.ComTypes.FILETIME ft)
    {
        return ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
    }

    private readonly List<HardwareSnapshot> _snapshots = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _samplingTask;
    private HardwareSnapshot _latestSnapshot = new();
    private bool _disposed;

    public HardwareTelemetryService()
    {
        Start(TimeSpan.FromSeconds(1));
    }

    public void Start(TimeSpan? interval = null)
    {
        ThrowIfDisposed();
        Stop();

        lock (_lock)
        {
            _snapshots.Clear();
        }

        _cts = new CancellationTokenSource();
        TimeSpan pollingInterval = interval ?? TimeSpan.FromSeconds(1);

        _samplingTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    HardwareSnapshot snapshot = CaptureCurrentSnapshot();
                    lock (_lock)
                    {
                        _latestSnapshot = snapshot;
                        _snapshots.Add(snapshot);
                    }
                }
                catch
                {
                }

                try
                {
                    await Task.Delay(pollingInterval, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try
            {
                _samplingTask?.Wait(2000);
            }
            catch
            {
            }
            _cts.Dispose();
            _cts = null;
            _samplingTask = null;
        }
    }

    public HardwareSnapshot GetCurrentSnapshot()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_latestSnapshot != null && _latestSnapshot.Timestamp != default)
            {
                return _latestSnapshot;
            }
        }
        var current = CaptureCurrentSnapshot();
        lock (_lock)
        {
            _latestSnapshot = current;
        }
        return current;
    }

    public HardwareSummary GetSummary()
    {
        ThrowIfDisposed();

        List<HardwareSnapshot> samples;
        lock (_lock)
        {
            samples = new List<HardwareSnapshot>(_snapshots);
        }

        if (samples.Count == 0)
        {
            HardwareSnapshot current = CaptureCurrentSnapshot();
            samples.Add(current);
        }

        HardwareSnapshot first = samples[0];
        HardwareSnapshot last = samples[^1];

        TimeSpan duration = last.Timestamp - first.Timestamp;
        if (duration.TotalSeconds < 1)
        {
            duration = TimeSpan.FromSeconds(1);
        }

        var dischargeRates = samples
            .Where(s => s.DischargeRateWatts > 0)
            .Select(s => s.DischargeRateWatts)
            .ToList();

        double avgDischargeWatts = dischargeRates.Count > 0
            ? dischargeRates.Average()
            : 0;

        double maxDischargeWatts = dischargeRates.Count > 0
            ? dischargeRates.Max()
            : 0;

        double minDischargeWatts = dischargeRates.Count > 0
            ? dischargeRates.Min()
            : 0;

        var temperatures = samples
            .Where(s => s.TemperatureCelsius.HasValue)
            .Select(s => s.TemperatureCelsius!.Value)
            .ToList();

        double? avgTemp = temperatures.Count > 0 ? temperatures.Average() : null;
        double? maxTemp = temperatures.Count > 0 ? temperatures.Max() : null;

        var cpuUsages = samples
            .Select(s => s.CpuUsagePercent)
            .Where(c => c > 0)
            .ToList();

        double avgCpu = cpuUsages.Count > 0 ? cpuUsages.Average() : 0;
        double maxCpu = cpuUsages.Count > 0 ? cpuUsages.Max() : 0;

        var gpuUsages = samples
            .Select(s => s.GpuUsagePercent)
            .Where(g => g > 0)
            .ToList();

        double avgGpu = gpuUsages.Count > 0 ? gpuUsages.Average() : 0;
        double maxGpu = gpuUsages.Count > 0 ? gpuUsages.Max() : 0;

        var npuUsages = samples
            .Select(s => s.NpuUsagePercent)
            .Where(n => n > 0)
            .ToList();

        double avgNpu = npuUsages.Count > 0 ? npuUsages.Average() : 0;
        double maxNpu = npuUsages.Count > 0 ? npuUsages.Max() : 0;

        double batteryPercentDelta = first.BatteryPercent - last.BatteryPercent;
        double capacityMwhDelta = Math.Max(0, first.RemainingCapacityMwh - last.RemainingCapacityMwh);

        double energyUsedWattHours = (avgDischargeWatts * duration.TotalHours);
        if (energyUsedWattHours <= 0 && capacityMwhDelta > 0)
        {
            energyUsedWattHours = capacityMwhDelta / 1000.0;
        }

        bool? fanActive = samples.Any(s => s.FanActive == true);

        return new HardwareSummary
        {
            StartedUtc = first.Timestamp,
            CompletedUtc = last.Timestamp,
            Duration = duration,
            SampleCount = samples.Count,
            IsOnBattery = samples.Any(s => s.IsOnBattery),
            InitialBatteryPercent = first.BatteryPercent,
            FinalBatteryPercent = last.BatteryPercent,
            BatteryPercentDelta = Math.Max(0, batteryPercentDelta),
            InitialCapacityMwh = first.RemainingCapacityMwh,
            FinalCapacityMwh = last.RemainingCapacityMwh,
            CapacityMwhDelta = capacityMwhDelta,
            AverageDischargeWatts = Math.Round(avgDischargeWatts, 2),
            PeakDischargeWatts = Math.Round(maxDischargeWatts, 2),
            MinDischargeWatts = Math.Round(minDischargeWatts, 2),
            EnergyConsumedWattHours = Math.Round(energyUsedWattHours, 3),
            AverageCpuPercent = Math.Round(avgCpu, 1),
            PeakCpuPercent = Math.Round(maxCpu, 1),
            AverageGpuPercent = Math.Round(avgGpu, 1),
            PeakGpuPercent = Math.Round(maxGpu, 1),
            AverageNpuPercent = Math.Round(avgNpu, 1),
            PeakNpuPercent = Math.Round(maxNpu, 1),
            AverageTemperatureCelsius = avgTemp.HasValue ? Math.Round(avgTemp.Value, 1) : null,
            PeakTemperatureCelsius = maxTemp.HasValue ? Math.Round(maxTemp.Value, 1) : null,
            FanActive = fanActive
        };
    }

    private HardwareSnapshot CaptureCurrentSnapshot()
    {
        int batteryPercent = 100;
        bool isOnBattery = false;

        if (GetSystemPowerStatus(out SystemPowerStatus status))
        {
            batteryPercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;
            isOnBattery = status.ACLineStatus == 0;
        }

        double dischargeWatts = 0;
        double remainingCapacityMwh = 0;
        double voltageVolts = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\wmi",
                "SELECT DischargeRate, RemainingCapacity, Voltage, PowerOnline FROM BatteryStatus WHERE Active = TRUE");

            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["DischargeRate"] != null &&
                    double.TryParse(obj["DischargeRate"].ToString(), out double rateMw))
                {
                    dischargeWatts = rateMw / 1000.0;
                }

                if (obj["RemainingCapacity"] != null &&
                    double.TryParse(obj["RemainingCapacity"].ToString(), out double cap))
                {
                    remainingCapacityMwh = cap;
                }

                if (obj["Voltage"] != null &&
                    double.TryParse(obj["Voltage"].ToString(), out double vMv))
                {
                    voltageVolts = vMv / 1000.0;
                }

                if (obj["PowerOnline"] != null &&
                    bool.TryParse(obj["PowerOnline"].ToString(), out bool powerOnline))
                {
                    isOnBattery = !powerOnline;
                }
            }
        }
        catch
        {
            try
            {
                using var batterySearcher = new ManagementObjectSearcher(
                    "SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");

                foreach (ManagementObject b in batterySearcher.Get())
                {
                    if (b["EstimatedChargeRemaining"] != null &&
                        int.TryParse(b["EstimatedChargeRemaining"].ToString(), out int estPct))
                    {
                        batteryPercent = estPct;
                    }
                    if (b["BatteryStatus"] != null &&
                        int.TryParse(b["BatteryStatus"].ToString(), out int bStatus))
                    {
                        isOnBattery = (bStatus == 1);
                    }
                }
            }
            catch
            {
            }
        }

        double cpuUsage = GetCpuUsage();
        var (gpuUsage, npuUsage) = GetGpuAndNpuUsage();

        double? temperature = null;
        try
        {
            using var tempSearcher = new ManagementObjectSearcher(
                "root\\wmi",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            foreach (ManagementObject t in tempSearcher.Get())
            {
                if (t["CurrentTemperature"] != null &&
                    double.TryParse(t["CurrentTemperature"].ToString(), out double tempRaw))
                {
                    double tempC = (tempRaw - 2732.0) / 10.0;
                    if (tempC is > 10 and < 120)
                    {
                        temperature = tempC;
                        break;
                    }
                }
            }
        }
        catch
        {
        }

        bool? fanActive = null;
        try
        {
            using var fanSearcher = new ManagementObjectSearcher(
                "SELECT ActiveCooling FROM Win32_Fan");

            foreach (ManagementObject f in fanSearcher.Get())
            {
                if (f["ActiveCooling"] != null &&
                    bool.TryParse(f["ActiveCooling"].ToString(), out bool active))
                {
                    fanActive = active;
                    break;
                }
            }
        }
        catch
        {
        }

        var (fanLevel, fanRpm) = EstimateFanState(
            isOnBattery,
            dischargeWatts,
            cpuUsage,
            gpuUsage,
            npuUsage,
            fanActive);

        return new HardwareSnapshot
        {
            Timestamp = DateTime.UtcNow,
            BatteryPercent = batteryPercent,
            IsOnBattery = isOnBattery,
            DischargeRateWatts = dischargeWatts,
            RemainingCapacityMwh = remainingCapacityMwh,
            VoltageVolts = voltageVolts,
            CpuUsagePercent = cpuUsage,
            GpuUsagePercent = gpuUsage,
            NpuUsagePercent = npuUsage,
            TemperatureCelsius = temperature,
            FanActive = fanActive,
            FanLevel = fanLevel,
            FanEstimatedRpm = fanRpm
        };
    }

    private static readonly HashSet<string> _gpuLuids = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _npuLuids = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastLuidScan = DateTime.MinValue;
    private static readonly object _luidLock = new();

    private static void RefreshAdapterLuids()
    {
        if (DateTime.UtcNow - _lastLuidScan < TimeSpan.FromSeconds(60))
        {
            lock (_luidLock)
            {
                if (_gpuLuids.Count > 0 || _npuLuids.Count > 0) return;
            }
        }

        try
        {
            var discoveredGpu = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allDiscovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PerfRawData_GPUPerformanceCounters_GPUEngine");

            foreach (ManagementObject obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                int luidIdx = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                if (luidIdx < 0) continue;

                int physIdx = name.IndexOf("_phys_", luidIdx, StringComparison.OrdinalIgnoreCase);
                if (physIdx < 0) continue;

                string luid = name.Substring(luidIdx + 5, physIdx - (luidIdx + 5));
                allDiscovered.Add(luid);

                if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    discoveredGpu.Add(luid);
                }
            }

            lock (_luidLock)
            {
                _gpuLuids.Clear();
                _npuLuids.Clear();

                foreach (var g in discoveredGpu)
                {
                    _gpuLuids.Add(g);
                }

                foreach (var l in allDiscovered)
                {
                    if (!_gpuLuids.Contains(l))
                    {
                        _npuLuids.Add(l);
                    }
                }

                _lastLuidScan = DateTime.UtcNow;
            }
        }
        catch
        {
        }
    }

    private static (double gpu, double npu) GetGpuAndNpuUsage()
    {
        RefreshAdapterLuids();

        double gpuTotal = 0;
        double npuTotal = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine WHERE UtilizationPercentage > 0");

            foreach (ManagementObject obj in searcher.Get())
            {
                string? name = obj["Name"]?.ToString();
                if (string.IsNullOrEmpty(name)) continue;

                if (double.TryParse(obj["UtilizationPercentage"]?.ToString(), out double util) && util > 0)
                {
                    string? luid = null;
                    int luidIdx = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                    if (luidIdx >= 0)
                    {
                        int physIdx = name.IndexOf("_phys_", luidIdx, StringComparison.OrdinalIgnoreCase);
                        if (physIdx > luidIdx)
                        {
                            luid = name.Substring(luidIdx + 5, physIdx - (luidIdx + 5));
                        }
                    }

                    bool isNpu = false;
                    lock (_luidLock)
                    {
                        if (luid != null && _npuLuids.Contains(luid))
                        {
                            isNpu = true;
                        }
                    }

                    if (isNpu || name.Contains("engtype_Neural", StringComparison.OrdinalIgnoreCase))
                    {
                        npuTotal += util;
                    }
                    else
                    {
                        gpuTotal += util;
                    }
                }
            }
        }
        catch
        {
        }

        return (Math.Min(100.0, Math.Round(gpuTotal, 1)), Math.Min(100.0, Math.Round(npuTotal, 1)));
    }

    private static (FanAcousticLevel level, string estimatedRpm) EstimateFanState(
        bool isOnBattery,
        double dischargeWatts,
        double cpuPercent,
        double gpuPercent,
        double npuPercent,
        bool? hardwareFanActive)
    {
        // High load condition: Heavy GPU stress or CPU saturation, or power > 28W
        if (dischargeWatts > 28.0 || gpuPercent > 65.0 || cpuPercent > 70.0)
        {
            return (FanAcousticLevel.High, ">3500 RPM");
        }

        // Active cooling condition: Moderate-high GPU/CPU load, or power 18W-28W
        if (dischargeWatts > 18.0 || gpuPercent > 25.0 || cpuPercent > 35.0)
        {
            return (FanAcousticLevel.Active, "~2500 RPM");
        }

        // Low / whisper condition: Mild load, or power 10W-18W
        if (dischargeWatts > 10.0 || gpuPercent > 12.0 || cpuPercent > 20.0)
        {
            return (FanAcousticLevel.Low, "~1200 RPM");
        }

        // Low power / NPU inference or idle: Completely silent / passive cooling
        return (FanAcousticLevel.Silent, "0 RPM");
    }

    private static double GetCpuUsage()
    {
        try
        {
            if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            {
                ulong idle = ToUInt64(idleFt);
                ulong kernel = ToUInt64(kernelFt);
                ulong user = ToUInt64(userFt);

                if (_prevIdleTime == 0)
                {
                    _prevIdleTime = idle;
                    _prevKernelTime = kernel;
                    _prevUserTime = user;
                    return 0;
                }

                ulong usrDiff = user - _prevUserTime;
                ulong kerDiff = kernel - _prevKernelTime;
                ulong idleDiff = idle - _prevIdleTime;

                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;

                ulong sysDiff = usrDiff + kerDiff;
                if (sysDiff > 0)
                {
                    double cpu = ((double)(sysDiff - idleDiff) / sysDiff) * 100.0;
                    return Math.Clamp(Math.Round(cpu, 1), 0, 100.0);
                }
            }
        }
        catch
        {
        }
        return 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(HardwareTelemetryService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}

public enum FanAcousticLevel
{
    Silent,
    Low,
    Active,
    High
}

public sealed class HardwareSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int BatteryPercent { get; set; } = 100;
    public bool IsOnBattery { get; set; }
    public double DischargeRateWatts { get; set; }
    public double RemainingCapacityMwh { get; set; }
    public double VoltageVolts { get; set; }
    public double CpuUsagePercent { get; set; }
    public double GpuUsagePercent { get; set; }
    public double NpuUsagePercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public bool? FanActive { get; set; }
    public FanAcousticLevel FanLevel { get; set; } = FanAcousticLevel.Silent;
    public string FanEstimatedRpm { get; set; } = "0 RPM";
}

public sealed class HardwareSummary
{
    public DateTime StartedUtc { get; set; }
    public DateTime CompletedUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public int SampleCount { get; set; }
    public bool IsOnBattery { get; set; }
    public int InitialBatteryPercent { get; set; }
    public int FinalBatteryPercent { get; set; }
    public double BatteryPercentDelta { get; set; }
    public double InitialCapacityMwh { get; set; }
    public double FinalCapacityMwh { get; set; }
    public double CapacityMwhDelta { get; set; }
    public double AverageDischargeWatts { get; set; }
    public double PeakDischargeWatts { get; set; }
    public double MinDischargeWatts { get; set; }
    public double EnergyConsumedWattHours { get; set; }
    public double AverageCpuPercent { get; set; }
    public double PeakCpuPercent { get; set; }
    public double AverageGpuPercent { get; set; }
    public double PeakGpuPercent { get; set; }
    public double AverageNpuPercent { get; set; }
    public double PeakNpuPercent { get; set; }
    public double? AverageTemperatureCelsius { get; set; }
    public double? PeakTemperatureCelsius { get; set; }
    public bool? FanActive { get; set; }
}
