using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using OmniWin.Core.Services;

namespace OmniWin.Tests;

public class CoreServicesTests
{
    [Fact]
    public void MemoryService_ReturnsValidStats()
    {
        var memoryService = new MemoryService();
        var stats = memoryService.GetMemoryStats();

        Assert.NotNull(stats);
        Assert.True(stats.TotalPhysicalBytes > 0, "Total physical RAM should be greater than 0");
        Assert.True(stats.AvailablePhysicalBytes > 0, "Available physical RAM should be greater than 0");
        Assert.InRange(stats.UsagePercentage, 0, 100);
    }

    [Fact]
    public void MemoryService_PurgeExecution_DoesNotThrow()
    {
        var memoryService = new MemoryService();
        var ex = Record.Exception(() => memoryService.PurgeMemory(purgeStandby: false, purgeWorkingSets: true));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DiskService_AnalyzeBloat_ReturnsCategories()
    {
        var diskService = new DiskService();
        var report = await diskService.AnalyzeBloatAsync();

        Assert.NotNull(report);
        Assert.NotEmpty(report.Categories);
        Assert.True(report.TotalBloatBytes >= 0);
        Assert.NotEmpty(report.Drives);
    }

    [Fact]
    public void ProcessService_GetRunningProcesses_ReturnsValidProcesses()
    {
        var processService = new ProcessService();
        var procs = processService.GetRunningProcesses(limit: 10, sortBy: "memory");

        Assert.NotNull(procs);
        Assert.NotEmpty(procs);
        Assert.True(procs.Count <= 10);
        Assert.All(procs, p =>
        {
            Assert.True(p.Pid >= 0);
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
        });
    }

    [Fact]
    public async Task NetworkService_RunDiagnostics_ReturnsValidMetrics()
    {
        var networkService = new NetworkService();
        var report = await networkService.RunDiagnosticsAsync("8.8.8.8");

        Assert.NotNull(report);
        Assert.NotNull(report.ActiveInterfaces);
        Assert.NotEmpty(report.ActiveInterfaces);
    }

    [Fact]
    public void TweakService_GetTweaks_ReturnsPopulatedCatalog()
    {
        var tweakService = new TweakService();
        var tweaks = tweakService.GetTweaks();

        Assert.NotNull(tweaks);
        Assert.NotEmpty(tweaks);
        Assert.All(tweaks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id), "Tweak ID must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(t.Name), "Tweak Name must not be empty");
            Assert.False(string.IsNullOrWhiteSpace(t.Category), "Tweak Category must not be empty");
        });
    }

    [Fact]
    public void SecurityAuditService_GetSecurityAudit_DetectsStatus()
    {
        var auditService = new SecurityAuditService();
        var audit = auditService.GetSecurityAudit();

        Assert.NotNull(audit);
        Assert.False(string.IsNullOrWhiteSpace(audit.AntivirusProduct));
    }

    [Fact]
    public void WindowsServiceService_GetServices_ReturnsDefinedList()
    {
        var winService = new WindowsServiceService();
        var services = winService.GetServices(bloatCandidatesOnly: true);

        Assert.NotNull(services);
        Assert.NotEmpty(services);
        Assert.Contains(services, s => s.Name.Equals("DiagTrack", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FileLockService_GetLockingProcesses_OnTempFile_ReturnsEmpty()
    {
        var lockService = new FileLockService();
        string tempFile = Path.GetTempFileName();
        try
        {
            var lockers = lockService.GetLockingProcesses(tempFile);
            Assert.NotNull(lockers);
            Assert.Empty(lockers);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void AudioMixerService_GetAudioSessions_DoesNotThrow()
    {
        var mixerService = new AudioMixerService();
        var sessions = mixerService.GetAudioSessions();

        Assert.NotNull(sessions);
        foreach (var session in sessions)
        {
            Assert.True(session.ProcessId >= 0);
            Assert.InRange(session.Volume, 0.0f, 1.0f);
            Assert.False(string.IsNullOrWhiteSpace(session.ProcessName));
        }
    }

    [Fact]
    public void AudioMixerService_MasterVolume_DoesNotThrow()
    {
        var mixerService = new AudioMixerService();
        float masterVol = mixerService.GetMasterVolume();
        Assert.InRange(masterVol, 0.0f, 1.0f);

        bool isMuted = mixerService.GetMasterMute();
        // Check reading mute doesn't throw
        Assert.True(isMuted || !isMuted);
    }

    [Fact]
    public void MetricsExporterService_GeneratesValidPrometheusMetricsText()
    {
        using var service = new MetricsExporterService();
        string metrics = service.GenerateMetricsText();

        Assert.False(string.IsNullOrWhiteSpace(metrics));
        Assert.Contains("windows_cpu_usage_percent", metrics);
        Assert.Contains("windows_memory_physical_total_bytes", metrics);
        Assert.Contains("windows_memory_physical_used_bytes", metrics);
        Assert.Contains("windows_memory_physical_available_bytes", metrics);
        Assert.Contains("windows_disk_free_bytes{drive=\"C:\"}", metrics);
        Assert.Contains("windows_system_uptime_seconds", metrics);
        Assert.Contains("windows_system_handles_count", metrics);
        Assert.Contains("windows_system_threads_count", metrics);
    }

    [Fact]
    public async Task MetricsExporterService_HttpServer_ServesPrometheusMetrics()
    {
        int testPort = 9188;
        using var service = new MetricsExporterService();
        service.Start(testPort);

        try
        {
            Assert.True(service.IsRunning);
            Assert.Equal($"http://localhost:{testPort}/metrics", service.MetricsUrl);

            using var httpClient = new System.Net.Http.HttpClient();
            var response = await httpClient.GetAsync(service.MetricsUrl);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();

            Assert.Contains("windows_cpu_usage_percent", body);
            Assert.Contains("windows_memory_physical_total_bytes", body);
            Assert.Contains("windows_disk_free_bytes", body);
        }
        finally
        {
            service.Stop();
            Assert.False(service.IsRunning);
        }
    }

    [Fact]
    public void RepairPipelineService_GetDefaultSteps_Returns6MicrosoftSteps()
    {
        var service = new RepairPipelineService();
        var steps = service.GetDefaultSteps();

        Assert.NotNull(steps);
        Assert.Equal(6, steps.Count);

        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(i + 1, steps[i].StepNumber);
            Assert.False(string.IsNullOrWhiteSpace(steps[i].Name));
            Assert.False(string.IsNullOrWhiteSpace(steps[i].CommandDescription));
            Assert.False(string.IsNullOrWhiteSpace(steps[i].Description));
            Assert.Equal(RepairStepStatus.Pending, steps[i].Status);
        }

        Assert.Contains("scanhealth", steps[0].CommandDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restorehealth", steps[1].CommandDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sfc", steps[2].CommandDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("startcomponentcleanup", steps[3].CommandDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SoftwareDistribution", steps[4].CommandDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("winsock", steps[5].CommandDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepairPipelineService_Cancellation_HandlesPreCancelledToken()
    {
        var service = new RepairPipelineService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.RunFullPipelineAsync(cts.Token);
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("cancelad", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExpandedTweakService_GetCategorizedTweaks_ContainsOver50ValidTweaks()
    {
        var service = new ExpandedTweakService();
        var tweaks = service.GetCategorizedTweaks();

        Assert.NotNull(tweaks);
        Assert.True(tweaks.Count >= 50, $"Expected at least 50 tweaks, got {tweaks.Count}");

        var categories = tweaks.Select(t => t.Category).Distinct().ToList();
        Assert.Contains("Gaming & Latencia", categories);
        Assert.Contains("Privacidad Radical", categories);
        Assert.Contains("Windows 11 UI & Explorer", categories);
        Assert.Contains("Rendimiento de Sistema", categories);

        Assert.All(tweaks, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Id), "Tweak ID cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(t.Name), "Tweak Name cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(t.Category), "Tweak Category cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(t.Description), "Tweak Description cannot be empty");
        });

        // Verify IsTweakApplied works without throwing
        foreach (var t in tweaks)
        {
            var isApplied = service.IsTweakApplied(t.Id);
            Assert.Equal(t.IsApplied, isApplied);
        }
    }

    [Fact]
    public void UwpDebloatService_GetDefaultCatalog_ContainsPreinstalledApps()
    {
        var catalog = UwpDebloatService.GetDefaultCatalog();

        Assert.NotNull(catalog);
        Assert.True(catalog.Count >= 10, "Expected at least 10 factory apps in catalog");

        var patterns = catalog.Select(c => c.PackagePattern).ToList();
        Assert.Contains("Microsoft.BingWeather", patterns);
        Assert.Contains("Microsoft.BingNews", patterns);
        Assert.Contains("Microsoft.MicrosoftSolitaireCollection", patterns);
        Assert.Contains("Microsoft.WindowsFeedbackHub", patterns);
        Assert.Contains("Microsoft.Getstarted", patterns);
        Assert.Contains("Microsoft.MSPaint", patterns);
        Assert.Contains("Microsoft.People", patterns);
        Assert.Contains("Microsoft.SkypeApp", patterns);
        Assert.Contains("Microsoft.549981C3F5F10", patterns);

        Assert.All(catalog, app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Id));
            Assert.False(string.IsNullOrWhiteSpace(app.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(app.PackagePattern));
        });
    }

    [Fact]
    public void ProcessDeepDiagService_GetProcessTree_ReturnsValidHierarchy()
    {
        var diagService = new ProcessDeepDiagService();
        var tree = diagService.GetProcessTree();
        var flat = diagService.GetFlattenedProcessTree();

        Assert.NotNull(tree);
        Assert.NotEmpty(tree);
        Assert.NotNull(flat);
        Assert.NotEmpty(flat);

        Assert.All(tree, root =>
        {
            Assert.True(root.Pid >= 0);
            Assert.False(string.IsNullOrWhiteSpace(root.Name));
        });

        int currentPid = Environment.ProcessId;
        var currentProcNode = flat.FirstOrDefault(n => n.Pid == currentPid);
        Assert.NotNull(currentProcNode);
        Assert.True(currentProcNode.WorkingSet64 > 0);
    }

    [Fact]
    public void ProcessDeepDiagService_GetProcessModules_ReturnsCurrentProcessModules()
    {
        var diagService = new ProcessDeepDiagService();
        int currentPid = Environment.ProcessId;
        var modules = diagService.GetProcessModules(currentPid);

        Assert.NotNull(modules);
        Assert.NotEmpty(modules);
        Assert.Contains(modules, m => m.ModuleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || m.ModuleName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProcessDeepDiagService_GetTcpConnections_ReturnsConnections()
    {
        var diagService = new ProcessDeepDiagService();
        var conns = diagService.GetTcpConnections();

        Assert.NotNull(conns);
        Assert.All(conns, c =>
        {
            Assert.True(c.OwningPid >= 0);
            Assert.False(string.IsNullOrWhiteSpace(c.LocalAddress));
            Assert.False(string.IsNullOrWhiteSpace(c.State));
        });
    }

    [Fact]
    public void ProcessDeepDiagService_GetProcessForensics_ReturnsMemoryAndThreads()
    {
        var diagService = new ProcessDeepDiagService();
        int currentPid = Environment.ProcessId;
        var forensics = diagService.GetProcessForensics(currentPid);

        Assert.NotNull(forensics);
        Assert.Equal(currentPid, forensics.Pid);
        Assert.True(forensics.WorkingSetBytes > 0);
        Assert.True(forensics.PrivateBytes > 0);
        Assert.True(forensics.HandleCount > 0);
        Assert.True(forensics.ThreadCount > 0);
        Assert.NotEmpty(forensics.Threads);
    }

    [Fact]
    public void AsrRulesService_NativeRulesMatrix_Contains16CanonicalRules()
    {
        Assert.Equal(16, AsrRulesService.NativeRules.Count);

        var guids = AsrRulesService.NativeRules.Select(r => r.Guid.ToLowerInvariant()).ToList();
        Assert.Equal(16, guids.Distinct().Count());

        string[] expectedGuids =
        {
            "56a8634f-1139-427a-a85b-0f5a066ab21f",
            "7674ba52-37eb-46dc-a42b-2e6f00c14f4f",
            "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2",
            "d4f940ab-401b-4efc-aadc-ad5f3c50688a",
            "b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4",
            "26190899-773f-478c-b965-0222241fb74a",
            "3b576839-7123-495a-8460-362c16e3d019",
            "756e358d-1488-49e2-86d7-ecbed922f94e",
            "d3e037e1-3eb8-44c8-a917-57927947596d",
            "e6db77e5-33e2-472b-9254-504432d709e1",
            "c1db55ab-c21a-4637-bb3f-a12568109d35",
            "01443614-cd74-433a-b99e-2ecdc07bfc25",
            "c0033c00-d16d-4114-a5a0-dc9b3a7d2ceb",
            "a8f5898e-1dc8-49a9-9878-85004b8a61e6",
            "33ddedf1-c6e0-47cb-833e-de6133960387",
            "4f940ab0-1a52-4140-a244-4670c3202575"
        };

        foreach (var expected in expectedGuids)
        {
            Assert.Contains(expected.ToLowerInvariant(), guids);
        }

        Assert.All(AsrRulesService.NativeRules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            Assert.False(string.IsNullOrWhiteSpace(rule.Description));
            Assert.False(string.IsNullOrWhiteSpace(rule.Category));
            Assert.False(string.IsNullOrWhiteSpace(rule.MitigationType));
        });
    }

    [Fact]
    public async Task AsrRulesService_GetRulesAsync_Returns16ConfiguredItems()
    {
        var asrService = new AsrRulesService();
        var rules = await asrService.GetRulesAsync();

        Assert.NotNull(rules);
        Assert.Equal(16, rules.Count);
        Assert.All(rules, r =>
        {
            Assert.False(string.IsNullOrWhiteSpace(r.Guid));
            Assert.False(string.IsNullOrWhiteSpace(r.Name));
            Assert.True(Enum.IsDefined(typeof(AsrRuleAction), r.Action));
        });
    }

    [Fact]
    public async Task AsrRulesService_GetDefenderStatusAsync_ReturnsStatusAndTotals()
    {
        var asrService = new AsrRulesService();
        var status = await asrService.GetDefenderStatusAsync();

        Assert.NotNull(status);
        Assert.Equal(16, status.TotalRulesCount);
        Assert.True(status.ActiveRulesCount >= 0 && status.ActiveRulesCount <= 16);
        Assert.True(status.BlockedRulesCount >= 0 && status.BlockedRulesCount <= 16);
        Assert.True(status.AuditedRulesCount >= 0 && status.AuditedRulesCount <= 16);
    }

    [Fact]
    public void HardwareService_GetTelemetrySnapshot_DoesNotThrow()
    {
        using var hwService = new HardwareService();
        var snapshot = hwService.GetTelemetrySnapshot();
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.CpuName));
    }

    [Fact]
    public void ProcessDeepDiagService_GetFlattenedProcessTree_DoesNotThrow()
    {
        var diagService = new ProcessDeepDiagService();
        var tree = diagService.GetFlattenedProcessTree();
        Assert.NotNull(tree);
        Assert.NotEmpty(tree);
    }

    [Fact]
    public void RtssService_FormatOsdMarkup_GeneratesValidTags()
    {
        using var rtss = new RtssService();
        string markup = rtss.FormatOsdMarkup(
            cpuLoad: 45.5,
            cpuName: "Intel Core i7-13700K",
            ramUsedGb: 14.2,
            ramTotalGb: 32.0,
            ramPercent: 44.3,
            gpuLoad: 68.0,
            gpuName: "NVIDIA GeForce RTX 4080",
            pingMs: 22
        );

        Assert.NotNull(markup);
        Assert.Contains("<C=10B981>", markup);
        Assert.Contains("<S=90>OmniWin OSD<S>", markup);
        Assert.Contains("CPU:", markup);
        Assert.Contains("45.5%", markup);
        Assert.Contains("RAM:", markup);
        Assert.Contains("14.2 GB", markup);
        Assert.Contains("GPU:", markup);
        Assert.Contains("68%", markup);
        Assert.Contains("PING:", markup);
        Assert.Contains("22 ms", markup);
    }

    [Fact]
    public void RtssService_IsRtssInstalled_And_Running_DoNotThrow()
    {
        using var rtss = new RtssService();
        var exInstalled = Record.Exception(() => rtss.IsRtssInstalled());
        var exRunning = Record.Exception(() => rtss.IsRtssRunning());

        Assert.Null(exInstalled);
        Assert.Null(exRunning);
    }

    [Fact]
    public void RtssService_UpdateOsd_DoesNotThrow()
    {
        using var rtss = new RtssService();
        var ex = Record.Exception(() => rtss.UpdateOsd("Test OmniWin OSD"));
        Assert.Null(ex);
    }

    [Fact]
    public void ThermalSensorService_GetSnapshot_ReturnsValidSnapshot()
    {
        var service = ThermalSensorService.Instance;
        var snapshot = service.GetSnapshot();

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Temperatures);
        Assert.NotNull(snapshot.Fans);
        Assert.NotNull(snapshot.ActiveAlerts);
    }

    [Fact]
    public void ThermalSensorService_ThresholdSettings_WorkProperly()
    {
        var service = new ThermalSensorService();
        service.Settings.CpuWarning = 70.0;
        service.Settings.CpuCritical = 85.0;

        Assert.Equal(70.0, service.Settings.CpuWarning);
        Assert.Equal(85.0, service.Settings.CpuCritical);
    }

    [Fact]
    public void ThermalSensorService_FanControl_DoesNotThrow()
    {
        var service = new ThermalSensorService();
        var exRamp = Record.Exception(() => service.SetAllFansPercent(100f));
        var exRestore = Record.Exception(() => service.RestoreAllFansAuto());

        Assert.Null(exRamp);
        Assert.Null(exRestore);
    }

    [Fact]
    public void FanCurveService_CalculateTargetFanSpeed_InterpolatesProperly()
    {
        var service = new FanCurveService();
        service.SetActiveProfile("balanced");

        double speedAt50 = service.CalculateTargetFanSpeed(cpuTemp: 50.0, gpuTemp: 45.0, applySmoothing: false);
        Assert.InRange(speedAt50, 30.0, 50.0);

        double speedAt85 = service.CalculateTargetFanSpeed(cpuTemp: 85.0, gpuTemp: 70.0, applySmoothing: false);
        Assert.InRange(speedAt85, 75.0, 100.0);
    }

    [Fact]
    public void FanCurveService_Hysteresis_HoldsSpeedOnSmallDrops()
    {
        var profile = new FanCurveProfile
        {
            Id = "test_hysteresis",
            HysteresisCelsius = 4.0,
            Points = new List<FanCurvePoint>
            {
                new(40.0, 30.0),
                new(70.0, 70.0),
                new(85.0, 100.0)
            }
        };

        // First evaluate at 70°C
        double speedAt70 = profile.Evaluate(70.0);
        Assert.True(speedAt70 > 30.0);

        // Drop to 68°C (only 2°C drop, less than 4°C hysteresis)
        double speedAt68 = profile.Evaluate(68.0);
        Assert.Equal(speedAt70, speedAt68);
    }

    [Fact]
    public void ThermalHealthDiagnosticsService_EvaluatesScoresCorrectly()
    {
        var diag = new ThermalHealthDiagnosticsService();

        // 1. Normal system
        var normalReport = diag.EvaluateSystemHealth(cpuTemp: 48.0, cpuLoad: 15.0, cpuPowerWatts: 35.0, gpuTemp: 42.0, maxSsdTemp: 40.0);
        Assert.InRange(normalReport.HealthScore, 85, 100);
        Assert.False(normalReport.IsHotIdleDetected);

        // 2. Hot-Idle (68°C at 10% load -> dry thermal paste or pump issue)
        var hotIdleReport = diag.EvaluateSystemHealth(cpuTemp: 68.0, cpuLoad: 10.0, cpuPowerWatts: 20.0, gpuTemp: 42.0, maxSsdTemp: 40.0);
        Assert.True(hotIdleReport.IsHotIdleDetected);
        Assert.True(hotIdleReport.HealthScore < 70);

        // 3. NVMe High Temp Throttling Risk
        var nvmeHotReport = diag.EvaluateSystemHealth(cpuTemp: 50.0, cpuLoad: 20.0, cpuPowerWatts: 30.0, gpuTemp: 45.0, maxSsdTemp: 72.0);
        Assert.True(nvmeHotReport.IsSsdThrottlingRisk);
    }

    [Fact]
    public void DynamicThermalProfileService_RegistersCustomGame()
    {
        var dyn = DynamicThermalProfileService.Instance;
        dyn.AddCustomGameProcess("CustomGame.exe");
        Assert.NotNull(dyn);
    }

    [Fact]
    public async Task EmergencyThermalGuard_EvaluatesTicksGracefully()
    {
        var guard = EmergencyThermalGuard.Instance;
        guard.AutoPowerMitigationEnabled = false; // Disable actual powercfg modification in unit test

        await guard.EvaluateTemperatureTicksAsync(cpuTemp: 50.0, gpuTemp: 50.0);
        Assert.False(guard.IsEmergencyMitigationActive);
    }

    [Fact]
    public void ThermalSensorService_SafeDefaults_EmergencyCoolingDisabled()
    {
        var service = ThermalSensorService.Instance;
        Assert.False(service.Settings.EnableEmergencyCooling);
        Assert.False(service.Settings.EnableAudioAlarm);
        
        // Ensure RestoreAllFansAuto executes cleanly
        var ex = Record.Exception(() => service.RestoreAllFansAuto());
        Assert.Null(ex);
    }

    [Fact]
    public void AwakeService_ActivationAndDeactivation_WorksProperly()
    {
        var awake = AwakeService.Instance;

        // 1. Activate Indefinite
        bool res1 = awake.Activate(AwakeMode.KeepAwakeIndefinite, keepDisplayOn: true);
        Assert.True(res1);
        Assert.True(awake.CurrentState.IsActive);
        Assert.True(awake.CurrentState.KeepDisplayOn);
        Assert.Equal(AwakeMode.KeepAwakeIndefinite, awake.CurrentState.Mode);

        // 2. Activate Timed (1 second for quick test)
        bool res2 = awake.Activate(AwakeMode.Timed, TimeSpan.FromMinutes(30), keepDisplayOn: false);
        Assert.True(res2);
        Assert.True(awake.CurrentState.IsActive);
        Assert.False(awake.CurrentState.KeepDisplayOn);
        Assert.NotNull(awake.CurrentState.ExpiresAt);

        // 3. Deactivate
        bool res3 = awake.Deactivate();
        Assert.True(res3);
        Assert.False(awake.CurrentState.IsActive);
        Assert.Equal(AwakeMode.Disabled, awake.CurrentState.Mode);
    }

    [Fact]
    public void AudioMixerService_PeakMeter_ReturnsValidRange()
    {
        var mixer = new AudioMixerService();
        float peak = mixer.GetMasterPeakValue();
        Assert.InRange(peak, 0.0f, 1.0f);

        var sessions = mixer.GetAudioSessions();
        Assert.NotNull(sessions);
        foreach (var s in sessions)
        {
            Assert.InRange(s.PeakValue, 0.0f, 1.0f);
            Assert.InRange(s.PeakPercent, 0, 100);
        }
    }

    [Fact]
    public void NvidiaGpuTuningService_ReturnsValidStatus()
    {
        var service = NvidiaGpuTuningService.Instance;
        var status = service.GetStatus();

        Assert.NotNull(status);
        if (status.IsNvidiaGpuDetected)
        {
            Assert.NotEmpty(status.GpuModel);
            Assert.Contains("NVIDIA", status.GpuModel, StringComparison.OrdinalIgnoreCase);
            Assert.NotEmpty(status.DriverVersion);
            Assert.NotEmpty(status.VbiosVersion);
            Assert.NotEmpty(status.PciBusId);
            Assert.True(status.PcieGenMax >= 1, "PCIe Max Gen should be at least 1");
            Assert.True(status.PcieWidthMax >= 1, "PCIe Max Width should be at least 1");
            Assert.False(string.IsNullOrWhiteSpace(status.SubsystemVendor), "Subsystem vendor should be identified");
            Assert.False(string.IsNullOrWhiteSpace(status.PcieLinkSummary), "Link summary should not be empty");
            Assert.False(string.IsNullOrWhiteSpace(status.ReBarStatusText), "ReBAR status text should be populated");
        }

        // Test safe tuning toggles
        bool setLl = service.SetLowLatencyMode(NvidiaLowLatencyMode.On);
        Assert.True(setLl);

        bool setPwr = service.SetPowerManagementMode(NvidiaPowerMode.PreferMaximumPerformance);
        Assert.True(setPwr);

        bool setFps = service.SetFrameRateLimiter(141);
        Assert.True(setFps);
    }

    [Fact]
    public void NvidiaGpuTuningService_VendorResolution_MatchesKnownIds()
    {
        Assert.Equal("ZOTAC Technology", NvidiaGpuTuningService.ResolveVendorFromSubsystemId("0x161219DA"));
        Assert.Equal("ASUS (Republic of Gamers / TUF)", NvidiaGpuTuningService.ResolveVendorFromSubsystemId("0x87651043"));
        Assert.Equal("NVIDIA (Founders Edition)", NvidiaGpuTuningService.ResolveVendorFromSubsystemId("0x123410DE"));
        Assert.Equal("MSI (Micro-Star International)", NvidiaGpuTuningService.ResolveVendorFromSubsystemId("0x99991462"));
        Assert.Equal("GIGABYTE / AORUS", NvidiaGpuTuningService.ResolveVendorFromSubsystemId("0x55551458"));
        Assert.Equal("EVGA Corporation", NvidiaGpuTuningService.ResolveVendorFromSubsystemId("0x11113842"));
    }

    [Fact]
    public async Task DiskDuplicateService_FindsDuplicateFilesAccurately()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OmniWin_Dup_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            byte[] contentA = new byte[8192];
            new Random(42).NextBytes(contentA);

            byte[] contentB = new byte[8192];
            new Random(99).NextBytes(contentB);

            // Write 3 duplicates of A and 1 of B
            string fileA1 = Path.Combine(tempDir, "fileA1.bin");
            string fileA2 = Path.Combine(tempDir, "fileA2.bin");
            string fileA3 = Path.Combine(tempDir, "fileA3.bin");
            string fileB = Path.Combine(tempDir, "fileB.bin");

            await File.WriteAllBytesAsync(fileA1, contentA);
            await File.WriteAllBytesAsync(fileA2, contentA);
            await File.WriteAllBytesAsync(fileA3, contentA);
            await File.WriteAllBytesAsync(fileB, contentB);

            var service = DiskDuplicateService.Instance;
            var duplicates = await service.FindDuplicatesAsync(tempDir, minSizeBytes: 1024);

            Assert.Single(duplicates);
            Assert.Equal(3, duplicates[0].FilePaths.Count);
            Assert.Equal(8192 * 2, duplicates[0].WastedBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task BrowserOptimizationService_ScansDatabasesSafely()
    {
        var service = BrowserOptimizationService.Instance;
        var databases = await service.ScanBrowserDatabasesAsync();
        Assert.NotNull(databases);

        // Optimizing empty or actual items should never throw
        var report = await service.OptimizeDatabasesAsync(databases);
        Assert.NotNull(report);
        Assert.True(report.TotalReclaimedBytes >= 0);
    }

    [Fact]
    public void FramePacingBenchmarkService_CalculatesMetricsAccurately()
    {
        // Simulate 1000 frames: 980 frames at 16.6ms (60 FPS), 15 frames at 33.3ms (30 FPS), 5 frames at 50.0ms (stutters)
        var samples = new List<double>();
        for (int i = 0; i < 980; i++) samples.Add(16.6);
        for (int i = 0; i < 15; i++) samples.Add(33.3);
        for (int i = 0; i < 5; i++) samples.Add(50.0);

        var report = FramePacingBenchmarkService.CalculateReport("CS2 Benchmark", TimeSpan.FromSeconds(16.7), samples);

        Assert.Equal(1000, report.TotalFrames);
        Assert.InRange(report.AverageFps, 55.0, 61.0);
        Assert.InRange(report.OnePercentLowFps, 20.0, 35.0);
        Assert.InRange(report.PointOnePercentLowFps, 18.0, 25.0);
        Assert.Equal(50.0, report.MaxFrametimeMs);
        Assert.Equal(16.6, report.MinFrametimeMs);
        Assert.True(report.StutterCount >= 5);
    }

    [Fact]
    public void TrayMonitorService_GeneratesIconsSafelyWithoutLeak()
    {
        using var iconCpu = TrayMonitorService.CreateTemperatureIcon(54, isCpu: true, size: 16);
        Assert.NotNull(iconCpu);
        Assert.Equal(16, iconCpu.Width);
        Assert.Equal(16, iconCpu.Height);

        using var iconGpu = TrayMonitorService.CreateTemperatureIcon(68, isCpu: false, size: 16);
        Assert.NotNull(iconGpu);
        Assert.Equal(16, iconGpu.Width);
        Assert.Equal(16, iconGpu.Height);

        var colorCold = TrayMonitorService.GetTemperatureColor(40);
        var colorHot = TrayMonitorService.GetTemperatureColor(88);
        Assert.NotEqual(colorCold, colorHot);
    }

    [Fact]
    public void CpuOptimizationService_DetectsCpuAndArchitectureAccurately()
    {
        var cpu = CpuOptimizationService.Instance.GetCpuDetails();
        Assert.NotNull(cpu);
        Assert.NotEmpty(cpu.Name);
        Assert.NotEmpty(cpu.Vendor);
        Assert.True(cpu.LogicalProcessors >= 1, "Must detect at least 1 logical processor");

        // Safe tuning call should execute without throwing
        bool ok = CpuOptimizationService.Instance.ApplyBalancedCpuTuning();
        Assert.True(ok);
    }

    [Fact]
    public void UniversalGpuService_EnumeratesGpusAndSupportsMultiVendor()
    {
        var gpus = UniversalGpuService.Instance.GetInstalledGpus();
        Assert.NotNull(gpus);
        Assert.NotEmpty(gpus);
        Assert.Contains(gpus, g => g.VendorType != GpuVendorType.Unknown);

        foreach (var gpu in gpus)
        {
            Assert.False(string.IsNullOrWhiteSpace(gpu.Name));
            Assert.False(string.IsNullOrWhiteSpace(gpu.ReBarTechnologyName));
        }
    }

    [Fact]
    public void PowerService_ProfileRecommendations_ReturnSpecificValues()
    {
        var service = new PowerService();

        var recGaming = service.GetProfileRecommendation("Ultimate Performance");
        Assert.Contains("100%", recGaming.CpuCoreParkingRecommendation);
        Assert.Contains("0%", recGaming.EnergyPreferenceRecommendation);
        Assert.NotEmpty(recGaming.Highlights);

        var recEco = service.GetProfileRecommendation("Power saver");
        Assert.NotEmpty(recEco.CpuCoreParkingRecommendation);
        Assert.NotEmpty(recEco.Highlights);

        var recBalanced = service.GetProfileRecommendation("Balanced");
        Assert.Contains("50%", recBalanced.EnergyPreferenceRecommendation);
    }

    [Fact]
    public void MotherboardBiosService_ReturnsValidHardwareData()
    {
        var service = MotherboardBiosService.Instance;
        var info = service.GetInfo();

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.MotherboardManufacturer));
        Assert.False(string.IsNullOrWhiteSpace(info.BiosVendor));
        Assert.True(info.RamModules.Count > 0, "System should report physical RAM modules");
        Assert.True(info.TotalRamGb > 0, "Total RAM must be positive");
    }

    [Fact]
    public void SoftwareUninstallerService_EnumeratesInstalledApps()
    {
        var service = SoftwareUninstallerService.Instance;
        var apps = service.GetInstalledApps();

        Assert.NotNull(apps);
        Assert.NotEmpty(apps);

        var firstApp = apps.First();
        Assert.False(string.IsNullOrWhiteSpace(firstApp.DisplayName));

        // Scan leftovers should execute cleanly without error
        var leftovers = service.ScanLeftovers(firstApp);
        Assert.NotNull(leftovers);
        Assert.Equal(firstApp.DisplayName, leftovers.AppName);
    }

    [Fact]
    public async Task IncidentInvestigatorService_CanReadIncidents()
    {
        var service = new IncidentInvestigatorService();
        var incidents = await service.GetIncidentsAsync(10);

        Assert.NotNull(incidents);
        foreach (var inc in incidents)
        {
            Assert.False(string.IsNullOrWhiteSpace(inc.EventTitle));
            Assert.False(string.IsNullOrWhiteSpace(inc.BadgeColor));
            Assert.False(string.IsNullOrWhiteSpace(inc.Icon));
        }
    }

    [Fact]
    public void KernelLatencyService_CanReadTimerResolution()
    {
        var service = new KernelLatencyService();
        var info = service.GetTimerResolution();

        Assert.NotNull(info);
        Assert.True(info.MinResolutionMs > 0);
        Assert.True(info.MaxResolutionMs > 0);
        Assert.True(info.CurrentResolutionMs > 0);
        Assert.False(string.IsNullOrWhiteSpace(info.FormattedCurrent));
    }

    [Fact]
    public async Task KernelLatencyService_RunJitterBenchmark_ReturnsValidResults()
    {
        var service = new KernelLatencyService();
        var bench = await service.RunJitterBenchmarkAsync(100);

        Assert.NotNull(bench);
        Assert.True(bench.HighResolutionFrequencyMhz > 0);
        Assert.False(string.IsNullOrWhiteSpace(bench.Verdict));
        Assert.False(string.IsNullOrWhiteSpace(bench.VerdictColor));
    }

    [Fact]
    public async Task DiskSpaceAnalyzerService_CanAnalyzeDirectory()
    {
        var service = new DiskSpaceAnalyzerService();
        string tempDir = Path.GetTempPath();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await service.AnalyzeDriveAsync(tempDir, null, cts.Token);

        Assert.NotNull(result);
        Assert.NotNull(result.TopFolders);
        Assert.NotNull(result.TopLargestFiles);
        Assert.NotNull(result.Categories);
        Assert.NotNull(result.SystemFiles);
    }

    [Fact]
    public async Task DriverService_RestoreDrivers_HandlesEmptyOrMissingFolder()
    {
        var service = new DriverService();
        string nonExistent = Path.Combine(Path.GetTempPath(), "NonExistent_" + Guid.NewGuid().ToString("N"));
        var res1 = await service.RestoreDriversAsync(nonExistent);
        Assert.False(res1.Success);
        Assert.Contains("no existe", res1.Message, StringComparison.OrdinalIgnoreCase);

        string emptyDir = Path.Combine(Path.GetTempPath(), "EmptyDir_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyDir);
        try
        {
            var res2 = await service.RestoreDriversAsync(emptyDir);
            Assert.False(res2.Success);
            Assert.Contains("No se encontraron archivos .inf", res2.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(emptyDir, true);
        }
    }
}



