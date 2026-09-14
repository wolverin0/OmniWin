using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class TrafficMonitorWidgetWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly NetworkService _networkService = new();
    private readonly MemoryService _memoryService = new();
    private readonly ThermalSensorService _thermalService = ThermalSensorService.Instance;

    public TrafficMonitorWidgetWindow()
    {
        InitializeComponent();

        var settings = AppSettingsService.Instance.Settings;
        if (settings.TrafficWidgetX.HasValue && settings.TrafficWidgetY.HasValue &&
            settings.TrafficWidgetX.Value >= 0 && settings.TrafficWidgetY.Value >= 0 &&
            settings.TrafficWidgetX.Value < SystemParameters.VirtualScreenWidth &&
            settings.TrafficWidgetY.Value < SystemParameters.VirtualScreenHeight)
        {
            Left = settings.TrafficWidgetX.Value;
            Top = settings.TrafficWidgetY.Value;
        }
        else
        {
            // Position near bottom right of primary screen (above taskbar)
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(0, workArea.Right - Width - 30);
            Top = Math.Max(0, workArea.Bottom - Height - 20);
        }

        LocationChanged += (s, e) =>
        {
            try
            {
                if (IsLoaded && IsVisible && WindowState == WindowState.Normal)
                {
                    AppSettingsService.Instance.SaveSettings(st =>
                    {
                        st.TrafficWidgetX = Left;
                        st.TrafficWidgetY = Top;
                    });
                }
            }
            catch { }
        };

        _timer.Interval = TimeSpan.FromSeconds(1.0);
        _timer.Tick += (s, e) => UpdateTelemetry();
        _timer.Start();

        UpdateTelemetry();
    }

    private void UpdateTelemetry()
    {
        try
        {
            // 1. Network Throughput
            var (down, up) = _networkService.GetNetworkThroughput();
            TxtDownloadSpeed.Text = FormatSpeed(down);
            TxtUploadSpeed.Text = FormatSpeed(up);

            // 2. Temperatures
            var snap = _thermalService.GetSnapshot();
            TxtCpuTemp.Text = snap.CpuPackageTemp.HasValue ? $"{snap.CpuPackageTemp.Value:F0}°C" : "--°C";
            TxtGpuTemp.Text = snap.GpuCoreTemp.HasValue ? $"{snap.GpuCoreTemp.Value:F0}°C" : "--°C";

            // 3. RAM
            var mem = _memoryService.GetMemoryStats();
            TxtRamPercent.Text = $"{mem.UsagePercentage:F0}%";
        }
        catch { }
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024)
        {
            return $"{(bytesPerSec / (1024.0 * 1024.0)):F1} MB/s";
        }
        if (bytesPerSec >= 1024)
        {
            return $"{(bytesPerSec / 1024.0):F0} KB/s";
        }
        return $"{bytesPerSec:F0} B/s";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Application.Current.MainWindow != null)
        {
            Application.Current.MainWindow.WindowState = WindowState.Normal;
            Application.Current.MainWindow.Activate();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
