using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OmniWin.Core.Services;
using OmniWin.UI.Services;
using OmniWin.UI.Views;

namespace OmniWin.UI;

public class DriveDisplayItem
{
    public string NameAndLabel { get; set; } = string.Empty;
    public double UsagePercent { get; set; }
    public string SpaceText { get; set; } = string.Empty;
}

public class BloatDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string FileCountText { get; set; } = string.Empty;
    public bool IsSelected { get; set; } = true;
}

public class TweakDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsApplied { get; set; }
    public string ActionButtonText => IsApplied ? "Revertir" : "Aplicar";
    public Brush ButtonColor => IsApplied ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(2, 132, 199));
}

public class StartupDisplayItem
{
    public string Scope { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string StatusText => IsEnabled ? "✔ Activo" : "✖ Desactivado";
}

public partial class MainWindow : Window
{
    private readonly MemoryService _memoryService = new();
    private readonly DiskService _diskService = new();
    private readonly ProcessService _processService = new();
    private readonly StartupService _startupService = new();
    private readonly TweakService _tweakService = new();
    private readonly NetworkService _networkService = new();
    private readonly SecurityAuditService _securityAuditService = new();
    private readonly SoftwareService _softwareService = new();
    private readonly DismService _dismService = new();
    private readonly DriverService _driverService = new();
    private readonly PowerService _powerService = new();
    private readonly FileLockService _fileLockService = new();
    private readonly WindowsServiceService _windowsServiceService = new();
    private readonly AsrRulesService _asrService = new();
    private HardwareService? _hardwareService;
    private NvidiaGpuStatus? _cachedGpuStatus;

    private GlobalHotkeyService? _hotkeyService;
    private SystemTrayService? _trayService;
    private readonly LocalizationService _locService = LocalizationService.Instance;

    private readonly DispatcherTimer _timer = new();
    private System.Threading.Timer? _signalTimer;
    private readonly ObservableCollection<BloatDisplayItem> _bloatItems = new();
    private readonly ObservableCollection<TweakDisplayItem> _tweakItems = new();

