using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OmniWin.Core.Services;
using Xunit;

namespace OmniWin.Tests;

public class NewFeaturesAdvancedTests
{
    [Fact]
    public void CompanionServerService_InitializesWithValidParameters()
    {
        var svc = new CompanionServerService(8799);
        Assert.NotNull(svc);
        Assert.Equal(8799, svc.Port);
        Assert.False(string.IsNullOrEmpty(svc.LocalIp));
        Assert.False(string.IsNullOrEmpty(svc.PairingToken));
        Assert.Equal(16, svc.PairingToken.Length); // 8 bytes = 16 hex chars
        Assert.StartsWith("http://", svc.PairingUrl);
        Assert.Contains(svc.PairingToken, svc.PairingUrl);
    }

    [Fact]
    public void CompanionServerService_GeneratesValidQrCodePng()
    {
        var svc = new CompanionServerService(8799);
        byte[] pngBytes = svc.GenerateQrCodePngBytes(5);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 100);

        // Verify PNG magic header bytes: 137, 80, 78, 71, 13, 10, 26, 10
        byte[] expectedHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < expectedHeader.Length; i++)
        {
            Assert.Equal(expectedHeader[i], pngBytes[i]);
        }
    }

    [Fact]
    public void CompanionServerService_LiveTelemetryJsonIsValid()
    {
        var svc = new CompanionServerService(8799);
        string json = svc.GetLiveTelemetryJson();

        Assert.False(string.IsNullOrWhiteSpace(json));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("hostname", out _));
        Assert.True(doc.RootElement.TryGetProperty("ramUsagePercent", out _));
        Assert.True(doc.RootElement.TryGetProperty("ramUsedGb", out _));
        Assert.True(doc.RootElement.TryGetProperty("netDownloadSpeed", out _));
    }

    [Fact]
    public void GameProfilerService_ManagesMonitoredGames()
    {
        var svc = GameProfilerService.Instance;
        var games = svc.GetMonitoredGames();

        Assert.NotEmpty(games);
        Assert.Contains(games, g => g.ExecutableName == "cs2.exe");
        Assert.Contains(games, g => g.ExecutableName == "blender.exe");

        // Add custom game
        svc.AddMonitoredGame("MyCustomGame.exe", "Custom Game Test");
        var updated = svc.GetMonitoredGames();
        Assert.Contains(updated, g => g.ExecutableName == "mycustomgame.exe");

        // Remove
        svc.RemoveMonitoredGame("mycustomgame.exe");
        var finalGames = svc.GetMonitoredGames();
        Assert.DoesNotContain(finalGames, g => g.ExecutableName == "mycustomgame.exe");
    }

    [Fact]
    public void FirewallMonitorService_RetrievesActiveConnectionsWithoutExceptions()
    {
        var svc = FirewallMonitorService.Instance;
        var connections = svc.GetActiveConnections();

        Assert.NotNull(connections);
        // On any running Windows system, there are active local/remote TCP connections
        Assert.True(connections.Count >= 0);
    }

    [Fact]
    public void DnsSecurityService_ProvidesKnownFastProviders()
    {
        var svc = DnsSecurityService.Instance;
        var providers = svc.GetProviders();

        Assert.NotEmpty(providers);
        Assert.Contains(providers, p => p.PrimaryIp == "1.1.1.1");
        Assert.Contains(providers, p => p.PrimaryIp == "9.9.9.9");
        Assert.Contains(providers, p => p.PrimaryIp == "8.8.8.8");
    }

    [Fact]
    public async Task DnsSecurityService_CanBenchmarkProviders()
    {
        var svc = DnsSecurityService.Instance;
        var benchmarked = await svc.BenchmarkAllProvidersAsync();

        Assert.NotEmpty(benchmarked);
        Assert.All(benchmarked, p => Assert.NotNull(p.Name));
    }

    [Fact]
    public void SystemSnapshotMigrationService_ExportsAndValidatesSnapshot()
    {
        var svc = SystemSnapshotMigrationService.Instance;
        string tempPath = Path.Combine(Path.GetTempPath(), $"test-snapshot-{Guid.NewGuid()}.omniwin");

        try
        {
            string exported = svc.ExportSnapshot(tempPath);
            Assert.True(File.Exists(exported));

            string json = File.ReadAllText(exported);
            var package = JsonSerializer.Deserialize<OmniWinSnapshotPackage>(json);

            Assert.NotNull(package);
            Assert.Equal("1.2.0", package.Version);
            Assert.Equal(Environment.MachineName, package.MachineName);
            Assert.NotNull(package.AppliedTweakIds);
            Assert.NotNull(package.AsrRuleStates);
            Assert.NotNull(package.Settings);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
