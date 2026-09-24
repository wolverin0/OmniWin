using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class AvailableDriveItem
{
    public string RootPath { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string TooltipText { get; set; } = string.Empty;
}

public class ScanTargetItem
{
    public string Path { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public bool IsDrive { get; set; }
}

public class DuplicateFileItem
{
    public string FullPath { get; set; } = string.Empty;
    public string VolumeBadge { get; set; } = string.Empty;
}

public class DuplicateGroupUiItem
{
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public List<DuplicateFileItem> Files { get; set; } = new();
    public long WastedBytes { get; set; }

    public string FileSizeFormatted => FileSizeBytes >= 1024 * 1024
        ? $"{FileSizeBytes / (1024.0 * 1024.0):N1} MB"
        : $"{FileSizeBytes / 1024.0:N1} KB";

    public string WastedFormatted => WastedBytes >= 1024 * 1024
        ? $"{WastedBytes / (1024.0 * 1024.0):N1} MB"
        : $"{WastedBytes / 1024.0:N1} KB";

    public string ShaShort => Sha256Hash.Length >= 8 ? Sha256Hash[..8] + "..." : Sha256Hash;
    public string FileCountText => $"{Files.Count} copias";
}

public partial class DeduplicationControl : UserControl
{
    private readonly DiskDuplicateService _dupService = DiskDuplicateService.Instance;
    private readonly ObservableCollection<ScanTargetItem> _targets = new();
    private readonly List<AvailableDriveItem> _availableDrives = new();
    private List<DuplicateFileGroup> _currentGroups = new();
    private CancellationTokenSource? _scanCts;

    public DeduplicationControl()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        _availableDrives.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;

                string freeGb = (d.TotalFreeSpace / (1024.0 * 1024 * 1024)).ToString("N0");
                string totalGb = (d.TotalSize / (1024.0 * 1024 * 1024)).ToString("N0");
                string driveName = d.Name.TrimEnd('\\');
                string label = string.IsNullOrEmpty(d.VolumeLabel) ? driveName : $"{driveName} ({d.VolumeLabel})";

                _availableDrives.Add(new AvailableDriveItem
                {
                    RootPath = d.Name,
                    DisplayLabel = $"💾 {driveName} ({freeGb} GB libres)",
                    TooltipText = $"{label} [{d.DriveFormat}] - {freeGb} GB libres de {totalGb} GB"
                });
            }
            catch { }
        }

        IcAvailableDrives.ItemsSource = _availableDrives;

        if (_targets.Count == 0)
        {
            string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddTarget(defaultPath, isDrive: false);
        }

        IcSelectedTargets.ItemsSource = _targets;
        UpdateTargetDisplay();
    }

    private void AddTarget(string path, bool isDrive)
    {
        string normalized = Path.GetFullPath(path);
        if (isDrive && normalized.EndsWith(":") || (normalized.Length == 2 && normalized[1] == ':'))
        {
            normalized = normalized.TrimEnd('\\') + "\\";
        }
        else if (!isDrive)
        {
            normalized = normalized.TrimEnd('\\');
        }

        if (_targets.Any(t => t.Path.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return;

        string display = isDrive ? normalized : (Path.GetFileName(normalized) switch
        {
            "" => normalized,
            var name => $"{name} ({normalized})"
        });

        _targets.Add(new ScanTargetItem
        {
            Path = normalized,
            DisplayPath = isDrive ? $"Unidad {normalized}" : display,
            Icon = isDrive ? "💾" : "📁",
            IsDrive = isDrive
        });

        UpdateTargetDisplay();
    }

    private void RemoveTarget(string path)
    {
        var found = _targets.FirstOrDefault(t => t.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            _targets.Remove(found);
            UpdateTargetDisplay();
        }
    }

    private void UpdateTargetDisplay()
    {
        TxtEmptyTargetsPrompt.Visibility = _targets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtTargetCountSummary.Text = _targets.Count == 1
            ? "1 objetivo listo para escanear"
            : $"{_targets.Count} objetivos listos para escanear";
    }

    private void BtnSelectAllDrives_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        foreach (var d in _availableDrives)
        {
            AddTarget(d.RootPath, isDrive: true);
        }
    }

    private void BtnToggleDrive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string rootPath)
        {
            var existing = _targets.FirstOrDefault(t => t.Path.Equals(rootPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _targets.Remove(existing);
                UpdateTargetDisplay();
            }
            else
            {
                AddTarget(rootPath, isDrive: true);
            }
        }
    }

    private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecciona una carpeta para escanear archivos duplicados",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dlg.ShowDialog() == true)
        {
            AddTarget(dlg.FolderName, isDrive: false);
        }
    }

    private void BtnRemoveTarget_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            RemoveTarget(path);
        }
    }

    private void BtnClearTargets_Click(object sender, RoutedEventArgs e)
    {
        _targets.Clear();
        UpdateTargetDisplay();
    }

    private void BtnCancelScan_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        ActiveTaskCoordinator.Instance.CancelTask("dedup_scan");
        TxtDeduplicateStatus.Text = "Cancelando escaneo...";
    }

    private async void BtnStartScan_Click(object sender, RoutedEventArgs e)
    {
        var validTargets = _targets.Select(t => t.Path).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validTargets.Count == 0)
        {
            MessageBox.Show("Por favor selecciona al menos una unidad o carpeta existente para escanear.", "Sin objetivos seleccionados", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        long minBytes = 1048576; // 1 MB default
        if (CbMinSize.SelectedItem is ComboBoxItem cbi && long.TryParse(cbi.Tag?.ToString(), out long parsedMin))
        {
            minBytes = parsedMin;
        }

        try
        {
            BtnStartScan.IsEnabled = false;
            BtnCancelScan.Visibility = Visibility.Visible;
            PbScan.Visibility = Visibility.Visible;
            TxtDeduplicateStatus.Text = $"Iniciando escaneo en {validTargets.Count} objetivo(s)...";

            ActiveTaskCoordinator.Instance.StartTask("dedup_scan", "Deduplicador Zero-Copy", $"Escaneando {validTargets.Count} objetivo(s)...", hubIndex: 2, tabIndex: 6);

            _scanCts = new CancellationTokenSource();
            var progress = new Progress<(int scanned, int found)>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    string msg = $"Archivos analizados: {p.scanned:N0} | Duplicados: {p.found:N0}";
                    TxtDeduplicateStatus.Text = msg;
                    ActiveTaskCoordinator.Instance.UpdateProgress("dedup_scan", msg);
                });
            });

            _currentGroups = await _dupService.FindDuplicatesAsync(validTargets, minBytes, progress, _scanCts.Token);

            var uiGroups = _currentGroups.Select(g => new DuplicateGroupUiItem
            {
                FileSizeBytes = g.FileSizeBytes,
                Sha256Hash = g.Sha256Hash,
                Files = g.FilePaths.Select(fp => new DuplicateFileItem
                {
                    FullPath = fp,
                    VolumeBadge = Path.GetPathRoot(Path.GetFullPath(fp))?.TrimEnd('\\') ?? "Vol"
                }).ToList(),
                WastedBytes = g.WastedBytes
            }).ToList();

            LbDuplicateGroups.ItemsSource = uiGroups;
            TxtGroupCountSummary.Text = $"{uiGroups.Count} grupos";

            long totalWasted = _currentGroups.Sum(g => g.WastedBytes);
            TxtWastedSpaceBadge.Text = $"{totalWasted / (1024.0 * 1024.0):N1} MB RECUPERABLES";
            TxtDeduplicateStatus.Text = $"Escaneo completado: {_currentGroups.Count} grupos duplicados ({totalWasted / (1024.0 * 1024.0):N1} MB desperdiciados).";
            ActiveTaskCoordinator.Instance.CompleteTask("dedup_scan", $"Finalizado: {_currentGroups.Count} grupos detectados");
        }
        catch (OperationCanceledException)
        {
            TxtDeduplicateStatus.Text = "Escaneo cancelado por el usuario.";
            ActiveTaskCoordinator.Instance.CancelTask("dedup_scan");
        }
        catch (Exception ex)
        {
            TxtDeduplicateStatus.Text = $"Error durante el escaneo: {ex.Message}";
            ActiveTaskCoordinator.Instance.CancelTask("dedup_scan");
        }
        finally
        {
            BtnStartScan.IsEnabled = true;
            BtnCancelScan.Visibility = Visibility.Collapsed;
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

        var confirm = MessageBox.Show($"¿Deseas reemplazar los archivos duplicados de los {_currentGroups.Count} grupos por Enlaces Duros (Hardlinks NTFS)?\n\n- Se conservará 1 copia física por cada disco/volumen.\n- Los demás archivos pasarán a ser Hardlinks Zero-Copy, liberando espacio real en disco sin perder los accesos ni carpetas.", "Confirmar Hardlinks Zero-Copy", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            long savedTotal = 0;
            int filesCount = 0;

            foreach (var g in _currentGroups)
            {
                var res = _dupService.DeduplicateGroupVolumeAware(g);
                savedTotal += res.BytesSaved;
                filesCount += res.FilesProcessed;
            }

            MessageBox.Show($"¡Deduplicación Zero-Copy Exitosa!\n\nSe convirtieron {filesCount} archivos a Enlaces Duros NTFS.\nEspacio físico liberado en disco: {savedTotal / (1024.0 * 1024.0):N2} MB.", "Éxito", MessageBoxButton.OK, MessageBoxImage.Information);
            _currentGroups.Clear();
            LbDuplicateGroups.ItemsSource = null;
            TxtGroupCountSummary.Text = "0 grupos";
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
            TxtGroupCountSummary.Text = "0 grupos";
            TxtWastedSpaceBadge.Text = "0 MB RECUPERABLES";
            TxtDeduplicateStatus.Text = $"{delCount} duplicados enviados a la Papelera de Reciclaje.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al eliminar archivos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
