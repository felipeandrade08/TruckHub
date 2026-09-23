using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private const string RankingPeriodDefault = "30";
    private const string RankingMetricDefault = "km";
    private string _rankingPeriod = RankingPeriodDefault;
    private string _rankingMetric = RankingMetricDefault;

    private sealed record RankingDriver(
        int Position,
        string Name,
        double Km,
        double RevenueBrl,
        double RateBrlKm,
        int Trips,
        bool IsMe);

    private async void RankingButton_Click(object sender, RoutedEventArgs e)
    {
        ShowStandardModal(
            "driver-ranking",
            "RANKING DOS MOTORISTAS",
            BuildRankingLoading(),
            "Desempenho da frota • quilômetros • valor pago por km • receita • viagens");

        await LoadRankingAsync();
    }

    private UIElement BuildRankingLoading()
    {
        return new Grid
        {
            Children =
            {
                new TextBlock
                {
                    Text = "CENTRAL DE MOTORISTAS • CONSOLIDANDO DESEMPENHO...",
                    Foreground = FindResource("Text") as Brush,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
    }

    private async Task LoadRankingAsync()
    {
        try
        {
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token))
            {
                ShowStandardModal(
                    "driver-ranking",
                    "RANKING DOS MOTORISTAS",
                    ModalStatePanel("SESSÃO OFFLINE", "Ranking indisponível sem autenticação", "Os dados locais do motorista continuam preservados. Conecte sua sessão TransPoli para consultar o comparativo da frota.", "Yellow"),
                    "Desempenho da frota");
                return;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{ApiBaseUrl}/me/ranking?period={Uri.EscapeDataString(_rankingPeriod)}&metric={Uri.EscapeDataString(_rankingMetric)}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");

            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                ShowStandardModal(
                    "driver-ranking",
                    "RANKING DOS MOTORISTAS",
                    ModalStatePanel("SERVIÇO INDISPONÍVEL", "Ranking temporariamente indisponível", RankingApiMessage(json, "Não foi possível carregar o ranking agora."), "Yellow"),
                    "Desempenho da frota");
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var drivers = new List<RankingDriver>();

            if (root.TryGetProperty("drivers", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    drivers.Add(new RankingDriver(
                        RankingJsonInt(item, "position"),
                        RankingJsonString(item, "name", "Motorista"),
                        RankingJsonNumber(item, "km"),
                        RankingJsonNumber(item, "revenueBrl"),
                        RankingJsonNumber(item, "rateBrlKm"),
                        RankingJsonInt(item, "trips"),
                        item.TryGetProperty("id", out var id) &&
                        id.ValueKind == JsonValueKind.String &&
                        string.Equals(id.GetString(), CurrentUserId(), StringComparison.OrdinalIgnoreCase)));
                }
            }

            var me = root.TryGetProperty("me", out var mine) && mine.ValueKind == JsonValueKind.Object
                ? mine
                : (JsonElement?)null;

            ShowStandardModal(
                "driver-ranking",
                "RANKING DOS MOTORISTAS",
                BuildRankingPanel(drivers, me),
                "Desempenho da frota • dados reais das viagens finalizadas");
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DriverRanking.Load", ex);
            ShowStandardModal(
                "driver-ranking",
                "RANKING DOS MOTORISTAS",
                ModalStatePanel("COMUNICAÇÃO INDISPONÍVEL", "Ranking temporariamente offline", "A telemetria e as viagens locais continuam preservadas. Tente atualizar a central de motoristas mais tarde.", "Yellow"),
                "Desempenho da frota");
        }
    }

    private UIElement BuildRankingPanel(IReadOnlyList<RankingDriver> drivers, JsonElement? me)
    {
        var root = new StackPanel();
        root.Children.Add(ModalHero("CENTRAL DE MOTORISTAS", "Ranking operacional", "Comparativo das viagens finalizadas no TransPoli com quilômetros, tarifa, receita e quantidade de operações.", $"{drivers.Count} MOTORISTA(S)", "GoldBright"));
        root.Children.Add(ModalStatusStrip("✓ RANKING BASEADO EM VIAGENS FINALIZADAS • SEM DUPLICAR KM OU RECEITA", "Green"));

        var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        filters.Children.Add(RankingSectionLabel("PERÍODO"));
        foreach (var item in new[] { ("7", "7 DIAS"), ("30", "30 DIAS"), ("90", "90 DIAS"), ("month", "ESTE MÊS"), ("all", "GERAL") })
        {
            var button = RankingFilterButton(item.Item2, item.Item1, _rankingPeriod == item.Item1);
            button.Click += async (_, _) =>
            {
                _rankingPeriod = item.Item1;
                await LoadRankingAsync();
            };
            filters.Children.Add(button);
        }

        filters.Children.Add(RankingSectionLabel("ORDENAR POR"));
        foreach (var item in new[] { ("km", "KM"), ("rate", "R$/KM"), ("revenue", "RECEITA"), ("trips", "VIAGENS") })
        {
            var button = RankingFilterButton(item.Item2, item.Item1, _rankingMetric == item.Item1);
            button.Click += async (_, _) =>
            {
                _rankingMetric = item.Item1;
                await LoadRankingAsync();
            };
            filters.Children.Add(button);
        }
        root.Children.Add(filters);

        if (me.HasValue)
        {
            var mine = me.Value;
            var mineGrid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            mineGrid.ColumnDefinitions.Add(new ColumnDefinition());
            mineGrid.ColumnDefinitions.Add(new ColumnDefinition());
            mineGrid.ColumnDefinitions.Add(new ColumnDefinition());
            mineGrid.ColumnDefinitions.Add(new ColumnDefinition());

            AddRankingMini(mineGrid, 0, "SUA POSIÇÃO", $"#{RankingJsonInt(mine, "position")}");
            AddRankingMini(mineGrid, 1, "TOTAL KM", $"{RankingJsonNumber(mine, "km"):N1} km");
            AddRankingMini(mineGrid, 2, "VALOR / KM", $"R$ {RankingJsonNumber(mine, "rateBrlKm"):N2}");
            AddRankingMini(mineGrid, 3, "TOTAL PAGO", $"R$ {RankingJsonNumber(mine, "revenueBrl"):N2}");
            root.Children.Add(mineGrid);
        }

        if (drivers.Count == 0)
        {
            root.Children.Add(ModalStatePanel(
                "RANKING OPERACIONAL",
                "Ainda não há viagens suficientes",
                "Assim que os motoristas concluírem operações, quilômetros, tarifa, receita e quantidade de viagens aparecerão aqui automaticamente.",
                "Muted"));
            return root;
        }

        root.Children.Add(BuildRankingHeader());
        foreach (var driver in drivers)
            root.Children.Add(BuildRankingRow(driver));

        root.Children.Add(ModalLine(
            "O ranking usa somente viagens finalizadas registradas no TransPoli. KM vem da viagem consolidada e o valor pago vem do frete liquidado no banco, evitando uma segunda contagem de quilometragem ou receita.",
            10));

        return root;
    }

    private Border BuildRankingHeader()
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });

        AddGridText(grid, "POS.", 0, true);
        AddGridText(grid, "MOTORISTA", 1, true);
        AddGridText(grid, "KM", 2, true, HorizontalAlignment.Right);
        AddGridText(grid, "R$/KM", 3, true, HorizontalAlignment.Right);
        AddGridText(grid, "TOTAL PAGO", 4, true, HorizontalAlignment.Right);
        AddGridText(grid, "VIAGENS", 5, true, HorizontalAlignment.Right);

        return new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
            Child = grid
        };
    }

    private Border BuildRankingRow(RankingDriver driver)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });

        AddGridText(grid, $"#{driver.Position}", 0, false, HorizontalAlignment.Left, driver.Position <= 3 ? "GoldBright" : "Text");
        AddGridText(grid, driver.IsMe ? $"{driver.Name}  • VOCÊ" : driver.Name, 1, false, HorizontalAlignment.Left, driver.IsMe ? "GoldBright" : "Text");
        AddGridText(grid, $"{driver.Km:N1}", 2, false, HorizontalAlignment.Right);
        AddGridText(grid, $"R$ {driver.RateBrlKm:N2}", 3, false, HorizontalAlignment.Right, "GoldBright");
        AddGridText(grid, $"R$ {driver.RevenueBrl:N2}", 4, false, HorizontalAlignment.Right);
        AddGridText(grid, driver.Trips.ToString(), 5, false, HorizontalAlignment.Right);

        return new Border
        {
            Background = driver.IsMe
                ? new SolidColorBrush(Color.FromArgb(35, 216, 169, 46))
                : FindResource("Panel2") as Brush,
            BorderBrush = driver.IsMe ? FindResource("StrokeGold") as Brush : FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 11, 12, 11),
            Margin = new Thickness(0, 0, 0, 5),
            Child = grid
        };
    }

    private void AddGridText(Grid grid, string value, int column, bool header, HorizontalAlignment alignment = HorizontalAlignment.Left, string resource = "Text")
    {
        var text = new TextBlock
        {
            Text = value,
            Foreground = FindResource(header ? "Muted" : resource) as Brush,
            FontSize = header ? 10 : 13,
            FontWeight = header ? FontWeights.Bold : FontWeights.SemiBold,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0)
        };
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
    }

    private Border MiniRankingCard(string label, string value)
    {
        return new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(12),
            Margin = new Thickness(3),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, Foreground = FindResource("Muted") as Brush, FontSize = 10, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = value, Foreground = FindResource("Text") as Brush, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 4, 0, 0) }
                }
            }
        };
    }

    private void AddRankingMini(Grid grid, int column, string label, string value)
    {
        var card = MiniRankingCard(label, value);
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private TextBlock RankingSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = FindResource("Muted") as Brush,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 7, 0)
        };
    }

    private Button RankingFilterButton(string text, string value, bool active)
    {
        var button = new Button
        {
            Content = text,
            Tag = value,
            Height = 38,
            MinWidth = 58,
            Margin = new Thickness(2),
            Padding = new Thickness(9, 4, 9, 4),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = active ? FindResource("Bg") as Brush : FindResource("Text") as Brush,
            Background = active ? FindResource("Gold") as Brush : FindResource("Panel2") as Brush,
            BorderBrush = active ? FindResource("Gold") as Brush : FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        return button;
    }

    private static int RankingJsonInt(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var p) && p.TryGetInt32(out var value) ? value : 0;
    }

    private static double RankingJsonNumber(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var p) && p.TryGetDouble(out var value) ? value : 0d;
    }

    private static string RankingJsonString(JsonElement item, string name, string fallback)
    {
        return item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? (p.GetString() ?? fallback)
            : fallback;
    }

    private static string RankingApiMessage(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString() ?? fallback
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private string CurrentUserId()
    {
        // O endpoint já calcula "me"; a comparação visual é apenas um bônus.
        // Se a sessão não expuser o ID localmente, nenhuma linha é destacada.
        return "";
    }
}
