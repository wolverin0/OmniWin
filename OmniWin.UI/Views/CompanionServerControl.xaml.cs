using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class CompanionServerControl : UserControl
{
    private readonly CompanionServerService _server = CompanionServerService.Instance;
    private readonly DispatcherTimer _pollTimer = new();
    private bool _isPopulatingAdapters;

    public CompanionServerControl()
    {
        InitializeComponent();
        Loaded += CompanionServerControl_Loaded;
        Unloaded += CompanionServerControl_Unloaded;

        _pollTimer.Interval = TimeSpan.FromSeconds(2);
        _pollTimer.Tick += (s, e) => UpdateClientCount();
    }

    private void CompanionServerControl_Loaded(object sender, RoutedEventArgs e)
    {
        _server.OnNetworkChanged += Server_OnNetworkChanged;
        LoadNetworkAdapters();
        RefreshUi();
        _pollTimer.Start();
    }

    private void CompanionServerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _server.OnNetworkChanged -= Server_OnNetworkChanged;
        _pollTimer.Stop();
    }

    private void Server_OnNetworkChanged(string newIp)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshUi();
        });
    }

    private void LoadNetworkAdapters()
    {
        _isPopulatingAdapters = true;
        try
        {
            var adapters = CompanionServerService.GetAvailableNetworkAdapters();
            CmbNetworkAdapters.ItemsSource = null;
            CmbNetworkAdapters.ItemsSource = adapters;

            var selected = adapters.FirstOrDefault(a => a.IpAddress == _server.LocalIp) ?? adapters.FirstOrDefault();
            if (selected != null)
            {
                CmbNetworkAdapters.SelectedItem = selected;
                TxtAdapterName.Text = $"{selected.Name} ({selected.InterfaceType})";
            }
        }
        catch { }
        finally
        {
            _isPopulatingAdapters = false;
        }
    }

    private void RefreshUi()
    {
        try
        {
            TxtPairingUrl.Text = _server.PairingUrl;
            TxtLocalIp.Text = _server.LocalIp;
            TxtPort.Text = _server.Port.ToString();
            UpdateClientCount();

            // Render QR Code Image
            byte[] qrBytes = _server.GenerateQrCodePngBytes(6);
            if (qrBytes != null && qrBytes.Length > 0)
            {
                var bitmap = new BitmapImage();
                using (var ms = new MemoryStream(qrBytes))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                }
                bitmap.Freeze();
                ImgQrCode.Source = bitmap;
            }

            if (_server.IsRunning)
            {
                BtnToggleServer.Content = "⏸️ Detener Servidor";
                DotStatus.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtServerStatus.Text = $"Servidor Activo en Puerto {_server.Port}";
                TxtServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtFooter.Text = $"Empareja apuntando la cámara a la URL: {_server.PairingUrl}";
            }
            else
            {
                BtnToggleServer.Content = "▶️ Iniciar Servidor";
                DotStatus.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtServerStatus.Text = "Servidor Detenido";
                TxtServerStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtFooter.Text = "El servidor está apagado. Presiona 'Iniciar Servidor' para permitir el acceso móvil.";
            }
        }
        catch (Exception ex)
        {
            TxtFooter.Text = $"Error al actualizar vista: {ex.Message}";
        }
    }

    private void UpdateClientCount()
    {
        int count = _server.ActiveClientsCount;
        TxtConnectedClients.Text = count == 1 ? "1 dispositivo" : $"{count} dispositivos";
    }

    private void BtnToggleServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_server.IsRunning)
            {
                _server.Stop();
            }
            else
            {
                _server.Start();
            }
            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo cambiar el estado del servidor: {ex.Message}", "OmniCompanion", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnRefreshToken_Click(object sender, RoutedEventArgs e)
    {
        _server.RegenerateToken();
        RefreshUi();
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_server.IsRunning)
            {
                _server.Start();
                RefreshUi();
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = _server.PairingUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al abrir navegador: {ex.Message}", "OmniCompanion", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_server.PairingUrl);
            TxtFooter.Text = "¡Enlace copiado al portapapeles!";
        }
        catch { }
    }

    private void CmbNetworkAdapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingAdapters) return;

        if (CmbNetworkAdapters.SelectedItem is NetworkAdapterInfo selected)
        {
            _server.SetSelectedIp(selected.IpAddress);
            TxtAdapterName.Text = $"{selected.Name} ({selected.InterfaceType})";
            RefreshUi();
            TxtFooter.Text = $"Red seleccionada: {selected.Name} ({selected.IpAddress}). Código QR actualizado.";
        }
    }

    private void BtnRefreshAdapters_Click(object sender, RoutedEventArgs e)
    {
        LoadNetworkAdapters();
        RefreshUi();
        TxtFooter.Text = "Adaptadores de red re-escaneados exitosamente.";
    }
}
