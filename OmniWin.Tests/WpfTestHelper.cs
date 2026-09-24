using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OmniWin.Tests;

public static class WpfTestHelper
{
    public static readonly object AppLock = new();
    private static readonly Lazy<Dispatcher> _dispatcher = new(() =>
    {
        Dispatcher? disp = null;
        using var ready = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            lock (AppLock)
            {
                if (Application.Current == null)
                {
                    try
                    {
                        var app = new UI.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                        app.InitializeComponent();
                    }
                    catch { }
                }
            }
            disp = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();
        return disp!;
    });

    public static void Run(Action action)
    {
        Exception? error = null;
        _dispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        if (error != null)
        {
            throw new AggregateException("WPF test runner execution failed", error);
        }
    }
}
