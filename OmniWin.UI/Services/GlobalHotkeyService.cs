using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OmniWin.UI.Services;

public class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Window Messages
    private const int WM_HOTKEY = 0x0312;

    public const int HOTKEY_OVERLAY_ID = 9001;
    public const int HOTKEY_WIDGET_ID = 9002;
    public const int HOTKEY_PURGE_ID = 9003;

    private IntPtr _hWnd;
    private HwndSource? _hwndSource;
    private bool _isDisposed;

    public event Action? OnOverlayHotkeyPressed;
    public event Action? OnWidgetHotkeyPressed;
    public event Action? OnPurgeHotkeyPressed;

    public bool IsOverlayRegistered { get; private set; }
    public bool IsWidgetRegistered { get; private set; }
    public bool IsPurgeRegistered { get; private set; }

    public void Initialize(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _hWnd = helper.Handle;

        if (_hWnd == IntPtr.Zero)
        {
            // If window not yet shown, wait for source initialized
            window.SourceInitialized += (_, _) =>
            {
                _hWnd = new WindowInteropHelper(window).Handle;
                SetupHookAndRegister();
            };
        }
        else
        {
            SetupHookAndRegister();
        }
    }

    private void SetupHookAndRegister()
    {
        if (_hWnd == IntPtr.Zero) return;

        _hwndSource = HwndSource.FromHwnd(_hWnd);
        _hwndSource?.AddHook(HwndHook);

        // 1. Register Ctrl + Shift + O (0x4F) for HUD Overlay
        IsOverlayRegistered = RegisterHotKey(_hWnd, HOTKEY_OVERLAY_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x4F);

        // 2. Register Ctrl + Shift + W (0x57) for Floating Traffic Widget
        IsWidgetRegistered = RegisterHotKey(_hWnd, HOTKEY_WIDGET_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x57);

        // 3. Register Ctrl + Shift + P (0x50) for Quick RAM Purge
        IsPurgeRegistered = RegisterHotKey(_hWnd, HOTKEY_PURGE_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, 0x50);

        App.Log($"GlobalHotkeyService registered: Overlay={IsOverlayRegistered}, Widget={IsWidgetRegistered}, Purge={IsPurgeRegistered}");
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_OVERLAY_ID:
                    OnOverlayHotkeyPressed?.Invoke();
                    handled = true;
                    break;

                case HOTKEY_WIDGET_ID:
                    OnWidgetHotkeyPressed?.Invoke();
                    handled = true;
                    break;

                case HOTKEY_PURGE_ID:
                    OnPurgeHotkeyPressed?.Invoke();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_hWnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hWnd, HOTKEY_OVERLAY_ID);
            UnregisterHotKey(_hWnd, HOTKEY_WIDGET_ID);
            UnregisterHotKey(_hWnd, HOTKEY_PURGE_ID);
        }

        _hwndSource?.RemoveHook(HwndHook);
    }
}
