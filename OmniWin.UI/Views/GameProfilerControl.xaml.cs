using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class GameProfilerControl : UserControl
{
    private readonly GameProfilerService _profiler = GameProfilerService.Instance;
    private readonly EtwFramePacingService _etwPacing = EtwFramePacingService.Instance;

    public GameProfilerControl()
    {
        InitializeComponent();
        Loaded += GameProfilerControl_Loaded;
        Unloaded += GameProfilerControl_Unloaded;
    }

    private void GameProfilerControl_Loaded(object sender, RoutedEventArgs e)
    {
        _profiler.OnGameStatusChanged += Profiler_OnGameStatusChanged;
        _etwPacing.OnFrameMetricsUpdated += EtwPacing_OnFrameMetricsUpdated;
        RefreshUi();
    }

    private void GameProfilerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _profiler.OnGameStatusChanged -= Profiler_OnGameStatusChanged;
        _etwPacing.OnFrameMetricsUpdated -= EtwPacing_OnFrameMetricsUpdated;
        if (_etwPacing.IsRunning)
        {
            _etwPacing.Stop();
        }
    }

    private void Profiler_OnGameStatusChanged(string displayName, bool isActive)
    {
        Dispatcher.Invoke(() =>
        {
            if (isActive)
            {
                DotGameActive.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtActiveGame.Text = $"⚡ Activo: {displayName}";
                TxtProfilerDetail.Text = "Afinidad P-Cores aplicada, Timer 0.5ms activo y lista de reserva de RAM purgada.";
                TxtProfilerFooter.Text = $"[PERFIL APLICADO] {displayName} detectado y optimizado a las {DateTime.Now:HH:mm:ss}.";
            }
            else
            {
                DotGameActive.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
                TxtActiveGame.Text = "Monitoreando en segundo plano...";
                TxtProfilerDetail.Text = "Detección activa cada 2000ms. Listo para optimizar.";
                TxtProfilerFooter.Text = $"[JUEGO FINALIZADO] {displayName} cerrado. Timer y afinidades restablecidos.";
            }
            RefreshUi();
        });
    }

    private void RefreshUi()
    {
        try
        {
            var activeGame = _profiler.GetMonitoredGames().FirstOrDefault(g => g.IsActive);
            if (activeGame != null)
            {
                TxtActiveGame.Text = $"⚡ Activo: {activeGame.DisplayName}";
                DotGameActive.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
            else
            {
                TxtActiveGame.Text = "Monitoreando en segundo plano...";
                DotGameActive.Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246));
            }

            var games = _profiler.GetMonitoredGames();
            string filter = TxtSearchProfile?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(filter))
            {
                games = games.Where(g =>
                    g.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    g.ExecutableName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            DgGames.ItemsSource = null;
            DgGames.ItemsSource = games;
        }
        catch (Exception ex)
        {
            TxtProfilerFooter.Text = $"Error al actualizar: {ex.Message}";
        }
    }

    private void TxtSearchProfile_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtSearchPlaceholder != null)
        {
            TxtSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtSearchProfile.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        RefreshUi();
    }

    private void BtnToggleProfiler_Click(object sender, RoutedEventArgs e)
    {
        if (BtnToggleProfiler.Content.ToString()?.Contains("Pausar") == true)
        {
            _profiler.StopMonitoring();
            BtnToggleProfiler.Content = "▶️ Reanudar Detección";
            DotGameActive.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtActiveGame.Text = "Detección en Pausa";
            TxtProfilerDetail.Text = "El monitor de juegos no está analizando procesos.";
        }
        else
        {
            _profiler.StartMonitoring();
            BtnToggleProfiler.Content = "⏸️ Pausar Detección";
            RefreshUi();
        }
    }

    private void BtnRefreshProfiler_Click(object sender, RoutedEventArgs e)
    {
        _profiler.CheckRunningProcesses();
        RefreshUi();
    }

    private void BtnAddGame_Click(object sender, RoutedEventArgs e)
    {
        string exe = TxtNewExe.Text?.Trim() ?? string.Empty;
        string name = TxtNewName.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(exe))
        {
            MessageBox.Show("Ingresa el nombre del ejecutable (ej. juego.exe).", "Profiler", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = exe;
        }

        _profiler.AddMonitoredGame(exe, name);
        TxtNewExe.Text = string.Empty;
        TxtNewName.Text = string.Empty;
        RefreshUi();
    }

    private void BtnRemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is GameProfileItem game)
        {
            _profiler.RemoveMonitoredGame(game.ExecutableName);
            RefreshUi();
        }
    }

    private void EtwPacing_OnFrameMetricsUpdated(double fps, double oneLow, double pointOneLow, double frametimeMs)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLiveFps.Text = $"{fps:F0} FPS";
            Txt1PercentLow.Text = $"{oneLow:F0} FPS";
            TxtPointOneLow.Text = $"{pointOneLow:F0} FPS";
            TxtFrametimeMs.Text = $"{frametimeMs:F1} ms";
            TxtStutterRate.Text = $"{_etwPacing.StutterRatePercent:F1}%";
        });
    }

    private void BtnEtwBenchmark_Click(object sender, RoutedEventArgs e)
    {
        if (!_etwPacing.IsRunning)
        {
            var activeGame = _profiler.GetMonitoredGames().FirstOrDefault(g => g.IsActive);
            string target = activeGame != null ? activeGame.DisplayName : "Proceso en Primer Plano";
            string targetExe = activeGame != null ? activeGame.ExecutableName : string.Empty;

            _etwPacing.Start($"Benchmark ETW - {target}", targetExe);
            PnlEtwTelemetry.Visibility = Visibility.Visible;
            TxtEtwEngineMode.Text = _etwPacing.EngineMode.ToUpperInvariant();
            TxtEtwTarget.Text = $"Target: {target}";
            BtnEtwBenchmark.Content = "⏹️ Detener Benchmark";
            TxtProfilerFooter.Text = $"[ETW 1% LOW] Medición activa con motor {_etwPacing.EngineMode}.";
        }
        else
        {
            BtnStopEtw_Click(sender, e);
        }
    }

    private void BtnStopEtw_Click(object sender, RoutedEventArgs e)
    {
        if (_etwPacing.IsRunning)
        {
            var report = _etwPacing.Stop();
            BtnEtwBenchmark.Content = "⚡ Benchmark 1% Low (ETW)";
            TxtProfilerFooter.Text = $"[BENCHMARK COMPLETADO] FPS Promedio: {report.AverageFps:F1} | 1% Low: {report.OnePercentLowFps:F1} FPS | Micro-tirones: {report.StutterPercentage:F1}%.";

            string summary = $"Resultados del Benchmark ETW / Frame Pacing:\n\n" +
                             $"• Sesión: {report.SessionName}\n" +
                             $"• Duración: {report.Duration.TotalSeconds:F1} segundos\n" +
                             $"• Cuadros Totales: {report.TotalFrames:N0} frames\n" +
                             $"• FPS Promedio: {report.AverageFps:F1} FPS\n" +
                             $"• 1% Low FPS: {report.OnePercentLowFps:F1} FPS\n" +
                             $"• 0.1% Low FPS: {report.PointOnePercentLowFps:F1} FPS\n" +
                             $"• Frametime Promedio: {report.AverageFrametimeMs:F2} ms (Min: {report.MinFrametimeMs:F2} ms, Max: {report.MaxFrametimeMs:F2} ms)\n" +
                             $"• Micro-tirones detectados: {report.StutterCount} ({report.StutterPercentage:F1}% de frames)\n\n" +
                             $"Evaluación: {(report.StutterPercentage < 1.0 ? "EXCELENTE (Frame Pacing ultra-estable)" : report.StutterPercentage < 3.0 ? "BUENO (Fluctuaciones mínimas)" : "ATENCIÓN (Micro-stuttering detectable)")}";

            MessageBox.Show(summary, "Reporte de Frame Pacing & 1% Lows", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
