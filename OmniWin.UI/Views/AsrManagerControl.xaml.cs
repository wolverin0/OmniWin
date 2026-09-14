using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class AsrRuleViewModel : INotifyPropertyChanged
{
    private AsrRuleAction _action;
    private bool _isBusy;

    public string Guid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string MitigationType { get; set; } = string.Empty;

    public AsrRuleAction Action
    {
        get => _action;
        set
        {
            if (_action != value)
            {
                _action = value;
                OnPropertyChanged(nameof(Action));
                OnPropertyChanged(nameof(IsBlocked));
                OnPropertyChanged(nameof(IsAudited));
                OnPropertyChanged(nameof(IsDisabled));
                OnPropertyChanged(nameof(StatusBadgeText));
                OnPropertyChanged(nameof(StatusBadgeBackground));
                OnPropertyChanged(nameof(StatusBadgeForeground));
                OnPropertyChanged(nameof(DisabledButtonBackground));
                OnPropertyChanged(nameof(DisabledButtonForeground));
                OnPropertyChanged(nameof(AuditButtonBackground));
                OnPropertyChanged(nameof(AuditButtonForeground));
                OnPropertyChanged(nameof(BlockButtonBackground));
                OnPropertyChanged(nameof(BlockButtonForeground));
            }
        }
    }

    public bool IsBlocked => Action == AsrRuleAction.Block;
    public bool IsAudited => Action == AsrRuleAction.Audit;
    public bool IsDisabled => Action == AsrRuleAction.Disabled;

    public string StatusBadgeText => Action switch
    {
        AsrRuleAction.Block => "Bloquear",
        AsrRuleAction.Audit => "Auditar",
        _ => "Desactivado"
    };

    public Brush StatusBadgeBackground => Action switch
    {
        AsrRuleAction.Block => new SolidColorBrush(Color.FromRgb(6, 78, 59)),
        AsrRuleAction.Audit => new SolidColorBrush(Color.FromRgb(120, 53, 15)),
        _ => new SolidColorBrush(Color.FromRgb(30, 41, 59))
    };

    public Brush StatusBadgeForeground => Action switch
    {
        AsrRuleAction.Block => new SolidColorBrush(Color.FromRgb(52, 211, 153)),
        AsrRuleAction.Audit => new SolidColorBrush(Color.FromRgb(251, 191, 36)),
        _ => new SolidColorBrush(Color.FromRgb(148, 163, 184))
    };

    public Brush CategoryBadgeBackground => Category switch
    {
        "Office & Aplicaciones" => new SolidColorBrush(Color.FromRgb(30, 58, 138)),
        "Credenciales & Sistema" => new SolidColorBrush(Color.FromRgb(88, 28, 135)),
        "Protección Malware" => new SolidColorBrush(Color.FromRgb(136, 19, 55)),
        "Scripts & Intérpretes" => new SolidColorBrush(Color.FromRgb(124, 45, 18)),
        _ => new SolidColorBrush(Color.FromRgb(30, 41, 59))
    };

    public Brush CategoryBadgeForeground => Category switch
    {
        "Office & Aplicaciones" => new SolidColorBrush(Color.FromRgb(147, 197, 253)),
        "Credenciales & Sistema" => new SolidColorBrush(Color.FromRgb(216, 180, 254)),
        "Protección Malware" => new SolidColorBrush(Color.FromRgb(253, 164, 175)),
        "Scripts & Intérpretes" => new SolidColorBrush(Color.FromRgb(253, 186, 116)),
        _ => new SolidColorBrush(Color.FromRgb(203, 213, 225))
    };

    // Segmented Button Colors:
    public Brush DisabledButtonBackground => IsDisabled ? new SolidColorBrush(Color.FromRgb(51, 65, 85)) : Brushes.Transparent;
    public Brush DisabledButtonForeground => IsDisabled ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 116, 139));

    public Brush AuditButtonBackground => IsAudited ? new SolidColorBrush(Color.FromRgb(217, 119, 6)) : Brushes.Transparent;
    public Brush AuditButtonForeground => IsAudited ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 116, 139));

    public Brush BlockButtonBackground => IsBlocked ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : Brushes.Transparent;
    public Brush BlockButtonForeground => IsBlocked ? Brushes.White : new SolidColorBrush(Color.FromRgb(100, 116, 139));

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

