using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

/// <summary>
/// Vista de telemetría de hardware con gráficos en vivo (Sparklines) y panel de control de Prometheus Exporter.
/// </summary>
public partial class HardwareTelemetryControl : UserControl
{
    private const int HistoryPoints = 60;
    private readonly double[] _cpuHistory = new double[HistoryPoints];
    private readonly double[] _ramHistory = new double[HistoryPoints];

    private readonly DispatcherTimer _timer = new();
    private readonly MemoryService _memoryService = new();
    private readonly MetricsExporterService _metricsExporter = MetricsExporterService.Instance;
    private readonly RtssService _rtssService = new();

    public HardwareTelemetryControl()
    {
        InitializeComponent();

        _timer.Interval = TimeSpan.FromSeconds(1.0);
        _timer.Tick += Timer_Tick;

        Loaded += HardwareTelemetryControl_Loaded;
        Unloaded += HardwareTelemetryControl_Unloaded;

        _metricsExporter.StateChanged += OnExporterStateChanged;
        OverlayManager.VisibilityChanged += OnOverlayVisibilityChanged;
    }

    private void HardwareTelemetryControl_Loaded(object sender, RoutedEventArgs e)
    {
        // Iniciar el servidor Prometheus automáticamente si no está corriendo
        if (!_metricsExporter.IsRunning)
        {
            try
            {
                _metricsExporter.Start(9182);
                SetFeedbackNotice(string.Empty);
            }
            catch (Exception ex)
            {
                SetFeedbackNotice($"Puerto 9182 en conflicto con otro servicio: {ex.Message}", isError: true);
            }
        }

        UpdateExporterUi();
        UpdateOverlayAndRtssUi();
        SampleAndRender();
        RefreshGpuDiagnosticsAsync();
        UpdateTimerResolutionUi();
        RefreshMsiDiagnosticsAsync();

        _timer.Start();
    }

