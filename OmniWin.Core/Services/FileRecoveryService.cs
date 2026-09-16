using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public record RecycleBinItem(
    string Id,
    string OriginalPath,
    string FileName,
    string Extension,
    long FileSizeBytes,
    DateTime DeletedAtUtc,
    string RFilePath,
    string IFilePath,
    bool IsContentAvailable
);

public record ShadowCopyInfo(
    string ShadowCopyId,
    string OriginalVolume,
    string ShadowVolumeName,
    DateTime CreatedAtUtc,
    string Provider
);

public record CarvedFile(
    string FilePath,
    string FileType,
    long FileSizeBytes,
    long StreamOffset
);

public record CarveResult(
    int TotalFilesCarved,
    long TotalBytesCarved,
    List<CarvedFile> Files,
    TimeSpan Duration
);

/// <summary>
/// Engine for file recovery, Recycle Bin forensics, Volume Shadow Copy previous versions,
/// and deep byte carving from raw storage.
/// </summary>
public class FileRecoveryService
{
    // ==========================================
    // 1. RECYCLE BIN FORENSIC DEEP RECOVERY
    // ==========================================

    /// <summary>
    /// Scans all available drives for $Recycle.Bin directories, parsing $I metadata headers
    /// and mapping them to corresponding $R binary payloads.
    /// </summary>
    public async Task<List<RecycleBinItem>> EnumerateRecycleBinAsync()
    {
        var items = new List<RecycleBinItem>();

        await Task.Run(() =>
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)).ToArray();
            }
            catch
            {
                return;
            }

            foreach (var drive in drives)
            {
                try
                {
                    string recyclePath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                    if (!Directory.Exists(recyclePath)) continue;

                    var userDirs = Directory.GetDirectories(recyclePath);
                    foreach (var userDir in userDirs)
                    {
                        try
                        {
                            var iFiles = Directory.GetFiles(userDir, "$I*");
                            foreach (var iFile in iFiles)
                            {
                                try
                                {
                                    var item = ParseRecycleBinMetadata(iFile);
                                    if (item != null)
                                    {
                                        items.Add(item);
                                    }
                                }
                                catch
                                {
                                    // Skip locked or corrupted entries
                                }
                            }
                        }
                        catch
                        {
                            // Permission restricted on specific user SID folder
                        }
                    }
                }
                catch
                {
                    // Drive access denied
                }
            }
        });

        return items.OrderByDescending(i => i.DeletedAtUtc).ToList();
    }

    /// <summary>
    /// Parses an NTFS $I file header according to Windows 10/11 (version 2) or Vista/7/8 (version 1) specs.
    /// </summary>
    public static RecycleBinItem? ParseRecycleBinMetadata(string iFilePath)
    {
        if (!File.Exists(iFilePath)) return null;

        string fileName = Path.GetFileName(iFilePath);
        if (!fileName.StartsWith("$I", StringComparison.OrdinalIgnoreCase)) return null;

        string rFileName = "$R" + fileName.Substring(2);
        string rFilePath = Path.Combine(Path.GetDirectoryName(iFilePath)!, rFileName);
        bool contentAvailable = File.Exists(rFilePath);

        byte[] headerBytes;
        using (var fs = new FileStream(iFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (fs.Length < 28) return null; // Minimum header length
            headerBytes = new byte[fs.Length];
            int read = fs.Read(headerBytes, 0, headerBytes.Length);
            if (read < 28) return null;
        }

        long version = BitConverter.ToInt64(headerBytes, 0);
        long fileSize = BitConverter.ToInt64(headerBytes, 8);
        long fileTime = BitConverter.ToInt64(headerBytes, 16);

        DateTime deletedAtUtc;
        try
        {
            deletedAtUtc = DateTime.FromFileTimeUtc(fileTime);
        }
        catch
        {
            deletedAtUtc = DateTime.UtcNow;
        }

        string originalPath = "";

        if (version == 2)
        {
            // Windows 10 / Windows 11 format:
            // Offset 24: 4 bytes char count
            // Offset 28: UTF-16LE null-terminated path
            int charCount = BitConverter.ToInt32(headerBytes, 24);
            int pathByteLength = charCount * 2;
            int availableBytes = headerBytes.Length - 28;
            int bytesToRead = Math.Min(pathByteLength, availableBytes);

            if (bytesToRead > 0)
            {
                originalPath = Encoding.Unicode.GetString(headerBytes, 28, bytesToRead).TrimEnd('\0');
            }
        }
        else
        {
            // Windows Vista / 7 / 8 format:
            // Offset 24: fixed 260 WCHARs (520 bytes) UTF-16LE
            int bytesToRead = Math.Min(520, headerBytes.Length - 24);
            if (bytesToRead > 0)
            {
                originalPath = Encoding.Unicode.GetString(headerBytes, 24, bytesToRead).TrimEnd('\0');
            }
        }

        if (string.IsNullOrWhiteSpace(originalPath))
        {
            originalPath = "Unknown_" + fileName.Substring(2);
        }

        string originalFileName = Path.GetFileName(originalPath);
        string extension = Path.GetExtension(originalFileName);

        return new RecycleBinItem(
            fileName.Substring(2),
            originalPath,
            originalFileName,
            extension,
            fileSize,
            deletedAtUtc,
            rFilePath,
            iFilePath,
            contentAvailable
        );
    }

    /// <summary>
    /// Restores a deleted file from its $R payload to a destination path, preserving original timestamp.
    /// </summary>
    public async Task<bool> RestoreRecycleBinItemAsync(RecycleBinItem item, string targetDestination)
    {
        if (!File.Exists(item.RFilePath)) return false;

        string targetFile;
        if (Directory.Exists(targetDestination))
        {
            targetFile = Path.Combine(targetDestination, item.FileName);
        }
        else
        {
            string? dir = Path.GetDirectoryName(targetDestination);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            targetFile = targetDestination;
        }

        await Task.Run(() =>
        {
            File.Copy(item.RFilePath, targetFile, overwrite: true);
            try
            {
                File.SetLastWriteTimeUtc(targetFile, item.DeletedAtUtc);
                File.SetCreationTimeUtc(targetFile, item.DeletedAtUtc);
            }
            catch { }
        });

        return File.Exists(targetFile);
    }

    // ==========================================
    // 2. VOLUME SHADOW COPY (VSS / PREVIOUS VERSIONS)
    // ==========================================

    /// <summary>
    /// Enumerates all Volume Shadow Copies on the system via vssadmin.
    /// </summary>
    public async Task<List<ShadowCopyInfo>> GetShadowCopiesAsync()
    {
        var list = new List<ShadowCopyInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "vssadmin.exe",
                Arguments = "list shadows",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return list;

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            string currentId = "";
            string origVolume = "";
            string shadowVolume = "";
            DateTime createdAt = DateTime.MinValue;
            string provider = "Microsoft Software Shadow Copy provider 1.0";

            foreach (var rawLine in output.Split('\n'))
            {
                string line = rawLine.Trim();

                if (line.Contains("Shadow Copy ID:", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Id. de instantánea:", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentId) && !string.IsNullOrEmpty(shadowVolume))
                    {
                        list.Add(new ShadowCopyInfo(currentId, origVolume, shadowVolume, createdAt, provider));
                    }
                    var match = Regex.Match(line, @"\{[0-9a-fA-F\-]+\}");
                    currentId = match.Success ? match.Value : "";
                }
                else if (line.Contains("Original Volume:", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Volumen original:", StringComparison.OrdinalIgnoreCase))
                {
                    origVolume = line.Split(':').Last().Trim();
                }
                else if (line.Contains("Shadow Copy Volume:", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Volumen de instantánea:", StringComparison.OrdinalIgnoreCase))
                {
                    shadowVolume = line.Substring(line.IndexOf(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase) >= 0
                        ? line.IndexOf(@"\\?\GLOBALROOT", StringComparison.OrdinalIgnoreCase)
                        : 0).Trim();
                }
                else if (line.Contains("Creation Time:", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Hora de creación:", StringComparison.OrdinalIgnoreCase))
                {
                    string datePart = line.Substring(line.IndexOf(':') + 1).Trim();
                    DateTime.TryParse(datePart, out createdAt);
                }
            }

            if (!string.IsNullOrEmpty(currentId) && !string.IsNullOrEmpty(shadowVolume))
            {
                list.Add(new ShadowCopyInfo(currentId, origVolume, shadowVolume, createdAt, provider));
            }
        }
        catch { }

        return list;
    }

    // ==========================================
    // 3. DEEP BYTE CARVING (RAW STREAM RECOVERY)
    // ==========================================

    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] JpegFooter = [0xFF, 0xD9];

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] PngFooter = [0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82]; // IEND chunk

    private static readonly byte[] PdfHeader = [0x25, 0x50, 0x44, 0x46, 0x2D]; // %PDF-
    private static readonly byte[] PdfFooter = [0x25, 0x25, 0x45, 0x4F, 0x46]; // %%EOF

    private static readonly byte[] ZipHeader = [0x50, 0x4B, 0x03, 0x04]; // PK\x03\x04
    private static readonly byte[] ZipFooter = [0x50, 0x4B, 0x05, 0x06]; // End of Central Directory

    /// <summary>
    /// Deeply carves files out of any readable Stream (e.g. unallocated memory dump or raw image)
    /// based on cryptographic/magic byte headers and footers.
    /// </summary>
    public async Task<CarveResult> CarveFilesAsync(
        Stream sourceStream,
        string destinationDir,
        HashSet<string>? requestedTypes = null,
        long maxScanBytes = 50 * 1024 * 1024)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var carved = new List<CarvedFile>();
        long totalBytesCarved = 0;

        await Task.Run(() =>
        {
            const int bufferSize = 64 * 1024;
            byte[] buffer = new byte[bufferSize * 2];
            long currentStreamPos = sourceStream.Position;
            long scannedBytes = 0;

            int read = sourceStream.Read(buffer, 0, bufferSize);
            while (read > 0 && scannedBytes < maxScanBytes)
            {
                int nextRead = sourceStream.Read(buffer, read, bufferSize);
                int totalInWindow = read + nextRead;

                // Look for known headers in current window
                for (int i = 0; i < read; i++)
                {
                    long offset = currentStreamPos + i;

                    // 1. JPEG
                    if ((requestedTypes == null || requestedTypes.Contains("jpg")) &&
                        MatchesPattern(buffer, i, JpegHeader) &&
                        (buffer[i + 3] == 0xE0 || buffer[i + 3] == 0xE1 || buffer[i + 3] == 0xDB || buffer[i + 3] == 0xEE))
                    {
                        var carvedFile = CarveSingleFile(sourceStream, offset, JpegFooter, 2, 25 * 1024 * 1024, destinationDir, "jpg", carved.Count + 1);
                        if (carvedFile != null)
                        {
                            carved.Add(carvedFile);
                            totalBytesCarved += carvedFile.FileSizeBytes;
                        }
                    }

                    // 2. PNG
                    if ((requestedTypes == null || requestedTypes.Contains("png")) &&
                        MatchesPattern(buffer, i, PngHeader))
                    {
                        var carvedFile = CarveSingleFile(sourceStream, offset, PngFooter, 8, 25 * 1024 * 1024, destinationDir, "png", carved.Count + 1);
                        if (carvedFile != null)
                        {
                            carved.Add(carvedFile);
                            totalBytesCarved += carvedFile.FileSizeBytes;
                        }
                    }

                    // 3. PDF
                    if ((requestedTypes == null || requestedTypes.Contains("pdf")) &&
                        MatchesPattern(buffer, i, PdfHeader))
                    {
                        var carvedFile = CarveSingleFile(sourceStream, offset, PdfFooter, 5, 50 * 1024 * 1024, destinationDir, "pdf", carved.Count + 1);
                        if (carvedFile != null)
                        {
                            carved.Add(carvedFile);
                            totalBytesCarved += carvedFile.FileSizeBytes;
                        }
                    }

                    // 4. ZIP
                    if ((requestedTypes == null || requestedTypes.Contains("zip")) &&
                        MatchesPattern(buffer, i, ZipHeader))
                    {
                        // ZIP EOCD requires at least 22 bytes after the signature
                        var carvedFile = CarveSingleFile(sourceStream, offset, ZipFooter, 22, 100 * 1024 * 1024, destinationDir, "zip", carved.Count + 1);
                        if (carvedFile != null)
                        {
                            carved.Add(carvedFile);
                            totalBytesCarved += carvedFile.FileSizeBytes;
                        }
                    }
                }

                currentStreamPos += read;
                scannedBytes += read;

                // Shift second half of buffer to first half
                Array.Copy(buffer, read, buffer, 0, nextRead);
                read = nextRead;
            }
        });

        stopwatch.Stop();
        return new CarveResult(carved.Count, totalBytesCarved, carved, stopwatch.Elapsed);
    }

    private static CarvedFile? CarveSingleFile(
        Stream stream,
        long startOffset,
        byte[] footerSignature,
        int bytesAfterFooter,
        long maxFileLength,
        string destinationDir,
        string ext,
        int fileIndex)
    {
        long initialPos = stream.Position;
        try
        {
            stream.Seek(startOffset, SeekOrigin.Begin);
            byte[] searchBuf = new byte[32 * 1024];
            long bytesRead = 0;
            long endOffset = -1;

            int n;
            while (bytesRead < maxFileLength && (n = stream.Read(searchBuf, 0, searchBuf.Length)) > 0)
            {
                for (int j = 0; j <= n - footerSignature.Length; j++)
                {
                    if (MatchesPattern(searchBuf, j, footerSignature))
                    {
                        endOffset = startOffset + bytesRead + j + footerSignature.Length + (bytesAfterFooter - footerSignature.Length);
                        break;
                    }
                }

                if (endOffset != -1) break;
                bytesRead += n;
            }

            if (endOffset > startOffset)
            {
                long fileLen = endOffset - startOffset;
                string outPath = Path.Combine(destinationDir, $"recovered_{fileIndex:D4}_{startOffset:X8}.{ext}");

                stream.Seek(startOffset, SeekOrigin.Begin);
                using (var outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                {
                    byte[] copyBuf = new byte[64 * 1024];
                    long remaining = fileLen;
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(copyBuf.Length, remaining);
                        int r = stream.Read(copyBuf, 0, toRead);
                        if (r <= 0) break;
                        outFs.Write(copyBuf, 0, r);
                        remaining -= r;
                    }
                }

                return new CarvedFile(outPath, ext, fileLen, startOffset);
            }
        }
        catch { }
        finally
        {
            try { stream.Seek(initialPos, SeekOrigin.Begin); } catch { }
        }

        return null;
    }

    private static bool MatchesPattern(byte[] buffer, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > buffer.Length) return false;
        for (int i = 0; i < pattern.Length; i++)
        {
            if (buffer[offset + i] != pattern[i]) return false;
        }
        return true;
    }
}
