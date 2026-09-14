using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OmniWin.UI;
using OmniWin.UI.Views;
using Xunit;

namespace OmniWin.Tests;

[Collection("WpfVisualTests")]
public class TabNavigationVisualTests
{
    private static readonly object _appLock = new();

    private static void RunInSta(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(20000);

        if (threadEx != null)
        {
            throw new AggregateException("STA thread failed", threadEx);
        }
    }

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
