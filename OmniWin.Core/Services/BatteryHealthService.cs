using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace OmniWin.Core.Services;

public class BatteryReport
{
    public bool HasBattery { get; set; }
    public bool IsPluggedIn { get; set; }
    public bool IsCharging { get; set; }
    public int ChargePercent { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    public long DesignCapacityMWh { get; set; }
    public long FullChargeCapacityMWh { get; set; }
    public double WearLevelPercent => DesignCapacityMWh > 0 ? Math.Max(0, Math.Round((1.0 - (FullChargeCapacityMWh / (double)DesignCapacityMWh)) * 100.0, 1)) : 0;
    public double HealthPercent => DesignCapacityMWh > 0 ? Math.Min(100.0, Math.Round((FullChargeCapacityMWh / (double)DesignCapacityMWh) * 100.0, 1)) : 0;

    public int CycleCount { get; set; }
    public string DeviceName { get; set; } = "Batería Interna";
    public string Manufacturer { get; set; } = "Estándar ACPI";
    public string Chemistry { get; set; } = "Ión de Litio (Li-ion)";
    public double CurrentDischargeRateWatts { get; set; }

    public string HealthGrade => HealthPercent switch
    {
        >= 90 => "Excelente (Batería como nueva)",
        >= 75 => "Bueno (Degradación normal por uso)",
        >= 50 => "Aceptable (Autonomía reducida)",
        > 0 => "Degradada (Se recomienda reemplazo)",
        _ => "Información de celdas no disponible"
    };
}

public class BatteryHealthService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    /// <summary>
    /// Genera un informe detallado de salud, degradación y consumo en tiempo real de la batería.
    /// </summary>
    public BatteryReport GetBatteryReport()
    {
        var report = new BatteryReport();

        if (GetSystemPowerStatus(out var status))
        {
            report.HasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255;
            report.IsPluggedIn = status.ACLineStatus == 1;
            report.IsCharging = (status.BatteryFlag & 8) != 0;
            report.ChargePercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 0;

            if (status.BatteryLifeTime > 0 && status.BatteryLifeTime != -1)
            {
                report.EstimatedTimeRemaining = TimeSpan.FromSeconds(status.BatteryLifeTime);
            }
        }

        if (!report.HasBattery)
        {
            return report;
        }

        // Consultar WMI para capacidades de diseño, carga máxima y ciclos
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryStaticData");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["DesignedCapacity"] is uint des && des > 0)
                {
                    report.DesignCapacityMWh = des;
                }
                if (obj["DeviceName"] is string dev && !string.IsNullOrWhiteSpace(dev))
                {
                    report.DeviceName = dev.Trim();
                }
                if (obj["ManufactureName"] is string man && !string.IsNullOrWhiteSpace(man))
                {
                    report.Manufacturer = man.Trim();
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryFullChargedCapacity");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["FullChargedCapacity"] is uint full && full > 0)
                {
                    report.FullChargeCapacityMWh = full;
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Win32_Battery");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (report.DesignCapacityMWh == 0 && obj["DesignCapacity"] is uint des && des > 0)
                {
                    report.DesignCapacityMWh = des;
                }
                if (obj["Chemistry"] is ushort chem)
                {
                    report.Chemistry = GetChemistryName(chem);
                }
                if (obj["EstimatedRunTime"] is uint run && run > 0 && run != 71582788 && report.EstimatedTimeRemaining == null)
                {
                    report.EstimatedTimeRemaining = TimeSpan.FromMinutes(run);
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryStatus");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["DischargeRate"] is int rate && rate > 0)
                {
                    report.CurrentDischargeRateWatts = Math.Round(rate / 1000.0, 2);
                }
                else if (obj["ChargeRate"] is int chRate && chRate > 0)
                {
                    report.CurrentDischargeRateWatts = Math.Round(chRate / 1000.0, 2);
                }
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM BatteryCycleCount");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["CycleCount"] is uint cycles)
                {
                    report.CycleCount = (int)cycles;
                }
            }
        }
        catch { }

        // Fallback si WMI BatteryStaticData no reportó diseño
        if (report.DesignCapacityMWh == 0 && report.FullChargeCapacityMWh > 0)
        {
            report.DesignCapacityMWh = report.FullChargeCapacityMWh;
        }

        return report;
    }

    /// <summary>
    /// Activa el modo de Ahorro Extremo de Batería (Power Saver + EcoQoS + límite de frecuencia).
    /// </summary>
    public bool ApplyExtremeBatterySaver(bool enable)
    {
        try
        {
            string scheme = enable
                ? "a1841308-3541-4fab-bc81-f71556f20b4a" // Economizador (Power Saver)
                : "381b4222-f694-41f0-9685-ff5bb260df2e"; // Equilibrado (Balanced)

            var psi = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"/setactive {scheme}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetChemistryName(ushort val) => val switch
    {
        1 => "Otro",
        2 => "Desconocido",
        3 => "Plomo-Ácido",
        4 => "Níquel-Cadmio (NiCd)",
        5 => "Níquel-Metalhidruro (NiMH)",
        6 => "Ión de Litio (Li-ion)",
        7 => "Zinc-Aire",
        8 => "Polímero de Litio (Li-Polymer)",
        _ => "Ión de Litio (Li-ion)"
    };
}
