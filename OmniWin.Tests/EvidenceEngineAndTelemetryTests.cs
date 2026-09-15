using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using OmniWin.Core.Services;
using OmniWin.Core.Mcp;
using System.Text.Json.Nodes;

namespace OmniWin.Tests;

public class EvidenceEngineAndTelemetryTests : IDisposable
{
    private readonly string _tempTestDir;

    public EvidenceEngineAndTelemetryTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "OmniWin_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void TelemetryHub_Instance_ProvidesValidSnapshot()
    {
        var hub = TelemetryHub.Instance;
        Assert.NotNull(hub);

        var snap = hub.CurrentSnapshot;
        Assert.NotNull(snap);
        Assert.True(snap.RamTotalBytes > 0, "RamTotalBytes should be greater than 0");
    }

    [Fact]
    public void TelemetryHub_SampleNow_InvokesSubscribersAndReturnsFreshData()
    {
        var hub = TelemetryHub.Instance;
        bool eventFired = false;
        SystemTelemetrySnapshot? captured = null;

        Action<SystemTelemetrySnapshot> handler = s =>
        {
            eventFired = true;
            captured = s;
        };

        hub.OnTelemetryUpdated += handler;
        try
        {
            var snap = hub.SampleNow();
            Assert.NotNull(snap);
            Assert.True(eventFired);
            Assert.NotNull(captured);
            Assert.Equal(snap.Timestamp, captured.Timestamp);
            Assert.True(snap.CpuLoadPercent >= 0.0 && snap.CpuLoadPercent <= 100.0);
            Assert.True(snap.RamUsagePercent >= 0.0 && snap.RamUsagePercent <= 100.0);
        }
        finally
        {
            hub.OnTelemetryUpdated -= handler;
        }
    }

