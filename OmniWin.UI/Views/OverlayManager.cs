using System;
using System.Windows;

namespace OmniWin.UI.Views;

/// <summary>
/// Manages the single instance and visibility of the Gaming HUD Overlay.
/// </summary>
public static class OverlayManager
{
    private static GamingOverlayWindow? _overlayInstance;

    public static GamingOverlayWindow Current => _overlayInstance ??= CreateInstance();

    public static bool IsActive => _overlayInstance != null && _overlayInstance.Visibility == Visibility.Visible;

    public static event Action<bool>? VisibilityChanged;

    private static GamingOverlayWindow CreateInstance()
    {
        var win = new GamingOverlayWindow();
        win.IsVisibleChanged += (s, e) =>
        {
            VisibilityChanged?.Invoke(win.Visibility == Visibility.Visible);
        };
        win.Closed += (s, e) =>
        {
            _overlayInstance = null;
            VisibilityChanged?.Invoke(false);
        };
        return win;
    }

    public static void ShowOverlay()
    {
        if (_overlayInstance == null)
        {
            _overlayInstance = CreateInstance();
        }

        _overlayInstance.Visibility = Visibility.Visible;
        _overlayInstance.Topmost = true;
        VisibilityChanged?.Invoke(true);
    }

    public static void HideOverlay()
    {
        if (_overlayInstance != null)
        {
            _overlayInstance.Visibility = Visibility.Collapsed;
            VisibilityChanged?.Invoke(false);
        }
    }

    public static void ToggleOverlay()
    {
        if (_overlayInstance == null || _overlayInstance.Visibility != Visibility.Visible)
        {
            ShowOverlay();
        }
        else
        {
            HideOverlay();
        }
    }
}
