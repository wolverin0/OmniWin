using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class UsbDoctorControl : UserControl
{
    private readonly UsbDoctorService _usbService = new();
    private List<UsbDriveItem> _drives = new();
    private UsbDriveItem? _selectedDrive;
    private CancellationTokenSource? _actionCts;

    public UsbDoctorControl()
    {
        InitializeComponent();
    }

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshDrivesAsync();
    }

    private async void BtnRefreshDrives_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDrivesAsync();
    }

    private async Task RefreshDrivesAsync()
    {
        try
        {
            TxtFooterStatus.Text = "Detectando unidades extraíbles conectadas...";
            _drives = await Task.Run(() => _usbService.GetRemovableDrives());
            LbDrives.ItemsSource = _drives;
            TxtDrivesBadge.Text = $"{_drives.Count} UNIDADES";

            if (_drives.Count > 0)
            {
                LbDrives.SelectedIndex = 0;
            }
            else
            {
                TxtLocksSummary.Text = "No se detectaron unidades extraíbles USB en este momento.";
                IcLocks.ItemsSource = null;
            }
            TxtFooterStatus.Text = $"Se encontraron {_drives.Count} dispositivo(s) de almacenamiento.";
        }
        catch (Exception ex)
        {
            TxtFooterStatus.Text = $"Error al listar unidades: {ex.Message}";
        }
    }

    private void LbDrives_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LbDrives.SelectedItem is UsbDriveItem drive)
        {
            _selectedDrive = drive;
            RefreshLockingProcesses(drive.DriveLetter);
        }
    }

    private void RefreshLockingProcesses(string driveLetter)
    {
        try
        {
            var locks = _usbService.GetLockingProcesses(driveLetter);
            IcLocks.ItemsSource = locks;

            if (locks.Count == 0)
            {
                TxtLocksSummary.Text = $"✔ La unidad {driveLetter} no tiene archivos bloqueados. Es seguro expulsarla.";
            }
            else
            {
                TxtLocksSummary.Text = $"⚠️ {locks.Count} proceso(s) están utilizando archivos en {driveLetter}:";
            }
        }
        catch (Exception ex)
        {
            TxtLocksSummary.Text = $"Error al inspeccionar bloqueos: {ex.Message}";
        }
    }

    private async void BtnEjectSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null)
        {
            MessageBox.Show("Selecciona una unidad de la lista para expulsar.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            TxtFooterStatus.Text = $"Expulsando {_selectedDrive.DriveLetter}...";
            var res = await _usbService.SafelyEjectDriveAsync(_selectedDrive.DriveLetter, forceCloseProcesses: false);

            if (res.Success)
            {
                MessageBox.Show(res.Message, "Expulsión Segura", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshDrivesAsync();
            }
            else
            {
                MessageBox.Show($"{res.Message}\n\nUsa 'Forzar Cierre & Expulsar' si deseas cerrar los procesos automáticamente.", "Unidad Ocupada", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshLockingProcesses(_selectedDrive.DriveLetter);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al expulsar: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnForceEject_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null)
        {
            MessageBox.Show("Selecciona una unidad de la lista para expulsar.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"¿Deseas forzar el cierre de todos los procesos que bloquean {_selectedDrive.DriveLetter} y desmontar la unidad?", "Confirmar Cierre Forzado", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            TxtFooterStatus.Text = $"Terminando bloqueos y expulsando {_selectedDrive.DriveLetter}...";
            var res = await _usbService.SafelyEjectDriveAsync(_selectedDrive.DriveLetter, forceCloseProcesses: true);
            MessageBox.Show(res.Message, res.Success ? "Expulsión Exitosa" : "Resultado", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await RefreshDrivesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRunFlashTest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null)
        {
            MessageBox.Show("Selecciona una unidad USB para verificar.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int gb = 1;
        if (CbTestSize.SelectedItem is ComboBoxItem cbi && int.TryParse(cbi.Tag?.ToString(), out int parsedGb))
        {
            gb = parsedGb;
        }
        long testBytes = (long)gb * 1024L * 1024 * 1024;

        var confirm = MessageBox.Show($"Se escribirán y verificarán {gb} GB de datos de prueba en {_selectedDrive.DriveLetter}.\n\nNo desconectes el pendrive durante la prueba. ¿Continuar?", "Test de Integridad Flash", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            BtnRunFlashTest.IsEnabled = false;
            PbFlashTest.Visibility = Visibility.Visible;
            PbFlashTest.IsIndeterminate = true;
            TxtFlashResult.Text = "Escribiendo patrones y verificando celdas NAND...";

            _actionCts = new CancellationTokenSource();
            var progress = new Progress<(long verifiedBytes, double speedMBs)>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    PbFlashTest.IsIndeterminate = false;
                    double pct = testBytes > 0 ? (p.verifiedBytes / (double)testBytes) * 100.0 : 0;
                    PbFlashTest.Value = Math.Min(100, pct);
                    TxtFlashResult.Text = $"Verificados: {p.verifiedBytes / (1024.0 * 1024.0):N1} MB | Velocidad: {p.speedMBs:N1} MB/s";
                });
            });

            var res = await _usbService.ValidateFlashCapacityAsync(_selectedDrive.DriveLetter, testBytes, progress, _actionCts.Token);
            PbFlashTest.Visibility = Visibility.Collapsed;

            if (res.Passed)
            {
                MessageBox.Show(res.Details, "Test Superado ✔", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtFlashResult.Text = "✔ " + res.Details;
            }
            else
            {
                MessageBox.Show(res.Details, "¡ALERTA DE FRAUDE / ERROR!", MessageBoxButton.OK, MessageBoxImage.Error);
                TxtFlashResult.Text = "❌ " + res.Details;
            }
        }
        catch (Exception ex)
        {
            TxtFlashResult.Text = $"Error durante el test: {ex.Message}";
        }
        finally
        {
            BtnRunFlashTest.IsEnabled = true;
            PbFlashTest.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnWipeFreeSpace_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null)
        {
            MessageBox.Show("Selecciona una unidad para purgar el espacio libre.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show($"Se llenará el espacio libre de {_selectedDrive.DriveLetter} con ceros para destruir rastros de archivos viejos.\n\nEsto no borrará tus archivos actuales, pero puede tardar unos minutos. ¿Deseas continuar?", "Confirmar Purga Zero-Fill", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            BtnWipeFreeSpace.IsEnabled = false;
            TxtWipeStatus.Text = "Sobrescribiendo espacio libre con ceros...";

            _actionCts = new CancellationTokenSource();
            var progress = new Progress<double>(pct =>
            {
                Dispatcher.Invoke(() => TxtWipeStatus.Text = $"Progreso de sobrescritura: {pct:N1}%");
            });

            var res = await _usbService.WipeFreeSpaceAsync(_selectedDrive.DriveLetter, progress, _actionCts.Token);
            MessageBox.Show(res.Message, "Purga Completada", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtWipeStatus.Text = "✔ " + res.Message;
        }
        catch (Exception ex)
        {
            TxtWipeStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnWipeFreeSpace.IsEnabled = true;
        }
    }
}
