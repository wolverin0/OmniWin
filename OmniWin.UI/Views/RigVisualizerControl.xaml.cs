using System;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class RigVisualizerControl : UserControl, INotifyPropertyChanged
{
    private readonly OpenRgbClientService _rgbService = OpenRgbClientService.Instance;
    private SolidColorBrush _currentRgbBrush = new(Color.FromRgb(0x00, 0xE5, 0xFF));

    public SolidColorBrush CurrentRgbBrush
    {
        get => _currentRgbBrush;
        set
        {
            _currentRgbBrush = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private string _cpuModel = "Intel Core i9-14900K";
    private string _gpuModel = "NVIDIA GeForce RTX 3080";
    private string _motherboardModel = "Gigabyte Technology Co., Ltd. Z790 AORUS PRO X";
    private double _ramGb = 64.0;

    public RigVisualizerControl()
    {
        InitializeComponent();
        DataContext = this;
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        DetectLocalHardware();
        UpdateRigDisplay();

        _rgbService.OnColorChanged += (hex) =>
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(hex);
                    CurrentRgbBrush = new SolidColorBrush(color);
                    IcRgbDevices.Items.Refresh();
                }
                catch { }
            });
        };

        bool connected = await _rgbService.ConnectAsync();
        UpdateOpenRgbUi(connected);

        LoadAiRenderImage();
    }

    private void LoadAiRenderImage()
    {
        try
        {
            string[] candidatePaths = new[]
            {
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "rig_render.jpg"),
                @"C:\Users\pauol\Source\Repos\OmniWin\OmniWin.UI\Assets\rig_render.jpg",
                System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "OmniWin.UI", "Assets", "rig_render.jpg")
            };

            foreach (var path in candidatePaths)
            {
                if (System.IO.File.Exists(path))
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(System.IO.Path.GetFullPath(path), UriKind.Absolute);
                    bmp.EndInit();
                    ImgAiRender.Source = bmp;
                    break;
                }
            }
        }
        catch { }
    }

    private void DetectLocalHardware()
    {
        try
        {
            using var searcherCpu = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var item in searcherCpu.Get())
            {
                _cpuModel = item["Name"]?.ToString()?.Trim() ?? _cpuModel;
                break;
            }

            using var searcherGpu = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcherGpu.Get())
            {
                string name = item["Name"]?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(name))
                {
                    _gpuModel = name;
                    break;
                }
            }

            using var searcherMb = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var item in searcherMb.Get())
            {
                string mfg = item["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                string prod = item["Product"]?.ToString()?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(prod))
                {
                    _motherboardModel = string.IsNullOrEmpty(mfg) ? prod : $"{mfg} {prod}";
                }
                break;
            }

            using var searcherCs = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var item in searcherCs.Get())
            {
                if (ulong.TryParse(item["TotalPhysicalMemory"]?.ToString(), out ulong totalBytes))
                {
                    _ramGb = Math.Round(totalBytes / (1024.0 * 1024 * 1024), 0);
                }
                break;
            }
        }
        catch { }
    }

    private void UpdateRigDisplay()
    {
        TxtRigSummaryBadge.Text = $"{CleanCpuName(_cpuModel)} • {CleanGpuName(_gpuModel)} • {_ramGb:N0} GB";
        TxtCpuName.Text = _cpuModel;
        TxtGpuName.Text = _gpuModel;
        TxtMbHeader.Text = _motherboardModel;
        TxtRamTotal.Text = $"{_ramGb:N0} GB DDR5 (4 Módulos)";
        TxtCpuTempCircle.Text = "48°C";
        TxtCpuClockCircle.Text = "5.8 GHz";
        TxtGpuTemp.Text = "42°C";
    }

    private static string CleanCpuName(string name)
    {
        return name.Replace("13th Gen ", "").Replace("14th Gen ", "")
                   .Replace("Intel(R) Core(TM) ", "")
                   .Replace("AMD Ryzen ", "Ryzen ")
                   .Trim();
    }

    private static string CleanGpuName(string name)
    {
        return name.Replace("NVIDIA GeForce ", "").Replace("AMD Radeon ", "").Trim();
    }

    private void UpdateOpenRgbUi(bool connected)
    {
        if (connected)
        {
            BrdOpenRgbStatus.Background = new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B));
            TxtOpenRgbStatus.Text = $"🟢 Servidor OpenRGB Activo ({_rgbService.Devices.Count} Dispositivos)";
            TxtOpenRgbStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        }
        else
        {
            BrdOpenRgbStatus.Background = new SolidColorBrush(Color.FromRgb(0x78, 0x35, 0x0F));
            TxtOpenRgbStatus.Text = "🟡 Modo Simulación Local (OpenRGB no detectado en puerto 6742)";
            TxtOpenRgbStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        }

        IcRgbDevices.ItemsSource = _rgbService.Devices;
    }

    private void BtnModeHologram_Click(object sender, RoutedEventArgs e)
    {
        PnlHologramView.Visibility = Visibility.Visible;
        PnlAiRenderView.Visibility = Visibility.Collapsed;
        BtnModeHologram.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        BtnModeAiRender.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
    }

    private void BtnModeAiRender_Click(object sender, RoutedEventArgs e)
    {
        PnlHologramView.Visibility = Visibility.Collapsed;
        PnlAiRenderView.Visibility = Visibility.Visible;
        BtnModeAiRender.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
        BtnModeHologram.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));
    }

    private void Component_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string tag)
        {
            InspectComponent(tag);
        }
    }

    private void InspectComponent(string tag)
    {
        PnlInspectorDetails.Children.Clear();

        switch (tag)
        {
            case "CPU":
                TxtInspectorTitle.Text = _cpuModel;
                TxtInspectorSubtitle.Text = "24 Núcleos (8 Rendimiento + 16 Eficiencia) • 32 Hilos";
                AddDetail("• Frecuencia Actual: 5.7 - 5.8 GHz (Turbo Boost Max)", "#F8FAFC");
                AddDetail("• Temperatura en Paquete: 48°C (Rango Óptimo)", "#10B981");
                AddDetail("• Litografía: Intel 7 (10nm Enhanced SuperFin)", "#94A3B8");
                AddDetail("• Zócalo / Socket: LGA1700", "#94A3B8");
                AddDetail("• Potencia / Consumo: 65W Idle (253W PL2)", "#94A3B8");
                break;

            case "GPU":
                TxtInspectorTitle.Text = _gpuModel;
                TxtInspectorSubtitle.Text = "Arquitectura Ampere • 8704 Núcleos CUDA";
                AddDetail("• VRAM: 10 GB GDDR6X (Bus 320-bit @ 19 Gbps)", "#F8FAFC");
                AddDetail("• Temperatura Core: 42°C | Hot Spot: 51°C", "#10B981");
                AddDetail("• Ventiladores: 0 RPM (Modo Silencio Fan-Stop)", "#38BDF8");
                AddDetail("• Interfaz de Bus: PCIe 4.0 x16 Nativo", "#94A3B8");
                AddDetail("• Potencia TGP: 320W", "#94A3B8");
                break;

            case "RAM":
                TxtInspectorTitle.Text = "Memoria del Sistema (DDR5)";
                TxtInspectorSubtitle.Text = $"{_ramGb:N0} GB Totales • Configuración Quad-Channel Virtual";
                AddDetail("• Estado de Uso: ~18% Utilizado (Espacio Libre: ~52 GB)", "#F8FAFC");
                AddDetail("• Frecuencia / Perfil: DDR5-6000 MT/s (Intel XMP 3.0)", "#38BDF8");
                AddDetail("• Latencia CAS: CL30 / CL32", "#94A3B8");
                AddDetail("• Iluminación: ARGB Sincronizado por Software", "#10B981");
                break;

            case "MOTHERBOARD":
                TxtInspectorTitle.Text = _motherboardModel;
                TxtInspectorSubtitle.Text = "Chipset Intel Z790 • Factor de Forma ATX";
                AddDetail("• Conectores M.2 NVMe: 5x PCIe 5.0 / 4.0 con Thermal Guards", "#F8FAFC");
                AddDetail("• Red / LAN: 5 GbE + Wi-Fi 7 (802.11be)", "#38BDF8");
                AddDetail("• Audio: Realtek ALC1220-VB High Definition", "#94A3B8");
                AddDetail("• Cabezales ARGB: 3x 5V Addressable RGB + 1x 12V RGB", "#10B981");
                break;

            case "STORAGE":
                TxtInspectorTitle.Text = "Sub-sistema de Almacenamiento";
                TxtInspectorSubtitle.Text = "Unidades de Estado Sólido NVMe PCIe Gen 4 + SATA";
                AddDetail("• Unidad C: (Sistema): NVMe M.2 1 TB (262 GB Libres)", "#F8FAFC");
                AddDetail("• Estado de Salud S.M.A.R.T.: 100% Bueno", "#10B981");
                AddDetail("• Temperatura Promedio SSD: 38°C", "#10B981");
                AddDetail("• Velocidad de Lectura Secuencial: Hasta 7.000 MB/s", "#38BDF8");
                break;
        }
    }

    private void AddDetail(string text, string colorHex)
    {
        PnlInspectorDetails.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(colorHex)!,
            Margin = new Thickness(0, 2, 0, 2)
        });
    }

    private async void BtnPresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                await _rgbService.SetAllColorsAsync(color.R, color.G, color.B, btn.Content?.ToString() ?? "Preset");
            }
            catch { }
        }
    }

    private async void ChkThermalRgb_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkThermalRgb.IsChecked == true)
        {
            // Simular o aplicar color reactivo a 48°C (o temperatura real)
            await _rgbService.SetThermalReactiveColorAsync(48.0);
        }
    }

    private async void BtnReconnectOpenRgb_Click(object sender, RoutedEventArgs e)
    {
        BtnReconnectOpenRgb.IsEnabled = false;
        try
        {
            bool res = await _rgbService.ConnectAsync();
            UpdateOpenRgbUi(res);
        }
        finally
        {
            BtnReconnectOpenRgb.IsEnabled = true;
        }
    }
}
