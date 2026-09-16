using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public partial class WelcomeTourControl : UserControl
{
    public event Action? OnTourFinished;
    public event Action? OnLaunchWizardRequested;

    private int _currentSlide = 1;

    public WelcomeTourControl()
    {
        InitializeComponent();
        UpdateSlide();
    }

    public static bool ShouldShowTour()
    {
        return !AppSettingsService.Instance.Settings.WelcomeTourCompleted;
    }

    public void ResetToFirstSlide()
    {
        _currentSlide = 1;
        UpdateSlide();
    }

    private void UpdateSlide()
    {
        if (Slide1 == null || Slide2 == null || Slide3 == null) return;

        Slide1.Visibility = (_currentSlide == 1) ? Visibility.Visible : Visibility.Collapsed;
        Slide2.Visibility = (_currentSlide == 2) ? Visibility.Visible : Visibility.Collapsed;
        Slide3.Visibility = (_currentSlide == 3) ? Visibility.Visible : Visibility.Collapsed;

        // Badge
        TxtTourSlideBadge.Text = _currentSlide switch
        {
            1 => "1 de 3 • Introducción",
            2 => "2 de 3 • 6 Hubs de Control",
            3 => "3 de 3 • Atajos & Listo",
            _ => $"{_currentSlide} de 3"
        };

        // Dots
        var activeBrush = (Brush)FindResource("Accent");
        var inactiveBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x27, 0x3C));

        Dot1.Background = (_currentSlide == 1) ? activeBrush : inactiveBrush;
        Dot1.Width = (_currentSlide == 1) ? 20 : 8;

        Dot2.Background = (_currentSlide == 2) ? activeBrush : inactiveBrush;
        Dot2.Width = (_currentSlide == 2) ? 20 : 8;

        Dot3.Background = (_currentSlide == 3) ? activeBrush : inactiveBrush;
        Dot3.Width = (_currentSlide == 3) ? 20 : 8;

        // Buttons
        BtnPrev.Visibility = (_currentSlide > 1) ? Visibility.Visible : Visibility.Collapsed;
        BtnNext.Visibility = (_currentSlide < 3) ? Visibility.Visible : Visibility.Collapsed;

        BtnStartWizard.Visibility = (_currentSlide == 3) ? Visibility.Visible : Visibility.Collapsed;
        BtnGoDashboard.Visibility = (_currentSlide == 3) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSlide < 3)
        {
            _currentSlide++;
            UpdateSlide();
        }
    }

    private void BtnPrev_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSlide > 1)
        {
            _currentSlide--;
            UpdateSlide();
        }
    }

    private void BtnStartWizard_Click(object sender, RoutedEventArgs e)
    {
        MarkTourCompleted();
        OnLaunchWizardRequested?.Invoke();
    }

    private void BtnGoDashboard_Click(object sender, RoutedEventArgs e)
    {
        MarkTourCompleted();
        OnTourFinished?.Invoke();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        MarkTourCompleted();
        OnTourFinished?.Invoke();
    }

    private static void MarkTourCompleted()
    {
        try
        {
            AppSettingsService.Instance.SaveSettings(s => s.WelcomeTourCompleted = true);
        }
        catch { }
    }
}
