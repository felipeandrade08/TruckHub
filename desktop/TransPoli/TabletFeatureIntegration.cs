using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    // Executa durante a inicialização do tipo, sem mexer no construtor.
    private static readonly bool _tabletFeatureIntegration = TabletFeatureIntegration.Register();
}

/// <summary>
/// Acrescenta os botões de Banco, Garagem e Nota Fiscal à grade de ações
/// rápidas do tablet.
///
/// Cada botão recebe uma Tag própria. Sem ela, o roteador de modais do
/// DocumentModal capturava o clique pelo texto — "🧾 NOTA FISCAL" casava
/// com a regra de "NOTA" e abria a tela de documentos, deixando a nota
/// fiscal completa inacessível.
/// </summary>
internal static class TabletFeatureIntegration
{
    internal static bool Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
        return true;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;

        var quick = FindQuickGrid(window);
        if (quick == null) return;

        // Evita duplicar os botões em um segundo Loaded.
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

        AddButton(quick, "🧾 NOTA FISCAL", "feature-invoice", (_, args) =>
        {
            args.Handled = true;
            window.ShowRealisticInvoiceModal();
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
            Padding = new Thickness(12, 11, 12, 11),
            FontSize = 12,
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
                && grid.Columns == 2
                && grid.Children.OfType<Button>().Any(b => (b.Content?.ToString() ?? "").Contains("ABASTECIMENTO")))
                return grid;

            var found = FindQuickGrid(child);
            if (found != null) return found;
        }
        return null;
    }
}
