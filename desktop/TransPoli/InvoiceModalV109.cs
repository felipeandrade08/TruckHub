using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TransPoli;

public partial class MainWindow
{
    internal async void ShowRealisticInvoiceModal()
    {
        EnsureEconomyModalHost(); if (_documentModalLayer == null) return; _documentModalKind = "invoice-v109"; _documentModalLayer.Visibility = Visibility.Visible;
        _documentModalLayer.Child = new Border { Background = Brushes.White, Padding = new Thickness(30), CornerRadius = new CornerRadius(20), Child = new TextBlock { Text = "GERANDO DOCUMENTO...", Foreground = Brushes.Black, FontSize = 18 } };
        try
        {
            var token = SecureTokenStore.Read(); if (string.IsNullOrWhiteSpace(token)) return; using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips"); request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}"); using var response = await _http.SendAsync(request); if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); JsonElement? selected = null; foreach (var trip in doc.RootElement.GetProperty("trips").EnumerateArray()) { selected = trip; break; }
            _documentModalLayer.Child = BuildRealisticInvoice(selected);
        }
        catch { _documentModalLayer.Child = new Border { Background = Brushes.White, Padding = new Thickness(30), CornerRadius = new CornerRadius(20), Child = new TextBlock { Text = "Não foi possível gerar a nota agora.", Foreground = Brushes.Black, FontSize = 16 } }; }
    }

    private UIElement BuildRealisticInvoice(JsonElement? trip)
    {
        var cargo = trip?.GetProperty("cargo").GetString() ?? _invoiceTelemetry?.Cargo ?? "Carga não informada";
        var origin = trip?.GetProperty("origin").GetString() ?? _invoiceTelemetry?.SourceCity ?? "Origem";
        var destination = trip?.GetProperty("destination").GetString() ?? _invoiceTelemetry?.DestinationCity ?? "Destino";
        var distance = trip?.GetProperty("distance_km").GetDecimal() ?? 0;
        var value = trip?.GetProperty("cargo_value_brl").ValueKind == JsonValueKind.Number ? trip.Value.GetProperty("cargo_value_brl").GetDecimal() : (_invoiceTelemetry?.CargoValueBrl ?? 0);
        var nf = $"TP-NF-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(100000,999999)}";
        var paper = new Border { Background = Brushes.White, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(28) };
        var root = new StackPanel(); var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal }; brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/TransPoli;component/Assets/transpoli-logo.png")), Width = 70, Height = 70, Margin = new Thickness(0, 0, 14, 0) });
        var brandText = new StackPanel(); brandText.Children.Add(new TextBlock { Text = "TRANSPOLI", FontSize = 25, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Black }); brandText.Children.Add(new TextBlock { Text = "DOCUMENTO FISCAL INTERNO • SIMULAÇÃO", FontSize = 9, Foreground = Brushes.Gray }); brand.Children.Add(brandText); header.Children.Add(brand);
        var nfBox = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(2), Padding = new Thickness(12), Child = new StackPanel { Children = { new TextBlock { Text = "DOCUMENTO", FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center }, new TextBlock { Text = nf, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center } } } }; Grid.SetColumn(nfBox, 1); header.Children.Add(nfBox); root.Children.Add(header);
        root.Children.Add(new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(0, 1, 0, 1), Margin = new Thickness(0, 18, 0, 12), Padding = new Thickness(0, 8, 0, 8), Child = new TextBlock { Text = $"EMISSÃO: {DateTime.Now:dd/MM/yyyy HH:mm:ss}    SÉRIE: TP-01", Foreground = Brushes.Black, FontSize = 10 } });
        root.Children.Add(InvoiceSection("EMITENTE", "TransPoli • TruckHub\nDocumento interno do simulador de transporte"));
        root.Children.Add(InvoiceSection("MOTORISTA / VEÍCULO", $"Motorista: {Environment.UserName}\nVeículo: {_invoiceTelemetry?.TruckBrand} {_invoiceTelemetry?.TruckModel}\nPlaca: {_invoiceTelemetry?.LicensePlate ?? "Não informada"}"));
        root.Children.Add(InvoiceSection("OPERAÇÃO", $"Carga: {cargo}\nOrigem: {origin}\nDestino: {destination}\nPeso: {_invoiceTelemetry?.CargoMassKg ?? 0:0} kg\nDistância: {distance:0.0} km"));
        root.Children.Add(InvoiceSection("VALORES", $"Valor da carga: {FormatInvoiceBrl(value)}\nFrete calculado pelo TruckHub: {FormatInvoiceBrl(value)}"));
        root.Children.Add(new TextBlock { Text = "Documento interno fictício para simulação e conferência no TruckHub. Não é NF-e oficial e não possui validade fiscal ou jurídica.", FontSize = 9, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 0) });
        var close = new Button { Content = "FECHAR", Tag = "modal-action", Style = FindResource("TabletButton") as Style, Width = 130, Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); }; root.Children.Add(close);
        paper.Child = root; return paper;
    }

    private static UIElement InvoiceSection(string title, string value) { var p = new StackPanel { Margin = new Thickness(0, 7, 0, 7) }; p.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray }); p.Children.Add(new TextBlock { Text = value, FontSize = 11, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) }); return p; }
    private static string FormatInvoiceBrl(decimal value) => value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"));
}
