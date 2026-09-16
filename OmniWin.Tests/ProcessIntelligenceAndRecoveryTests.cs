using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OmniWin.Core.Services;
using Xunit;

namespace OmniWin.Tests;

public class ProcessIntelligenceAndRecoveryTests
{
    private readonly ProcessIntelligenceService _intelService = new();
    private readonly FileRecoveryService _recoveryService = new();

    [Fact]
    public void ProcessCatalog_ReturnsExpectedDefinitions_ForCoreProcesses()
    {
        var svchost = _intelService.GetProcessInfo("svchost.exe");
        Assert.NotNull(svchost);
        Assert.Equal(ProcessCategory.WindowsService, svchost.Category);
        Assert.True(svchost.IsEssential);

        var csrss = _intelService.GetProcessInfo("csrss.exe");
        Assert.NotNull(csrss);
        Assert.Equal(ProcessCategory.SystemCore, csrss.Category);
        Assert.Equal(ProcessSafetyImpact.Never, csrss.SafetyImpact);

        var code = _intelService.GetProcessInfo("code.exe");
        Assert.NotNull(code);
        Assert.Equal(ProcessCategory.Development, code.Category);
        Assert.Equal(ProcessSafetyImpact.Careful, code.SafetyImpact);
    }

    [Fact]
    public void ProcessIntelligence_DetectsMasqueradingThreat_WhenCriticalProcessRunsFromWrongPath()
    {
        // svchost.exe running from Downloads/Temp is a classic malware masquerade
        string fakePath = @"C:\Users\Attacker\Downloads\svchost.exe";
        var result = _intelService.EvaluateProcessIntelligence(9999, "svchost.exe", fakePath, 15.5);

        Assert.Equal(ProcessThreatLevel.CriticalMasquerading, result.ThreatLevel);
        Assert.Contains("ALERTA CRÍTICA: Proceso suplantado", result.SecurityVerdict);
    }

    [Fact]
    public void ProcessIntelligence_AcceptsLegitimatePath_WithoutMasqueradeAlert()
    {
        string legitPath = @"C:\Windows\System32\svchost.exe";
        var result = _intelService.EvaluateProcessIntelligence(1234, "svchost.exe", legitPath, 45.0);

        Assert.NotEqual(ProcessThreatLevel.CriticalMasquerading, result.ThreatLevel);
    }

    [Fact]
    public async Task ProcessIntelligence_AuditRunningProcesses_ExecutesSuccessfully()
    {
        var audit = await _intelService.AuditRunningProcessesAsync(topMemoryCount: 5);

        Assert.NotNull(audit);
        Assert.True(audit.TotalProcesses > 0, "Debe encontrar procesos activos en la máquina.");
        Assert.True(audit.SystemCoreCount > 0, "Debe catalogar procesos del núcleo o servicios de Windows.");
        Assert.NotNull(audit.TopMemoryProcesses);
    }

    [Fact]
    public void RecycleBinForensics_ParsesWindows11_IFileMetadataHeader()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "omni_test_recycle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            string token = "ABCDEF";
            string iFileName = $"$I{token}.txt";
            string rFileName = $"$R{token}.txt";

            string iPath = Path.Combine(tempDir, iFileName);
            string rPath = Path.Combine(tempDir, rFileName);

            // Create payload in $R
            byte[] payload = Encoding.UTF8.GetBytes("Recuperación de archivo importante OmniWin");
            File.WriteAllBytes(rPath, payload);

            // Create $I header (Version 2 - Windows 10/11)
            // Offset 0 (8 bytes): Version = 2
            // Offset 8 (8 bytes): FileSize = payload.Length
            // Offset 16 (8 bytes): FileTime
            // Offset 24 (4 bytes): Char count
            // Offset 28+: UTF-16LE path null-terminated
            string originalPath = @"C:\Users\TestUser\Documents\archivo_recuperado.txt";
            var now = DateTime.UtcNow;
            long fileTime = now.ToFileTimeUtc();

            byte[] originalPathBytes = Encoding.Unicode.GetBytes(originalPath + "\0");
            int charCount = originalPath.Length + 1;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((long)2); // Version 2
                bw.Write((long)payload.Length); // Size
                bw.Write(fileTime); // FileTime
                bw.Write((int)charCount); // Char count
                bw.Write(originalPathBytes); // Path
                File.WriteAllBytes(iPath, ms.ToArray());
            }

            var item = FileRecoveryService.ParseRecycleBinMetadata(iPath);

            Assert.NotNull(item);
            Assert.Equal("archivo_recuperado.txt", item.FileName);
            Assert.Equal(".txt", item.Extension);
            Assert.Equal(payload.Length, item.FileSizeBytes);
            Assert.True(item.IsContentAvailable);
            Assert.Equal(rPath, item.RFilePath);
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
    public async Task DeepByteCarving_RecoversPngAndPdf_FromRawByteStream()
    {
        string outDir = Path.Combine(Path.GetTempPath(), "omni_test_carve_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);

        try
        {
            // Build synthetic stream with:
            // 1. 100 bytes of garbage
            // 2. A valid PNG with PNG header and IEND footer
            // 3. 200 bytes of garbage
            // 4. A valid PDF with %PDF- header and %%EOF footer
            // 5. 50 bytes of garbage

            byte[] garbage1 = new byte[100];
            new Random().NextBytes(garbage1);

            byte[] pngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            byte[] pngBody = Encoding.ASCII.GetBytes("FakePngChunkData12345678");
            byte[] pngFooter = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82]; // IEND

            byte[] garbage2 = new byte[200];
            new Random().NextBytes(garbage2);

            byte[] pdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
            byte[] pdfBody = Encoding.ASCII.GetBytes("1.7\n1 0 obj<<>>endobj\nxref\ntrailer<<>>\nstartxref\n123\n");
            byte[] pdfFooter = [0x25, 0x25, 0x45, 0x4F, 0x46]; // %%EOF

            byte[] garbage3 = new byte[50];
            new Random().NextBytes(garbage3);

            using var memoryStream = new MemoryStream();
            memoryStream.Write(garbage1);
            memoryStream.Write(pngHeader);
            memoryStream.Write(pngBody);
            memoryStream.Write(pngFooter);
            memoryStream.Write(garbage2);
            memoryStream.Write(pdfHeader);
            memoryStream.Write(pdfBody);
            memoryStream.Write(pdfFooter);
            memoryStream.Write(garbage3);

            memoryStream.Seek(0, SeekOrigin.Begin);

            var result = await _recoveryService.CarveFilesAsync(memoryStream, outDir);

            Assert.NotNull(result);
            Assert.Equal(2, result.TotalFilesCarved);
            Assert.Contains(result.Files, f => f.FileType == "png");
            Assert.Contains(result.Files, f => f.FileType == "pdf");

            // Verify carved files exist on disk and have non-zero size
            foreach (var file in result.Files)
            {
                Assert.True(File.Exists(file.FilePath));
                Assert.True(new FileInfo(file.FilePath).Length > 0);
            }
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, true);
            }
        }
    }
}
