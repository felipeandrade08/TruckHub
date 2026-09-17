using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal async void ShowCargoMarketModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("cargo-market", BuildModalLoading("CARREGANDO MERCADO..."));
        var panel = await BuildCargoMarketPanelAsync();
        ShowModalContent("cargo-market", BuildModalCard("📦 MERCADO DE CARGAS", panel,
            "Ofertas internas do TruckHub • tarifa por quilômetro"));
    }

    internal async void ShowTripCenterModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("trip-center", BuildModalLoading("CARREGANDO VIAGEM..."));
        _invoiceTelemetry = await LoadCurrentTelemetryAsync();
        ShowModalContent("trip-center", BuildModalCard("🚛 CENTRAL DE VIAGENS", BuildCargoModal(),
            "Acompanhamento da viagem e da carga atualmente vinculada"));
    }

    private async Task<UIElement> BuildCargoMarketPanelAsync()
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalPanel(new TextBlock
        {
            Text = "O Mercado de Cargas é separado da Central de Viagens. Aqui você escolhe e aceita uma oferta; a viagem fica na tela própria.",
            FontSize = 12,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap
        }));

        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Sessão do motorista não encontrada. Faça login/ativação novamente para carregar as ofertas.",
                FontSize = 12,
                Foreground = FindResource("Yellow") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
            panel.Children.Add(ModalButton("FECHAR"));
            return panel;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/cargo-market");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                panel.Children.Add(ModalPanel(new TextBlock
                {
                    Text = TryApiError(json, "Não foi possível carregar o Mercado de Cargas."),
                    FontSize = 12,
                    Foreground = FindResource("Yellow") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }));
                return panel;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var offers = root.TryGetProperty("offers", out var offersElement) && offersElement.ValueKind == JsonValueKind.Array
                ? offersElement
                : default;

            if (offers.ValueKind != JsonValueKind.Array || offers.GetArrayLength() == 0)
            {
                panel.Children.Add(ModalPanel(new TextBlock
                {
                    Text = "Nenhuma oferta disponível ainda. As cargas descobertas nas viagens entram automaticamente no mercado.",
                    FontSize = 12,
                    Foreground = FindResource("Muted") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }));
                return panel;
            }

            panel.Children.Add(ModalLabel($"OFERTAS DISPONÍVEIS • {offers.GetArrayLength()} CARGAS"));
            foreach (var offer in offers.EnumerateArray())
            {
                var id = GetString(offer, "id");
                var cargo = GetString(offer, "display_name") ?? "Carga geral";
                var rate = GetDecimal(offer, "rate_brl_km");
                var status = GetString(offer, "market_status")?.ToLowerInvariant() switch
                {
                    "high" => "ALTA",
                    "low" => "BAIXA",
                    _ => "NORMAL"
                };
                var discoveries = GetInt(offer, "discovered_count");

                var card = new StackPanel();
                card.Children.Add(new TextBlock
                {
                    Text = cargo,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Text") as Brush,
                    TextWrapping = TextWrapping.Wrap
                });
                card.Children.Add(ModalValueRow("Tarifa", $"R$ {rate:0.00}/km"));
                card.Children.Add(ModalValueRow("Mercado", status));
                card.Children.Add(ModalValueRow("Descobertas", discoveries.ToString(CultureInfo.InvariantCulture)));

                var accept = ModalButton("✓ ACEITAR ESTA CARGA");
                accept.Tag = "cargo-market-action";
                accept.IsEnabled = !string.IsNullOrWhiteSpace(id);
                accept.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await AcceptCargoOfferAsync(id!, cargo);
                };
                card.Children.Add(accept);
                panel.Children.Add(ModalPanel(card));
            }
        }
        catch
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Erro de comunicação com o Mercado de Cargas. Tente atualizar novamente.",
                FontSize = 12,
                Foreground = FindResource("Yellow") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        return panel;
    }

    private async Task AcceptCargoOfferAsync(string offerId, string cargo)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusText.Text = "TruckHub • sessão não encontrada";
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new { offerId, cargo });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/cargo-market/contracts");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                StatusText.Text = "TruckHub • " + TryApiError(json, "Não foi possível aceitar a carga.");
                return;
            }

            StatusText.Text = $"TruckHub • carga aceita • {cargo}";
            ShowTripCenterModal();
        }
        catch
        {
            StatusText.Text = "TruckHub • erro de comunicação ao aceitar a carga";
        }
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static decimal GetDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}
