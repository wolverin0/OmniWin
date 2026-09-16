using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

/// <summary>
/// Proporciona optimizaciones de memoria de nivel kernel y modo bajo consumo (Eco-Mode).
/// Libera el Working Set físico de la aplicación cuando pasa a segundo plano o bandeja del sistema.
/// </summary>
public static class ProcessEfficiencyService
{
    [DllImport("psapi.dll", SetLastError = true)]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    /// <summary>
    /// Vuelca las páginas de memoria física no utilizadas (buffers DirectX de WPF, caches de páginas),
    /// reduciendo el Working Set en hasta un 75% sin cerrar la aplicación ni interrumpir servicios.
    /// </summary>
    public static (bool Success, double BeforeMB, double AfterMB, double ReductionPercent) TrimWorkingSet()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            double before = proc.WorkingSet64 / (1024.0 * 1024.0);

            int res = EmptyWorkingSet(proc.Handle);
            proc.Refresh();
            double after = proc.WorkingSet64 / (1024.0 * 1024.0);
            double reduction = before > 0 ? Math.Max(0, (1.0 - (after / before)) * 100.0) : 0;

            return (res != 0, before, after, reduction);
        }
        catch
        {
            return (false, 0, 0, 0);
        }
    }
}
