using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class DismActionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public class DismService
{
    public async Task<DismActionResult> CleanComponentStoreAsync(bool resetBase = false)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new DismActionResult
            {
                Success = false,
                Summary = "Permisos de Administrador requeridos",
                Output = "Error 740: Se requieren permisos de Administrador para ejecutar DISM.\nPor favor inicia OmniWin como Administrador (clic derecho -> Ejecutar como Administrador)."
            };
        }

        string args = resetBase 
            ? "/Online /Cleanup-Image /StartComponentCleanup /ResetBase" 
            : "/Online /Cleanup-Image /StartComponentCleanup";

        return await RunDismCommandAsync(args, "Limpieza profunda de WinSxS finalizada.");
    }

    public async Task<DismActionResult> CheckSystemHealthAsync()
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new DismActionResult
            {
                Success = false,
                Summary = "Permisos de Administrador requeridos",
                Output = "Error 740: Se requieren permisos de Administrador para ejecutar DISM.\nPor favor inicia OmniWin como Administrador."
            };
        }

        return await RunDismCommandAsync("/Online /Cleanup-Image /ScanHealth", "Escaneo de integridad de la imagen completado.");
    }

    public async Task<DismActionResult> RepairSystemHealthAsync()
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new DismActionResult
            {
                Success = false,
                Summary = "Permisos de Administrador requeridos",
                Output = "Error 740: Se requieren permisos de Administrador para ejecutar DISM.\nPor favor inicia OmniWin como Administrador."
            };
        }

        return await RunDismCommandAsync("/Online /Cleanup-Image /RestoreHealth", "Reparación de la imagen de Windows completada.");
    }

    public async Task<DismActionResult> RunSfcScanAsync()
    {
        if (!SecurityHelper.IsAdministrator())
        {
            return new DismActionResult
            {
                Success = false,
                Summary = "Permisos de Administrador requeridos",
                Output = "Error 740: Se requieren permisos de Administrador para ejecutar SFC /scannow.\nPor favor inicia OmniWin como Administrador."
            };
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sfc.exe",
                Arguments = "/scannow",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return new DismActionResult { Success = false, Summary = "No se pudo iniciar sfc.exe" };

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            string output = await outTask;
            string err = await errTask;
            if (!string.IsNullOrWhiteSpace(err)) output += "\n" + err;

            return new DismActionResult
            {
                Success = proc.ExitCode == 0,
                Output = output,
                Summary = "Escaneo del comprobador de archivos de sistema (SFC) finalizado."
            };
        }
        catch (Exception ex)
        {
            return new DismActionResult { Success = false, Summary = $"Error ejecutando SFC: {ex.Message}" };
        }
    }

    private static async Task<DismActionResult> RunDismCommandAsync(string arguments, string defaultSummary)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return new DismActionResult { Success = false, Summary = "No se pudo iniciar dism.exe" };

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            string output = await outTask;
            string err = await errTask;
            if (!string.IsNullOrWhiteSpace(err)) output += "\n" + err;

            return new DismActionResult
            {
                Success = proc.ExitCode == 0,
                Output = output,
                Summary = defaultSummary
            };
        }
        catch (Exception ex)
        {
            return new DismActionResult
            {
                Success = false,
                Summary = $"Error ejecutando DISM (¿Faltan permisos de Administrador?): {ex.Message}"
            };
        }
    }
}
