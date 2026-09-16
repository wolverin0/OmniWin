using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class MigrationPlan
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public long TotalSizeBytes { get; set; }
    public int TotalFiles { get; set; }
    public string SourceDrive { get; set; } = string.Empty;
    public string TargetDrive { get; set; } = string.Empty;
    public long TargetFreeSpaceBytes { get; set; }
    public bool HasEnoughSpace { get; set; }
    public bool IsCurrentlyJunction { get; set; }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public long BytesMigrated { get; set; }
    public int FilesMigrated { get; set; }
    public long DurationMs { get; set; }
}

public class MigrationRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long TotalBytes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class AppMigrationService
{
    public static AppMigrationService Instance { get; } = new();

    private readonly string _historyPath;

    public AppMigrationService()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OmniWin", "migrations");
        Directory.CreateDirectory(appData);
        _historyPath = Path.Combine(appData, "migration_history.json");
    }

    public bool IsJunction(string path)
    {
        if (!Directory.Exists(path)) return false;
        try
        {
            var di = new DirectoryInfo(path);
            return (di.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    public string GetJunctionTarget(string path)
    {
        if (!IsJunction(path)) return string.Empty;
        try
        {
            var di = new DirectoryInfo(path);
            return di.LinkTarget ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public MigrationPlan AnalyzeFolder(string sourcePath, string targetRoot)
    {
        var plan = new MigrationPlan { SourcePath = sourcePath };
        if (!Directory.Exists(sourcePath)) return plan;

        plan.IsCurrentlyJunction = IsJunction(sourcePath);
        plan.SourceDrive = Path.GetPathRoot(sourcePath) ?? "C:\\";
        string targetDrive = Path.GetPathRoot(targetRoot) ?? "D:\\";
        plan.TargetDrive = targetDrive;

        string folderName = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
        plan.TargetPath = Path.Combine(targetRoot, folderName);

        try
        {
            var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            plan.TotalFiles = files.Length;
            plan.TotalSizeBytes = files.Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            plan.TotalFiles = 0;
            plan.TotalSizeBytes = 0;
        }

        try
        {
            var di = new DriveInfo(targetDrive);
            plan.TargetFreeSpaceBytes = di.AvailableFreeSpace;
            // 500MB safety threshold beyond total size
            plan.HasEnoughSpace = plan.TargetFreeSpaceBytes > (plan.TotalSizeBytes + (500L * 1024 * 1024));
        }
        catch
        {
            plan.HasEnoughSpace = false;
        }

        return plan;
    }

    public async Task<MigrationResult> MigrateFolderAsync(
        string sourcePath,
        string targetRoot,
        IProgress<(long copied, long total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var plan = AnalyzeFolder(sourcePath, targetRoot);

        if (!Directory.Exists(sourcePath))
            return new MigrationResult { Success = false, Message = "Carpeta de origen no existe." };

        if (plan.IsCurrentlyJunction)
            return new MigrationResult { Success = false, Message = "La carpeta de origen ya es un enlace Junction." };

        if (!plan.HasEnoughSpace)
            return new MigrationResult { Success = false, Message = "Espacio insuficiente en el disco de destino." };

        string targetDir = plan.TargetPath;
        if (Directory.Exists(targetDir))
            return new MigrationResult { Success = false, Message = "El directorio de destino ya existe. Elija otra ruta." };

        long copiedBytes = 0;
        int copiedFiles = 0;

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(targetDir);
                var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);

                foreach (var file in allFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(sourcePath, file);
                    string dest = Path.Combine(targetDir, relative);
                    string? destDir = Path.GetDirectoryName(dest);
                    if (destDir != null && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(file, dest, overwrite: true);
                    var fi = new FileInfo(file);
                    copiedBytes += fi.Length;
                    copiedFiles++;

                    progress?.Report((copiedBytes, plan.TotalSizeBytes, Path.GetFileName(file)));
                }
            }, ct);

            // Rename source to temp backup, create junction, then delete backup
            string tempBackup = sourcePath.TrimEnd('\\', '/') + "_migrating_tmp";
            if (Directory.Exists(tempBackup)) Directory.Delete(tempBackup, recursive: true);

            Directory.Move(sourcePath, tempBackup);

            bool junctionCreated = CreateJunction(sourcePath, targetDir);
            if (!junctionCreated)
            {
                // Restore backup
                if (Directory.Exists(sourcePath)) Directory.Delete(sourcePath, recursive: false);
                Directory.Move(tempBackup, sourcePath);
                try { Directory.Delete(targetDir, recursive: true); } catch { }
                return new MigrationResult { Success = false, Message = "Error al crear enlace Junction NTFS." };
            }

            // Cleanup backup folder
            try
            {
                Directory.Delete(tempBackup, recursive: true);
            }
            catch (Exception ex)
            {
                // Non-fatal, data is already at target and linked
                Debug.WriteLine($"Error removing temp backup: {ex.Message}");
            }

            RecordMigration(sourcePath, targetDir, copiedBytes);
            sw.Stop();

            return new MigrationResult
            {
                Success = true,
                Message = "Migración completada exitosamente con Junction transparente.",
                SourcePath = sourcePath,
                TargetPath = targetDir,
                BytesMigrated = copiedBytes,
                FilesMigrated = copiedFiles,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new MigrationResult
            {
                Success = false,
                Message = $"Error durante la migración: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    public async Task<MigrationResult> RollbackMigrationAsync(
        string sourcePath,
        IProgress<(long copied, long total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (!IsJunction(sourcePath))
            return new MigrationResult { Success = false, Message = "La ruta indicada no es un enlace Junction." };

        string target = GetJunctionTarget(sourcePath);
        if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
        {
            var rec = GetActiveMigrations().FirstOrDefault(m => string.Equals(m.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
            if (rec != null) target = rec.TargetPath;
        }

        if (!Directory.Exists(target))
            return new MigrationResult { Success = false, Message = "No se encontró el directorio de destino original para revertir." };

        try
        {
            // Remove junction
            Directory.Delete(sourcePath, recursive: false);

            long totalBytes = 0;
            long copiedBytes = 0;
            int fileCount = 0;

            await Task.Run(() =>
            {
                Directory.CreateDirectory(sourcePath);
                var allFiles = Directory.GetFiles(target, "*", SearchOption.AllDirectories);
                totalBytes = allFiles.Sum(f => new FileInfo(f).Length);

                foreach (var file in allFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(target, file);
                    string dest = Path.Combine(sourcePath, relative);
                    string? destDir = Path.GetDirectoryName(dest);
                    if (destDir != null && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    File.Copy(file, dest, overwrite: true);
                    copiedBytes += new FileInfo(file).Length;
                    fileCount++;
                    progress?.Report((copiedBytes, totalBytes, Path.GetFileName(file)));
                }

                Directory.Delete(target, recursive: true);
            }, ct);

            RemoveMigrationRecord(sourcePath);
            sw.Stop();

            return new MigrationResult
            {
                Success = true,
                Message = "Reversión completada: archivos restaurados a la ubicación original.",
                SourcePath = sourcePath,
                TargetPath = target,
                BytesMigrated = copiedBytes,
                FilesMigrated = fileCount,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new MigrationResult
            {
                Success = false,
                Message = $"Error durante la reversión: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    public List<string> FindCandidateFolders(string rootDriveLetter)
    {
        var candidates = new List<string>();
        string root = rootDriveLetter.TrimEnd('\\') + "\\";
        string[] searchPaths = [
            Path.Combine(root, "Program Files"),
            Path.Combine(root, "Program Files (x86)"),
            Path.Combine(root, "Games"),
            Path.Combine(root, "SteamLibrary", "steamapps", "common"),
            Path.Combine(root, "Epic Games")
        ];

        foreach (var p in searchPaths)
        {
            if (Directory.Exists(p))
            {
                try
                {
                    foreach (var sub in Directory.GetDirectories(p))
                    {
                        candidates.Add(sub);
                    }
                }
                catch { }
            }
        }
        return candidates;
    }

    private bool CreateJunction(string junctionPath, string targetPath)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch
        {
            return false;
        }
    }

    public List<MigrationRecord> GetActiveMigrations()
    {
        try
        {
            if (!File.Exists(_historyPath)) return new List<MigrationRecord>();
            string json = File.ReadAllText(_historyPath);
            return JsonSerializer.Deserialize<List<MigrationRecord>>(json) ?? new List<MigrationRecord>();
        }
        catch
        {
            return new List<MigrationRecord>();
        }
    }

    private void RecordMigration(string source, string target, long bytes)
    {
        try
        {
            var records = GetActiveMigrations();
            records.RemoveAll(r => string.Equals(r.SourcePath, source, StringComparison.OrdinalIgnoreCase));
            records.Add(new MigrationRecord { SourcePath = source, TargetPath = target, TotalBytes = bytes });
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void RemoveMigrationRecord(string source)
    {
        try
        {
            var records = GetActiveMigrations();
            records.RemoveAll(r => string.Equals(r.SourcePath, source, StringComparison.OrdinalIgnoreCase));
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
