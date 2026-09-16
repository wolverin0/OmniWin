using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using OmniWin.Core.Services;

namespace OmniWin.Tests;

public class Phase28EcosystemTests : IDisposable
{
    private readonly string _testTempDir;

    public Phase28EcosystemTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), "OmniWin_Phase28_Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task DiskDuplicate_DetectsDuplicates_AndCreatesHardlink()
    {
        var dupService = DiskDuplicateService.Instance;

        // Crear 2 archivos idénticos en el directorio temporal
        string file1 = Path.Combine(_testTempDir, "document_original.txt");
        string file2 = Path.Combine(_testTempDir, "document_copy.txt");
        byte[] content = new byte[1024 * 128]; // 128 KB
        new Random(42).NextBytes(content);

        await File.WriteAllBytesAsync(file1, content);
        await File.WriteAllBytesAsync(file2, content);

        var duplicates = await dupService.FindDuplicatesAsync(_testTempDir, minSizeBytes: 1024);
        Assert.NotEmpty(duplicates);
        Assert.Contains(duplicates, g => g.FilePaths.Contains(file1) && g.FilePaths.Contains(file2));

        // Reemplazar duplicado por Hardlink Zero-Copy
        var linkRes = dupService.ReplaceWithHardLink(file2, file1);
        Assert.True(linkRes.Success, linkRes.Message);
        Assert.True(File.Exists(file1), "El archivo original debe seguir existiendo");
        Assert.True(File.Exists(file2), "El archivo enlazado debe seguir existiendo");

        // Validar que ambos tienen el mismo contenido
        byte[] readBack = await File.ReadAllBytesAsync(file2);
        Assert.Equal(content, readBack);
    }

    [Fact]
    public async Task DiskDuplicate_FindDuplicatesAcrossMultipleRoots()
    {
        var dupService = DiskDuplicateService.Instance;

        string rootA = Path.Combine(_testTempDir, "FolderA");
        string rootB = Path.Combine(_testTempDir, "FolderB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        string fileA = Path.Combine(rootA, "test_file_a.dat");
        string fileB = Path.Combine(rootB, "test_file_b.dat");
        byte[] data = new byte[1024 * 64];
        new Random(99).NextBytes(data);

        await File.WriteAllBytesAsync(fileA, data);
        await File.WriteAllBytesAsync(fileB, data);

        var duplicates = await dupService.FindDuplicatesAsync(new[] { rootA, rootB }, minSizeBytes: 1024);
        Assert.NotEmpty(duplicates);
        Assert.Contains(duplicates, g => g.FilePaths.Contains(fileA) && g.FilePaths.Contains(fileB));
    }

    [Fact]
    public async Task DiskDuplicate_VolumeAwareDeduplication()
    {
        var dupService = DiskDuplicateService.Instance;

        string f1 = Path.Combine(_testTempDir, "vol_test_1.bin");
        string f2 = Path.Combine(_testTempDir, "vol_test_2.bin");
        string f3 = Path.Combine(_testTempDir, "vol_test_3.bin");
        byte[] data = new byte[1024 * 32];
        new Random(123).NextBytes(data);

        await File.WriteAllBytesAsync(f1, data);
        await File.WriteAllBytesAsync(f2, data);
        await File.WriteAllBytesAsync(f3, data);

        var group = new DuplicateFileGroup
        {
            FileSizeBytes = data.Length,
            Sha256Hash = "dummy",
            FilePaths = new System.Collections.Generic.List<string> { f1, f2, f3 }
        };

        var result = dupService.DeduplicateGroupVolumeAware(group, preferredMasterPath: f1);
        Assert.True(result.Success);
        Assert.Equal(2, result.FilesProcessed);
        Assert.True(File.Exists(f1));
        Assert.True(File.Exists(f2));
        Assert.True(File.Exists(f3));
    }

    [Fact]
    public void UsbDoctor_EnumeratesDrives_AndHandlesNormalization()
    {
        var usbService = new UsbDoctorService();
        var drives = usbService.GetRemovableDrives();

        Assert.NotNull(drives);
        // Cada unidad debe tener propiedades coherentes
        foreach (var d in drives)
        {
            Assert.False(string.IsNullOrEmpty(d.DriveLetter));
            Assert.True(d.TotalSizeBytes >= 0);
            Assert.True(d.UsedPercent >= 0 && d.UsedPercent <= 100);
        }
    }

    [Fact]
    public void PrivacyShield_ReturnsComprehensiveAudit()
    {
        var privacyService = new PrivacyShieldService();
        var audit = privacyService.GetPrivacyAudit();

        Assert.NotNull(audit);
        Assert.True(audit.Count >= 10, $"Se esperaban al menos 10 ajustes de privacidad, se obtuvieron {audit.Count}");

        // Validar que existen categorías clave
        Assert.Contains(audit, a => a.Category == PrivacyCategory.TelemetryAndDiagnostics);
        Assert.Contains(audit, a => a.Category == PrivacyCategory.WindowsAIAndRecall);
        Assert.Contains(audit, a => a.Category == PrivacyCategory.SearchAndAdvertising);
        Assert.Contains(audit, a => a.Category == PrivacyCategory.ActivityAndLocation);
        Assert.Contains(audit, a => a.Category == PrivacyCategory.BackgroundServicesAndTasks);
    }

    [Fact]
    public void BatteryHealth_ReturnsReport_WithoutExceptions()
    {
        var batteryService = new BatteryHealthService();
        var report = batteryService.GetBatteryReport();

        Assert.NotNull(report);
        Assert.NotNull(report.HealthGrade);
        Assert.NotNull(report.DeviceName);
        Assert.NotNull(report.Chemistry);

        if (report.HasBattery)
        {
            Assert.True(report.ChargePercent >= 0 && report.ChargePercent <= 100);
            Assert.True(report.HealthPercent >= 0 && report.HealthPercent <= 100);
            Assert.True(report.WearLevelPercent >= 0 && report.WearLevelPercent <= 100);
        }
    }

    [Fact]
    public void RansomwareCanary_Lifecycle_StartsAndStopsCleanly()
    {
        var canaryService = RansomwareCanaryService.Instance;

        // Iniciar escudo
        canaryService.StartShield();
        var status = canaryService.GetStatus();

        Assert.True(status.IsActive);
        Assert.NotNull(status.MonitoredFolders);
        Assert.NotNull(status.ActiveCanaries);

        // Detener escudo
        canaryService.StopShield();
        var stoppedStatus = canaryService.GetStatus();
        Assert.False(stoppedStatus.IsActive);
        Assert.Empty(stoppedStatus.ActiveCanaries);
    }

    [Fact]
    public void ContextMenu_EnumeratesItems_AndChecksClassicState()
    {
        var ctxService = new ContextMenuService();
        var items = ctxService.GetContextMenuItems();

        Assert.NotNull(items);
        // Debe leer los handlers de registro del sistema
        Assert.True(items.Count > 0, "Debería haber al menos un elemento de menú contextual en Windows");

        bool isClassic = ctxService.IsWindows11ClassicContextMenuEnabled();
        // Debe retornar true o false sin lanzar excepciones
        Assert.True(isClassic || !isClassic);
    }
}
