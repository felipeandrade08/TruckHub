using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

/// <summary>Aba PLANEJAR do banco: meta de economia e simulador de crédito, com base no histórico real.</summary>
public partial class MainWindow
{
    private static string GoalPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli", "bank-goal.txt");

    private static decimal ReadGoal()
    {
        try { return decimal.TryParse(File.ReadAllText(GoalPath).Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0m; }
        catch { return 0m; }
    }

    private static void WriteGoal(decimal value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoalPath)!);
            if (value <= 0) { if (File.Exists(GoalPath)) File.Delete(GoalPath); }
            else File.WriteAllText(GoalPath, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    private UIElement BuildPlannerTab(BankData data)
    {
        var panel = new StackPanel();
        var from = DateTime.UtcNow.AddDays(-30);
        var recent = data.TripHistory.Where(x => x.FinishedAtUtc != DateTime.MinValue && x.FinishedAtUtc >= from).ToList();
        var net30 = recent.Sum(x => x.Net);
        var perDay = net30 / 30m;
        var perTrip = recent.Count > 0 ? net30 / recent.Count : 0m;

        // ---- Meta de economia
        panel.Children.Add(ModalSectionTitle("META DE ECONOMIA", "QUANTO FALTA PARA CHEGAR LÁ"));
        var goal = ReadGoal();
        var box = new StackPanel();
        if (goal > 0)
        {
            var missing = Math.Max(0m, goal - data.Balance);
            var pct = (double)Math.Clamp(data.Balance / goal, 0m, 1m);
            box.Children.Add(ModalValueRow("Meta", Money(goal), "GoldBright"));
            box.Children.Add(ModalValueRow("Saldo atual", Money(data.Balance), data.Balance >= goal ? "Green" : "Text"));
            box.Children.Add(ModalValueRow("Falta", Money(missing), missing > 0 ? "Yellow" : "Green"));
            var track = new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = FindResource("TpSurfaceSoft") as Brush, Margin = new Thickness(0, 6, 0, 6) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, pct), GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - pct), GridUnitType.Star) });
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = FindResource("Green") as Brush });
            track.Child = grid;
            box.Children.Add(track);
            box.Children.Add(ModalLine(missing <= 0 ? "Meta atingida!" :
                perDay > 0 ? $"No ritmo dos últimos 30 dias ({Money(perDay)}/dia de lucro líquido) você chega em cerca de {Math.Ceiling((double)(missing / perDay)):N0} dia(s)."
                           : "Sem lucro líquido nos últimos 30 dias para projetar o prazo.", 11));
        }
        else box.Children.Add(ModalLine("Defina um valor para acompanhar quanto falta (por exemplo, para comprar um caminhão ou quitar um crédito).", 12));
        panel.Children.Add(ModalPanel(box));

        var input = new TextBox
        {
            Text = goal > 0 ? goal.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) : "", Height = 36, FontSize = 13,
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6),
            Background = FindResource("TpSurfaceSoft") as Brush, Foreground = FindResource("Text") as Brush,
            BorderBrush = FindResource("TpStrokeStrong") as Brush, ToolTip = "Valor da meta em R$ (vazio ou 0 remove a meta)"
        };
        panel.Children.Add(input);
        var save = ModalButton("SALVAR META");
        save.Click += (_, e) =>
        {
            e.Handled = true;
            var txt = (input.Text ?? "").Trim().Replace("R$", "").Trim();
            if (txt.Length == 0) WriteGoal(0);
            else if (decimal.TryParse(txt, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), out var v) && v > 0 && v < 1_000_000_000m) WriteGoal(v);
            ShowBankModal("planejar");
        };
        panel.Children.Add(save);

        // ---- Simulador de crédito
        panel.Children.Add(ModalSectionTitle("SIMULADOR DE CRÉDITO", "QUANTAS VIAGENS PARA PAGAR"));
        if (perTrip <= 0)
        {
            panel.Children.Add(ModalStatePanel("SEM BASE DE CÁLCULO", "Faltam viagens recentes",
                "O simulador usa o lucro líquido médio por viagem dos últimos 30 dias. Conclua viagens para habilitar.", "Muted"));
        }
        else
        {
            var sim = new StackPanel();
            sim.Children.Add(ModalValueRow("Lucro líquido médio por viagem", Money(perTrip), "Green"));
            foreach (var amount in new[] { 5000m, 10000m, 20000m, 50000m })
                foreach (var pct in new[] { 20m, 30m })
                {
                    var trips = Math.Ceiling((double)(amount / (perTrip * pct / 100m)));
                    sim.Children.Add(ModalValueRow($"{Money(amount)} com {pct:0}% por viagem", $"≈ {trips:N0} viagens"));
                }
            panel.Children.Add(ModalPanel(sim));
            panel.Children.Add(ModalLine("Estimativa sem juros: o contrato real (juros e percentual) é definido pela política da empresa. Serve para saber se a parcela cabe no seu ritmo.", 11));
        }
        return panel;
    }
}
