using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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

    #region NTFS Hardlinks (Zero-Copy Deduplication) & Deletion

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>
    /// Reemplaza un archivo duplicado por un Enlace Duro (Hardlink) NTFS apuntando al archivo primario.
    /// Ambos archivos siguen existiendo para el usuario y aplicaciones, pero comparten exactamente los mismos bloques físicos en disco.
    /// Requiere que ambos archivos residan en el mismo volumen/partición NTFS.
    /// </summary>
    public DeduplicationResult ReplaceWithHardLink(string duplicatePath, string sourcePath)
    {
        if (!File.Exists(duplicatePath))
            return new DeduplicationResult { Success = false, Message = $"El archivo duplicado '{duplicatePath}' no existe." };

        if (!File.Exists(sourcePath))
            return new DeduplicationResult { Success = false, Message = $"El archivo primario '{sourcePath}' no existe." };

        string rootDup = Path.GetPathRoot(Path.GetFullPath(duplicatePath)) ?? string.Empty;
        string rootSrc = Path.GetPathRoot(Path.GetFullPath(sourcePath)) ?? string.Empty;

        if (!rootDup.Equals(rootSrc, StringComparison.OrdinalIgnoreCase))
        {
            return new DeduplicationResult
            {
                Success = false,
                Message = $"No se puede crear un Hardlink entre distintos volúmenes ({rootDup} vs {rootSrc}). NTFS exige el mismo volumen."
            };
        }

        try
        {
            var fi = new FileInfo(duplicatePath);
            long savedBytes = fi.Length;
            string tempBackup = duplicatePath + ".omni_tmp";

            // Renombrar temporalmente el duplicado
            File.Move(duplicatePath, tempBackup);

            bool linkCreated = CreateHardLink(duplicatePath, sourcePath, IntPtr.Zero);
            if (linkCreated)
            {
                // Hardlink creado exitosamente, eliminar el backup temporal
                File.Delete(tempBackup);
                return new DeduplicationResult
                {
                    Success = true,
                    BytesSaved = savedBytes,
                    FilesProcessed = 1,
                    Message = $"Zero-Copy Hardlink creado con éxito. Recuperados {savedBytes / (1024.0 * 1024.0):N2} MB sin perder el archivo."
                };
            }
            else
            {
                // Falló, restaurar el archivo original
                int err = Marshal.GetLastWin32Error();
                File.Move(tempBackup, duplicatePath);
                return new DeduplicationResult
                {
                    Success = false,
                    Message = $"Error de Windows al crear Hardlink (Código Win32 {err}). Archivo original preservado."
                };
            }
        }
        catch (Exception ex)
        {
            return new DeduplicationResult { Success = false, Message = $"Excepción al procesar Hardlink: {ex.Message}" };
        }
    }

    /// <summary>
    /// Deduplica automáticamente un grupo reemplazando todos los duplicados por Hardlinks al primario.
    /// </summary>
    public DeduplicationResult DeduplicateGroupWithHardLinks(DuplicateFileGroup group, string primaryPath)
    {
        long totalSaved = 0;
        int count = 0;
        var errors = new List<string>();

        foreach (var file in group.FilePaths)
        {
            if (file.Equals(primaryPath, StringComparison.OrdinalIgnoreCase)) continue;

            var res = ReplaceWithHardLink(file, primaryPath);
            if (res.Success)
            {
                totalSaved += res.BytesSaved;
                count++;
            }
            else
            {
                errors.Add(res.Message);
            }
        }

        return new DeduplicationResult
        {
            Success = count > 0 || errors.Count == 0,
            FilesProcessed = count,
            BytesSaved = totalSaved,
            Message = $"Procesados {count} archivo(s). Espacio físico liberado: {totalSaved / (1024.0 * 1024.0):N2} MB." +
                      (errors.Count > 0 ? $" ({errors.Count} errores detectados)" : "")
        };
    }

    /// <summary>
    /// Elimina un archivo duplicado (permanentemente o enviándolo a la Papelera de Reciclaje).
    /// </summary>
    public bool DeleteDuplicateFile(string filePath, bool sendToRecycleBin = true)
    {
        if (!File.Exists(filePath)) return false;

        try
        {
            if (sendToRecycleBin)
            {
                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = filePath + '\0' + '\0',
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
                };
                int ret = SHFileOperation(ref shf);
                return ret == 0;
            }
            else
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    #endregion
}

public class DeduplicationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public long BytesSaved { get; set; }
    public int FilesProcessed { get; set; }
}
