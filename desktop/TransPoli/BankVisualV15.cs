using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private UIElement BuildBankBody(BankData data)
    {
        var root = new StackPanel();

        var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var balance = new StackPanel();
        balance.Children.Add(new TextBlock { Text = "CONTA TRANSPOLI", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        balance.Children.Add(new TextBlock { Text = "Saldo disponível", FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 7, 0, 0) });
        balance.Children.Add(new TextBlock { Text = Money(data.Balance), FontSize = 36, FontWeight = FontWeights.Bold, Foreground = data.Balance >= 0 ? FindResource("Green") as Brush : FindResource("Yellow") as Brush, Margin = new Thickness(0, 1, 0, 0) });
        balance.Children.Add(new TextBlock { Text = $"Atualizado hoje • {data.TripCount} viagens liquidadas", FontSize = 10, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 4, 0, 0) });
        top.Children.Add(balance);

        var badge = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 35, 28)), BorderBrush = new SolidColorBrush(Color.FromRgb(43, 91, 62)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 7, 11, 7), VerticalAlignment = VerticalAlignment.Top };
        badge.Child = new TextBlock { Text = "● CONTA ATIVA", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = FindResource("Green") as Brush };
        Grid.SetColumn(badge, 1); top.Children.Add(badge);

        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(13, 20, 27)), BorderBrush = FindResource("Stroke") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(18), Child = top });

        var quick = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        quick.ColumnDefinitions.Add(new ColumnDefinition()); quick.ColumnDefinitions.Add(new ColumnDefinition()); quick.ColumnDefinitions.Add(new ColumnDefinition());
        AddBankMetric(quick, 0, "ENTRADAS", Money(data.TotalCredits), "Green");
        AddBankMetric(quick, 1, "SAÍDAS", Money(data.TotalDebits), "Yellow");
        AddBankMetric(quick, 2, "VIAGENS PAGAS", data.TripCount.ToString(), "Text");
        root.Children.Add(quick);

        root.Children.Add(BuildBankTabs());

        UIElement content = _bankTab switch
        {
            "caixa" => BuildCashbookTab(data),
            "tarifas" => BuildRatesTab(data),
            "emprestimo" => BuildLoanTab(data),
            "viagem" => BuildTripTab(data),
            _ => BuildBalanceTab(data)
        };
        root.Children.Add(content);

        var refresh = ModalButton("↻ ATUALIZAR CONTA");
        refresh.Click += (_, e) => { e.Handled = true; ShowBankModal(_bankTab); };
        root.Children.Add(refresh);
        return root;
    }

    private void AddBankMetric(Grid grid, int column, string label, string value, string resource)
    {
        var brush = FindResource(resource) as Brush ?? FindResource("Text") as Brush;
        var card = new Border { Background = FindResource("Panel2") as Brush, BorderBrush = FindResource("Stroke") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0) };
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        box.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = brush, Margin = new Thickness(0, 5, 0, 0) });
        card.Child = box; Grid.SetColumn(card, column); grid.Children.Add(card);
    }
}
