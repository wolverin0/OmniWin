using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Linq;
using OmniWin.Core.Services;
using OmniWin.UI;
using OmniWin.UI.Services;
using OmniWin.UI.Views;
using Xunit;

namespace OmniWin.Tests;

[Collection("WpfVisualTests")]
public class TabNavigationVisualTests
{
    private static readonly object _appLock = WpfTestHelper.AppLock;
    private static void RunInSta(Action action) => WpfTestHelper.Run(action);

    [Fact]
    public void TabControl_DoesNotSwapRows_AndMaintainsConsistentYPosition()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var control = new SoftwareDriversControl();
            var window = new Window
            {
                Width = 1050,
                Height = 720,
                Content = control,
                Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x19)),
                WindowStyle = WindowStyle.None
            };

            window.Show();

            // Locate the inner TabControl
            var tabControl = control.Content as TabControl ?? FindVisualChild<TabControl>(control);
            Assert.NotNull(tabControl);
            Assert.Equal(4, tabControl.Items.Count);

            // 1. Select Tab 2: Controladores (DriverStore)
            tabControl.SelectedIndex = 2;
            window.UpdateLayout();

            var tab2 = (TabItem)tabControl.Items[2];
            var tab3 = (TabItem)tabControl.Items[3];

            var p2Initial = tab2.TransformToAncestor(window).Transform(new Point(0, 0));
            var p3Initial = tab3.TransformToAncestor(window).Transform(new Point(0, 0));

            // Save Screenshot of Tab 2
            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);

            RenderAndSave(window, Path.Combine(reportsDir, "Tab-Test-Controladores.png"));

            // 2. Select Tab 3: Placa Madre, BIOS & RAM
            tabControl.SelectedIndex = 3;
            window.UpdateLayout();

            var p2After = tab2.TransformToAncestor(window).Transform(new Point(0, 0));
            var p3After = tab3.TransformToAncestor(window).Transform(new Point(0, 0));

            // Save Screenshot of Tab 3
            RenderAndSave(window, Path.Combine(reportsDir, "Tab-Test-PlacaMadre.png"));

            window.Close();

            // 3. Verify that Y positions did NOT change or swap!
            // In the bugged multi-row layout, selecting Tab 3 swapped rows, changing p2.Y and p3.Y drastically.
            // With the fixed single-row horizontal layout, both tabs remain on the exact same row.
            Assert.Equal(p2Initial.Y, p2After.Y, 1.0);
            Assert.Equal(p3Initial.Y, p3After.Y, 1.0);
            Assert.True(Math.Abs(p2After.Y - p3After.Y) <= 5.0, $"Tabs are not on the same horizontal row: p2={p2After.Y}, p3={p3After.Y}");
        });
    }

    [Fact]
    public void HubNavigationRegistry_All29Tabs_MappedToHubsWithoutDuplicates()
    {
        Assert.Equal(6, HubNavigationRegistry.Hubs.Length);

        var allTabIndices = new System.Collections.Generic.List<int>();
        foreach (var hub in HubNavigationRegistry.Hubs)
        {
            Assert.False(string.IsNullOrWhiteSpace(hub.Title));
            Assert.NotEmpty(hub.SubItems);
            foreach (var sub in hub.SubItems)
            {
                allTabIndices.Add(sub.TabIndex);
            }
        }

        // All 30 tabs (0 to 29) must be present without duplicates
        Assert.Equal(30, allTabIndices.Count);
        Assert.Equal(30, allTabIndices.Distinct().Count());

        for (int i = 0; i < 30; i++)
        {
            var mapping = HubNavigationRegistry.FindByTabIndex(i);
            Assert.NotNull(mapping);
            Assert.Equal(i, mapping.Value.SubItem.TabIndex);
            Assert.InRange(mapping.Value.Hub.HubIndex, 0, 5);
        }
    }

    [Fact]
    public void WelcomeTourControl_InstantiatesAndRendersAllSlides()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var tourControl = new WelcomeTourControl();
            var window = new Window
            {
                Width = 900,
                Height = 650,
                Content = tourControl,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0E)),
                WindowStyle = WindowStyle.None
            };

            window.Show();
            window.UpdateLayout();

            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);

            // Render Slide 1
            RenderAndSave(window, Path.Combine(reportsDir, "WelcomeTour-Slide1.png"));
            Assert.Equal(Visibility.Visible, tourControl.Slide1.Visibility);

            // Transition to Slide 2
            var btnNext = tourControl.BtnNext;
            btnNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            RenderAndSave(window, Path.Combine(reportsDir, "WelcomeTour-Slide2.png"));
            Assert.Equal(Visibility.Visible, tourControl.Slide2.Visibility);

            // Transition to Slide 3
            btnNext.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            RenderAndSave(window, Path.Combine(reportsDir, "WelcomeTour-Slide3.png"));
            Assert.Equal(Visibility.Visible, tourControl.Slide3.Visibility);

            bool completedFired = false;
            tourControl.OnTourFinished += () => completedFired = true;
            tourControl.BtnGoDashboard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(completedFired);

            window.Close();
        });
    }

    [Fact]
    public void MainWindow_HubNavigation_SwitchesHubsAndPopulatesPills()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var mainWin = new MainWindow();
            mainWin.Show();
            mainWin.OnboardingOverlay.Visibility = Visibility.Collapsed;
            mainWin.WelcomeTourOverlay.Visibility = Visibility.Collapsed;
            mainWin.UpdateLayout();

            // Hub 0 (Visión General) is selected by default -> 4 sub items
            Assert.Equal(0, mainWin.MainTabs.SelectedIndex);
            Assert.Equal(4, mainWin.HubSubNavPanel.Children.Count);

            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);
            RenderAndSave(mainWin, Path.Combine(reportsDir, "MainWindow-WinUI3-Dashboard.png"));

            // Switch to Hub 2 (Almacenamiento & Archivos) -> 7 sub items
            mainWin.NavHub2.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            mainWin.UpdateLayout();

            Assert.Equal(16, mainWin.MainTabs.SelectedIndex); // Tab 16 is Disk Space
            Assert.Equal(7, mainWin.HubSubNavPanel.Children.Count);
            RenderAndSave(mainWin, Path.Combine(reportsDir, "MainWindow-6Hub-Storage.png"));

            // Switch to Hub 1 (Rendimiento & Gaming) -> 5 sub items (including Rig Hologram & RGB)
            mainWin.NavHub1.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            mainWin.UpdateLayout();

            Assert.Equal(3, mainWin.MainTabs.SelectedIndex); // Tab 3 is RAM
            Assert.Equal(5, mainWin.HubSubNavPanel.Children.Count);
            RenderAndSave(mainWin, Path.Combine(reportsDir, "MainWindow-6Hub-Performance.png"));

            mainWin.Close();
        });
    }

    [Fact]
    public void DeduplicationControl_Renders_MultiTargetScopeAndDriveChips()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var dedupControl = new DeduplicationControl();
            var window = new Window
            {
                Width = 1050,
                Height = 750,
                Content = dedupControl,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0E)),
                WindowStyle = WindowStyle.None
            };

            window.Show();
            window.UpdateLayout();

            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);

            // Render Deduplicator Control with multi-target scope and drives
            RenderAndSave(window, Path.Combine(reportsDir, "Deduplicator-MultiDrive-Scope.png"));

            Assert.NotNull(dedupControl.BtnSelectAllDrives);
            Assert.NotNull(dedupControl.BtnAddFolder);
            Assert.NotNull(dedupControl.BtnStartScan);

            // Test clicking "Todos los Discos"
            dedupControl.BtnSelectAllDrives.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            RenderAndSave(window, Path.Combine(reportsDir, "Deduplicator-AllDrives-Selected.png"));

            window.Close();
        });
    }

    [Fact]
    public void PrivacyShieldControl_RendersWithoutException()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var privacyControl = new PrivacyShieldControl();
            var window = new Window
            {
                Width = 1050,
                Height = 750,
                Content = privacyControl,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0E)),
                WindowStyle = WindowStyle.None
            };

            // This verifies the BoolToVis / StatusBackground crash is permanently resolved!
            window.Show();
            window.UpdateLayout();

            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);
            RenderAndSave(window, Path.Combine(reportsDir, "PrivacyShield-Resolved.png"));

            window.Close();
        });
    }

    [Fact]
    public void RigVisualizerControl_Renders_HologramAndAiRender()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var rigControl = new RigVisualizerControl();
            var window = new Window
            {
                Width = 1050,
                Height = 750,
                Content = rigControl,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0E)),
                WindowStyle = WindowStyle.None
            };

            window.Show();
            window.UpdateLayout();

            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);

            // 1. Initial Default View: Interactive Realistic Gaming Rig View
            Assert.Equal(Visibility.Visible, rigControl.PnlInteractiveRigView.Visibility);
            Assert.Equal(Visibility.Collapsed, rigControl.PnlHologramView.Visibility);
            RenderAndSave(window, Path.Combine(reportsDir, "RigVisualizer-AiRender.png"));

            // 2. Switch to Holographic Blueprint View
            rigControl.BtnModeHologram.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.Equal(Visibility.Visible, rigControl.PnlHologramView.Visibility);
            Assert.Equal(Visibility.Collapsed, rigControl.PnlInteractiveRigView.Visibility);
            RenderAndSave(window, Path.Combine(reportsDir, "RigVisualizer-Hologram.png"));

            // 3. Test OpenRGB and ProcessEfficiency
            Assert.NotNull(OpenRgbClientService.Instance.Devices);
            var trim = ProcessEfficiencyService.TrimWorkingSet();
            Assert.True(trim.Success);

            window.Close();
        });
    }

    [Fact]
    public void CompanionServerControl_Renders_NetworkSelectionAndQrCode()
    {
        RunInSta(() =>
        {
            lock (_appLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }

            var companionControl = new CompanionServerControl();
            var window = new Window
            {
                Width = 1050,
                Height = 750,
                Content = companionControl,
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x09, 0x0E)),
                WindowStyle = WindowStyle.None
            };

            window.Show();
            window.UpdateLayout();

            var reportsDir = @"C:\Users\pauol\Source\Repos\OmniWin\scripts\reports";
            Directory.CreateDirectory(reportsDir);

            // Assert adapters are populated in the ComboBox
            Assert.NotNull(companionControl.CmbNetworkAdapters.ItemsSource);
            var adapters = companionControl.CmbNetworkAdapters.ItemsSource as System.Collections.Generic.List<NetworkAdapterInfo>;
            Assert.NotNull(adapters);
            Assert.NotEmpty(adapters);

            // Capture initial render
            RenderAndSave(window, Path.Combine(reportsDir, "CompanionServer-NetworkSelection.png"));

            // Select next adapter if available and verify pairing URL updates
            if (adapters.Count > 1)
            {
                companionControl.CmbNetworkAdapters.SelectedIndex = 1;
                window.UpdateLayout();
                var selected = adapters[1];
                Assert.Contains(selected.IpAddress, companionControl.TxtPairingUrl.Text);
                RenderAndSave(window, Path.Combine(reportsDir, "CompanionServer-SecondaryNetwork.png"));
            }

            window.Close();
        });
    }

    private static void RenderAndSave(Window window, string filePath)
    {
        var width = (int)window.ActualWidth;
        var height = (int)window.ActualHeight;
        if (width <= 0) width = 1050;
        if (height <= 0) height = 720;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var fileStream = File.Create(filePath);
        encoder.Save(fileStream);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }
}
