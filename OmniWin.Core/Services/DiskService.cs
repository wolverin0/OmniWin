using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class BloatCategoryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool RequiresAdmin { get; set; }
    public bool IsSafeToClean { get; set; } = true;
    public bool SelectedByDefault { get; set; } = true;
}

public class DiskBloatAnalysis
{
    public List<BloatCategoryInfo> Categories { get; set; } = new();
    public long TotalBloatBytes => Categories.Sum(c => c.SizeBytes);
    public int TotalBloatFiles => Categories.Sum(c => c.FileCount);
    public List<DriveVolumeInfo> Drives { get; set; } = new();
}

public class DriveVolumeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double UsagePercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0.0;
}

public class DiskCleanOptions
{
    public List<string> CategoryIdsToClean { get; set; } = new();
    public bool EmptyRecycleBin { get; set; } = true;
}

public class DiskCleanResult
{
    public bool Success { get; set; }
    public long BytesFreed { get; set; }
    public int FilesDeleted { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class DiskService
{
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    public List<DriveVolumeInfo> GetDriveVolumes()
    {
        var list = new List<DriveVolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                list.Add(new DriveVolumeInfo
                {
                    Name = drive.Name,
                    Label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Disco Local" : drive.VolumeLabel,
                    Format = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace
                });
            }
        }
        return list;
    }

    private static (long size, int count) ScanDirectory(string dirPath, string searchPattern = "*.*")
    {
        if (!Directory.Exists(dirPath)) return (0, 0);

        long size = 0;
        int count = 0;

        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            foreach (var file in dirInfo.EnumerateFiles(searchPattern, SearchOption.AllDirectories))
            {
                try
                {
                    size += file.Length;
                    count++;
                }
                catch
                {
                    // Skip files locked or without permissions
                }
            }
        }
        catch
        {
            // Directory access denied or inaccessible
        }

        return (size, count);
    }

    public Task<DiskBloatAnalysis> AnalyzeBloatAsync()
    {
        return Task.Run(() =>
        {
            var analysis = new DiskBloatAnalysis
            {
                Drives = GetDriveVolumes()
            };

            // 1. User Temp
            string userTemp = Path.GetTempPath();
            var (uSize, uCount) = ScanDirectory(userTemp);
            analysis.Categories.Add(new BloatCategoryInfo
            {
                Id = "user_temp",
                Name = "Archivos Temporales de Usuario",
                Description = "Caché de aplicaciones, instaladores temporales e informes generados en %TEMP%",
                Path = userTemp,
                SizeBytes = uSize,
                FileCount = uCount,
                RequiresAdmin = false,
                SelectedByDefault = true
            });

            // 2. Windows Temp
            string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            var (wSize, wCount) = ScanDirectory(winTemp);
            analysis.Categories.Add(new BloatCategoryInfo
            {
                Id = "win_temp",
                Name = "Archivos Temporales de Windows",
                Description = "Temporales creados por servicios del sistema operativo en C:\\Windows\\Temp",
                Path = winTemp,
                SizeBytes = wSize,
                FileCount = wCount,
                RequiresAdmin = true,
                SelectedByDefault = true
            });

            // 3. Software Distribution Download Cache
            string sDist = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            var (sSize, sCount) = ScanDirectory(sDist);
            analysis.Categories.Add(new BloatCategoryInfo
            {
                Id = "windows_update_cache",
                Name = "Caché de Descargas de Windows Update",
                Description = "Actualizaciones ya descargadas e instaladas que pueden liberarse con seguridad",
                Path = sDist,
                SizeBytes = sSize,
                FileCount = sCount,
                RequiresAdmin = true,
                SelectedByDefault = true
            });

            // 4. Crash Dumps
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string crashDumps = Path.Combine(localAppData, "CrashDumps");
            var (cdSize, cdCount) = ScanDirectory(crashDumps);
            analysis.Categories.Add(new BloatCategoryInfo
            {
                Id = "crash_dumps",
                Name = "Volcados de Error y Crash Dumps",
                Description = "Archivos .dmp generados tras cierres inesperados de aplicaciones",
                Path = crashDumps,
                SizeBytes = cdSize,
                FileCount = cdCount,
                RequiresAdmin = false,
                SelectedByDefault = true
            });

            // 5. Windows Prefetch
            string prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            var (pfSize, pfCount) = ScanDirectory(prefetch);
            analysis.Categories.Add(new BloatCategoryInfo
            {
                Id = "windows_prefetch",
                Name = "Caché de Prefetch de Windows",
                Description = "Registros de arranque de aplicaciones en C:\\Windows\\Prefetch",
                Path = prefetch,
                SizeBytes = pfSize,
                FileCount = pfCount,
                RequiresAdmin = true,
                SelectedByDefault = false
            });

            return analysis;
        });
    }

    public Task<DiskCleanResult> CleanBloatAsync(DiskCleanOptions options)
    {
        return Task.Run(() =>
        {
            var result = new DiskCleanResult { Success = true };
            long freed = 0;
            int deleted = 0;

            var categoryPaths = new Dictionary<string, string>
            {
                ["user_temp"] = Path.GetTempPath(),
                ["win_temp"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                ["windows_update_cache"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"),
                ["crash_dumps"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
                ["windows_prefetch"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch")
            };

            foreach (var catId in options.CategoryIdsToClean)
            {
                if (!categoryPaths.TryGetValue(catId, out var targetDir) || !Directory.Exists(targetDir))
                    continue;

                try
                {
                    var dir = new DirectoryInfo(targetDir);
                    foreach (var file in dir.EnumerateFiles("*.*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            long len = file.Length;
                            file.Delete();
                            freed += len;
                            deleted++;
                        }
                        catch
                        {
                            // File in use, continue
                        }
                    }

                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        try
                        {
                            sub.Delete(true);
                        }
                        catch
                        {
                            // Directory in use
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error limpiando {catId}: {ex.Message}");
                }
            }

            if (options.EmptyRecycleBin)
            {
                try
                {
                    SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Error vaciando papelera: {ex.Message}");
                }
            }

            result.BytesFreed = freed;
            result.FilesDeleted = deleted;
            result.Summary = $"Limpieza completada. Archivos eliminados: {deleted:N0}. Espacio liberado: {freed / (1024 * 1024):N0} MB.";
            return result;
        });
    }
}
