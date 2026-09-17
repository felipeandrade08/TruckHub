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
/// Integra os recursos antigos ao cockpit novo sem depender do texto para decidir
/// qual tela abrir. Cada atalho recebe uma Tag própria e uma ação explícita.
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

        WireTripNavigation(window);

        var quick = FindQuickGrid(window);
        if (quick == null) return;
        if (quick.Children.OfType<Button>().Any(b => Equals(b.Tag, "feature-cargo-market"))) return;

        quick.Columns = 4;
        quick.Rows = 2;

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
            window.ShowCargoMarketModal();
        });

        AddButton(quick, "🧾 NOTA FISCAL", "feature-invoice", (_, args) =>
        {
            args.Handled = true;
            window.ShowRealisticInvoiceModal();
        });
    }

    private static void WireTripNavigation(MainWindow window)
    {
        foreach (var button in FindButtons(window))
        {
            if (button.Tag != null) continue;
            var text = button.Content?.ToString() ?? string.Empty;
            if (!text.Contains("VIAGEM", StringComparison.OrdinalIgnoreCase)) continue;

            // O botão VIAGEM não deve ser confundido com CARGA/MERCADO.
            button.Tag = "nav-trip-center";
            button.Click += (_, args) =>
            {
                args.Handled = true;
                window.ShowTripCenterModal();
            };
        }
    }

    private static System.Collections.Generic.IEnumerable<Button> FindButtons(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button) yield return button;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
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
            Padding = new Thickness(8, 9, 8, 9),
            FontSize = 10,
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
                    (b.Content?.ToString() ?? string.Empty).Contains("ABAST.", StringComparison.OrdinalIgnoreCase)))
                return grid;

            var found = FindQuickGrid(child);
            if (found != null) return found;
        }
        return null;
    }
}
