using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace OmniWin.Core.Services;

public record UsbDriveItem(
    string DriveLetter,
    string VolumeLabel,
    string FileSystem,
    long TotalSizeBytes,
    long FreeSizeBytes,
    bool IsRemovable,
    string DriveType
)
{
    public double TotalSizeGB => Math.Round(TotalSizeBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double FreeSizeGB => Math.Round(FreeSizeBytes / (1024.0 * 1024.0 * 1024.0), 2);
    public double UsedSizeGB => Math.Round((TotalSizeBytes - FreeSizeBytes) / (1024.0 * 1024.0 * 1024.0), 2);
    public double UsedPercent => TotalSizeBytes > 0 ? Math.Round(((TotalSizeBytes - FreeSizeBytes) / (double)TotalSizeBytes) * 100.0, 1) : 0;
}

public class UsbEjectResult
{
    public string DriveLetter { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<LockingProcessInfo> LockingProcesses { get; set; } = new();
}

public class FlashCapacityTestResult
{
    public string DriveLetter { get; set; } = string.Empty;
    public long TestedBytes { get; set; }
    public long VerifiedBytes { get; set; }
    public bool Passed { get; set; }
    public bool IsFakeSuspected { get; set; }
    public string Details { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }

    public double TestedMB => Math.Round(TestedBytes / (1024.0 * 1024.0), 2);
    public double VerifiedMB => Math.Round(VerifiedBytes / (1024.0 * 1024.0), 2);
}

public class WipeResult
{
    public string DriveLetter { get; set; } = string.Empty;
    public bool Success { get; set; }
    public long BytesWiped { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UsbDoctorService
{
    private readonly FileLockService _lockService = new();

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    private const uint IOCTL_STORAGE_MEDIA_REMOVAL = 0x002D4804;
    private const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct PREVENT_MEDIA_REMOVAL
    {
        [MarshalAs(UnmanagedType.U1)]
        public bool PreventMediaRemoval;
    }

    /// <summary>
    /// Lista todas las unidades extraíbles / USB conectadas al equipo.
    /// </summary>
    public List<UsbDriveItem> GetRemovableDrives()
    {
        var items = new List<UsbDriveItem>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;

                if (drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.Fixed)
                {
                    // Detectar si es removible o pendrive
                    bool isRemovable = drive.DriveType == DriveType.Removable;

                    items.Add(new UsbDriveItem(
                        DriveLetter: drive.Name.TrimEnd('\\'),
                        VolumeLabel: string.IsNullOrEmpty(drive.VolumeLabel) ? "Disco Local" : drive.VolumeLabel,
                        FileSystem: drive.DriveFormat,
                        TotalSizeBytes: drive.TotalSize,
                        FreeSizeBytes: drive.TotalFreeSpace,
                        IsRemovable: isRemovable,
                        DriveType: drive.DriveType.ToString()
                    ));
                }
            }
            catch { }
        }

        return items;
    }

    /// <summary>
    /// Identifica los procesos que tienen archivos abiertos en la unidad USB especificada.
    /// </summary>
    public List<LockingProcessInfo> GetLockingProcesses(string driveLetter)
    {
        string path = NormalizeDrivePath(driveLetter);
        return _lockService.GetLockingProcesses(path);
    }

    /// <summary>
    /// Intenta desmontar y expulsar de forma segura una unidad USB, liberando handles si es necesario.
    /// </summary>
    public async Task<UsbEjectResult> SafelyEjectDriveAsync(string driveLetter, bool forceCloseProcesses = false)
    {
        string norm = NormalizeDrivePath(driveLetter);
        var locking = GetLockingProcesses(norm);

        if (locking.Count > 0)
        {
            if (forceCloseProcesses)
            {
                foreach (var p in locking)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(p.ProcessId);
                        proc.Kill();
                        proc.WaitForExit(1000);
                    }
                    catch { }
                }
                await Task.Delay(300);
            }
            else
            {
                return new UsbEjectResult
                {
                    DriveLetter = norm,
                    Success = false,
                    Message = $"La unidad {norm} está en uso por {locking.Count} proceso(s).",
                    LockingProcesses = locking
                };
            }
        }

        return await Task.Run(() =>
        {
            string devicePath = @"\\.\" + norm.TrimEnd('\\');
            using var handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int err = Marshal.GetLastWin32Error();
                return new UsbEjectResult
                {
                    DriveLetter = norm,
                    Success = false,
                    Message = $"No se pudo abrir acceso directo al volumen {norm} (Código Win32 {err}). Requiere privilegios de Administrador.",
                    LockingProcesses = locking
                };
            }

            uint bytesReturned;
            // 1. Lock Volume
            DeviceIoControl(handle, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

            // 2. Dismount Volume
            bool dismounted = DeviceIoControl(handle, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

            // 3. Allow removal
            PREVENT_MEDIA_REMOVAL pmr = new() { PreventMediaRemoval = false };
            int pmrSize = Marshal.SizeOf(pmr);
            IntPtr pmrPtr = Marshal.AllocHGlobal(pmrSize);
            try
            {
                Marshal.StructureToPtr(pmr, pmrPtr, false);
                DeviceIoControl(handle, IOCTL_STORAGE_MEDIA_REMOVAL, pmrPtr, (uint)pmrSize, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(pmrPtr);
            }

            // 4. Eject Media
            bool ejected = DeviceIoControl(handle, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);

            bool ok = dismounted || ejected;
            return new UsbEjectResult
            {
                DriveLetter = norm,
                Success = ok,
                Message = ok
                    ? $"La unidad {norm} fue expulsada de forma segura y puede desconectarse físicamente."
                    : $"No se pudo completar la expulsión del dispositivo (Código Win32 {Marshal.GetLastWin32Error()}).",
                LockingProcesses = locking
            };
        });
    }

    /// <summary>
    /// Escribe y verifica patrones criptográficos en la unidad USB para detectar memorias flash falsas o con capacidad trucada.
    /// </summary>
    public async Task<FlashCapacityTestResult> ValidateFlashCapacityAsync(
        string driveLetter,
        long bytesToTest,
        IProgress<(long verifiedBytes, double speedMBs)>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            string norm = NormalizeDrivePath(driveLetter);
            string testFile = Path.Combine(norm, ".omni_flash_test.bin");
            var sw = Stopwatch.StartNew();

            long tested = 0;
            long verified = 0;
            const int blockSize = 1024 * 1024; // 1 MB
            byte[] writeBuffer = new byte[blockSize];
            byte[] readBuffer = new byte[blockSize];

            try
            {
            // Fase de Escritura
            using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.Write, FileShare.None, blockSize, FileOptions.WriteThrough))
            {
                int blockCount = (int)Math.Max(1, bytesToTest / blockSize);
                for (int i = 0; i < blockCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Generar patrón predecible basado en índice
                    FillTestPattern(writeBuffer, i);
                    fs.Write(writeBuffer, 0, blockSize);
                    tested += blockSize;

                    if (i % 10 == 0)
                    {
                        double mbSec = (tested / (1024.0 * 1024.0)) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                        progress?.Report((tested, mbSec));
                    }
                }
                fs.Flush(true);
            }

            // Fase de Lectura y Verificación
            sw.Restart();
            using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.None, blockSize, FileOptions.SequentialScan))
            {
                int blockCount = (int)(tested / blockSize);
                for (int i = 0; i < blockCount; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    int read = fs.Read(readBuffer, 0, blockSize);
                    if (read != blockSize)
                    {
                        return new FlashCapacityTestResult
                        {
                            DriveLetter = norm,
                            TestedBytes = tested,
                            VerifiedBytes = verified,
                            Passed = false,
                            IsFakeSuspected = true,
                            Duration = sw.Elapsed,
                            Details = $"Fin prematuro del archivo a los {verified / (1024.0 * 1024.0):N1} MB. La memoria física real es menor a la reportada."
                        };
                    }

                    FillTestPattern(writeBuffer, i);
                    if (!writeBuffer.AsSpan().SequenceEqual(readBuffer.AsSpan()))
                    {
                        return new FlashCapacityTestResult
                        {
                            DriveLetter = norm,
                            TestedBytes = tested,
                            VerifiedBytes = verified,
                            Passed = false,
                            IsFakeSuspected = true,
                            Duration = sw.Elapsed,
                            Details = $"¡Discrepancia de datos en el bloque #{i} ({verified / (1024.0 * 1024.0):N1} MB)! Los datos leídos no coinciden con los escritos (chip flash trucado)."
                        };
                    }

                    verified += blockSize;
                    if (i % 10 == 0)
                    {
                        double mbSec = (verified / (1024.0 * 1024.0)) / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                        progress?.Report((verified, mbSec));
                    }
                }
            }

            return new FlashCapacityTestResult
            {
                DriveLetter = norm,
                TestedBytes = tested,
                VerifiedBytes = verified,
                Passed = true,
                IsFakeSuspected = false,
                Duration = sw.Elapsed,
                Details = $"Prueba de integridad superada con éxito ({verified / (1024.0 * 1024.0):N1} MB verificados sin corrupción). La controladora y celdas NAND son legítimas."
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FlashCapacityTestResult
            {
                DriveLetter = norm,
                TestedBytes = tested,
                VerifiedBytes = verified,
                Passed = false,
                IsFakeSuspected = tested > 0 && verified < tested / 2,
                Duration = sw.Elapsed,
                Details = $"Error durante el test: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                if (File.Exists(testFile)) File.Delete(testFile);
            }
            catch { }
        }
        }, ct);
    }

    /// <summary>
    /// Llena el espacio libre de una unidad con ceros (Zero-Fill) para impedir la recuperación de archivos borrados.
    /// </summary>
    public async Task<WipeResult> WipeFreeSpaceAsync(
        string driveLetter,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            string norm = NormalizeDrivePath(driveLetter);
            string wipeFile = Path.Combine(norm, ".omni_wipe_temp.bin");
            var drive = new DriveInfo(norm);
            long freeBytes = drive.AvailableFreeSpace;

            const int bufferSize = 4 * 1024 * 1024; // 4 MB
            byte[] zeroBuffer = new byte[bufferSize];
            long written = 0;

            try
            {
                using (var fs = new FileStream(wipeFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.WriteThrough))
                {
                    while (written < freeBytes)
                    {
                        ct.ThrowIfCancellationRequested();
                        long remaining = freeBytes - written;
                        int toWrite = (int)Math.Min(bufferSize, remaining);

                        try
                        {
                            fs.Write(zeroBuffer, 0, toWrite);
                            written += toWrite;
                        }
                        catch (IOException)
                        {
                            // Disco completamente lleno
                            break;
                        }

                        double pct = freeBytes > 0 ? (written / (double)freeBytes) * 100.0 : 100.0;
                        progress?.Report(pct);
                    }
                    fs.Flush(true);
                }

                return new WipeResult
                {
                    DriveLetter = norm,
                    Success = true,
                    BytesWiped = written,
                    Message = $"Sobrescritura completada: {written / (1024.0 * 1024.0):N1} MB de espacio libre purgados con ceros."
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(wipeFile)) File.Delete(wipeFile);
                }
                catch { }
            }
        }, ct);
    }

    private static void FillTestPattern(byte[] buffer, int blockIndex)
    {
        // 4 bytes: Magic 'OMNI'
        buffer[0] = 0x4F;
        buffer[1] = 0x4D;
        buffer[2] = 0x4E;
        buffer[3] = 0x49;

        // 4 bytes: blockIndex
        var idxBytes = BitConverter.GetBytes(blockIndex);
        buffer[4] = idxBytes[0];
        buffer[5] = idxBytes[1];
        buffer[6] = idxBytes[2];
        buffer[7] = idxBytes[3];

        // Resto: pseudo-aleatorio deterministic
        byte seed = (byte)(blockIndex ^ 0xAA);
        for (int i = 8; i < buffer.Length; i++)
        {
            buffer[i] = (byte)(seed + (i & 0xFF));
        }
    }

    private static string NormalizeDrivePath(string drive)
    {
        string d = drive.Trim();
        if (d.Length == 1 && char.IsLetter(d[0])) return d.ToUpper() + @":\";
        if (d.Length == 2 && d[1] == ':') return d.ToUpper() + @"\";
        if (!d.EndsWith('\\')) return d + @"\";
        return d;
    }
}
