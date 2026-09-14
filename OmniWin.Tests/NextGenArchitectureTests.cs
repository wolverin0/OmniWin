using System;
using System.Diagnostics;
using System.Linq;
using OmniWin.Core.Services;
using Xunit;

namespace OmniWin.Tests;

public class NextGenArchitectureTests
{
    [Fact]
    public void CpuTopologyService_DetectsHardwareTopologyWithoutSkuguesstimate()
    {
        var svc = CpuTopologyService.Instance;
        Assert.NotNull(svc);

        var topo = svc.GetTopology();
        Assert.NotNull(topo);
        Assert.True(topo.LogicalProcessorCount > 0, "Logical processor count must be > 0");
        Assert.True(topo.PhysicalCoreCount > 0, "Physical core count must be > 0");
        Assert.NotNull(topo.Processors);
        Assert.NotEmpty(topo.Processors);

        // Verify summary string
        Assert.False(string.IsNullOrWhiteSpace(topo.Summary));

        // If hybrid (e.g. 14900K on host), verify masks
        if (topo.IsHybrid)
        {
            Assert.True(topo.PerformanceThreadsCount > 0);
            Assert.True(topo.EfficiencyThreadsCount > 0);
            Assert.NotEqual(0L, topo.PerformanceAffinityMask);
            Assert.NotEqual(0L, topo.EfficiencyAffinityMask);
            Assert.Equal(topo.PerformanceAffinityMask, svc.GetPerformanceCoreMask());
            Assert.Equal(topo.EfficiencyAffinityMask, svc.GetEfficiencyCoreMask());
        }
    }

    [Fact]
    public void EcoQoSService_HasSafeProtectedProcessGuards()
    {
        var svc = EcoQoSService.Instance;
        Assert.NotNull(svc);

        // Verify that setting EcoQoS for invalid PID handles gracefully
        bool res = svc.SetProcessEcoQoS(-999, true);
        Assert.False(res);

        // Verify that reverting handles cleanly
        int reverted = svc.RevertAllEcoQoS();
        Assert.True(reverted >= 0);
    }

    [Fact]
    public void PcieLinkInspector_InspectsDevicesAndReportsNegotiationStatus()
    {
        var inspector = PcieLinkInspector.Instance;
        Assert.NotNull(inspector);

        var report = inspector.RunDoctorCheck();
        Assert.NotNull(report);
        Assert.NotNull(report.Devices);
        Assert.False(string.IsNullOrWhiteSpace(report.Summary));

        // Check each device report
        foreach (var dev in report.Devices)
        {
            Assert.False(string.IsNullOrWhiteSpace(dev.DeviceName));
            Assert.False(string.IsNullOrWhiteSpace(dev.Status));
            Assert.False(string.IsNullOrWhiteSpace(dev.DiagnosticMessage));
            Assert.True(dev.MaxLinkWidthLanes >= 0);
            Assert.True(dev.CurrentLinkWidthLanes >= 0);
        }
    }

    [Fact]
    public void BypassIoService_HandlesOsVersionAndVolumeCheckCleanly()
    {
        var svc = BypassIoService.Instance;
        Assert.NotNull(svc);

        var report = svc.CheckSystemBypassIoState();
        Assert.NotNull(report);
        Assert.True(report.WindowsBuild > 0);
        Assert.False(string.IsNullOrWhiteSpace(report.OverallAssessment));
        Assert.NotEmpty(report.Volumes);

        var cDrive = svc.CheckVolumeBypassIo("C:\\");
        Assert.NotNull(cDrive);
        Assert.Equal("C:\\", cDrive.Volume);
        Assert.False(string.IsNullOrWhiteSpace(cDrive.SummaryMessage));
    }

    [Fact]
    public void StutterInvestigatorService_CorrelatesThermalAndMemoryAnomalies()
    {
        var svc = StutterInvestigatorService.Instance;
        Assert.NotNull(svc);
        svc.ClearBuffer();

        // Feed normal snapshots
        for (int i = 0; i < 10; i++)
        {
            svc.RecordSnapshot(new TelemetrySnapshot
            {
                Timestamp = DateTime.UtcNow.AddSeconds(-10 + i),
                CpuLoadPercent = 35.0,
                GpuLoadPercent = 85.0,
                CpuTempC = 62.0,
                GpuTempC = 68.0,
                AvailableRamMb = 16384,
                FrameTimeMs = 6.9, // 144 FPS
                Fps = 144.0,
                ThermalThrottling = false
            });
        }

        // Add a severe thermal throttle stutter
        svc.RecordSnapshot(new TelemetrySnapshot
        {
            Timestamp = DateTime.UtcNow.AddSeconds(-1),
            CpuLoadPercent = 99.0,
            GpuLoadPercent = 40.0,
            CpuTempC = 95.0, // Critical thermal
            GpuTempC = 88.0,
            AvailableRamMb = 12000,
            FrameTimeMs = 55.4, // Stutter
            Fps = 18.0,
            ThermalThrottling = true
        });

        var analysis = svc.AnalyzeRecentStutter(TimeSpan.FromSeconds(15));
        Assert.NotNull(analysis);
        Assert.Equal(55.4, analysis.PeakFrameTimeMs);
        Assert.Equal(18.0, analysis.LowestFps);
        Assert.Contains("Throttling Térmico", analysis.ProbableCause);
        Assert.NotEmpty(analysis.CorrelatedFindings);
    }

    [Fact]
    public void GameProfilerService_HasEmpiricalSafeDefaults()
    {
        var item = new GameProfileItem();
        // Safe-by-default to prevent launch stutter
        Assert.False(item.AutoPurgeRam, "AutoPurgeRam must be false by default");
        Assert.False(item.AutoEnforcePCores, "AutoEnforcePCores must be false by default (scheduler-managed)");
        Assert.True(item.AutoEcoQoSBackground, "AutoEcoQoSBackground should be true to throttle background bloat");
        Assert.True(item.AutoSetTimer05ms, "AutoSetTimer05ms should be true for esports responsiveness");
    }

    [Fact]
    public void PowerService_SetHighPrecisionTimer_SymmetricRevert()
    {
        var svc = new PowerService();
        var (enableOk, curRes) = svc.SetHighPrecisionTimer(true);
        Assert.True(curRes > 0.0);

        // Reverting must not throw and should return current resolution
        var (disableOk, revertRes) = svc.SetHighPrecisionTimer(false);
        Assert.True(revertRes > 0.0);
    }

    [Fact]
    public void MemoryService_NonDestructivePurge_ExecutesCleanly()
    {
        var mem = new MemoryService();
        var stats = mem.GetMemoryStats();
        Assert.True(stats.TotalPhysicalBytes > 0);

        // Purge with working sets false (safe mode)
        var result = mem.PurgeMemory(purgeStandby: true, purgeWorkingSets: false);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
