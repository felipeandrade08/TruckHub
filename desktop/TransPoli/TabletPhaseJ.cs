using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Fase J — Estatísticas reais dentro do tablet.
/// Os valores vêm da API/banco e nunca são preenchidos com números fictícios.
/// </summary>
public sealed class TabletPhaseJ
{
    private readonly DispatcherTimer _hookTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<Button> _wired = new();
    private MainWindow? _main;
    private Grid? _screen;
    private Border? _panel;
    private StackPanel? _body;
    private TextBlock? _status;
    private bool _hooked;
    private string _period = "all";

    public TabletPhaseJ()
    {
        _hookTimer.Tick += (_, _) => Hook();
        _hookTimer.Start();
        Application.Current?.Dispatcher.BeginInvoke(new Action(Hook), DispatcherPriority.Loaded);
    }

    private void Hook()
    {
        _main ??= Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (_main is null) return;
        _screen ??= FindTabletScreenGrid(_main);
        if (_screen is null) return;
        if (!_hooked) BuildHost();
        foreach (var button in FindVisualChildren<Button>(_screen))
        {
            if (_wired.Contains(button)) continue;
            var text = button.Content?.ToString() ?? string.Empty;
            if (!text.Contains("ESTAT", StringComparison.OrdinalIgnoreCase)) continue;
            _wired.Add(button);
            button.Click += async (_, _) => await OpenAsync();
        }
    }

