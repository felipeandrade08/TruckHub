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
    private void EnsureEconomyModalHost()
    {
        if (_documentModalHost != null) return;
        if (Content is not UIElement original) return;
        _documentModalOriginalContent = original;
        var host = new Grid(); Content = host; host.Children.Add(original); _documentModalHost = host;
        _documentModalLayer = new Border { Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)), Padding = new Thickness(55, 65, 55, 65), Visibility = Visibility.Collapsed };
        host.Children.Add(_documentModalLayer);
    }

    internal async void ShowEconomyModal()
    {
        EnsureEconomyModalHost();
        if (_documentModalLayer == null) return;
        _documentModalKind = "economy";
        _documentModalLayer.Visibility = Visibility.Visible;
        _documentModalLayer.Child = BuildEconomyLoading();
        try
        {
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token))
            {
                _documentModalLayer.Child = BuildEconomyError("Sessão do motorista não encontrada. Ative o computador novamente.");
                return;
            }
            _documentModalLayer.Child = BuildEconomyContent(await LoadEconomyDataAsync(token));
        }
        catch
        {
            _documentModalLayer.Child = BuildEconomyError("Não foi possível carregar o banco do motorista agora.");
        }
    }

    private async Task<EconomyViewData> LoadEconomyDataAsync(string token)
    {
        var result = new EconomyViewData();
        using var economy = await SendEconomyRequestAsync(HttpMethod.Get, "/me/economy", token, null);
        if (!economy.IsSuccessStatusCode) throw new InvalidOperationException();
        using var economyDoc = JsonDocument.Parse(await economy.Content.ReadAsStringAsync());
        var root = economyDoc.RootElement;
        result.Balance = root.GetProperty("account").GetProperty("balanceBrl").GetDecimal();
        if (root.TryGetProperty("loan", out var loan) && loan.ValueKind == JsonValueKind.Object)
        {
            result.LoanPrincipal = loan.GetProperty("principal_brl").GetDecimal();
            result.LoanRemaining = loan.GetProperty("remaining_brl").GetDecimal();
            result.LoanPct = loan.GetProperty("repayment_pct").GetDecimal();
        }
        using var rates = await SendEconomyRequestAsync(HttpMethod.Get, "/me/economy/rates", token, null);
        if (rates.IsSuccessStatusCode)
        {
            using var ratesDoc = JsonDocument.Parse(await rates.Content.ReadAsStringAsync());
            var settings = ratesDoc.RootElement.GetProperty("settings");
            result.FuelPrice = settings.GetProperty("fuelPriceBrl").GetDecimal();
            result.Margin = settings.GetProperty("minimumMarginPct").GetDecimal();
            foreach (var row in ratesDoc.RootElement.GetProperty("rates").EnumerateArray())
            {
                var displayName = row.GetProperty("display_name").GetString() ?? "Carga";
                var rate = row.GetProperty("rate_brl_km").GetDecimal();
                result.Rates.Add(displayName + ": " + FormatBrlEconomy(rate) + "/km");
            }
        }
        using var trips = await SendEconomyRequestAsync(HttpMethod.Get, "/me/trips", token, null);
        if (trips.IsSuccessStatusCode)
        {
            using var tripsDoc = JsonDocument.Parse(await trips.Content.ReadAsStringAsync());
            foreach (var trip in tripsDoc.RootElement.GetProperty("trips").EnumerateArray())
            {
                if (!string.Equals(trip.GetProperty("status").GetString(), "active", StringComparison.OrdinalIgnoreCase)) continue;
                result.ActiveTripId = trip.GetProperty("id").GetString();
                result.ActiveCargo = trip.GetProperty("cargo").GetString() ?? "Carga";
                break;
            }
        }
        if (!string.IsNullOrWhiteSpace(result.ActiveTripId))
        {
            using var preview = await SendEconomyRequestAsync(HttpMethod.Get, $"/me/trips/{result.ActiveTripId}/economy-preview", token, null);
            if (preview.IsSuccessStatusCode)
            {
                using var previewDoc = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
                var p = previewDoc.RootElement.GetProperty("preview");
                result.PreviewRevenue = p.GetProperty("projectedRevenue").GetDecimal();
                result.PreviewFuel = p.GetProperty("fuelCost").GetDecimal();
                result.PreviewDistance = p.GetProperty("distanceKm").GetDecimal();
            }
        }
        return result;
    }

    private async Task<HttpResponseMessage> SendEconomyRequestAsync(HttpMethod method, string path, string token, object? body)
    {
        var request = new HttpRequestMessage(method, $"{ApiBaseUrl}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await _http.SendAsync(request);
    }

    private UIElement BuildEconomyLoading()
    {
        var text = new TextBlock { Text = "💰 CARREGANDO BANCO DO MOTORISTA...", Foreground = FindResource("Text") as Brush, FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return new Border { Background = FindResource("Bg") as Brush, CornerRadius = new CornerRadius(24), Padding = new Thickness(30), Child = text };
    }

    private UIElement BuildEconomyError(string message)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "💰 BANCO DO MOTORISTA", Foreground = FindResource("Text") as Brush, FontSize = 22, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = message, Foreground = FindResource("Muted") as Brush, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) });
        return new Border { Background = FindResource("Bg") as Brush, CornerRadius = new CornerRadius(24), Padding = new Thickness(30), Child = stack };
    }

    private UIElement BuildEconomyContent(EconomyViewData data)
    {
        var card = new Border { Background = FindResource("Bg") as Brush, BorderBrush = FindResource("Panel2") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Padding = new Thickness(24) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "💰 BANCO DO MOTORISTA", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
        var close = new Button { Content = "✕", Tag = "modal-action", Style = FindResource("TabletButton") as Style, Width = 48, Height = 44 };
        close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
        Grid.SetColumn(close, 1); header.Children.Add(close); root.Children.Add(header);
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        panel.Children.Add(ModalLine($"SALDO DISPONÍVEL\n{FormatBrlEconomy(data.Balance)}", 22));
        panel.Children.Add(ModalLine($"⛽ Diesel configurado: {FormatBrlEconomy(data.FuelPrice)}/L\n🛡 Margem mínima de segurança: {data.Margin:0.##}%", 13));
        if (!string.IsNullOrWhiteSpace(data.ActiveTripId)) panel.Children.Add(ModalLine($"VIAGEM ATIVA: {data.ActiveCargo}\nDistância registrada: {data.PreviewDistance:0.0} km\nReceita projetada: {FormatBrlEconomy(data.PreviewRevenue)}\nCombustível estimado: -{FormatBrlEconomy(data.PreviewFuel)}", 14)); else panel.Children.Add(ModalLine("Nenhuma viagem ativa no momento.", 13));
        panel.Children.Add(new TextBlock { Text = "TARIFAS POR CARGA", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 8) });
        foreach (var rate in data.Rates) panel.Children.Add(ModalLine(rate, 12));
        panel.Children.Add(new TextBlock { Text = "EMPRÉSTIMO INICIAL", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 8) });
        if (data.LoanRemaining > 0) panel.Children.Add(ModalLine($"Empréstimo: {FormatBrlEconomy(data.LoanPrincipal)}\nSaldo devedor: {FormatBrlEconomy(data.LoanRemaining)}\nDesconto automático: {data.LoanPct:0.##}% da receita líquida", 13));
        else
        {
            var loan5 = ModalButton("💳 SOLICITAR R$ 5.000"); loan5.Click += async (_, e) => { e.Handled = true; await RequestLoanAsync(5000); };
            var loan10 = ModalButton("💳 SOLICITAR R$ 10.000"); loan10.Click += async (_, e) => { e.Handled = true; await RequestLoanAsync(10000); };
            panel.Children.Add(loan5); panel.Children.Add(loan10);
        }
        var refresh = ModalButton("↻ ATUALIZAR BANCO"); refresh.Click += (_, e) => { e.Handled = true; ShowEconomyModal(); }; panel.Children.Add(refresh);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll); card.Child = root; return card;
    }

    private async Task RequestLoanAsync(decimal amount)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        using var response = await SendEconomyRequestAsync(HttpMethod.Post, "/me/economy/loan", token, new { principalBrl = amount, repaymentPct = 20 });
        StatusText.Text = response.IsSuccessStatusCode ? $"TransPoli • empréstimo de {FormatBrlEconomy(amount)} liberado" : "TransPoli • não foi possível liberar o empréstimo";
        ShowEconomyModal();
    }

    private static string FormatBrlEconomy(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));

    private sealed class EconomyViewData
    {
        public decimal Balance { get; set; }
        public decimal FuelPrice { get; set; }
        public decimal Margin { get; set; }
        public decimal LoanPrincipal { get; set; }
        public decimal LoanRemaining { get; set; }
        public decimal LoanPct { get; set; }
        public string? ActiveTripId { get; set; }
        public string ActiveCargo { get; set; } = "";
        public decimal PreviewRevenue { get; set; }
        public decimal PreviewFuel { get; set; }
        public decimal PreviewDistance { get; set; }
        public System.Collections.Generic.List<string> Rates { get; } = new();
    }
}
