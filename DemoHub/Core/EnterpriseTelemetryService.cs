using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace CopilotPlusDemoHub.Core;

public class FleetTelemetryRecord
{
    public long Id { get; set; }
    public string TimestampUtc { get; set; } = string.Empty;
    public string TimestampLocal { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string PcModel { get; set; } = string.Empty;
    public string Processor { get; set; } = string.Empty;
    public string DemoName { get; set; } = string.Empty;
    public string RequestedSilicon { get; set; } = string.Empty;
    public string EffectiveSilicon { get; set; } = string.Empty;
    public string SiliconDeviceName { get; set; } = string.Empty;
    public int ExecutionTimeMs { get; set; }
    public double ThroughputTokPerSec { get; set; }
    public double EstimatedPowerWatts { get; set; }
    public double EnergyJoules { get; set; }
    public double EnergySavedVsGpuJoules { get; set; }
    public bool IsNpuFallbackAnomaly { get; set; }
    public double PerformanceDriftPercent { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string MetadataJson { get; set; } = string.Empty;

    public string FormattedLatency => $"{ExecutionTimeMs} ms";
    public string FormattedPower => $"~{EstimatedPowerWatts:F1} W";
    public string FormattedEnergy => $"{EnergyJoules:F3} J";
    public string FormattedSavedVsGpu => EnergySavedVsGpuJoules > 0 ? $"+{EnergySavedVsGpuJoules:F3} J" : "0 J";
    public string AnomalyBadge => IsNpuFallbackAnomaly ? "⚠️ Fallback NPU" : (PerformanceDriftPercent > 25.0 ? $"⚠️ Drift +{PerformanceDriftPercent:F0}%" : "✅ Normal");
    public string StatusColor => (IsNpuFallbackAnomaly || PerformanceDriftPercent > 25.0) ? "#7F1D1D" : "#064E3B";
    public string StatusForeground => (IsNpuFallbackAnomaly || PerformanceDriftPercent > 25.0) ? "#FCA5A5" : "#6EE7B7";
    public string SiliconBadgeColor => EffectiveSilicon.ToUpperInvariant() switch
    {
        "NPU" => "#064E3B",
        "GPU" => "#78350F",
        _ => "#1E3A8A"
    };
    public string SiliconBadgeForeground => EffectiveSilicon.ToUpperInvariant() switch
    {
        "NPU" => "#34D399",
        "GPU" => "#FBBF24",
        _ => "#93C5FD"
    };
}

public class FleetAnalyticsSummary
{
    public int TotalInferences { get; set; }
    public int NpuInferences { get; set; }
    public int GpuInferences { get; set; }
    public int CpuInferences { get; set; }
    public double NpuAdoptionRatePercent { get; set; }
    public int AnomalyCount { get; set; }
    public double TotalEnergyConsumedJoules { get; set; }
    public double TotalEnergyConsumedWattHours { get; set; }
    public double TotalEnergySavedVsGpuJoules { get; set; }
    public double TotalEnergySavedVsGpuWattHours { get; set; }
    public double Co2AvoidedGrams { get; set; }
    public double AverageLatencyNpuMs { get; set; }
    public double AverageLatencyGpuMs { get; set; }
    public double AverageLatencyCpuMs { get; set; }
    public double MaxPerformanceDriftPercent { get; set; }
}

public sealed class EnterpriseTelemetryService
{
    private static readonly Lazy<EnterpriseTelemetryService> _instance =
        new(() => new EnterpriseTelemetryService());

    public static EnterpriseTelemetryService Instance => _instance.Value;

    public string DeviceSerialNumber { get; private set; } = "UNKNOWN-TAG";
    public string PcModel { get; private set; } = "Copilot+ PC";
    public string PcManufacturer { get; private set; } = "OEM";
    public string ProcessorName { get; private set; } = "Intel Core Ultra";
    public string Hostname { get; private set; } = Environment.MachineName;
    public string OsVersion { get; private set; } = Environment.OSVersion.ToString();
    public bool HasNpu { get; private set; } = true;
    public string NpuName { get; private set; } = "Intel(R) AI Boost (48 TOPS)";

    private readonly string _dbPath;
    private readonly string _jsonlPath;
    private readonly string _connectionString;
    private readonly object _lock = new();

    // In-memory baseline tracking for Performance Drift (DemoName + Silicon -> List of initial ms)
    private readonly ConcurrentDictionary<string, List<int>> _baselineLatencies = new();

    public event Action? OnRecordAdded;

    private EnterpriseTelemetryService()
    {
        DiscoverHardwareIdentity();

        string appData = AppDomain.CurrentDomain.BaseDirectory;
        string telemetryDir = Path.Combine(appData, "Data", "Telemetry");
        Directory.CreateDirectory(telemetryDir);

        _dbPath = Path.Combine(telemetryDir, "enterprise_telemetry.db");
        _jsonlPath = Path.Combine(telemetryDir, "audit_log.jsonl");
        _connectionString = $"Data Source={_dbPath};";

        InitializeDatabase();
    }

    private void DiscoverHardwareIdentity()
    {
        try
        {
            Hostname = Environment.MachineName;
            OsVersion = Environment.OSVersion.VersionString;

            // 1. BIOS Serial Number (Service Tag)
            try
            {
                using var searcherBios = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BIOS");
                foreach (ManagementObject obj in searcherBios.Get())
                {
                    string? s = obj["SerialNumber"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(s) && !s.Equals("Default string", StringComparison.OrdinalIgnoreCase))
                    {
                        DeviceSerialNumber = s;
                        break;
                    }
                }
            }
            catch { }

            // 2. Computer Model & Manufacturer
            try
            {
                using var searcherCs = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcherCs.Get())
                {
                    PcManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? PcManufacturer;
                    PcModel = obj["Model"]?.ToString()?.Trim() ?? PcModel;
                }
            }
            catch { }

            // 3. Processor
            try
            {
                using var searcherCpu = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementObject obj in searcherCpu.Get())
                {
                    string? name = obj["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        ProcessorName = name;
                        break;
                    }
                }
            }
            catch { }

            // 4. Default NPU detection
            HasNpu = true;
            NpuName = "Intel(R) AI Boost (48 TOPS)";
        }
        catch
        {
            // Graceful fallback
            DeviceSerialNumber = "PC-" + Math.Abs(Environment.MachineName.GetHashCode()).ToString("X8");
            PcModel = "Copilot+ PC (Intel Lunar Lake)";
        }
    }