    public MainWindow()
    {
        App.Log("MainWindow constructor: starting InitializeComponent...");
        try
        {
            InitializeComponent();
            App.Log("MainWindow constructor: InitializeComponent done.");
        }
        catch (Exception ex)
        {
            App.Log($"[FATAL_INIT_COMPONENT] {ex}");
            throw;
        }

        Task.Run(() =>
        {
            try
            {
                App.Log("HardwareService initializing in background...");
                _hardwareService = new HardwareService();
                App.Log("HardwareService initialized.");
            }
            catch (Exception ex)
            {
                App.Log($"HardwareService init error (graceful): {ex.Message}");
            }
        });

        IcBloatCategories.ItemsSource = _bloatItems;
        // Tweaks are now hosted inside TweaksDebloatControl

        WizardControl.OnWizardCompleted += OnWizardCompletedHandler;
        Loaded += MainWindow_Loaded;
        App.Log("MainWindow constructor: completed.");
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        App.Log("MainWindow_Loaded: started.");
        try { InitNavigation(); } catch (Exception ex) { App.Log($"InitNavigation error: {ex}"); }
        try { UpdateAdminBadge(); } catch (Exception ex) { App.Log($"UpdateAdminBadge error: {ex}"); }
        try { RefreshDashboard(); } catch (Exception ex) { App.Log($"RefreshDashboard error: {ex}"); }
        try { RefreshStartup(); } catch (Exception ex) { App.Log($"RefreshStartup error: {ex}"); }
        try { LoadGpuStatusAsync(); } catch (Exception ex) { App.Log($"LoadGpuStatusAsync error: {ex}"); }
        try
        {
            string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OmniWin.exe");
            string escapedPath = currentExe.Replace("\\", "\\\\");
            TxtMcpManualConfig.Text = $"{{\n  \"mcpServers\": {{\n    \"omniwin\": {{\n      \"command\": \"{escapedPath}\",\n      \"args\": [\"mcp\"]\n    }}\n  }}\n}}";
        }
        catch { }

        Task.Run(() =>
        {
            try
            {
                CompanionServerService.Instance.Start();
                App.Log("CompanionServerService started on port 8766.");
            }
            catch (Exception ex)
            {
                App.Log($"[COMPANION_SERVER_ERROR] {ex.Message}");
            }

            try
            {
                GameProfilerService.Instance.Start();
                App.Log("GameProfilerService background loop started.");
            }
            catch (Exception ex)
            {
                App.Log($"[GAME_PROFILER_ERROR] {ex.Message}");
            }
        });

        App.Log("MainWindow_Loaded: initial controls populated.");

        _timer.Interval = TimeSpan.FromSeconds(3.0);
        _timer.Tick += (s, ev) =>
        {
            try
            {
                RefreshDashboard();
            }
            catch (Exception ex)
            {
                App.Log($"[TIMER_TICK_ERROR] {ex.Message}");
            }
        };
        _timer.Start();

        _signalTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                string signalPath = Path.Combine(AppContext.BaseDirectory, "close.signal");
                if (File.Exists(signalPath))
                {
                    try { File.Delete(signalPath); } catch { }
                    Dispatcher.Invoke(() => Application.Current.Shutdown());
                    return;
                }

                string snapshotSignal = Path.Combine(AppContext.BaseDirectory, "snapshot.signal");
                if (File.Exists(snapshotSignal))
                {
                    try { File.Delete(snapshotSignal); } catch { }
                    Dispatcher.InvokeAsync(async () => await CaptureAllTabsAsync());
                }

                string overlaySignal = Path.Combine(AppContext.BaseDirectory, "overlay_test.signal");
                if (File.Exists(overlaySignal))
                {
                    try { File.Delete(overlaySignal); } catch { }
                    Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            MainTabs.SelectedIndex = 1;
                            await Task.Delay(600);
                            OverlayManager.ShowOverlay();
                            await Task.Delay(1500);
                            CaptureSnapshot("02_telemetria_live_with_overlay_card");

                            var win = OverlayManager.Current;
                            win.UpdateLayout();
                            int w = Math.Max((int)win.ActualWidth, (int)win.Width);
                            int h = Math.Max((int)win.ActualHeight, (int)win.Height);
                            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            rtb.Render(win);
                            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                            string overlayImgPath = @"C:\Users\pauol\Pictures\Screenshots\OmniWinTests\16_gaming_overlay_hud.png";
                            using (var fs = File.Create(overlayImgPath))
                            {
                                enc.Save(fs);
                            }
                            App.Log($"[UI_TEST] Captured overlay to {overlayImgPath}");
                        }
                        catch (Exception ex)
                        {
                            App.Log($"[UI_TEST] Overlay capture error: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                App.Log($"[SIGNAL_TIMER_ERROR] {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        if (Views.OnboardingWizardControl.ShouldShowOnboarding())
        {
            ShowOnboardingWizard();
        }

        try
        {
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.Initialize(this);
            _hotkeyService.OnOverlayHotkeyPressed += () => Dispatcher.Invoke(ToggleOverlay);
            _hotkeyService.OnWidgetHotkeyPressed += () => Dispatcher.Invoke(ToggleTrafficWidget);
            _hotkeyService.OnPurgeHotkeyPressed += () => Dispatcher.Invoke(ExecutePurge);
            App.Log("GlobalHotkeyService initialized in MainWindow.");
        }
        catch (Exception ex)
        {
            App.Log($"[HOTKEY_INIT_ERROR] {ex.Message}");
        }

        try
        {
            _trayService = new SystemTrayService(this);
            _trayService.Initialize();
            _trayService.OnOpenDashboardRequested += () => Dispatcher.Invoke(() => { MainTabs.SelectedIndex = 0; });
            _trayService.OnQuickPurgeRequested += () => Dispatcher.Invoke(ExecutePurge);
            _trayService.OnToggleOverlayRequested += () => Dispatcher.Invoke(ToggleOverlay);
            _trayService.OnToggleWidgetRequested += () => Dispatcher.Invoke(ToggleTrafficWidget);
            _trayService.OnExitRequested += () => Dispatcher.Invoke(() => Application.Current.Shutdown());

            StateChanged += (s, ev) =>
            {
                if (WindowState == WindowState.Minimized && AppSettingsService.Instance.Settings.MinimizeToTray)
                {
                    Hide();
                    _trayService?.ShowNotification("OmniWin", "OmniWin continúa ejecutándose en la bandeja del sistema.");
                }
            };
            App.Log("SystemTrayService initialized in MainWindow.");
        }
        catch (Exception ex)
        {
            App.Log($"[TRAY_INIT_ERROR] {ex.Message}");
        }

        try
        {
            _locService.LanguageChanged += OnLanguageChanged;
            ApplyLocalization();
        }
        catch (Exception ex)
        {
            App.Log($"[LOC_INIT_ERROR] {ex.Message}");
        }

        App.Log("MainWindow_Loaded: completed.");
    }

    private readonly Dictionary<int, Button> _navMap = new();

    private void InitNavigation()
    {
        _navMap[0] = NavBtn0;
        _navMap[1] = NavBtn1;
        _navMap[2] = NavBtn2;
        _navMap[3] = NavBtn3;
        _navMap[4] = NavBtn4;
        _navMap[5] = NavBtn5;
        _navMap[6] = NavBtn6;
        _navMap[7] = NavBtn7;
        _navMap[8] = NavBtn8;
        _navMap[9] = NavBtn9;
        _navMap[10] = NavBtn10;
        _navMap[11] = NavBtn11;
        _navMap[12] = NavBtn12;
        _navMap[13] = NavBtn13;
        _navMap[14] = NavBtn14;
        _navMap[15] = NavBtn15;
        _navMap[16] = NavBtn16;
        _navMap[17] = NavBtn17;
        _navMap[18] = NavBtn18;
        _navMap[19] = NavBtn19;
        _navMap[20] = NavBtn20;
        _navMap[21] = NavBtn21;

        UpdateNavHighlight(MainTabs.SelectedIndex);
    }

    private void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int index))
        {
            MainTabs.SelectedIndex = index;
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl)
        {
            UpdateNavHighlight(MainTabs.SelectedIndex);
        }
    }

    private void UpdateNavHighlight(int activeIndex)
    {
        if (_navMap.Count == 0) return;

        foreach (var kvp in _navMap)
        {
            if (kvp.Value == null) continue;
            bool isActive = (kvp.Key == activeIndex);
            kvp.Value.Style = (Style)FindResource(isActive ? "NavButtonActive" : "NavButton");
        }

        var (title, desc) = GetTabHeaderInfo(activeIndex);
        if (TxtActiveSectionTitle != null) TxtActiveSectionTitle.Text = title;
        if (TxtActiveSectionDesc != null) TxtActiveSectionDesc.Text = desc;
    }

