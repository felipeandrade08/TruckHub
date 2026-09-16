using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private Grid? _documentModalHost;
    private TelemetrySnapshot? _pendingRefuelTelemetry;
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
                        kind.Contains("RESUM") ? "summary" : null;
        if (modal is null) return;

        var window = Window.GetWindow(button) as MainWindow;
        if (window is null) return;
        e.Handled = true;
        window.ShowOperationalModal(modal);
    }

    private void ShowOperationalModal(string kind)
    {
        if (_documentModalHost != null) return;
        if (Content is not UIElement original) return;

        var host = new Grid();
        Content = host;
        host.Children.Add(original);
        _documentModalHost = host;

        var dim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)),
            Padding = new Thickness(70, 100, 70, 100)
        };
        var card = new Border
        {
            Background = FindResource("Bg") as Brush,
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1, 1, 1, 1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(24, 24, 24, 24)
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

    private string ModalTitle(string kind) => kind switch
    {
        "document" => "📄 DOCUMENTOS E NOTA DA CARGA",
        "stop" => "🛑 PARADAS",
        "occurrence" => "⚠ OCORRÊNCIAS",
        "fuel" => "⛽ ABASTECIMENTOS",
        "summary" => "📊 RESUMO OPERACIONAL",
        _ => "TRANS POLI"
    };

    private UIElement BuildModalContent(string kind) => kind switch
    {
        "document" => BuildDocumentsModal(),
        "stop" => BuildStopsModal(),
        "occurrence" => BuildOccurrenceModal(),
        "fuel" => BuildFuelModal(),
        "summary" => BuildSummaryModal(),
        _ => new TextBlock { Text = "Tela indisponível.", Foreground = FindResource("Text") as Brush }
    };

    private UIElement BuildDocumentsModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "CARGA ATIVA", Style = FindResource("Label") as Style });
        panel.Children.Add(new TextBlock { Text = TripCargoText?.Text ?? "Nenhuma carga ativa", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = TripRouteText?.Text ?? "Rota não disponível", FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 4, 0, 12), TextWrapping = TextWrapping.Wrap });

        var invoice = new Border { Background = FindResource("Panel2") as Brush, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 16, 16, 16) };
        var invoicePanel = new StackPanel();
        invoicePanel.Children.Add(new TextBlock { Text = "NOTA FISCAL / DOCUMENTO DA CARGA", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Orange") as Brush });
        invoicePanel.Children.Add(new TextBlock { Text = $"Carga: {TripCargoText?.Text ?? "Não informada"}\nRota: {TripRouteText?.Text ?? "Não informada"}\nPeso: {CargoMassText?.Text ?? "Não informado"}", FontSize = 13, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 10, 0, 10), TextWrapping = TextWrapping.Wrap });

        var latest = _documents.OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
        var status = new TextBlock { Text = latest is null ? "Status: Aguardando nota" : $"Status: {latest.Status} • Ref.: {latest.Reference}", FontSize = 12, Foreground = FindResource("Yellow") as Brush, TextWrapping = TextWrapping.Wrap };
        invoicePanel.Children.Add(status);

        var reference = new TextBox { Text = latest?.Reference ?? "", FontSize = 14, Padding = new Thickness(10, 10, 10, 10), Background = FindResource("Bg") as Brush, Foreground = FindResource("Text") as Brush, MinWidth = 230 };
        invoicePanel.Children.Add(new TextBlock { Text = "NÚMERO / REFERÊNCIA DA NF", Style = FindResource("Label") as Style, Margin = new Thickness(0, 12, 0, 6) });
        invoicePanel.Children.Add(reference);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var stamp = new Button { Content = "🟠 CARIMBAR NOTA", Tag = "modal-action", Style = FindResource("TabletButton") as Style, Padding = new Thickness(12, 10, 12, 10) };
        stamp.Click += (_, _) =>
        {
            var value = reference.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                if (latest is null && string.IsNullOrWhiteSpace(TripCargoText?.Text))
                {
                    StatusText.Text = "TransPoli • nenhuma carga ativa para carimbar";
                    return;
                }
                value = $"NF-{DateTime.Now:yyyyMMdd-HHmmss}";
            }

            if (latest is null)
                _documents.Add(new DocumentRecord { Id = Guid.NewGuid().ToString("N"), Status = "Carimbado", Reference = value, RecordedAtUtc = DateTime.UtcNow });
            else
            {
                latest.Status = "Carimbado";
                latest.Reference = value;
                latest.RecordedAtUtc = DateTime.UtcNow;
            }
            SaveOperations();
            UpdateOpsCounters();
            StatusText.Text = "TransPoli • nota fiscal/documento carimbado";
            CloseOperationalModal();
        };
        buttons.Children.Add(stamp);
        var received = new Button { Content = "✓ MARCAR CONFERIDO", Tag = "modal-action", Style = FindResource("TabletButton") as Style, Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 10, 12, 10) };
        received.Click += (_, _) =>
        {
            var value = reference.Text.Trim();
            if (string.IsNullOrWhiteSpace(value)) value = $"NF-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (latest is null)
                _documents.Add(new DocumentRecord { Id = Guid.NewGuid().ToString("N"), Status = "Conferido", Reference = value, RecordedAtUtc = DateTime.UtcNow });
            else { latest.Status = "Conferido"; latest.Reference = value; latest.RecordedAtUtc = DateTime.UtcNow; }
            SaveOperations(); UpdateOpsCounters(); StatusText.Text = "TransPoli • documento conferido"; CloseOperationalModal();
        };
        buttons.Children.Add(received);
        invoicePanel.Children.Add(buttons);
        invoice.Child = invoicePanel;
        panel.Children.Add(invoice);

        panel.Children.Add(new TextBlock { Text = "HISTÓRICO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var item in _documents.OrderByDescending(x => x.RecordedAtUtc).Take(20))
            panel.Children.Add(ModalLine($"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {item.Status}\n{item.Reference}", 11));
        if (!_documents.Any()) panel.Children.Add(ModalLine("Nenhum documento registrado. A nota pode ser conferida e carimbada diretamente aqui.", 12));
        return panel;
    }

    private UIElement BuildStopsModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO DA PARADA", Style = FindResource("Label") as Style });
        var type = new ComboBox { ItemsSource = new[] { "Descanso", "Abastecimento", "Refeição", "Manutenção", "Carga/Descarga", "Documentação", "Trânsito", "Outro" }, SelectedIndex = 0, FontSize = 14, Padding = new Thickness(8, 8, 8, 8), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(type);
        panel.Children.Add(new TextBlock { Text = "OBSERVAÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var note = new TextBox { FontSize = 14, Padding = new Thickness(10, 10, 10, 10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush, AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(note);
        var save = ModalButton("✓ REGISTRAR PARADA");
        save.Click += (_, _) =>
        {
            var selected = type.SelectedItem?.ToString() ?? "Outro";
            _stops.Add(new StopRecord { Id = Guid.NewGuid().ToString("N"), Type = selected, Note = note.Text.Trim(), StartedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer });
            SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • parada registrada • {selected}"; CloseOperationalModal();
        };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "ÚLTIMAS PARADAS", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _stops.OrderByDescending(x => x.StartedAtUtc).Take(15)) panel.Children.Add(ModalLine($"{x.StartedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Type}\n{x.Note}", 11));
        return panel;
    }

    private UIElement BuildOccurrenceModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO", Style = FindResource("Label") as Style });
        var type = new ComboBox { ItemsSource = new[] { "Acidente", "Avaria", "Problema mecânico", "Problema com carga", "Atraso", "Observação" }, SelectedIndex = 0, FontSize = 14, Padding = new Thickness(8, 8, 8, 8), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(type);
        panel.Children.Add(new TextBlock { Text = "DESCRIÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var details = new TextBox { FontSize = 14, Padding = new Thickness(10, 10, 10, 10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush, AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(details);
        var save = ModalButton("✓ REGISTRAR OCORRÊNCIA");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(details.Text)) return;
            var selected = type.SelectedItem?.ToString() ?? "Observação";
            _occurrences.Add(new OccurrenceRecord { Id = Guid.NewGuid().ToString("N"), Type = selected, Details = details.Text.Trim(), RecordedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer });
            SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • ocorrência registrada • {selected}"; CloseOperationalModal();
        };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "ÚLTIMAS OCORRÊNCIAS", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _occurrences.OrderByDescending(x => x.RecordedAtUtc).Take(15)) panel.Children.Add(ModalLine($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Type}\n{x.Details}", 11));
        return panel;
    }

    private UIElement BuildFuelModal()
    {
        var panel = new StackPanel();
        if (_pendingRefuelTelemetry != null)
        {
            var data = _pendingRefuelTelemetry;
            var detected = new Border { Background = FindResource("Panel2") as Brush, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 16, 16, 16) };
            var detectedPanel = new StackPanel();
            detectedPanel.Children.Add(new TextBlock { Text = "⛽ ABASTECIMENTO DETECTADO AUTOMATICAMENTE", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = FindResource("Green") as Brush });
            detectedPanel.Children.Add(new TextBlock { Text = $"Quantidade detectada: {_pendingRefuelLiters:0.0} L\nOdômetro: {data.OdometerKm:0.0} km\nVeículo: {data.TruckBrand} {data.TruckModel}".Trim(), FontSize = 13, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap });
            var station = new TextBox { Text = "", FontSize = 14, Padding = new Thickness(10, 10, 10, 10), Background = FindResource("Bg") as Brush, Foreground = FindResource("Text") as Brush };
            var location = new TextBox { Text = data.DestinationCity ?? data.SourceCity ?? "", FontSize = 14, Padding = new Thickness(10, 10, 10, 10), Background = FindResource("Bg") as Brush, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 8, 0, 0) };
            detectedPanel.Children.Add(new TextBlock { Text = "POSTO", Style = FindResource("Label") as Style });
            detectedPanel.Children.Add(station);
            detectedPanel.Children.Add(new TextBlock { Text = "LOCALIZAÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 10, 0, 6) });
            detectedPanel.Children.Add(location);
            var saveDetected = ModalButton("✓ REGISTRAR ABASTECIMENTO");
            saveDetected.Click += (_, _) =>
            {
                var stationValue = station.Text.Trim();
                if (string.IsNullOrWhiteSpace(stationValue)) stationValue = "Posto não informado";
                var locationValue = location.Text.Trim();
                _refuelings.Add(new RefuelingRecord { Id = Guid.NewGuid().ToString("N"), RecordedAtUtc = DateTime.UtcNow, Station = stationValue, Location = locationValue, Liters = _pendingRefuelLiters, FuelBefore = _fuelBefore, FuelAfter = _fuelAfter, OdometerKm = data.OdometerKm, Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(), LicensePlate = data.LicensePlate ?? "" });
                SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • abastecimento registrado • {_pendingRefuelLiters:0.0} L • {stationValue}";
                _pendingRefuelTelemetry = null; _pendingRefuelLiters = 0;
                CloseOperationalModal();
            };
            detectedPanel.Children.Add(saveDetected);
            detected.Child = detectedPanel;
            panel.Children.Add(detected);
        }
        panel.Children.Add(new TextBlock { Text = "HISTÓRICO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(20)) panel.Children.Add(ModalLine($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Liters:0.0} L • {x.Station}\n{x.Location} • Odômetro {x.OdometerKm:0.0} km", 11));
        if (!_refuelings.Any()) panel.Children.Add(ModalLine("Nenhum abastecimento registrado ainda.", 12));
        return panel;
    }

    private UIElement BuildSummaryModal()
    {
        var distance = _tripActive ? Math.Max(0, _lastOdometer - _tripStartOdometer) : 0;
        var fuelUsed = _tripActive ? Math.Max(0, _tripStartFuel - (_lastFuelLiters ?? _tripStartFuel)) : 0;
        return new TextBlock
        {
            Text = $"Viagem ativa: {(_tripActive ? "SIM" : "NÃO")}\n\nDistância registrada: {distance:0.0} km\nCombustível consumido: {fuelUsed:0.0} L\nAbastecimentos: {_refuelings.Count}\nParadas: {_stops.Count}\nOcorrências: {_occurrences.Count}\nDocumentos: {_documents.Count}\n\nÚltimo odômetro: {_lastOdometer:0.0} km",
            FontSize = 15,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 4, 4, 4)
        };
    }

    private Button ModalButton(string text) => new Button
    {
        Content = text,
        Tag = "modal-action",
        Style = FindResource("TabletButton") as Style,
        Margin = new Thickness(0, 14, 0, 0),
        Padding = new Thickness(12, 10, 12, 10)
    };

    private static TextBlock ModalLine(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = (Brush)Application.Current.FindResource("Text"),
        Margin = new Thickness(0, 0, 0, 10),
        TextWrapping = TextWrapping.Wrap
    };

    private void CloseOperationalModal()
    {
        if (_documentModalHost == null) return;
        var host = _documentModalHost;
        if (host.Children.Count > 1) host.Children.RemoveAt(1);
        _documentModalHost = null;
    }
}
