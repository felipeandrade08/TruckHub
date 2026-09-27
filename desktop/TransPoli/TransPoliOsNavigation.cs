using System.Windows;

namespace TransPoli;

/// <summary>
/// Navegação explícita do TransPoli OS. Evita que a Home dependa dos hooks
/// antigos que procuram botões pela árvore visual.
/// </summary>
public partial class MainWindow
{
    private async void NotificationsOsButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await ShowNotificationsTabletModalAsync();
    }

    private void ProfileOsButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowMyProfileModal();
    }

    private void MyTruckOsButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowMyTruckModal();
    }

    private async void MaintenanceOsButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await ShowMaintenanceTabletModalAsync();
    }
}
