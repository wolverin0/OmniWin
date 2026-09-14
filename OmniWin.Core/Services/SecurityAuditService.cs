using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class CriticalEventLogItem
{
    public string LogName { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string EntryType { get; set; } = string.Empty;
    public DateTime TimeGenerated { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class SecurityAuditReport
{
    public string AntivirusProduct { get; set; } = "Windows Defender";
    public bool AntivirusEnabled { get; set; } = true;
    public bool UacEnabled { get; set; } = true;
    public List<CriticalEventLogItem> RecentCriticalEvents { get; set; } = new();
}

public class SecurityAuditService
{
    public SecurityAuditReport GetSecurityAudit()
    {
        var report = new SecurityAuditReport();

        // 1. UAC status
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            report.UacEnabled = Convert.ToInt32(key?.GetValue("EnableLUA") ?? 1) == 1;
        }
        catch { }

        // 2. Antivirus Product via WMI SecurityCenter2
        try
        {
            var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT * FROM AntiVirusProduct");
            foreach (var item in searcher.Get())
            {
                report.AntivirusProduct = item["displayName"]?.ToString() ?? "Windows Defender";
                break;
            }
        }
        catch
        {
            report.AntivirusProduct = "Windows Defender (Predeterminado)";
        }

        // 3. Read Recent Critical Event Logs via fast EventLogReader (last 48 hours, limit 10)
        try
        {
            string query = "*[System[(Level=1 or Level=2 or EventID=41 or EventID=6008) and TimeCreated[timediff(@SystemTime) <= 172800000]]]";
            var eventsQuery = new System.Diagnostics.Eventing.Reader.EventLogQuery("System", System.Diagnostics.Eventing.Reader.PathType.LogName, query)
            {
                ReverseDirection = true
            };
            using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(eventsQuery);

            int count = 0;
            for (var record = reader.ReadEvent(); record != null && count < 10; record = reader.ReadEvent())
            {
                using (record)
                {
                    string msg = string.Empty;
                    try { msg = record.FormatDescription() ?? $"Evento {record.Id}"; } catch { msg = $"Evento {record.Id}"; }

                    report.RecentCriticalEvents.Add(new CriticalEventLogItem
                    {
                        LogName = "System",
                        EventId = record.Id,
                        Source = record.ProviderName,
                        EntryType = record.Level == 1 ? "Critical" : "Error",
                        TimeGenerated = record.TimeCreated ?? DateTime.Now,
                        Message = msg.Length > 200 ? msg.Substring(0, 197) + "..." : msg
                    });
                    count++;
                }
            }
        }
        catch { }

        return report;
    }
}
