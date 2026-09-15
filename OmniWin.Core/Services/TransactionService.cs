using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;

namespace OmniWin.Core.Services;

public record RegistryValueSnapshot
{
    public string HiveName { get; init; } = string.Empty;
    public string SubPath { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
    public bool ExistedBefore { get; init; }
    public string? ValueKind { get; init; }
    public string? StringifiedValue { get; init; }
}

public record ServiceStateSnapshot
{
    public string ServiceName { get; init; } = string.Empty;
    public int StartType { get; init; } = -1; // 2=Auto, 3=Manual, 4=Disabled
    public string Status { get; init; } = string.Empty;
}

public class TweakTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string TweakId { get; init; } = string.Empty;
    public DateTime AppliedAt { get; init; } = DateTime.UtcNow;
    public string Description { get; init; } = string.Empty;
    public List<RegistryValueSnapshot> RegistrySnapshots { get; init; } = new();
    public List<ServiceStateSnapshot> ServiceSnapshots { get; init; } = new();
    public bool IsActive { get; set; } = true;
    public string State { get; set; } = "PREPARED"; // PREPARED, COMMITTED, ROLLED_BACK
}

/// <summary>
/// TransactionService: Provides transactional change-set management and deterministic rollbacks for OmniWin tweaks.
/// Uses a Write-Ahead Log (WAL) to ensure crash safety if Windows BSODs, reboots, or crashes mid-mutation.
/// Restores exact pre-existing registry values/types and service states.
/// </summary>
public class TransactionService
{
    private static readonly Lazy<TransactionService> _instance = new(() => new TransactionService());
    public static TransactionService Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly string _journalPath;
    private readonly string _walPath;
    private Dictionary<string, TweakTransaction> _transactions = new(StringComparer.OrdinalIgnoreCase);
    private TweakTransaction? _inFlightTransaction;

    public bool HasInFlightTransaction
    {
        get
        {
            lock (_lock) { return _inFlightTransaction != null; }
        }
    }

