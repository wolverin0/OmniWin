using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public class RegistryValueBackup
{
    public string RootHive { get; set; } = "HKLM"; // HKLM or HKCU
    public string KeyPath { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public object? ValueData { get; set; }
    public string ValueKind { get; set; } = "DWord";
    public bool ExistedBefore { get; set; } = true;
}

public class SystemSnapshotMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public bool HasSystemRestorePoint { get; set; }
    public string SystemRestorePointDescription { get; set; } = string.Empty;
    public int RegistryKeysCount { get; set; }
    public List<string> AppliedTweakIds { get; set; } = new();
    public List<RegistryValueBackup> RegistryValues { get; set; } = new();
}

public class RollbackResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int RestoredValuesCount { get; set; }
    public List<string> RestoredKeys { get; set; } = new();
}

public class RollbackSnapshotService
{
    private static readonly Lazy<RollbackSnapshotService> _instance = new(() => new RollbackSnapshotService());
    public static RollbackSnapshotService Instance => _instance.Value;

    private readonly string _snapshotDirectory;

    public RollbackSnapshotService()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        _snapshotDirectory = Path.Combine(baseDir, "OmniWin", "Snapshots");
        try
        {
            Directory.CreateDirectory(_snapshotDirectory);
        }
        catch { }
    }

    public string SnapshotDirectory => _snapshotDirectory;

    public static readonly (string Hive, string Key, string Name, string Kind)[] CriticalTweakKeys = new[]
    {
        ("HKLM", @"SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", "DWord"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", "DWord"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", "DWord"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", "DWord"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", "DWord"),
        ("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", "String"),
        ("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", "DWord"),
        ("HKCU", @"System\GameConfigStore", "GameDVR_FSEBehaviorMode", "DWord"),
        ("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", "DWord"),
        ("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", "DWord"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", "DWord"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\DiagTrack", "Start", "DWord"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\dmwappushservice", "Start", "DWord"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Services\SysMain", "Start", "DWord"),
        ("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", "DWord"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", "DWord"),
        ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", "DWord")
    };

    public async Task<SystemSnapshotMetadata> CreateSnapshotAsync(string name, string description, List<string>? tweakIds = null, bool createWindowsRestorePoint = true)
    {
        var metadata = new SystemSnapshotMetadata
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}" : name,
            Description = description,
            CreatedAt = DateTime.UtcNow,
            AppliedTweakIds = tweakIds ?? new List<string>()
        };

        // 1. Capture Critical Registry Values
        foreach (var (hive, key, valName, kind) in CriticalTweakKeys)
        {
            var backup = ReadRegistryValue(hive, key, valName, kind);
            metadata.RegistryValues.Add(backup);
        }
        metadata.RegistryKeysCount = metadata.RegistryValues.Count;

        // 2. Optionally trigger Windows System Restore Point
        if (createWindowsRestorePoint)
        {
            metadata.HasSystemRestorePoint = await Task.Run(() => TryCreateSystemRestorePoint(metadata.Name));
            if (metadata.HasSystemRestorePoint)
            {
                metadata.SystemRestorePointDescription = $"OmniWin: {metadata.Name}";
            }
        }

        // 3. Save JSON Manifest
        try
        {
            Directory.CreateDirectory(_snapshotDirectory);
            string filePath = Path.Combine(_snapshotDirectory, $"{metadata.Id}.json");
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write snapshot file: {ex.Message}");
        }

        return metadata;
    }

    public List<SystemSnapshotMetadata> GetSnapshots()
    {
        var list = new List<SystemSnapshotMetadata>();
        if (!Directory.Exists(_snapshotDirectory)) return list;

        foreach (var file in Directory.EnumerateFiles(_snapshotDirectory, "*.json"))
        {
            try
            {
                string json = File.ReadAllText(file);
                var meta = JsonSerializer.Deserialize<SystemSnapshotMetadata>(json);
                if (meta != null)
                {
                    list.Add(meta);
                }
            }
            catch { }
        }

        return list.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public async Task<RollbackResult> RollbackSnapshotAsync(string snapshotId)
    {
        var result = new RollbackResult();
        string file = Path.Combine(_snapshotDirectory, $"{snapshotId}.json");
        if (!File.Exists(file))
        {
            result.Success = false;
            result.Message = "El archivo de snapshot no existe o fue eliminado.";
            return result;
        }

        try
        {
            string json = await File.ReadAllTextAsync(file);
            var snapshot = JsonSerializer.Deserialize<SystemSnapshotMetadata>(json);
            if (snapshot == null)
            {
                result.Success = false;
                result.Message = "No se pudo deserializar el contenido del snapshot.";
                return result;
            }

            int restored = 0;
            foreach (var item in snapshot.RegistryValues)
            {
                bool ok = RestoreRegistryValue(item);
                if (ok)
                {
                    restored++;
                    result.RestoredKeys.Add($"{item.RootHive}\\{item.KeyPath}::{item.ValueName}");
                }
            }

            result.RestoredValuesCount = restored;
            result.Success = true;
            result.Message = $"Snapshot '{snapshot.Name}' revertido con éxito. Se restauraron {restored} valores del registro a su estado original.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Error durante el rollback: {ex.Message}";
        }

        return result;
    }

    public bool DeleteSnapshot(string snapshotId)
    {
        try
        {
            string file = Path.Combine(_snapshotDirectory, $"{snapshotId}.json");
            if (File.Exists(file))
            {
                File.Delete(file);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static RegistryValueBackup ReadRegistryValue(string hive, string subKey, string valueName, string kind)
    {
        var backup = new RegistryValueBackup
        {
            RootHive = hive,
            KeyPath = subKey,
            ValueName = valueName,
            ValueKind = kind
        };

        try
        {
            RegistryKey baseKey = hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            using var key = baseKey.OpenSubKey(subKey, false);
            if (key == null)
            {
                backup.ExistedBefore = false;
                backup.ValueData = null;
                return backup;
            }

            object? val = key.GetValue(valueName);
            if (val == null)
            {
                backup.ExistedBefore = false;
                backup.ValueData = null;
            }
            else
            {
                backup.ExistedBefore = true;
                backup.ValueData = val;
            }
        }
        catch
        {
            backup.ExistedBefore = false;
        }

        return backup;
    }

    private static bool RestoreRegistryValue(RegistryValueBackup item)
    {
        try
        {
            RegistryKey baseKey = item.RootHive.Equals("HKCU", StringComparison.OrdinalIgnoreCase)
                ? Registry.CurrentUser
                : Registry.LocalMachine;

            if (!item.ExistedBefore)
            {
                // Delete value if it didn't exist prior to tweak
                using var key = baseKey.OpenSubKey(item.KeyPath, true);
                if (key != null)
                {
                    try { key.DeleteValue(item.ValueName, false); } catch { }
                }
                return true;
            }

            using (var key = baseKey.CreateSubKey(item.KeyPath, true))
            {
                if (key == null) return false;

                if (item.ValueData is JsonElement jsonElem)
                {
                    if (item.ValueKind == "DWord" && jsonElem.TryGetInt32(out int intVal))
                    {
                        key.SetValue(item.ValueName, intVal, RegistryValueKind.DWord);
                    }
                    else if (jsonElem.ValueKind == JsonValueKind.String)
                    {
                        key.SetValue(item.ValueName, jsonElem.GetString() ?? "", RegistryValueKind.String);
                    }
                }
                else if (item.ValueData != null)
                {
                    var kind = item.ValueKind switch
                    {
                        "DWord" => RegistryValueKind.DWord,
                        "QWord" => RegistryValueKind.QWord,
                        _ => RegistryValueKind.String
                    };
                    key.SetValue(item.ValueName, item.ValueData, kind);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error restoring {item.RootHive}\\{item.KeyPath}: {ex.Message}");
            return false;
        }
    }

    private static bool TryCreateSystemRestorePoint(string description)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description 'OmniWin: {description.Replace("'", "")}' -RestorePointType 'MODIFY_SETTINGS'\"",
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(12000);
            return proc?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
