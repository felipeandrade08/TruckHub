using System;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly bool V15CargoBindingStartupRegistered = RegisterV15CargoBindingStartup();

    private static bool RegisterV15CargoBindingStartup()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is MainWindow window)
                    window.Dispatcher.BeginInvoke(new Action(window.StartV15CargoBinding), DispatcherPriority.ContextIdle);
            }), true);
        return true;
    }
}
