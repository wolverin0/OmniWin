using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class DiskFileItem
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string FormattedSize => FormatBytes(SizeBytes);
    public string Extension { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}

public class DiskFolderItem
{
    public string FolderName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public string FormattedSize => FormatBytes(TotalSizeBytes);
    public int FileCount { get; set; }
    public double PercentOfRoot { get; set; }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}

public class DiskCategoryBreakdown
{
    public string CategoryName { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public long TotalBytes { get; set; }
    public string FormattedSize => FormatBytes(TotalBytes);
    public int FileCount { get; set; }
    public double Percentage { get; set; }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}

public class SpecialSystemFile
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string FormattedSize => SizeBytes > 0 ? FormatBytes(SizeBytes) : "No presente";
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public bool Exists => SizeBytes > 0;

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}

public class DiskAnalysisResult
{
    public string RootPath { get; set; } = string.Empty;
    public long TotalScannedBytes { get; set; }
    public string FormattedTotalSize => FormatBytes(TotalScannedBytes);
    public int TotalFilesScanned { get; set; }
    public int TotalDirectoriesScanned { get; set; }
    public TimeSpan ScanDuration { get; set; }
    public List<DiskFolderItem> TopFolders { get; set; } = new();
    public List<DiskFileItem> TopLargestFiles { get; set; } = new();
    public List<DiskCategoryBreakdown> Categories { get; set; } = new();
    public List<SpecialSystemFile> SystemFiles { get; set; } = new();

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n2} {suffixes[counter]}";
    }
}

