using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class MigrationDisplayItem
{
    public string SourcePath { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public string TotalBytesText => $"{(TotalBytes / (1024.0 * 1024.0 * 1024.0)):F2} GB";
    public DateTime Timestamp { get; set; }
    public string DateText => Timestamp.ToString("yyyy-MM-dd HH:mm");
}

public partial class AppMigrationControl : UserControl
{
    private readonly AppMigrationService _migrationService = AppMigrationService.Instance;
    private readonly ObservableCollection<MigrationDisplayItem> _migrations = new();
    private MigrationPlan? _currentPlan;

    public AppMigrationControl()
    {
        InitializeComponent();
        DgMigrations.ItemsSource = _migrations;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshMigrationsList();
    }

    private void RefreshMigrationsList()
    {
        _migrations.Clear();
        var list = _migrationService.GetActiveMigrations();
        foreach (var item in list)
        {
            _migrations.Add(new MigrationDisplayItem
            {
                SourcePath = item.SourcePath,
                TargetPath = item.TargetPath,
                TotalBytes = item.TotalBytes,
                Timestamp = item.Timestamp
            });
        }
    }

    private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta a migrar (Juego o Aplicación)"
        };

        if (dialog.ShowDialog() == true)
        {
            TxtSourcePath.Text = dialog.FolderName;
        }
    }

    private void BtnBrowseTarget_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta base de destino (ej. D:\\Games)"
        };

        if (dialog.ShowDialog() == true)
        {
            TxtTargetRoot.Text = dialog.FolderName;
        }
    }

    private void TxtSourcePath_TextChanged(object sender, TextChangedEventArgs e) => TriggerAnalysis();
    private void TxtTargetRoot_TextChanged(object sender, TextChangedEventArgs e) => TriggerAnalysis();

    private void TriggerAnalysis()
    {
        string src = TxtSourcePath?.Text?.Trim() ?? "";
        string tgt = TxtTargetRoot?.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(src) || !Directory.Exists(src) || string.IsNullOrEmpty(tgt))
        {
            if (BtnExecuteMigration != null) BtnExecuteMigration.IsEnabled = false;
            if (BadgeSpaceStatus != null) BadgeSpaceStatus.Visibility = Visibility.Collapsed;
            if (TxtDiagnosticsTitle != null) TxtDiagnosticsTitle.Text = "Selecciona una carpeta válida para calcular tamaño y espacio.";
            if (TxtDiagnosticsSubtitle != null) TxtDiagnosticsSubtitle.Text = "";
            return;
        }

        Task.Run(() =>
        {
            var plan = _migrationService.AnalyzeFolder(src, tgt);
            Dispatcher.Invoke(() =>
            {
                _currentPlan = plan;
                double sizeGb = plan.TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
                double freeGb = plan.TargetFreeSpaceBytes / (1024.0 * 1024.0 * 1024.0);

                TxtDiagnosticsTitle.Text = $"Tamaño: {sizeGb:F2} GB ({plan.TotalFiles:N0} archivos) • Destino {plan.TargetDrive}: {freeGb:F2} GB libres";
                BadgeSpaceStatus.Visibility = Visibility.Visible;

                if (plan.IsCurrentlyJunction)
                {
                    TxtDiagnosticsSubtitle.Text = "La carpeta de origen ya es un enlace Junction activo.";
                    BadgeSpaceStatus.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
                    TxtSpaceBadge.Text = "YA ES UN JUNCTION";
                    TxtSpaceBadge.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    BtnExecuteMigration.IsEnabled = false;
                }
                else if (plan.HasEnoughSpace)
                {
                    TxtDiagnosticsSubtitle.Text = $"Se creará el Junction en '{src}' apuntando a '{plan.TargetPath}'.";
                    BadgeSpaceStatus.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                    TxtSpaceBadge.Text = "ESPACIO SUFICIENTE ✔";
                    TxtSpaceBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    BtnExecuteMigration.IsEnabled = true;
                }
                else
                {
                    TxtDiagnosticsSubtitle.Text = "No hay suficiente espacio libre en el disco de destino seleccionado.";
                    BadgeSpaceStatus.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
                    TxtSpaceBadge.Text = "ESPACIO INSUFICIENTE ✘";
                    TxtSpaceBadge.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    BtnExecuteMigration.IsEnabled = false;
                }
            });
        });
    }

    private async void BtnExecuteMigration_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlan == null || !_currentPlan.HasEnoughSpace) return;

        string src = _currentPlan.SourcePath;
        string tgt = TxtTargetRoot.Text.Trim();

        var confirm = MessageBox.Show(
            $"¿Deseas migrar la carpeta?\n\nOrigen: {src}\nDestino: {tgt}\n\nLos archivos se moverán y se creará un enlace Junction transparente.",
            "Confirmar Migración", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        BtnExecuteMigration.IsEnabled = false;
        PnlProgress.Visibility = Visibility.Visible;
        TxtStatus.Text = "Migrando archivos...";

        var progress = new Progress<(long copied, long total, string file)>(p =>
        {
            if (p.total > 0)
            {
                double pct = (double)p.copied / p.total * 100.0;
                PbMigration.Value = pct;
                TxtProgressDetails.Text = $"{pct:F1}% • {p.file}";
            }
        });

        var result = await _migrationService.MigrateFolderAsync(src, tgt, progress);
        PnlProgress.Visibility = Visibility.Collapsed;
        BtnExecuteMigration.IsEnabled = true;

        if (result.Success)
        {
            MessageBox.Show(result.Message, "Migración Exitosa", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtStatus.Text = $"✔ Migración completada: {result.FilesMigrated:N0} archivos en {result.DurationMs} ms.";
            RefreshMigrationsList();
            TriggerAnalysis();
        }
        else
        {
            MessageBox.Show(result.Message, "Error en Migración", MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = $"✘ {result.Message}";
        }
    }

    private async void BtnRollbackRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sourcePath)
        {
            var confirm = MessageBox.Show(
                $"¿Deseas revertir la migración de '{sourcePath}'?\n\nLos archivos volverán a su ubicación original y se eliminará el enlace Junction.",
                "Revertir Migración", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            TxtStatus.Text = $"Revirtiendo '{sourcePath}'...";
            var res = await _migrationService.RollbackMigrationAsync(sourcePath);
            if (res.Success)
            {
                MessageBox.Show(res.Message, "Reversión Completada", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshMigrationsList();
                TriggerAnalysis();
            }
            else
            {
                MessageBox.Show(res.Message, "Error al Revertir", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnScanCandidates_Click(object sender, RoutedEventArgs e)
    {
        var candidates = _migrationService.FindCandidateFolders("C:\\");
        if (candidates.Count > 0)
        {
            TxtSourcePath.Text = candidates.First();
        }
        else
        {
            MessageBox.Show("No se encontraron carpetas estándar de juegos o software en C:\\Program Files.", "Escanear Candidatos", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