    private void HardwareTelemetryControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        OverlayManager.VisibilityChanged -= OnOverlayVisibilityChanged;
    }

    private void OnExporterStateChanged(bool isRunning)
    {
        Dispatcher.Invoke(UpdateExporterUi);
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        SampleAndRender();
    }

    private void SampleAndRender()
    {
        try
        {
            // 1. Muestreo de CPU
            double cpuVal = _metricsExporter.GetCpuUsagePercent();
            Array.Copy(_cpuHistory, 1, _cpuHistory, 0, HistoryPoints - 1);
            _cpuHistory[HistoryPoints - 1] = cpuVal;

            // 2. Muestreo de Memoria RAM
            var mem = _memoryService.GetMemoryStats();
            double ramVal = mem.UsagePercentage;
            Array.Copy(_ramHistory, 1, _ramHistory, 0, HistoryPoints - 1);
            _ramHistory[HistoryPoints - 1] = ramVal;

            // 3. Textos y estadísticas
            TxtCpuCurrentVal.Text = $"{cpuVal:F1}%";
            double cpuAvg = _cpuHistory.Average();
            double cpuMax = _cpuHistory.Max();
            TxtCpuStats.Text = $"Historial últimos 60s • Promedio: {cpuAvg:F1}% • Pico: {cpuMax:F1}%";

            TxtRamCurrentVal.Text = $"{ramVal:F1}%";
            double usedGb = mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            double totalGb = mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            double availGb = mem.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            TxtRamStats.Text = $"Usada: {usedGb:F1} GB / {totalGb:F1} GB • Disponible: {availGb:F1} GB";

            // 4. Actualizar contadores del exportador
            TxtScrapesCount.Text = _metricsExporter.ScrapesCount.ToString("N0");
            if (_metricsExporter.IsRunning && _metricsExporter.StartedAt.HasValue)
            {
                var span = DateTime.Now - _metricsExporter.StartedAt.Value;
                TxtServerUptime.Text = span.ToString(@"hh\:mm\:ss");
            }
            else
            {
                TxtServerUptime.Text = "--:--:--";
            }

            // 5. Dibujar Sparklines en los Canvas
            RenderSparkline(CanvasCpu, PolyLineCpu, PolyAreaCpu, DotCurrentCpu, _cpuHistory);
            RenderSparkline(CanvasRam, PolyLineRam, PolyAreaRam, DotCurrentRam, _ramHistory);

            // 6. Alimentar buffer circular forense de Stutter Investigator
            StutterInvestigatorService.Instance.RecordSnapshot(new TelemetrySnapshot
            {
                Timestamp = DateTime.UtcNow,
                CpuLoadPercent = cpuVal,
                AvailableRamMb = mem.AvailablePhysicalBytes / (1024 * 1024),
                ThermalThrottling = false
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareTelemetryControl Error] {ex.Message}");
        }
    }

    private void RenderSparkline(Canvas canvas, Polyline polyline, Polygon polygon, Ellipse dot, double[] history)
    {
        double width = canvas.ActualWidth;
        double height = canvas.ActualHeight;

        if (width <= 10 || height <= 10) return;

        double topPad = 8.0;
        double bottomPad = 8.0;
        double plotHeight = height - topPad - bottomPad;

        var linePoints = new PointCollection(HistoryPoints);
        var areaPoints = new PointCollection(HistoryPoints + 2);

        // Origen inferior izquierdo para el polígono de sombreado
        areaPoints.Add(new Point(0, height));

        double stepX = width / (HistoryPoints - 1);
        Point lastPoint = new(0, 0);

        for (int i = 0; i < HistoryPoints; i++)
        {
            double val = Math.Clamp(history[i], 0.0, 100.0);
            double x = i * stepX;
            double y = topPad + plotHeight * (1.0 - (val / 100.0));

            var pt = new Point(x, y);
            linePoints.Add(pt);
            areaPoints.Add(pt);

            if (i == HistoryPoints - 1)
            {
                lastPoint = pt;
            }
        }

        // Cierre inferior derecho para el polígono de sombreado
        areaPoints.Add(new Point(width, height));

        linePoints.Freeze();
        areaPoints.Freeze();

        polyline.Points = linePoints;
        polygon.Points = areaPoints;

        // Posicionar indicador luminoso del punto actual
        Canvas.SetLeft(dot, Math.Clamp(lastPoint.X - dot.Width / 2.0, 0, width - dot.Width));
        Canvas.SetTop(dot, Math.Clamp(lastPoint.Y - dot.Height / 2.0, 0, height - dot.Height));
        dot.Visibility = Visibility.Visible;
    }

    private void CanvasCpu_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderSparkline(CanvasCpu, PolyLineCpu, PolyAreaCpu, DotCurrentCpu, _cpuHistory);
    }

    private void CanvasRam_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderSparkline(CanvasRam, PolyLineRam, PolyAreaRam, DotCurrentRam, _ramHistory);
    }

    private void UpdateExporterUi()
    {
        if (_metricsExporter.IsRunning)
        {
            TxtServerTitle.Text = $"Servidor Prometheus Activo en {_metricsExporter.MetricsUrl}";
            TxtServerStatusBadge.Text = "ACTIVO";
            BadgeServerStatus.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            DotServerStatus.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtServerStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));

            BtnToggleServer.Content = "⏹️ Detener Servidor";
            BtnToggleServer.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));

            TxtEndpointUrl.Text = _metricsExporter.MetricsUrl;
            TxtEndpointUrl.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        }
        else
        {
            TxtServerTitle.Text = $"Servidor Prometheus Inactivo (Puerto {_metricsExporter.Port})";
            TxtServerStatusBadge.Text = "DETENIDO";
            BadgeServerStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            DotServerStatus.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            TxtServerStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));

            BtnToggleServer.Content = "▶️ Iniciar Servidor";
            BtnToggleServer.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199));

            TxtEndpointUrl.Text = $"http://localhost:{_metricsExporter.Port}/metrics (Detenido)";
            TxtEndpointUrl.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            TxtServerUptime.Text = "--:--:--";
        }
    }

    private void SetFeedbackNotice(string msg, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(msg))
        {
            BoxFeedbackNotice.Visibility = Visibility.Collapsed;
            TxtActionFeedback.Text = string.Empty;
            return;
        }

        BoxFeedbackNotice.Visibility = Visibility.Visible;
        TxtActionFeedback.Text = msg;
        if (isError)
        {
            IcoActionFeedback.Text = "⚠";
            TxtActionFeedback.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            BoxFeedbackNotice.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            BoxFeedbackNotice.BorderBrush = new SolidColorBrush(Color.FromRgb(153, 27, 27));
        }
        else
        {
            IcoActionFeedback.Text = "✔";
            TxtActionFeedback.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            BoxFeedbackNotice.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            BoxFeedbackNotice.BorderBrush = new SolidColorBrush(Color.FromRgb(4, 120, 87));
        }
    }

    private void BtnToggleServer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_metricsExporter.IsRunning)
            {
                _metricsExporter.Stop();
                SetFeedbackNotice("Servidor detenido correctamente.", isError: false);
            }
            else
            {
                _metricsExporter.Start(9182);
                SetFeedbackNotice("Servidor iniciado en http://localhost:9182/metrics", isError: false);
            }
        }
        catch (Exception ex)
        {
            SetFeedbackNotice($"Error al iniciar servidor en puerto 9182: {ex.Message}", isError: true);
            MessageBox.Show($"No se pudo cambiar el estado del servidor:\n{ex.Message}", "OmniWin Prometheus", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateExporterUi();
    }

    private void BtnCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_metricsExporter.MetricsUrl);
            SetFeedbackNotice("URL copiada al portapapeles con éxito.", isError: false);
        }
        catch (Exception ex)
        {
            SetFeedbackNotice($"Error al copiar: {ex.Message}", isError: true);
        }
    }

    private void BtnOpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_metricsExporter.IsRunning)
            {
                _metricsExporter.Start(9182);
                UpdateExporterUi();
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _metricsExporter.MetricsUrl,
                UseShellExecute = true
            });

            TxtActionFeedback.Text = "Abriendo http://localhost:9182/metrics en navegador...";
        }
        catch (Exception ex)
        {
            TxtActionFeedback.Text = $"Error al abrir: {ex.Message}";
        }
    }

    private void OnOverlayVisibilityChanged(bool isVisible)
    {
        Dispatcher.Invoke(UpdateOverlayAndRtssUi);
    }

    private void UpdateOverlayAndRtssUi()
    {
        bool isActive = OverlayManager.IsActive;
        DotOverlayStatus.Fill = isActive ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) : new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        TxtOverlayStatusBadge.Text = isActive ? "ACTIVO" : "INACTIVO";
        TxtOverlayStatusBadge.Foreground = isActive ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)) : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        BadgeOverlayStatus.Background = isActive ? new SolidColorBrush(Color.FromRgb(0x06, 0x4E, 0x3B)) : new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B));

        BtnToggleOverlayHud.Content = isActive ? "⏹️ Ocultar HUD" : "🎮 Abrir HUD en Juego";
        BtnToggleOverlayHud.Background = isActive ? new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)) : new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));

        bool rtssInstalled = _rtssService.IsRtssInstalled();
        bool rtssRunning = _rtssService.IsRtssRunning();

        TxtRtssInstalled.Text = rtssInstalled ? "✔ Detectado" : "No detectado";
        TxtRtssInstalled.Foreground = rtssInstalled ? new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)) : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

        TxtRtssRunning.Text = rtssRunning ? "🟢 En ejecución" : "⚪ Detenido";
        TxtRtssRunning.Foreground = rtssRunning ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)) : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
    }

    private void BtnToggleOverlayHud_Click(object sender, RoutedEventArgs e)
    {
        OverlayManager.ToggleOverlay();
        UpdateOverlayAndRtssUi();
    }

    private void BtnConfigOverlayHud_Click(object sender, RoutedEventArgs e)
    {
        OverlayManager.ShowOverlay();
        OverlayManager.Current.SetClickThrough(false);
        OverlayManager.Current.DrawerSettings.Visibility = Visibility.Visible;
        UpdateOverlayAndRtssUi();
    }

    private bool _rtssSyncEnabled = true;
    private void BtnToggleRtssSync_Click(object sender, RoutedEventArgs e)
    {
        _rtssSyncEnabled = !_rtssSyncEnabled;
        BtnToggleRtssSync.Content = _rtssSyncEnabled ? "🔄 Sync RTSS: On" : "🔄 Sync RTSS: Off";
        OverlayManager.Current.SyncWithRtss = _rtssSyncEnabled;
    }

    private void BtnRefreshGpuDiag_Click(object sender, RoutedEventArgs e)
    {
        RefreshGpuDiagnosticsAsync();
    }

    private async void RefreshGpuDiagnosticsAsync()
    {
        try
        {
            BtnRefreshGpuDiag.IsEnabled = false;
            BtnRefreshGpuDiag.Content = "⏳ Analizando...";

            var status = await Task.Run(() => NvidiaGpuTuningService.Instance.GetStatus());
            var pcieReport = await Task.Run(() => PcieLinkInspector.Instance.RunDoctorCheck());

            if (!status.IsNvidiaGpuDetected)
            {
                var fallbackGpu = pcieReport.Devices.FirstOrDefault(d => d.DeviceClass == "GPU");
                if (fallbackGpu != null)
                {
                    TxtGpuModelHeader.Text = fallbackGpu.DeviceName;
                    TxtGpuVbiosChip.Text = "PnP Device";
                    TxtGpuSlotChip.Text = fallbackGpu.DeviceId.Length > 20 ? fallbackGpu.DeviceId[..20] : fallbackGpu.DeviceId;
                    TxtGpuVendorChip.Text = fallbackGpu.DeviceName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || fallbackGpu.DeviceName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD" : "Intel / Genérico";
                    TxtGpuDriverChip.Text = "WDDM Driver";

                    if (fallbackGpu.CurrentLinkWidthLanes > 0 && fallbackGpu.MaxLinkWidthLanes > 0)
                    {
                        TxtPcieWidthVal.Text = fallbackGpu.CurrentWidthString;
                        TxtPcieWidthMaxVal.Text = $" / {fallbackGpu.MaxWidthString} max";
                        double pct = (double)fallbackGpu.CurrentLinkWidthLanes / fallbackGpu.MaxLinkWidthLanes * 100.0;
                        PbPcieWidth.Value = pct;
                        TxtPcieWidthPercent.Text = $"{pct:F0}% del ancho de banda nativo";

                        if (fallbackGpu.IsDegraded)
                        {
                            TxtPcieWidthVal.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                            PbPcieWidth.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                            BoxWidthBottleneck.Visibility = Visibility.Visible;
                            TxtWidthBottleneckMsg.Text = fallbackGpu.DiagnosticMessage;
                        }
                        else
                        {
                            TxtPcieWidthVal.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                            PbPcieWidth.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                            BoxWidthBottleneck.Visibility = Visibility.Collapsed;
                        }
                    }

                    TxtPcieGenVal.Text = fallbackGpu.CurrentSpeedString;
                    TxtPcieGenDetails.Text = $"Capacidad: {fallbackGpu.MaxSpeedString}";
                    BoxReBarNotice.Visibility = Visibility.Collapsed;
                    return;
                }

                TxtGpuModelHeader.Text = "No se detectó GPU discreta";
                TxtGpuVbiosChip.Text = "N/A";
                TxtGpuSlotChip.Text = "N/A";
                TxtGpuVendorChip.Text = "N/A";
                TxtGpuDriverChip.Text = "N/A";
                BoxWidthBottleneck.Visibility = Visibility.Collapsed;
                BoxReBarNotice.Visibility = Visibility.Collapsed;
                return;
            }

            // Modelo y chips de identificación
            TxtGpuModelHeader.Text = status.GpuModel;
            TxtGpuVbiosChip.Text = string.IsNullOrWhiteSpace(status.VbiosVersion) ? "Desconocido" : status.VbiosVersion;
            TxtGpuSlotChip.Text = string.IsNullOrWhiteSpace(status.PciBusId) ? "N/A" : status.PciBusId;
            TxtGpuVendorChip.Text = string.IsNullOrWhiteSpace(status.SubsystemVendor) ? "OEM / Genérico" : status.SubsystemVendor;
            TxtGpuDriverChip.Text = string.IsNullOrWhiteSpace(status.DriverVersion) ? "N/A" : $"NVIDIA {status.DriverVersion}";

            // Métricas de ancho de banda PCIe
            int currentWidth = status.PcieWidthCurrent;
            int maxWidth = status.PcieWidthMax;
            bool isDegraded = status.IsWidthBottlenecked;
            string bottleneckMsg = status.BottleneckNotice;

            // Cross-check con PcieLinkInspector para GPU NVIDIA
            var pcieGpu = pcieReport.Devices.FirstOrDefault(d => d.DeviceClass == "GPU");
            if (pcieGpu != null)
            {
                if (maxWidth <= 0 && pcieGpu.MaxLinkWidthLanes > 0) maxWidth = pcieGpu.MaxLinkWidthLanes;
                if (currentWidth <= 0 && pcieGpu.CurrentLinkWidthLanes > 0) currentWidth = pcieGpu.CurrentLinkWidthLanes;
                if (pcieGpu.IsDegraded)
                {
                    isDegraded = true;
                    bottleneckMsg = pcieGpu.DiagnosticMessage;
                }
            }

            if (maxWidth > 0 && currentWidth > 0)
            {
                TxtPcieWidthVal.Text = $"x{currentWidth}";
                TxtPcieWidthMaxVal.Text = $" / x{maxWidth} max";
                double pct = (double)currentWidth / maxWidth * 100.0;
                PbPcieWidth.Value = pct;
                TxtPcieWidthPercent.Text = $"{pct:F0}% del ancho de banda nativo";

                if (isDegraded || currentWidth < maxWidth)
                {
                    var alertColor = currentWidth <= 4 ? Color.FromRgb(239, 68, 68) : Color.FromRgb(245, 158, 11);
                    TxtPcieWidthVal.Foreground = new SolidColorBrush(alertColor);
                    PbPcieWidth.Foreground = new SolidColorBrush(alertColor);
                    BoxWidthBottleneck.Visibility = Visibility.Visible;
                    TxtWidthBottleneckMsg.Text = $"⚠ Negociación PCIe reducida a x{currentWidth} (Capacidad física: x{maxWidth}). Ancho de banda al {pct:F0}%. Posibles causas: Ranura secundaria, líneas compartidas con M.2 NVMe en Z790, o ahorro ASPM.";
                }
                else
                {
                    TxtPcieWidthVal.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Verde
                    PbPcieWidth.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                    BoxWidthBottleneck.Visibility = Visibility.Collapsed;
                }
            }

            // Generación PCIe & Estado de Reposo ASPM
            if (status.PcieGenCurrent > 0 && status.PcieGenMax > 0 && status.PcieGenCurrent < status.PcieGenMax)
            {
                TxtPcieGenVal.Text = $"PCIe {status.PcieGenCurrent}.0 (Reposo ASPM)";
                TxtPcieGenVal.FontSize = 16;
                TxtPcieGenVal.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Ámbar
                TxtPcieGenDetails.Text = $"Capaz de Gen {status.PcieGenMax}.0 • Enlace 2D en reposo (P8)";
            }
            else
            {
                TxtPcieGenVal.Text = status.PcieGenCurrent > 0 ? $"PCIe {status.PcieGenCurrent}.0" : "PCIe Gen 4.0";
                TxtPcieGenVal.FontSize = 22;
                TxtPcieGenVal.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Verde
                TxtPcieGenDetails.Text = $"GPU: Gen {status.PcieGenMax}.0 • Host: Gen {status.PcieGenHostMax}.0";
            }

            // Apertura de Resizable BAR (BAR1)
            if (status.Bar1TotalMb > 0)
            {
                TxtReBarApertureVal.Text = $"{status.Bar1TotalMb} MiB";
                TxtReBarUsed.Text = $"En uso: {status.Bar1UsedMb} / {status.Bar1TotalMb} MiB";

                if (status.IsReBarHardwareActive)
                {
                    BadgeReBarState.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                    TxtReBarBadge.Text = "ACTIVO FULL VRAM";
                    TxtReBarBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    TxtReBarApertureVal.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    TxtReBarSubtext.Text = "Acceso directo completo de CPU a VRAM";
                    BoxReBarNotice.Visibility = Visibility.Collapsed;
                }
                else
                {
                    BadgeReBarState.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
                    TxtReBarBadge.Text = "INACTIVO (BIOS)";
                    TxtReBarBadge.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    TxtReBarApertureVal.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                    TxtReBarSubtext.Text = "Apertura BAR1 Legacy (32-bit: 256 MiB)";
                    BoxReBarNotice.Visibility = Visibility.Visible;
                }
            }

            // VRAM Total
            if (status.VramTotalMb > 0)
            {
                TxtVramTotalVal.Text = $"{status.VramTotalMb} MiB";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GPU Diag Refresh Error] {ex.Message}");
        }
        finally
        {
            BtnRefreshGpuDiag.IsEnabled = true;
            BtnRefreshGpuDiag.Content = "🔄 Re-analizar";
        }
    }

    private async void BtnAuditPcie_Click(object sender, RoutedEventArgs e)
    {
        BtnAuditPcie.IsEnabled = false;
        BtnAuditPcie.Content = "⏳ Auditando...";
        try
        {
            string report = await PcieLinkInspector.Instance.AuditDetailedBandwidthAsync();
            MessageBox.Show(report, "OmniWin — Auditoría de Bus PCIe & NVMe", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error en auditoría: {ex.Message}", "OmniWin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnAuditPcie.IsEnabled = true;
            BtnAuditPcie.Content = "⚡ Auditar Bus PCIe";
        }
    }

    private void BtnTriggerStutterCheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var report = StutterInvestigatorService.Instance.AnalyzeRecentStutter(TimeSpan.FromSeconds(30));
            TxtLatencyVerdict.Text = $"[Stutter] {report.ProbableCause}: {report.Summary}";
            TxtLatencyVerdict.Foreground = report.ProbableCause == "Sin anomalías evidentes"
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }
        catch (Exception ex)
        {
            TxtLatencyVerdict.Text = $"Error: {ex.Message}";
        }
    }

    private readonly KernelLatencyService _latencyService = new();

    private void UpdateTimerResolutionUi()
    {
        try
        {
            var info = _latencyService.GetTimerResolution();
            TxtTimerCurrent.Text = info.FormattedCurrent;
            TxtTimerMax.Text = info.FormattedMax;
            if (info.IsOptimizedForGaming)
            {
                TxtTimerCurrent.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
        }
        catch { }
    }

    private void BtnApplyHighResTimer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool ok = _latencyService.SetHighResolutionTimer(true);
            UpdateTimerResolutionUi();
            if (ok)
            {
                TxtLatencyVerdict.Text = "Timer de alta resolución (0.5 ms) aplicado en Windows.";
                TxtLatencyVerdict.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
            else
            {
                TxtLatencyVerdict.Text = "No se pudo cambiar la resolución del temporizador (se requieren permisos).";
            }
        }
        catch (Exception ex)
        {
            TxtLatencyVerdict.Text = $"Error: {ex.Message}";
        }
    }

    private async void BtnBenchmarkLatency_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnBenchmarkLatency.IsEnabled = false;
            TxtLatencyVerdict.Text = "Ejecutando muestreo de jitter y latencia de kernel (5000 muestras)...";

            var result = await _latencyService.RunJitterBenchmarkAsync(5000);
            TxtLatencyJitter.Text = $"{result.MaxJitterMicroseconds:F1} µs (Prom: {result.AverageJitterMicroseconds:F2} µs)";
            TxtLatencyVerdict.Text = result.Verdict;

            var converter = new BrushConverter();
            if (converter.ConvertFromString(result.VerdictColor) is Brush brush)
            {
                TxtLatencyVerdict.Foreground = brush;
                TxtLatencyJitter.Foreground = brush;
            }
        }
        catch (Exception ex)
        {
            TxtLatencyVerdict.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnBenchmarkLatency.IsEnabled = true;
            UpdateTimerResolutionUi();
        }
    }

    private async void RefreshMsiDiagnosticsAsync()
    {
        try
        {
            var report = await System.Threading.Tasks.Task.Run(() => MsiInterruptService.Instance.RunDoctorReport());
            TxtMsiTotalDevices.Text = report.TotalDevices.ToString();
            TxtMsiEnabledDevices.Text = report.MsiActiveCount.ToString();
            TxtMsiLegacyDevices.Text = report.LineBasedCount.ToString();
            TxtMsiUnoptimizedCount.Text = report.RecommendedToOptimizeCount.ToString();
            TxtMsiUnoptimizedCount.Foreground = report.RecommendedToOptimizeCount == 0
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            PanelMsiDevices.Children.Clear();

            // Filtrar dispositivos relevantes para gaming y baja latencia
            var criticalDevices = report.Devices
                .Where(d => d.IsRecommended || 
                            d.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase) || 
                            d.DeviceClass.Equals("Net", StringComparison.OrdinalIgnoreCase) || 
                            d.DeviceClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase) || 
                            d.DeviceClass.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase) ||
                            d.DeviceClass.Equals("HDC", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.IsRecommended)
                .ThenBy(d => d.DeviceClass)
                .ToList();

            if (criticalDevices.Count == 0)
            {
                PanelMsiDevices.Children.Add(new TextBlock
                {
                    Text = "No se detectaron dispositivos PCIe relevantes.",
                    Foreground = (Brush)FindResource("TextSecondary"),
                    Margin = new Thickness(6)
                });
                return;
            }

            foreach (var dev in criticalDevices)
            {
                var rowBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    BorderBrush = (Brush)FindResource("CardBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Badge tipo
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Nombre
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Modo MSI pill
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Prioridad pill
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Botón acción

                // 1. Badge Categoría
                string catName = dev.DeviceClass.ToUpperInvariant() switch
                {
                    "DISPLAY" => "GPU",
                    "NET" => "RED",
                    "MEDIA" => "AUDIO",
                    "SCSIADAPTER" or "HDC" => "DISCO",
                    _ => dev.DeviceClass
                };

                Color catColor = catName switch
                {
                    "GPU" => Color.FromRgb(129, 140, 248),
                    "RED" => Color.FromRgb(56, 189, 248),
                    "AUDIO" => Color.FromRgb(245, 158, 11),
                    "DISCO" => Color.FromRgb(16, 185, 129),
                    _ => Color.FromRgb(148, 163, 184)
                };

                var catBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(40, catColor.R, catColor.G, catColor.B)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                catBadge.Child = new TextBlock
                {
                    Text = catName,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(catColor)
                };
                Grid.SetColumn(catBadge, 0);
                grid.Children.Add(catBadge);

                // 2. Nombre dispositivo
                var txtName = new TextBlock
                {
                    Text = dev.FriendlyName,
                    ToolTip = $"Key: {dev.DeviceKeyPath}\nClase: {dev.DeviceClass}",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(txtName, 1);
                grid.Children.Add(txtName);

                // 3. Modo MSI Pill
                bool isMsi = dev.MsiSupported;
                var modeBadge = new Border
                {
                    Background = new SolidColorBrush(isMsi ? Color.FromArgb(35, 16, 185, 129) : Color.FromArgb(35, 245, 158, 11)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                modeBadge.Child = new TextBlock
                {
                    Text = isMsi ? "MSI-X" : "Line-IRQ",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(isMsi ? Color.FromRgb(52, 211, 153) : Color.FromRgb(251, 191, 36))
                };
                Grid.SetColumn(modeBadge, 2);
                grid.Children.Add(modeBadge);

                // 4. Prioridad Pill
                var prioBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 100, 116, 139)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                prioBadge.Child = new TextBlock
                {
                    Text = dev.Priority,
                    FontSize = 10,
                    Foreground = string.Equals(dev.Priority, "High", StringComparison.OrdinalIgnoreCase)
                        ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                        : new SolidColorBrush(Color.FromRgb(148, 163, 184))
                };
                Grid.SetColumn(prioBadge, 3);
                grid.Children.Add(prioBadge);

                // 5. Botón Acción rápida
                bool needsOpt = !dev.MsiSupported || !string.Equals(dev.Priority, "High", StringComparison.OrdinalIgnoreCase);
                var btnAction = new Button
                {
                    Content = needsOpt ? "⚡ MSI High" : "✓ Óptimo",
                    IsEnabled = needsOpt,
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10.5,
                    Background = needsOpt ? new SolidColorBrush(Color.FromRgb(2, 132, 199)) : new SolidColorBrush(Color.FromArgb(20, 16, 185, 129)),
                    Foreground = Brushes.White,
                    Tag = dev.DeviceKeyPath
                };
                btnAction.Click += (s, ev) =>
                {
                    try
                    {
                        bool ok = MsiInterruptService.Instance.SetMsiMode(dev.DeviceKeyPath, enable: true, priority: "High");
                        TxtMsiFeedback.Text = ok ? $"Actualizado: {dev.FriendlyName}" : "No se pudo actualizar la clave de registro.";
                        RefreshMsiDiagnosticsAsync();
                    }
                    catch (Exception ex)
                    {
                        TxtMsiFeedback.Text = $"Error: {ex.Message}";
                    }
                };
                Grid.SetColumn(btnAction, 4);
                grid.Children.Add(btnAction);

                rowBorder.Child = grid;
                PanelMsiDevices.Children.Add(rowBorder);
            }
        }
        catch (Exception ex)
        {
            TxtMsiFeedback.Text = $"Error leyendo MSI: {ex.Message}";
        }
    }

    private void BtnRefreshMsi_Click(object sender, RoutedEventArgs e)
    {
        RefreshMsiDiagnosticsAsync();
    }

    private async void BtnOptimizeMsi_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnOptimizeMsi.IsEnabled = false;
            TxtMsiFeedback.Text = "Aplicando modo MSI y prioridad alta en GPU y adaptadores de red...";

            int count = await System.Threading.Tasks.Task.Run(() => 
                MsiInterruptService.Instance.OptimizeRecommendedGamingDevices());

            TxtMsiFeedback.Text = count > 0 
                ? $"Optimización completada: {count} dispositivo(s) configurados en MSI Mode (High Priority)."
                : "Todos los dispositivos recomendados ya se encuentran configurados en MSI Mode.";
            RefreshMsiDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            TxtMsiFeedback.Text = $"Error: {ex.Message}";
        }
        finally
        {
            BtnOptimizeMsi.IsEnabled = true;
        }
    }
}
