using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class FirewallMonitorControl : UserControl
{
    private readonly FirewallMonitorService _firewall = FirewallMonitorService.Instance;
    private List<ActiveConnectionItem> _allConnections = new();

    public FirewallMonitorControl()
    {
        InitializeComponent();
        Loaded += FirewallMonitorControl_Loaded;
    }

    private async void FirewallMonitorControl_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            TxtFirewallStatus.Text = "Obteniendo conexiones TCP y reglas de firewall...";
            var conns = await Task.Run(() => _firewall.GetActiveConnections());
            var rules = await Task.Run(() => _firewall.GetOmniWinBlockedRules());

            _allConnections = conns;
            ApplyFilter();

            DgBlockedRules.ItemsSource = null;
            DgBlockedRules.ItemsSource = rules;
            TxtBlockedCount.Text = $"({rules.Count} bloqueados)";

            TxtFirewallStatus.Text = $"Conexiones TCP actualizadas a las {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            TxtFirewallStatus.Text = $"Error al leer sockets: {ex.Message}";
        }
    }

    private void ApplyFilter()
    {
        string query = TxtSearchFilter.Text?.Trim().ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrEmpty(query)
            ? _allConnections
            : _allConnections.Where(c =>
                c.ProcessName.ToLowerInvariant().Contains(query) ||
                c.RemoteEndpoint.ToLowerInvariant().Contains(query) ||
                c.LocalEndpoint.ToLowerInvariant().Contains(query) ||
                c.ProcessId.ToString().Contains(query)).ToList();

        DgConnections.ItemsSource = null;
        DgConnections.ItemsSource = filtered;
        TxtConnectionsCount.Text = $"Mostrando {filtered.Count} de {_allConnections.Count} sockets";
    }

    private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void BtnRefreshSockets_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async void BtnBlockProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ActiveConnectionItem conn)
        {
            if (string.IsNullOrWhiteSpace(conn.ExecutablePath))
            {
                MessageBox.Show($"No se pudo determinar la ruta en disco del proceso '{conn.ProcessName}'. Puede ser un servicio protegido del sistema.", "Bloquear en Firewall", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"¿Deseas bloquear todo el tráfico de red saliente y entrante para '{conn.ProcessName}'?\n\nRuta: {conn.ExecutablePath}",
                "Confirmar Bloqueo en Firewall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                bool success = await Task.Run(() => _firewall.BlockProcessInFirewall(conn.ExecutablePath, $"OmniWin_Block_{conn.ProcessName}"));
                if (success)
                {
                    TxtFirewallStatus.Text = $"[BLOQUEADO] Regla de firewall aplicada para {conn.ProcessName}.";
                    await RefreshAllAsync();
                }
                else
                {
                    MessageBox.Show("No se pudo crear la regla en el Firewall de Windows. Asegúrate de ejecutar OmniWin como Administrador.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private async void BtnUnblockRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is BlockedFirewallRuleItem rule)
        {
            bool success = await Task.Run(() => _firewall.UnblockProcessInFirewall(rule.RuleName));
            if (success)
            {
                TxtFirewallStatus.Text = $"[DESBLOQUEADO] Regla {rule.RuleName} eliminada del firewall.";
                await RefreshAllAsync();
            }
            else
            {
                MessageBox.Show($"No se pudo eliminar la regla '{rule.RuleName}'.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
