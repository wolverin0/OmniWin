using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class RepairConsoleControl : UserControl
{
    private readonly RepairPipelineService _repairService = new();
    private readonly ObservableCollection<RepairStepDisplayItem> _steps = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public RepairConsoleControl()
    {
        InitializeComponent();
        InitSteps();
        WireServiceEvents();
        CheckAdminStatus();
        PrintWelcomeBanner();
    }

    private void InitSteps()
    {
        _steps.Clear();
        var defaults = _repairService.GetDefaultSteps();
        foreach (var def in defaults)
        {
            _steps.Add(new RepairStepDisplayItem
            {
                StepNumber = def.StepNumber,
                Title = def.Name,
                Command = def.CommandDescription,
                Description = def.Description,
                StatusText = def.StatusText,
                Status = def.Status
            });
        }
        IcSteps.ItemsSource = _steps;
    }

    private void WireServiceEvents()
    {
        _repairService.OnProgressOutput += (line, progress) =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtConsole.AppendText(line + Environment.NewLine);
                if (ChkAutoScroll.IsChecked == true)
                {
                    TxtConsole.ScrollToEnd();
                }

                PbOverall.Value = progress;
                TxtOverallPercentage.Text = $"{progress:0}%";

                if (line.StartsWith("[Paso", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("[PASO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("[INICIO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("[FIN", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("[*]", StringComparison.OrdinalIgnoreCase))
                {
                    TxtCurrentAction.Text = line;
                }
            });
        };

        _repairService.OnStepStatusChanged += (stepNum, status, text) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (stepNum >= 1 && stepNum <= _steps.Count)
                {
                    _steps[stepNum - 1].UpdateStatus(status, text);
                }
            });
        };

        _repairService.OnExecutionStateChanged += isRunning =>
        {
            Dispatcher.Invoke(() =>
            {
                _isRunning = isRunning;
                BtnRunFull.IsEnabled = !isRunning;
                BtnRunSfc.IsEnabled = !isRunning;
                BtnRunWinUpdate.IsEnabled = !isRunning;
                BtnCancel.IsEnabled = isRunning;
            });
        };
    }

    private void CheckAdminStatus()
    {
        bool isAdmin = SecurityHelper.IsAdministrator();
        if (isAdmin)
        {
            BadgeAdmin.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
            TxtAdminStatus.Text = "Administrador Activo ✔";
            TxtAdminStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
            TxtAdminIcon.Text = "🛡️";
            BadgeAdmin.ToolTip = "OmniWin cuenta con permisos elevados para reparar el sistema.";
        }
        else
        {
            BadgeAdmin.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A03"));
            TxtAdminStatus.Text = "Sin permisos de Admin ⚠ (Clic para reiniciar)";
            TxtAdminStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
            TxtAdminIcon.Text = "⚠️";
            BadgeAdmin.ToolTip = "Haz clic aquí para reiniciar OmniWin como Administrador y desbloquear reparaciones.";
        }
    }

    private void PrintWelcomeBanner()
    {
        var sb = new StringBuilder();
        sb.AppendLine("╔══════════════════════════════════════════════════════════════════════════════════════╗");
        sb.AppendLine("║                OMNIWIN COMPONENT REPAIR KIT & DIAGNOSTICS CONSOLE                    ║");
        sb.AppendLine("║             Protocolo Secuencial Estricto de Mantenimiento de Microsoft              ║");
        sb.AppendLine("╚══════════════════════════════════════════════════════════════════════════════════════╝");
        sb.AppendLine("[*] Consola de comandos inicializada. Motor de captura por stdio activo.");
        sb.AppendLine("[*] Secuencia estandarizada: DISM ScanHealth -> RestoreHealth -> SFC -> WinSxS ResetBase -> WinUpdate -> Winsock/WMI.");
        if (!SecurityHelper.IsAdministrator())
        {
            sb.AppendLine("[!] ADVERTENCIA: OmniWin no se está ejecutando como Administrador.");
            sb.AppendLine("[!] Haz clic en el indicador 'Sin permisos de Admin' para relanzar con privilegios elevados.");
        }
        else
        {
            sb.AppendLine("[✔] Privilegios de Administrador verificados y confirmados.");
        }
        sb.AppendLine("────────────────────────────────────────────────────────────────────────────────────────");

        TxtConsole.Text = sb.ToString();
    }

    private void ResetStepsToPending()
    {
        foreach (var s in _steps)
        {
            s.UpdateStatus(RepairStepStatus.Pending, "Pendiente ⏳");
        }
        PbOverall.Value = 0;
        TxtOverallPercentage.Text = "0%";
    }

    private async void BtnRunFull_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        ResetStepsToPending();
        TxtCurrentAction.Text = "Iniciando Pipeline Integral Secuencial...";
        _cts = new CancellationTokenSource();

        try
        {
            var res = await _repairService.RunFullPipelineAsync(_cts.Token);
            TxtCurrentAction.Text = res.Success 
                ? "✔ Pipeline de Reparación finalizado exitosamente." 
                : "⚠ Pipeline finalizado con advertencias o errores. Revisa la consola.";
        }
        catch (OperationCanceledException)
        {
            TxtCurrentAction.Text = "🛑 Operación cancelada por el usuario.";
        }
        catch (Exception ex)
        {
            TxtCurrentAction.Text = $"✖ Error: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void BtnRunSfc_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        ResetStepsToPending();
        TxtCurrentAction.Text = "Iniciando examen SFC /scannow...";
        _cts = new CancellationTokenSource();

        try
        {
            var res = await _repairService.RunSfcOnlyAsync(_cts.Token);
            TxtCurrentAction.Text = res.Success
                ? "✔ SFC /scannow completado exitosamente."
                : "⚠ SFC finalizó con errores o advertencias.";
        }
        catch (OperationCanceledException)
        {
            TxtCurrentAction.Text = "🛑 SFC cancelado por el usuario.";
        }
        catch (Exception ex)
        {
            TxtCurrentAction.Text = $"✖ Error: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void BtnRunWinUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        ResetStepsToPending();
        TxtCurrentAction.Text = "Iniciando reseteo de Windows Update...";
        _cts = new CancellationTokenSource();

        try
        {
            var res = await _repairService.RunWinUpdateResetOnlyAsync(_cts.Token);
            TxtCurrentAction.Text = res.Success
                ? "✔ Reseteo de Windows Update completado."
                : "⚠ Reseteo finalizado con advertencias.";
        }
        catch (OperationCanceledException)
        {
            TxtCurrentAction.Text = "🛑 Reseteo cancelado por el usuario.";
        }
        catch (Exception ex)
        {
            TxtCurrentAction.Text = $"✖ Error: {ex.Message}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            TxtCurrentAction.Text = "Cancelando operación en curso...";
            BtnCancel.IsEnabled = false;
            _cts.Cancel();
        }
    }

    private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TxtConsole.Text);
            TxtConsoleFooter.Text = "¡Logs copiados exitosamente al portapapeles!";
        }
        catch (Exception ex)
        {
            TxtConsoleFooter.Text = $"Error al copiar al portapapeles: {ex.Message}";
        }
    }

    private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        TxtConsole.Clear();
        TxtConsoleFooter.Text = "Pantalla de terminal limpiada.";
    }

    private void BadgeAdmin_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            var mbr = MessageBox.Show(
                "OmniWin se reiniciará con permisos de Administrador para habilitar todas las operaciones de reparación profunda.\n\n¿Deseas reiniciar ahora?",
                "Reiniciar como Administrador",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (mbr == MessageBoxResult.Yes)
            {
                SecurityHelper.RestartAsAdministrator();
            }
        }
    }
}