using System;
using System.Collections.Generic;
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

internal static class TabletFeatureIntegration
{
    internal static bool Register()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        return true;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;

        var quick = FindQuickGrid(window);
        if (quick != null && !quick.Children.OfType<Button>().Any(b => Equals(b.Tag, "feature-cargo-market")))
        {
            // Keep the dashboard compact: the save/fleet details live behind Meu Caminhão.
            quick.Columns = 4;
            quick.Rows = 2;

            AddButton(quick, "💰 BANCO", "feature-bank", (_, args) =>
            {
                args.Handled = true;
                window.ShowBankModal();
            });
            AddButton(quick, "🚛 VEÍCULO & FROTA", "feature-my-truck", (_, args) =>
            {
                args.Handled = true;
                window.ShowMyTruckModal();
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

        AddProfileNavigation(window);
    }

    private static void AddProfileNavigation(MainWindow window)
    {
        var navigation = FindNavigationStack(window);
        if (navigation == null || navigation.Children.OfType<Button>().Any(b => Equals(b.Tag, "feature-profile")))
            return;

        var template = navigation.Children.OfType<Button>().FirstOrDefault();
        var button = new Button
        {
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "👤", FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), FontSize = 19, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "MEU PERFIL", HorizontalAlignment = HorizontalAlignment.Center }
                }
            },
            Tag = "feature-profile",
            Style = template?.Style,
            Margin = new Thickness(2),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += (_, args) =>
        {
            args.Handled = true;
            window.ShowMyProfileModal();
        };

        var fuel = navigation.Children.OfType<Button>()
            .FirstOrDefault(b => (FindButtonText(b) ?? string.Empty)
                .Contains("COMBUSTÍVEL", StringComparison.OrdinalIgnoreCase));

        if (fuel != null)
        {
            var index = navigation.Children.IndexOf(fuel);
            navigation.Children.Insert(Math.Max(0, index), button);
        }
        else
        {
            navigation.Children.Add(button);
        }
    }

    private static string? FindButtonText(Button button)
    {
        if (button.Content is string text) return text;
        if (button.Content is DependencyObject root)
        {
            var texts = FindVisualChildren<TextBlock>(root).Select(t => t.Text);
            return string.Join(" ", texts);
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
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
            if (child is UniformGrid grid && grid.Children.OfType<Button>().Any(b =>
                (b.Content?.ToString() ?? string.Empty).Contains("ABAST.", StringComparison.OrdinalIgnoreCase)))
                return grid;

            var found = FindQuickGrid(child);
            if (found != null) return found;
        }
        return null;
    }

    private static StackPanel? FindNavigationStack(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is StackPanel panel &&
                panel.Children.OfType<Button>().Any(b =>
                    (FindButtonText(b) ?? string.Empty).Contains("PAINEL", StringComparison.OrdinalIgnoreCase)) &&
                panel.Children.OfType<Button>().Any(b =>
                    (FindButtonText(b) ?? string.Empty).Contains("MEU BANCO", StringComparison.OrdinalIgnoreCase)))
                return panel;

            var found = FindNavigationStack(child);
            if (found != null) return found;
        }
        return null;
    }
}