/// <summary>
/// Interaction logic for AsrManagerControl.xaml
/// </summary>
public partial class AsrManagerControl : UserControl
{
    private readonly AsrRulesService _asrService = new();
    private readonly ObservableCollection<AsrRuleViewModel> _displayedRules = new();
    private readonly List<AsrRuleViewModel> _allRules = new();
    private bool _isOperating = false;

    public AsrManagerControl()
    {
        InitializeComponent();
        IcAsrRules.ItemsSource = _displayedRules;
        Loaded += AsrManagerControl_Loaded;
    }

    private async void AsrManagerControl_Loaded(object sender, RoutedEventArgs e)
    {
        CheckAdminBanner();
        await RefreshRulesAsync();
    }

    private void CheckAdminBanner()
    {
        bool isAdmin = SecurityHelper.IsAdministrator();
        AdminBanner.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;
    }

    public async Task RefreshRulesAsync()
    {
        if (_isOperating) return;
        _isOperating = true;

        try
        {
            TxtStatusLog.Text = "Consultando configuración de Microsoft Defender (Get-MpPreference)...";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(2, 132, 199));

            // 1. Obtener estado general de Defender
            var statusTask = _asrService.GetDefenderStatusAsync();
            // 2. Obtener reglas ASR
            var rulesTask = _asrService.GetRulesAsync();

            await Task.WhenAll(statusTask, rulesTask);

            var status = statusTask.Result;
            var rules = rulesTask.Result;

            // Actualizar encabezado
            if (status.RealTimeProtectionEnabled)
            {
                TxtRealTimeStatus.Text = "Activo ✔";
                TxtRealTimeStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                EllRealTime.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                TxtRealTimeStatus.Text = "Inactivo ✖";
                TxtRealTimeStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                EllRealTime.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }

            TxtActiveRulesCount.Text = $"{status.ActiveRulesCount} / {status.TotalRulesCount}";
            TxtBlockedCount.Text = $"{status.BlockedRulesCount} Bloqueadas";
            TxtAuditedCount.Text = $"{status.AuditedRulesCount} Auditar";

            // Guardar reglas en lista maestra y filtrar
            _allRules.Clear();
            foreach (var r in rules)
            {
                _allRules.Add(new AsrRuleViewModel
                {
                    Guid = r.Guid,
                    Name = r.Name,
                    Description = r.Description,
                    Category = r.Category,
                    MitigationType = r.MitigationType,
                    Action = r.Action
                });
            }

            ApplyCurrentFilters();

            TxtStatusLog.Text = $"Listo. {status.ActiveRulesCount} de 16 reglas ASR están activas en Microsoft Defender.";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        }
        catch (Exception ex)
        {
            TxtStatusLog.Text = $"Error al consultar reglas ASR: {ex.Message}";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
        finally
        {
            _isOperating = false;
        }
    }

    private string _selectedCategory = "Todas las Categorías";

