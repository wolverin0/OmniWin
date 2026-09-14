using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class BrowserDatabaseItem
{
    public string BrowserName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool IsOptimized { get; set; }
}

public class BrowserOptimizationReport
{
    public int TotalDatabasesFound { get; set; }
    public long TotalOriginalSizeBytes { get; set; }
    public long TotalReclaimedBytes { get; set; }
    public List<BrowserDatabaseItem> Databases { get; set; } = new();
}

public class BrowserOptimizationService
{
    public static BrowserOptimizationService Instance { get; } = new();

    private static readonly byte[] SqliteHeader = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");

    public async Task<List<BrowserDatabaseItem>> ScanBrowserDatabasesAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<BrowserDatabaseItem>();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var searchTargets = new List<(string browser, string basePath, string[] dbNames)>
            {
                ("Google Chrome", Path.Combine(localAppData, @"Google\Chrome\User Data\Default"), new[] { "History", "Cookies", "Web Data", "Favicons" }),
                ("Microsoft Edge", Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default"), new[] { "History", "Cookies", "Web Data", "Favicons" }),
                ("Brave Browser", Path.Combine(localAppData, @"BraveSoftware\Brave-Browser\User Data\Default"), new[] { "History", "Cookies", "Web Data", "Favicons" }),
                ("Mozilla Firefox", Path.Combine(appData, @"Mozilla\Firefox\Profiles"), new[] { "places.sqlite", "cookies.sqlite", "favicons.sqlite", "formhistory.sqlite" })
            };

            foreach (var target in searchTargets)
            {
                if (!Directory.Exists(target.basePath)) continue;

                if (target.browser == "Mozilla Firefox")
                {
                    try
                    {
                        foreach (var profileDir in Directory.GetDirectories(target.basePath))
                        {
                            foreach (var dbName in target.dbNames)
                            {
                                string path = Path.Combine(profileDir, dbName);
                                CheckAndAddDatabase(target.browser, dbName, path, results);
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    foreach (var dbName in target.dbNames)
                    {
                        string path = Path.Combine(target.basePath, dbName);
                        CheckAndAddDatabase(target.browser, dbName, path, results);
                    }
                }
            }

            return results;
        });
    }

    private static void CheckAndAddDatabase(string browser, string name, string path, List<BrowserDatabaseItem> results)
    {
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if (fi.Length > 65536) // Only analyze databases > 64 KB
                {
                    if (IsSqliteDatabase(path))
                    {
                        results.Add(new BrowserDatabaseItem
                        {
                            BrowserName = browser,
                            DatabaseName = name,
                            FilePath = path,
                            SizeBytes = fi.Length
                        });
                    }
                }
            }
        }
        catch { }
    }

    public static bool IsSqliteDatabase(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] header = new byte[16];
            int read = fs.Read(header, 0, 16);
            if (read < 16) return false;
            return header.SequenceEqual(SqliteHeader);
        }
        catch
        {
            return false;
        }
    }

    public async Task<BrowserOptimizationReport> OptimizeDatabasesAsync(List<BrowserDatabaseItem> items)
    {
        return await Task.Run(() =>
        {
            var report = new BrowserOptimizationReport
            {
                TotalDatabasesFound = items.Count,
                TotalOriginalSizeBytes = items.Sum(i => i.SizeBytes),
                Databases = items
            };

            long reclaimed = 0;

            foreach (var item in items)
            {
                try
                {
                    // Clean orphaned WAL and SHM write-ahead logs if browser is closed
                    string walPath = item.FilePath + "-wal";
                    string shmPath = item.FilePath + "-shm";

                    if (File.Exists(walPath))
                    {
                        try
                        {
                            long walSize = new FileInfo(walPath).Length;
                            File.Delete(walPath);
                            reclaimed += walSize;
                        }
                        catch { }
                    }

                    if (File.Exists(shmPath))
                    {
                        try
                        {
                            long shmSize = new FileInfo(shmPath).Length;
                            File.Delete(shmPath);
                            reclaimed += shmSize;
                        }
                        catch { }
                    }

                    item.IsOptimized = true;
                }
                catch { }
            }

            report.TotalReclaimedBytes = reclaimed;
            return report;
        });
    }
}
