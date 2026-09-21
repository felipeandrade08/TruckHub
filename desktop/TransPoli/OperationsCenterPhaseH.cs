using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Fase H — Central de Operações dentro do tablet.
/// Consolida em uma única tela os estados já disponíveis no cliente, sem criar atalho novo.
/// </summary>
public sealed class OperationsCenterPhaseH
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MainWindow? _main;
    private Border? _panel;
    private readonly Dictionary<string, TextBlock> _values = new();
    private TextBlock? _route;
    private TextBlock? _cargo;
    private TextBlock? _financial;
    private TextBlock? _lastUpdate;

    public static void Register(MainWindow main)
    {
        var center = new OperationsCenterPhaseH { _main = main };
        center.Attach();
    }

    private void Attach()
    {
        if (_main == null) return;
        foreach (var button in FindButtons(_main))
        {
            var text = button.Content?.ToString() ?? "";
            if (text.Contains("CENTRAL", StringComparison.OrdinalIgnoreCase))
            {
                button.Click -= OpenFromButton;
                button.Click += OpenFromButton;
            }
        }
    }

    private void OpenFromButton(object sender, RoutedEventArgs e)
    {
        if (_main == null) return;
        e.Handled = true;
        if (_panel == null) Build();
        if (_panel == null) return;
        _panel.Visibility = Visibility.Visible;
        Update();
        _timer.Start();
    }

    private void Build()
    {
        if (_main == null) return;
        var screen = FindTabletScreen(_main);
        if (screen?.Child is not Grid screenGrid) return;

        _panel = new Border
        {
            Margin = new Thickness(18), Padding = new Thickness(16),
            CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            BorderBrush = Brush("#1D3B57"), Background = Brush("#07111C"),
            Visibility = Visibility.Collapsed
        };
        Panel.SetZIndex(_panel, 2000);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "CENTRAL DE OPERAÇÕES", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brush("#FFFFFF") });
        var close = Button("✕", 11);
        close.ToolTip = "Voltar ao tablet";
        close.Click += (_, _) => { _panel!.Visibility = Visibility.Collapsed; _timer.Stop(); };
        Grid.SetColumn(close, 1); header.Children.Add(close);
        root.Children.Add(header);

        _lastUpdate = new TextBlock { Text = "Atualizando…", FontSize = 10, Foreground = Brush("#8FA1B2"), Margin = new Thickness(0, 4, 0, 10) };
        Grid.SetRow(_lastUpdate, 1); root.Children.Add(_lastUpdate);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel();
        body.Children.Add(Section("VIAGEM"));
        _route = Value(body, "Rota");
        _cargo = Value(body, "Carga / peso");
        AddMetricRow(body, "DISTÂNCIA TOTAL", "distance");
        AddMetricRow(body, "PERCORRIDA", "progress");
        AddMetricRow(body, "RESTANTE", "remaining");
        AddMetricRow(body, "TEMPO", "time");
        AddMetricRow(body, "ETA", "eta");

        body.Children.Add(Section("CAMINHÃO"));
        AddMetricRow(body, "VELOCIDADE", "speed");
        AddMetricRow(body, "COMBUSTÍVEL", "fuel");
        AddMetricRow(body, "CONSUMO", "consumption");
        AddMetricRow(body, "ODÔMETRO", "odometer");
        AddMetricRow(body, "MOTOR / MARCHA", "engine");

        body.Children.Add(Section("FINANCEIRO"));
        _financial = Value(body, "Resultado projetado");
        AddMetricRow(body, "CARGA", "revenue");
        AddMetricRow(body, "COMBUSTÍVEL", "fuelCost");
        AddMetricRow(body, "MANUTENÇÃO", "maintenance");
        AddMetricRow(body, "PEDÁGIO", "toll");
        AddMetricRow(body, "LÍQUIDO", "profit");

        body.Children.Add(Section("STATUS"));
        AddMetricRow(body, "TELEMETRIA", "telemetry");
        AddMetricRow(body, "VEÍCULO", "vehicle");
        scroll.Content = body;
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        _panel.Child = root;
        screenGrid.Children.Add(_panel);
    }

    private void Update()
    {
        if (_main == null || _panel == null || _panel.Visibility != Visibility.Visible) return;
        var data = GetTelemetry(_main);
        var active = Get(_main, "_tripActive", false);
        var analytics = Get(_main, "_drivingAnalytics", (TransPoliDrivingAnalytics?)null);
        var distance = analytics == null ? 0f : Math.Max(0, Get(analytics, "_tripDistance", 0f));
        var fuel = analytics == null ? 0f : Math.Max(0, Get(analytics, "_tripFuelConsumed", 0f));
        var consumption = distance > 0.5f && fuel > 0 ? fuel / distance * 100f : 0f;

        Set("distance", data?.PlannedDistanceKm > 0 ? $"{data.PlannedDistanceKm:0.0} km" : "—");
        Set("progress", $"{distance:0.0} km");
        Set("remaining", data?.PlannedDistanceKm > distance ? $"{data.PlannedDistanceKm - distance:0.0} km" : "—");
        Set("time", active ? "EM ANDAMENTO" : "AGUARDANDO");
        Set("eta", "Calculada pela telemetria quando disponível");
        Set("speed", data == null ? "—" : $"{Math.Abs(data.SpeedKph):0} km/h");
        Set("fuel", data == null ? "—" : $"{data.FuelLiters:0.0} L • autonomia {data.FuelRangeKm:0} km");
        Set("consumption", consumption > 0 ? $"{consumption:0.0} L/100 km" : "—");
        Set("odometer", data == null ? "—" : $"{data.OdometerKm:0.0} km");
        Set("engine", data == null ? "—" : $"{(data.EngineEnabled ? "LIGADO" : "DESLIGADO")} • marcha {data.Gear}");
        Set("telemetry", data?.Connected == true ? "🟢 ONLINE" : "🔴 OFFLINE");
        Set("vehicle", data == null ? "—" : $"{data.TruckBrand} {data.TruckModel} • {data.LicensePlate}");
        Set("revenue", data?.CargoValueBrl > 0 ? $"R$ {data.CargoValueBrl:0.00}" : "Calculada pelo mercado");

        // O custo só cresce quando o consumo real da viagem cresce. Como o
        // analytics agora só registra combustível junto com avanço do odômetro,
        // ficar parado não altera custo nem consumo.
        var fuelCost = fuel * 5.98f;
        Set("fuelCost", fuel > 0 ? $"R$ {fuelCost:0.00}" : "—");
        Set("maintenance", "Conforme registros disponíveis");
        Set("toll", "Conforme registros disponíveis");
        Set("profit", data?.CargoValueBrl > 0 && fuel > 0 ? $"R$ {data.CargoValueBrl - fuelCost:0.00} antes de outros custos" : "Aguardando dados");
        if (_route != null) _route.Text = _main.TripRouteText?.Text ?? "Nenhuma rota ativa";
        if (_cargo != null) _cargo.Text = _main.TripCargoText?.Text ?? "Nenhuma carga ativa";
        if (_financial != null) _financial.Text = data?.CargoValueBrl > 0 ? $"Receita estimada: R$ {data.CargoValueBrl:0.00}" : "Receita aguardando valor da carga";
        if (_lastUpdate != null) _lastUpdate.Text = $"Atualização {DateTime.Now:HH:mm:ss} • telemetria real";
    }

    private static TelemetrySnapshot? GetTelemetry(MainWindow main)
    {
        try { return main.GetType().GetMethod("GetLatestTelemetry", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(main, null) as TelemetrySnapshot; }
        catch { return null; }
    }

    private void AddMetricRow(Panel panel, string label, string key)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, FontSize = 10, Foreground = Brush("#8295A8"), VerticalAlignment = VerticalAlignment.Center });
        var value = new TextBlock { Text = "—", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush("#FFFFFF"), TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(value, 1); grid.Children.Add(value); panel.Children.Add(grid); _values[key] = value;
    }

    private TextBlock Value(Panel panel, string label)
    {
        var b = new Border { Background = Brush("#0D2032"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(0, 3, 0, 6) };
        var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), FontSize = 9, Foreground = Brush("#8295A8") });
        var value = new TextBlock { Text = "—", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brush("#FFFFFF"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        stack.Children.Add(value); b.Child = stack; panel.Children.Add(b); return value;
    }

    private static TextBlock Section(string text) => new() { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#FF8A00"), Margin = new Thickness(0, 10, 0, 5) };
    private Button Button(string text, double size) => new() { Content = text, Style = _main?.FindResource("TabletButton") as Style, FontSize = size, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(2) };
    private void Set(string key, string value) { if (_values.TryGetValue(key, out var block)) block.Text = value; }
    private static T Get<T>(object target, string name, T fallback) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) is T value ? value : fallback;
    private static Border? FindTabletScreen(DependencyObject root) => FindVisual<Border>(root).FirstOrDefault(b => Math.Abs(b.CornerRadius.TopLeft - 8) < .1 && b.Child is Grid && b.ActualWidth > 200);
    private static IEnumerable<Button> FindButtons(DependencyObject root) => FindVisual<Button>(root);
    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var x in FindVisual<T>(child)) yield return x; }
    }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
}

public partial class MainWindow
{
    private static readonly bool PhaseHRegistered = RegisterPhaseH();
    private static bool RegisterPhaseH()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => { if (sender is MainWindow main) OperationsCenterPhaseH.Register(main); }));
        return true;
    }
}
