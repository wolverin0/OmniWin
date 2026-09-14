using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class DnsSecurityControl : UserControl
{
    private readonly DnsSecurityService _dns = DnsSecurityService.Instance;

    public DnsSecurityControl()
    {
        InitializeComponent();
        Loaded += DnsSecurityControl_Loaded;
    }

    private void DnsSecurityControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshProviders();
        RefreshHostsStatus();
    }

    private void RefreshProviders()
    {
        try
        {
            DgProviders.ItemsSource = null;
            DgProviders.ItemsSource = _dns.GetProviders();
        }
        catch (Exception ex)
        {
            TxtDnsFooter.Text = $"Error cargando proveedores: {ex.Message}";
        }
    }

    private void RefreshHostsStatus()
    {
        try
        {
            var (isApplied, blockedCount, lastMod) = _dns.GetHostsBlockerStatus();

            if (isApplied)
            {
                BadgeHostsStatus.Background = new SolidColorBrush(Color.FromRgb(6, 46, 33));
                BadgeHostsStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtHostsBadge.Text = "ACTIVO";
                TxtHostsBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                TxtHostsDetails.Text = $"Bloqueador HOSTS activo con {blockedCount:N0} dominios de telemetría y publicidad redirigidos a 0.0.0.0 (Actualizado: {lastMod:dd/MM/yyyy HH:mm}).";
            }
            else
            {
                BadgeHostsStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                BadgeHostsStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(71, 85, 105));
                TxtHostsBadge.Text = "INACTIVO";
                TxtHostsBadge.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                TxtHostsDetails.Text = "No hay bloqueador masivo activo. Sincroniza la lista comunitaria oficial de StevenBlack para bloquear 60.000+ dominios no deseados.";
            }
        }
        catch (Exception ex)
        {
            TxtDnsFooter.Text = $"Error verificando HOSTS: {ex.Message}";
        }
    }

    private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnRunBenchmark.IsEnabled = false;
            TxtBenchmarkTime.Text = "⚡ Midiendo latencia a resolvers globales...";
            TxtDnsFooter.Text = "Enviando paquetes de sondeo de baja latencia a Cloudflare, Quad9, Google y AdGuard...";

            var results = await _dns.BenchmarkAllProvidersAsync();

            DgProviders.ItemsSource = null;
            DgProviders.ItemsSource = results;

            TxtBenchmarkTime.Text = $"Último test: {DateTime.Now:HH:mm:ss}";
            TxtDnsFooter.Text = $"Benchmark completado. El proveedor más rápido para tu conexión es '{results[0].Name}' ({results[0].LatencyText}).";
        }
        catch (Exception ex)
        {
            TxtDnsFooter.Text = $"Error durante el benchmark: {ex.Message}";
        }
        finally
        {
            BtnRunBenchmark.IsEnabled = true;
        }
    }

    private async void BtnApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DnsProviderItem provider)
        {
            try
            {
                TxtDnsFooter.Text = $"Configurando {provider.Name} ({provider.PrimaryIp}) en adaptadores de red...";
                bool success = await Task.Run(() => _dns.ApplyDnsConfiguration(provider));

                if (success)
                {
                    TxtDnsFooter.Text = $"[DNS APLICADO] {provider.Name} configurado correctamente. Caché DNS purgada.";
                    MessageBox.Show($"Se ha configurado '{provider.Name}' como servidor DNS preferido ({provider.PrimaryIp}) y alternativo ({provider.SecondaryIp}).\n\nSe ha ejecutado ipconfig /flushdns.", "DNS Seguro", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No se pudieron modificar los servidores DNS. Asegúrate de iniciar OmniWin con privilegios de Administrador.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                TxtDnsFooter.Text = $"Error aplicando DNS: {ex.Message}";
            }
        }
    }

    private async void BtnRestoreDhcp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TxtDnsFooter.Text = "Restableciendo DNS automático (DHCP) en adaptadores...";
            bool success = await Task.Run(() => _dns.RestoreDefaultDns());

            if (success)
            {
                TxtDnsFooter.Text = "[DHCP RESTAURADO] Adaptadores configurados para obtener DNS automáticamente del router.";
                MessageBox.Show("Los servidores DNS de todos los adaptadores de red han sido restablecidos a DHCP automático.", "DNS Restaurado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No se pudo restaurar la configuración DHCP.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtDnsFooter.Text = $"Error al restaurar DNS: {ex.Message}";
        }
    }

    private async void BtnSyncHosts_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnSyncHosts.IsEnabled = false;
            TxtDnsFooter.Text = "Descargando lista comunitaria unificada de StevenBlack (~60.000 reglas)...";

            var progress = new Progress<string>(msg =>
            {
                TxtDnsFooter.Text = msg;
            });

            var (success, count, msg) = await _dns.SyncStevenBlackHostsAsync(progress);

            if (success)
            {
                RefreshHostsStatus();
                MessageBox.Show($"¡Sincronización exitosa!\nSe agregaron {count:N0} dominios de telemetría y malware a tu archivo HOSTS.", "Bloqueador HOSTS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Error al sincronizar HOSTS: {msg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            TxtDnsFooter.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnSyncHosts.IsEnabled = true;
        }
    }

    private async void BtnRestoreHosts_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "¿Deseas restaurar el archivo HOSTS limpio por defecto de Windows y eliminar todas las reglas de bloqueo?",
            "Restaurar HOSTS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            bool success = await Task.Run(() => _dns.RestoreOriginalHosts());
            if (success)
            {
                RefreshHostsStatus();
                TxtDnsFooter.Text = "Archivo HOSTS restaurado a su estado limpio original de Windows.";
                MessageBox.Show("El archivo HOSTS ha sido limpiado y restaurado.", "Restaurado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("No se pudo restaurar el archivo HOSTS. Verifica los permisos de Administrador.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