    private void ApplyCurrentFilters()
    {
        string searchText = TxtSearchFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;
        string selectedCategory = _selectedCategory;

        _displayedRules.Clear();
        foreach (var rule in _allRules)
        {
            // Filtro por categoría
            if (selectedCategory != "Todas las Categorías" && !rule.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Filtro por texto de búsqueda
            if (!string.IsNullOrEmpty(searchText))
            {
                bool matches = rule.Name.ToLowerInvariant().Contains(searchText) ||
                               rule.Description.ToLowerInvariant().Contains(searchText) ||
                               rule.Guid.ToLowerInvariant().Contains(searchText) ||
                               rule.Category.ToLowerInvariant().Contains(searchText);

                if (!matches) continue;
            }

            _displayedRules.Add(rule);
        }
    }

    // --- MANEJADORES DE CAMBIO DE ESTADO DE REGLA ---

    private async void BtnSetRuleDisabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AsrRuleViewModel vm)
        {
            await UpdateRuleActionAsync(vm, AsrRuleAction.Disabled);
        }
    }

    private async void BtnSetRuleAudit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AsrRuleViewModel vm)
        {
            await UpdateRuleActionAsync(vm, AsrRuleAction.Audit);
        }
    }

    private async void BtnSetRuleBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AsrRuleViewModel vm)
        {
            await UpdateRuleActionAsync(vm, AsrRuleAction.Block);
        }
    }

    private async Task UpdateRuleActionAsync(AsrRuleViewModel vm, AsrRuleAction newAction)
    {
        if (vm.Action == newAction) return;

        if (!SecurityHelper.IsAdministrator())
        {
            MessageBox.Show("Se requieren privilegios de Administrador para modificar las reglas ASR de Microsoft Defender.\n\nPor favor reinicia OmniWin como Administrador usando el botón superior.", 
                            "Permisos Requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previousAction = vm.Action;
        vm.Action = newAction;
        vm.IsBusy = true;

        TxtStatusLog.Text = $"Aplicando cambio en regla '{vm.Name}' vía Set-MpPreference...";
        TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));

        var res = await _asrService.SetRuleActionAsync(vm.Guid, newAction);
        vm.IsBusy = false;

        if (res.Success)
        {
            TxtStatusLog.Text = $"✔ {res.Message}";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            UpdateHeaderCounts();
        }
        else
        {
            vm.Action = previousAction; // Revertir en UI
            TxtStatusLog.Text = $"❌ {res.Message}";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            MessageBox.Show(res.Message, "Error al configurar regla ASR", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- MANEJADORES DE PERFILES RÁPIDOS ---

    private async void BtnProfileMaxProtection_Click(object sender, RoutedEventArgs e)
    {
        await ApplyProfileWrapperAsync("Máxima Protección");
    }

    private async void BtnProfileDeveloper_Click(object sender, RoutedEventArgs e)
    {
        await ApplyProfileWrapperAsync("Modo Desarrollador / Gamer");
    }

    private async void BtnProfileDisableAll_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("¿Estás seguro de que deseas desactivar todas las 16 reglas ASR de Microsoft Defender?",
                                      "Confirmar Desactivación", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            await ApplyProfileWrapperAsync("Desactivar Todas");
        }
    }

    private async Task ApplyProfileWrapperAsync(string profileName)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            MessageBox.Show("Se requieren privilegios de Administrador para aplicar perfiles ASR en Microsoft Defender.",
                            "Permisos Requeridos", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TxtStatusLog.Text = $"Aplicando perfil '{profileName}' en Microsoft Defender...";
        TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));

        var res = await _asrService.ApplyProfileAsync(profileName);
        if (res.Success)
        {
            TxtStatusLog.Text = $"✔ {res.Message}";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            await RefreshRulesAsync();
        }
        else
        {
            TxtStatusLog.Text = $"❌ {res.Message}";
            TxtStatusLog.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            MessageBox.Show(res.Message, "Error al aplicar perfil ASR", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateHeaderCounts()
    {
        int blocked = _allRules.Count(r => r.IsBlocked);
        int audited = _allRules.Count(r => r.IsAudited);
        int active = blocked + audited;

        TxtActiveRulesCount.Text = $"{active} / {_allRules.Count}";
        TxtBlockedCount.Text = $"{blocked} Bloqueadas";
        TxtAuditedCount.Text = $"{audited} Auditar";
    }

    // --- BÚSQUEDA Y FILTROS ---

    private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyCurrentFilters();
    }

    private void BtnCategoryFilter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string cat)
        {
            _selectedCategory = cat;

            BtnCatAll.Background = (_selectedCategory == "Todas las Categorías") ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(16, 23, 38));
            BtnCatAll.Foreground = (_selectedCategory == "Todas las Categorías") ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            BtnCatOffice.Background = (_selectedCategory == "Office & Aplicaciones") ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(16, 23, 38));
            BtnCatOffice.Foreground = (_selectedCategory == "Office & Aplicaciones") ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            BtnCatLsass.Background = (_selectedCategory == "Credenciales & Sistema") ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(16, 23, 38));
            BtnCatLsass.Foreground = (_selectedCategory == "Credenciales & Sistema") ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            BtnCatMalware.Background = (_selectedCategory == "Protección Malware") ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(16, 23, 38));
            BtnCatMalware.Foreground = (_selectedCategory == "Protección Malware") ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            BtnCatScripts.Background = (_selectedCategory == "Scripts & Persistencia") ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromRgb(16, 23, 38));
            BtnCatScripts.Foreground = (_selectedCategory == "Scripts & Persistencia") ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));

            ApplyCurrentFilters();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshRulesAsync();
    }

    private void BtnRestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        SecurityHelper.RestartAsAdministrator();
    }
}