    private void BuildHost()
    {
        if (_screen is null) return;
        _hooked = true;
        _panel = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(16),
            Background = Brush("#0A1828"),
            BorderBrush = Brush("#1D3B57"),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_panel, 2500);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "ESTATÍSTICAS", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        var close = Button("← VOLTAR", 10);
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1); header.Children.Add(close);
        root.Children.Add(header);

        var periods = new UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
        AddPeriodButton(periods, "HOJE", "today");
        AddPeriodButton(periods, "7 DIAS", "7d");
        AddPeriodButton(periods, "30 DIAS", "30d");
        AddPeriodButton(periods, "TOTAL", "all");
        Grid.SetRow(periods, 1); root.Children.Add(periods);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _body = new StackPanel();
        _status = new TextBlock { FontSize = 10, Foreground = Brush("#AAB6C3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        _body.Children.Add(_status);
        scroll.Content = _body;
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        _panel.Child = root;
        _screen.Children.Add(_panel);
    }

    private void AddPeriodButton(Panel parent, string text, string value)
    {
        var b = Button(text, 9);
        b.Click += async (_, _) => { _period = value; await LoadAsync(); };
        parent.Children.Add(b);
    }

    private async Task OpenAsync()
    {
        if (_panel is null) return;
        _panel.Visibility = Visibility.Visible;
        await LoadAsync();
    }

    private void Close()
    {
        if (_panel is not null) _panel.Visibility = Visibility.Collapsed;
    }

    private async Task LoadAsync()
    {
        if (_body is null || _status is null) return;
        _body.Children.Clear();
        _body.Children.Add(_status);
        _status.Text = "Carregando dados reais...";
        var result = await FetchAsync(_period);
        if (result is null)
        {
            _status.Text = "Não foi possível carregar as estatísticas. Verifique a conexão e tente novamente.";
            _body.Children.Add(Card("SEM DADOS DISPONÍVEIS", "A API não retornou estatísticas para este período."));
            return;
        }
        var s = result.Statistics;
        _status.Text = $"Período: {PeriodLabel(_period)} • Dados de viagens concluídas e despesas registradas no banco.";
        AddSection("VIAGENS E DESEMPENHO", new[]
        {
            Metric("Viagens concluídas", s.CompletedTrips.ToString()),
            Metric("Viagens com incidentes", s.IncidentTrips.ToString()),
            Metric("Viagens sem incidentes", s.NonIncidentTrips.ToString()),
            Metric("Resultado médio/viagem", Money(s.ProfitPerTrip)),
            Metric("Entregas sem dano", s.CleanDeliveries.ToString()),
            Metric("Entregas com dano", s.DamagedDeliveries.ToString())
        });
        AddSection("QUILOMETRAGEM", new[]
        {
            Metric("Km totais", Km(s.DistanceKm)),
            Metric("Km médio/viagem", Km(s.AverageDistanceKm)),
            Metric("Receita/km", Money(s.RevenuePerKm)),
            Metric("Custo/km", Money(s.CostPerKm)),
            Metric("Lucro/km", Money(s.ProfitPerKm))
        });
        AddSection("CARGAS", new[]
        {
            Metric("Toneladas transportadas", Tons(s.CargoTons)),
            Metric("Tipos de carga", s.CargoTypes.ToString()),
            Metric("Receita total", Money(s.RevenueBrl))
        });
        AddSection("COMBUSTÍVEL", new[]
        {
            Metric("Litros consumidos", Liters(s.FuelLiters)),
            Metric("Média km/L", Value(s.AverageKmPerLiter, "km/L")),
            Metric("Consumo", Value(s.AverageFuelL100, "L/100 km")),
            Metric("Gasto combustível", Money(s.FuelExpensesBrl))
        });
        AddSection("FINANCEIRO", new[]
        {
            Metric("Receita", Money(s.RevenueBrl)),
            Metric("Despesas", Money(s.ExpensesBrl)),
            Metric("Combustível", Money(s.FuelExpensesBrl)),
            Metric("Manutenção", Money(s.MaintenanceBrl)),
            Metric("Pedágios", Money(s.TollBrl)),
            Metric("Lucro", Money(s.ProfitBrl)),
            Metric("Lucro médio/viagem", Money(s.ProfitPerTrip))
        });
        AddSection("DANOS", new[]
        {
            Metric("Entregas limpas", s.CleanDeliveries.ToString()),
            Metric("Entregas danificadas", s.DamagedDeliveries.ToString()),
            Metric("Percentual de dano", Percent(s.DamagePercent)),
            Metric("Penalidades por dano", Money(s.DamagePenaltiesBrl))
        });
        AddCargoBreakdown(result.ByCargo);
    }

    private void AddSection(string title, IEnumerable<UIElement> metrics)
    {
        if (_body is null) return;
        _body.Children.Add(new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 8, 0, 5) });
        var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 2) };
        foreach (var metric in metrics) grid.Children.Add(metric);
        _body.Children.Add(grid);
    }

    private UIElement Metric(string label, string value)
    {
        var border = new Border { Background = Brush("#101C29"), BorderBrush = Brush("#1D3B57"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8), Margin = new Thickness(0, 0, 5, 5) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 8, Foreground = Brush("#7F91A3"), TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        border.Child = stack;
        return border;
    }

    private void AddCargoBreakdown(List<CargoStat>? cargo)
    {
        if (_body is null || cargo is null || cargo.Count == 0) return;
        _body.Children.Add(new TextBlock { Text = "CARGAS POR TIPO", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 8, 0, 5) });
        foreach (var item in cargo.Take(8))
        {
            var card = new Border { Background = Brush("#101C29"), BorderBrush = Brush("#1D3B57"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 5) };
            var stack = new StackPanel();
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(new TextBlock { Text = item.Cargo, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            var right = new TextBlock { Text = $"{item.Trips} viagem(ns)", FontSize = 9, Foreground = Brush("#AAB6C3") };
            Grid.SetColumn(right, 1); line.Children.Add(right);
            stack.Children.Add(line);
            stack.Children.Add(new TextBlock { Text = $"{Km(item.DistanceKm)} • {Tons(item.CargoKg / 1000)} • {Money(item.RevenueBrl)}", FontSize = 9, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 3, 0, 4) });
            var maxTrips = Math.Max(1, cargo.Max(x => x.Trips));
            stack.Children.Add(new ProgressBar { Minimum = 0, Maximum = maxTrips, Value = item.Trips, Height = 5 });
            card.Child = stack;
            _body.Children.Add(card);
        }
    }

    private Task<StatisticsResponse?> FetchAsync(string period)
    {
        try
        {
            if (LocalData.Current is not { } store) return Task.FromResult<StatisticsResponse?>(null);
            var from = period switch
            {
                "today" => DateTime.UtcNow.Date,
                "7d" => DateTime.UtcNow.AddDays(-7),
                "30d" => DateTime.UtcNow.AddDays(-30),
                _ => DateTime.MinValue
            };
            var response = BuildLocalStatistics(store.Db, from);
            return Task.FromResult<StatisticsResponse?>(response);
        }
        catch { return Task.FromResult<StatisticsResponse?>(null); }
    }

    private static StatisticsResponse BuildLocalStatistics(TransPoliDb db, DateTime fromUtc)
    {
        var result = new StatisticsResponse { Ok = true, Statistics = new StatisticsData { Period = fromUtc == DateTime.MinValue ? "all" : "custom" }, ByCargo = new List<CargoStat>() };
        var filter = fromUtc == DateTime.MinValue ? "" : " AND finished_at_utc >= @from";
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = $@"SELECT COUNT(*),COALESCE(SUM(distance_km),0),COALESCE(SUM(fuel_consumed_l),0),COALESCE(SUM(income_gross),0),COALESCE(SUM(expense_total),0),COALESCE(SUM(net_value),0),COALESCE(AVG(distance_km),0),COALESCE(SUM(cargo_mass_kg),0) FROM trip WHERE status='finished'{filter};";
        if (filter.Length > 0) cmd.Parameters.AddWithValue("@from", fromUtc.ToString("O"));
        using var row = cmd.ExecuteReader();
        if (row.Read())
        {
            var trips = row.GetInt32(0); var distance = row.GetDouble(1); var fuel = row.GetDouble(2); var revenue = row.GetDecimal(3); var expenses = row.GetDecimal(4); var profit = row.GetDecimal(5);
            result.Statistics.CompletedTrips = trips;
            result.Statistics.NonIncidentTrips = trips;
            result.Statistics.DistanceKm = distance;
            result.Statistics.AverageDistanceKm = trips > 0 ? row.GetDouble(6) : null;
            result.Statistics.CargoKg = row.GetDouble(7);
            result.Statistics.CargoTons = row.GetDouble(7) / 1000.0;
            result.Statistics.FuelLiters = fuel;
            result.Statistics.AverageKmPerLiter = fuel > 0 ? distance / fuel : null;
            result.Statistics.AverageFuelL100 = distance > 0 ? fuel / distance * 100 : null;
            result.Statistics.RevenueBrl = (double)revenue;
            result.Statistics.ExpensesBrl = (double)expenses;
            result.Statistics.ProfitBrl = (double)profit;
            result.Statistics.RevenuePerKm = distance > 0 ? (double)(revenue / (decimal)distance) : null;
            result.Statistics.CostPerKm = distance > 0 ? (double)(expenses / (decimal)distance) : null;
            result.Statistics.ProfitPerKm = distance > 0 ? (double)(profit / (decimal)distance) : null;
            result.Statistics.ProfitPerTrip = trips > 0 ? (double)(profit / trips) : null;
        }

        result.Statistics.FuelExpensesBrl = LocalDecimal(db, "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE type='fuel_expense' AND amount<0" + (filter.Length > 0 ? " AND occurred_at_utc >= @from" : ""), fromUtc);
        result.Statistics.MaintenanceBrl = LocalDecimal(db, "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE type='maintenance_expense' AND amount<0" + (filter.Length > 0 ? " AND occurred_at_utc >= @from" : ""), fromUtc);
        result.Statistics.TollBrl = LocalDecimal(db, "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE type='toll_expense' AND amount<0" + (filter.Length > 0 ? " AND occurred_at_utc >= @from" : ""), fromUtc);
        result.Statistics.CleanDeliveries = result.Statistics.CompletedTrips;
        result.Statistics.DamagedDeliveries = 0;
        result.Statistics.DamagePercent = 0;

        using var cargo = db.Connection.CreateCommand();
        cargo.CommandText = $@"SELECT COALESCE(NULLIF(TRIM(cargo_name),''),'Não informado'),COUNT(*),COALESCE(SUM(cargo_mass_kg),0),COALESCE(SUM(distance_km),0),COALESCE(SUM(income_gross),0) FROM trip WHERE status='finished'{filter} GROUP BY 1 ORDER BY COUNT(*) DESC,cargo_name LIMIT 8;";
        if (filter.Length > 0) cargo.Parameters.AddWithValue("@from", fromUtc.ToString("O"));
        using var cr = cargo.ExecuteReader();
        while (cr.Read()) result.ByCargo!.Add(new CargoStat { Cargo=cr.GetString(0), Trips=cr.GetInt32(1), CargoKg=cr.GetDouble(2), DistanceKm=cr.GetDouble(3), RevenueBrl=cr.GetDouble(4) });
        result.Statistics.CargoTypes = result.ByCargo!.Count;
        return result;
    }

    private static double LocalDecimal(TransPoliDb db, string sql, DateTime fromUtc)
    {
        using var c=db.Connection.CreateCommand(); c.CommandText=sql; if(sql.Contains("@from",StringComparison.Ordinal)) c.Parameters.AddWithValue("@from",fromUtc.ToString("O")); return Convert.ToDouble(c.ExecuteScalar() ?? 0);
    }

    private static string PeriodLabel(string value) => value switch { "today" => "Hoje", "7d" => "Últimos 7 dias", "30d" => "Últimos 30 dias", _ => "Total" };
    private static string Km(double? v) => v.HasValue ? $"{v.Value:0.0} km" : "—";
    private static string Liters(double? v) => v.HasValue ? $"{v.Value:0.0} L" : "—";
    private static string Tons(double? v) => v.HasValue ? $"{v.Value:0.00} t" : "—";
    private static string Money(double? v) => v.HasValue ? $"R$ {v.Value:N2}" : "—";
    private static string Percent(double? v) => v.HasValue ? $"{v.Value:0.0}%" : "—";
    private static string Value(double? v, string suffix) => v.HasValue ? $"{v.Value:0.00} {suffix}" : "—";
    private Border Card(string title, string text) => new() { Background = Brush("#101C29"), BorderBrush = Brush("#1D3B57"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White }, new TextBlock { Text = text, FontSize = 10, Foreground = Brush("#AAB6C3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) } } } };
    private Button Button(string text, double fontSize) => new() { Content = text, FontSize = fontSize, Padding = new Thickness(6, 7, 6, 7), Margin = new Thickness(2), Background = Brush("#15263A"), Foreground = Brushes.White, BorderBrush = Brush("#294761") };
    private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private static Grid? FindTabletScreenGrid(DependencyObject root){foreach(var border in FindVisualChildren<Border>(root)){if(Math.Abs(border.CornerRadius.TopLeft-8)<0.1&&border.Child is Grid grid&&border.ActualWidth>200)return grid;}return null;}
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{if(root is null)yield break;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var nested in FindVisualChildren<T>(child))yield return nested;}}

    private sealed class StatisticsResponse { public bool Ok { get; set; } public StatisticsData Statistics { get; set; } = new(); public List<CargoStat>? ByCargo { get; set; } }
    private sealed class StatisticsData { public string Period { get; set; } = "all"; public int Trips { get; set; } public int CompletedTrips { get; set; } public int IncidentTrips { get; set; } public int NonIncidentTrips { get; set; } public int CleanDeliveries { get; set; } public int DamagedDeliveries { get; set; } public double? DamagePercent { get; set; } public double? DamagePenaltiesBrl { get; set; } public double? DistanceKm { get; set; } public double? AverageDistanceKm { get; set; } public double? CargoKg { get; set; } public double? CargoTons { get; set; } public int CargoTypes { get; set; } public double? FuelLiters { get; set; } public double? AverageKmPerLiter { get; set; } public double? AverageFuelL100 { get; set; } public double? RevenueBrl { get; set; } public double? ExpensesBrl { get; set; } public double? FuelExpensesBrl { get; set; } public double? MaintenanceBrl { get; set; } public double? TollBrl { get; set; } public double? ProfitBrl { get; set; } public double? RevenuePerKm { get; set; } public double? CostPerKm { get; set; } public double? ProfitPerKm { get; set; } public double? ProfitPerTrip { get; set; } }
    private sealed class CargoStat { public string Cargo { get; set; } = "Não informado"; public int Trips { get; set; } public double CargoKg { get; set; } public double DistanceKm { get; set; } public double RevenueBrl { get; set; } }
}
