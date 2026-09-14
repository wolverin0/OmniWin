using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace OmniWin.Core.Services;

public record WindowsServiceInfo(
    string Name,
    string DisplayName,
    string Status,
    string StartMode,
    string Description,
    bool IsRecommendedToDisable
);

public class WindowsServiceService
{
    private static readonly HashSet<string> TelemetryAndBloatServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "DiagTrack",              // Connected User Experiences and Telemetry
        "dmwappushservice",       // Device Management Wireless Application Protocol Push
        "MapsBroker",             // Downloaded Maps Manager
        "RetailDemo",             // Retail Demo Service
        "diagnosticshub.standardcollector.service", // Microsoft (R) Diagnostics Hub Standard Collector Service
        "WerSvc",                 // Windows Error Reporting Service (optional)
        "XblAuthManager",         // Xbox Live Auth Manager
        "XblGameSave",            // Xbox Live Game Save
        "XboxNetApiSvc",          // Xbox Live Networking Service
        "XboxGipSvc"              // Xbox Accessory Management Service
    };

    public List<WindowsServiceInfo> GetServices(bool bloatCandidatesOnly = false)
    {
        var list = new List<WindowsServiceInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, State, StartMode, Description FROM Win32_Service");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = mo["Name"]?.ToString() ?? string.Empty;
                string displayName = mo["DisplayName"]?.ToString() ?? name;
                string state = mo["State"]?.ToString() ?? "Unknown";
                string startMode = mo["StartMode"]?.ToString() ?? "Unknown";
                string desc = mo["Description"]?.ToString() ?? string.Empty;

                bool isBloat = TelemetryAndBloatServices.Contains(name);

                if (bloatCandidatesOnly && !isBloat)
                {
                    continue;
                }

                list.Add(new WindowsServiceInfo(
                    Name: name,
                    DisplayName: displayName,
                    Status: state,
                    StartMode: startMode,
                    Description: desc,
                    IsRecommendedToDisable: isBloat
                ));
            }
        }
        catch { }

        return list.OrderByDescending(s => s.IsRecommendedToDisable).ThenBy(s => s.DisplayName).ToList();
    }

    public bool SetServiceState(string serviceName, string startMode, bool stopNow = false)
    {
        // Valid start modes for sc: auto, demand, disabled, delayed-auto
        string scMode = startMode.ToLowerInvariant() switch
        {
            "disabled" => "disabled",
            "manual" => "demand",
            "auto" => "auto",
            "automatic" => "auto",
            _ => "demand"
        };

        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"config \"{serviceName}\" start= {scMode}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();

            if (stopNow)
            {
                var stopPsi = new ProcessStartInfo("sc.exe", $"stop \"{serviceName}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using var pStop = Process.Start(stopPsi);
                pStop?.WaitForExit();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public List<string> OptimizeTelemetryServices()
    {
        var disabled = new List<string>();
        string[] targetServices = ["DiagTrack", "dmwappushservice", "MapsBroker", "RetailDemo"];

        foreach (var svc in targetServices)
        {
            if (SetServiceState(svc, "disabled", true))
            {
                disabled.Add(svc);
            }
        }

        return disabled;
    }
}