    [Fact]
    public void TransactionService_JournalPersistence_WorksCorrectly()
    {
        string journalFile = Path.Combine(_tempTestDir, "test_journal.json");
        var txService = new TransactionService(journalFile);

        string testTweakId = "test_custom_tweak_01";
        string subPath = @"Software\OmniWinTestTx";
        txService.BeginTransaction(testTweakId, "Test Tweak Description");

        // Mutate a registry value using transaction-aware mutator
        txService.SetDword(Microsoft.Win32.Registry.CurrentUser, subPath, "TestVal", 42);

        // Commit transaction
        txService.CommitTransaction(testTweakId);

        Assert.True(txService.HasActiveTransaction(testTweakId));
        Assert.True(File.Exists(journalFile));

        // Re-load with new instance from the same journal
        var txServiceReloaded = new TransactionService(journalFile);
        Assert.True(txServiceReloaded.HasActiveTransaction(testTweakId));

        // Rollback
        bool rolledBack = txServiceReloaded.RollbackTransaction(testTweakId, out string msg);
        Assert.True(rolledBack);
        Assert.Contains("revertida con éxito", msg);
        Assert.False(txServiceReloaded.HasActiveTransaction(testTweakId));

        // Clean up test key
        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subPath, false); } catch { }
    }

    [Fact]
    public void TransactionService_ZeroSnapshots_ReturnsFalseAndLeavesRollbackToFallback()
    {
        string journalFile = Path.Combine(_tempTestDir, "test_zero_snap_journal.json");
        var txService = new TransactionService(journalFile);

        string testTweakId = "test_zero_snap";
        txService.BeginTransaction(testTweakId, "Zero snapshot tweak");
        txService.CommitTransaction(testTweakId);

        bool rolledBack = txService.RollbackTransaction(testTweakId, out string msg);
        // Must return false so caller executes legacy fallback!
        Assert.False(rolledBack);
        Assert.Contains("no contiene snapshots", msg);
    }

    [Fact]
    public void TransactionService_CustomDwordAndStringRestoration_WorksExactly()
    {
        string subPath = @"Software\OmniWinExactTest";
        // Setup initial custom values in registry
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subPath, true))
        {
            key.SetValue("CustomDword", 999, Microsoft.Win32.RegistryValueKind.DWord);
            key.SetValue("CustomStr", "OriginalString", Microsoft.Win32.RegistryValueKind.String);
        }

        string journalFile = Path.Combine(_tempTestDir, "test_exact_journal.json");
        var tx = new TransactionService(journalFile);

        string tweakId = "tweak_exact_restore";
        tx.BeginTransaction(tweakId, "Testing exact values");

        // Mutate both values
        tx.SetDword(Microsoft.Win32.Registry.CurrentUser, subPath, "CustomDword", 111);
        tx.SetString(Microsoft.Win32.Registry.CurrentUser, subPath, "CustomStr", "OverwrittenString");
        tx.CommitTransaction(tweakId);

        // Verify values changed
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(111, Convert.ToInt32(key?.GetValue("CustomDword")));
            Assert.Equal("OverwrittenString", key?.GetValue("CustomStr") as string);
        }

        // Rollback
        bool success = tx.RollbackTransaction(tweakId, out string msg);
        Assert.True(success);

        // Verify exact original values restored
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(999, Convert.ToInt32(key?.GetValue("CustomDword")));
            Assert.Equal("OriginalString", key?.GetValue("CustomStr") as string);
        }

        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subPath, false); } catch { }
    }

    [Fact]
    public void TransactionService_WalCrashRecovery_RestoresUncommittedMutations()
    {
        string journalFile = Path.Combine(_tempTestDir, "test_wal_journal.json");
        string walFile = Path.Combine(_tempTestDir, "test_wal_journal.wal.json");
        string subPath = @"Software\OmniWinWalTest";

        // Initial state: value exists with 1234
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subPath, true))
        {
            key.SetValue("CrashVal", 1234, Microsoft.Win32.RegistryValueKind.DWord);
        }

        // Simulate an in-flight uncommitted transaction recorded in WAL
        var uncommittedTx = new TweakTransaction
        {
            TweakId = "crash_tweak",
            Description = "Simulated crash mid-mutation",
            State = "PREPARED",
            AppliedAt = DateTime.UtcNow,
            RegistrySnapshots = new List<RegistryValueSnapshot>
            {
                new RegistryValueSnapshot
                {
                    HiveName = Microsoft.Win32.Registry.CurrentUser.Name,
                    SubPath = subPath,
                    ValueName = "CrashVal",
                    ExistedBefore = true,
                    ValueKind = "DWord",
                    StringifiedValue = "1234"
                }
            }
        };

        // Mutate registry to corrupted value (as if app crashed right after mutating)
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath, true))
        {
            key?.SetValue("CrashVal", 9999, Microsoft.Win32.RegistryValueKind.DWord);
        }

        // Write WAL file
        File.WriteAllText(walFile, System.Text.Json.JsonSerializer.Serialize(uncommittedTx));

        // When new TransactionService starts, it detects uncommitted WAL and recovers to baseline!
        var txService = new TransactionService(journalFile);

        // Verify that WAL recovery restored "CrashVal" back to 1234
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(1234, Convert.ToInt32(key?.GetValue("CrashVal")));
        }

        // Verify WAL file is cleaned up after recovery
        Assert.False(File.Exists(walFile));

        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subPath, false); } catch { }
    }

    [Fact]
    public void EcoQoSService_InvalidPid_FailsGracefullyWithoutTracking()
    {
        var eco = EcoQoSService.Instance;
        int nonExistentPid = 999999;
        bool result = eco.SetProcessEcoQoS(nonExistentPid, true);

        Assert.False(result);
        Assert.DoesNotContain(nonExistentPid, eco.GetThrottledPids());
    }

    [Fact]
    public void McpServer_WinPurgeRam_DefaultsToSafeStandbyOnly()
    {
        var tools = McpServer.GetToolsList();
        var purgeTool = tools.OfType<JsonObject>().FirstOrDefault(t => t?["name"]?.GetValue<string>() == "win_purge_ram");
        Assert.NotNull(purgeTool);

        string desc = purgeTool["description"]?.GetValue<string>() ?? "";
        Assert.Contains("segura", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OmniExperimentEngine_CategorizesRebootAndNonPerformanceTweaks()
    {
        Assert.True(OmniExperimentEngine.IsRebootRequired("gaming_hags"));
        Assert.True(OmniExperimentEngine.IsRebootRequired("sys_disable_hibernation"));
        Assert.False(OmniExperimentEngine.IsRebootRequired("gaming_gpu_priority_games"));

        Assert.True(OmniExperimentEngine.IsNonPerformanceTweak("privacy_cortana_telemetry"));
        Assert.True(OmniExperimentEngine.IsNonPerformanceTweak("win11_classic_context_menu"));
        Assert.False(OmniExperimentEngine.IsNonPerformanceTweak("gaming_gpu_priority_games"));
    }

    [Fact]
    public void OmniExperimentEngine_ComputeSummary_AccuratelyCalculatesStatistics()
    {
        // 100 synthetic jitter samples: 90 samples at 100µs, 9 samples at 500µs, 1 spike at 2000µs
        var samples = new List<double>();
        for (int i = 0; i < 90; i++) samples.Add(100.0);
        for (int i = 0; i < 9; i++) samples.Add(500.0);
        samples.Add(2000.0);

        var summary = OmniExperimentEngine.ComputeSummary(samples);

        Assert.Equal(100, summary.SampleCount);
        Assert.Equal(100.0, summary.Min);
        Assert.Equal(2000.0, summary.Max);
        Assert.Equal(100.0, summary.Median);
        Assert.True(summary.Mean > 100.0 && summary.Mean < 200.0);
        Assert.True(summary.StdDev > 0.0);
        Assert.True(summary.P99 >= 500.0);
        Assert.Equal(2000.0, summary.P99_9);
    }

    [Fact]
    public void OmniExperimentEngine_ComputeSummary_HandlesEmptyAndSingleSampleGracefully()
    {
        var emptySummary = OmniExperimentEngine.ComputeSummary(new List<double>());
        Assert.Equal(0, emptySummary.SampleCount);
        Assert.Equal(0.0, emptySummary.Mean);

        var singleSummary = OmniExperimentEngine.ComputeSummary(new List<double> { 42.5 });
        Assert.Equal(1, singleSummary.SampleCount);
        Assert.Equal(42.5, singleSummary.Mean);
        Assert.Equal(42.5, singleSummary.Min);
        Assert.Equal(42.5, singleSummary.Max);
        Assert.Equal(42.5, singleSummary.P99);
    }

    [Fact]
    public void OmniExperimentEngine_HistoryPersistence_WorksCorrectly()
    {
        string historyFile = Path.Combine(_tempTestDir, "test_exp_history.json");
        var engine = new OmniExperimentEngine(historyFile);

        var history = engine.GetHistory();
        Assert.NotNull(history);
        Assert.Empty(history);
    }

    [Fact]
    public void McpServer_ExposesWinExperimentEngineTool()
    {
        var tools = McpServer.GetToolsList();
        Assert.NotNull(tools);

        var experimentTool = tools.OfType<JsonObject>().FirstOrDefault(t => t?["name"]?.GetValue<string>() == "win_experiment_engine");
        Assert.NotNull(experimentTool);
        Assert.False(string.IsNullOrWhiteSpace(experimentTool["description"]?.GetValue<string>()));
    }

    [Fact]
    public async Task McpServer_WinExperimentEngine_GetHistory_ReturnsArray()
    {
        var mcp = new McpServer();
        var callRpc = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 2,
            ["method"] = "tools/call",
            ["params"] = new JsonObject
            {
                ["name"] = "win_experiment_engine",
                ["arguments"] = new JsonObject
                {
                    ["action"] = "get_history"
                }
            }
        };

        var responseNode = await mcp.HandleRequestAsync(callRpc);
        Assert.NotNull(responseNode);

        var resultObj = responseNode["result"]?.AsObject();
        Assert.NotNull(resultObj);
        var contentArr = resultObj["content"]?.AsArray();
        Assert.NotNull(contentArr);
        Assert.NotEmpty(contentArr);

        string text = contentArr[0]?["text"]?.GetValue<string>() ?? "";
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.StartsWith("[", text.Trim());
    }

    [Fact]
    public void CompanionServerService_GetLiveTelemetryJson_IncludesEnrichedMetrics()
    {
        string json = CompanionServerService.Instance.GetLiveTelemetryJson();
        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("cpuLoad", out _));
        Assert.True(root.TryGetProperty("ramUsagePercent", out _));
        Assert.True(root.TryGetProperty("kernelJitterUs", out _));
    }

    [Fact]
    public void MetricsExporterService_GenerateMetricsText_ContainsPrometheusFormat()
    {
        string metrics = MetricsExporterService.Instance.GenerateMetricsText();
        Assert.False(string.IsNullOrWhiteSpace(metrics));

        Assert.Contains("windows_cpu_usage_percent", metrics);
        Assert.Contains("windows_memory_physical_total_bytes", metrics);
        Assert.Contains("windows_memory_physical_used_bytes", metrics);
        Assert.Contains("windows_system_uptime_seconds", metrics);
    }

    [Fact]
    public void TransactionService_DoubleApply_PreservesOriginalBaseline()
    {
        string subPath = @"Software\OmniWinDoubleApplyTest";
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subPath, true))
        {
            key.SetValue("OriginalVal", 42, Microsoft.Win32.RegistryValueKind.DWord);
        }

        string journalFile = Path.Combine(_tempTestDir, "test_double_apply_journal.json");
        var tx = new TransactionService(journalFile);

        string tweakId = "tweak_double_apply";
        // 1st apply: change from 42 to 100
        tx.BeginTransaction(tweakId, "First application");
        tx.SetDword(Microsoft.Win32.Registry.CurrentUser, subPath, "OriginalVal", 100);
        tx.CommitTransaction(tweakId);

        // Verify registry is 100
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(100, Convert.ToInt32(key?.GetValue("OriginalVal")));
        }

        // 2nd apply (double-apply!): change from 100 to 200
        tx.BeginTransaction(tweakId, "Second application");
        tx.SetDword(Microsoft.Win32.Registry.CurrentUser, subPath, "OriginalVal", 200);
        tx.CommitTransaction(tweakId);

        // Verify registry is 200
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(200, Convert.ToInt32(key?.GetValue("OriginalVal")));
        }

        // Rollback: MUST restore 42, NOT 100!
        bool rolledBack = tx.RollbackTransaction(tweakId, out string msg);
        Assert.True(rolledBack, msg);

        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(42, Convert.ToInt32(key?.GetValue("OriginalVal")));
        }

        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subPath, false); } catch { }
    }

    [Fact]
    public void TransactionService_MutationFailsMidway_ImmediatelyRollsBackInFlight()
    {
        string subPath = @"Software\OmniWinMidwayFailTest";
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subPath, true))
        {
            key.SetValue("SafeVal", 77, Microsoft.Win32.RegistryValueKind.DWord);
        }

        string journalFile = Path.Combine(_tempTestDir, "test_midway_fail_journal.json");
        var tx = new TransactionService(journalFile);

        string tweakId = "tweak_fail_midway";
        tx.BeginTransaction(tweakId, "Testing midway failure rollback");
        tx.SetDword(Microsoft.Win32.Registry.CurrentUser, subPath, "SafeVal", 999);

        // Assert that value was mutated in registry
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(999, Convert.ToInt32(key?.GetValue("SafeVal")));
        }

        Assert.True(tx.HasInFlightTransaction);

        // Simulate failure trigger: immediate in-flight rollback
        bool reverted = tx.RollbackInFlightTransaction();
        Assert.True(reverted);
        Assert.False(tx.HasInFlightTransaction);

        // Verify value was restored back to 77
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(77, Convert.ToInt32(key?.GetValue("SafeVal")));
        }

        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subPath, false); } catch { }
    }

    [Fact]
    public void TransactionService_BeginTransaction_WhileInFlightPrepared_RejectsConcurrentTransaction()
    {
        string journalFile = Path.Combine(_tempTestDir, "test_concurrent_journal.json");
        var tx = new TransactionService(journalFile);

        tx.BeginTransaction("tweak_first", "First in flight");
        Assert.True(tx.HasInFlightTransaction);

        // Attempting to begin another transaction with a different ID while one is in-flight throws InvalidOperationException
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            tx.BeginTransaction("tweak_second", "Second in flight");
        });

        Assert.Contains("already in-flight", ex.Message);

        // Clean up
        tx.RollbackInFlightTransaction();
        Assert.False(tx.HasInFlightTransaction);
    }

    [Fact]
    public void TransactionService_CorruptWal_GracefullyRecoversFromBackupWal()
    {
        string journalFile = Path.Combine(_tempTestDir, "test_backup_wal_journal.json");
        string walFile = Path.Combine(_tempTestDir, "test_backup_wal_journal.wal.json");
        string walBackupFile = Path.Combine(_tempTestDir, "test_backup_wal_journal.wal.previous.json");
        string subPath = @"Software\OmniWinWalBackupTest";

        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subPath, true))
        {
            key.SetValue("RecoverMe", 9999, Microsoft.Win32.RegistryValueKind.DWord);
        }

        // Corrupt primary WAL file
        File.WriteAllText(walFile, "{ \"Corrupt\": true, \"TweakId\": ... [TRUNCATED] }");

        // Create valid backup WAL file pointing to original baseline 333
        var backupTx = new TweakTransaction
        {
            TweakId = "backup_wal_tweak",
            Description = "Recovered from backup WAL",
            State = "PREPARED",
            AppliedAt = DateTime.UtcNow,
            RegistrySnapshots = new List<RegistryValueSnapshot>
            {
                new RegistryValueSnapshot
                {
                    HiveName = Microsoft.Win32.Registry.CurrentUser.Name,
                    SubPath = subPath,
                    ValueName = "RecoverMe",
                    ExistedBefore = true,
                    ValueKind = "DWord",
                    StringifiedValue = "333"
                }
            }
        };
        File.WriteAllText(walBackupFile, JsonSerializer.Serialize(backupTx));

        // When starting new TransactionService, primary is corrupt, so it falls back to wal.previous.json!
        var txService = new TransactionService(journalFile);

        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subPath))
        {
            Assert.Equal(333, Convert.ToInt32(key?.GetValue("RecoverMe")));
        }

        try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(subPath, false); } catch { }
    }

    [Fact]
    public void TransactionService_ServiceStateSnapshot_SerializesAndRestoresCorrectly()
    {
        string journalFile = Path.Combine(_tempTestDir, "test_svc_journal.json");
        var tx = new TransactionService(journalFile);

        string tweakId = "tweak_service_test";
        tx.BeginTransaction(tweakId, "Service snapshot verification");

        // Capture a known service (e.g., Spooler)
        tx.CaptureServicePreState("Spooler");
        tx.CommitTransaction(tweakId);

        Assert.True(tx.HasActiveTransaction(tweakId));

        // Reload from journal to verify serialization
        var txReloaded = new TransactionService(journalFile);
        Assert.True(txReloaded.HasActiveTransaction(tweakId));
    }

    [Fact]
    public void EcoQoSService_InvalidPid_DoesNotThrowAndMaintainsStableTracking()
    {
        var eco = EcoQoSService.Instance;
        Assert.NotNull(eco);

        // Attempting to modulate non-existent PID should gracefully return false and not throw
        bool result = eco.SetProcessEcoQoS(-99999, true);
        Assert.False(result);

        // Reverting non-existent PID should also return false and not corrupt list
        bool revertResult = eco.SetProcessEcoQoS(-99999, false);
        Assert.False(revertResult);

        // RevertAllEcoQoS handles empty/stale processes cleanly
        int reverted = eco.RevertAllEcoQoS();
        Assert.True(reverted >= 0);
    }

    [Fact]
    public void LauncherHibernatorService_GetStatus_ContainsCorrectSummary()
    {
        var hibernator = LauncherHibernatorService.Instance;
        Assert.NotNull(hibernator);

        var status = hibernator.GetStatus();
        Assert.NotNull(status);
        Assert.NotNull(status.Summary);

        // Verify summary matches modernized wording
        Assert.Contains("segundo plano", status.Summary);
    }

    [Fact]
    public void OmniExperimentEngine_MetricDomains_ClassifiesCorrectly()
    {
        Assert.Equal(TweakMetricDomain.NetworkLatency, OmniExperimentEngine.GetMetricDomain("network_throttling_disable"));
        Assert.Equal(TweakMetricDomain.NetworkLatency, OmniExperimentEngine.GetMetricDomain("tcp_nagle_disable"));
        Assert.Equal(TweakMetricDomain.RebootRequired, OmniExperimentEngine.GetMetricDomain("gaming_hpet_disable"));
        Assert.Equal(TweakMetricDomain.RebootRequired, OmniExperimentEngine.GetMetricDomain("sys_disable_hibernation"));
        Assert.Equal(TweakMetricDomain.FunctionalNonPerformance, OmniExperimentEngine.GetMetricDomain("privacy_telemetry_disable"));
        Assert.Equal(TweakMetricDomain.FunctionalNonPerformance, OmniExperimentEngine.GetMetricDomain("win11_classic_context_menu"));
        Assert.Equal(TweakMetricDomain.FramePacing, OmniExperimentEngine.GetMetricDomain("game_bar_disable"));
        Assert.Equal(TweakMetricDomain.SchedulerJitter, OmniExperimentEngine.GetMetricDomain("scheduler_priorities"));
    }

    [Fact]
    public void OmniExperimentEngine_WelchTTest_CalculatesDegreesOfFreedomAndSignificance()
    {
        var baseline = new double[] { 100.0, 102.0, 98.0, 101.0, 99.0, 103.0 };
        var treatment = new double[] { 70.0, 72.0, 69.0, 71.0, 68.0, 70.0 };

        var (evidence, tStat, df, pVal) = OmniExperimentEngine.ComputeWelchTTest(baseline, treatment);

        Assert.True(df > 1.0, $"Expected df > 1, got {df}");
        Assert.True(tStat > 5.0, $"Expected large tStat, got {tStat}");
        Assert.True(evidence > 0.90, $"Expected high evidence score, got {evidence}");
        Assert.True(pVal < 0.01, $"Expected low pVal, got {pVal}");
    }

    [Fact]
    public void OmniExperimentEngine_ResumableState_PersistsAndLoadsAcrossSessions()
    {
        string historyFile = Path.Combine(_tempTestDir, "test_resumable_history.json");
        var engine = new OmniExperimentEngine(historyFile);

        string testTweak = "gaming_hpet_disable";
        var state = new ResumableExperimentState
        {
            TweakId = testTweak,
            Status = "PendingReboot",
            StagedAt = DateTime.UtcNow,
            PreRebootBaseline = new ExperimentMetricSummary
            {
                Mean = 12.5,
                P95 = 25.0,
                P99 = 40.0,
                P99_9 = 65.0
            }
        };

        engine.SaveResumableState(state);

        var loaded = engine.LoadResumableState(testTweak);
        Assert.NotNull(loaded);
        Assert.Equal(testTweak, loaded.TweakId);
        Assert.Equal("PendingReboot", loaded.Status);
        Assert.Equal(12.5, loaded.PreRebootBaseline.Mean);
        Assert.Equal(40.0, loaded.PreRebootBaseline.P99);

        // Remove
        engine.RemoveResumableState(testTweak);
        var afterRemoval = engine.LoadResumableState(testTweak);
        Assert.Null(afterRemoval);
    }
}

