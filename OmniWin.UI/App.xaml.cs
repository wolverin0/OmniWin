using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace OmniWin.UI;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "ui_startup.log");

    public static void Log(string msg)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    private const string AppMutexName = @"Global\OmniWin_SingleInstance_AppMutex";
    private static System.Threading.Mutex? _appMutex;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"[FATAL] AppDomain UnhandledException: {args.ExceptionObject}");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), args.ExceptionObject?.ToString() ?? "Unknown error"); } catch { }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log($"[FATAL] DispatcherUnhandledException: {args.Exception}");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), args.Exception.ToString()); } catch { }
            args.Handled = true;
        };
        Log("=== OmniWin.UI Application Starting ===");

        // If invoked with 'mcp', run the MCP stdio server directly without GUI
        if (e.Args.Length > 0 && e.Args[0].Equals("mcp", StringComparison.OrdinalIgnoreCase))
        {
            Log("Starting OmniWin MCP stdio server...");
            try
            {
                var mcp = new OmniWin.Core.Mcp.McpServer();
                mcp.RunAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"MCP server error: {ex.Message}");
            }
            Environment.Exit(0);
            return;
        }

        // Single-Instance Guard: prevent duplicate instances fighting over hardware sensors or companion port
        bool createdNew = false;
        try
        {
            _appMutex = new System.Threading.Mutex(true, AppMutexName, out createdNew);
        }
        catch (Exception ex)
        {
            Log($"Mutex creation warning: {ex.Message}");
            createdNew = true;
        }

        bool isTestHost = AppDomain.CurrentDomain.FriendlyName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
                          AppDomain.CurrentDomain.FriendlyName.Contains("xunit", StringComparison.OrdinalIgnoreCase);

        if (!createdNew && !e.Args.Contains("--multi-instance") && !isTestHost)
        {
            var currentPid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var otherProc = System.Diagnostics.Process.GetProcessesByName("OmniWin")
                .Concat(System.Diagnostics.Process.GetProcessesByName("OmniWin.UI"))
                .FirstOrDefault(p => p.Id != currentPid);

            if (otherProc != null)
            {
                Log($"Another instance of OmniWin (PID {otherProc.Id}) is already running. Focusing and exiting.");
                try
                {
                    if (otherProc.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(otherProc.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(otherProc.MainWindowHandle);
                    }
                }
                catch { }

                Shutdown(0);
                return;
            }
            else
            {
                Log("Stale mutex detected without running process. Proceeding with startup.");
            }
        }

        // Automatically elevate to Administrator if started as standard user (unless in test mode)
        if (!OmniWin.Core.Services.SecurityHelper.IsAdministrator() && !e.Args.Contains("--no-elevate") && !isTestHost)
        {
            Log("Process is not running as Administrator. Triggering UAC elevation...");
            try
            {
                string rawArgs = string.Join(" ", e.Args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
                if (OmniWin.Core.Services.SecurityHelper.RestartAsAdministrator(rawArgs))
                {
                    Log("RestartAsAdministrator initiated. Exiting un-elevated process.");
                    _appMutex?.Dispose();
                    _appMutex = null;
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"RestartAsAdministrator failed: {ex.Message}");
            }
        }

        base.OnStartup(e);
        this.ShutdownMode = ShutdownMode.OnMainWindowClose;
        this.Exit += (s, ev) =>
        {
            Log($"[EXIT] Application.Exit event fired with code {ev.ApplicationExitCode}");
            try
            {
                _appMutex?.ReleaseMutex();
                _appMutex?.Dispose();
                _appMutex = null;
            }
            catch { }
        };

        try
        {
            Log("Creating MainWindow instance...");
            var win = new MainWindow();
            this.MainWindow = win;

            win.Closing += (s, ev) => Log($"[WINDOW] MainWindow.Closing fired! Cancel={ev.Cancel}\nStackTrace:\n{Environment.StackTrace}");
            win.Closed += (s, ev) => Log($"[WINDOW] MainWindow.Closed fired!\nStackTrace:\n{Environment.StackTrace}");
            win.StateChanged += (s, ev) => Log($"[WINDOW] WindowState changed to: {win.WindowState}");
            win.IsVisibleChanged += (s, ev) => Log($"[WINDOW] IsVisible changed to: {win.IsVisible}");
            win.Activated += (s, ev) => Log("[WINDOW] MainWindow Activated");
            win.Deactivated += (s, ev) => Log("[WINDOW] MainWindow Deactivated");

            Log("Calling MainWindow.Show()...");
            win.Topmost = true;
            win.Show();
            win.Activate();
            win.Focus();
            var handle = new System.Windows.Interop.WindowInteropHelper(win).Handle;
            Log($"MainWindow.Show() completed. HWND: {handle}, IsVisible: {win.IsVisible}, WindowState: {win.WindowState}");

            _ = Task.Run(async () =>
            {
                await Task.Delay(1500);
                await Dispatcher.InvokeAsync(() => { win.Topmost = false; });
            });

            if (e.Args.Contains("--test-ui"))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1200);
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await RunAutomatedUiTestAsync(win, e.Args.Contains("--exit-after-test"));
                    });
                });
            }
        }
        catch (Exception ex)
        {
            Log($"[ERROR] Exception creating/showing MainWindow: {ex}");
            try { File.WriteAllText(@"C:\Users\pauol\Source\Repos\OmniWin\crash.txt", ex.ToString()); } catch { }
            MessageBox.Show($"Error al iniciar OmniWin:\n{ex.Message}\n\nDetalles:\n{ex.StackTrace}", "OmniWin - Error de Inicio", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunAutomatedUiTestAsync(MainWindow win, bool exitAfter = false)
    {
        Log("[UI_TEST] Starting automated UI self-test suite via CaptureAllTabsAsync...");
        await win.CaptureAllTabsAsync();
        Log("[UI_TEST] Automated UI self-test completed successfully across all 15 tabs!");
        if (exitAfter)
        {
            await Task.Delay(400);
            Environment.Exit(0);
        }
    }
}
