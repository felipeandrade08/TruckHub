using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private UIElement BuildBankBody(BankData data)
    {
        var root = new StackPanel();
        var source = data.OfficialDataLoaded ? "OFICIAL" : "LOCAL / CACHE";
        var syncTone = data.PendingSyncCount > 0 ? "Yellow" : data.OfficialDataLoaded ? "Green" : "GoldBright";

        var executive = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        executive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        executive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var hero = ModalHero("BANCO TRANSPOLI", "Conta operacional do motorista",
            $"Fonte {source} • receitas, despesas e acertos vinculados à operação TransPoli.",
            Money(data.Balance), data.Balance >= 0 ? "Green" : "Yellow");
        hero.Margin = new Thickness(0, 0, 8, 0);
        executive.Children.Add(hero);

        var account = new StackPanel();
        account.Children.Add(ModalSectionTitle("Situação da conta"));
        account.Children.Add(ModalValueRow("Fonte", source, data.OfficialDataLoaded ? "Green" : "GoldBright"));
        account.Children.Add(ModalValueRow("Sincronização",
            data.PendingSyncCount > 0 ? $"{data.PendingSyncCount} PENDENTE(S)" : "SEM PENDÊNCIAS", syncTone));
        account.Children.Add(ModalValueRow("Viagens liquidadas", data.TripCount.ToString("N0")));
        var accountPanel = ModalPanel(account);
        accountPanel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(accountPanel, 1);
        executive.Children.Add(accountPanel);
        root.Children.Add(executive);

        var metrics = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
        metrics.Children.Add(BankExecutiveMetric("ENTRADAS", Money(data.TotalCredits), "Green"));
        metrics.Children.Add(BankExecutiveMetric("SAÍDAS", Money(data.TotalDebits), "Yellow"));
        metrics.Children.Add(BankExecutiveMetric("RESULTADO", Money(data.TotalCredits - data.TotalDebits),
            data.TotalCredits - data.TotalDebits >= 0 ? "Green" : "Yellow"));
        metrics.Children.Add(BankExecutiveMetric("DISTÂNCIA", $"{data.StatsDistanceKm:N0} km", "GoldBright"));
        root.Children.Add(metrics);

        root.Children.Add(ModalStatusStrip(
            data.PendingSyncCount > 0
                ? $"↻ {data.PendingSyncCount} MOVIMENTAÇÃO(ÕES) AGUARDANDO SINCRONIZAÇÃO • DADOS LOCAIS PRESERVADOS"
                : data.OfficialDataLoaded
                    ? "✓ CONTA OFICIAL CONSOLIDADA • SEM PENDÊNCIAS LOCAIS"
                    : "● MODO LOCAL / CACHE • O SALDO OFICIAL SERÁ CONSOLIDADO QUANDO DISPONÍVEL",
            syncTone));

        root.Children.Add(ModalSectionTitle("CENTRAL BANCÁRIA", "Extrato, caixa, viagem, tarifas, crédito e desempenho"));
        root.Children.Add(BuildBankTabs());

        root.Children.Add(_bankTab switch
        {
            "caixa" => BuildCashbookTab(data),
            "tarifas" => BuildRatesTab(data),
            "emprestimo" => BuildLoanTab(data),
            "estatisticas" => BuildStatisticsTab(data),
            "viagem" => BuildTripTab(data),
            _ => BuildBalanceTab(data)
        });

        var refresh = ModalButton("↻  ATUALIZAR DADOS DA CONTA");
        refresh.Click += (_, e) =>
        {
            e.Handled = true;
            InvalidateBankCache();
            ShowBankModal(_bankTab);
        };
        root.Children.Add(refresh);
        return root;
    }

    private Border BankExecutiveMetric(string label, string value, string resource)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = FindResource("Muted") as Brush });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = FindResource(resource) as Brush, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        return new Border { Background = FindResource("Panel2") as Brush, BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16),
            Margin = new Thickness(4, 0, 4, 0), Child = stack };
    }

    private void AddBankMetric(Grid grid, int column, string label, string value, string resource)
    {
        var brush = FindResource(resource) as Brush ?? FindResource("Text") as Brush;
        var card = new Border { Background = FindResource("Panel2") as Brush, BorderBrush = FindResource("Stroke") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(13, 11, 13, 11), Margin = new Thickness(column == 0 ? 0 : 3, 0, 3, 0) };
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        box.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = brush, Margin = new Thickness(0, 5, 0, 0) });
        card.Child = box; Grid.SetColumn(card, column); grid.Children.Add(card);
    }
}
