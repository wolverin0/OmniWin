using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class DriverPackageInfo
{
    public string PublishedName { get; set; } = string.Empty; // e.g. oem12.inf
    public string OriginalName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string DateAndVersion { get; set; } = string.Empty;
}

public class DriverService
{
    public async Task<List<DriverPackageInfo>> GetOemDriversAsync()
    {
        var list = new List<DriverPackageInfo>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/enum-drivers",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return list;

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var blocks = output.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var block in blocks)
            {
                if (!block.Contains(".inf", StringComparison.OrdinalIgnoreCase)) continue;

                var item = new DriverPackageInfo();
                foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length < 2) continue;

                    string key = parts[0].Trim().ToLowerInvariant();
                    string val = parts[1].Trim();

                    if (key.Contains("published name") || key.Contains("nombre publicado"))
                        item.PublishedName = val;
                    else if (key.Contains("original name") || key.Contains("nombre original"))
                        item.OriginalName = val;
                    else if (key.Contains("provider name") || key.Contains("nombre de proveedor"))
                        item.Provider = val;
                    else if (key.Contains("class name") || key.Contains("nombre de clase"))
                        item.ClassName = val;
                    else if (key.Contains("driver version") || key.Contains("versión del controlador") || key.Contains("date"))
                        item.DateAndVersion = val;
                }

                if (!string.IsNullOrEmpty(item.PublishedName))
                {
                    list.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DriverService Error]: {ex.Message}");
        }

        return list;
    }

    public async Task<string> DeleteDriverAsync(string publishedName, bool force = false)
    {
        try
        {
            string args = force ? $"/delete-driver {publishedName} /force" : $"/delete-driver {publishedName}";
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "No se pudo iniciar pnputil.exe";

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            return output.Trim();
        }
        catch (Exception ex)
        {
            return $"Error eliminando driver {publishedName}: {ex.Message}";
        }
    }

    public async Task<(bool Success, string Message, int ExportedCount)> ExportDriversAsync(string destinationFolder, IProgress<string>? progress = null)
    {
        try
        {
            if (!System.IO.Directory.Exists(destinationFolder))
            {
                System.IO.Directory.CreateDirectory(destinationFolder);
            }

            progress?.Report($"Iniciando exportación de controladores a: {destinationFolder}...");

            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/export-driver * \"{destinationFolder}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "No se pudo iniciar pnputil.exe", 0);

            while (!proc.StandardOutput.EndOfStream)
            {
                string? line = await proc.StandardOutput.ReadLineAsync();
                if (line != null)
                {
                    progress?.Report(line);
                }
            }

            await proc.WaitForExitAsync();

            int infCount = System.IO.Directory.GetFiles(destinationFolder, "*.inf", System.IO.SearchOption.AllDirectories).Length;
            progress?.Report($"Exportación completada. Se exportaron {infCount} paquetes de controladores.");
            return (true, $"Se exportaron {infCount} paquetes de controladores con éxito.", infCount);
        }
        catch (Exception ex)
        {
            string err = $"Error al exportar controladores: {ex.Message}";
            progress?.Report(err);
            return (false, err, 0);
        }
    }

    public async Task<(bool Success, string Message)> RestoreDriversAsync(string sourceFolder, IProgress<string>? progress = null)
    {
        try
        {
            if (!System.IO.Directory.Exists(sourceFolder))
            {
                return (false, $"La carpeta origen no existe: {sourceFolder}");
            }

            int infFiles = System.IO.Directory.GetFiles(sourceFolder, "*.inf", System.IO.SearchOption.AllDirectories).Length;
            if (infFiles == 0)
            {
                return (false, "No se encontraron archivos .inf de controladores en la carpeta seleccionada.");
            }

            progress?.Report($"Iniciando instalación/restauración de controladores desde: {sourceFolder} ({infFiles} paquetes detectados)...");

            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/add-driver \"{System.IO.Path.Combine(sourceFolder, "*.inf")}\" /subdirs /install",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "No se pudo iniciar pnputil.exe");

            while (!proc.StandardOutput.EndOfStream)
            {
                string? line = await proc.StandardOutput.ReadLineAsync();
                if (line != null)
                {
                    progress?.Report(line);
                }
            }

            await proc.WaitForExitAsync();
            progress?.Report("Proceso de restauración finalizado.");
            return (true, $"Restauración de controladores finalizada con código de salida {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            string err = $"Error al restaurar controladores: {ex.Message}";
            progress?.Report(err);
            return (false, err);
        }
    }
}
