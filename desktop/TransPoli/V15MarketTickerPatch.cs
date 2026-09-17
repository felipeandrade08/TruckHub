using System;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly DispatcherTimer _v15TickerPatchTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _v15TickerPatchStarted;

    private static readonly bool V15TickerPatchRegistration = RegisterV15TickerPatch();

    private static bool RegisterV15TickerPatch()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V15TickerPatchLoaded), true);
        return true;
    }

    private static void V15TickerPatchLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v15TickerPatchStarted) return;
        window._v15TickerPatchStarted = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window._v15TickerPatchTimer.Tick += (_, _) => window.RenderV15MarketTicker();
            window._v15TickerPatchTimer.Start();
        }), DispatcherPriority.ContextIdle);
    }
}
