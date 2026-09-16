using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class FileRecoveryControl : UserControl
{
    private readonly FileRecoveryService _recoveryService = new();

    private readonly ObservableCollection<RecycleBinItem> _displayedRecycleItems = new();
    private List<RecycleBinItem> _allRecycleItems = new();

    private readonly ObservableCollection<ShadowCopyInfo> _shadowCopies = new();
    private readonly ObservableCollection<CarvedFile> _carvedFiles = new();

    public FileRecoveryControl()
    {
        InitializeComponent();

        DgRecycleItems.ItemsSource = _displayedRecycleItems;
        DgShadowCopies.ItemsSource = _shadowCopies;
        DgCarvedResults.ItemsSource = _carvedFiles;

        string defaultDest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OmniWin_Carved");
        TxtCarveDest.Text = defaultDest;
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshRecycleBinAsync();
    }

    // =========================================================================
    // 1. PAPELERA FORENSE ($Recycle.Bin)
    // =========================================================================

    private async Task RefreshRecycleBinAsync()
    {
        try
        {
            BtnScanRecycle.IsEnabled = false;
            TxtRecycleStatus.Text = "Analizando $Recycle.Bin en todas las unidades del sistema...";

            var items = await _recoveryService.EnumerateRecycleBinAsync();
            _allRecycleItems = items;

            ApplyRecycleFilter();

            TxtRecycleCountBadge.Text = $"{_allRecycleItems.Count} ARCHIVOS";
            TxtRecycleStatus.Text = $"Análisis completado: {_allRecycleItems.Count} archivos recuperables encontrados en la papelera.";
        }
        catch (Exception ex)
        {
            TxtRecycleStatus.Text = $"Error al escanear papelera: {ex.Message}";
        }
        finally
        {
            BtnScanRecycle.IsEnabled = true;
        }
    }

    private async void BtnScanRecycle_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRecycleBinAsync();
    }

    private void TxtSearchRecycle_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyRecycleFilter();
    }

    private void ApplyRecycleFilter()
    {
        string query = TxtSearchRecycle.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = _allRecycleItems.Where(i =>
            string.IsNullOrEmpty(query) ||
            i.FileName.ToLowerInvariant().Contains(query) ||
            i.OriginalPath.ToLowerInvariant().Contains(query) ||
            i.Extension.ToLowerInvariant().Contains(query)
        ).ToList();

        _displayedRecycleItems.Clear();
        foreach (var item in filtered)
        {
            _displayedRecycleItems.Add(item);
        }
    }

    private async void BtnRestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (DgRecycleItems.SelectedItem is not RecycleBinItem selected)
        {
            MessageBox.Show("Por favor selecciona un archivo de la lista para restaurar.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!selected.IsContentAvailable)
        {
            MessageBox.Show("El contenido binario de este archivo ya no está disponible en la papelera.", "No Disponible", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OmniWin_Recovered");

        var confirm = MessageBox.Show($"¿Deseas restaurar '{selected.FileName}' hacia la carpeta:\n\n{destDir}\n\nSe conservará su fecha y metadatos originales.", "Confirmar Restauración", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            TxtRecycleStatus.Text = $"Restaurando '{selected.FileName}'...";
            bool ok = await _recoveryService.RestoreRecycleBinItemAsync(selected, destDir);

            if (ok)
            {
                TxtRecycleStatus.Text = $"[ÉXITO] Archivo restaurado en: {destDir}\\{selected.FileName}";
                MessageBox.Show($"Archivo restaurado con éxito en:\n\n{destDir}\\{selected.FileName}", "Restauración Completada", MessageBoxButton.OK, MessageBoxImage.Information);
                try { Process.Start(new ProcessStartInfo("explorer.exe", destDir) { UseShellExecute = true }); } catch { }
            }
            else
            {
                TxtRecycleStatus.Text = "[ERROR] No se pudo restaurar el archivo seleccionado.";
                MessageBox.Show("No se pudo completar la restauración.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error durante la restauración: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =========================================================================
    // 2. COPIAS DE VOLUMEN (VSS)
    // =========================================================================

    private async void BtnScanShadows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnScanShadows.IsEnabled = false;
            TxtVssStatus.Text = "Consultando instantáneas Volume Shadow Copy (vssadmin)...";

            var shadows = await _recoveryService.GetShadowCopiesAsync();
            _shadowCopies.Clear();

            foreach (var s in shadows)
            {
                _shadowCopies.Add(s);
            }

            TxtVssStatus.Text = shadows.Count > 0
                ? $"{shadows.Count} instantáneas de volumen encontradas."
                : "No se encontraron copias de sombra activas en el equipo.";
        }
        catch (Exception ex)
        {
            TxtVssStatus.Text = $"Error al consultar VSS: {ex.Message}";
        }
        finally
        {
            BtnScanShadows.IsEnabled = true;
        }
    }

    // =========================================================================
    // 3. DEEP BYTE CARVING
    // =========================================================================

    private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleccionar archivo, imagen o dump para Deep Carving",
            Filter = "Todos los archivos (*.*)|*.*|Archivos de disco e imágenes (*.img;*.bin;*.vhd;*.raw)|*.img;*.bin;*.vhd;*.raw"
        };

        if (dlg.ShowDialog() == true)
        {
            TxtCarveSource.Text = dlg.FileName;
        }
    }

    private void BtnBrowseDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecciona la carpeta donde guardar los archivos recuperados"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtCarveDest.Text = dlg.FolderName;
        }
    }

    private async void BtnStartCarving_Click(object sender, RoutedEventArgs e)
    {
        string srcPath = TxtCarveSource.Text?.Trim() ?? string.Empty;
        string destPath = TxtCarveDest.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
        {
            MessageBox.Show("Por favor selecciona un archivo o volcado de datos existente para analizar.", "Origen Requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrEmpty(destPath))
        {
            MessageBox.Show("Por favor especifica una carpeta de destino válida.", "Destino Requerido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ChkCarveJpg.IsChecked == true) types.Add("jpg");
        if (ChkCarvePng.IsChecked == true) types.Add("png");
        if (ChkCarvePdf.IsChecked == true) types.Add("pdf");
        if (ChkCarveZip.IsChecked == true) types.Add("zip");

        if (types.Count == 0)
        {
            MessageBox.Show("Debes seleccionar al menos un tipo de archivo para buscar.", "Formatos Requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            BtnStartCarving.IsEnabled = false;
            TxtCarvingStatus.Text = "Iniciando tallado binario por firmas mágicas...";
            _carvedFiles.Clear();

            using var stream = File.OpenRead(srcPath);
            var result = await _recoveryService.CarveFilesAsync(stream, destPath, types);

            foreach (var f in result.Files)
            {
                _carvedFiles.Add(f);
            }

            TxtCarvingStatus.Text = $"Tallado completado en {result.Duration.TotalSeconds:N1}s. Total recuperados: {result.TotalFilesCarved} ({result.TotalBytesCarved / 1024.0:N1} KB).";

            if (result.TotalFilesCarved > 0)
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", destPath) { UseShellExecute = true }); } catch { }
            }
        }
        catch (Exception ex)
        {
            TxtCarvingStatus.Text = $"Error durante el tallado: {ex.Message}";
        }
        finally
        {
            BtnStartCarving.IsEnabled = true;
        }
    }
}
