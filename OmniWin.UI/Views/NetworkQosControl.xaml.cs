using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class QosDisplayItem
{
    public string Name { get; set; } = string.Empty;
    public string AppPathName { get; set; } = string.Empty;
    public double ThrottleRateKbps { get; set; }
    public string ThrottleRateKbpsText => $"{ThrottleRateKbps:N0} KB/s";
    public string ThrottleRateMbpsText => $"{(ThrottleRateKbps / 1024.0):F2} MB/s";
}

public partial class NetworkQosControl : UserControl
{
    private readonly NetworkQosService _qosService = NetworkQosService.Instance;
    private readonly PortConflictService _portService = PortConflictService.Instance;
    private readonly IdleMaintenanceService _idleService = IdleMaintenanceService.Instance;

    private readonly ObservableCollection<QosDisplayItem> _qosList = new();
    private readonly ObservableCollection<ListeningPortItem> _portList = new();
    private readonly DispatcherTimer _idleTimer = new();

    private int _lastDiagnosedPort = 0;

    public NetworkQosControl()
    {
        InitializeComponent();
        DgQosPolicies.ItemsSource = _qosList;
        DgListeningPorts.ItemsSource = _portList;

        _idleTimer.Interval = TimeSpan.FromSeconds(3);
        _idleTimer.Tick += IdleTimer_Tick;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshQosPolicies();
        RefreshPorts();
        UpdateIdleUi();
        _idleTimer.Start();
    }

    private void SubTab_Checked(object sender, RoutedEventArgs e)
    {
        if (PageQos == null || PagePorts == null || PageIdle == null) return;

        PageQos.Visibility = (RbTabQos.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        PagePorts.Visibility = (RbTabPorts.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        PageIdle.Visibility = (RbTabIdle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ==========================================
    // QoS SECTION
    // ==========================================

    private void SliderQosRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSliderValue != null)
        {
            long val = (long)SliderQosRate.Value;
            TxtSliderValue.Text = $"{val:N0} KB/s";
        }
    }

    private void CbQosPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbQosPresets.SelectedItem is ComboBoxItem item && long.TryParse(item.Tag?.ToString(), out long val))
        {
            if (SliderQosRate != null) SliderQosRate.Value = val;
        }
    }

    private async void RefreshQosPolicies()
    {
        _qosList.Clear();
        var policies = await _qosService.ListPoliciesAsync();
        foreach (var p in policies)
        {
            _qosList.Add(new QosDisplayItem
            {
                Name = p.Name,
                AppPathName = p.AppPathName,
                ThrottleRateKbps = p.ThrottleRateKbps
            });
        }
        TxtStatus.Text = $"{_qosList.Count} políticas QoS activas.";
    }

    private void BtnRefreshQos_Click(object sender, RoutedEventArgs e) => RefreshQosPolicies();

