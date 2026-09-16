using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class PrivacySettingUiItem
{
    public string Id { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SafetyLevel { get; set; } = string.Empty;
    public bool IsProtected { get; set; }

    public string IsProtectedText => IsProtected ? "PROTEGIDO ✔" : "EXPUESTO ⚠️";
    public Brush StatusColor => IsProtected ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(251, 191, 36));
    public string ActionButtonText => IsProtected ? "Revertir" : "Bloquear";
    public Brush ActionButtonBackground => IsProtected ? new SolidColorBrush(Color.FromRgb(55, 65, 81)) : new SolidColorBrush(Color.FromRgb(5, 150, 105));
}

public partial class PrivacyShieldControl : UserControl
{
    private readonly PrivacyShieldService _privacyService = new();
    private readonly RansomwareCanaryService _canaryService = RansomwareCanaryService.Instance;
    private readonly ObservableCollection<CanaryBreachAlert> _breachAlerts = new();

    public PrivacyShieldControl()
    {
        InitializeComponent();
        LbBreachAlerts.ItemsSource = _breachAlerts;
        _canaryService.CanaryBreached += OnCanaryBreached;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshPrivacyAudit();
        RefreshCanaryStatus();
    }

    private void BtnRefreshAudit_Click(object sender, RoutedEventArgs e)
    {
        RefreshPrivacyAudit();
    }

    private void RefreshPrivacyAudit()
    {
        try
        {
            var audit = _privacyService.GetPrivacyAudit();
            var uiItems = audit.Select(a => new PrivacySettingUiItem
            {
                Id = a.Id,
                CategoryName = a.Category switch
                {
                    PrivacyCategory.TelemetryAndDiagnostics => "Telemetría & Diagnóstico",
                    PrivacyCategory.WindowsAIAndRecall => "Windows AI & Recall",
                    PrivacyCategory.SearchAndAdvertising => "Búsqueda & Publicidad",
                    PrivacyCategory.ActivityAndLocation => "Actividad & Ubicación",
                    PrivacyCategory.BackgroundServicesAndTasks => "Servicios & Tareas",
                    _ => a.Category.ToString()
                },
                Title = a.Title,
                Description = a.Description,
                SafetyLevel = a.SafetyLevel,
                IsProtected = a.IsProtected
            }).ToList();

            DgPrivacySettings.ItemsSource = uiItems;
            int protectedCount = uiItems.Count(u => u.IsProtected);
            TxtProtectedBadge.Text = $"{protectedCount}/{uiItems.Count} PROTEGIDOS";
            TxtPrivacyFooterStatus.Text = $"Auditoría completada: {protectedCount} de {uiItems.Count} protecciones de telemetría activas.";
        }
        catch (Exception ex)
        {
            TxtPrivacyFooterStatus.Text = $"Error al auditar privacidad: {ex.Message}";
        }
    }

    private void BtnProfileRecommended_Click(object sender, RoutedEventArgs e)
    {
        ApplyProfile(PrivacyProfile.Recommended);
    }

    private void BtnProfileStrict_Click(object sender, RoutedEventArgs e)
    {
        ApplyProfile(PrivacyProfile.StrictPrivacy);
    }

    private void BtnProfileGamer_Click(object sender, RoutedEventArgs e)
    {
        ApplyProfile(PrivacyProfile.GamerZeroTelemetry);
    }

    private void ApplyProfile(PrivacyProfile profile)
    {
        try
        {
            var res = _privacyService.ApplyProfile(profile);
            MessageBox.Show(res.Message, "Perfil de Privacidad", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshPrivacyAudit();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al aplicar perfil: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnToggleSetting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string settingId)
        {
            try
            {
                var audit = _privacyService.GetPrivacyAudit();
                var current = audit.FirstOrDefault(a => a.Id == settingId);
                if (current != null)
                {
                    bool newTarget = !current.IsProtected;
                    if (newTarget)
                    {
                        _privacyService.ApplySetting(settingId, true);
                    }
                    else
                    {
                        _privacyService.RollbackSetting(settingId);
                    }
                    RefreshPrivacyAudit();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al alternar ajuste: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ==========================================
    // CANARY HONEYPOT LOGIC
    // ==========================================

    private void RefreshCanaryStatus()
    {
        var status = _canaryService.GetStatus();
        if (status.IsActive)
        {
            TxtCanaryStatusBadge.Text = "ESCUDO ACTIVO ✔";
            TxtCanaryStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            BtnToggleCanaryShield.Content = "⏸️ Detener Escudo Canario";
            BtnToggleCanaryShield.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TxtCanaryFooterStatus.Text = $"Vigilando activamente {status.MonitoredFolders.Count} carpetas críticas con {status.ActiveCanaries.Count} trampas señuelo.";
        }
        else
        {
            TxtCanaryStatusBadge.Text = "ESCUDO INACTIVO";
            TxtCanaryStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            BtnToggleCanaryShield.Content = "🛡️ Activar Escudo Canario";
            BtnToggleCanaryShield.Background = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtCanaryFooterStatus.Text = "Escudo inactivo. Haz clic en 'Activar Escudo' para sembrar señuelos y activar la congelación de procesos.";
        }

        LbCanaryFiles.ItemsSource = status.ActiveCanaries;

        _breachAlerts.Clear();
        foreach (var a in _canaryService.GetRecentAlerts())
        {
            _breachAlerts.Add(a);
        }
    }

    private void BtnToggleCanaryShield_Click(object sender, RoutedEventArgs e)
    {
        var status = _canaryService.GetStatus();
        if (status.IsActive)
        {
            _canaryService.StopShield();
            MessageBox.Show("Escudo Canario detenido y archivos trampa eliminados limpiamente.", "Escudo Desactivado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            _canaryService.StartShield();
            MessageBox.Show("¡Escudo Canario ACTIVO!\n\nSe han desplegado archivos trampa en Documentos, Escritorio e Imágenes. Si cualquier ransomware o malware los modifica, el proceso será congelado al instante.", "Escudo Activo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        RefreshCanaryStatus();
    }

    private void OnCanaryBreached(CanaryBreachAlert alert)
    {
        Dispatcher.Invoke(() =>
        {
            _breachAlerts.Insert(0, alert);
            System.Media.SystemSounds.Exclamation.Play();
            MessageBox.Show($"¡ALERTA DE SEGURIDAD CRÍTICA!\n\n{alert.ActionTaken}\n\nArchivo objetivo: {alert.CanaryFilePath}\nProceso: {alert.OffendingProcessName} (PID {alert.OffendingPid})\nEjecutable: {alert.OffendingProcessPath}", "Alerta Anti-Ransomware", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }
}
