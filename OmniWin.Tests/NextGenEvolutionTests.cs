using System;
using System.Linq;
using System.Text.Json;
using Xunit;
using OmniWin.Core.Services;

namespace OmniWin.Tests;

public class NextGenEvolutionTests
{
    [Fact]
    public void MsiInterruptService_RunDoctorReport_ReturnsValidReport()
    {
        var report = MsiInterruptService.Instance.RunDoctorReport();

        Assert.NotNull(report);
        Assert.NotNull(report.Devices);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));
        Assert.True(report.TotalDevices >= 0);
        Assert.True(report.MsiActiveCount >= 0);
        Assert.True(report.LineBasedCount >= 0);
    }

    [Fact]
    public void MsiDeviceModel_CategoryDisplayName_MapsExpectedCategories()
    {
        var gpu = new MsiDeviceModel { DeviceClass = "Display" };
        var net = new MsiDeviceModel { DeviceClass = "Net" };
        var scsi = new MsiDeviceModel { DeviceClass = "SCSIAdapter" };
        var other = new MsiDeviceModel { DeviceClass = "USB" };

        Assert.Contains("GPU", gpu.CategoryDisplayName);
        Assert.Contains("Red", net.CategoryDisplayName);
        Assert.Contains("Almacenamiento", scsi.CategoryDisplayName);
        Assert.Equal("USB", other.CategoryDisplayName);
    }

    [Fact]
    public void LauncherHibernatorService_GetStatus_ReturnsValidStructure()
    {
        var status = LauncherHibernatorService.Instance.GetStatus();

        Assert.NotNull(status);
        Assert.NotNull(status.Processes);
        Assert.False(string.IsNullOrWhiteSpace(status.Summary));
        Assert.True(status.TotalMemoryFreedMb >= 0);
    }

    [Fact]
    public void LauncherHibernatorService_WakeAll_WhenEmpty_DoesNotThrow()
    {
        int woken = LauncherHibernatorService.Instance.WakeAllHibernatedProcesses();

        Assert.True(woken >= 0);
        Assert.False(LauncherHibernatorService.Instance.IsHibernating);
    }

    [Fact]
    public void CompanionServerService_GetLiveTelemetryJson_ContainsNextGenState()
    {
        string json = CompanionServerService.Instance.GetLiveTelemetryJson();

        Assert.False(string.IsNullOrWhiteSpace(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("hostname", out var hostProp));
        Assert.False(string.IsNullOrWhiteSpace(hostProp.GetString()));

        Assert.True(root.TryGetProperty("isHibernating", out _));
        Assert.True(root.TryGetProperty("hibernatedCount", out _));
        Assert.True(root.TryGetProperty("hudStyle", out _));
        Assert.True(root.TryGetProperty("hudOpacity", out _));
        Assert.True(root.TryGetProperty("hudScale", out _));
    }

    [Fact]
    public void GameProfilerService_MonitoredGames_HaveLauncherHibernationEnabledByDefault()
    {
        var games = GameProfilerService.Instance.GetMonitoredGames();

        Assert.NotEmpty(games);
        foreach (var g in games)
        {
            Assert.True(g.AutoHibernateLaunchers, $"Expected AutoHibernateLaunchers true for {g.DisplayName}");
            Assert.True(g.AutoSetTimer05ms, $"Expected AutoSetTimer05ms true for {g.DisplayName}");
        }
    }
}
