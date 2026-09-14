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
        txService.BeginTransaction(testTweakId, "Test Tweak Description");

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
}