    private void InitializeDatabase()
    {
        lock (_lock)
        {
            try
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                string ddl = @"
                    CREATE TABLE IF NOT EXISTS AiFleetAuditLog (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TimestampUtc TEXT NOT NULL,
                        TimestampLocal TEXT NOT NULL,
                        DeviceId TEXT NOT NULL,
                        Hostname TEXT NOT NULL,
                        PcModel TEXT NOT NULL,
                        Processor TEXT NOT NULL,
                        DemoName TEXT NOT NULL,
                        RequestedSilicon TEXT NOT NULL,
                        EffectiveSilicon TEXT NOT NULL,
                        SiliconDeviceName TEXT NOT NULL,
                        ExecutionTimeMs INTEGER NOT NULL,
                        ThroughputTokPerSec REAL NOT NULL,
                        EstimatedPowerWatts REAL NOT NULL,
                        EnergyJoules REAL NOT NULL,
                        EnergySavedVsGpuJoules REAL NOT NULL,
                        IsNpuFallbackAnomaly INTEGER NOT NULL,
                        PerformanceDriftPercent REAL NOT NULL,
                        Status TEXT NOT NULL,
                        MetadataJson TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IDX_AuditLog_Timestamp ON AiFleetAuditLog(TimestampUtc);
                    CREATE INDEX IF NOT EXISTS IDX_AuditLog_Demo ON AiFleetAuditLog(DemoName);
                    CREATE INDEX IF NOT EXISTS IDX_AuditLog_Silicon ON AiFleetAuditLog(EffectiveSilicon);
                ";

                using var cmd = new SqliteCommand(ddl, connection);
                cmd.ExecuteNonQuery();

                SeedInitialDataIfEmpty(connection);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telemetry Init DB Error: {ex.Message}]");
            }
        }
    }

    private void SeedInitialDataIfEmpty(SqliteConnection connection)
    {
        try
        {
            using var countCmd = new SqliteCommand("SELECT COUNT(*) FROM AiFleetAuditLog", connection);
            long count = (long)(countCmd.ExecuteScalar() ?? 0L);
            if (count > 0) return;

            DateTime baseTime = DateTime.Now.AddMinutes(-45);

            var seedEntries = new[]
            {
                new { Demo = "Gomme Magique - Détection", Req = "AUTO", Eff = "NPU", Name = $"NPU ({SiliconDetector.NpuName})", Ms = 13, Tok = 0.0, W = 2.2, J = 0.029, Saved = 0.185, Fallback = 0, Drift = 0.0, MinOffset = 42 },
                new { Demo = "Gomme Magique - Effacement", Req = "AUTO", Eff = "NPU", Name = "Inpainting (NPU)", Ms = 45, Tok = 0.0, W = 2.2, J = 0.099, Saved = 0.643, Fallback = 0, Drift = 0.0, MinOffset = 40 },
                new { Demo = "Avatar Studio 3D", Req = "GPU", Eff = "GPU", Name = $"GPU ({SiliconDetector.GpuName})", Ms = 4680, Tok = 0.0, W = 15.2, J = 71.136, Saved = 0.0, Fallback = 0, Drift = 0.0, MinOffset = 32 },
                new { Demo = "Audio Studio - Diarisation", Req = "AUTO", Eff = "NPU", Name = $"{SiliconDetector.NpuName} (Whisper)", Ms = 850, Tok = 0.0, W = 2.5, J = 2.125, Saved = 11.900, Fallback = 0, Drift = 0.0, MinOffset = 25 },
                new { Demo = "Audio Studio - Diarisation", Req = "AUTO", Eff = "CPU", Name = $"{SiliconDetector.CpuName} (CPU Fallback)", Ms = 3450, Tok = 0.0, W = 26.0, J = 89.700, Saved = 0.0, Fallback = 1, Drift = 0.0, MinOffset = 18 },
                new { Demo = "Opérations Industrielles", Req = "AUTO", Eff = "NPU", Name = "phi-4-mini (NPU DirectML)", Ms = 420, Tok = 34.2, W = 2.2, J = 0.924, Saved = 6.006, Fallback = 0, Drift = 0.0, MinOffset = 12 },
                new { Demo = "Ingénierie & RAG Technique", Req = "AUTO", Eff = "NPU", Name = "phi-4-mini (NPU DirectML)", Ms = 910, Tok = 24.1, W = 2.2, J = 2.002, Saved = 13.013, Fallback = 0, Drift = 29.2, MinOffset = 4 }
            };

            using var tx = connection.BeginTransaction();
            foreach (var s in seedEntries)
            {
                DateTime itemTime = baseTime.AddMinutes(s.MinOffset);
                string insertSql = @"
                    INSERT INTO AiFleetAuditLog (
                        TimestampUtc, TimestampLocal, DeviceId, Hostname, PcModel, Processor,
                        DemoName, RequestedSilicon, EffectiveSilicon, SiliconDeviceName,
                        ExecutionTimeMs, ThroughputTokPerSec, EstimatedPowerWatts, EnergyJoules,
                        EnergySavedVsGpuJoules, IsNpuFallbackAnomaly, PerformanceDriftPercent,
                        Status, MetadataJson
                    ) VALUES (
                        $tsUtc, $tsLoc, $devId, $host, $model, $proc,
                        $demo, $req, $eff, $name,
                        $ms, $tok, $w, $j,
                        $saved, $fb, $drift,
                        $st, $meta
                    );";

                using var insertCmd = new SqliteCommand(insertSql, connection, tx);
                insertCmd.Parameters.AddWithValue("$tsUtc", itemTime.ToUniversalTime().ToString("o"));
                insertCmd.Parameters.AddWithValue("$tsLoc", itemTime.ToString("yyyy-MM-dd HH:mm:ss"));
                insertCmd.Parameters.AddWithValue("$devId", DeviceSerialNumber);
                insertCmd.Parameters.AddWithValue("$host", Hostname);
                insertCmd.Parameters.AddWithValue("$model", $"{PcManufacturer} {PcModel}".Trim());
                insertCmd.Parameters.AddWithValue("$proc", ProcessorName);
                insertCmd.Parameters.AddWithValue("$demo", s.Demo);
                insertCmd.Parameters.AddWithValue("$req", s.Req);
                insertCmd.Parameters.AddWithValue("$eff", s.Eff);
                insertCmd.Parameters.AddWithValue("$name", s.Name);
                insertCmd.Parameters.AddWithValue("$ms", s.Ms);
                insertCmd.Parameters.AddWithValue("$tok", s.Tok);
                insertCmd.Parameters.AddWithValue("$w", s.W);
                insertCmd.Parameters.AddWithValue("$j", s.J);
                insertCmd.Parameters.AddWithValue("$saved", s.Saved);
                insertCmd.Parameters.AddWithValue("$fb", s.Fallback);
                insertCmd.Parameters.AddWithValue("$drift", s.Drift);
                insertCmd.Parameters.AddWithValue("$st", s.Fallback == 1 ? "ANOMALY_FALLBACK" : (s.Drift > 25 ? "ANOMALY_DRIFT" : "SUCCESS"));
                insertCmd.Parameters.AddWithValue("$meta", "{}");

                insertCmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { }
    }

    public async Task RecordInferenceAsync(
        string demoName,
        string requestedSilicon,
        string effectiveSilicon,
        string siliconDeviceName,
        int latencyMs,
        double tokensPerSecond = 0.0,
        double? customPowerWatts = null,
        string status = "SUCCESS",
        Dictionary<string, object>? metadata = null)
    {
        int executionTimeMs = latencyMs;
        double throughputTokPerSec = tokensPerSecond;
        await Task.Run(() =>
        {
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                DateTime nowLocal = DateTime.Now;

                string effUpper = (effectiveSilicon ?? "NPU").ToUpperInvariant();
                string reqUpper = (requestedSilicon ?? "AUTO").ToUpperInvariant();

                // 1. Calculate active power & energy
                double watts = customPowerWatts ?? effUpper switch
                {
                    "GPU" => 16.5,
                    "CPU" => 26.0,
                    _ => 2.2 // NPU
                };

                double joules = watts * (executionTimeMs / 1000.0);

                // Energy comparison vs GPU baseline (~16.5W for equivalent workload duration)
                double gpuEquivJoules = 16.5 * (executionTimeMs / 1000.0);
                double energySavedVsGpu = effUpper == "NPU" ? Math.Max(0, gpuEquivJoules - joules) : 0.0;

                // 2. Anomaly detection: Did we run on CPU/GPU while NPU was requested or available?
                // Anomaly triggers if NPU was expected/Auto and system has NPU, but it executed on CPU or GPU.
                bool isFallbackAnomaly = HasNpu &&
                                         (reqUpper == "NPU" || reqUpper == "AUTO") &&
                                         (effUpper == "CPU" || effUpper == "GPU");

                // 3. Performance Drift calculation against baseline
                string baselineKey = $"{demoName}::{effUpper}";
                var list = _baselineLatencies.GetOrAdd(baselineKey, _ => new List<int>());
                double driftPercent = 0.0;

                lock (list)
                {
                    if (list.Count >= 3)
                    {
                        double baselineAvg = 0;
                        int count = Math.Min(list.Count, 10);
                        for (int i = 0; i < count; i++) baselineAvg += list[i];
                        baselineAvg /= count;

                        if (baselineAvg > 0)
                        {
                            driftPercent = Math.Round(((executionTimeMs - baselineAvg) / baselineAvg) * 100.0, 1);
                        }
                    }

                    if (list.Count < 50)
                    {
                        list.Add(executionTimeMs);
                    }
                }

                string metadataStr = metadata != null ? JsonSerializer.Serialize(metadata) : "{}";

                var record = new FleetTelemetryRecord
                {
                    TimestampUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture),
                    TimestampLocal = nowLocal.ToString("yyyy-MM-dd HH:mm:ss"),
                    DeviceId = DeviceSerialNumber,
                    Hostname = Hostname,
                    PcModel = PcModel,
                    Processor = ProcessorName,
                    DemoName = demoName,
                    RequestedSilicon = reqUpper,
                    EffectiveSilicon = effUpper,
                    SiliconDeviceName = string.IsNullOrWhiteSpace(siliconDeviceName) ? effUpper : siliconDeviceName,
                    ExecutionTimeMs = executionTimeMs,
                    ThroughputTokPerSec = throughputTokPerSec,
                    EstimatedPowerWatts = Math.Round(watts, 2),
                    EnergyJoules = Math.Round(joules, 4),
                    EnergySavedVsGpuJoules = Math.Round(energySavedVsGpu, 4),
                    IsNpuFallbackAnomaly = isFallbackAnomaly,
                    PerformanceDriftPercent = driftPercent,
                    Status = status,
                    MetadataJson = metadataStr
                };

                // 4. Insert into SQLite
                lock (_lock)
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    string insertSql = @"
                        INSERT INTO AiFleetAuditLog (
                            TimestampUtc, TimestampLocal, DeviceId, Hostname, PcModel, Processor,
                            DemoName, RequestedSilicon, EffectiveSilicon, SiliconDeviceName,
                            ExecutionTimeMs, ThroughputTokPerSec, EstimatedPowerWatts, EnergyJoules,
                            EnergySavedVsGpuJoules, IsNpuFallbackAnomaly, PerformanceDriftPercent,
                            Status, MetadataJson
                        ) VALUES (
                            $tsUtc, $tsLoc, $devId, $host, $model, $proc,
                            $demo, $req, $eff, $devName,
                            $time, $tok, $watts, $joules,
                            $saved, $anomaly, $drift,
                            $status, $meta
                        );
                    ";

                    using var cmd = new SqliteCommand(insertSql, connection);
                    cmd.Parameters.AddWithValue("$tsUtc", record.TimestampUtc);
                    cmd.Parameters.AddWithValue("$tsLoc", record.TimestampLocal);
                    cmd.Parameters.AddWithValue("$devId", record.DeviceId);
                    cmd.Parameters.AddWithValue("$host", record.Hostname);
                    cmd.Parameters.AddWithValue("$model", record.PcModel);
                    cmd.Parameters.AddWithValue("$proc", record.Processor);
                    cmd.Parameters.AddWithValue("$demo", record.DemoName);
                    cmd.Parameters.AddWithValue("$req", record.RequestedSilicon);
                    cmd.Parameters.AddWithValue("$eff", record.EffectiveSilicon);
                    cmd.Parameters.AddWithValue("$devName", record.SiliconDeviceName);
                    cmd.Parameters.AddWithValue("$time", record.ExecutionTimeMs);
                    cmd.Parameters.AddWithValue("$tok", record.ThroughputTokPerSec);
                    cmd.Parameters.AddWithValue("$watts", record.EstimatedPowerWatts);
                    cmd.Parameters.AddWithValue("$joules", record.EnergyJoules);
                    cmd.Parameters.AddWithValue("$saved", record.EnergySavedVsGpuJoules);
                    cmd.Parameters.AddWithValue("$anomaly", record.IsNpuFallbackAnomaly ? 1 : 0);
                    cmd.Parameters.AddWithValue("$drift", record.PerformanceDriftPercent);
                    cmd.Parameters.AddWithValue("$status", record.Status);
                    cmd.Parameters.AddWithValue("$meta", record.MetadataJson);

                    cmd.ExecuteNonQuery();

                    // 5. Append to JSONL for SIEM Log Forwarders (Splunk, Elastic, Sentinel)
                    try
                    {
                        string line = JsonSerializer.Serialize(record) + Environment.NewLine;
                        File.AppendAllText(_jsonlPath, line, Encoding.UTF8);
                    }
                    catch { }
                }

                OnRecordAdded?.Invoke();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Telemetry Logging Error: {ex.Message}]");
            }
        });
    }

    public async Task<List<FleetTelemetryRecord>> GetRecentRecordsAsync(
        int limit = 100,
        string? demoFilter = null,
        string? siliconFilter = null,
        bool anomaliesOnly = false)
    {
        return await Task.Run(() =>
        {
            var records = new List<FleetTelemetryRecord>();
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    var query = new StringBuilder("SELECT * FROM AiFleetAuditLog WHERE 1=1 ");
                    var parameters = new List<SqliteParameter>();

                    if (!string.IsNullOrWhiteSpace(demoFilter) && demoFilter != "ALL")
                    {
                        query.Append(" AND DemoName LIKE $demo ");
                        parameters.Add(new SqliteParameter("$demo", $"%{demoFilter}%"));
                    }

                    if (!string.IsNullOrWhiteSpace(siliconFilter) && siliconFilter != "ALL")
                    {
                        query.Append(" AND EffectiveSilicon = $silicon ");
                        parameters.Add(new SqliteParameter("$silicon", siliconFilter));
                    }

                    if (anomaliesOnly)
                    {
                        query.Append(" AND (IsNpuFallbackAnomaly = 1 OR PerformanceDriftPercent > 25.0) ");
                    }

                    query.Append(" ORDER BY Id DESC LIMIT $limit");
                    parameters.Add(new SqliteParameter("$limit", limit));

                    using var cmd = new SqliteCommand(query.ToString(), connection);
                    cmd.Parameters.AddRange(parameters.ToArray());

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        records.Add(new FleetTelemetryRecord
                        {
                            Id = reader.GetInt64(reader.GetOrdinal("Id")),
                            TimestampUtc = reader.GetString(reader.GetOrdinal("TimestampUtc")),
                            TimestampLocal = reader.GetString(reader.GetOrdinal("TimestampLocal")),
                            DeviceId = reader.GetString(reader.GetOrdinal("DeviceId")),
                            Hostname = reader.GetString(reader.GetOrdinal("Hostname")),
                            PcModel = reader.GetString(reader.GetOrdinal("PcModel")),
                            Processor = reader.GetString(reader.GetOrdinal("Processor")),
                            DemoName = reader.GetString(reader.GetOrdinal("DemoName")),
                            RequestedSilicon = reader.GetString(reader.GetOrdinal("RequestedSilicon")),
                            EffectiveSilicon = reader.GetString(reader.GetOrdinal("EffectiveSilicon")),
                            SiliconDeviceName = reader.GetString(reader.GetOrdinal("SiliconDeviceName")),
                            ExecutionTimeMs = reader.GetInt32(reader.GetOrdinal("ExecutionTimeMs")),
                            ThroughputTokPerSec = reader.GetDouble(reader.GetOrdinal("ThroughputTokPerSec")),
                            EstimatedPowerWatts = reader.GetDouble(reader.GetOrdinal("EstimatedPowerWatts")),
                            EnergyJoules = reader.GetDouble(reader.GetOrdinal("EnergyJoules")),
                            EnergySavedVsGpuJoules = reader.GetDouble(reader.GetOrdinal("EnergySavedVsGpuJoules")),
                            IsNpuFallbackAnomaly = reader.GetInt32(reader.GetOrdinal("IsNpuFallbackAnomaly")) == 1,
                            PerformanceDriftPercent = reader.GetDouble(reader.GetOrdinal("PerformanceDriftPercent")),
                            Status = reader.GetString(reader.GetOrdinal("Status")),
                            MetadataJson = reader.IsDBNull(reader.GetOrdinal("MetadataJson")) ? "{}" : reader.GetString(reader.GetOrdinal("MetadataJson"))
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Telemetry Query Error: {ex.Message}]");
                }
            }
            return records;
        });
    }

    public async Task<FleetAnalyticsSummary> GetFleetAnalyticsSummaryAsync()
    {
        return await Task.Run(() =>
        {
            var summary = new FleetAnalyticsSummary();
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();

                    string sql = @"
                        SELECT
                            COUNT(*) as Total,
                            SUM(CASE WHEN EffectiveSilicon = 'NPU' THEN 1 ELSE 0 END) as NpuCount,
                            SUM(CASE WHEN EffectiveSilicon = 'GPU' THEN 1 ELSE 0 END) as GpuCount,
                            SUM(CASE WHEN EffectiveSilicon = 'CPU' THEN 1 ELSE 0 END) as CpuCount,
                            SUM(CASE WHEN IsNpuFallbackAnomaly = 1 OR PerformanceDriftPercent > 25.0 THEN 1 ELSE 0 END) as AnomalyCount,
                            TOTAL(EnergyJoules) as TotalJoules,
                            TOTAL(EnergySavedVsGpuJoules) as SavedJoules,
                            AVG(CASE WHEN EffectiveSilicon = 'NPU' THEN ExecutionTimeMs ELSE NULL END) as AvgNpuMs,
                            AVG(CASE WHEN EffectiveSilicon = 'GPU' THEN ExecutionTimeMs ELSE NULL END) as AvgGpuMs,
                            AVG(CASE WHEN EffectiveSilicon = 'CPU' THEN ExecutionTimeMs ELSE NULL END) as AvgCpuMs,
                            MAX(PerformanceDriftPercent) as MaxDrift
                        FROM AiFleetAuditLog;
                    ";

                    using var cmd = new SqliteCommand(sql, connection);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        summary.TotalInferences = reader.GetInt32(0);
                        summary.NpuInferences = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        summary.GpuInferences = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        summary.CpuInferences = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                        summary.AnomalyCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                        summary.TotalEnergyConsumedJoules = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5);
                        summary.TotalEnergySavedVsGpuJoules = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6);
                        summary.AverageLatencyNpuMs = reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7);
                        summary.AverageLatencyGpuMs = reader.IsDBNull(8) ? 0.0 : reader.GetDouble(8);
                        summary.AverageLatencyCpuMs = reader.IsDBNull(9) ? 0.0 : reader.GetDouble(9);
                        summary.MaxPerformanceDriftPercent = reader.IsDBNull(10) ? 0.0 : reader.GetDouble(10);

                        summary.NpuAdoptionRatePercent = summary.TotalInferences > 0
                            ? Math.Round(((double)summary.NpuInferences / summary.TotalInferences) * 100.0, 1)
                            : 0.0;

                        summary.TotalEnergyConsumedWattHours = summary.TotalEnergyConsumedJoules / 3600.0;
                        summary.TotalEnergySavedVsGpuWattHours = summary.TotalEnergySavedVsGpuJoules / 3600.0;

                        // European electricity mix average: ~230 g CO2 / kWh
                        summary.Co2AvoidedGrams = (summary.TotalEnergySavedVsGpuWattHours / 1000.0) * 230.0;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Telemetry Summary Error: {ex.Message}]");
                }
            }
            return summary;
        });
    }

    public async Task<string> ExportToCsvAsync(string targetFilePath)
    {
        return await Task.Run(() =>
        {
            var records = GetRecentRecordsAsync(limit: 5000).Result;
            var sb = new StringBuilder();
            sb.AppendLine("ID;TimestampUtc;TimestampLocal;DeviceId;Hostname;PcModel;Processor;DemoName;RequestedSilicon;EffectiveSilicon;SiliconDeviceName;ExecutionTimeMs;ThroughputTokPerSec;EstimatedPowerWatts;EnergyJoules;EnergySavedVsGpuJoules;IsNpuFallbackAnomaly;PerformanceDriftPercent;Status;MetadataJson");

            foreach (var r in records)
            {
                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0};\"{1}\";\"{2}\";\"{3}\";\"{4}\";\"{5}\";\"{6}\";\"{7}\";\"{8}\";\"{9}\";\"{10}\";{11};{12:F1};{13:F2};{14:F4};{15:F4};{16};{17:F1};\"{18}\";\"{19}\"",
                    r.Id,
                    r.TimestampUtc,
                    r.TimestampLocal,
                    r.DeviceId,
                    r.Hostname,
                    r.PcModel,
                    r.Processor,
                    r.DemoName,
                    r.RequestedSilicon,
                    r.EffectiveSilicon,
                    r.SiliconDeviceName,
                    r.ExecutionTimeMs,
                    r.ThroughputTokPerSec,
                    r.EstimatedPowerWatts,
                    r.EnergyJoules,
                    r.EnergySavedVsGpuJoules,
                    r.IsNpuFallbackAnomaly ? 1 : 0,
                    r.PerformanceDriftPercent,
                    r.Status,
                    r.MetadataJson.Replace("\"", "\"\"")
                ));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetFilePath))!);
            File.WriteAllText(targetFilePath, sb.ToString(), Encoding.UTF8);
            return targetFilePath;
        });
    }

    public async Task ClearAuditLogAsync()
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                try
                {
                    using var connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    using var cmd = new SqliteCommand("DELETE FROM AiFleetAuditLog; VACUUM;", connection);
                    cmd.ExecuteNonQuery();

                    if (File.Exists(_jsonlPath))
                    {
                        File.Delete(_jsonlPath);
                    }
                    _baselineLatencies.Clear();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Telemetry Clear Error: {ex.Message}]");
                }
            }
            OnRecordAdded?.Invoke();
        });
    }

    public async Task<bool> SimulateSiemSyncAsync(string destination = "Microsoft Sentinel / Splunk Enterprise")
    {
        // Simulates zero-loss batch telemetry sync to corporate SIEM endpoint
        await Task.Delay(1200);
        return true;
    }
}
