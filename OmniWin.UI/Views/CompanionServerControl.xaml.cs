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
        RefreshUi();
        _pollTimer.Start();
    }

    private void CompanionServerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _pollTimer.Stop();
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
}
