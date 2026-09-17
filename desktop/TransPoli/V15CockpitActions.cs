using System.Windows;
using System.Windows.Controls;

namespace TransPoli;

public partial class MainWindow
{
    private void BankButtonV15_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowBankModal();
    }

    private void GarageButtonV15_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowGarageTabletModal();
    }

    private void CargoMarketButtonV15_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowCargoMarketModal();
    }

    private void InvoiceButtonV15_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowRealisticInvoiceModal();
    }
}
