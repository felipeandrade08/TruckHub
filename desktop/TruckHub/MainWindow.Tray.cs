using System;
using System.Windows;

namespace TruckHub;

/// <summary>Minimizar manda o TruckHub para a bandeja (ícones ocultos) em vez da barra de tarefas.</summary>
public partial class MainWindow
{
    private TrayIcon? _tray;

    private void HideToTray()
    {
        try
        {
            if (_tray == null)
            {
                _tray = new TrayIcon("TruckHub • clique para abrir");
                _tray.OpenRequested += () => Dispatcher.BeginInvoke(new Action(RestoreFromTray));
                _tray.ExitRequested += () => Dispatcher.BeginInvoke(new Action(() => { RestoreFromTray(); Close(); }));
            }
            _tray.Show();
            ShowInTaskbar = false;
            Hide();
        }
        catch
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Minimized;
        }
    }

    private void RestoreFromTray()
    {
        try
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            _tray?.Hide();
        }
        catch { }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && IsLoaded) { WindowState = WindowState.Normal; HideToTray(); }
    }
}
