using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
