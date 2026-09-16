using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class DuplicateGroupUiItem
{
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public List<string> FilePaths { get; set; } = new();
    public long WastedBytes { get; set; }

    public string FileSizeFormatted => FileSizeBytes >= 1024 * 1024
        ? $"{FileSizeBytes / (1024.0 * 1024.0):N1} MB"
        : $"{FileSizeBytes / 1024.0:N1} KB";

    public string WastedFormatted => WastedBytes >= 1024 * 1024
        ? $"{WastedBytes / (1024.0 * 1024.0):N1} MB"
        : $"{WastedBytes / 1024.0:N1} KB";

    public string ShaShort => Sha256Hash.Length >= 8 ? Sha256Hash[..8] + "..." : Sha256Hash;
}

public partial class DeduplicationControl : UserControl
{
    private readonly DiskDuplicateService _dupService = DiskDuplicateService.Instance;
    private List<DuplicateFileGroup> _currentGroups = new();
    private CancellationTokenSource? _scanCts;

    public DeduplicationControl()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtTargetFolder.Text))
        {
            TxtTargetFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecciona la carpeta para escanear archivos duplicados",
            InitialDirectory = Directory.Exists(TxtTargetFolder.Text) ? TxtTargetFolder.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dlg.ShowDialog() == true)
        {
            TxtTargetFolder.Text = dlg.FolderName;
        }
    }

    private async void BtnStartScan_Click(object sender, RoutedEventArgs e)
    {
        string folder = TxtTargetFolder.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            MessageBox.Show("Por favor especifica una carpeta existente válida para escanear.", "Ruta Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        long minBytes = 102400; // 100 KB
        if (CbMinSize.SelectedItem is ComboBoxItem cbi && long.TryParse(cbi.Tag?.ToString(), out long parsedMin))
        {
            minBytes = parsedMin;
        }

        try
        {
            BtnStartScan.IsEnabled = false;
            PbScan.Visibility = Visibility.Visible;
            TxtDeduplicateStatus.Text = $"Escaneando '{folder}' en busca de duplicados...";

            _scanCts = new CancellationTokenSource();
            var progress = new Progress<(int scanned, int found)>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtDeduplicateStatus.Text = $"Archivos analizados: {p.scanned:N0} | Grupos duplicados detectados: {p.found:N0}...";
                });
            });

            _currentGroups = await _dupService.FindDuplicatesAsync(folder, minBytes, progress, _scanCts.Token);

            var uiGroups = _currentGroups.Select(g => new DuplicateGroupUiItem
            {
                FileSizeBytes = g.FileSizeBytes,
                Sha256Hash = g.Sha256Hash,
                FilePaths = g.FilePaths,
                WastedBytes = g.WastedBytes
            }).ToList();

            LbDuplicateGroups.ItemsSource = uiGroups;

            long totalWasted = _currentGroups.Sum(g => g.WastedBytes);
            TxtWastedSpaceBadge.Text = $"{totalWasted / (1024.0 * 1024.0):N1} MB RECUPERABLES";
            TxtDeduplicateStatus.Text = $"Escaneo completado: {_currentGroups.Count} grupos duplicados ({totalWasted / (1024.0 * 1024.0):N1} MB desperdiciados).";
        }
        catch (OperationCanceledException)
        {
            TxtDeduplicateStatus.Text = "Escaneo cancelado.";
        }
        catch (Exception ex)
        {
            TxtDeduplicateStatus.Text = $"Error durante el escaneo: {ex.Message}";
        }
        finally
        {
            BtnStartScan.IsEnabled = true;
            PbScan.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnHardlinkAll_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroups.Count == 0)
        {
            MessageBox.Show("No hay archivos duplicados para procesar. Realiza un escaneo primero.", "Sin Duplicados", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"¿Deseas reemplazar todos los archivos duplicados de los {_currentGroups.Count} grupos por Enlaces Duros (Hardlinks NTFS)?\n\nAmbos archivos seguirán existiendo normalmente para ti y para los programas, pero compartirán el mismo espacio físico en disco (Zero-Copy).", "Confirmar Hardlinks Zero-Copy", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            long savedTotal = 0;
            int filesCount = 0;

            foreach (var g in _currentGroups)
            {
                string primary = g.FilePaths[0];
                var res = _dupService.DeduplicateGroupWithHardLinks(g, primary);
                savedTotal += res.BytesSaved;
                filesCount += res.FilesProcessed;
            }

            MessageBox.Show($"¡Deduplicación Zero-Copy Exitosa!\n\nSe convirtieron {filesCount} archivos a Hardlinks.\nEspacio físico liberado en disco: {savedTotal / (1024.0 * 1024.0):N2} MB.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            _currentGroups.Clear();
            LbDuplicateGroups.ItemsSource = null;
            TxtWastedSpaceBadge.Text = "0 MB RECUPERABLES";
            TxtDeduplicateStatus.Text = $"Operación completada: {filesCount} archivos deduplicados mediante Hardlinks NTFS.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al procesar Hardlinks: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnDeleteAllDupes_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroups.Count == 0)
        {
            MessageBox.Show("No hay archivos duplicados para eliminar.", "Sin Duplicados", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"¿Deseas mover las copias redundantes de los {_currentGroups.Count} grupos a la Papelera de Reciclaje?\n\nSe conservará siempre el primer archivo de cada grupo.", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            int delCount = 0;
            foreach (var g in _currentGroups)
            {
                for (int i = 1; i < g.FilePaths.Count; i++)
                {
                    if (_dupService.DeleteDuplicateFile(g.FilePaths[i], sendToRecycleBin: true))
                    {
                        delCount++;
                    }
                }
            }

            MessageBox.Show($"Se enviaron {delCount} archivos duplicados a la Papelera de Reciclaje.", "Archivos Eliminados", MessageBoxButton.OK, MessageBoxImage.Information);
            _currentGroups.Clear();
            LbDuplicateGroups.ItemsSource = null;
            TxtWastedSpaceBadge.Text = "0 MB RECUPERABLES";
            TxtDeduplicateStatus.Text = $"{delCount} duplicados enviados a la Papelera de Reciclaje.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al eliminar archivos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
