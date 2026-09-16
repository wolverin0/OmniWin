using System;
using System.IO;
using System.Text.Json;

namespace OmniWin.Core.Services;

public class AppSettingsModel
{
    public bool OnboardingCompleted { get; set; } = false;
    public bool WelcomeTourCompleted { get; set; } = false;
    public string SelectedProfile { get; set; } = "Desktop";
    public string Language { get; set; } = "es";
    public bool MinimizeToTray { get; set; } = true;
    public bool TrafficWidgetVisible { get; set; } = false;
    public double? TrafficWidgetX { get; set; } = null;
    public double? TrafficWidgetY { get; set; } = null;
    public string OverlayHotkey { get; set; } = "Ctrl+Shift+O";
    public string WidgetHotkey { get; set; } = "Ctrl+Shift+W";
    public string PurgeHotkey { get; set; } = "Ctrl+Shift+P";
    public DateTime? LastOptimizationDate { get; set; } = null;
    public int TotalOptimizationsApplied { get; set; } = 0;

    // Gaming Overlay HUD Configuration
    public int HudStyleIndex { get; set; } = 0; // 0 = RivaTunerText, 1 = GlassmorphicCard, 2 = CompactBar
    public double HudBackgroundOpacity { get; set; } = 0.0; // 0.0 to 1.0
    public double HudScale { get; set; } = 1.0; // 0.8 to 1.6
    public bool HudShowCpu { get; set; } = true;
    public bool HudShowCpuTemp { get; set; } = true;
    public bool HudShowGpu { get; set; } = true;
    public bool HudShowGpuTemp { get; set; } = true;
    public bool HudShowRam { get; set; } = true;
    public bool HudShowPing { get; set; } = true;
    public bool HudShowSessionTimer { get; set; } = true;
    public bool HudShowClock { get; set; } = true;
    public double? HudPositionX { get; set; } = null;
    public double? HudPositionY { get; set; } = null;
}

public class AppSettingsService
{
    private static readonly Lazy<AppSettingsService> _instance = new(() => new AppSettingsService());
    public static AppSettingsService Instance => _instance.Value;

    private readonly string _settingsDir;
    private readonly string _settingsFilePath;
    private readonly object _lock = new();
    private AppSettingsModel _settings;

    public AppSettingsModel Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    public AppSettingsService()
    {
        _settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniWin");
        _settingsFilePath = Path.Combine(_settingsDir, "settings.json");
        _settings = LoadSettingsInternal();
    }

    public void SaveSettings(Action<AppSettingsModel>? updateAction = null)
    {
        lock (_lock)
        {
            try
            {
                updateAction?.Invoke(_settings);
                if (!Directory.Exists(_settingsDir))
                {
                    Directory.CreateDirectory(_settingsDir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }
    }

    private AppSettingsModel LoadSettingsInternal()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                string json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettingsModel>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch { }

        return new AppSettingsModel();
    }
}
