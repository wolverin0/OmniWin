using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class DuplicateFileGroup
{
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public List<string> FilePaths { get; set; } = new();
    public long WastedBytes => FileSizeBytes * Math.Max(0, FilePaths.Count - 1);
}

public class DiskDuplicateService
{
    public static DiskDuplicateService Instance { get; } = new();

    public async Task<List<DuplicateFileGroup>> FindDuplicatesAsync(
        string rootDirectory, 
        long minSizeBytes = 1024, 
        IProgress<(int scanned, int foundGroups)>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<DuplicateFileGroup>();
            if (!Directory.Exists(rootDirectory)) return results;

            // Phase 1: Enumerate and group by file size
            var filesBySize = new Dictionary<long, List<string>>();
            int totalScanned = 0;

            try
            {
                var enumOptions = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                };

                foreach (var file in Directory.EnumerateFiles(rootDirectory, "*.*", enumOptions))
                {
                    ct.ThrowIfCancellationRequested();
                    totalScanned++;

                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length >= minSizeBytes)
                        {
                            if (!filesBySize.TryGetValue(fi.Length, out var list))
                            {
                                list = new List<string>();
                                filesBySize[fi.Length] = list;
                            }
                            list.Add(file);
                        }
                    }
                    catch { }

                    if (totalScanned % 100 == 0)
                    {
                        progress?.Report((totalScanned, results.Count));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }

            // Filter out unique sizes (size groups with only 1 file have 0 duplicates)
            var candidateGroups = filesBySize.Where(kv => kv.Value.Count > 1).ToList();

            // Phase 2: Quick partial hash (first 16 KB)
            var partialHashGroups = new Dictionary<string, List<string>>();

            foreach (var group in candidateGroups)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var path in group.Value)
                {
                    try
                    {
                        string partialHash = ComputePartialHash(path, 16384);
                        string key = $"{group.Key}_{partialHash}";

                        if (!partialHashGroups.TryGetValue(key, out var list))
                        {
                            list = new List<string>();
                            partialHashGroups[key] = list;
                        }
                        list.Add(path);
                    }
                    catch { }
                }
            }

            // Phase 3: Full SHA-256 for surviving candidate collisions
            var fullHashCandidates = partialHashGroups.Where(kv => kv.Value.Count > 1).ToList();
            var finalGroups = new Dictionary<string, DuplicateFileGroup>();

            using var sha256 = SHA256.Create();

            foreach (var kv in fullHashCandidates)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var path in kv.Value)
                {
                    try
                    {
                        string fullHash = ComputeFullHash(path, sha256);
                        var fi = new FileInfo(path);

                        if (!finalGroups.TryGetValue(fullHash, out var dupGroup))
                        {
                            dupGroup = new DuplicateFileGroup
                            {
                                FileSizeBytes = fi.Length,
                                Sha256Hash = fullHash,
                                FilePaths = new List<string>()
                            };
                            finalGroups[fullHash] = dupGroup;
                        }
                        dupGroup.FilePaths.Add(path);
                    }
                    catch { }
                }
            }

            results = finalGroups.Values.Where(g => g.FilePaths.Count > 1).OrderByDescending(g => g.WastedBytes).ToList();
            progress?.Report((totalScanned, results.Count));
            return results;
        }, ct);
    }

    private static string ComputePartialHash(string filePath, int bytesToRead)
    {
        byte[] buffer = new byte[bytesToRead];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = fs.Read(buffer, 0, bytesToRead);
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(buffer, 0, read);
        return Convert.ToHexString(hash);
    }

    private static string ComputeFullHash(string filePath, SHA256 sha256)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        byte[] hash = sha256.ComputeHash(fs);
        return Convert.ToHexString(hash);
    }
}
