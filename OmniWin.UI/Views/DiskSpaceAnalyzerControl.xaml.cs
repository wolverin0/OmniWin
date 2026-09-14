using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class DiskSpaceAnalyzerControl : UserControl
{
    private readonly DiskSpaceAnalyzerService _service = new();
    private CancellationTokenSource? _scanCts;
    private DiskAnalysisResult? _currentResult;

    public DiskSpaceAnalyzerControl()
    {
        InitializeComponent();
        Loaded += DiskSpaceAnalyzerControl_Loaded;
    }

    private void DiskSpaceAnalyzerControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (CmbDrives.Items.Count == 0)
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                    .Select(d => $"{d.Name} ({d.VolumeLabel})")
                    .ToList();

                foreach (var d in drives)
                {
                    CmbDrives.Items.Add(d);
                }

                if (CmbDrives.Items.Count > 0)
                {
                    CmbDrives.SelectedIndex = 0;
                }
            }
            catch
            {
                CmbDrives.Items.Add("C:\\");
                CmbDrives.SelectedIndex = 0;
            }
        }

        CmbDrives.SelectionChanged -= CmbDrives_SelectionChanged;
        CmbDrives.SelectionChanged += CmbDrives_SelectionChanged;
        UpdateBypassIoStatusAsync();
    }

    private void CmbDrives_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBypassIoStatusAsync();
    }

    private async void UpdateBypassIoStatusAsync()
    {
        try
        {
            string selected = CmbDrives.SelectedItem?.ToString() ?? "C:\\";
            string drive = selected.Split(' ')[0].Trim();
            if (drive.Length >= 2) drive = drive.Substring(0, 2);

            var detail = await Task.Run(() => BypassIoService.Instance.CheckVolumeBypassIo(drive));
            Dispatcher.Invoke(() =>
            {
                switch (detail.Status)
                {
                    case BypassIoStatus.Supported:
                        BadgeBypassIo.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                        BadgeBypassIo.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                        TxtBypassIoIcon.Text = "⚡";
                        TxtBypassIoStatus.Text = $"DirectStorage BypassIO: Activo en {drive} (Full HW NVMe Bypass)";
                        TxtBypassIoStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                        break;
                    case BypassIoStatus.UnsupportedOsVersion:
                        BadgeBypassIo.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                        BadgeBypassIo.BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
                        TxtBypassIoIcon.Text = "ℹ️";
                        TxtBypassIoStatus.Text = $"DirectStorage: Modo Estándar NVMe (Windows 10)";
                        TxtBypassIoStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                        break;
                    case BypassIoStatus.NotSupported:
                    case BypassIoStatus.PartiallySupported:
                        BadgeBypassIo.Background = new SolidColorBrush(Color.FromRgb(69, 26, 3));
                        BadgeBypassIo.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                        TxtBypassIoIcon.Text = "⚠️";
                        string reason = detail.BlockingDrivers.Count > 0 ? $"Filtro: {string.Join(", ", detail.BlockingDrivers)}" : "Driver o volumen no compatible";
                        TxtBypassIoStatus.Text = $"DirectStorage BypassIO: Inactivo ({reason})";
                        TxtBypassIoStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                        break;
                    default:
                        BadgeBypassIo.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                        TxtBypassIoIcon.Text = "💾";
                        TxtBypassIoStatus.Text = $"DirectStorage BypassIO: No evaluado en {drive}";
                        TxtBypassIoStatus.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BypassIO UI Error] {ex.Message}");
        }
    }

    private async void BtnStartScan_Click(object sender, RoutedEventArgs e)
    {
        string selected = CmbDrives.SelectedItem?.ToString() ?? "C:\\";
        string root = selected.Split(' ')[0].Trim();
        if (!root.EndsWith("\\")) root += "\\";

        try
        {
            BtnStartScan.IsEnabled = false;
            BtnCancelScan.IsEnabled = true;
            PnlProgress.Visibility = Visibility.Visible;
            _scanCts = new CancellationTokenSource();

            var progress = new Progress<(string CurrentPath, int FilesScanned, long TotalBytes)>(info =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtScanningPath.Text = $"Escaneando: {info.CurrentPath}";
                    double mb = info.TotalBytes / (1024.0 * 1024.0);
                    TxtScanStats.Text = $"{info.FilesScanned:N0} archivos | {mb:N0} MB";
                });
            });

            TxtStatus.Text = $"Iniciando análisis rápido en {root}...";

            _currentResult = await _service.AnalyzeDriveAsync(root, progress, _scanCts.Token);

            DgFolders.ItemsSource = _currentResult.TopFolders;
            DgFiles.ItemsSource = _currentResult.TopLargestFiles;
            DgCategories.ItemsSource = _currentResult.Categories;
            DgSystemFiles.ItemsSource = _currentResult.SystemFiles;

            TxtStatus.Text = $"Escaneo finalizado en {_currentResult.ScanDuration.TotalSeconds:F1}s: {_currentResult.TotalFilesScanned:N0} archivos ({_currentResult.FormattedTotalSize}) analizados en {root}.";
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Escaneo detenido por el usuario.";
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error durante el escaneo: {ex.Message}";
        }
        finally
        {
            BtnStartScan.IsEnabled = true;
            BtnCancelScan.IsEnabled = false;
            PnlProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnCancelScan_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
    }

    private void BtnOpenSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (DgFiles.SelectedItem is DiskFileItem item && File.Exists(item.FullPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.FullPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al abrir explorador: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Selecciona un archivo de la lista para localizarlo en el Explorador de Windows.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
