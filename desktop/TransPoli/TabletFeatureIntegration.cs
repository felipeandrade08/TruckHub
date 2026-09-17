using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly bool _tabletFeatureIntegration = TabletFeatureIntegration.Register();
}

/// <summary>
/// Mantém as funcionalidades já implementadas acessíveis no cockpit moderno.
/// Os botões são adicionados à Central de Operação sem depender de uma grade
/// específica do layout antigo.
/// </summary>
internal static class TabletFeatureIntegration
{
    internal static bool Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        return true;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;

        var quick = FindQuickGrid(window);
        if (quick == null) return;
        if (quick.Children.OfType<Button>().Any(b => Equals(b.Tag, "feature-bank"))) return;

        AddButton(quick, "💰 BANCO", "feature-bank", (_, args) =>
        {
            args.Handled = true;
            window.ShowBankModal();
        });

        AddButton(quick, "🚛 GARAGEM", "feature-garage", (_, args) =>
        {
            args.Handled = true;
            window.ShowGarageTabletModal();
        });

        AddButton(quick, "📦 MERCADO DE CARGAS", "feature-cargo-market", (_, args) =>
        {
            args.Handled = true;
            window.ShowOperationalModal("cargo");
        });

        AddButton(quick, "🧾 NOTA FISCAL", "feature-invoice", (_, args) =>
        {
            args.Handled = true;
            window.ShowRealisticInvoiceModal();
        });

        AddButton(quick, "⛽ ABASTECIMENTO", "feature-fuel", (_, args) =>
        {
            args.Handled = true;
            window.ShowOperationalModal("fuel");
        });

        AddButton(quick, "🛠️ MANUTENÇÃO", "feature-maintenance", (_, args) =>
        {
            args.Handled = true;
            window.ShowOperationalModal("summary");
        });
    }

    private static void AddButton(UniformGrid grid, string text, string tag, RoutedEventHandler click)
    {
        var template = grid.Children.OfType<Button>().FirstOrDefault();
        var button = new Button
        {
            Content = text,
            Tag = tag,
            Style = template?.Style,
            Margin = new Thickness(3),
            Padding = new Thickness(10, 11, 10, 11),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += click;
        grid.Children.Add(button);
    }

    private static UniformGrid? FindQuickGrid(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UniformGrid grid
                && grid.Children.OfType<Button>().Any(b =>
                    (b.Content?.ToString() ?? "").Contains("ABASTECIMENTO", StringComparison.OrdinalIgnoreCase)))
                return grid;

            var found = FindQuickGrid(child);
            if (found != null) return found;
        }
        return null;
    }
}
