using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class RamModuleInfo
{
    public string DeviceLocator { get; set; } = string.Empty; // e.g. DDR5-A2
    public string Manufacturer { get; set; } = string.Empty;   // e.g. Corsair
    public ulong CapacityBytes { get; set; }
    public double CapacityGb => CapacityBytes / (1024.0 * 1024.0 * 1024.0);
    public uint SpeedMtS { get; set; }
    public uint ConfiguredSpeedMtS { get; set; }
    public string PartNumber { get; set; } = string.Empty;
}

public class MotherboardBiosInfo
{
    // Motherboard
    public string MotherboardManufacturer { get; set; } = "Desconocido";
    public string MotherboardProduct { get; set; } = "Desconocido";
    public string MotherboardVersion { get; set; } = string.Empty;
    public string MotherboardSerial { get; set; } = string.Empty;

    // BIOS
    public string BiosVendor { get; set; } = "Desconocido";
    public string BiosVersion { get; set; } = "Desconocido";
    public string BiosReleaseDate { get; set; } = string.Empty;
    public string SmbiosVersion { get; set; } = string.Empty;
    public bool IsUefi { get; set; } = true;
    public bool IsSecureBootEnabled { get; set; } = false;

    // TPM
    public bool IsTpmPresent { get; set; } = false;
    public string TpmVersion { get; set; } = string.Empty;
    public bool IsTpmEnabled { get; set; } = false;

    // RAM Modules
    public List<RamModuleInfo> RamModules { get; set; } = new();
    public double TotalRamGb => RamModules.Sum(m => m.CapacityGb);
    public string ChannelMode => RamModules.Count >= 2 ? "Dual / Multi-Channel" : "Single Channel";
}

public class MotherboardBiosService
{
    public static MotherboardBiosService Instance { get; } = new();

    public async Task<MotherboardBiosInfo> GetInfoAsync()
    {
        return await Task.Run(() => GetInfo());
    }

    public MotherboardBiosInfo GetInfo()
    {
        var info = new MotherboardBiosInfo();

        try
        {
            // 1. Motherboard Info (Win32_BaseBoard)
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard"))
            {
                foreach (var obj in searcher.Get())
                {
                    info.MotherboardManufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Desconocido";
                    info.MotherboardProduct = obj["Product"]?.ToString()?.Trim() ?? "Desconocido";
                    info.MotherboardVersion = obj["Version"]?.ToString()?.Trim() ?? string.Empty;
                    info.MotherboardSerial = obj["SerialNumber"]?.ToString()?.Trim() ?? string.Empty;
                    break;
                }
            }

            // 2. BIOS Info (Win32_BIOS)
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Name, Version, ReleaseDate, SMBIOSBIOSVersion FROM Win32_BIOS"))
            {
                foreach (var obj in searcher.Get())
                {
                    info.BiosVendor = obj["Manufacturer"]?.ToString()?.Trim() ?? "Desconocido";
                    info.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim() 
                                    ?? obj["Name"]?.ToString()?.Trim() 
                                    ?? obj["Version"]?.ToString()?.Trim() 
                                    ?? "Desconocido";

                    var relDate = obj["ReleaseDate"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(relDate))
                    {
                        if (relDate.Length >= 8 && char.IsDigit(relDate[0]) && char.IsDigit(relDate[1]))
                        {
                            string y = relDate.Substring(0, 4);
                            string m = relDate.Substring(4, 2);
                            string d = relDate.Substring(6, 2);
                            info.BiosReleaseDate = $"{d}/{m}/{y}";
                        }
                        else
                        {
                            info.BiosReleaseDate = relDate;
                        }
                    }
                    break;
                }
            }

            // 3. Secure Boot Status from Registry (Reliable without elevated privileges)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                if (key != null)
                {
                    var val = key.GetValue("UEFISecureBootEnabled");
                    if (val is int intVal)
                    {
                        info.IsSecureBootEnabled = intVal == 1;
                        info.IsUefi = true;
                    }
                }
            }
            catch { }

            // 4. Physical RAM Modules (Win32_PhysicalMemory)
            using (var searcher = new ManagementObjectSearcher("SELECT DeviceLocator, Manufacturer, Capacity, Speed, ConfiguredClockSpeed, PartNumber FROM Win32_PhysicalMemory"))
            {
                foreach (var obj in searcher.Get())
                {
                    ulong cap = 0;
                    if (obj["Capacity"] != null)
                    {
                        ulong.TryParse(obj["Capacity"].ToString(), out cap);
                    }

                    uint speed = 0;
                    if (obj["Speed"] != null) uint.TryParse(obj["Speed"].ToString(), out speed);

                    uint confSpeed = speed;
                    if (obj["ConfiguredClockSpeed"] != null) uint.TryParse(obj["ConfiguredClockSpeed"].ToString(), out confSpeed);

                    info.RamModules.Add(new RamModuleInfo
                    {
                        DeviceLocator = obj["DeviceLocator"]?.ToString()?.Trim() ?? "Slot",
                        Manufacturer = obj["Manufacturer"]?.ToString()?.Trim() ?? "Desconocido",
                        CapacityBytes = cap,
                        SpeedMtS = speed,
                        ConfiguredSpeedMtS = confSpeed,
                        PartNumber = obj["PartNumber"]?.ToString()?.Trim() ?? string.Empty
                    });
                }
            }

            // 5. TPM Status
            try
            {
                var tpmScope = new ManagementScope(@"\\.\root\cimv2\security\microsofttpm");
                tpmScope.Connect();
                using var tpmSearcher = new ManagementObjectSearcher(tpmScope, new ObjectQuery("SELECT IsActivated_InitialValue, IsEnabled_InitialValue, SpecVersion FROM Win32_Tpm"));
                foreach (var obj in tpmSearcher.Get())
                {
                    info.IsTpmPresent = true;
                    info.TpmVersion = obj["SpecVersion"]?.ToString()?.Trim() ?? "2.0";
                    info.IsTpmEnabled = (bool)(obj["IsEnabled_InitialValue"] ?? false);
                    break;
                }
            }
            catch
            {
                // Fallback check from registry
                try
                {
                    using var tpmReg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM\WMI");
                    if (tpmReg != null)
                    {
                        info.IsTpmPresent = true;
                        info.TpmVersion = "2.0";
                        info.IsTpmEnabled = true;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MotherboardBiosService Error] {ex.Message}");
        }

        return info;
    }
}
