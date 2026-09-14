using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

/// <summary>
/// Service that interfaces with RivaTuner Statistics Server (RTSS)
/// via Win32 Shared Memory (RTSSSharedMemoryV2).
/// Allows OmniWin to stream telemetry (FPS, CPU, GPU, RAM, ping) directly
/// to RivaTuner's On-Screen Display (OSD).
/// </summary>
public class RtssService : IDisposable
{
    private const string RTSS_MAP_NAME = "RTSSSharedMemoryV2";
    private const uint FILE_MAP_ALL_ACCESS = 0xF001F;
    private const uint RTSS_SIGNATURE = 0x53535452; // 'RTSS' in little-endian

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private IntPtr _hMap = IntPtr.Zero;
    private IntPtr _pMap = IntPtr.Zero;

    /// <summary>
    /// Checks if RTSS (RivaTuner Statistics Server) is installed on this PC.
    /// </summary>
    public bool IsRtssInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Unwinder\RTSS");
            if (key?.GetValue("InstallPath") != null) return true;

            string standardPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "RivaTuner Statistics Server", "RTSS.exe");
            if (File.Exists(standardPath)) return true;
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Checks if the RTSS background process is currently running.
    /// </summary>
    public bool IsRtssRunning()
    {
        try
        {
            return Process.GetProcessesByName("RTSS").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Formats hardware telemetry into RTSS OSD rich markup tags.
    /// </summary>
    public string FormatOsdMarkup(double? cpuLoad, string? cpuName, double? ramUsedGb, double? ramTotalGb, double? ramPercent, double? gpuLoad, string? gpuName, long? pingMs)
    {
        var sb = new StringBuilder();
        sb.Append("<C=10B981><S=90>OmniWin OSD<S><C>\n");

        if (cpuLoad.HasValue)
        {
            string color = cpuLoad.Value > 85 ? "EF4444" : (cpuLoad.Value > 60 ? "F59E0B" : "10B981");
            sb.Append($"<C=94A3B8>CPU: <C><C={color}>{cpuLoad.Value:F1}%<C>\n");
        }

        if (ramPercent.HasValue && ramUsedGb.HasValue)
        {
            string color = ramPercent.Value > 85 ? "EF4444" : "38BDF8";
            sb.Append($"<C=94A3B8>RAM: <C><C={color}>{ramUsedGb.Value:F1} GB ({ramPercent.Value:F0}%)<C>\n");
        }

        if (!string.IsNullOrWhiteSpace(gpuName) && gpuName != "Desconocido")
        {
            string gpuLoadStr = gpuLoad.HasValue ? $"{gpuLoad.Value:F0}%" : "Activa";
            sb.Append($"<C=94A3B8>GPU: <C><C=A855F7>{gpuLoadStr}<C>\n");
        }

        if (pingMs.HasValue && pingMs.Value >= 0)
        {
            string pingColor = pingMs.Value > 80 ? "EF4444" : (pingMs.Value > 45 ? "F59E0B" : "10B981");
            sb.Append($"<C=94A3B8>PING: <C><C={pingColor}>{pingMs.Value} ms<C>\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Sends formatted text to RivaTuner's OSD buffer in shared memory.
    /// </summary>
    public bool UpdateOsd(string text, string owner = "OmniWin")
    {
        if (!IsRtssRunning())
            return false;

        try
        {
            EnsureMapped();
            if (_pMap == IntPtr.Zero)
                return false;

            // Read RTSS Header
            uint signature = (uint)Marshal.ReadInt32(_pMap, 0);
            if (signature != RTSS_SIGNATURE)
                return false;

            uint version = (uint)Marshal.ReadInt32(_pMap, 4);
            if (version < 0x00020000)
                return false; // Requires RTSS v2.0+

            uint osdArrOffset = (uint)Marshal.ReadInt32(_pMap, 12);
            uint osdArrSize = (uint)Marshal.ReadInt32(_pMap, 16);

            if (osdArrOffset == 0 || osdArrSize == 0)
                return false;

            // Structure of RTSS_SHARED_MEMORY_OSD_ENTRY:
            // char szOSD[256]
            // char szOSDOwner[256]
            int entrySize = 512;

            for (int i = 0; i < osdArrSize; i++)
            {
                IntPtr entryPtr = IntPtr.Add(_pMap, (int)(osdArrOffset + (i * entrySize)));
                IntPtr ownerPtr = IntPtr.Add(entryPtr, 256);

                string currentOwner = Marshal.PtrToStringAnsi(ownerPtr) ?? string.Empty;

                // If matching owner or empty slot
                if (string.IsNullOrEmpty(currentOwner) || currentOwner == owner)
                {
                    // Write owner
                    byte[] ownerBytes = Encoding.ASCII.GetBytes(owner + "\0");
                    Marshal.Copy(ownerBytes, 0, ownerPtr, Math.Min(ownerBytes.Length, 256));

                    // Write OSD text (up to 255 chars in primary slot)
                    byte[] textBytes = Encoding.ASCII.GetBytes(text + "\0");
                    Marshal.Copy(textBytes, 0, entryPtr, Math.Min(textBytes.Length, 256));

                    // Increment frame counter to signal RTSS that text changed
                    int frameOffset = 20; // dwOSDFrame
                    int currentFrame = Marshal.ReadInt32(_pMap, frameOffset);
                    Marshal.WriteInt32(_pMap, frameOffset, currentFrame + 1);

                    return true;
                }
            }
        }
        catch (Exception)
        {
            CloseMapping();
        }

        return false;
    }

    /// <summary>
    /// Clears any OSD text previously written by OmniWin in RTSS.
    /// </summary>
    public bool ClearOsd(string owner = "OmniWin")
    {
        return UpdateOsd(string.Empty, owner);
    }

    private void EnsureMapped()
    {
        if (_pMap != IntPtr.Zero) return;

        _hMap = OpenFileMapping(FILE_MAP_ALL_ACCESS, false, RTSS_MAP_NAME);
        if (_hMap != IntPtr.Zero)
        {
            _pMap = MapViewOfFile(_hMap, FILE_MAP_ALL_ACCESS, 0, 0, UIntPtr.Zero);
        }
    }

    private void CloseMapping()
    {
        if (_pMap != IntPtr.Zero)
        {
            UnmapViewOfFile(_pMap);
            _pMap = IntPtr.Zero;
        }

        if (_hMap != IntPtr.Zero)
        {
            CloseHandle(_hMap);
            _hMap = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        CloseMapping();
        GC.SuppressFinalize(this);
    }
}
