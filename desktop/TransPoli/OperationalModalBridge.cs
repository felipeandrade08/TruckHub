using System;
using System.Windows;
using System.Windows.Input;

namespace TransPoli;

public partial class MainWindow
{
    private readonly OperationalShortcutRouter _operationalShortcutRouter = new();

    internal void OpenOperationalModalFromShortcut(string kind)
    {
        ShowOperationalModal(kind);
    }
}

internal sealed class OperationalShortcutRouter
{
    public OperationalShortcutRouter()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), UIElement.PreviewKeyDownEvent, new KeyEventHandler(HandlePreviewKeyDown), true);
    }

    private static void HandlePreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var window = Window.GetWindow(source) as MainWindow;
        if (window is null) return;
        if (e.Key == Key.F8)
        {
            e.Handled = true;
            window.OpenOperationalModalFromShortcut("cargo");
        }
        else if (e.Key == Key.F9)
        {
            e.Handled = true;
            window.OpenOperationalModalFromShortcut("summary");
        }
    }
}
