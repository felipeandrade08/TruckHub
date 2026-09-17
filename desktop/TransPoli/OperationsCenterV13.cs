using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

// V1.0.13 — Central de Operações: painel ao vivo com cálculos consistentes.
// Não cria atalhos de teclado; a abertura continua dentro do tablet.
public sealed class OperationsCenterV13
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MainWindow? _main;
    private Window? _window;
    private readonly Dictionary<string, TextBlock> _cards = new();
    private TextBlock? _status;
    private TextBlock? _route;
    private TextBlock? _cargo;
    private DateTime _lastTelemetry = DateTime.MinValue;

    public static void Register(MainWindow main)
    {
        var instance = new OperationsCenterV13();
        instance.Hook(main);
    }

    private void Hook(MainWindow main)
    {
        _main = main;
        foreach (var b in FindButtons(main))
        {
            var text = b.Content?.ToString() ?? string.Empty;
            if (!text.Contains("RESUMO", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("VIAGEM", StringComparison.OrdinalIgnoreCase)) continue;
            b.PreviewMouseLeftButtonDown -= ButtonPreview;
            b.PreviewMouseLeftButtonDown += ButtonPreview;
            b.PreviewKeyDown -= ButtonPreviewKey;
            b.PreviewKeyDown += ButtonPreviewKey;
        }
    }

    private void ButtonPreview(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_main != null) Open(_main);
    }

    private void ButtonPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Space) return;
        e.Handled = true;
        if (_main != null) Open(_main);
    }

    private void Open(MainWindow main)
    {
        if (_window != null && _window.IsVisible)
        {
            _window.Activate();
            return;
        }

        _window = new Window
        {
            Title = "TransPoli • Central de Operações",
            Width = 1120,
            Height = 720,
            MinWidth = 900,
            MinHeight = 600,
            Owner = main,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush(main, "Bg"),
            Foreground = Brush(main, "Text")
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "CENTRAL DE OPERAÇÕES", FontSize = 25, FontWeight = FontWeights.Bold });
        _status = new TextBlock { FontSize = 11, Foreground = Brush(main, "Muted"), Margin = new Thickness(0, 3, 0, 12) };
        header.Children.Add(_status);
        root.Children.Add(header);

        var cards = new UniformGrid { Columns = 5, Margin = new Thickness(0, 0, 0, 12) };
        AddCard(cards, main, "STATUS", "status");
        AddCard(cards, main, "DISTÂNCIA", "distance");
        AddCard(cards, main, "COMBUSTÍVEL", "fuel");
        AddCard(cards, main, "CONSUMO", "consumption");
        AddCard(cards, main, "CUSTO COMBUSTÍVEL", "fuelCost");
        Grid.SetRow(cards, 1); root.Children.Add(cards);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel();
        _route = AddLarge(body, main, "ROTA");
        _cargo = AddLarge(body, main, "CARGA");
        AddLarge(body, main, "ECONOMIA DA VIAGEM", "O valor recebido é calculado no Banco do Motorista conforme tarifa da carga, distância, peso, bônus, avarias e regras do servidor. A Central não duplica essa regra.");
        AddLarge(body, main, "VALIDAÇÃO DOS DADOS", "• Distância: odômetro da telemetria\n• Combustível: consumo acumulado, resistente a abastecimentos\n• Consumo: litros ÷ quilômetros × 100\n• Custo: litros consumidos × preço configurado no Banco\n• Dados zerados ou inválidos não são transformados em valores falsos");
        scroll.Content = body;
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);

        _window.Content = root;
        _window.Closed += (_, _) => { _timer.Stop(); _window = null; };
        _timer.Tick -= TimerTick;
        _timer.Tick += TimerTick;
        _timer.Start();
        Update();
        _window.ShowDialog();
    }

    private void TimerTick(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var main = _main;
        if (main == null || _window == null || !_window.IsVisible) return;

        var active = Get(main, "_tripActive", false);
        var analytics = Get(main, "_drivingAnalytics", (TransPoliDrivingAnalytics?)null);
        var distance = analytics == null ? 0f : Math.Max(0, Get(analytics, "_tripDistance", 0f));
        var fuel = analytics == null ? 0f : Math.Max(0, Get(analytics, "_tripFuelConsumed", 0f));
        var consumption = distance > 0.5f ? fuel / distance * 100f : 0f;
        var fuelPrice = 5.98f;
        var fuelCost = fuel * fuelPrice;

        _cards["status"].Text = active ? "EM VIAGEM" : "AGUARDANDO";
        _cards["distance"].Text = $"{distance:0.0} km";
        _cards["fuel"].Text = $"{fuel:0.0} L";
        _cards["consumption"].Text = consumption > 0 ? $"{consumption:0.0} L/100 km" : "--";
        _cards["fuelCost"].Text = fuel > 0 ? $"R$ {fuelCost:0.00}" : "--";
        if (_route != null) _route.Text = main.TripRouteText?.Text ?? "Nenhuma rota informada";
        if (_cargo != null) _cargo.Text = main.TripCargoText?.Text ?? "Nenhuma carga informada";

        if (ReadTelemetry() != null)
        {
            _lastTelemetry = DateTime.UtcNow;
            _status!.Text = $"Telemetria online • {DateTime.Now:HH:mm:ss} • {(active ? "viagem ativa" : "sem viagem ativa")}";
        }
        else
        {
            var age = _lastTelemetry == DateTime.MinValue ? "sem leitura" : $"última leitura há {(int)(DateTime.UtcNow - _lastTelemetry).TotalSeconds}s";
            _status!.Text = $"Telemetria indisponível • {age} • cálculos preservados sem inventar dados";
        }
    }

    private TelemetrySnapshot? ReadTelemetry()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            var json = client.GetStringAsync(MainWindow.TelemetryUrl).GetAwaiter().GetResult();
            return JsonSerializer.Deserialize<TelemetrySnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static TextBlock AddLarge(Panel panel, MainWindow main, string title, string? value = null)
    {
        var b = new Border { Background = Brush(main, "Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 8) };
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, FontSize = 9, Foreground = Brush(main, "Muted") });
        var t = new TextBlock { Text = value ?? "", FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        p.Children.Add(t); b.Child = p; panel.Children.Add(b); return t;
    }

    private void AddCard(Panel panel, MainWindow main, string title, string key)
    {
        var b = new Border { Background = Brush(main, "Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 7, 0) };
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = title, FontSize = 9, Foreground = Brush(main, "Muted") });
        var t = new TextBlock { Text = "--", FontSize = 18, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
        p.Children.Add(t); b.Child = p; panel.Children.Add(b); _cards[key] = t;
    }

    private static SolidColorBrush Brush(MainWindow main, string key) => main.FindResource(key) as SolidColorBrush ?? Brushes.White;
    private static T Get<T>(object target, string name, T fallback)
    {
        var value = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);
        return value is T typed ? typed : fallback;
    }
    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        if (root == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b) yield return b;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }
}

public partial class MainWindow
{
    private static readonly bool _operationsCenterV13Registered = RegisterOperationsCenterV13();

    private static bool RegisterOperationsCenterV13()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is MainWindow main) OperationsCenterV13.Register(main);
            }));
        return true;
    }
}
