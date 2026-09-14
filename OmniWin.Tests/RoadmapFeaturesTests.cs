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
}
