using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniWin.Core.Services;
using OmniWin.UI;
using OmniWin.UI.Services;
using OmniWin.UI.Views;
using Xunit;

namespace OmniWin.Tests;

[Collection("WpfVisualTests")]
public class NewFeaturesVisualTests
{
    private static readonly object _appLock = WpfTestHelper.AppLock;
    private static void RunInSta(Action action) => WpfTestHelper.Run(action);

    private static void EnsureAppResources()
    {
        lock (_appLock)
        {
            if (Application.Current == null)
            {
                try
                {
                    var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.InitializeComponent();
                }
                catch { }
            }
        }
    }

    private static void RenderControlToFile(FrameworkElement control, string outputFileName)
    {
        var window = new Window
        {
            Width = 1100,
            Height = 750,
            Content = control,
            Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0E)),
            WindowStyle = WindowStyle.None
        };

        window.Show();
        window.UpdateLayout();

        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        if (width <= 0) width = 1100;
        if (height <= 0) height = 750;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        string dir = @"C:\Users\pauol\Pictures\Screenshots\OmniWinTests";
        Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, outputFileName);
        using (var fs = File.Create(filePath))
        {
            encoder.Save(fs);
        }

        string reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
        Directory.CreateDirectory(reportsDir);
        try { File.Copy(filePath, Path.Combine(reportsDir, outputFileName), true); } catch { }

        window.Close();
    }

    private static void RenderWindowToFile(Window window, string outputFileName, bool withDarkBackdrop = true)
    {
        window.Show();
        window.UpdateLayout();

        var width = (int)Math.Max(window.ActualWidth, 320);
        var height = (int)Math.Max(window.ActualHeight, 180);

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);

        if (withDarkBackdrop)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0C, 0x11, 0x1D)), null, new Rect(0, 0, width, height));
            }
            rtb.Render(dv);
        }

        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        string dir = @"C:\Users\pauol\Pictures\Screenshots\OmniWinTests";
        Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, outputFileName);
        using var fs = File.Create(filePath);
        encoder.Save(fs);

        window.Close();
    }

    [Fact]
    public void Render_CompanionServerControl_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var ctl = new CompanionServerControl();
            RenderControlToFile(ctl, "19_omnicompanion_dashboard.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\19_omnicompanion_dashboard.png"));
        });
    }

    [Fact]
    public void Render_GameProfilerControl_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var ctl = new GameProfilerControl();
            RenderControlToFile(ctl, "20_game_profiler.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\20_game_profiler.png"));
        });
    }

    [Fact]
    public void Render_FirewallMonitorControl_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var ctl = new FirewallMonitorControl();
            RenderControlToFile(ctl, "21_firewall_monitor.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\21_firewall_monitor.png"));
        });
    }

    [Fact]
    public void Render_DnsSecurityControl_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var ctl = new DnsSecurityControl();
            RenderControlToFile(ctl, "22_dns_security.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\22_dns_security.png"));
        });
    }

    [Fact]
    public void Render_GamingOverlayWindow_RivaTunerStyle_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudStyleIndex = 0;
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudBackgroundOpacity = 0.0;
            var win = new GamingOverlayWindow();
            win.ApplyHudConfiguration();
            win.SetClickThrough(true); // Locked mode: pure floating text over 3D game render
            Assert.Equal(Visibility.Visible, win.PanelRivaTuner.Visibility);
            Assert.Equal(Visibility.Collapsed, win.PanelGlassmorphicCard.Visibility);
            Assert.Equal(Visibility.Collapsed, win.HeaderBar.Visibility);
            RenderWindowToFile(win, "23_gaming_hud_rivatuner.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\23_gaming_hud_rivatuner.png"));
        });
    }

    [Fact]
    public void Render_GamingOverlayWindow_GlassmorphicCard_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudStyleIndex = 1;
            var win = new GamingOverlayWindow();
            win.ApplyHudConfiguration();
            Assert.Equal(Visibility.Visible, win.PanelGlassmorphicCard.Visibility);
            Assert.Equal(Visibility.Collapsed, win.PanelRivaTuner.Visibility);
            RenderWindowToFile(win, "24_gaming_hud_card.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\24_gaming_hud_card.png"));
        });
    }

    [Fact]
    public void Render_GamingOverlayWindow_CompactBar_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudStyleIndex = 2;
            var win = new GamingOverlayWindow();
            win.ApplyHudConfiguration();
            Assert.Equal(Visibility.Visible, win.PanelCompactBar.Visibility);
            RenderWindowToFile(win, "25_gaming_hud_compact.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\25_gaming_hud_compact.png"));
        });
    }

    [Fact]
    public void Render_GamingOverlayWindow_CompactBar_Locked_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudStyleIndex = 2;
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudBackgroundOpacity = 0.0;
            var win = new GamingOverlayWindow();
            win.ApplyHudConfiguration();
            win.SetClickThrough(true);
            Assert.Equal(Visibility.Visible, win.PanelCompactBar.Visibility);
            Assert.Equal(Visibility.Collapsed, win.HeaderBar.Visibility);
            RenderWindowToFile(win, "25b_gaming_hud_compact_locked.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\25b_gaming_hud_compact_locked.png"));
        });
    }

    [Fact]
    public void Render_GamingOverlayWindow_SettingsDrawer_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            OmniWin.Core.Services.AppSettingsService.Instance.Settings.HudStyleIndex = 0;
            var win = new GamingOverlayWindow();
            win.ApplyHudConfiguration();
            win.DrawerSettings.Visibility = Visibility.Visible;
            RenderWindowToFile(win, "26_gaming_hud_drawer.png");
            Assert.True(File.Exists(@"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\26_gaming_hud_drawer.png"));
        });
    }

    private static void RenderControlToFileWithTheme(FrameworkElement control, string outputFileName, AppTheme theme)
    {
        ThemeService.Instance.ApplyTheme(theme);
        var bg = theme == AppTheme.Dark ? Color.FromRgb(0x09, 0x09, 0x0B) : Color.FromRgb(0xF4, 0xF4, 0xF6);
        var window = new Window
        {
            Width = 1100,
            Height = 750,
            Content = control,
            Background = new SolidColorBrush(bg),
            WindowStyle = WindowStyle.None
        };

        window.Show();
        window.UpdateLayout();

        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        if (width <= 0) width = 1100;
        if (height <= 0) height = 750;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        string dir = @"C:\Users\pauol\Pictures\Screenshots\OmniWinTests";
        Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, outputFileName);
        using (var fs = File.Create(filePath))
        {
            encoder.Save(fs);
        }

        string artifactDir = @"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285";
        Directory.CreateDirectory(artifactDir);
        try { File.Copy(filePath, Path.Combine(artifactDir, outputFileName), true); } catch { }

        string reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
        Directory.CreateDirectory(reportsDir);
        try { File.Copy(filePath, Path.Combine(reportsDir, outputFileName), true); } catch { }

        window.Close();
    }

    private static void RenderMainWindowWithTheme(string outputFileName, AppTheme theme)
    {
        AppSettingsService.Instance.SaveSettings(s => s.ThemeMode = theme.ToString());
        ThemeService.Instance.ApplyTheme(theme);
        var bg = theme == AppTheme.Dark ? Color.FromRgb(0x09, 0x09, 0x0B) : Color.FromRgb(0xF4, 0xF4, 0xF6);
        var window = new MainWindow
        {
            Width = 1200,
            Height = 780,
            Background = new SolidColorBrush(bg),
            WindowStyle = WindowStyle.None,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None
        };

        window.Show();
        ThemeService.Instance.ApplyTheme(theme);
        window.WelcomeTourOverlay.Visibility = Visibility.Collapsed;
        window.OnboardingOverlay.Visibility = Visibility.Collapsed;
        window.WizardControl.Visibility = Visibility.Collapsed;
        window.TourControl.Visibility = Visibility.Collapsed;
        window.UpdateLayout();

        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        if (width <= 0) width = 1200;
        if (height <= 0) height = 780;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        string dir = @"C:\Users\pauol\Pictures\Screenshots\OmniWinTests";
        Directory.CreateDirectory(dir);

        string filePath = Path.Combine(dir, outputFileName);
        using (var fs = File.Create(filePath))
        {
            encoder.Save(fs);
        }

        string artifactDir = @"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285";
        Directory.CreateDirectory(artifactDir);
        try { File.Copy(filePath, Path.Combine(artifactDir, outputFileName), true); } catch { }

        string reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
        Directory.CreateDirectory(reportsDir);
        try { File.Copy(filePath, Path.Combine(reportsDir, outputFileName), true); } catch { }

        window.Close();
    }

    [Fact]
    public void Render_Dashboard_ObsidianDark_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            RenderMainWindowWithTheme("omniwin_obsidian_dark_dashboard.png", AppTheme.Dark);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_obsidian_dark_dashboard.png"));
        });
    }

    [Fact]
    public void Render_Dashboard_CeramicLight_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            RenderMainWindowWithTheme("omniwin_ceramic_light_dashboard.png", AppTheme.Light);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_ceramic_light_dashboard.png"));
        });
    }

    [Fact]
    public void Render_GameProfiler_ObsidianDark_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var control = new GameProfilerControl();
            RenderControlToFileWithTheme(control, "omniwin_obsidian_dark_game_profiler.png", AppTheme.Dark);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_obsidian_dark_game_profiler.png"));
        });
    }

    [Fact]
    public void Render_GameProfiler_CeramicLight_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var control = new GameProfilerControl();
            RenderControlToFileWithTheme(control, "omniwin_ceramic_light_game_profiler.png", AppTheme.Light);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_ceramic_light_game_profiler.png"));
        });
    }

    [Fact]
    public void Render_DiskSpaceAnalyzer_ObsidianDark_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var control = new DiskSpaceAnalyzerControl();
            RenderControlToFileWithTheme(control, "omniwin_obsidian_dark_disk_analyzer.png", AppTheme.Dark);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_obsidian_dark_disk_analyzer.png"));
        });
    }

    [Fact]
    public void Render_DiskSpaceAnalyzer_CeramicLight_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var control = new DiskSpaceAnalyzerControl();
            RenderControlToFileWithTheme(control, "omniwin_ceramic_light_disk_analyzer.png", AppTheme.Light);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_ceramic_light_disk_analyzer.png"));
        });
    }

    [Fact]
    public void Render_DeduplicationControl_CeramicLight_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var control = new DeduplicationControl();
            RenderControlToFileWithTheme(control, "omniwin_ceramic_light_deduplication.png", AppTheme.Light);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_ceramic_light_deduplication.png"));
        });
    }

    [Fact]
    public void Render_SoftwareDriversControl_CeramicLight_VisualTest()
    {
        RunInSta(() =>
        {
            EnsureAppResources();
            var control = new SoftwareDriversControl();
            RenderControlToFileWithTheme(control, "omniwin_ceramic_light_softwaredrivers.png", AppTheme.Light);
            Assert.True(File.Exists(@"C:\Users\pauol\.gemini\antigravity-cli\brain\b45df942-f950-4a39-be78-996814694285\omniwin_ceramic_light_softwaredrivers.png"));
        });
    }
}