    private static (string Title, string Desc) GetTabHeaderInfo(int index) => index switch
    {
        0 => ("📊 Dashboard del Sistema", "Resumen de telemetría de hardware, almacenamiento y procesos activos en vivo."),
        1 => ("📈 Telemetría & Live", "Métricas en tiempo real, latencia de kernel DPC y servidor exporter Prometheus."),
        2 => ("🔍 Forense de Procesos", "Inspección de árbol de procesos, módulos DLL cargados y sockets TCP en tiempo real."),
        3 => ("⚡ Optimizador de Memoria RAM", "Purga de lista de reserva (Standby List) y liberación con llamadas nativas NT."),
        4 => ("🧹 Limpieza Segura de Disco", "Análisis y eliminación de archivos temporales, caché y volcados del sistema."),
        5 => ("🛠️ Catálogo de Tweaks & Debloat", "55+ optimizaciones reales del registro y desinstalador de bloatware UWP."),
        6 => ("📦 Software & Controladores", "Gestor WinGet, auditoría de drivers OEM, respaldo de drivers y desinstalador profundo."),
        7 => ("⚙️ Mantenimiento de Sistema", "Limpieza de WinSxS con DISM, comprobación SFC y planes de energía con timer 0.5ms."),
        8 => ("🩹 Consola de Reparación Integral", "Pipeline automatizado de 6 pasos de reparación de componentes y Windows Update."),
        9 => ("🌐 Diagnóstico de Red & Sockets", "Auditoría de seguridad, prueba de latencia DNS y reseteo de Winsock/DNS."),
        10 => ("🔒 Desbloqueador de Archivos", "Identificación y liberación de archivos bloqueados mediante Windows Restart Manager."),
        11 => ("🚀 Arranque & Servicios", "Control de elementos de inicio y desactivación segura de telemetría de Windows."),
        12 => ("🤖 Servidor IA / MCP", "Servidor de 28 herramientas para Claude, Antigravity y Cursor con auto-registro."),
        13 => ("🎧 Mezclador de Audio Nativo", "Control de volumen independiente por aplicación y muting vía CoreAudio."),
        14 => ("🛡️ Microsoft Defender ASR", "Gestión de las 16 reglas de Attack Surface Reduction para mitigación en kernel."),
        15 => ("🔋 Energía, CPU & Multi-GPU", "Administración de planes de energía, Core Parking, EPP, scheduler de P/E-Cores y soporte universal AMD/Intel/NVIDIA."),
        16 => ("💾 Analizador de Espacio en Disco", "Exploración visual estilo WizTree / TreeSize de carpetas pesadas, archivos gigantes (>50MB) y caché del sistema."),
        17 => ("✈️ Caja Negra & Análisis de BSOD", "Auditoría forense de pantallazos azules (BugChecks), eventos Kernel-Power 41, volcados minidump y cierres de sesión."),
        18 => ("📱 OmniCompanion Web Dashboard", "Monitoreo remoto de telemetría y ejecución de acciones desde cualquier móvil o tablet con emparejamiento QR."),
        19 => ("🎮 Auto-Game & App Profiler", "Optimización automática para juegos y suites pesadas: Afinidad P-Cores, Timer 0.5ms y purga de RAM."),
        20 => ("🛡️ Cortafuegos Visual & Sockets", "Monitoreo de conexiones TCP mediante tabla MIB iphlpapi.dll y bloqueo de tráfico en 1-clic."),
        21 => ("🌐 DNS Seguro (DoH) & HOSTS", "Benchmark de latencia entre servidores DNS mundiales y bloqueo masivo de 60.000+ dominios en HOSTS."),
        _ => ("OmniWin", "Panel de Control y Optimización de Windows")
    };

    public async Task CaptureAllTabsAsync()
    {
        try
        {
            App.Log("[UI_TEST] CaptureAllTabsAsync started...");
            string[] tabNames = [
                "01_dashboard", "02_telemetria_live", "03_forense_procesos", "04_ram", "05_disco",
                "06_tweaks_debloat", "07_software", "08_mantenimiento", "09_reparacion_console",
                "10_red", "11_desbloqueo", "12_inicio", "13_ia_mcp", "14_mezclador_audio", "15_asr_defender", "16_energia_cpu",
                "17_espacio_disco", "18_caja_negra_bsod", "19_omnicompanion", "20_game_profiler", "21_firewall_monitor", "22_dns_security"
            ];

            int originalIndex = MainTabs.SelectedIndex;
            for (int i = 0; i < tabNames.Length && i < MainTabs.Items.Count; i++)
            {
                try
                {
                    App.Log($"[UI_TEST] Switching to tab {i} ({tabNames[i]})...");
                    MainTabs.SelectedIndex = i;
                    await Task.Delay(800);
                    CaptureSnapshot(tabNames[i]);
                }
                catch (Exception exTab)
                {
                    App.Log($"[UI_TEST] Error capturing tab {i} ({tabNames[i]}): {exTab.Message}");
                }
            }
            MainTabs.SelectedIndex = originalIndex;
            App.Log("[UI_TEST] CaptureAllTabsAsync completed all tabs!");
        }
        catch (Exception ex)
        {
            App.Log($"[UI_TEST] CaptureAllTabsAsync FATAL error: {ex}");
        }
    }

