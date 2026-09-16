using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using OmniWin.Core.Services;

namespace OmniWin.Tests;

public class DriverCenterAndForensicTests : IDisposable
{
    private readonly string _tempTestDir;

    public DriverCenterAndForensicTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "OmniWin_DriverTests_" + Guid.NewGuid().ToString("N"));
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
    public void DriverCenterService_CategorizeDevice_CategorizesCorrectly()
    {
        Assert.Equal("GPU", DriverCenterService.CategorizeDevice("Display", "NVIDIA GeForce RTX 4080"));
        Assert.Equal("GPU", DriverCenterService.CategorizeDevice("Display", "AMD Radeon RX 7900 XTX"));
        Assert.Equal("GPU", DriverCenterService.CategorizeDevice("DisplayAdapter", "Intel(R) Arc(TM) A770 Graphics"));
        Assert.Equal("Audio", DriverCenterService.CategorizeDevice("Media", "Realtek High Definition Audio"));
        Assert.Equal("Network", DriverCenterService.CategorizeDevice("Net", "Intel(R) Wi-Fi 6E AX211 160MHz"));
        Assert.Equal("Network", DriverCenterService.CategorizeDevice("Net", "Realtek PCIe 2.5GbE Family Controller"));
        Assert.Equal("Bluetooth", DriverCenterService.CategorizeDevice("Bluetooth", "Intel(R) Wireless Bluetooth(R)"));
        Assert.Equal("Storage", DriverCenterService.CategorizeDevice("SCSIAdapter", "Standard NVM Express Controller"));
        Assert.Equal("Chipset", DriverCenterService.CategorizeDevice("System", "PCI Express Root Port"));
        Assert.Equal("Other", DriverCenterService.CategorizeDevice("HIDClass", "Standard PS/2 Keyboard"));
    }

    [Fact]
    public void DriverCenterService_VerifyFileSignature_ValidatesSystemBinary()
    {
        var service = DriverCenterService.Instance;
        string systemFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (!File.Exists(systemFile))
        {
            systemFile = Path.Combine(Environment.SystemDirectory, "kernel32.dll");
        }

        if (File.Exists(systemFile))
        {
            var result = DriverCenterService.VerifyFileSignature(systemFile);
            Assert.True(result.IsSigned, $"System binary {systemFile} should have an Authenticode signature.");
            Assert.True(result.IsTrusted, $"System binary {systemFile} signature should be trusted by Windows trust store.");
            Assert.Contains("Microsoft", result.SignerName);
            Assert.Contains("Verified", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DriverCenterService_VerifyFileSignature_DetectsUnsignedFile()
    {
        string unsignedFile = Path.Combine(_tempTestDir, "unsigned_test.dll");
        File.WriteAllText(unsignedFile, "This is not a signed binary.");

        var result = DriverCenterService.VerifyFileSignature(unsignedFile);
        Assert.False(result.IsSigned);
        Assert.False(result.IsTrusted);
        Assert.Contains("Unsigned", result.StatusMessage);
    }

    [Fact]
    public void DriverCenterService_DriverHistory_PersistsAndLoadsAcrossInstances()
    {
        string historyFile = Path.Combine(_tempTestDir, "test_driver_history.json");
        var service = new DriverCenterService(historyFile);

        var historyBefore = service.GetDriverHistory();
        Assert.NotNull(historyBefore);
        Assert.Empty(historyBefore);

        var entry = new DriverHistoryEntry
        {
            Action = "Backup",
            DeviceName = "NVIDIA GeForce RTX 4080",
            PreviousVersion = "555.85",
            InstalledVersion = "561.09",
            Source = "NVIDIA Official CDN",
            Success = true
        };

        service.RecordDriverHistory(entry);

        // Reload from same history file
        var serviceReloaded = new DriverCenterService(historyFile);
        var historyAfter = serviceReloaded.GetDriverHistory();

        Assert.NotNull(historyAfter);
        Assert.Single(historyAfter);
        Assert.Equal("NVIDIA GeForce RTX 4080", historyAfter[0].DeviceName);
        Assert.Equal("561.09", historyAfter[0].InstalledVersion);
        Assert.Equal("Backup", historyAfter[0].Action);
        Assert.True(historyAfter[0].Success);
    }

    [Fact]
    public async Task DriverCenterService_EnumerateDrivers_ReturnsDiscoveredDevices()
    {
        var service = DriverCenterService.Instance;
        var drivers = await service.EnumerateDriversAsync();

        Assert.NotNull(drivers);
        // On any real Windows machine there are dozens of active devices
        Assert.NotEmpty(drivers);

        // Verify that items have populated names and valid categories
        var first = drivers.First();
        Assert.False(string.IsNullOrWhiteSpace(first.DeviceName));
        Assert.False(string.IsNullOrWhiteSpace(first.Category));
    }
}
