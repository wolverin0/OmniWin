using System;
using System.Windows;
using System.Windows.Media;
using OmniWin.Core.Services;
using Wpf.Ui.Appearance;

namespace OmniWin.UI.Services;

public enum AppTheme
{
    Dark,
    Light
}

public class ThemeService
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Instance => _instance.Value;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;
    public event Action<AppTheme>? ThemeChanged;

    public void Initialize()
    {
        string saved = AppSettingsService.Instance.Settings.ThemeMode;
        if (string.Equals(saved, "Light", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTheme(AppTheme.Light);
        }
        else
        {
            ApplyTheme(AppTheme.Dark);
        }
    }

    public void ToggleTheme()
    {
        var target = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ApplyTheme(target);
    }

    public void ApplyTheme(AppTheme theme)
    {
        if (Application.Current == null) return;

        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => ApplyTheme(theme));
            return;
        }

        CurrentTheme = theme;
        AppSettingsService.Instance.SaveSettings(s => s.ThemeMode = theme.ToString());

        // Apply WPF-UI Native Theme
        try
        {
            ApplicationThemeManager.Apply(theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch { }

        // Apply Custom Monochrome Tokens
        var res = Application.Current.Resources;

        if (theme == AppTheme.Dark)
        {
            // Obsidian Precision Studio (Dark Monochrome)
            SetColorAndBrush(res, "BgDark", Color.FromRgb(0x09, 0x09, 0x0B));
            SetColorAndBrush(res, "BgSidebar", Color.FromRgb(0x0D, 0x0D, 0x11));
            SetColorAndBrush(res, "CardDark", Color.FromRgb(0x12, 0x12, 0x15));
            SetColorAndBrush(res, "CardDarkHover", Color.FromRgb(0x18, 0x18, 0x1B));
            SetColorAndBrush(res, "CardBorder", Color.FromRgb(0x27, 0x27, 0x2A));
            SetColorAndBrush(res, "CardBorderSubtle", Color.FromRgb(0x1E, 0x1E, 0x22));
            SetColorAndBrush(res, "BgInput", Color.FromRgb(0x12, 0x12, 0x15));

            SetColorAndBrush(res, "Accent", Color.FromRgb(0xFA, 0xFA, 0xFA));
            SetColorAndBrush(res, "AccentHover", Color.FromRgb(0xE4, 0xE4, 0xE7));
            SetColorAndBrush(res, "AccentMuted", Color.FromRgb(0x27, 0x27, 0x2A));

            SetColorAndBrush(res, "TextPrimary", Color.FromRgb(0xFA, 0xFA, 0xFA));
            SetColorAndBrush(res, "TextSecondary", Color.FromRgb(0xA1, 0xA1, 0xAA));
            SetColorAndBrush(res, "TextMuted", Color.FromRgb(0x71, 0x71, 0x7A));
            SetColorAndBrush(res, "TextSubtle", Color.FromRgb(0x52, 0x52, 0x5B));

            SetColorAndBrush(res, "Emerald", Color.FromRgb(0x10, 0xB9, 0x81));
            SetColorAndBrush(res, "Amber", Color.FromRgb(0xF5, 0x9E, 0x0B));
            SetColorAndBrush(res, "Crimson", Color.FromRgb(0xEF, 0x44, 0x44));

            SetColorAndBrush(res, "ButtonPrimaryBg", Color.FromRgb(0xFA, 0xFA, 0xFA));
            SetColorAndBrush(res, "ButtonPrimaryFg", Color.FromRgb(0x09, 0x09, 0x0B));
            SetColorAndBrush(res, "ButtonSecondaryBg", Color.FromRgb(0x18, 0x18, 0x1B));
            SetColorAndBrush(res, "ButtonSecondaryFg", Color.FromRgb(0xFA, 0xFA, 0xFA));
            SetColorAndBrush(res, "ButtonSecondaryBorder", Color.FromRgb(0x27, 0x27, 0x2A));
        }
        else
        {
            // Ceramic Minimalist Paper (Light Monochrome)
            SetColorAndBrush(res, "BgDark", Color.FromRgb(0xF4, 0xF4, 0xF6));
            SetColorAndBrush(res, "BgSidebar", Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetColorAndBrush(res, "CardDark", Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetColorAndBrush(res, "CardDarkHover", Color.FromRgb(0xF4, 0xF4, 0xF6));
            SetColorAndBrush(res, "CardBorder", Color.FromRgb(0xE4, 0xE4, 0xE7));
            SetColorAndBrush(res, "CardBorderSubtle", Color.FromRgb(0xEE, 0xEE, 0xF0));
            SetColorAndBrush(res, "BgInput", Color.FromRgb(0xFF, 0xFF, 0xFF));

            SetColorAndBrush(res, "Accent", Color.FromRgb(0x18, 0x18, 0x1B));
            SetColorAndBrush(res, "AccentHover", Color.FromRgb(0x27, 0x27, 0x2A));
            SetColorAndBrush(res, "AccentMuted", Color.FromRgb(0xE4, 0xE4, 0xE7));

            SetColorAndBrush(res, "TextPrimary", Color.FromRgb(0x09, 0x09, 0x0B));
            SetColorAndBrush(res, "TextSecondary", Color.FromRgb(0x52, 0x52, 0x5B));
            SetColorAndBrush(res, "TextMuted", Color.FromRgb(0x71, 0x71, 0x7A));
            SetColorAndBrush(res, "TextSubtle", Color.FromRgb(0xA1, 0xA1, 0xAA));

            SetColorAndBrush(res, "Emerald", Color.FromRgb(0x05, 0x96, 0x69));
            SetColorAndBrush(res, "Amber", Color.FromRgb(0xD9, 0x77, 0x06));
            SetColorAndBrush(res, "Crimson", Color.FromRgb(0xDC, 0x26, 0x26));

            SetColorAndBrush(res, "ButtonPrimaryBg", Color.FromRgb(0x18, 0x18, 0x1B));
            SetColorAndBrush(res, "ButtonPrimaryFg", Color.FromRgb(0xFA, 0xFA, 0xFA));
            SetColorAndBrush(res, "ButtonSecondaryBg", Color.FromRgb(0xFF, 0xFF, 0xFF));
            SetColorAndBrush(res, "ButtonSecondaryFg", Color.FromRgb(0x09, 0x09, 0x0B));
            SetColorAndBrush(res, "ButtonSecondaryBorder", Color.FromRgb(0xE4, 0xE4, 0xE7));
        }

        ThemeChanged?.Invoke(CurrentTheme);
    }

    private static void SetColorAndBrush(ResourceDictionary res, string name, Color color)
    {
        res[$"{name}Color"] = color;
        res[name] = new SolidColorBrush(color);
    }
}