    public TransactionService(string? customJournalPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customJournalPath))
        {
            _journalPath = customJournalPath;
            string dir = Path.GetDirectoryName(_journalPath) ?? Path.GetTempPath();
            string fname = Path.GetFileNameWithoutExtension(_journalPath);
            _walPath = Path.Combine(dir, $"{fname}.wal.json");
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(appData, "OmniWin", "transactions");
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }
            _journalPath = Path.Combine(dir, "journal.json");
            _walPath = Path.Combine(dir, "wal.json");
        }

        LoadJournal();
        RecoverWalIfPresent();
    }

    public void BeginTransaction(string tweakId, string description = "")
    {
        lock (_lock)
        {
            // Review Item 3: Concurrency protection — If another transaction is in-flight and PREPARED,
            // reject to prevent overwriting or leaving orphan state.
            if (_inFlightTransaction != null && _inFlightTransaction.State == "PREPARED")
            {
                if (!_inFlightTransaction.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Cannot begin transaction for '{tweakId}': transaction for '{_inFlightTransaction.TweakId}' is already in-flight (PREPARED).");
                }
                // Same tweak already in-flight: reuse
                return;
            }

            // Review Item 1: Double-Apply Protection — If this tweak is already committed with active snapshots,
            // DO NOT overwrite the original user baseline!
            // Clone the existing baseline snapshots into the new transaction so that subsequent mutations
            // cannot replace the true pre-state with the already-mutated value.
            if (_transactions.TryGetValue(tweakId, out var existingTx) && existingTx.IsActive &&
                (existingTx.RegistrySnapshots.Count > 0 || existingTx.ServiceSnapshots.Count > 0))
            {
                _inFlightTransaction = new TweakTransaction
                {
                    TweakId = tweakId,
                    Description = description,
                    AppliedAt = DateTime.UtcNow,
                    State = "PREPARED",
                    RegistrySnapshots = new List<RegistryValueSnapshot>(existingTx.RegistrySnapshots),
                    ServiceSnapshots = new List<ServiceStateSnapshot>(existingTx.ServiceSnapshots)
                };
                SaveWal();
                return;
            }

            _inFlightTransaction = new TweakTransaction
            {
                TweakId = tweakId,
                Description = description,
                AppliedAt = DateTime.UtcNow,
                State = "PREPARED"
            };
            SaveWal();
        }
    }

    public void CaptureRegistryPreState(RegistryKey root, string subPath, string valueName)
    {
        lock (_lock)
        {
            if (_inFlightTransaction == null) return;

            string hiveName = root.Name;
            // Prevent duplicate captures in the same transaction
            if (_inFlightTransaction.RegistrySnapshots.Any(s =>
                s.HiveName.Equals(hiveName, StringComparison.OrdinalIgnoreCase) &&
                s.SubPath.Equals(subPath, StringComparison.OrdinalIgnoreCase) &&
                s.ValueName.Equals(valueName, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            try
            {
                using var key = root.OpenSubKey(subPath, false);
                if (key == null)
                {
                    _inFlightTransaction.RegistrySnapshots.Add(new RegistryValueSnapshot
                    {
                        HiveName = hiveName,
                        SubPath = subPath,
                        ValueName = valueName,
                        ExistedBefore = false
                    });
                    SaveWal();
                    return;
                }

                object? val = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (val == null)
                {
                    _inFlightTransaction.RegistrySnapshots.Add(new RegistryValueSnapshot
                    {
                        HiveName = hiveName,
                        SubPath = subPath,
                        ValueName = valueName,
                        ExistedBefore = false
                    });
                }
                else
                {
                    var kind = key.GetValueKind(valueName);
                    string stringVal;
                    if (kind == RegistryValueKind.Binary && val is byte[] bytes)
                    {
                        stringVal = Convert.ToBase64String(bytes);
                    }
                    else if (kind == RegistryValueKind.MultiString && val is string[] arr)
                    {
                        stringVal = JsonSerializer.Serialize(arr);
                    }
                    else
                    {
                        stringVal = val.ToString() ?? string.Empty;
                    }

                    _inFlightTransaction.RegistrySnapshots.Add(new RegistryValueSnapshot
                    {
                        HiveName = hiveName,
                        SubPath = subPath,
                        ValueName = valueName,
                        ExistedBefore = true,
                        ValueKind = kind.ToString(),
                        StringifiedValue = stringVal
                    });
                }

                SaveWal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionService] Failed to capture pre-state for {subPath}\\{valueName}: {ex.Message}");
            }
        }
    }

    public void CaptureServicePreState(string serviceName)
    {
        lock (_lock)
        {
            if (_inFlightTransaction == null) return;

            if (_inFlightTransaction.ServiceSnapshots.Any(s => s.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase)))
                return;

            try
            {
                int startMode = 3; // Default manual
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                if (key != null)
                {
                    var startVal = key.GetValue("Start");
                    if (startVal != null)
                    {
                        startMode = Convert.ToInt32(startVal);
                    }
                }

                string status = "Unknown";
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c sc query {serviceName}",
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    string output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit(1000);
                    if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) status = "Running";
                    else if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) status = "Stopped";
                }
                catch { }

                _inFlightTransaction.ServiceSnapshots.Add(new ServiceStateSnapshot
                {
                    ServiceName = serviceName,
                    StartType = startMode,
                    Status = status
                });

                SaveWal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionService] Failed to capture service {serviceName}: {ex.Message}");
            }
        }
    }

    // ==========================================
    // TRANSACTION-AWARE MUTATORS
    // ==========================================
    public void SetDword(RegistryKey root, string subPath, string valueName, int value)
    {
        CaptureRegistryPreState(root, subPath, valueName);
        using var key = root.OpenSubKey(subPath, true) ?? root.CreateSubKey(subPath, true);
        key?.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    public void SetString(RegistryKey root, string subPath, string valueName, string value)
    {
        CaptureRegistryPreState(root, subPath, valueName);
        using var key = root.OpenSubKey(subPath, true) ?? root.CreateSubKey(subPath, true);
        key?.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void SetQword(RegistryKey root, string subPath, string valueName, long value)
    {
        CaptureRegistryPreState(root, subPath, valueName);
        using var key = root.OpenSubKey(subPath, true) ?? root.CreateSubKey(subPath, true);
        key?.SetValue(valueName, value, RegistryValueKind.QWord);
    }

    public void SetMultiString(RegistryKey root, string subPath, string valueName, string[] values)
    {
        CaptureRegistryPreState(root, subPath, valueName);
        using var key = root.OpenSubKey(subPath, true) ?? root.CreateSubKey(subPath, true);
        key?.SetValue(valueName, values, RegistryValueKind.MultiString);
    }

    public void SetBinary(RegistryKey root, string subPath, string valueName, byte[] bytes)
    {
        CaptureRegistryPreState(root, subPath, valueName);
        using var key = root.OpenSubKey(subPath, true) ?? root.CreateSubKey(subPath, true);
        key?.SetValue(valueName, bytes, RegistryValueKind.Binary);
    }

    public void DeleteValue(RegistryKey root, string subPath, string valueName)
    {
        CaptureRegistryPreState(root, subPath, valueName);
        using var key = root.OpenSubKey(subPath, true);
        key?.DeleteValue(valueName, false);
    }

    public void CommitTransaction(string tweakId)
    {
        lock (_lock)
        {
            if (_inFlightTransaction == null || !_inFlightTransaction.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase))
                return;

            _inFlightTransaction.IsActive = true;
            _inFlightTransaction.State = "COMMITTED";
            _transactions[tweakId] = _inFlightTransaction;
            _inFlightTransaction = null;

            SaveJournal();
            ClearWal();
        }
    }

    /// <summary>
    /// Rolls back any mutations performed by the currently in-flight transaction,
    /// marks WAL as ROLLED_BACK, and cleans up in-flight state.
    /// Used when a tweak execution throws or fails midway.
    /// </summary>
    public bool RollbackInFlightTransaction()
    {
        lock (_lock)
        {
            if (_inFlightTransaction == null) return false;
            var inFlight = _inFlightTransaction;
            _inFlightTransaction = null;

            bool result = false;
            if (inFlight.RegistrySnapshots.Count > 0 || inFlight.ServiceSnapshots.Count > 0)
            {
                _transactions[inFlight.TweakId] = inFlight;
                result = RollbackTransaction(inFlight.TweakId, out _);
                _transactions.Remove(inFlight.TweakId);
            }
            ClearWal();
            return result;
        }
    }

    public bool HasActiveTransaction(string tweakId)
    {
        lock (_lock)
        {
            return _transactions.TryGetValue(tweakId, out var tx) && tx.IsActive;
        }
    }

    public bool RollbackTransaction(string tweakId, out string message)
    {
        lock (_lock)
        {
            if (!_transactions.TryGetValue(tweakId, out var tx) || !tx.IsActive)
            {
                message = $"No hay transacción activa registrada para el tweak '{tweakId}'.";
                return false;
            }

            // CRITICAL FIX: If transaction was recorded with 0 snapshots, do NOT claim success!
            // Return false so caller can execute its legacy fallback logic.
            if (tx.RegistrySnapshots.Count == 0 && tx.ServiceSnapshots.Count == 0)
            {
                message = $"La transacción registrada para '{tweakId}' no contiene snapshots de cambios.";
                return false;
            }

            int regRestored = 0;
            int svcRestored = 0;

            // 1. Restore Registry Pre-States
            foreach (var reg in tx.RegistrySnapshots)
            {
                try
                {
                    var root = GetRegistryRoot(reg.HiveName);
                    if (root == null) continue;

                    if (!reg.ExistedBefore)
                    {
                        // Value did not exist prior to tweak: safely delete it
                        using var key = root.OpenSubKey(reg.SubPath, true);
                        if (key != null)
                        {
                            key.DeleteValue(reg.ValueName, false);
                            regRestored++;
                        }
                    }
                    else
                    {
                        // Restore original value and kind
                        using var key = root.CreateSubKey(reg.SubPath, true);
                        if (key != null && Enum.TryParse<RegistryValueKind>(reg.ValueKind, out var kind))
                        {
                            object parsedVal = reg.StringifiedValue ?? "";
                            if (kind == RegistryValueKind.DWord && int.TryParse(reg.StringifiedValue, out int dwordVal))
                            {
                                parsedVal = dwordVal;
                            }
                            else if (kind == RegistryValueKind.QWord && long.TryParse(reg.StringifiedValue, out long qwordVal))
                            {
                                parsedVal = qwordVal;
                            }
                            else if (kind == RegistryValueKind.Binary && reg.StringifiedValue != null)
                            {
                                parsedVal = Convert.FromBase64String(reg.StringifiedValue);
                            }
                            else if (kind == RegistryValueKind.MultiString && reg.StringifiedValue != null)
                            {
                                parsedVal = JsonSerializer.Deserialize<string[]>(reg.StringifiedValue) ?? Array.Empty<string>();
                            }

                            key.SetValue(reg.ValueName, parsedVal, kind);
                            regRestored++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TransactionService] Rollback reg error: {ex.Message}");
                }
            }

            // 2. Restore Service Pre-States
            foreach (var svc in tx.ServiceSnapshots)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}", true);
                    if (key != null && svc.StartType >= 0)
                    {
                        key.SetValue("Start", svc.StartType, RegistryValueKind.DWord);
                        svcRestored++;
                    }

                    if (svc.Status.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    {
                        using var p = Process.Start(new ProcessStartInfo
                        {
                            FileName = "net.exe",
                            Arguments = $"start {svc.ServiceName}",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        p?.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[TransactionService] Rollback service error: {ex.Message}");
                }
            }

            tx.IsActive = false;
            tx.State = "ROLLED_BACK";
            SaveJournal();

            message = $"Transacción '{tweakId}' revertida con éxito ({regRestored} valores de registro y {svcRestored} servicios restaurados a su estado exacto original).";
            return true;
        }
    }

    public List<TweakTransaction> GetAllTransactions()
    {
        lock (_lock)
        {
            return _transactions.Values.ToList();
        }
    }

    private static RegistryKey? GetRegistryRoot(string hiveName)
    {
        if (hiveName.Contains("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase))
            return Registry.LocalMachine;
        if (hiveName.Contains("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase))
            return Registry.CurrentUser;
        if (hiveName.Contains("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase))
            return Registry.ClassesRoot;
        if (hiveName.Contains("HKEY_USERS", StringComparison.OrdinalIgnoreCase))
            return Registry.Users;
        return null;
    }

    private void LoadJournal()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_journalPath))
                {
                    string json = File.ReadAllText(_journalPath);
                    var list = JsonSerializer.Deserialize<List<TweakTransaction>>(json);
                    if (list != null)
                    {
                        _transactions = list.ToDictionary(t => t.TweakId, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionService] Error reading journal: {ex.Message}");
            }
        }
    }

    private void SaveJournal()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_journalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = _journalPath + ".tmp";
            string json = JsonSerializer.Serialize(_transactions.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });

            // Durable flush to disk before rename
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(true);
            }

            File.Move(tmpPath, _journalPath, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TransactionService] Error writing journal: {ex.Message}");
        }
    }

    private string GetPreviousWalPath() =>
        _walPath.EndsWith(".wal.json", StringComparison.OrdinalIgnoreCase)
            ? _walPath.Substring(0, _walPath.Length - 9) + ".wal.previous.json"
            : _walPath.Replace(".json", ".previous.json");

    private void SaveWal()
    {
        if (_inFlightTransaction == null) return;
        try
        {
            string? dir = Path.GetDirectoryName(_walPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmpPath = _walPath + ".tmp";
            string prevPath = GetPreviousWalPath();
            string json = JsonSerializer.Serialize(_inFlightTransaction, new JsonSerializerOptions { WriteIndented = true });

            // 1. Write to tmp file with durable OS flush
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var sw = new StreamWriter(fs, System.Text.Encoding.UTF8))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(true);
            }

            // 2. Rotate previous WAL if main exists
            if (File.Exists(_walPath))
            {
                try
                {
                    File.Copy(_walPath, prevPath, true);
                }
                catch { }
            }

            // 3. Atomic replace / move
            File.Move(tmpPath, _walPath, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TransactionService] Error writing WAL: {ex.Message}");
        }
    }

    private void ClearWal()
    {
        try
        {
            if (File.Exists(_walPath)) File.Delete(_walPath);
            string prevPath = GetPreviousWalPath();
            if (File.Exists(prevPath)) File.Delete(prevPath);
            string tmpPath = _walPath + ".tmp";
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
        }
        catch { }
    }

    private void RecoverWalIfPresent()
    {
        lock (_lock)
        {
            try
            {
                string prevPath = GetPreviousWalPath();

                TweakTransaction? uncommittedTx = null;

                // 1. Try reading primary WAL
                if (File.Exists(_walPath))
                {
                    try
                    {
                        string json = File.ReadAllText(_walPath);
                        uncommittedTx = JsonSerializer.Deserialize<TweakTransaction>(json);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TransactionService] Primary WAL file corrupted ({ex.Message}), checking backup...");
                    }
                }

                // 2. Fall back to previous backup WAL if primary was corrupt/truncated
                if (uncommittedTx == null && File.Exists(prevPath))
                {
                    try
                    {
                        string json = File.ReadAllText(prevPath);
                        uncommittedTx = JsonSerializer.Deserialize<TweakTransaction>(json);
                        Debug.WriteLine($"[TransactionService] Successfully recovered uncommitted transaction from backup WAL: {uncommittedTx?.TweakId}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TransactionService] Backup WAL file also corrupted: {ex.Message}");
                    }
                }

                if (uncommittedTx != null && uncommittedTx.State == "PREPARED")
                {
                    Debug.WriteLine($"[TransactionService] Interrupted transaction detected for '{uncommittedTx.TweakId}'. Rolling back to safe baseline...");
                    // Restore captured pre-states
                    _transactions[uncommittedTx.TweakId] = uncommittedTx;
                    RollbackTransaction(uncommittedTx.TweakId, out _);
                    _transactions.Remove(uncommittedTx.TweakId);
                }

                ClearWal();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TransactionService] Error during WAL recovery: {ex.Message}");
            }
        }
    }
}