    private async void BtnApplyQos_Click(object sender, RoutedEventArgs e)
    {
        string app = TxtQosApp.Text.Trim();
        if (string.IsNullOrEmpty(app)) return;

        long limit = (long)SliderQosRate.Value;
        TxtStatus.Text = $"Aplicando límite a {app}...";

        var res = await _qosService.SetProcessLimitAsync(app, limit);
        MessageBox.Show(res.Message, res.Success ? "QoS Aplicado" : "Error QoS", MessageBoxButton.OK,
            res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

        RefreshQosPolicies();
    }

    private async void BtnRemoveQosRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string policyName)
        {
            var res = await _qosService.RemovePolicyAsync(policyName);
            RefreshQosPolicies();
        }
    }

    private async void BtnRemoveAllQos_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show("¿Eliminar todos los límites QoS aplicados por OmniWin?", "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        int count = await _qosService.RemoveAllOmniWinLimitsAsync();
        MessageBox.Show($"{count} políticas QoS eliminadas.", "QoS Restablecido", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshQosPolicies();
    }

    private async void BtnGamingPreset_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Aplicando Preset Gaming...";
        var res = await _qosService.ApplyGamingPresetAsync(300);
        MessageBox.Show(res.Message, "Preset Gaming", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshQosPolicies();
    }

    // ==========================================
    // PORTS SECTION
    // ==========================================

    private void RefreshPorts()
    {
        _portList.Clear();
        var ports = _portService.GetListeningPorts();
        foreach (var p in ports)
        {
            _portList.Add(p);
        }
    }

    private void BtnRefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void BtnDiagnosePort_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtPortSearch.Text.Trim(), out int port))
        {
            TxtPortDiagnosisResult.Text = "Ingresa un número de puerto válido.";
            return;
        }

        _lastDiagnosedPort = port;
        var diag = _portService.DiagnosePort(port);
        TxtPortDiagnosisResult.Text = diag.Description;

        if (diag.IsOccupied && diag.OccupyingProcess != null)
        {
            BtnReleasePort.Visibility = Visibility.Visible;
            BtnReleasePort.Content = $"🔓 Liberar Puerto {port} ({diag.OccupyingProcess.ProcessName})";
        }
        else
        {
            BtnReleasePort.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnReleasePort_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDiagnosedPort <= 0) return;

        var confirm = MessageBox.Show($"¿Deseas terminar el proceso que ocupa el puerto {_lastDiagnosedPort}?", "Liberar Puerto", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        bool ok = await _portService.ReleasePortAsync(_lastDiagnosedPort);
        MessageBox.Show(ok ? $"Puerto {_lastDiagnosedPort} liberado exitosamente." : $"No se pudo terminar el proceso en el puerto {_lastDiagnosedPort}.", "Resultado", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Error);

        RefreshPorts();
        BtnDiagnosePort_Click(sender, e);
    }

    private async void BtnKillPortRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int port)
        {
            var confirm = MessageBox.Show($"¿Deseas terminar el proceso que ocupa el puerto {port}?", "Liberar Puerto", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            bool ok = await _portService.ReleasePortAsync(port);
            RefreshPorts();
        }
    }

    // ==========================================
    // IDLE MAINTENANCE SECTION
    // ==========================================

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        if (RbTabIdle.IsChecked == true)
        {
            UpdateIdleUi();
        }
    }

    private void UpdateIdleUi()
    {
        var status = _idleService.GetStatus();
        TxtIdleMonitoringStatus.Text = status.IsMonitoring ? "Estado: Monitor ACTIVO ✔" : "Estado: Monitor DETENIDO ⏸";
        TxtIdleMonitoringStatus.Foreground = (System.Windows.Media.Brush)FindResource(status.IsMonitoring ? "Emerald" : "TextSecondary");
        BtnToggleIdleMonitor.Content = status.IsMonitoring ? "Detener Monitor" : "Iniciar Monitor";

        TxtIdleTimerInfo.Text = $"Inactividad actual: {status.CurrentIdleSeconds:F0} seg • Umbral para disparo: {status.ThresholdSeconds:F0} seg (10 min)";
        TxtIdleLastRun.Text = $"Última ejecución: {status.LastRunSummary}";
    }

    private void BtnToggleIdleMonitor_Click(object sender, RoutedEventArgs e)
    {
        var status = _idleService.GetStatus();
        if (status.IsMonitoring) _idleService.Stop();
        else _idleService.Start();

        UpdateIdleUi();
    }

    private async void BtnRunIdleNow_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = "Ejecutando mantenimiento en segundo plano...";
        _idleService.Config.TrimSsdEnabled = ChkTrimSsd.IsChecked == true;
        _idleService.Config.PurgeRamIfHighEnabled = ChkPurgeRam.IsChecked == true;
        _idleService.Config.CleanTempEnabled = ChkCleanTemp.IsChecked == true;

        string log = await _idleService.TriggerMaintenanceNowAsync();
        TxtIdleLog.Text = log;
        TxtStatus.Text = "Mantenimiento completado.";
        UpdateIdleUi();
    }
}
