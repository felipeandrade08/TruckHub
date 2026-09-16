using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    // Runs during MainWindow type initialization without changing the existing constructor.
    private static readonly bool _tabletFeatureIntegration = TabletFeatureIntegration.Register();

    internal async void ShowGarageTabletModal()
    {
        if (_documentModalHost == null)
        {
            if (Content is not UIElement original) return;
            _documentModalOriginalContent = original;
            var host = new Grid();
            Content = host;
            host.Children.Add(original);
            _documentModalHost = host;
            _documentModalLayer = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)),
                Padding = new Thickness(55, 65, 55, 65)
            };
            host.Children.Add(_documentModalLayer);
        }

        if (_documentModalLayer == null) return;
        _documentModalKind = "garage";
        _documentModalLayer.Visibility = Visibility.Visible;
        _documentModalLayer.Child = new Border
        {
            Background = FindResource("Bg") as Brush,
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(24),
            Child = new TextBlock { Text = "CARREGANDO GARAGEM...", Foreground = FindResource("Text") as Brush, FontSize = 18 }
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "🚛 GARAGEM TRANSPOLI", Foreground = FindResource("Text") as Brush, FontSize = 24, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Caminhões exclusivos vinculados ao motorista", Foreground = FindResource("Muted") as Brush, FontSize = 12, Margin = new Thickness(0, 4, 0, 18) });

        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            panel.Children.Add(ModalLine("Faça login no TransPoli para consultar sua garagem.", 13));
        }
        else
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/garage");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                using var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    panel.Children.Add(ModalLine("Não foi possível consultar a garagem agora. O bloqueio de exclusividade continua funcionando de forma independente.", 13));
                }
                else
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var garage = doc.RootElement.TryGetProperty("garage", out var node) ? node : default;
                    if (garage.ValueKind != JsonValueKind.Array || garage.GetArrayLength() == 0)
                    {
                        panel.Children.Add(ModalLine("Nenhum caminhão exclusivo está vinculado a este motorista.", 13));
                    }
                    else
                    {
                        foreach (var item in garage.EnumerateArray())
                        {
                            var truck = item.TryGetProperty("truck_name", out var tn) ? tn.GetString() : null;
                            var brand = item.TryGetProperty("brand", out var br) ? br.GetString() : null;
                            var model = item.TryGetProperty("model", out var mo) ? mo.GetString() : null;
                            var plate = item.TryGetProperty("license_plate", out var pl) ? pl.GetString() : null;
                            var skin = item.TryGetProperty("skin_code", out var sk) ? sk.GetString() : null;
                            var exclusive = item.TryGetProperty("exclusive", out var ex) && ex.GetBoolean();
                            var text = $"{truck ?? $"{brand} {model}"}\nPlaca: {plate ?? "não informada"}\nSkin/código: {skin ?? "não informado"}\nStatus: {(exclusive ? "EXCLUSIVO PARA ESTE MOTORISTA" : "vinculado")}";
                            panel.Children.Add(new Border
                            {
                                Background = FindResource("Panel2") as Brush,
                                CornerRadius = new CornerRadius(16),
                                Padding = new Thickness(16),
                                Margin = new Thickness(0, 0, 0, 10),
                                Child = new TextBlock { Text = text, Foreground = FindResource("Text") as Brush, FontSize = 13, TextWrapping = TextWrapping.Wrap }
                            });
                        }
                    }
                }
            }
            catch
            {
                panel.Children.Add(ModalLine("Erro de comunicação com a garagem. Tente novamente em instantes.", 13));
            }
        }

        var close = new Button { Content = "✕ FECHAR", Tag = "modal-action", Style = FindResource("TabletButton") as Style, Margin = new Thickness(0, 18, 0, 0), Height = 48 };
        close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
        panel.Children.Add(close);
        _documentModalLayer.Child = new Border
        {
            Background = FindResource("Bg") as Brush,
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(24),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel }
        };
    }
}

internal static class TabletFeatureIntegration
{
    internal static bool Register()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
        return true;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;
        var quick = FindQuickGrid(window);
        if (quick == null || quick.Children.OfType<Button>().Any(b => (b.Content?.ToString() ?? "").Contains("ECONOMIA"))) return;

        AddButton(quick, "💰 ECONOMIA", (_, args) => { args.Handled = true; window.ShowEconomyModal(); });
        AddButton(quick, "🚛 GARAGEM", (_, args) => { args.Handled = true; window.ShowGarageTabletModal(); });
        AddButton(quick, "🧾 NOTA FISCAL", (_, args) => { args.Handled = true; window.ShowRealisticInvoiceModal(); });
    }

    private static void AddButton(UniformGrid grid, string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Style = grid.Children.OfType<Button>().FirstOrDefault()?.Style, Margin = new Thickness(3), Padding = new Thickness(12, 11, 12, 11), FontSize = 12, FontWeight = FontWeights.Bold, Cursor = System.Windows.Input.Cursors.Hand };
        button.Click += click;
        grid.Children.Add(button);
    }

    private static UniformGrid? FindQuickGrid(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UniformGrid grid && grid.Columns == 2 && grid.Children.OfType<Button>().Any(b => (b.Content?.ToString() ?? "").Contains("ABASTECIMENTO"))) return grid;
            var found = FindQuickGrid(child);
            if (found != null) return found;
        }
        return null;
    }
}