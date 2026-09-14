using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using OmniWin.Core.Services;

namespace OmniWin.UI.Services;

public class SystemTrayService : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_INFO = 0x00000001;

    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 2048;

    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly Window _window;
    private IntPtr _hWnd;
    private HwndSource? _hwndSource;
    private bool _isCreated;
    private IntPtr _hIcon = IntPtr.Zero;

    public event Action? OnOpenDashboardRequested;
    public event Action? OnQuickPurgeRequested;
    public event Action? OnToggleOverlayRequested;
    public event Action? OnToggleWidgetRequested;
    public event Action? OnExitRequested;

    public SystemTrayService(Window window)
    {
        _window = window;
    }

    public void Initialize()
    {
        var helper = new WindowInteropHelper(_window);
        _hWnd = helper.Handle;

        if (_hWnd == IntPtr.Zero)
        {
            _window.SourceInitialized += (_, _) =>
            {
                _hWnd = new WindowInteropHelper(_window).Handle;
                CreateTrayIcon();
            };
        }
        else
        {
            CreateTrayIcon();
        }
    }

    private void CreateTrayIcon()
    {
        if (_isCreated || _hWnd == IntPtr.Zero) return;

        _hwndSource = HwndSource.FromHwnd(_hWnd);
        _hwndSource?.AddHook(TrayWndProc);

        // Try load icon from resource or exe
        try
        {
            string exePath = Environment.ProcessPath ?? string.Empty;
            if (File.Exists(exePath))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (ico != null)
                {
                    _hIcon = ico.Handle;
                }
            }
        }
        catch { }

        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION fallback
        }

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = "OmniWin — Windows Control Plane & MCP Agent"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref nid);
        App.Log($"SystemTrayService created: {_isCreated}");
    }

    public void ShowNotification(string title, string message)
    {
        if (!_isCreated || _hWnd == IntPtr.Zero) return;

        var nid = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hWnd,
            uID = 1001,
            uFlags = NIF_INFO,
            szInfoTitle = title,
            szInfo = message,
            dwInfoFlags = NIIF_INFO
        };

        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            int eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONDBLCLK)
            {
                ToggleWindowVisibility();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void ToggleWindowVisibility()
    {
        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
        {
            _window.WindowState = WindowState.Minimized;
            _window.Hide();
        }
        else
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
    }

    private void ShowContextMenu()
    {
        var menu = new ContextMenu
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x15, 0x23)),
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x20, 0x35)),
            BorderThickness = new Thickness(1),
            FontSize = 12
        };

        var mDashboard = new MenuItem { Header = "📊 Abrir Panel de Control (Dashboard)", FontWeight = FontWeights.Bold };
        mDashboard.Click += (_, _) =>
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
            OnOpenDashboardRequested?.Invoke();
        };
        menu.Items.Add(mDashboard);

        menu.Items.Add(new Separator());

        var mPurge = new MenuItem { Header = "⚡ Liberar Memoria RAM (Standby & Sets)" };
        mPurge.Click += (_, _) => OnQuickPurgeRequested?.Invoke();
        menu.Items.Add(mPurge);

        var mOverlay = new MenuItem { Header = "🎮 Alternar Gaming HUD (Ctrl+Shift+O)" };
        mOverlay.Click += (_, _) => OnToggleOverlayRequested?.Invoke();
        menu.Items.Add(mOverlay);

        var mWidget = new MenuItem { Header = "📌 Alternar Widget Flotante (Ctrl+Shift+W)" };
        mWidget.Click += (_, _) => OnToggleWidgetRequested?.Invoke();
        menu.Items.Add(mWidget);

        menu.Items.Add(new Separator());

        var mExit = new MenuItem { Header = "✖ Salir de OmniWin" };
        mExit.Click += (_, _) => OnExitRequested?.Invoke();
        menu.Items.Add(mExit);

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_isCreated && _hWnd != IntPtr.Zero)
        {
            var nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hWnd,
                uID = 1001
            };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isCreated = false;
        }

        _hwndSource?.RemoveHook(TrayWndProc);
    }
}
