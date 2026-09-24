using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

public partial class SoftwareDriversControl : UserControl
{
    private readonly SoftwareService _softwareService = new();
    private readonly DriverService _driverService = new();
    private readonly SoftwareUninstallerService _uninstallerService = SoftwareUninstallerService.Instance;
    private readonly MotherboardBiosService _biosService = MotherboardBiosService.Instance;

    private readonly ObservableCollection<UpgradablePackage> _upgradesList = new();
    private List<DriverPackageInfo> _allDrivers = new();
    private List<InstalledDesktopApp> _allApps = new();
    private CancellationTokenSource? _actionCts;

    public SoftwareDriversControl()
    {
        InitializeComponent();

        DgUpgrades.ItemsSource = _upgradesList;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAllDataAsync();
    }

    private async void RefreshAllDataAsync()
    {
        await Task.WhenAll(
            RefreshSoftwareUpdatesAsync(),
            RefreshDriversAsync(),
            RefreshInstalledAppsAsync(),
            RefreshBiosAndRamAsync()
        );
    }

    // =========================================================================
    // 1. SOFTWARE UPDATES & QUICK APP STORE
    // =========================================================================

    private async Task RefreshSoftwareUpdatesAsync()
    {
        try
        {
            BtnCheckUpdates.IsEnabled = false;
            BtnCheckUpdates.Content = "⏳ Buscando...";
            _upgradesList.Clear();
            TxtNoUpgrades.Visibility = Visibility.Collapsed;

            var apps = await _softwareService.GetUpgradableAppsAsync();
            foreach (var app in apps)
            {
                _upgradesList.Add(app);
            }

            TxtUpgradesBadge.Text = $"{_upgradesList.Count} ACTUALIZACIONES";
            TxtNoUpgrades.Visibility = _upgradesList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            TxtConsoleOutput.Text = $"[Error buscando actualizaciones]: {ex.Message}";
        }
        finally
        {
            BtnCheckUpdates.IsEnabled = true;
            BtnCheckUpdates.Content = "🔄 Buscar Actualizaciones";
        }
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSoftwareUpdatesAsync();
    }

    private async void BtnUpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnUpgradeAll.IsEnabled = false;
            TxtConsoleOutput.Text = "Iniciando actualización masiva de todos los paquetes vía WinGet...\n";
            _actionCts = new CancellationTokenSource();

