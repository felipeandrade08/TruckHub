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

        root.Children.Add(ModalHero("BANCO TRANSPOLI", "Conta operacional do motorista", "Economia própria TransPoli • saldo, receitas, despesas, crédito e desempenho reunidos em uma única central.", Money(data.Balance), data.Balance >= 0 ? "Green" : "Yellow"));
        root.Children.Add(ModalStatusStrip(data.SyncStatus == "PENDENTE DE SINCRONIZAÇÃO" ? $"↻ {data.PendingSyncCount} movimentação(ões) pendente(s) • operação local preservada" : data.SyncStatus == "SINCRONIZADO" ? "✓ CONTA SINCRONIZADA • DADOS LOCAIS ATUALIZADOS" : "● BANCO LOCAL • FUNCIONA OFFLINE", data.SyncStatus == "PENDENTE DE SINCRONIZAÇÃO" ? "Yellow" : "Green"));

        var top = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var balance = new StackPanel();
        balance.Children.Add(new TextBlock
        {
            Text = "CONTA OPERACIONAL • TRANSPOLI",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Muted") as Brush,
            Margin = new Thickness(0, 0, 0, 4)
        });
        balance.Children.Add(new TextBlock { Text = "SALDO DISPONÍVEL", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        balance.Children.Add(new TextBlock { Text = Money(data.Balance), FontSize = 34, FontWeight = FontWeights.Bold, Foreground = data.Balance >= 0 ? FindResource("Green") as Brush : FindResource("Yellow") as Brush, Margin = new Thickness(0, 2, 0, 0) });
        balance.Children.Add(new TextBlock { Text = $"{data.TripCount} viagens liquidadas • conta ativa", FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 2, 0, 0) });
        header.Children.Add(balance);
        var badge = new Border { Background = new SolidColorBrush(Color.FromRgb(20, 35, 28)), BorderBrush = new SolidColorBrush(Color.FromRgb(43, 91, 62)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 6, 9, 6), VerticalAlignment = VerticalAlignment.Top };
        badge.Child = new TextBlock { Text = data.SyncStatus == "PENDENTE DE SINCRONIZAÇÃO" ? "↻ PENDENTE • OFFLINE OK" : data.SyncStatus == "SINCRONIZADO" ? "✓ SINCRONIZADO • PIX" : "● BANCO LOCAL • PIX", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = data.SyncStatus == "PENDENTE DE SINCRONIZAÇÃO" ? FindResource("Yellow") as Brush : FindResource("Green") as Brush };
        Grid.SetColumn(badge, 1); header.Children.Add(badge);
        top.Child = header;
        root.Children.Add(top);

        root.Children.Add(ModalSectionTitle("VISÃO FINANCEIRA", "INDICADORES PRINCIPAIS"));
        var quick1 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        quick1.ColumnDefinitions.Add(new ColumnDefinition()); quick1.ColumnDefinitions.Add(new ColumnDefinition()); quick1.ColumnDefinitions.Add(new ColumnDefinition()); quick1.ColumnDefinitions.Add(new ColumnDefinition());
        AddBankMetric(quick1, 0, "ENTRADAS", Money(data.TotalCredits), "Green");
        AddBankMetric(quick1, 1, "SAÍDAS", Money(data.TotalDebits), "Yellow");
        AddBankMetric(quick1, 2, "VIAGENS", data.StatsTrips.ToString("N0"), "Text");
        AddBankMetric(quick1, 3, "DISTÂNCIA", $"{data.StatsDistanceKm:N0} km", "Text");
        root.Children.Add(quick1);

        var quick2 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        quick2.ColumnDefinitions.Add(new ColumnDefinition()); quick2.ColumnDefinitions.Add(new ColumnDefinition()); quick2.ColumnDefinitions.Add(new ColumnDefinition()); quick2.ColumnDefinitions.Add(new ColumnDefinition());
        AddBankMetric(quick2, 0, "RECEITA", Money(data.StatsRevenue), "Green");
        AddBankMetric(quick2, 1, "DESPESAS", Money(data.StatsExpenses), "Yellow");
        AddBankMetric(quick2, 2, "LUCRO", Money(data.StatsProfit), data.StatsProfit >= 0 ? "Green" : "Yellow");
        AddBankMetric(quick2, 3, "COMBUSTÍVEL", $"{data.StatsFuelLiters:N0} L", "Text");
        root.Children.Add(quick2);

        var quick3 = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        quick3.ColumnDefinitions.Add(new ColumnDefinition()); quick3.ColumnDefinitions.Add(new ColumnDefinition()); quick3.ColumnDefinitions.Add(new ColumnDefinition()); quick3.ColumnDefinitions.Add(new ColumnDefinition());
        AddBankMetric(quick3, 0, "MÉDIA", data.StatsAverageKmPerLiter > 0 ? $"{data.StatsAverageKmPerLiter:0.00} km/L" : "—", "Text");
        AddBankMetric(quick3, 1, "RECEITA/KM", data.StatsRevenuePerKm > 0 ? Money(data.StatsRevenuePerKm) : "—", "Text");
        AddBankMetric(quick3, 2, "CUSTO/KM", data.StatsCostPerKm > 0 ? Money(data.StatsCostPerKm) : "—", "Text");
        AddBankMetric(quick3, 3, "LUCRO/KM", data.StatsProfitPerKm != 0 ? Money(data.StatsProfitPerKm) : "—", data.StatsProfitPerKm >= 0 ? "Green" : "Yellow");
        root.Children.Add(quick3);

        var info = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        info.Child = new TextBlock
        {
            Text = "🔒 Banco local • Pix e pagamentos registrados no dispositivo • funciona offline",
            FontSize = 10,
            Foreground = FindResource("Muted") as Brush,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(info);

        root.Children.Add(ModalSectionTitle("CENTRAL BANCÁRIA", "ESCOLHA UMA ÁREA"));
        root.Children.Add(BuildBankTabs());

        UIElement content = _bankTab switch
        {
            "caixa" => BuildCashbookTab(data),
            "tarifas" => BuildRatesTab(data),
            "emprestimo" => BuildLoanTab(data),
            "estatisticas" => BuildStatisticsTab(data),
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
        var card = new Border { Background = FindResource("Panel2") as Brush, BorderBrush = FindResource("Stroke") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(15), Padding = new Thickness(13), Margin = new Thickness(column == 0 ? 0 : 3, 0, 3, 0) };
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        box.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = brush, Margin = new Thickness(0, 5, 0, 0) });
        card.Child = box; Grid.SetColumn(card, column); grid.Children.Add(card);
    }
}
