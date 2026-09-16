using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class WallpaperDisplayItem
{
    public string OriginalFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
}

public partial class SpotlightWallpaperControl : UserControl
{
    private readonly SpotlightWallpaperService _service = SpotlightWallpaperService.Instance;
    private readonly ObservableCollection<WallpaperDisplayItem> _wallpapers = new();

    public SpotlightWallpaperControl()
    {
        InitializeComponent();
        DgWallpapers.ItemsSource = _wallpapers;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshWallpapers();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshWallpapers();

    private void RefreshWallpapers()
    {
        _wallpapers.Clear();
        TxtStatus.Text = "Escaneando caché de Windows Spotlight...";

        Task.Run(() =>
        {
            var items = _service.ScanSpotlightAssets(minWidth: 1920, onlyLandscape: true);
            Dispatcher.Invoke(() =>
            {
                foreach (var item in items)
                {
                    _wallpapers.Add(new WallpaperDisplayItem
                    {
                        OriginalFilePath = item.OriginalFilePath,
                        FileName = item.FileName,
                        Resolution = item.Resolution,
                        SizeText = $"{(item.FileSizeBytes / (1024.0 * 1024.0)):F2} MB",
                        DateText = item.DateCreated.ToString("yyyy-MM-dd HH:mm")
                    });
                }

                TxtWallpaperCountBadge.Text = $"{_wallpapers.Count} FONDOS 4K/FHD";
                TxtStatus.Text = $"{_wallpapers.Count} fondos descubiertos en la caché local.";
            });
        });
    }

    private async void BtnExportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_wallpapers.Count == 0) return;

        string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Spotlight Wallpapers");
        TxtStatus.Text = $"Exportando {_wallpapers.Count} fondos a {dest}...";

        int count = await _service.ExportWallpapersAsync(_wallpapers.Select(w => w.OriginalFilePath), dest);
        MessageBox.Show($"{count} fotografías exportadas exitosamente con extensión .jpg a:\n{dest}", "Exportación Completada", MessageBoxButton.OK, MessageBoxImage.Information);

        TxtStatus.Text = $"✔ {count} fondos exportados con éxito.";
    }

    private void BtnSetWallpaper_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string imgPath)
        {
            bool ok = _service.SetAsDesktopWallpaper(imgPath);
            if (ok)
            {
                MessageBox.Show("¡Fondo de escritorio actualizado exitosamente con la imagen seleccionada!", "Fondo de Pantalla", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtStatus.Text = "✔ Fondo de escritorio actualizado.";
            }
            else
            {
                MessageBox.Show("No se pudo aplicar el fondo de pantalla.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnOpenExportFolder_Click(object sender, RoutedEventArgs e)
    {
        string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Spotlight Wallpapers");
        Directory.CreateDirectory(dest);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", dest) { UseShellExecute = true });
        }
        catch { }
    }
}
