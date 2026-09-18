using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly TransPoliOperationsCenter _operationsCenter = new();
}

public sealed class TransPoliOperationsCenter
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<Button> _wired = new();
    private readonly List<TripHistoryRecord> _history = new();
    private readonly string _path;
    private bool _hooked;
    private bool _wasActive;

    public TransPoliOperationsCenter()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "transpoli-trip-history.json");
        Load();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Application.Current?.Dispatcher.BeginInvoke(new Action(Hook), DispatcherPriority.Loaded);
    }

    private void Hook()
    {
        if (_hooked) return;
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null) return;
        _hooked = true;
        main.Loaded += (_, _) => Wire(main);
        // A Central de Operações não possui mais atalho próprio.
        // O F10 fica reservado exclusivamente para abrir/fechar o tablet.
        Wire(main);
    }

    private void Wire(MainWindow main)
    {
        foreach (var b in FindButtons(main))
        {
            if (_wired.Contains(b)) continue;
            var text = b.Content?.ToString() ?? "";
            if (text.Contains("RESUMO", StringComparison.OrdinalIgnoreCase) || text.Contains("VIAGEM", StringComparison.OrdinalIgnoreCase))
            {
                _wired.Add(b);
                b.Click += (_, _) => Open(main);
            }
        }
    }

    private void Tick()
    {
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null) return;
        Wire(main);
        var active = Get(main, "_tripActive", false);
        if (active) _wasActive = true;
        if (!active && _wasActive)
        {
            _wasActive = false;
            CaptureFinished(main);
        }
    }

    private void CaptureFinished(MainWindow main)
    {
        var started = Get(main, "_tripStartedAtUtc", default(DateTime));
        if (started == default) return;
        var analytics = Get(main, "_drivingAnalytics", (TransPoliDrivingAnalytics?)null);
        var distance = analytics is null ? 0f : Get(analytics, "_tripDistance", 0f);
        var fuelUsed = analytics is null
            ? Math.Max(0, Get(main, "_tripStartFuel", 0f) - GetTelemetryFuel(main))
            : Math.Max(0, Get(analytics, "_tripFuelConsumed", 0f));
        var key = $"{started.Ticks}|{Get(main, "_tripStartOdometer", 0f):0.0}";
        if (_history.Any(x => x.Key == key)) return;

        _history.Insert(0, new TripHistoryRecord
        {
            Key = key,
            StartedAtUtc = started,
            FinishedAtUtc = DateTime.UtcNow,
            DistanceKm = Math.Max(0, distance),
            FuelUsedL = Math.Max(0, fuelUsed),
            Route = main.TripRouteText?.Text ?? "",
            Cargo = main.TripCargoText?.Text ?? ""
        });
        if (_history.Count > 200) _history.RemoveRange(200, _history.Count - 200);
        Save();
    }

    private static float GetTelemetryFuel(MainWindow main)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(700) };
            var json = client.GetStringAsync(MainWindow.TelemetryUrl).GetAwaiter().GetResult();
            var data = JsonSerializer.Deserialize<TelemetrySnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return data?.FuelLiters ?? 0;
        }
        catch { return 0; }
    }

    public void Open(MainWindow main)
    {
        var tabs = new TabControl
        {
            Background = Brush(main, "Bg"),
            BorderThickness = new Thickness(0)
        };
        tabs.Items.Add(Tab("VIAGEM ATUAL", Current(main)));
        tabs.Items.Add(Tab("VIAGENS", Trips(main)));
        tabs.Items.Add(Tab("RESUMO", Summary(main)));

        main.ShowStandardModal(
            "operations-history",
            "📊 CENTRAL DE OPERAÇÕES",
            tabs,
            "Histórico e acompanhamento da viagem • dados locais do TransPoli");
    }

    private UIElement Current(MainWindow main)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        AddSection(panel, main, "ESTADO", Lifecycle(main));
        AddSection(panel, main, "ROTA", main.TripRouteText?.Text ?? "Nenhuma viagem ativa");
        AddSection(panel, main, "CARGA", main.TripCargoText?.Text ?? "Não informada");
        AddSection(panel, main, "STATUS", main.TripStatusText?.Text ?? "Aguardando");

        var analytics = Get(main, "_drivingAnalytics", (TransPoliDrivingAnalytics?)null);
        if (analytics is not null)
        {
            var distance = Math.Max(0, Get(analytics, "_tripDistance", 0f));
            var fuel = Math.Max(0, Get(analytics, "_tripFuelConsumed", 0f));
            var consumption = distance > 0.5f ? fuel / distance * 100f : 0f;
            AddSection(panel, main, "DISTÂNCIA REAL", $"{distance:0.0} km");
            AddSection(panel, main, "COMBUSTÍVEL CONSUMIDO", $"{fuel:0.0} L");
            AddSection(panel, main, "CONSUMO MÉDIO", consumption > 0 ? $"{consumption:0.0} L/100 km" : "Calculando...");
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Trips(MainWindow main)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        foreach (var x in _history.Take(100))
        {
            AddSection(panel, main,
                $"{x.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} → {x.FinishedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
                $"{x.Route}\n{x.Cargo}\n{x.DistanceKm:0.0} km • {x.FuelUsedL:0.0} L" +
                (x.DistanceKm > 0.5f ? $" • {x.FuelUsedL / x.DistanceKm * 100:0.0} L/100 km" : ""));
        }
        if (_history.Count == 0) AddSection(panel, main, "VIAGENS", "Nenhuma viagem finalizada localmente ainda.");
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Summary(MainWindow main)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        var analytics = Get(main, "_drivingAnalytics", (TransPoliDrivingAnalytics?)null);
        var distance = analytics is null ? 0f : Math.Max(0, Get(analytics, "_tripDistance", 0f));
        var fuel = analytics is null ? 0f : Math.Max(0, Get(analytics, "_tripFuelConsumed", 0f));
        var avgConsumption = distance > 0.5f ? fuel / distance * 100f : 0f;
        AddSection(panel, main, "DISTÂNCIA REAL", $"{distance:0.0} km");
        AddSection(panel, main, "COMBUSTÍVEL CONSUMIDO", $"{fuel:0.0} L");
        AddSection(panel, main, "CONSUMO MÉDIO", avgConsumption > 0 ? $"{avgConsumption:0.0} L/100 km" : "Sem dados suficientes");
        AddSection(panel, main, "VIAGENS REGISTRADAS", _history.Count.ToString());
        AddSection(panel, main, "TELEMETRIA", Get(main, "_tripActive", false) ? "VIAGEM ATIVA" : "AGUARDANDO");
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static void AddSection(Panel panel, MainWindow main, string title, string value)
    {
        var b = new Border { Background = Brush(main, "Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 8) };
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, FontSize = 9, Foreground = Brush(main, "Muted") });
        p.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        b.Child = p; panel.Children.Add(b);
    }

    private static TabItem Tab(string title, UIElement content) => new() { Header = title, Content = content, FontWeight = FontWeights.Bold, Padding = new Thickness(12, 7, 12, 7) };
    private static SolidColorBrush Brush(MainWindow main, string key) => main.FindResource(key) as SolidColorBrush ?? Brushes.White;
    private static T Get<T>(object target, string name, T fallback) { var value = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target); return value is T typed ? typed : fallback; }
    private static string Lifecycle(MainWindow main) { if (Get(main, "_truckLocked", true)) return "CAMINHÃO BLOQUEADO"; if (!Get(main, "_tripActive", false)) return "AGUARDANDO CARGA"; var text = main.TripStatusText?.Text ?? ""; if (text.Contains("descarregada", StringComparison.OrdinalIgnoreCase)) return "CARGA ENTREGUE"; if (text.Contains("parad", StringComparison.OrdinalIgnoreCase)) return "PARADO"; return "EM VIAGEM"; }
    private static IEnumerable<Button> FindButtons(DependencyObject root) { if (root == null) yield break; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is Button b) yield return b; foreach (var nested in FindButtons(child)) yield return nested; } }
    private void Save() { try { File.WriteAllText(_path, JsonSerializer.Serialize(_history, new JsonSerializerOptions { WriteIndented = true })); } catch { } }
    private void Load() { try { if (File.Exists(_path)) _history.AddRange(JsonSerializer.Deserialize<List<TripHistoryRecord>>(File.ReadAllText(_path)) ?? new()); } catch { } }
}

public sealed class TripHistoryRecord
{
    public string Key { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public string Route { get; set; } = "";
    public string Cargo { get; set; } = "";
    public float DistanceKm { get; set; }
    public float FuelUsedL { get; set; }
}
