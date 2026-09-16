using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace OmniWin.Core.Services;

public class QosPolicyInfo
{
    public string Name { get; set; } = string.Empty;
    public string AppPathName { get; set; } = string.Empty;
    public long ThrottleRateBps { get; set; }
    public double ThrottleRateKbps => ThrottleRateBps / (1024.0 * 8);
    public double ThrottleRateMbps => ThrottleRateBps / (1024.0 * 1024.0 * 8);
    public bool IsOmniWinManaged => Name.StartsWith("OmniWin_QoS_", StringComparison.OrdinalIgnoreCase);
}

public class QosOperationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public long LimitKbps { get; set; }
}

public class NetworkQosService
{
    public static NetworkQosService Instance { get; } = new();

    public async Task<List<QosPolicyInfo>> ListPoliciesAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<QosPolicyInfo>();
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetQosPolicy | Select-Object Name, AppPathNameMatchCondition, ThrottleRateActionBitsPerSecond | ConvertTo-Json -Compress\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return list;
                string output = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit(6000);

                if (string.IsNullOrWhiteSpace(output)) return list;

                if (output.StartsWith("["))
                {
                    using var doc = JsonDocument.Parse(output);
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        list.Add(ParseQosElement(elem));
                    }
                }
                else if (output.StartsWith("{"))
                {
                    using var doc = JsonDocument.Parse(output);
                    list.Add(ParseQosElement(doc.RootElement));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[QOS_LIST_ERROR] {ex.Message}");
            }
            return list;
        });
    }

    private static QosPolicyInfo ParseQosElement(JsonElement elem)
    {
        var info = new QosPolicyInfo();
        if (elem.TryGetProperty("Name", out var nameProp))
            info.Name = nameProp.GetString() ?? "";

        if (elem.TryGetProperty("AppPathNameMatchCondition", out var appProp))
            info.AppPathName = appProp.GetString() ?? "";

        if (elem.TryGetProperty("ThrottleRateActionBitsPerSecond", out var bpsProp))
            info.ThrottleRateBps = bpsProp.GetInt64();

        return info;
    }

    public async Task<QosOperationResult> SetProcessLimitAsync(string appName, long maxKilobytesPerSecond)
    {
        return await Task.Run(async () =>
        {
            string cleanApp = Path.GetFileName(appName).Trim();
            if (string.IsNullOrWhiteSpace(cleanApp))
                return new QosOperationResult { Success = false, Message = "Nombre de ejecutable no válido." };

            if (!cleanApp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanApp += ".exe";

            string policyName = $"OmniWin_QoS_{cleanApp.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)}";
            long bitsPerSec = Math.Max(64 * 1024 * 8, maxKilobytesPerSecond * 1024 * 8);

            // Remove existing policy first to ensure clean creation
            await RemovePolicyAsync(policyName);

            string psScript = $"New-NetQosPolicy -Name '{policyName}' -AppPathNameMatchCondition '{cleanApp}' -ThrottleRateActionBitsPerSecond {bitsPerSec} -PolicyStore ActiveStore";

            try
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return new QosOperationResult { Success = false, Message = "No se pudo iniciar el proceso de QoS." };

                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit(6000);

                if (proc.ExitCode == 0)
                {
                    return new QosOperationResult
                    {
                        Success = true,
                        Message = $"Límite de {maxKilobytesPerSecond} KB/s aplicado con éxito a {cleanApp}.",
                        PolicyName = policyName,
                        AppName = cleanApp,
                        LimitKbps = maxKilobytesPerSecond
                    };
                }

                return new QosOperationResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(err) ? "Error al ejecutar comando de política QoS." : err.Trim()
                };
            }
            catch (Exception ex)
            {
                return new QosOperationResult { Success = false, Message = ex.Message };
            }
        });
    }

    public async Task<QosOperationResult> RemoveProcessLimitAsync(string appName)
    {
        string cleanApp = Path.GetFileName(appName).Trim();
        if (!cleanApp.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            cleanApp += ".exe";

        string policyName = $"OmniWin_QoS_{cleanApp.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)}";
        return await RemovePolicyAsync(policyName);
    }

    public async Task<QosOperationResult> RemovePolicyAsync(string policyName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"Remove-NetQosPolicy -Name '{policyName}' -Confirm:$false -ErrorAction SilentlyContinue\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);

                return new QosOperationResult
                {
                    Success = true,
                    Message = $"Política {policyName} eliminada o no existente.",
                    PolicyName = policyName
                };
            }
            catch (Exception ex)
            {
                return new QosOperationResult { Success = false, Message = ex.Message };
            }
        });
    }

    public async Task<int> RemoveAllOmniWinLimitsAsync()
    {
        var policies = await ListPoliciesAsync();
        int count = 0;
        foreach (var p in policies.Where(p => p.IsOmniWinManaged))
        {
            var res = await RemovePolicyAsync(p.Name);
            if (res.Success) count++;
        }
        return count;
    }

    public async Task<QosOperationResult> ApplyGamingPresetAsync(long limitKbps = 300)
    {
        string[] targetLaunchers = ["steam.exe", "epicgameslauncher.exe", "battle.net.exe", "discord.exe", "qbittorrent.exe", "chrome.exe"];
        int count = 0;
        foreach (var app in targetLaunchers)
        {
            var res = await SetProcessLimitAsync(app, limitKbps);
            if (res.Success) count++;
        }

        return new QosOperationResult
        {
            Success = count > 0,
            Message = $"Preset Gaming activado: {count} aplicaciones limitadas a {limitKbps} KB/s.",
            LimitKbps = limitKbps
        };
    }
}