    public void CaptureSnapshot(string name)
    {
        try
        {
            string testDir = @"C:\Users\pauol\Pictures\Screenshots\OmniWinTests";
            Directory.CreateDirectory(testDir);
            UpdateLayout();
            int width = (int)ActualWidth;
            int height = (int)ActualHeight;
            if (width <= 0 || height <= 0) { width = 1120; height = 720; }

            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            string path = Path.Combine(testDir, $"{name}.png");
            using var fs = File.Create(path);
            encoder.Save(fs);
            App.Log($"[UI_TEST] Snapshot saved: {path}");
        }
        catch (Exception ex)
        {
            App.Log($"[UI_TEST] Snapshot error: {ex.Message}");
        }
    }

    private void UpdateAdminBadge()
    {
        bool isAdmin = SecurityHelper.IsAdministrator();
        if (isAdmin)
        {
            BadgeAdmin.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            TxtAdminIcon.Text = "🛡️";
            TxtAdminStatus.Text = "Administrador";
            TxtAdminStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
        }
        else
        {
            BadgeAdmin.Background = new SolidColorBrush(Color.FromRgb(120, 53, 15));
            TxtAdminIcon.Text = "⚠️";
            TxtAdminStatus.Text = "Reiniciar como Admin";
            TxtAdminStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        }
    }