            string res = await _softwareService.UpgradeAllAsync(line =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtConsoleOutput.AppendText(line + "\n");
                    TxtConsoleOutput.ScrollToEnd();
                });
            }, _actionCts.Token);

            TxtConsoleOutput.AppendText("\n✔ Proceso de actualización finalizado.\n");
            await RefreshSoftwareUpdatesAsync();
        }
        catch (Exception ex)
        {
            TxtConsoleOutput.AppendText($"\n[Error]: {ex.Message}\n");
        }
        finally
        {
            BtnUpgradeAll.IsEnabled = true;
        }
    }

    private async void BtnUpgradeSingle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is UpgradablePackage pkg)
        {
            try
            {
                btn.IsEnabled = false;
                TxtConsoleOutput.Text = $"Actualizando {pkg.Name} ({pkg.Id})...\n";
                _actionCts = new CancellationTokenSource();

                await _softwareService.UpgradePackageAsync(pkg.Id, line =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtConsoleOutput.AppendText(line + "\n");
                        TxtConsoleOutput.ScrollToEnd();
                    });
                }, _actionCts.Token);

                TxtConsoleOutput.AppendText($"\n✔ {pkg.Name} actualizado.\n");
                await RefreshSoftwareUpdatesAsync();
            }
            catch (Exception ex)
            {
                TxtConsoleOutput.AppendText($"\n[Error]: {ex.Message}\n");
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async void BtnInstallSelectedApps_Click(object sender, RoutedEventArgs e)
    {
        var selectedTags = new List<string>();
        foreach (var child in WpEssentialApps.Children)
        {
            if (child is CheckBox cb && cb.IsChecked == true && cb.Tag is string tag)
            {
                selectedTags.Add(tag);
            }
        }

        if (selectedTags.Count == 0)
        {
            MessageBox.Show("Selecciona al menos una aplicación de la lista para instalar.", "Instalador Ninite", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            BtnInstallSelectedApps.IsEnabled = false;
            TxtConsoleOutput.Text = $"Iniciando instalación de {selectedTags.Count} aplicaciones seleccionadas...\n";
            _actionCts = new CancellationTokenSource();

            foreach (var packageId in selectedTags)
            {
                TxtConsoleOutput.AppendText($"\n--- Instalando {packageId} ---\n");
                await _softwareService.InstallPackageAsync(packageId, line =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtConsoleOutput.AppendText(line + "\n");
                        TxtConsoleOutput.ScrollToEnd();
                    });
                }, _actionCts.Token);
            }

            TxtConsoleOutput.AppendText("\n✔ Instalación de aplicaciones completada.\n");
        }
        catch (Exception ex)
        {
            TxtConsoleOutput.AppendText($"\n[Error durante la instalación]: {ex.Message}\n");
        }
        finally
        {
            BtnInstallSelectedApps.IsEnabled = true;
        }
    }

    // =========================================================================
    // 2. DRIVERSTORE OEM DRIVER MANAGER
    // =========================================================================

    private async Task RefreshDriversAsync()
    {
        try
        {
            TxtDriverStatus.Text = "Leyendo controladores instalados en el DriverStore vía PnPUtil...";
            _allDrivers = await _driverService.GetOemDriversAsync();
            FilterDrivers();
            TxtDriverStatus.Text = $"{_allDrivers.Count} controlador(es) OEM de terceros encontrados en DriverStore.";
        }
        catch (Exception ex)
        {
            TxtDriverStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void FilterDrivers()
    {
        string filter = TxtDriverSearch.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            DgDrivers.ItemsSource = _allDrivers;
        }
        else
        {
            DgDrivers.ItemsSource = _allDrivers.Where(d => 
                d.PublishedName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                d.OriginalName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                d.Provider.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                d.ClassName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private void TxtDriverSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterDrivers();
    }

    private async void BtnRefreshDrivers_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDriversAsync();
    }

    private async void BtnDeleteSelectedDriver_Click(object sender, RoutedEventArgs e)
    {
        if (DgDrivers.SelectedItem is DriverPackageInfo driver)
        {
            var res = MessageBox.Show($"¿Deseas eliminar permanentemente el paquete de controlador '{driver.PublishedName}' ({driver.OriginalName} - {driver.Provider})?\n\nEsta acción liberará espacio en DriverStore.", "Confirmar Eliminación", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res != MessageBoxResult.Yes) return;

            try
            {
                TxtDriverStatus.Text = $"Eliminando {driver.PublishedName}...";
                string output = await _driverService.DeleteDriverAsync(driver.PublishedName, true);
                MessageBox.Show(output, "Resultado PnPUtil", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshDriversAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Por favor, selecciona un controlador de la lista para eliminar.", "Selección Requerida", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnExportDrivers_Click(object sender, RoutedEventArgs e)
    {
        string defaultFolder = @"C:\OmniWin_DriverBackup";
        var res = MessageBox.Show($"¿Deseas exportar una copia de seguridad de TODOS los controladores OEM del sistema a la carpeta:\n\n{defaultFolder}\n\nEsta copia contendrá los archivos .inf, .sys y .cat necesarios para reinstalar Windows sin perder controladores.", "Copia de Seguridad de Controladores", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        try
        {
            BtnExportDrivers.IsEnabled = false;
            TxtDriverStatus.Text = "Exportando controladores OEM con pnputil /export-driver (esto puede tardar unos momentos)...";

            var progress = new Progress<string>(msg =>
            {
                Dispatcher.Invoke(() => TxtDriverStatus.Text = msg);
            });

            var result = await _driverService.ExportDriversAsync(defaultFolder, progress);
            if (result.Success)
            {
                MessageBox.Show($"{result.Message}\n\nUbicación: {defaultFolder}", "Copia de Seguridad Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
                try { Process.Start(new ProcessStartInfo("explorer.exe", defaultFolder) { UseShellExecute = true }); } catch { }
            }
            else
            {
                MessageBox.Show(result.Message, "Error al Exportar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnExportDrivers.IsEnabled = true;
            await RefreshDriversAsync();
        }
    }

    private async void BtnRestoreDrivers_Click(object sender, RoutedEventArgs e)
    {
        string defaultFolder = @"C:\OmniWin_DriverBackup";
        if (!System.IO.Directory.Exists(defaultFolder))
        {
            MessageBox.Show($"No se encontró la carpeta predeterminada de respaldos:\n{defaultFolder}\n\nPrimero realiza una exportación de controladores o coloca tus controladores .inf en dicha carpeta.", "Carpeta no encontrada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var res = MessageBox.Show($"¿Deseas instalar/restaurar todos los controladores ubicados en:\n{defaultFolder}?\n\nWindows PnPUtil instalará recursivamente todos los paquetes .inf encontrados.", "Restauración de Controladores", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        try
        {
            BtnRestoreDrivers.IsEnabled = false;
            TxtDriverStatus.Text = "Restaurando e instalando controladores OEM...";

            var progress = new Progress<string>(msg =>
            {
                Dispatcher.Invoke(() => TxtDriverStatus.Text = msg);
            });

            var result = await _driverService.RestoreDriversAsync(defaultFolder, progress);
            MessageBox.Show(result.Message, "Resultado de Restauración", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRestoreDrivers.IsEnabled = true;
            await RefreshDriversAsync();
        }
    }

    // =========================================================================
    // 3. DEEP UNINSTALLER & LEFTOVER HUNTER
    // =========================================================================

    private async Task RefreshInstalledAppsAsync()
    {
        try
        {
            TxtUninstallStatus.Text = "Escaneando programas instalados en el Registro (64-bit y 32-bit)...";
            _allApps = await _uninstallerService.GetInstalledAppsAsync();
            FilterApps();
            TxtUninstallStatus.Text = $"{_allApps.Count} aplicaciones de escritorio encontradas.";
        }
        catch (Exception ex)
        {
            TxtUninstallStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void FilterApps()
    {
        string filter = TxtAppSearch.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            DgInstalledApps.ItemsSource = _allApps;
        }
        else
        {
            DgInstalledApps.ItemsSource = _allApps.Where(a => 
                a.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private void TxtAppSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterApps();
    }

    private async void BtnRefreshApps_Click(object sender, RoutedEventArgs e)
    {
        await RefreshInstalledAppsAsync();
    }

    private async void BtnUninstallApp_Click(object sender, RoutedEventArgs e)
    {
        if (DgInstalledApps.SelectedItem is InstalledDesktopApp app)
        {
            var res = MessageBox.Show($"¿Deseas iniciar la desinstalación de '{app.DisplayName}'?\n\nComando: {app.UninstallString}", "Desinstalar Programa", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;

            TxtUninstallStatus.Text = $"Ejecutando desinstalador para {app.DisplayName}...";
            var result = await _uninstallerService.LaunchUninstallerExAsync(app, false);

            if (result.Success)
            {
                TxtUninstallStatus.Text = $"Desinstalador completado para {app.DisplayName}. Se recomienda ejecutar 'Escanear Rastros' para limpiar sobras residuales.";
                await RefreshInstalledAppsAsync();
            }
            else if (result.ExeNotFound)
            {
                var removeGhost = MessageBox.Show(
                    $"El ejecutable del desinstalador no fue encontrado en el disco:\n\n\"{result.ExecutablePath}\"\n\nEsta es una entrada huérfana de registro (el programa fue borrado manualmente o la instalación quedó corrupta).\n\n¿Deseas que OmniWin elimine esta entrada fantasma del registro de Windows para que no vuelva a aparecer en la lista?",
                    "Entrada Fantasma / Desinstalador No Encontrado",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (removeGhost == MessageBoxResult.Yes)
                {
                    bool removed = _uninstallerService.ForceRemoveAppRegistryEntry(app);
                    if (removed)
                    {
                        MessageBox.Show($"La entrada '{app.DisplayName}' fue eliminada del registro de Windows exitosamente.", "Entrada Eliminada", MessageBoxButton.OK, MessageBoxImage.Information);
                        TxtUninstallStatus.Text = $"✔ Entrada huérfana '{app.DisplayName}' eliminada del registro.";
                        await RefreshInstalledAppsAsync();
                    }
                    else
                    {
                        MessageBox.Show("No se pudo eliminar la clave de registro. Asegúrate de ejecutar OmniWin como Administrador.", "Error de Permisos", MessageBoxButton.OK, MessageBoxImage.Error);
                        TxtUninstallStatus.Text = "Error: no se pudo eliminar la clave de registro (se requieren permisos de Administrador).";
                    }
                }
                else
                {
                    TxtUninstallStatus.Text = "Operación cancelada.";
                }
            }
            else
            {
                TxtUninstallStatus.Text = $"Error ejecutando desinstalador: {result.ErrorMessage}";
                MessageBox.Show($"Error ejecutando el desinstalador:\n\n{result.ErrorMessage}", "Fallo al Iniciar Desinstalador", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Selecciona un programa de la tabla.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnScanLeftovers_Click(object sender, RoutedEventArgs e)
    {
        if (DgInstalledApps.SelectedItem is InstalledDesktopApp app)
        {
            TxtUninstallStatus.Text = $"Buscando carpetas y claves huérfanas de '{app.DisplayName}'...";
            var leftovers = _uninstallerService.ScanLeftovers(app);

            int totalItems = leftovers.LeftoverDirectories.Count + leftovers.LeftoverRegistryKeys.Count;
            if (totalItems == 0)
            {
                MessageBox.Show($"No se encontraron carpetas ni claves residuales obvias para '{app.DisplayName}'. ¡El sistema está limpio!", "Sin Rastros", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtUninstallStatus.Text = "Sin rastros residuales detectados.";
                return;
            }

            string msg = $"Se encontraron {leftovers.LeftoverDirectories.Count} carpeta(s) y {leftovers.LeftoverRegistryKeys.Count} clave(s) de registro asociadas:\n\n" +
                         string.Join("\n", leftovers.LeftoverDirectories.Take(5)) +
                         (leftovers.LeftoverDirectories.Count > 5 ? $"\n... y {leftovers.LeftoverDirectories.Count - 5} más" : "") +
                         "\n\n¿Deseas purgar y eliminar permanentemente estos rastros?";

            var res = MessageBox.Show(msg, "Limpiador de Rastros Estilo Revo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                int purged = _uninstallerService.PurgeLeftovers(leftovers);
                MessageBox.Show($"Se eliminaron {purged} elementos residuales exitosamente.", "Limpieza Completada", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtUninstallStatus.Text = $"✔ {purged} rastros purgados correctamente.";
            }
        }
        else
        {
            MessageBox.Show("Selecciona un programa para analizar sus rastros.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // =========================================================================
    // 4. BIOS, MOTHERBOARD & RAM TELEMETRY
    // =========================================================================

    private async Task RefreshBiosAndRamAsync()
    {
        try
        {
            var info = await _biosService.GetInfoAsync();

            // Motherboard
            TxtMbProduct.Text = info.MotherboardProduct;
            TxtMbManufacturer.Text = $"Fabricante: {info.MotherboardManufacturer}";
            TxtMbVersion.Text = string.IsNullOrEmpty(info.MotherboardVersion) ? "Revisión: Estándar" : $"Versión / Rev: {info.MotherboardVersion}";

            // BIOS
            TxtBiosVersion.Text = $"BIOS {info.BiosVersion}";
            TxtBiosVendor.Text = $"Proveedor: {info.BiosVendor}";
            TxtBiosDate.Text = string.IsNullOrEmpty(info.BiosReleaseDate) ? "Fecha: Desconocida" : $"Lanzamiento: {info.BiosReleaseDate}";

            // Secure Boot & TPM
            TxtSecureBootStatus.Text = info.IsSecureBootEnabled ? "Secure Boot: Activo ✔" : "Secure Boot: Desactivado";
            BadgeSecureBoot.Background = new SolidColorBrush(info.IsSecureBootEnabled ? Color.FromRgb(6, 78, 59) : Color.FromRgb(30, 41, 59));

            TxtTpmStatus.Text = info.IsTpmPresent ? $"TPM {info.TpmVersion}: Habilitado ✔" : "TPM: No Detectado";
            BadgeTpm.Background = new SolidColorBrush(info.IsTpmPresent ? Color.FromRgb(6, 78, 59) : Color.FromRgb(120, 53, 15));

            // RAM
            TxtRamTotalHeader.Text = $"Total: {info.TotalRamGb:F0} GB ({info.RamModules.Count} módulos)";
            TxtRamTopologySummary.Text = $"Topología: {info.ChannelMode} • {info.RamModules.Count} ranura(s) DIMM ocupadas";
            IcRamModules.ItemsSource = info.RamModules;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RefreshBiosAndRam Error]: {ex.Message}");
        }
    }

    private async void BtnRefreshBios_Click(object sender, RoutedEventArgs e)
    {
        await RefreshBiosAndRamAsync();
    }
}