public class DiskSpaceAnalyzerService
{
    public async Task<DiskAnalysisResult> AnalyzeDriveAsync(
        string rootPath, 
        IProgress<(string CurrentPath, int FilesScanned, long TotalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var result = new DiskAnalysisResult { RootPath = rootPath };

            var allLargestFiles = new List<DiskFileItem>();
            var folderSizes = new Dictionary<string, (long Size, int Files)>(StringComparer.OrdinalIgnoreCase);
            var categoryBytes = new Dictionary<string, (long Size, int Files)>(StringComparer.OrdinalIgnoreCase);

            // Special system files check
            result.SystemFiles = DetectSpecialSystemFiles(rootPath);

            long totalBytes = 0;
            int totalFiles = 0;
            int totalDirs = 0;

            var rootDir = new DirectoryInfo(rootPath);
            if (!rootDir.Exists) return result;

            // Scan immediate first-level folders and recurse up to a sensible depth
            IEnumerable<DirectoryInfo> topLevelDirs = Enumerable.Empty<DirectoryInfo>();
            try
            {
                topLevelDirs = rootDir.EnumerateDirectories();
            }
            catch { }

            foreach (var subDir in topLevelDirs)
            {
                if (ct.IsCancellationRequested) break;

                // Skip junctions and system volume info directly
                if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (string.Equals(subDir.Name, "System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(subDir.Name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) continue;

                long subDirSize = 0;
                int subDirFiles = 0;

                var stack = new Stack<DirectoryInfo>();
                stack.Push(subDir);

                while (stack.Count > 0 && !ct.IsCancellationRequested)
                {
                    var current = stack.Pop();
                    totalDirs++;

                    // Enumerate files
                    try
                    {
                        foreach (var fi in current.EnumerateFiles())
                        {
                            if (ct.IsCancellationRequested) break;

                            long sz = fi.Length;
                            subDirSize += sz;
                            subDirFiles++;
                            totalBytes += sz;
                            totalFiles++;

                            string ext = fi.Extension.ToLowerInvariant();
                            string cat = ClassifyExtension(ext);

                            if (!categoryBytes.ContainsKey(cat)) categoryBytes[cat] = (0, 0);
                            categoryBytes[cat] = (categoryBytes[cat].Size + sz, categoryBytes[cat].Files + 1);

                            // Keep top 100 files
                            if (sz > 50 * 1024 * 1024) // Only track files > 50MB for performance
                            {
                                allLargestFiles.Add(new DiskFileItem
                                {
                                    FileName = fi.Name,
                                    FullPath = fi.FullName,
                                    SizeBytes = sz,
                                    Extension = ext,
                                    LastModified = fi.LastWriteTime
                                });
                            }

                            if (totalFiles % 1000 == 0)
                            {
                                progress?.Report((current.FullName, totalFiles, totalBytes));
                            }
                        }
                    }
                    catch { }

                    // Enumerate subdirectories
                    try
                    {
                        foreach (var nextDir in current.EnumerateDirectories())
                        {
                            if ((nextDir.Attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                stack.Push(nextDir);
                            }
                        }
                    }
                    catch { }
                }

                folderSizes[subDir.FullName] = (subDirSize, subDirFiles);
            }

            // Also check files directly in root (like hiberfil.sys, pagefile.sys)
            try
            {
                foreach (var rootFile in rootDir.EnumerateFiles())
                {
                    long sz = rootFile.Length;
                    totalBytes += sz;
                    totalFiles++;
                    string ext = rootFile.Extension.ToLowerInvariant();
                    string cat = ClassifyExtension(ext);
                    if (!categoryBytes.ContainsKey(cat)) categoryBytes[cat] = (0, 0);
                    categoryBytes[cat] = (categoryBytes[cat].Size + sz, categoryBytes[cat].Files + 1);

                    if (sz > 50 * 1024 * 1024)
                    {
                        allLargestFiles.Add(new DiskFileItem
                        {
                            FileName = rootFile.Name,
                            FullPath = rootFile.FullName,
                            SizeBytes = sz,
                            Extension = ext,
                            LastModified = rootFile.LastWriteTime
                        });
                    }
                }
            }
            catch { }

            sw.Stop();
            result.ScanDuration = sw.Elapsed;
            result.TotalScannedBytes = totalBytes;
            result.TotalFilesScanned = totalFiles;
            result.TotalDirectoriesScanned = totalDirs;

            // Sort Top Folders
            result.TopFolders = folderSizes
                .OrderByDescending(kvp => kvp.Value.Size)
                .Take(25)
                .Select(kvp => new DiskFolderItem
                {
                    FolderName = Path.GetFileName(kvp.Key),
                    FullPath = kvp.Key,
                    TotalSizeBytes = kvp.Value.Size,
                    FileCount = kvp.Value.Files,
                    PercentOfRoot = totalBytes > 0 ? (double)kvp.Value.Size / totalBytes * 100.0 : 0
                })
                .ToList();

            // Sort Top Files
            result.TopLargestFiles = allLargestFiles
                .OrderByDescending(f => f.SizeBytes)
                .Take(50)
                .ToList();

            // Categories
            result.Categories = categoryBytes
                .OrderByDescending(kvp => kvp.Value.Size)
                .Select(kvp => new DiskCategoryBreakdown
                {
                    CategoryName = kvp.Key,
                    Icon = GetCategoryIcon(kvp.Key),
                    TotalBytes = kvp.Value.Size,
                    FileCount = kvp.Value.Files,
                    Percentage = totalBytes > 0 ? (double)kvp.Value.Size / totalBytes * 100.0 : 0
                })
                .ToList();

            return result;
        }, ct);
    }

    private static string ClassifyExtension(string ext) => ext switch
    {
        ".iso" or ".vmdk" or ".vhdx" or ".vdi" or ".img" or ".wim" => "Máquinas Virtuales e Imágenes",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".mp3" or ".wav" or ".flac" => "Video y Multimedia",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" => "Archivos Comprimidos",
        ".exe" or ".msi" or ".msix" or ".appx" => "Instaladores y Ejecutables",
        ".sys" or ".dmp" or ".evtx" or ".log" or ".tmp" or ".temp" or ".old" => "Sistema, Dumps y Temporales",
        ".dll" or ".pdb" or ".so" or ".node" => "Librerías y Dependencias",
        ".psd" or ".ai" or ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" => "Imágenes y Diseño",
        ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".txt" or ".csv" => "Documentos y Datos",
        _ => "Otros Archivos y Datos"
    };

    private static string GetCategoryIcon(string category) => category switch
    {
        "Máquinas Virtuales e Imágenes" => "💿",
        "Video y Multimedia" => "🎬",
        "Archivos Comprimidos" => "🗜️",
        "Instaladores y Ejecutables" => "📦",
        "Sistema, Dumps y Temporales" => "⚠️",
        "Librerías y Dependencias" => "🧩",
        "Imágenes y Diseño" => "🖼️",
        "Documentos y Datos" => "📄",
        _ => "📁"
    };

    private static List<SpecialSystemFile> DetectSpecialSystemFiles(string rootPath)
    {
        var list = new List<SpecialSystemFile>();
        string driveLetter = Path.GetPathRoot(rootPath) ?? "C:\\";

        // hiberfil.sys
        string hiberPath = Path.Combine(driveLetter, "hiberfil.sys");
        long hiberSize = GetFileLengthSafe(hiberPath);
        list.Add(new SpecialSystemFile
        {
            Name = "hiberfil.sys (Archivo de Hibernación)",
            FullPath = hiberPath,
            SizeBytes = hiberSize,
            Description = "Almacena el contenido de la memoria RAM en disco para el inicio rápido o hibernación.",
            Recommendation = "Si usas un SSD NVMe rápido, puedes desactivar la hibernación ejecutando 'powercfg -h off' en OmniWin para recuperar 16-64 GB."
        });

        // pagefile.sys
        string pagePath = Path.Combine(driveLetter, "pagefile.sys");
        long pageSize = GetFileLengthSafe(pagePath);
        list.Add(new SpecialSystemFile
        {
            Name = "pagefile.sys (Memoria Virtual / Paginación)",
            FullPath = pagePath,
            SizeBytes = pageSize,
            Description = "Archivo de paginación donde Windows almacena páginas de memoria RAM no utilizadas.",
            Recommendation = "Es esencial para la estabilidad de Windows; no se recomienda eliminarlo, pero se puede fijar un tamaño personalizado."
        });

        // swapfile.sys
        string swapPath = Path.Combine(driveLetter, "swapfile.sys");
        long swapSize = GetFileLengthSafe(swapPath);
        list.Add(new SpecialSystemFile
        {
            Name = "swapfile.sys (Paginación de Apps UWP)",
            FullPath = swapPath,
            SizeBytes = swapSize,
            Description = "Paginación para aplicaciones modernas universales de Windows.",
            Recommendation = "Generalmente ocupa entre 256 MB y 512 MB."
        });

        // SoftwareDistribution\Download
        string winDown = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        long winDownSize = GetDirectorySizeSafe(winDown);
        list.Add(new SpecialSystemFile
        {
            Name = "Caché de Descargas de Windows Update",
            FullPath = winDown,
            SizeBytes = winDownSize,
            Description = "Archivos de instalación y parches acumulados tras las actualizaciones de Windows.",
            Recommendation = "Se puede purgar de forma 100% segura usando el limpiador de disco de OmniWin."
        });

        return list;
    }

    private static long GetFileLengthSafe(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
        }
        catch { }
        return 0;
    }

    private static long GetDirectorySizeSafe(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
            }
        }
        catch { }
        return 0;
    }
}
