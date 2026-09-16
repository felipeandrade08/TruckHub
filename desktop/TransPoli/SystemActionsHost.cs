using System.Windows;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly bool _systemActionsIntegration = SystemActionsIntegration.Initialize();

    internal void TriggerUpdateAction()
        => UpdateButton_Click(this, new RoutedEventArgs());

    internal void TriggerAboutAction()
        => AboutButton_Click(this, new RoutedEventArgs());
}
