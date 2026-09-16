using System.Windows;

namespace TransPoli;

public partial class MainWindow
{
    internal void TriggerUpdateAction()
        => UpdateButton_Click(this, new RoutedEventArgs());

    internal void TriggerAboutAction()
        => AboutButton_Click(this, new RoutedEventArgs());
}
