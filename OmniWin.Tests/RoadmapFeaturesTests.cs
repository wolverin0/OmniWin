using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OmniWin.Core.Services;
using OmniWin.UI.Services;
using Xunit;

namespace OmniWin.Tests;

public class RoadmapFeaturesTests
{
    [Fact]
    public void AppSettingsService_CanLoadAndPersistSettings()
    {
        var svc = AppSettingsService.Instance;
        Assert.NotNull(svc);
        Assert.NotNull(svc.Settings);

        // Update settings
        svc.SaveSettings(s =>
        {
            s.SelectedProfile = "Gaming";
            s.TrafficWidgetX = 150.0;
            s.TrafficWidgetY = 250.0;
            s.Language = "es";
        });

        Assert.Equal("Gaming", svc.Settings.SelectedProfile);
        Assert.Equal(150.0, svc.Settings.TrafficWidgetX);
        Assert.Equal(250.0, svc.Settings.TrafficWidgetY);
        Assert.Equal("es", svc.Settings.Language);
    }

    [Fact]
    public void OptimizationReportService_GeneratesValidHtmlReport()
    {
        var svc = OptimizationReportService.Instance;
        Assert.NotNull(svc);

        string tempPath = Path.Combine(Path.GetTempPath(), $"omniwin-test-report-{Guid.NewGuid()}.html");
        try
        {
            var model = new OptimizationReportModel
            {
                Hostname = "TEST-PC",
                OsVersion = "Windows 11 Pro 23H2",
                CpuModel = "Intel Core i9-14900K",
                TotalRamGb = "64 GB",
                GpuModel = "NVIDIA GeForce RTX 4090",
                SelectedProfile = "Gaming",
                RestorePointCreated = true,
                RamPurgedBytes = 1024 * 1024 * 512,
                DiskFreedBytes = 1024 * 1024 * 1024,
                Tweaks = new()
                {
                    new ReportTweakEntry
                    {
                        Category = "Gaming",
                        Name = "Disable GameDVR",
                        Description = "Disables background Xbox recording",
                        RegistryPath = "HKCU\\System\\GameConfigStore",
                        PreviousValue = "1",
                        NewValue = "0",
                        Success = true
                    }
                }
            };

            string generatedPath = svc.GenerateHtmlReport(model, tempPath);
            Assert.True(File.Exists(generatedPath));

            string html = File.ReadAllText(generatedPath);
            Assert.Contains("OmniWin", html);
            Assert.Contains("Intel Core i9-14900K", html);
            Assert.Contains("NVIDIA GeForce RTX 4090", html);
            Assert.Contains("Disable GameDVR", html);
            Assert.Contains("HKCU\\System\\GameConfigStore", html);
            Assert.Contains("<!DOCTYPE html>", html);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void UpdateCheckService_CurrentVersionMatchesConstant()
    {
        var svc = UpdateCheckService.Instance;
        Assert.NotNull(svc);
        Assert.Equal("1.2.0", UpdateCheckService.CURRENT_VERSION);
    }

    [Fact]
    public void LocalizationService_ReturnsSpanishAndEnglishStrings()
    {
        var loc = LocalizationService.Instance;
        Assert.NotNull(loc);

        loc.SetLanguage("es");
        Assert.Equal("es", loc.CurrentLanguage);
        Assert.Equal("⚡ Liberar RAM", loc.Get("QuickPurge"));
        Assert.Equal("Dashboard Central", loc.Get("Dashboard"));

        loc.SetLanguage("en");
        Assert.Equal("en", loc.CurrentLanguage);
        Assert.Equal("⚡ Free RAM", loc.Get("QuickPurge"));
        Assert.Equal("Central Dashboard", loc.Get("Dashboard"));

        // Fallback testing
        Assert.Equal("DefaultValue", loc.Get("NonExistentKey", "DefaultValue"));

        // Reset to default
        loc.SetLanguage("es");
    }

    [Fact]
    public void GlobalHotkeyService_ConstantsAreValid()
    {
        Assert.Equal(9001, GlobalHotkeyService.HOTKEY_OVERLAY_ID);
        Assert.Equal(9002, GlobalHotkeyService.HOTKEY_WIDGET_ID);
        Assert.Equal(9003, GlobalHotkeyService.HOTKEY_PURGE_ID);
    }

    [Fact]
    public void WinGetManifests_FilesExistAndHaveValidStructure()
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\.."));
        string wingetDir = Path.Combine(repoRoot, "distribution", "winget");

        if (!Directory.Exists(wingetDir))
        {
            // If running from different workdir, check relative to source
            wingetDir = @"C:\Users\pauol\Source\Repos\OmniWin\distribution\winget";
        }

        Assert.True(Directory.Exists(wingetDir), $"WinGet directory not found at: {wingetDir}");

        string versionFile = Path.Combine(wingetDir, "pauol.OmniWin.yaml");
        string installerFile = Path.Combine(wingetDir, "pauol.OmniWin.installer.yaml");
        string localeEnFile = Path.Combine(wingetDir, "pauol.OmniWin.locale.en-US.yaml");
        string localeEsFile = Path.Combine(wingetDir, "pauol.OmniWin.locale.es-ES.yaml");

        Assert.True(File.Exists(versionFile), "Version manifest missing");
        Assert.True(File.Exists(installerFile), "Installer manifest missing");
        Assert.True(File.Exists(localeEnFile), "en-US locale manifest missing");
        Assert.True(File.Exists(localeEsFile), "es-ES locale manifest missing");

        string versionContent = File.ReadAllText(versionFile);
        Assert.Contains("PackageIdentifier: pauol.OmniWin", versionContent);
        Assert.Contains("PackageVersion: 1.2.0", versionContent);

        string installerContent = File.ReadAllText(installerFile);
        Assert.Contains("PackageIdentifier: pauol.OmniWin", installerContent);
        Assert.Contains("InstallerType: zip", installerContent);
        Assert.Contains("PortableCommandAlias: omniwin", installerContent);
    }

    [Fact]
    public void EtwFramePacing_CalculatesMetricsAndLowsAccurately()
    {
        var service = EtwFramePacingService.Instance;
        Assert.NotNull(service);

        // Test static frame pacing calculation with known distribution
        // 100 frames: 98 frames at 6.94ms (~144 FPS) and 2 frames at 30ms (~33 FPS)
        var frametimes = new System.Collections.Generic.List<double>();
        for (int i = 0; i < 98; i++) frametimes.Add(6.94);
        frametimes.Add(30.0);
        frametimes.Add(35.0);

        var report = FramePacingBenchmarkService.CalculateReport("Test Benchmark", TimeSpan.FromSeconds(1), frametimes);

        Assert.NotNull(report);
        Assert.Equal(100, report.TotalFrames);
        Assert.True(report.AverageFps > 120, $"Average FPS expected > 120, was {report.AverageFps}");
        Assert.True(report.OnePercentLowFps < report.AverageFps, "1% Low must be lower than Average FPS");
        Assert.True(report.PointOnePercentLowFps <= report.OnePercentLowFps, "0.1% Low must be <= 1% Low");
        Assert.True(report.StutterCount >= 2, "Stutter count should capture frame spikes");
    }

    [Fact]
    public async Task PcieLinkInspector_AuditDetailedBandwidth_ExecutesAndClassifies()
    {
        var inspector = PcieLinkInspector.Instance;
        Assert.NotNull(inspector);

        var doctorResult = inspector.RunDoctorCheck();
        Assert.NotNull(doctorResult);
        Assert.NotNull(doctorResult.Devices);

        string reportText = await inspector.AuditDetailedBandwidthAsync();
        Assert.False(string.IsNullOrWhiteSpace(reportText));
        Assert.Contains("OMNIWIN — AUDITORÍA PROFUNDA DE BUS PCIE", reportText);
    }

    [Fact]
    public async Task RollbackSnapshotService_CreatesAndListsSnapshots()
    {
        var service = RollbackSnapshotService.Instance;
        Assert.NotNull(service);

        // Don't invoke Windows Restore Point in unit tests to avoid requiring admin elevation
        var snapshot = await service.CreateSnapshotAsync("Unit Test Pre-Tweak", "Validation Profile", createWindowsRestorePoint: false);
        Assert.NotNull(snapshot);
        Assert.Equal("Unit Test Pre-Tweak", snapshot.Name);
        Assert.True(snapshot.RegistryKeysCount > 0);

        string backupFile = Path.Combine(service.SnapshotDirectory, $"{snapshot.Id}.json");
        Assert.True(File.Exists(backupFile), "Backup JSON file must exist on disk");

        var all = service.GetSnapshots();
        Assert.Contains(all, s => s.Id == snapshot.Id);
    }

    [Fact]
    public async Task OpenRgbClientService_ThermalReactiveGradient_ComputesExpectedColors()
    {
        var service = OpenRgbClientService.Instance;
        Assert.NotNull(service);

        // Cold temperature (< 45°C) -> Should set Cyan/Arctic
        await service.SetThermalReactiveColorAsync(35.0);
        Assert.Equal("#00E5FF", service.CurrentHexColor);

        // Normal temperature (45 - 65°C) -> Should set Emerald
        await service.SetThermalReactiveColorAsync(55.0);
        Assert.Equal("#10B981", service.CurrentHexColor);

        // Warm temperature (65 - 75°C) -> Should set Amber
        await service.SetThermalReactiveColorAsync(70.0);
        Assert.Equal("#F59E0B", service.CurrentHexColor);

        // Hot temperature (> 75°C) -> Should set Red
        await service.SetThermalReactiveColorAsync(85.0);
        Assert.Equal("#EF4444", service.CurrentHexColor);
    }

    [Fact]
    public void CompanionServer_NetworkAdapterSelection_UpdatesPairingUrlAndQrCode()
    {
        var server = CompanionServerService.Instance;
        Assert.NotNull(server);

        var adapters = CompanionServerService.GetAvailableNetworkAdapters();
        Assert.NotNull(adapters);
        Assert.NotEmpty(adapters);

        var first = adapters[0];
        Assert.False(string.IsNullOrWhiteSpace(first.IpAddress));
        Assert.False(string.IsNullOrWhiteSpace(first.DisplayName));

        // Test changing IP selection
        string testIp = "192.168.100.188";
        server.SetSelectedIp(testIp);

        Assert.Equal(testIp, server.LocalIp);
        Assert.Contains(testIp, server.PairingUrl);
        Assert.Contains($":{server.Port}/?token=", server.PairingUrl);

        // Verify QR code generation succeeds for the updated URL
        byte[] qrBytes = server.GenerateQrCodePngBytes(5);
        Assert.NotNull(qrBytes);
        Assert.True(qrBytes.Length > 0);

        // Verify setting was persisted in AppSettingsService
        Assert.Equal(testIp, AppSettingsService.Instance.Settings.PreferredCompanionIp);
    }
}
