using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TruckHub;

/// <summary>Painel "HOJE": próximas ações, resumo e metas da semana, conquistas, cargas que compensam,
/// manutenção preditiva e modo silencioso dos alertas.</summary>
public partial class MainWindow
{
    private static string QuietPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TruckHub", "quiet-alerts.txt");
    private bool _quietAlerts = ReadQuietFlag();

    private static bool ReadQuietFlag()
    {
        try { return File.Exists(QuietPath); } catch { return false; }
    }

    private static void WriteQuietFlag(bool on)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(QuietPath)!);
            if (on) File.WriteAllText(QuietPath, "1"); else if (File.Exists(QuietPath)) File.Delete(QuietPath);
        }
        catch { }
    }

    private void TodayButton_Click(object sender, RoutedEventArgs e) => ShowDriverDayModal();

    private void ShowDriverDayModal()
    {
        if (EnsureModalHost() == null) return;
        var data = LastTelemetry;
        BankData bank;
        try { bank = LoadBankDataLocal(); }
        catch (Exception ex) { App.WriteUiCrashLog("Insights.Bank", ex); bank = new BankData(); }

        var panel = new StackPanel();
        panel.Children.Add(ModalHero("HOJE", "Painel do motorista", "O que fazer agora, como foi a semana e o que compensa.",
            $"{DateTime.Now:dd/MM}", "GoldBright"));

        // ---- Próximas ações
        panel.Children.Add(ModalSectionTitle("PRÓXIMAS AÇÕES"));
        var actions = new StackPanel();
        var any = false;
        void AddAction(string text, string tag, string color) { actions.Children.Add(ModalValueRow(text, tag, color)); any = true; }
        var pendingDocs = _documents.Count(d => !string.Equals(d.Status, "Carimbado", StringComparison.OrdinalIgnoreCase));
        if (pendingDocs > 0) AddAction($"{pendingDocs} nota(s) aguardando carimbo", "NOTAS", "Yellow");
        if (data is { Connected: true })
        {
            if (data.RestStopMinutes > 0 && data.RestStopMinutes <= 60) AddAction($"Pausa obrigatória em {data.RestStopMinutes} min", "DESCANSO", data.RestStopMinutes <= 10 ? "Yellow" : "GoldBright");
            if (data.BrakeTemperature >= BrakeHotC) AddAction($"Freios a {data.BrakeTemperature:0}°C", "FREIOS", data.BrakeTemperature >= BrakeCriticalC ? "Yellow" : "GoldBright");
            if (data.FuelLiters > 0 && data.FuelRangeKm > 0 && data.FuelRangeKm < 150) AddAction($"Autonomia de {data.FuelRangeKm:0} km", "COMBUSTÍVEL", "Yellow");
            foreach (var (name, v) in new[] { ("Motor", data.WearEngine), ("Transmissão", data.WearTransmission), ("Chassi", data.WearChassis), ("Pneus/rodas", data.WearWheels), ("Cabine", data.WearCabin) })
                if (v >= 0.5f) AddAction($"{name} com {v * 100:0}% de desgaste", "OFICINA", v >= 0.75f ? "Yellow" : "GoldBright");
        }
        if (bank.HasLoan && bank.LoanRemaining > 0) AddAction($"Crédito em aberto: {Money(bank.LoanRemaining)}", "CRÉDITO", "Text");
        if (!any) actions.Children.Add(ModalValueRow("Tudo em ordem por aqui", "OK", "Green"));
        panel.Children.Add(ModalPanel(actions));

        // ---- Semana
        var now = DateTime.UtcNow;
        var done = bank.TripHistory.Where(t => t.FinishedAtUtc != DateTime.MinValue).ToList();
        var thisWeek = done.Where(t => t.FinishedAtUtc >= now.AddDays(-7)).ToList();
        var prevWeek = done.Where(t => t.FinishedAtUtc >= now.AddDays(-14) && t.FinishedAtUtc < now.AddDays(-7)).ToList();
        string Delta(decimal cur, decimal prev) => prev == 0 ? "" : $" ({(cur - prev) / Math.Abs(prev) * 100:+0;-0}% vs. semana anterior)";
        var wKm = thisWeek.Sum(t => t.DistanceKm); var pKm = prevWeek.Sum(t => t.DistanceKm);
        var wGross = thisWeek.Sum(t => t.Gross); var pGross = prevWeek.Sum(t => t.Gross);
        var wNet = thisWeek.Sum(t => t.Net);

        panel.Children.Add(ModalSectionTitle("RESUMO DA SEMANA", "ÚLTIMOS 7 DIAS"));
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 6) };
        tiles.Children.Add(MiniCard("VIAGENS", thisWeek.Count.ToString()));
        tiles.Children.Add(MiniCard("KM", $"{wKm:N0}"));
        tiles.Children.Add(MiniCard("RECEITA", Money(wGross)));
        tiles.Children.Add(MiniCard("LUCRO", Money(wNet)));
        panel.Children.Add(tiles);
        panel.Children.Add(ModalLine($"Receita{Delta(wGross, pGross)} • quilometragem{Delta((decimal)wKm, (decimal)pKm)} • infrações registradas no total: {WeighStation.ViolationCount}", 11));

        // ---- Metas da semana
        panel.Children.Add(ModalSectionTitle("METAS DA SEMANA"));
        var goals = new StackPanel();
        goals.Children.Add(GoalRow("Viagens", thisWeek.Count, 5, v => $"{v:0}"));
        goals.Children.Add(GoalRow("Quilômetros", wKm, 1000, v => $"{v:N0} km"));
        goals.Children.Add(GoalRow("Lucro líquido", (double)wNet, 10000, v => Money((decimal)v)));
        panel.Children.Add(ModalPanel(goals));

        // ---- Conquistas
        panel.Children.Add(ModalSectionTitle("CONQUISTAS"));
        var totalTrips = bank.TripCount; var totalKm = (double)bank.StatsDistanceKm;
        var badges = new (string Name, string Desc, bool Done)[]
        {
            ("Primeira viagem", "conclua 1 viagem", totalTrips >= 1),
            ("Estradeiro", "10 viagens", totalTrips >= 10),
            ("Veterano da pista", "50 viagens", totalTrips >= 50),
            ("Centurião", "100 viagens", totalTrips >= 100),
            ("Mil quilômetros", "1.000 km rodados", totalKm >= 1000),
            ("Dez mil", "10.000 km rodados", totalKm >= 10000),
            ("Volta ao mundo", "40.000 km rodados", totalKm >= 40000),
            ("Semana cheia", "5 viagens em 7 dias", thisWeek.Count >= 5),
            ("Caixa forte", "R$ 10.000 de lucro em 7 dias", wNet >= 10000),
            ("Ficha limpa", "5+ viagens sem nenhuma infração", totalTrips >= 5 && WeighStation.ViolationCount == 0)
        };
        var bx = new StackPanel();
        foreach (var b in badges) bx.Children.Add(ModalValueRow($"{(b.Done ? "✓" : "○")}  {b.Name} — {b.Desc}", b.Done ? "CONQUISTADA" : "EM ABERTO", b.Done ? "Green" : "Muted"));
        panel.Children.Add(ModalPanel(bx));

        // ---- Cargas que compensam (real x estimado)
        panel.Children.Add(ModalSectionTitle("CARGAS QUE COMPENSAM", "LUCRO REAL POR KM"));
        var groups = done.Where(t => !string.IsNullOrWhiteSpace(t.Cargo) && t.DistanceKm > 1)
            .GroupBy(t => t.Cargo.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var km = g.Sum(t => t.DistanceKm);
                var net = g.Sum(t => t.Net);
                var est = g.Sum(t => FreightEstimator.Estimate(t.DistanceKm, (double)t.RatePerKm).Net);
                return (Name: g.Key, Trips: g.Count(), PerKm: km > 0 ? net / (decimal)km : 0m, Net: net, Est: est);
            })
            .OrderByDescending(x => x.PerKm).Take(6).ToList();
        if (groups.Count == 0) panel.Children.Add(ModalStatePanel("SEM DADOS", "Conclua viagens para ver quais cargas rendem mais", "A comparação usa o lucro final de cada viagem.", "Muted"));
        else
        {
            var gx = new StackPanel();
            foreach (var g in groups)
                gx.Children.Add(ModalValueRow($"{g.Name} • {g.Trips} viagem(ns)\nreal {Money(g.Net)} • estimado {Money(g.Est)}", $"{Money(g.PerKm)}/km", g.PerKm >= 0 ? "Green" : "Yellow"));
            panel.Children.Add(ModalPanel(gx));
        }

        // ---- Manutenção preditiva
        panel.Children.Add(ModalSectionTitle("MANUTENÇÃO PREDITIVA", "QUANDO CADA PEÇA CHEGA A 75%"));
        var mx = new StackPanel();
        if (data is { Connected: true })
        {
            foreach (var (key, name, v) in new[] { ("engine", "Motor", data.WearEngine), ("transmission", "Transmissão", data.WearTransmission), ("chassis", "Chassi", data.WearChassis), ("wheels", "Pneus/rodas", data.WearWheels), ("cabin", "Cabine", data.WearCabin) })
            {
                var p = WearHistory.Predict(key, v);
                var txt = v >= 0.75f ? "JÁ NO LIMITE" : p.KmToTarget is { } km ? $"≈ {km:N0} km" : "coletando dados";
                var detail = p.PerThousandKm is { } r ? $"{name} • {v * 100:0}% • +{r * 100:0.0}% a cada 1.000 km" : $"{name} • {v * 100:0}%";
                mx.Children.Add(ModalValueRow(detail, txt, v >= 0.75f ? "Yellow" : "Text"));
            }
        }
        else mx.Children.Add(ModalLine("Conecte o jogo para acompanhar o desgaste. A previsão melhora conforme você roda.", 11));
        panel.Children.Add(ModalPanel(mx));

        // ---- Alertas
        panel.Children.Add(ModalSectionTitle("ALERTAS NA HUD"));
        var quiet = ModalButton(_quietAlerts ? "MODO SILENCIOSO: LIGADO (só críticos) • DESLIGAR" : "MODO SILENCIOSO: DESLIGADO • LIGAR (só críticos)");
        quiet.Click += (_, ev) => { ev.Handled = true; _quietAlerts = !_quietAlerts; WriteQuietFlag(_quietAlerts); ShowDriverDayModal(); };
        panel.Children.Add(quiet);

        ShowModalContent("driver-day", BuildModalCard("HOJE", panel, "Dados do seu histórico real • atualiza ao abrir"));
    }

    private UIElement GoalRow(string label, double value, double goal, Func<double, string> fmt)
    {
        var pct = goal <= 0 ? 0 : Math.Clamp(value / goal, 0, 1);
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        box.Children.Add(ModalValueRow(label, $"{fmt(value)} / {fmt(goal)}", pct >= 1 ? "Green" : "Text"));
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = FindResource("TpSurfaceSoft") as Brush, Margin = new Thickness(0, 3, 0, 0) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, pct), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - pct), GridUnitType.Star) });
        grid.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = FindResource(pct >= 1 ? "Green" : "GoldBright") as Brush });
        track.Child = grid;
        box.Children.Add(track);
        return box;
    }
}
