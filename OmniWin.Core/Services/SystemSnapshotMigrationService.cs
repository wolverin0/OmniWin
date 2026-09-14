using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OmniWin.Core.Services;

public class OmniWinSnapshotPackage
{
    public string Version { get; set; } = "1.2.0";
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string MachineName { get; set; } = Environment.MachineName;
    public string OsVersion { get; set; } = Environment.OSVersion.ToString();
    public List<string> AppliedTweakIds { get; set; } = new();
    public Dictionary<string, int> AsrRuleStates { get; set; } = new();
    public AppSettingsModel Settings { get; set; } = new();
}

public class SystemSnapshotMigrationService
{
    private static readonly Lazy<SystemSnapshotMigrationService> _instance = new(() => new SystemSnapshotMigrationService());
    public static SystemSnapshotMigrationService Instance => _instance.Value;

    public string ExportSnapshot(string? destinationPath = null)
    {
        string defaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OmniWin", "Snapshots");
        if (!Directory.Exists(defaultDir))
        {
            Directory.CreateDirectory(defaultDir);
        }

        string finalPath = destinationPath ?? Path.Combine(defaultDir, $"OmniWin-State-{DateTime.Now:yyyyMMdd_HHmmss}.omniwin");

        var tweaksSvc = new ExpandedTweakService();
        var appliedTweaks = tweaksSvc.GetCategorizedTweaks()
            .Where(t => t.IsApplied)
            .Select(t => t.Id)
            .ToList();

        var asrRules = AsrRulesService.NativeRules;
        var asrDict = asrRules.ToDictionary(r => r.Guid, r => (int)r.DefaultAction);

        var package = new OmniWinSnapshotPackage
        {
            Version = "1.2.0",
            ExportedAt = DateTime.UtcNow,
            MachineName = Environment.MachineName,
            OsVersion = Environment.OSVersion.ToString(),
            AppliedTweakIds = appliedTweaks,
            AsrRuleStates = asrDict,
            Settings = AppSettingsService.Instance.Settings
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(package, options);
        File.WriteAllText(finalPath, json);

        return finalPath;
    }

    public (bool Success, string Message, int TweaksApplied) ImportSnapshot(string filePath, bool createVssRestorePoint = true)
    {
        if (!File.Exists(filePath))
        {
            return (false, "El archivo de snapshot no existe.", 0);
        }

        try
        {
            string json = File.ReadAllText(filePath);
            var package = JsonSerializer.Deserialize<OmniWinSnapshotPackage>(json);
            if (package == null)
            {
                return (false, "Formato de archivo .omniwin inválido.", 0);
            }

            if (createVssRestorePoint)
            {
                TweakService.CreateRestorePoint($"OmniWin Snapshot Restore ({package.ExportedAt:yyyy-MM-dd})");
            }

            // 1. Re-apply tweaks
            var tweaksSvc = new ExpandedTweakService();
            int appliedCount = 0;
            foreach (var tweakId in package.AppliedTweakIds)
            {
                var res = tweaksSvc.ApplyTweak(tweakId);
                if (res.Success) appliedCount++;
            }

            // 2. Re-apply ASR rules
            if (package.AsrRuleStates.Count > 0)
            {
                var asrSvc = new AsrRulesService();
                foreach (var kvp in package.AsrRuleStates)
                {
                    Task.Run(() => asrSvc.SetRuleActionAsync(kvp.Key, (AsrRuleAction)kvp.Value));
                }
            }

            // 3. Restore App Settings
            if (package.Settings != null)
            {
                AppSettingsService.Instance.SaveSettings(s =>
                {
                    s.SelectedProfile = package.Settings.SelectedProfile;
                    s.Language = package.Settings.Language;
                    s.MinimizeToTray = package.Settings.MinimizeToTray;
                });
            }

            return (true, $"Snapshot importado con éxito: {appliedCount} optimizaciones y {package.AsrRuleStates.Count} reglas ASR restauradas.", appliedCount);
        }
        catch (Exception ex)
        {
            return (false, $"Error al importar snapshot: {ex.Message}", 0);
        }
    }
}
