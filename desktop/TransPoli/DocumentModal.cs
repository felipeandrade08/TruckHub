using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private Grid? _documentModalHost;
    private TelemetrySnapshot? _pendingRefuelTelemetry;
    private TelemetrySnapshot? _invoiceTelemetry;
    private float _pendingRefuelLiters;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(InterceptOperationalButton), true);
    }

    private static void InterceptOperationalButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button || button.Tag?.ToString() == "modal-action") return;
        var text = button.Content?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return;
        var kind = text.ToUpperInvariant();
        string? modal = kind.Contains("DOCUMENT") || kind.Contains("NOTA") ? "document" :
                        kind.Contains("PARADA") ? "stop" :
                        kind.Contains("OCORR") || kind.Contains("AVARIA") ? "occurrence" :
                        kind.Contains("ABAST") || kind.Contains("COMBUST") ? "fuel" :
                        kind.Contains("RESUM") ? "summary" :
                        kind.Contains("VIAGEM") || kind.Contains("OPERAÇÃO") || kind.Contains("OPERACAO") || kind.Contains("CARGA") ? "cargo" : null;
        if (modal is null) return;
        var window = Window.GetWindow(button) as MainWindow;
        if (window is null) return;
        e.Handled = true;
        window.ShowOperationalModal(modal);
    }

    internal async void ShowOperationalModal(string kind)
    {
        if (_documentModalHost != null) return;
        if (Content is not UIElement original) return;
        if (kind is "document" or "cargo") _invoiceTelemetry = await LoadCurrentTelemetryAsync();

        var host = new Grid();
        Content = host;
        host.Children.Add(original);
        _documentModalHost = host;

        var dim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)),
            Padding = new Thickness(55, 65, 55, 65)
        };
        var card = new Border
        {
            Background = FindResource("Bg") as Brush,
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(24)
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = ModalTitle(kind),
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush
        });
        var close = new Button
        {
            Content = "✕",
            Tag = "modal-action",
            Style = FindResource("TabletButton") as Style,
            Width = 48,
            Height = 44
        };
        close.Click += (_, _) => CloseOperationalModal();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);
        var content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 16, 0, 0),
            Content = BuildModalContent(kind)
        };
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        card.Child = root;
        dim.Child = card;
        host.Children.Add(dim);
    }

    private async Task<TelemetrySnapshot?> LoadCurrentTelemetryAsync()
    {
        try
        {
            using var response = await _http.GetAsync("http://127.0.0.1:17877/telemetry");
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private string ModalTitle(string kind) => kind switch
    {
        "cargo" => "🚛 VIAGEM / OPERAÇÃO DA CARGA",
        "document" => "📄 DOCUMENTOS E NOTA DA CARGA",
        "stop" => "🛑 PARADAS",
        "occurrence" => "⚠ OCORRÊNCIAS",
        "fuel" => "⛽ ABASTECIMENTOS",
        "summary" => "📊 RESUMO OPERACIONAL",
        _ => "TRANS POLI"
    };

    private UIElement BuildModalContent(string kind) => kind switch
    {
        "cargo" => BuildCargoModal(),
        "document" => BuildDocumentsModal(),
        "stop" => BuildStopsModal(),
        "occurrence" => BuildOccurrenceModal(),
        "fuel" => BuildFuelModal(),
        "summary" => BuildSummaryModal(),
        _ => new TextBlock { Text = "Tela indisponível.", Foreground = FindResource("Text") as Brush }
    };

    private UIElement BuildCargoModal()
    {
        var data = _invoiceTelemetry;
        var panel = new StackPanel();
        panel.Children.Add(ModalCard("CARGA", data?.Cargo ?? TripCargoText?.Text ?? "Nenhuma carga ativa", "ROTA", BuildRouteForInvoice(data)));
        panel.Children.Add(ModalCard("STATUS", _tripActive ? "EM VIAGEM" : "VIAGEM NÃO INICIADA", "VELOCIDADE", $"{data?.SpeedKph ?? 0:0} km/h"));
        panel.Children.Add(ModalCard("ODÔMETRO", $"{data?.OdometerKm ?? _lastOdometer:0.0} km", "DISTÂNCIA PLANEJADA", $"{data?.PlannedDistanceKm ?? 0} km"));
        panel.Children.Add(new TextBlock { Text = "DADOS DA CARGA", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        panel.Children.Add(ModalLine($"Carga: {data?.Cargo ?? "Não identificada"}\nPeso: {data?.CargoMassKg ?? 0:0} kg\nValor: {FormatBrl(data?.CargoValueBrl)}\nOdômetro: {data?.OdometerKm ?? _lastOdometer:0.0} km", 13));
        var docs = ModalButton("📄 VISUALIZAR NOTA FISCAL");
        docs.Click += (_, _) => { CloseOperationalModal(); ShowOperationalModal("document"); };
        panel.Children.Add(docs);
        return panel;
    }

    private UIElement BuildDocumentsModal()
    {
        var data = _invoiceTelemetry;
        var cargo = data?.Cargo ?? TripCargoText?.Text ?? "Nenhuma carga ativa";
        var route = BuildRouteForInvoice(data);
        var key = CargoKey(cargo, route);
        var latest = _documents.Where(x => string.IsNullOrWhiteSpace(x.CargoKey) || x.CargoKey == key).OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
        var invoiceNumber = string.IsNullOrWhiteSpace(latest?.Reference) ? GenerateInvoiceNumber() : latest!.Reference;
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "CARGA ATIVA", Style = FindResource("Label") as Style });
        panel.Children.Add(new TextBlock { Text = cargo, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = route, FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(BuildInvoicePreview(data, cargo, route, invoiceNumber, latest?.Status == "Carimbado"));

        var stamp = ModalButton("🟠 CARIMBAR E LANÇAR NOTA");
        stamp.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(cargo) || cargo.Contains("Nenhuma", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "TransPoli • nenhuma carga ativa para registrar";
                return;
            }
            var existing = _documents.FirstOrDefault(x => x.CargoKey == key) ?? new DocumentRecord { Id = Guid.NewGuid().ToString("N"), CargoKey = key };
            if (!_documents.Contains(existing)) _documents.Add(existing);
            existing.Status = "Carimbado";
            existing.Reference = invoiceNumber;
            existing.RecordedAtUtc = DateTime.UtcNow;
            SaveOperations();
            UpdateOpsCounters();
            StatusText.Text = "TransPoli • nota fiscal carimbada e lançada";
            CloseOperationalModal();
            ShowOperationalModal("document");
        };
        panel.Children.Add(stamp);
        panel.Children.Add(new TextBlock { Text = "HISTÓRICO DA NOTA", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var item in _documents.Where(x => string.IsNullOrWhiteSpace(x.CargoKey) || x.CargoKey == key).OrderByDescending(x => x.RecordedAtUtc).Take(10))
            panel.Children.Add(ModalLine($"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {item.Status}\nNF: {item.Reference}", 11));
        if (!_documents.Any(x => string.IsNullOrWhiteSpace(x.CargoKey) || x.CargoKey == key))
            panel.Children.Add(ModalLine("Nenhuma nota lançada para esta carga. O número é gerado automaticamente.", 12));
        return panel;
    }

    private UIElement BuildInvoicePreview(TelemetrySnapshot? data, string cargo, string route, string invoiceNumber, bool stamped)
    {
        var paper = new Border { Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(24), Margin = new Thickness(0, 4, 0, 0) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var brand = new StackPanel();
        brand.Children.Add(new TextBlock { Text = "TRANSPOLI", FontSize = 27, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Black });
        brand.Children.Add(new TextBlock { Text = "DOCUMENTO FISCAL INTERNO • SIMULAÇÃO", FontSize = 9, Foreground = Brushes.DimGray });
        head.Children.Add(brand);
        var nfStack = new StackPanel();
        nfStack.Children.Add(new TextBlock { Text = "NOTA FISCAL", FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center });
        nfStack.Children.Add(new TextBlock { Text = invoiceNumber, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center });
        var nf = new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(2), Padding = new Thickness(10), Child = nfStack };
        Grid.SetColumn(nf, 1); head.Children.Add(nf); root.Children.Add(head);
        var meta = new TextBlock
        {
            Text = $"Data: {DateTime.Now:dd/MM/yyyy HH:mm}\nMotorista: {Environment.UserName}\nVeículo: {data?.TruckBrand} {data?.TruckModel}\nPlaca: {data?.LicensePlate ?? "Não informada"}",
            Foreground = Brushes.Black, FontSize = 11, Margin = new Thickness(0, 18, 0, 12)
        };
        Grid.SetRow(meta, 1); root.Children.Add(meta);
        var cargoPanel = new StackPanel();
        cargoPanel.Children.Add(DocumentInvoiceRow("PRODUTO / CARGA", cargo));
        cargoPanel.Children.Add(DocumentInvoiceRow("ORIGEM / DESTINO", route));
        cargoPanel.Children.Add(DocumentInvoiceRow("EMPRESAS", $"{data?.SourceCompany ?? "Origem fictícia"}  →  {data?.DestinationCompany ?? "Destino fictício"}"));
        cargoPanel.Children.Add(DocumentInvoiceRow("PESO", $"{data?.CargoMassKg ?? 0:0} kg"));
        cargoPanel.Children.Add(DocumentInvoiceRow("VALOR DA CARGA", FormatBrl(data?.CargoValueBrl)));
        cargoPanel.Children.Add(DocumentInvoiceRow("DISTÂNCIA PLANEJADA", $"{data?.PlannedDistanceKm ?? 0} km"));
        cargoPanel.Children.Add(DocumentInvoiceRow("ODÔMETRO ATUAL", $"{data?.OdometerKm ?? _lastOdometer:0.0} km"));
        Grid.SetRow(cargoPanel, 2); root.Children.Add(cargoPanel);
        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock { Text = "Documento interno fictício para conferência. Os dados de carga e telemetria são os atuais do simulador.", FontSize = 9, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Bottom });
        if (stamped)
        {
            var stamp = new Border { BorderBrush = Brushes.Red, BorderThickness = new Thickness(3), Padding = new Thickness(12, 7, 12, 7), RenderTransform = new RotateTransform(-7), Child = new TextBlock { Text = "CARIMBADO\nCONFERIDO", FontSize = 15, FontWeight = FontWeights.ExtraBold, Foreground = Brushes.Red, TextAlignment = TextAlignment.Center } };
            Grid.SetColumn(stamp, 1); footer.Children.Add(stamp);
        }
        Grid.SetRow(footer, 3); root.Children.Add(footer); paper.Child = root; return paper;
    }

    private UIElement BuildStopsModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO DA PARADA", Style = FindResource("Label") as Style });
        var type = new ComboBox { ItemsSource = new[] { "Descanso", "Abastecimento", "Refeição", "Manutenção", "Carga/Descarga", "Documentação", "Trânsito", "Outro" }, SelectedIndex = 0, FontSize = 14, Padding = new Thickness(8), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(type);
        panel.Children.Add(new TextBlock { Text = "OBSERVAÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var note = new TextBox { FontSize = 14, Padding = new Thickness(10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush, AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(note);
        var save = ModalButton("✓ REGISTRAR PARADA");
        save.Click += (_, _) => { var selected = type.SelectedItem?.ToString() ?? "Outro"; _stops.Add(new StopRecord { Id = Guid.NewGuid().ToString("N"), Type = selected, Note = note.Text.Trim(), StartedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer }); SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • parada registrada • {selected}"; CloseOperationalModal(); };
        panel.Children.Add(save); return panel;
    }

    private UIElement BuildOccurrenceModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO", Style = FindResource("Label") as Style });
        var type = new ComboBox { ItemsSource = new[] { "Acidente", "Avaria", "Problema mecânico", "Problema com carga", "Atraso", "Observação" }, SelectedIndex = 0, FontSize = 14, Padding = new Thickness(8), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(type);
        panel.Children.Add(new TextBlock { Text = "DESCRIÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var details = new TextBox { FontSize = 14, Padding = new Thickness(10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush, AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(details);
        var save = ModalButton("✓ REGISTRAR OCORRÊNCIA");
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(details.Text)) return; var selected = type.SelectedItem?.ToString() ?? "Observação"; _occurrences.Add(new OccurrenceRecord { Id = Guid.NewGuid().ToString("N"), Type = selected, Details = details.Text.Trim(), RecordedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer }); SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • ocorrência registrada • {selected}"; CloseOperationalModal(); };
        panel.Children.Add(save); return panel;
    }

    private UIElement BuildFuelModal()
    {
        var panel = new StackPanel();
        if (_pendingRefuelTelemetry is not null)
        {
            var data = _pendingRefuelTelemetry;
            var detected = new Border { Background = FindResource("Panel2") as Brush, CornerRadius = new CornerRadius(16), Padding = new Thickness(16) };
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = "⛽ ABASTECIMENTO DETECTADO AUTOMATICAMENTE", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = FindResource("Green") as Brush });
            p.Children.Add(new TextBlock { Text = $"Quantidade: {_pendingRefuelLiters:0.0} L\nOdômetro: {data.OdometerKm:0.0} km\nVeículo: {data.TruckBrand} {data.TruckModel}", FontSize = 13, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 8, 0, 12) });
            var station = new TextBox { FontSize = 14, Padding = new Thickness(10), Background = FindResource("Bg") as Brush, Foreground = FindResource("Text") as Brush };
            p.Children.Add(new TextBlock { Text = "POSTO", Style = FindResource("Label") as Style }); p.Children.Add(station);
            var location = new TextBox { Text = data.DestinationCity ?? data.SourceCity ?? "", FontSize = 14, Padding = new Thickness(10), Background = FindResource("Bg") as Brush, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 8, 0, 0) };
            p.Children.Add(new TextBlock { Text = "LOCALIZAÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 10, 0, 6) }); p.Children.Add(location);
            var save = ModalButton("✓ REGISTRAR ABASTECIMENTO");
            save.Click += (_, _) => { var stationValue = string.IsNullOrWhiteSpace(station.Text) ? "Posto não informado" : station.Text.Trim(); _refuelings.Add(new RefuelingRecord { Id = Guid.NewGuid().ToString("N"), RecordedAtUtc = DateTime.UtcNow, Station = stationValue, Location = location.Text.Trim(), Liters = _pendingRefuelLiters, FuelBefore = _fuelBefore, FuelAfter = _fuelAfter, OdometerKm = data.OdometerKm, Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(), LicensePlate = data.LicensePlate ?? "" }); SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • abastecimento registrado • {_pendingRefuelLiters:0.0} L"; _pendingRefuelTelemetry = null; _pendingRefuelLiters = 0; CloseOperationalModal(); };
            p.Children.Add(save); detected.Child = p; panel.Children.Add(detected);
        }
        return panel;
    }

    private UIElement BuildSummaryModal() => new TextBlock
    {
        Text = $"Viagem ativa: {(_tripActive ? "SIM" : "NÃO")}\n\nDistância registrada: {(_tripActive ? Math.Max(0, _lastOdometer - _tripStartOdometer) : 0):0.0} km\nCombustível consumido: {(_tripActive ? Math.Max(0, _tripStartFuel - (_lastFuelLiters ?? _tripStartFuel)) : 0):0.0} L\nAbastecimentos: {_refuelings.Count}\nParadas: {_stops.Count}\nOcorrências: {_occurrences.Count}\nDocumentos: {_documents.Count}\n\nÚltimo odômetro: {_lastOdometer:0.0} km",
        FontSize = 15,
        Foreground = FindResource("Text") as Brush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4)
    };

    private Border ModalCard(string label1, string value1, string label2, string value2)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var first = MiniCard(label1, value1);
        var second = MiniCard(label2, value2);
        Grid.SetColumn(second, 1);
        g.Children.Add(first); g.Children.Add(second);
        return new Border { Child = g, Margin = new Thickness(0, 0, 0, 6) };
    }

    private Border MiniCard(string label, string value)
    {
        var b = new Border { Background = FindResource("Panel2") as Brush, CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(4) };
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = label, FontSize = 9, Foreground = FindResource("Muted") as Brush });
        p.Children.Add(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        b.Child = p; return b;
    }

    private Button ModalButton(string text) => new() { Content = text, Tag = "modal-action", Style = FindResource("TabletButton") as Style, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12, 10, 12, 10) };
    private static TextBlock ModalLine(string text, double size) => new() { Text = text, FontSize = size, Foreground = (Brush)Application.Current.FindResource("Text"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    private static TextBlock DocumentInvoiceRow(string label, string value) => new() { Text = $"{label}\n{value}", FontSize = 11, Foreground = Brushes.Black, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap };
    private static string GenerateInvoiceNumber() => $"TP-NF-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
    private static string FormatBrl(ulong? value) => value.HasValue ? $"R$ {value.Value:N0}" : "R$ 0,00";
    private static string CargoKey(string cargo, string route) => $"{cargo}|{route}".Trim().ToUpperInvariant();
    private static string BuildRouteForInvoice(TelemetrySnapshot? data) => data == null ? "Rota não disponível" : $"{data.SourceCity ?? "Origem"} → {data.DestinationCity ?? "Destino"}";

    internal void CloseOperationalModal()
    {
        if (_documentModalHost == null) return;
        var host = _documentModalHost;
        if (host.Children.Count > 1) host.Children.RemoveAt(1);
        Content = host.Children.Count > 0 ? host.Children[0] : Content;
        _documentModalHost = null;
    }
}
