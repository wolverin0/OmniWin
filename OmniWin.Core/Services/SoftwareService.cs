using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class UpgradablePackage
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string InstalledVersion { get; set; } = string.Empty;
    public string AvailableVersion { get; set; } = string.Empty;
    public string Source { get; set; } = "winget";
}

public class SoftwareService
{
    public async Task<List<UpgradablePackage>> GetUpgradableAppsAsync()
    {
        var list = new List<UpgradablePackage>();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget.exe",
                Arguments = "upgrade --include-unknown",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return list;

            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool tableStarted = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("---") || line.Contains("------"))
                {
                    tableStarted = true;
                    continue;
                }

                if (!tableStarted) continue;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Contains("upgrade") && line.Contains("available")) continue;

                // Match typical winget table format: Name Id Version Available Source
                // Often columns are space-delimited with 2+ spaces
                var parts = Regex.Split(line.Trim(), @"\s{2,}");
                if (parts.Length >= 4)
                {
                    list.Add(new UpgradablePackage
                    {
                        Name = parts[0],
                        Id = parts[1],
                        InstalledVersion = parts[2],
                        AvailableVersion = parts[3],
                        Source = parts.Length > 4 ? parts[4] : "winget"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SoftwareService Error]: {ex.Message}");
        }

        return list;
    }

    public async Task<string> UpgradeAllAsync(Action<string>? onOutput = null, System.Threading.CancellationToken ct = default)
    {
        return await RunWingetStreamingAsync(
            "upgrade --all --accept-package-agreements --accept-source-agreements --disable-interactivity",
            onOutput,
            ct
        );
    }

    public async Task<string> UpgradePackageAsync(string packageId, Action<string>? onOutput = null, System.Threading.CancellationToken ct = default)
    {
        return await RunWingetStreamingAsync(
            $"upgrade --id \"{packageId}\" -e --accept-package-agreements --accept-source-agreements --disable-interactivity",
            onOutput,
            ct
        );
    }

    public async Task<string> InstallPackageAsync(string packageId, Action<string>? onOutput = null, System.Threading.CancellationToken ct = default)
    {
        return await RunWingetStreamingAsync(
            $"install --id \"{packageId}\" -e --accept-package-agreements --accept-source-agreements --disable-interactivity",
            onOutput,
            ct
        );
    }

    private static async Task<string> RunWingetStreamingAsync(string arguments, Action<string>? onOutput, System.Threading.CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };

            proc.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    sb.AppendLine(e.Data);
                    onOutput?.Invoke(e.Data);
                }
            };
            proc.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    sb.AppendLine(e.Data);
                    onOutput?.Invoke(e.Data);
                }
            };

            if (!proc.Start()) return "No se pudo iniciar winget.exe";

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var reg = ct.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(true); } catch { }
            });

            await proc.WaitForExitAsync(ct);

            string result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "Operación finalizada." : result;
        }
        catch (OperationCanceledException)
        {
            return "Operación cancelada por el usuario.";
        }
        catch (Exception ex)
        {
            return $"Error ejecutando WinGet: {ex.Message}";
        }
    }
}
