using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class TweakCardModel : INotifyPropertyChanged
{
    private bool _isApplied;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = "Medio";
    public bool RequiresAdmin { get; set; }
    public bool IsRecommendedForGaming { get; set; }

    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied != value)
            {
                _isApplied = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ActionButtonBrush));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeBg));
                OnPropertyChanged(nameof(StatusBadgeFg));
            }
        }
    }

    public string ActionButtonText => IsApplied ? "Revertir" : "Aplicar";

    public Brush ActionButtonBrush => IsApplied
        ? new SolidColorBrush(Color.FromRgb(185, 28, 28))    // Refined Crimson
        : new SolidColorBrush(Color.FromRgb(2, 132, 199));    // Accent Blue

    public string StatusBadgeText => IsApplied ? "✔ Activo" : "⚪ Por defecto";

    public Brush StatusBadgeBg => IsApplied
        ? new SolidColorBrush(Color.FromRgb(6, 78, 59))      // Dark Green
        : new SolidColorBrush(Color.FromRgb(30, 41, 59));     // Dark Slate

    public Brush StatusBadgeFg => IsApplied
        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))    // Bright Green
        : new SolidColorBrush(Color.FromRgb(148, 163, 184));  // Muted Slate

    public Brush CategoryBadgeBg
    {
        get
        {
            return Category switch
            {
                "Gaming & Latencia" => new SolidColorBrush(Color.FromRgb(59, 7, 100)),
                "Privacidad Radical" => new SolidColorBrush(Color.FromRgb(6, 78, 59)),
                "Windows 11 UI & Explorer" => new SolidColorBrush(Color.FromRgb(12, 74, 110)),
                _ => new SolidColorBrush(Color.FromRgb(69, 26, 3)) // Sistema
            };
        }
    }

    public Brush CategoryBadgeFg
    {
        get
        {
            return Category switch
            {
                "Gaming & Latencia" => new SolidColorBrush(Color.FromRgb(192, 132, 252)),
                "Privacidad Radical" => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                "Windows 11 UI & Explorer" => new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                _ => new SolidColorBrush(Color.FromRgb(251, 191, 36))
            };
        }
    }

    public Brush CategoryBadgeBorder
    {
        get
        {
            return Category switch
            {
                "Gaming & Latencia" => new SolidColorBrush(Color.FromRgb(107, 33, 168)),
                "Privacidad Radical" => new SolidColorBrush(Color.FromRgb(5, 150, 105)),
                "Windows 11 UI & Explorer" => new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                _ => new SolidColorBrush(Color.FromRgb(217, 119, 6))
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class UwpAppCardModel : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isInstalled;
    private string _version = string.Empty;

    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PackagePattern { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconEmoji { get; set; } = "📦";
    public bool IsSafeToRemove { get; set; } = true;

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusFg));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            if (_version != value)
            {
                _version = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusText => IsInstalled ? "✔ Instalada" : "✖ No instalada / Eliminada";

    public Brush StatusFg => IsInstalled
        ? new SolidColorBrush(Color.FromRgb(56, 189, 248))
        : new SolidColorBrush(Color.FromRgb(100, 116, 139));

    public string SafeBadgeText => IsSafeToRemove ? "🛡️ Segura para eliminar" : "⚠️ Opcional";

    public Brush SafeBadgeBg => IsSafeToRemove
        ? new SolidColorBrush(Color.FromRgb(6, 78, 59))
        : new SolidColorBrush(Color.FromRgb(69, 26, 3));

    public Brush SafeBadgeFg => IsSafeToRemove
        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
        : new SolidColorBrush(Color.FromRgb(251, 191, 36));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class TweaksDebloatControl : UserControl
{
    private readonly ExpandedTweakService _tweakService = new();
    private readonly UwpDebloatService _debloatService = new();

    private readonly List<TweakCardModel> _allTweaks = new();
    private readonly ObservableCollection<TweakCardModel> _displayedTweaks = new();
    private readonly ObservableCollection<UwpAppCardModel> _uwpApps = new();

    private string _currentCategory = "Todos";
    private string _currentSearchText = string.Empty;

    public TweaksDebloatControl()
    {
        InitializeComponent();

        IcTweaksList.ItemsSource = _displayedTweaks;
        IcUwpApps.ItemsSource = _uwpApps;

        Loaded += TweaksDebloatControl_Loaded;
    }

    private async void TweaksDebloatControl_Loaded(object sender, RoutedEventArgs e)
    {
        LoadTweaks();
        await LoadUwpAppsAsync();
    }

    // ====================================================
    // TAB 1: 50+ TWEAKS LOGIC
    // ====================================================
    private void LoadTweaks()
    {
        _allTweaks.Clear();
        var rawList = _tweakService.GetCategorizedTweaks();

        foreach (var t in rawList)
        {
            _allTweaks.Add(new TweakCardModel
            {
                Id = t.Id,
                Name = t.Name,
                Category = t.Category,
                Description = t.Description,
                Impact = t.Impact,
                RequiresAdmin = t.RequiresAdmin,
                IsRecommendedForGaming = t.IsRecommendedForGaming,
                IsApplied = t.IsApplied
            });
        }

        FilterTweaks();
        UpdateTweaksStats();
    }

    private void UpdateTweaksStats()
    {
        int appliedCount = _allTweaks.Count(t => t.IsApplied);
        int totalCount = _allTweaks.Count;
        TxtTweaksStats.Text = $"✔ {appliedCount} / {totalCount} Tweaks Activos";
        TxtTweaksCount.Text = $"{_displayedTweaks.Count} de {totalCount} tweaks visibles";
    }

    private void FilterTweaks()
    {
        _displayedTweaks.Clear();

        var query = _allTweaks.AsEnumerable();

        if (_currentCategory != "Todos")
        {
            query = query.Where(t => string.Equals(t.Category, _currentCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(_currentSearchText))
        {
            query = query.Where(t =>
                t.Name.Contains(_currentSearchText, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(_currentSearchText, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(_currentSearchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            _displayedTweaks.Add(item);
        }

        UpdateTweaksStats();
    }

    private void BtnFilterCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string category)
        {
            _currentCategory = category;

            // Update button styles
            ResetCategoryFilterButtons();
            btn.Style = null;
            btn.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // Active accent
            btn.Foreground = Brushes.White;

            FilterTweaks();
        }
    }

    private void ResetCategoryFilterButtons()
    {
        var secondaryStyle = Application.Current.TryFindResource("SecondaryButton") as Style;
        foreach (var b in new[] { BtnFilterAll, BtnFilterGaming, BtnFilterPrivacy, BtnFilterWin11, BtnFilterSystem })
        {
            b.Style = secondaryStyle;
            b.ClearValue(Button.BackgroundProperty);
            b.ClearValue(Button.ForegroundProperty);
        }
    }

    private void TxtSearchTweaks_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentSearchText = TxtSearchTweaks.Text.Trim();
        TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(_currentSearchText)
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilterTweaks();
    }

    private void BtnToggleTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string tweakId)
        {
            var tweak = _allTweaks.FirstOrDefault(t => t.Id == tweakId);
            if (tweak == null) return;

            if (tweak.IsApplied)
            {
                var res = _tweakService.RollbackTweak(tweakId);
                tweak.IsApplied = false;
                TxtTweaksFeedback.Text = $"[REVERTIDO] {res.Message}";
            }
            else
            {
                var res = _tweakService.ApplyTweak(tweakId);
                tweak.IsApplied = res.Success;
                TxtTweaksFeedback.Text = res.Success ? $"[APLICADO] {res.Message}" : $"[FALLO] {res.Message}";
            }

            UpdateTweaksStats();
        }
    }

    private void BtnApplyGamingProfile_Click(object sender, RoutedEventArgs e)
    {
        var resConfirm = MessageBox.Show(
            "¿Deseas aplicar el Perfil Gaming Recomendado?\n\n" +
            "Se optimizarán de forma masiva los 14 parámetros clave de latencia:\n" +
            "• Win32PrioritySeparation (prioridad activa a CPU)\n" +
            "• Desactivación de Algoritmo de Nagle (TcpAckFrequency)\n" +
            "• Desactivación de NetworkThrottling y SystemResponsiveness 0%\n" +
            "• Prioridad de GPU en Games y desactivación de GameDVR\n" +
            "• Desactivación de aceleración de ratón y HPET\n\n" +
            "¿Continuar?",
            "OmniWin — Perfil Gaming Recomendado",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (resConfirm != MessageBoxResult.Yes) return;

        TxtTweaksFeedback.Text = "Creando Snapshot de seguridad y aplicando Perfil Gaming...";
        
        // Auto-create rollback snapshot before applying mass profile
        try
        {
            var activeIds = _allTweaks.Where(t => t.IsRecommendedForGaming).Select(t => t.Id).ToList();
            _ = Task.Run(() => RollbackSnapshotService.Instance.CreateSnapshotAsync("Pre_GamingProfile", "Snapshot automático previo a Perfil Gaming", activeIds, false));
        }
        catch { }

        var results = _tweakService.ApplyGamingProfile();

        int successCount = results.Count(r => r.Success);

        // Refresh tweaks state
        foreach (var t in _allTweaks)
        {
            if (t.IsRecommendedForGaming)
            {
                t.IsApplied = true;
            }
        }

        UpdateTweaksStats();
        TxtTweaksFeedback.Text = $"✔ Perfil Gaming aplicado con éxito: {successCount} ajustes activados. Snapshot creado.";
        MessageBox.Show($"¡Perfil Gaming aplicado con éxito!\n{successCount} tweaks de latencia y gaming han sido configurados.\n\nSe creó un Snapshot de respaldo en caso de que desees hacer Rollback.", "OmniWin Gaming", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnCreateSnapshot_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateSnapshot.IsEnabled = false;
        TxtTweaksFeedback.Text = "Creando Snapshot de Registro y Punto de Restauración...";

        try
        {
            var activeIds = _allTweaks.Where(t => t.IsApplied).Select(t => t.Id).ToList();
            var snapshot = await RollbackSnapshotService.Instance.CreateSnapshotAsync(
                $"Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}",
                $"Snapshot manual con {activeIds.Count} optimizaciones activas",
                activeIds,
                true);

            string restoreMsg = snapshot.HasSystemRestorePoint ? " (+ Punto de Restauración Windows)" : "";
            TxtTweaksFeedback.Text = $"✔ Snapshot '{snapshot.Name}' guardado con éxito{restoreMsg}.";
            MessageBox.Show($"Snapshot guardado correctamente:\n\n• ID: {snapshot.Id}\n• Claves respaldadas: {snapshot.RegistryKeysCount}\n• Punto de restauración: {(snapshot.HasSystemRestorePoint ? "Creado con éxito" : "No disponible")}",
                "OmniWin — Snapshot Creado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtTweaksFeedback.Text = $"Error al crear snapshot: {ex.Message}";
            MessageBox.Show($"Error al crear snapshot: {ex.Message}", "OmniWin", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BtnCreateSnapshot.IsEnabled = true;
        }
    }

    private async void BtnRollbackSnapshot_Click(object sender, RoutedEventArgs e)
    {
        var snapshots = RollbackSnapshotService.Instance.GetSnapshots();
        if (snapshots.Count == 0)
        {
            MessageBox.Show("No se encontraron snapshots guardados para revertir.", "OmniWin Rollback", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var latest = snapshots[0];
        var confirm = MessageBox.Show(
            $"¿Deseas revertir el sistema al estado guardado en el último snapshot?\n\n" +
            $"• Snapshot: {latest.Name}\n" +
            $"• Fecha: {latest.CreatedAt.ToLocalTime():g}\n" +
            $"• Claves a restaurar: {latest.RegistryKeysCount}\n\n" +
            "Se reescribirán las claves de registro originales de políticas, multimedias y kernel.",
            "OmniWin — Confirmar Rollback Inmediato",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        BtnRollbackSnapshot.IsEnabled = false;
        TxtTweaksFeedback.Text = $"Revertiendo snapshot '{latest.Name}'...";

        try
        {
            var result = await RollbackSnapshotService.Instance.RollbackSnapshotAsync(latest.Id);
            if (result.Success)
            {
                TxtTweaksFeedback.Text = $"✔ {result.Message}";
                LoadTweaks(); // Recargar estado de tweaks
                MessageBox.Show(result.Message, "OmniWin Rollback Completado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                TxtTweaksFeedback.Text = $"⚠ Fallo en rollback: {result.Message}";
                MessageBox.Show(result.Message, "OmniWin Rollback", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtTweaksFeedback.Text = $"Error en rollback: {ex.Message}";
            MessageBox.Show($"Error durante el rollback: {ex.Message}", "OmniWin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnRollbackSnapshot.IsEnabled = true;
        }
    }

    // ====================================================
    // TAB 2: UWP DEBLOAT LOGIC
    // ====================================================
    private async Task LoadUwpAppsAsync()
    {
        TxtDebloatConsoleLog.Text = "Escaneando paquetes UWP preinstalados de fábrica...";
        BtnScanUwpApps.IsEnabled = false;

        var catalog = await _debloatService.GetDetectedAppsAsync();

        _uwpApps.Clear();
        foreach (var app in catalog)
        {
            _uwpApps.Add(new UwpAppCardModel
            {
                Id = app.Id,
                DisplayName = app.DisplayName,
                PackagePattern = app.PackagePattern,
                Description = app.Description,
                Category = app.Category,
                IconEmoji = app.IconEmoji,
                IsSafeToRemove = app.IsSafeToRemove,
                IsInstalled = app.IsInstalled,
                IsSelected = app.IsSafeToRemove && app.IsInstalled,
                Version = app.Version
            });
        }

        BtnScanUwpApps.IsEnabled = true;
        int detectedCount = _uwpApps.Count(a => a.IsInstalled);
        TxtDebloatStats.Text = $"{detectedCount} Apps Instaladas";
        TxtDebloatConsoleLog.Text = $"Escaneo completado. {detectedCount} paquetes UWP detectados en el sistema.";
        UpdateSelectedAppsCount();
    }

    private async void BtnScanUwpApps_Click(object sender, RoutedEventArgs e) => await LoadUwpAppsAsync();

    private void BtnSelectAllSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _uwpApps)
        {
            if (app.IsSafeToRemove && app.IsInstalled)
            {
                app.IsSelected = true;
            }
            else if (!app.IsSafeToRemove)
            {
                app.IsSelected = false;
            }
        }
        UpdateSelectedAppsCount();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var app in _uwpApps)
        {
            app.IsSelected = false;
        }
        UpdateSelectedAppsCount();
    }

    private void AppCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSelectedAppsCount();
    }

    private void UpdateSelectedAppsCount()
    {
        int count = _uwpApps.Count(a => a.IsSelected);
        TxtSelectedAppsCount.Text = $"{count} apps seleccionadas";
    }

    private async void BtnUninstallSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _uwpApps.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Por favor selecciona al menos una aplicación para desinstalar.", "OmniWin Debloat", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var resConfirm = MessageBox.Show(
            $"¿Estás seguro de que deseas desinstalar {selected.Count} aplicación(es) seleccionada(s)?\n\n" +
            string.Join("\n", selected.Take(6).Select(s => $"• {s.DisplayName}")) +
            (selected.Count > 6 ? $"\n... y {selected.Count - 6} más" : "") +
            "\n\nLos paquetes se removerán para todos los usuarios y se desaprovisionarán de Windows.",
            "OmniWin — Confirmar Desinstalación UWP",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (resConfirm != MessageBoxResult.Yes) return;

        BtnUninstallSelected.IsEnabled = false;
        BtnScanUwpApps.IsEnabled = false;
        PbDebloatProgress.Visibility = Visibility.Visible;
        PbDebloatProgress.Value = 0;

        var progress = new Progress<DebloatProgressUpdate>(p =>
        {
            PbDebloatProgress.Value = p.Percent;
            TxtDebloatConsoleLog.Text = $"[{p.CurrentIndex}/{p.TotalCount}] {p.Message}";
        });

        var appItems = selected.Select(s => new UwpAppItem
        {
            Id = s.Id,
            DisplayName = s.DisplayName,
            PackagePattern = s.PackagePattern,
            IsInstalled = s.IsInstalled
        }).ToList();

        var result = await _debloatService.UninstallSelectedAppsAsync(appItems, progress);

        PbDebloatProgress.Visibility = Visibility.Collapsed;
        BtnUninstallSelected.IsEnabled = true;
        BtnScanUwpApps.IsEnabled = true;

        TxtDebloatConsoleLog.Text = result.Summary + "\n" + string.Join(" | ", result.LogMessages);
        MessageBox.Show(result.Summary, "OmniWin Debloat", MessageBoxButton.OK, MessageBoxImage.Information);

        await LoadUwpAppsAsync();
    }

    private void BtnExportTweaksReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var reportModel = new OptimizationReportModel
            {
                SelectedProfile = AppSettingsService.Instance.Settings.SelectedProfile,
                RestorePointCreated = true,
                RamPurgedBytes = 0,
                Tweaks = _tweakService.GetCategorizedTweaks().Select(t => new ReportTweakEntry
                {
                    Category = t.Category,
                    Name = t.Name,
                    Description = t.Description,
                    RegistryPath = t.Id,
                    PreviousValue = "Default",
                    NewValue = t.IsApplied ? "1 (Optimizado)" : "0 (Por defecto)",
                    Success = t.IsApplied
                }).ToList()
            };
            string path = OptimizationReportService.Instance.GenerateHtmlReport(reportModel);
            TxtTweaksFeedback.Text = $"Informe HTML generado: {System.IO.Path.GetFileName(path)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            TxtTweaksFeedback.Text = $"Error al generar reporte: {ex.Message}";
        }
    }

    private void BtnExportSnapshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exportar Snapshot de Configuración OmniWin",
                Filter = "Paquete OmniWin (*.omniwin)|*.omniwin",
                FileName = $"OmniWin_Config_{Environment.MachineName}_{DateTime.Now:yyyyMMdd}.omniwin"
            };

            if (sfd.ShowDialog() == true)
            {
                string target = SystemSnapshotMigrationService.Instance.ExportSnapshot(sfd.FileName);
                TxtTweaksFeedback.Text = $"[SNAPSHOT EXPORTADO] {System.IO.Path.GetFileName(target)} guardado con éxito.";
                MessageBox.Show(
                    $"Configuración exportada correctamente.\n\nArchivo: {target}\n\nEste archivo contiene el estado de tus optimizaciones, reglas ASR y preferencias para clonar en otros equipos.",
                    "Exportación Exitosa",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            TxtTweaksFeedback.Text = $"Error al exportar: {ex.Message}";
            MessageBox.Show($"Error al exportar snapshot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnImportSnapshot_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Seleccionar Paquete de Configuración OmniWin (.omniwin)",
                Filter = "Paquete OmniWin (*.omniwin)|*.omniwin"
            };

            if (ofd.ShowDialog() == true)
            {
                var confirm = MessageBox.Show(
                    $"¿Deseas aplicar el archivo de configuración '{ofd.SafeFileName}'?\n\nSe creará un Punto de Restauración del Sistema (VSS) antes de realizar cualquier cambio.",
                    "Confirmar Importación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    TxtTweaksFeedback.Text = "Importando snapshot y creando punto de restauración...";
                    var (success, message, tweaksApplied) = await Task.Run(() => SystemSnapshotMigrationService.Instance.ImportSnapshot(ofd.FileName));

                    if (success)
                    {
                        LoadTweaks();
                        TxtTweaksFeedback.Text = $"[SNAPSHOT APLICADO] {tweaksApplied} tweaks restaurados.";
                        MessageBox.Show(
                            $"¡Snapshot restaurado con éxito!\n\n• Tweaks aplicados: {tweaksApplied}\n• Punto de restauración VSS creado preventivamente.\n\nDetalle: {message}",
                            "Importación Exitosa",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        TxtTweaksFeedback.Text = $"Error al importar: {message}";
                        MessageBox.Show($"No se pudo aplicar el snapshot: {message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TxtTweaksFeedback.Text = $"Error: {ex.Message}";
            MessageBox.Show($"Error al importar snapshot: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
