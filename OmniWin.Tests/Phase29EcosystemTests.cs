using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmniWin.Core.Services;
using Xunit;

namespace OmniWin.Tests;

public class Phase29EcosystemTests
{
    // ==========================================
    // 1. APP MIGRATION & JUNCTIONS TESTS
    // ==========================================

    [Fact]
    public void AppMigrationService_IsJunction_NormalFolder_ReturnsFalse()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "omni_test_not_junction_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            bool isJunction = AppMigrationService.Instance.IsJunction(tempDir);
            Assert.False(isJunction);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AppMigrationService_AnalyzeFolder_CalculatesSizeAccurately()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "omni_test_analyze_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllBytes(Path.Combine(tempDir, "file1.bin"), new byte[1024 * 50]);
            File.WriteAllBytes(Path.Combine(tempDir, "file2.bin"), new byte[1024 * 30]);

            var plan = AppMigrationService.Instance.AnalyzeFolder(tempDir, Path.GetTempPath());
            Assert.Equal(2, plan.TotalFiles);
            Assert.Equal(80 * 1024, plan.TotalSizeBytes);
            Assert.False(plan.IsCurrentlyJunction);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void AppMigrationService_FindCandidateFolders_ReturnsNonNullList()
    {
        var candidates = AppMigrationService.Instance.FindCandidateFolders("C:\\");
        Assert.NotNull(candidates);
    }

    // ==========================================
    // 2. NETWORK QOS THROTTLER TESTS
    // ==========================================

    [Fact]
    public async Task NetworkQosService_ListPolicies_ReturnsValidList()
    {
        var policies = await NetworkQosService.Instance.ListPoliciesAsync();
        Assert.NotNull(policies);
    }

    [Fact]
    public void QosPolicyInfo_CalculatesRatesCorrectly()
    {
        var info = new QosPolicyInfo
        {
            Name = "OmniWin_QoS_Test",
            AppPathName = "test.exe",
            ThrottleRateBps = 8_000_000 // 1 MB/s roughly
        };

        Assert.True(info.ThrottleRateKbps > 900 && info.ThrottleRateKbps < 1000);
        Assert.True(info.IsOmniWinManaged);
    }

    // ==========================================
    // 3. IDLE MAINTENANCE ROBOT TESTS
    // ==========================================

    [Fact]
    public void IdleMaintenanceService_GetCurrentIdleSeconds_ReturnsNonNegative()
    {
        double idle = IdleMaintenanceService.Instance.GetCurrentIdleSeconds();
        Assert.True(idle >= 0.0);
    }

    [Fact]
    public void IdleMaintenanceService_GetStatus_ReturnsInitializedModel()
    {
        var status = IdleMaintenanceService.Instance.GetStatus();
        Assert.NotNull(status);
        Assert.True(status.ThresholdSeconds > 0);
        Assert.NotNull(status.LastRunSummary);
    }

    [Fact]
    public async Task IdleMaintenanceService_TriggerMaintenanceNow_ExecutesSuccessfully()
    {
        string log = await IdleMaintenanceService.Instance.TriggerMaintenanceNowAsync();
        Assert.NotNull(log);
        Assert.Contains("[IDLE_MAINTENANCE]", log);
        var status = IdleMaintenanceService.Instance.GetStatus();
        Assert.NotNull(status.LastRunTime);
    }

    // ==========================================
    // 4. SPOTLIGHT WALLPAPER EXTRACTOR TESTS
    // ==========================================

    [Fact]
    public void SpotlightWallpaperService_GetSpotlightAssetsPath_ReturnsValidPath()
    {
        string path = SpotlightWallpaperService.Instance.GetSpotlightAssetsPath();
        Assert.NotNull(path);
        Assert.Contains("Microsoft.Windows.ContentDeliveryManager", path);
    }

    [Fact]
    public void SpotlightWallpaperService_ScanSpotlightAssets_DoesNotThrow()
    {
        var items = SpotlightWallpaperService.Instance.ScanSpotlightAssets();
        Assert.NotNull(items);
        foreach (var item in items)
        {
            Assert.True(item.Width >= 1920);
            Assert.True(item.IsLandscape);
            Assert.True(item.FileSizeBytes > 0);
        }
    }

    [Fact]
    public async Task SpotlightWallpaperService_ExportWallpapers_WithSyntheticFile_ExportsJpg()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "omni_spotlight_export_" + Guid.NewGuid().ToString("N"));
        string dummyAsset = Path.Combine(Path.GetTempPath(), "dummy_asset_" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(dummyAsset, new byte[512]);

        try
        {
            int count = await SpotlightWallpaperService.Instance.ExportWallpapersAsync([dummyAsset], tempDir);
            Assert.Equal(1, count);
            Assert.True(Directory.Exists(tempDir));
            var exported = Directory.GetFiles(tempDir, "*.jpg");
            Assert.Single(exported);
        }
        finally
        {
            try { File.Delete(dummyAsset); } catch { }
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ==========================================
    // 5. PORT CONFLICT RESOLVER TESTS
    // ==========================================

    [Fact]
    public void PortConflictService_GetListeningPorts_ReturnsValidList()
    {
        var listeners = PortConflictService.Instance.GetListeningPorts();
        Assert.NotNull(listeners);
        // On any running Windows system, RPC/SMB/System or dev servers have listening ports
        foreach (var l in listeners)
        {
            Assert.True(l.Port > 0 && l.Port <= 65535);
            Assert.Equal("TCP", l.Protocol);
            Assert.Equal("LISTEN", l.State);
            Assert.NotNull(l.ProcessName);
        }
    }

    [Fact]
    public void PortConflictService_DiagnoseCommonPorts_ReturnsExpectedCatalog()
    {
        var common = PortConflictService.Instance.DiagnoseCommonPorts();
        Assert.NotNull(common);
        Assert.True(common.Count >= 10);
        Assert.Contains(common, c => c.Port == 80);
        Assert.Contains(common, c => c.Port == 443);
        Assert.Contains(common, c => c.Port == 3000);
        Assert.Contains(common, c => c.Port == 8080);
    }

    [Fact]
    public void PortConflictService_DiagnosePort_UnusedHighPort_ReportsFree()
    {
        // Port 59873 is typically unassigned
        var diag = PortConflictService.Instance.DiagnosePort(59873);
        Assert.NotNull(diag);
        Assert.Equal(59873, diag.Port);
        Assert.False(diag.IsOccupied);
        Assert.Contains("disponible", diag.Description, StringComparison.OrdinalIgnoreCase);
    }
}
