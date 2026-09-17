using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Correção visual final da V1.0.15. Remove elementos legados que ainda
/// estavam definidos no XAML antigo e deixa o Mercado como referência de tarifa.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _v15CleanupTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly HttpClient _v15CleanupHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private TextBlock? _v15CleanupCargo;
    private TextBlock? _v15CleanupRate;
    private List<(string Cargo, decimal Rate)> _v15CleanupOffers = new();
    private int _v15CleanupIndex;
    private bool _v15CleanupReady;

    private static readonly bool V15CleanupRegistered = RegisterV15Cleanup();

    private static bool RegisterV15Cleanup()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is MainWindow window)
                    window.Dispatcher.BeginInvoke(new Action(window.ApplyV15FinalCleanup), DispatcherPriority.ContextIdle);
            }), true);
        return true;
    }

    private void ApplyV15FinalCleanup()
    {
        if (_v15CleanupReady) return;
        _v15CleanupReady = true;

        ReplaceVersionLabels();
        RemoveLegacyElements();
        ReplaceAlertAreaWithMarket();

        _v15CleanupTimer.Tick += async (_, _) =>
        {
            RenderCleanupMarket();
            await LoadCleanupMarketAsync();
        };
        _v15CleanupTimer.Start();
        _ = LoadCleanupMarketAsync();
    }

    private void ReplaceVersionLabels()
    {
        foreach (var text in Descendants(this).OfType<TextBlock>())
        {
            if (text.Text.Contains("1.0.14", StringComparison.OrdinalIgnoreCase))
                text.Text = text.Text.Replace("1.0.14", "1.0.15", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void RemoveLegacyElements()
    {
        var forbidden = new[] { "PARADAS", "PARADA", "OCORR", "DOCS", "DOCUMENTOS", "ALERTAS" };
        foreach (var element in Descendants(this).ToList())
        {
            var label = element switch
            {
                TextBlock text => text.Text,
                Button button => button.Content?.ToString(),
                _ => null
            };
            if (string.IsNullOrWhiteSpace(label)) continue;
            if (!forbidden.Any(word => label.Contains(word, StringComparison.OrdinalIgnoreCase))) continue;
            if (element is FrameworkElement control)
                control.Visibility = Visibility.Collapsed;
        }

        if (FindName("OpsCounterText") is TextBlock ops)
            ops.Visibility = Visibility.Collapsed;
        if (FindName("AlertText") is TextBlock alert)
            alert.Visibility = Visibility.Collapsed;
    }

    private void ReplaceAlertAreaWithMarket()
    {
        var alert = FindName("AlertText") as TextBlock;
        if (alert == null) return;
        var host = FindAncestor<Panel>(alert);
        var oldBorder = FindAncestor<Border>(alert);
        if (host == null || oldBorder == null) return;

        var market = new Border
        {
            Background = FindResource("Panel") as Brush ?? new SolidColorBrush(Color.FromRgb(20, 25, 32)),
            BorderBrush = FindResource("Stroke") as Brush ?? new SolidColorBrush(Color.FromRgb(48, 58, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = oldBorder.Margin
        };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "MERCADO DE CARGAS", Foreground = FindResource("Muted") as Brush, FontSize = 10, FontWeight = FontWeights.SemiBold });
        _v15CleanupCargo = new TextBlock { Text = "📦 Aguardando carga", Foreground = FindResource("Text") as Brush, FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 5, 0, 0) };
        _v15CleanupRate = new TextBlock { Text = "Tarifa: —", Foreground = FindResource("Yellow") as Brush, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0) };
        content.Children.Add(_v15CleanupCargo);
        content.Children.Add(_v15CleanupRate);
        content.Children.Add(new TextBlock { Text = "Referência por KM • a carga é identificada automaticamente ao engatar no ETS2/ATS", Foreground = FindResource("Muted") as Brush, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var open = new Button { Content = "ABRIR MERCADO", Margin = new Thickness(0, 9, 0, 0), Padding = new Thickness(10, 6, 10, 6), HorizontalAlignment = HorizontalAlignment.Left };
        open.Click += (_, _) => ShowCargoMarketModal();
        content.Children.Add(open);
        market.Child = content;

        var index = host.Children.IndexOf(oldBorder);
        oldBorder.Visibility = Visibility.Collapsed;
        if (index < 0) host.Children.Add(market); else host.Children.Insert(index, market);
    }

    private void RenderCleanupMarket()
    {
        if (_v15CleanupCargo == null || _v15CleanupRate == null || _v15CleanupOffers.Count == 0) return;
        var offer = _v15CleanupOffers[_v15CleanupIndex % _v15CleanupOffers.Count];
        _v15CleanupIndex = (_v15CleanupIndex + 1) % _v15CleanupOffers.Count;
        _v15CleanupCargo.Text = $"📦 {offer.Cargo}";
        _v15CleanupRate.Text = $"R$ {offer.Rate.ToString("0.00", CultureInfo.InvariantCulture)}/km";
    }

    private async System.Threading.Tasks.Task LoadCleanupMarketAsync()
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/cargo-market");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _v15CleanupHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array) return;
            var result = new List<(string Cargo, decimal Rate)>();
            foreach (var item in offers.EnumerateArray())
            {
                var cargo = item.TryGetProperty("display_name", out var c) ? c.ToString() : "Carga geral";
                var rate = item.TryGetProperty("rate_brl_km", out var r) && r.TryGetDecimal(out var value) ? value : 0m;
                if (!string.IsNullOrWhiteSpace(cargo)) result.Add((cargo, rate));
            }
            if (result.Count > 0) _v15CleanupOffers = result;
        }
        catch { }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