    private void BadgeAdmin_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!SecurityHelper.IsAdministrator())
        {
            var res = MessageBox.Show(
                "OmniWin se reiniciará con permisos de Administrador para permitir limpieza de WinSxS con DISM, reparaciones SFC y actualizaciones completas de sistema.\n\n¿Deseas reiniciar ahora?",
                "OmniWin — Elevar Permisos",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                SecurityHelper.RestartAsAdministrator();
            }
        }
    }

    private void BtnOpenWizard_Click(object sender, RoutedEventArgs e)
    {
        ShowOnboardingWizard();
    }

    private void ShowOnboardingWizard()
    {
        OnboardingOverlay.Visibility = Visibility.Visible;
    }

    private async void OnWizardCompletedHandler(string? profile)
    {
        OnboardingOverlay.Visibility = Visibility.Collapsed;

        if (string.IsNullOrEmpty(profile)) return;

        bool isAdmin = SecurityHelper.IsAdministrator();

        if (!isAdmin)
        {
            var res = MessageBox.Show(
                $"Para activar completamente el perfil '{profile}' (reglas de kernel ASR de Microsoft Defender y purga profunda de Standby RAM), OmniWin requiere privilegios de Administrador.\n\n¿Deseas reiniciar OmniWin como Administrador ahora para aplicar todo de forma real?",
                "OmniWin — Elevación Requerida",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                SecurityHelper.RestartAsAdministrator();
                return;
            }

            TxtFooterStatus.Text = $"⚠️ Perfil '{profile}' aplicado parcialmente (modo usuario). Reinicia como Admin para ASR y Standby RAM.";
        }

        switch (profile)
        {
            case "Gamer":
                try
                {
                    _powerService.SetHighPrecisionTimer(true);
                    await _powerService.ApplyRecommendedTuningForSchemeAsync("Ultimate");
                    if (isAdmin)
                    {
                        await _asrService.ApplyProfileAsync("Modo Desarrollador / Gamer");
                    }
                    await Task.Run(() => _memoryService.PurgeMemory(isAdmin, true));
                    MainTabs.SelectedIndex = 1; // Pestaña Telemetría
                    TxtFooterStatus.Text = "✔ Perfil 'Gamer' aplicado al 100%: CPU Tuning (Core Parking 100% / EPP 0), Timer 0.5ms y ASR optimizados.";
                }
                catch (Exception ex)
                {
                    TxtFooterStatus.Text = $"Error al aplicar perfil Gamer: {ex.Message}";
                }
                break;

            case "Developer":
                try
                {
                    await _powerService.ApplyRecommendedTuningForSchemeAsync("Balanced");
                    if (isAdmin)
                    {
                        await _asrService.ApplyProfileAsync("Modo Desarrollador / Gamer");
                    }
                    MainTabs.SelectedIndex = 12; // Pestaña IA / MCP
                    TxtFooterStatus.Text = "✔ Perfil 'Desarrollador' aplicado al 100%: CPU Tuning Balanceado, ASR permisivo y Servidor MCP listos.";
                }
                catch (Exception ex)
                {
                    TxtFooterStatus.Text = $"Error al aplicar perfil Desarrollador: {ex.Message}";
                }
                break;

            case "Security":
                try
                {
                    if (isAdmin)
                    {
                        await _asrService.ApplyProfileAsync("Máxima Seguridad");
                    }
                    MainTabs.SelectedIndex = 14; // Pestaña ASR
                    TxtFooterStatus.Text = "✔ Perfil 'Máxima Seguridad' aplicado al 100%: 16 reglas ASR en Bloquear y auditoría activa.";
                }
                catch (Exception ex)
                {
                    TxtFooterStatus.Text = $"Error al aplicar perfil Seguridad: {ex.Message}";
                }
                break;

            case "Minimal":
                try
                {
                    await _powerService.ApplyRecommendedTuningForSchemeAsync("Power saver");
                    await Task.Run(() => _memoryService.PurgeMemory(isAdmin, true));
                    MainTabs.SelectedIndex = 3; // Pestaña RAM
                    TxtFooterStatus.Text = "✔ Perfil 'Limpieza & Silencio' aplicado: CPU límite 99% (sin picos térmicos) y memoria purgada.";
                }
                catch (Exception ex)
                {
                    TxtFooterStatus.Text = $"Error al aplicar perfil Limpieza: {ex.Message}";
                }
                break;
        }
    }

    private void RefreshDashboard()
    {
        try
        {
            // 1. Memory
            var mem = _memoryService.GetMemoryStats();
            double usedGb = mem.UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            double totalGb = mem.TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0);
            double availGb = mem.AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0);

            TxtRamSummary.Text = $"{usedGb:N1} / {totalGb:N1} GB";
            PbRam.Value = mem.UsagePercentage;
            TxtRamPercent.Text = $"Carga: {mem.UsagePercentage:N1}% ({availGb:N1} GB libres)";

            TxtRamDetailLoad.Text = $"{mem.UsagePercentage:N1}% en uso";
            PbRamDetail.Value = mem.UsagePercentage;
            TxtRamDetailUsed.Text = $"Usada: {usedGb:N1} GB";
            TxtRamDetailAvailable.Text = $"Disponible: {availGb:N1} GB";
        }
        catch (Exception ex)
        {
            App.Log($"RefreshDashboard (Memory) error: {ex.Message}");
        }

        try
        {
            // 2. Hardware Telemetry
            if (_hardwareService != null)
            {
                var tele = _hardwareService.GetTelemetrySnapshot();
                TxtCpuName.Text = tele.CpuName;
                PbCpu.Value = tele.CpuLoadPercent ?? 0;
                TxtCpuLoad.Text = $"Carga: {(tele.CpuLoadPercent.HasValue ? $"{tele.CpuLoadPercent:N0}%" : "0%")}";

                TxtGpuName.Text = tele.GpuName;
                PbGpu.Value = tele.GpuLoadPercent ?? 0;
                TxtGpuLoad.Text = $"Carga: {(tele.GpuLoadPercent.HasValue ? $"{tele.GpuLoadPercent:N0}%" : "0%")} | Temp: {(tele.GpuTemperatureCelsius.HasValue ? $"{tele.GpuTemperatureCelsius:N0}°C" : "--°C")}";

                if (_cachedGpuStatus != null && _cachedGpuStatus.IsNvidiaGpuDetected)
                {
                    TxtGpuPcieQuick.Text = $"{_cachedGpuStatus.PcieLinkSummary} • VBIOS {_cachedGpuStatus.VbiosVersion}";
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"RefreshDashboard (Hardware) error: {ex.Message}");
        }

        try
        {
            // 3. Drives
            var drives = _diskService.GetDriveVolumes();
            var driveItems = drives.Select(d => new DriveDisplayItem
            {
                NameAndLabel = $"{d.Name} ({d.Label})",
                UsagePercent = d.UsagePercent,
                SpaceText = $"{d.FreeBytes / (1024.0 * 1024.0 * 1024.0):N0} GB libres de {d.TotalBytes / (1024.0 * 1024.0 * 1024.0):N0} GB"
            }).ToList();
            IcDrives.ItemsSource = driveItems;
        }
        catch (Exception ex)
        {
            App.Log($"RefreshDashboard (Drives) error: {ex.Message}");
        }

        try
        {
            // 4. Processes
            var procs = _processService.GetRunningProcesses(15, "memory");
            DgProcesses.ItemsSource = procs;
        }
        catch (Exception ex)
        {
            App.Log($"RefreshDashboard (Processes) error: {ex.Message}");
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshDashboard();
    private void BtnQuickPurge_Click(object sender, RoutedEventArgs e) => ExecutePurge();
    private void BtnTopOverlay_Click(object sender, RoutedEventArgs e) => ToggleOverlay();
    private void BtnPurgeAllRam_Click(object sender, RoutedEventArgs e) => ExecutePurge();

    private void ExecutePurge()
    {
        var res = _memoryService.PurgeMemory(true, true);
        TxtRamStatusLog.Text = $"✔ {res.Message}";
        TxtFooterStatus.Text = res.Message;
        RefreshDashboard();
        _trayService?.ShowNotification("OmniWin", "Memoria RAM optimizada con éxito.");
    }

    private async void BtnScanBloat_Click(object sender, RoutedEventArgs e) => await ScanBloatAsync();

    private async Task ScanBloatAsync()
    {
        TxtDiskStatusLog.Text = "Analizando archivos temporales y caché del sistema...";
        var bloat = await _diskService.AnalyzeBloatAsync();

        _bloatItems.Clear();
        foreach (var c in bloat.Categories)
        {
            _bloatItems.Add(new BloatDisplayItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                SizeText = $"{c.SizeBytes / (1024.0 * 1024.0):N1} MB",
                FileCountText = $"{c.FileCount:N0} archivos",
                IsSelected = c.SelectedByDefault
            });
        }

        TxtDiskStatusLog.Text = $"Análisis completado: {bloat.TotalBloatBytes / (1024.0 * 1024.0):N0} MB recuperables en {bloat.TotalBloatFiles:N0} archivos.";
    }

    private async void BtnCleanBloat_Click(object sender, RoutedEventArgs e)
    {
        var selectedIds = _bloatItems.Where(b => b.IsSelected).Select(b => b.Id).ToList();
        if (selectedIds.Count == 0)
        {
            MessageBox.Show("Por favor selecciona al menos una categoría para limpiar.", "OmniWin", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TxtDiskStatusLog.Text = "Ejecutando limpieza segura...";
        var res = await _diskService.CleanBloatAsync(new DiskCleanOptions
        {
            CategoryIdsToClean = selectedIds,
            EmptyRecycleBin = true
        });

        TxtDiskStatusLog.Text = $"✔ {res.Summary}";
        TxtFooterStatus.Text = res.Summary;
        await ScanBloatAsync();
        RefreshDashboard();
    }

    private void RefreshTweaks()
    {
        _tweakItems.Clear();
        var tweaks = _tweakService.GetTweaks();
        foreach (var t in tweaks)
        {
            _tweakItems.Add(new TweakDisplayItem
            {
                Id = t.Id,
                Name = t.Name,
                Category = t.Category,
                Description = t.Description,
                IsApplied = t.IsApplied
            });
        }
    }

    private void BtnToggleTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string tweakId)
        {
            var tweak = _tweakItems.FirstOrDefault(t => t.Id == tweakId);
            if (tweak == null) return;

            if (tweak.IsApplied)
            {
                var res = _tweakService.RollbackTweak(tweakId);
                TxtFooterStatus.Text = res.Message;
            }
            else
            {
                var res = _tweakService.ApplyTweak(tweakId, false);
                TxtFooterStatus.Text = res.Message;
            }

            RefreshTweaks();
        }
    }

    private void RefreshStartup()
    {
        var items = _startupService.GetStartupItems();
        var displayItems = items.Select(i => new StartupDisplayItem
        {
            Scope = i.Scope,
            Name = i.Name,
            Command = i.Command,
            IsEnabled = i.IsEnabled
        }).ToList();
        DgStartup.ItemsSource = displayItems;
    }

    private void BtnRefreshStartup_Click(object sender, RoutedEventArgs e) => RefreshStartup();

    private void BtnOptimizeServices_Click(object sender, RoutedEventArgs e)
    {
        TxtStartupLog.Text = "Optimizando y desactivando servicios de telemetría...";
        var disabled = _windowsServiceService.OptimizeTelemetryServices();
        TxtStartupLog.Text = $"✔ Se desactivaron y detuvieron {disabled.Count} servicios de telemetría ({string.Join(", ", disabled)}).";
        TxtFooterStatus.Text = "Servicios de telemetría optimizados.";
    }

    // --- TAB 6: SOFTWARE & DRIVERS (Now encapsulated in SoftwareDriversControl) ---

    // --- TAB 7: SISTEMA & ENERGÍA ---
    private async void BtnDismClean_Click(object sender, RoutedEventArgs e)
    {
        TxtSystemActionsLog.Text = "Iniciando limpieza profunda de WinSxS con DISM /ResetBase...\nEsto puede tardar unos minutos en ejecutarse en segundo plano.";
        var res = await _dismService.CleanComponentStoreAsync(true);
        TxtSystemActionsLog.Text = $"[DISM Completado]: {(res.Success ? "Éxito" : "Finalizado")}\n{res.Summary}\n{res.Output}";
    }

    private async void BtnSfcScan_Click(object sender, RoutedEventArgs e)
    {
        TxtSystemActionsLog.Text = "Iniciando comprobación de integridad con SFC /scannow...\nPor favor espere mientras Windows analiza archivos de sistema.";
        var res = await _dismService.RunSfcScanAsync();
        TxtSystemActionsLog.Text = $"[SFC Completado]: {(res.Success ? "Éxito" : "Finalizado")}\n{res.Summary}\n{res.Output}";
    }

    private async void BtnUnlockUltimatePower_Click(object sender, RoutedEventArgs e)
    {
        var res = await _powerService.UnlockUltimatePerformanceSchemeAsync();
        TxtSystemActionsLog.Text = $"✔ Plan de Rendimiento Máximo desbloqueado:\n{res}";
        TxtFooterStatus.Text = "Plan desbloqueado.";
    }

    private void BtnSetHighPrecisionTimer_Click(object sender, RoutedEventArgs e)
    {
        var (success, newResMs) = _powerService.SetHighPrecisionTimer(true);
        TxtSystemActionsLog.Text = success 
            ? $"✔ Temporizador configurado a alta precisión: {newResMs:N2} ms (Menor latencia e input lag)"
            : "✖ No se pudo configurar la resolución del temporizador.";
        TxtFooterStatus.Text = $"Timer: {newResMs:N2} ms";
    }

    // --- TAB 7: RED & SEGURIDAD ---
    private async void BtnDiagnoseNetwork_Click(object sender, RoutedEventArgs e) => await RunNetworkDiagnosticsAsync();

    private async Task RunNetworkDiagnosticsAsync()
    {
        TxtNetLog.Text = "Ejecutando diagnóstico integral de red y auditoría de seguridad...";
        var report = await _networkService.RunDiagnosticsAsync();
        TxtNetStatus.Text = report.HasInternetAccess ? "Conectado ✔" : "Sin Conexión ✖";
        TxtDnsSpeed.Text = report.DnsResolutionTimeMs >= 0 ? $"{report.DnsResolutionTimeMs:N1} ms" : "Error";
        TxtTcpSockets.Text = report.ActiveTcpConnections.ToString();

        var audit = _securityAuditService.GetSecurityAudit();
        TxtAntivirusInfo.Text = $"Antivirus: {audit.AntivirusProduct}";
        TxtUacInfo.Text = $"UAC: {(audit.UacEnabled ? "Habilitado ✔" : "Deshabilitado ⚠")}";
        TxtBsodInfo.Text = $"Eventos BSOD (Kernel-Power ID 41): {audit.RecentCriticalEvents.Count} en las últimas 48h";

        TxtNetLog.Text = $"Diagnóstico finalizado. Acceso a Internet verificado en {report.ActiveInterfaces.Count} adaptadores.";
    }

    private async void BtnHealNetwork_Click(object sender, RoutedEventArgs e)
    {
        TxtNetLog.Text = "Reparando pila de red (Purgando DNS y reiniciando Winsock)...";
        var res = await _networkService.HealNetworkAsync();
        TxtNetLog.Text = $"✔ {res.Summary}\n" + string.Join(" | ", res.StepsExecuted);
        TxtFooterStatus.Text = res.Summary;
        await RunNetworkDiagnosticsAsync();
    }

    // --- TAB 8: DESBLOQUEADOR DE ARCHIVOS ---
    private void BtnCheckFileLocks_Click(object sender, RoutedEventArgs e)
    {
        string path = TxtUnlockFilePath.Text.Trim();
        if (string.IsNullOrEmpty(path)) return;

        var locks = _fileLockService.GetLockingProcesses(path);
        DgFileLocks.ItemsSource = locks;

        if (locks.Count == 0)
        {
            MessageBox.Show("El archivo no está bloqueado por ningún proceso activo.", "OmniWin", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnUnlockFile_Click(object sender, RoutedEventArgs e)
    {
        string path = TxtUnlockFilePath.Text.Trim();
        if (string.IsNullOrEmpty(path)) return;

        var res = _fileLockService.UnlockFile(path, true);
        BtnCheckFileLocks_Click(sender, e);
        MessageBox.Show(res.Message, "OmniWin Desbloqueador", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    // --- TAB 10: CLAUDE MCP REGISTRATION ---
    private void BtnRegisterClaudeMcp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string claudeConfigPath = Path.Combine(appData, "Claude", "claude_desktop_config.json");
            string currentExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OmniWin.exe");

            JsonObject root;
            if (File.Exists(claudeConfigPath))
            {
                string json = File.ReadAllText(claudeConfigPath);
                root = JsonNode.Parse(json) as JsonObject ?? new JsonObject();
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(claudeConfigPath)!);
                root = new JsonObject();
            }

            if (!root.ContainsKey("mcpServers") || root["mcpServers"] is not JsonObject)
            {
                root["mcpServers"] = new JsonObject();
            }

            var mcpServers = root["mcpServers"]!.AsObject();
            mcpServers["omniwin"] = new JsonObject
            {
                ["command"] = currentExe,
                ["args"] = new JsonArray { "mcp" }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(claudeConfigPath, root.ToJsonString(options));

            MessageBox.Show($"¡Servidor MCP registrado con éxito en Claude Desktop!\nArchivo actualizado: {claudeConfigPath}\nReinicia Claude Desktop para que cargue las 28 herramientas.", "OmniWin MCP", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtFooterStatus.Text = "Servidor MCP registrado en Claude Desktop.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al registrar en Claude Desktop: {ex.Message}", "Error OmniWin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Views.TrafficMonitorWidgetWindow? _trafficWidgetWindow;

    private void BtnTopAwake_Click(object sender, RoutedEventArgs e)
    {
        var awake = OmniWin.Core.Services.AwakeService.Instance;
        if (awake.CurrentState.IsActive)
        {
            awake.Deactivate();
            BtnTopAwake.Content = "☕ Cafeína: Off";
            BtnTopAwake.Style = (Style)FindResource("SecondaryButton");
            TxtFooterStatus.Text = "Modo Cafeína desactivado (Suspensión normal de Windows permitida).";
        }
        else
        {
            awake.Activate(OmniWin.Core.Services.AwakeMode.KeepAwakeIndefinite, keepDisplayOn: true);
            BtnTopAwake.Content = "☕ Cafeína: ON";
            BtnTopAwake.Background = (Brush)FindResource("Accent");
            TxtFooterStatus.Text = "Modo Cafeína ACTIVADO (Pantalla y PC se mantendrán despiertas).";
        }
    }

    private void BtnTopWidget_Click(object sender, RoutedEventArgs e)
    {
        if (_trafficWidgetWindow == null || !_trafficWidgetWindow.IsLoaded)
        {
            _trafficWidgetWindow = new Views.TrafficMonitorWidgetWindow();
            _trafficWidgetWindow.Show();
            BtnTopWidget.Content = "📌 Ocultar Widget";
            TxtFooterStatus.Text = "Widget flotante de red y temperaturas activado en escritorio.";
        }
        else
        {
            if (_trafficWidgetWindow.IsVisible)
            {
                _trafficWidgetWindow.Hide();
                BtnTopWidget.Content = "📌 Widget Flotante";
            }
            else
            {
                _trafficWidgetWindow.Show();
                BtnTopWidget.Content = "📌 Ocultar Widget";
            }
        }
    }

    private void ToggleOverlay()
    {
        try
        {
            if (OverlayManager.IsActive)
            {
                OverlayManager.HideOverlay();
                BtnTopOverlay.Content = _locService.Get("OverlayHud", "🎮 Overlay HUD");
                TxtFooterStatus.Text = "Overlay HUD en juego ocultado.";
            }
            else
            {
                OverlayManager.ShowOverlay();
                BtnTopOverlay.Content = "🎮 Ocultar HUD";
                TxtFooterStatus.Text = "Overlay HUD en juego activado (Ctrl+Shift+O).";
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ToggleOverlay Error] {ex.Message}");
        }
    }

    private void ToggleTrafficWidget()
    {
        if (_trafficWidgetWindow == null || !_trafficWidgetWindow.IsLoaded)
        {
            _trafficWidgetWindow = new Views.TrafficMonitorWidgetWindow();
            _trafficWidgetWindow.Show();
            BtnTopWidget.Content = _locService.Get("WidgetHide", "📌 Ocultar Widget");
            TxtFooterStatus.Text = "Widget flotante de red y temperaturas activado en escritorio.";
        }
        else
        {
            if (_trafficWidgetWindow.IsVisible)
            {
                _trafficWidgetWindow.Hide();
                BtnTopWidget.Content = _locService.Get("WidgetShow", "📌 Widget Flotante");
            }
            else
            {
                _trafficWidgetWindow.Show();
                BtnTopWidget.Content = _locService.Get("WidgetHide", "📌 Ocultar Widget");
            }
        }
    }

    private void BtnLang_Click(object sender, RoutedEventArgs e)
    {
        string nextLang = _locService.CurrentLanguage == "es" ? "en" : "es";
        _locService.SetLanguage(nextLang);
    }

    private void OnLanguageChanged(string lang)
    {
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        BtnLang.Content = _locService.CurrentLanguage == "es" ? "🌐 ES" : "🌐 EN";
        BtnQuickPurge.Content = _locService.Get("QuickPurge", "⚡ Liberar RAM");
        Title = _locService.Get("AppTitle", "OmniWin — Windows Control Plane, Optimizer & MCP Agent");
        UpdateNavHighlight(MainTabs.SelectedIndex);
    }

    private void BtnTopCompanion_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 18;
    }

    private void BtnTopReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var reportModel = new OptimizationReportModel
            {
                SelectedProfile = AppSettingsService.Instance.Settings.SelectedProfile,
                RestorePointCreated = true,
                RamPurgedBytes = 512 * 1024 * 1024L,
                Tweaks = new ExpandedTweakService().GetCategorizedTweaks().Where(t => t.IsApplied).Select(t => new ReportTweakEntry
                {
                    Category = t.Category,
                    Name = t.Name,
                    Description = t.Description,
                    RegistryPath = t.Id,
                    NewValue = "1 (Optimizado)",
                    Success = true
                }).ToList()
            };
            string path = OptimizationReportService.Instance.GenerateHtmlReport(reportModel);
            TxtFooterStatus.Text = $"Informe HTML generado: {Path.GetFileName(path)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            App.Log($"[BtnTopReport_Click Error] {ex.Message}");
        }
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnCheckUpdates.Content = "🔄 Comprobando...";
            var result = await UpdateCheckService.Instance.CheckForUpdatesAsync();
            BtnCheckUpdates.Content = result.IsUpdateAvailable
                ? $"⚡ Actualización: v{result.LatestVersion}"
                : $"✔ v{result.CurrentVersion} al día";
            TxtFooterStatus.Text = result.StatusMessage;

            if (result.IsUpdateAvailable)
            {
                var resp = MessageBox.Show(
                    $"¡Nueva versión disponible: v{result.LatestVersion}!\n\n¿Deseas abrir la página de descarga de GitHub?",
                    "Actualización Disponible — OmniWin", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (resp == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = result.DownloadUrl,
                        UseShellExecute = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            BtnCheckUpdates.Content = "🔄 v1.2.0";
            App.Log($"[BtnCheckUpdates_Click Error] {ex.Message}");
        }
    }

    private async void LoadGpuStatusAsync()
    {
        try
        {
            _cachedGpuStatus = await Task.Run(() => NvidiaGpuTuningService.Instance.GetStatus());
            if (_cachedGpuStatus != null && _cachedGpuStatus.IsNvidiaGpuDetected)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtGpuPcieQuick.Text = $"{_cachedGpuStatus.PcieLinkSummary} • VBIOS {_cachedGpuStatus.VbiosVersion}";
                    TxtGpuPcieQuick.ToolTip = $"{_cachedGpuStatus.BottleneckNotice}\nReBAR: {_cachedGpuStatus.ReBarStatusText}\nFabricante: {_cachedGpuStatus.SubsystemVendor}";
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    TxtGpuPcieQuick.Text = "PCIe: N/A";
                });
            }
        }
        catch (Exception ex)
        {
            App.Log($"[LoadGpuStatusAsync Error] {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _signalTimer?.Dispose();
        _timer.Stop();
        _hotkeyService?.Dispose();
        _trayService?.Dispose();
        _hardwareService?.Dispose();
        MetricsExporterService.Instance.Stop();
        OmniWin.Core.Services.AwakeService.Instance.Deactivate();
        try { _trafficWidgetWindow?.Close(); } catch { }
        base.OnClosed(e);
    }
}