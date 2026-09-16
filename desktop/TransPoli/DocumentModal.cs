using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private Grid? _documentModalHost;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(InterceptOperationalButton), true);
    }

    private static void InterceptOperationalButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button || button.Tag?.ToString() == "modal-action") return;
        var text = button.Content?.ToString()?.ToUpperInvariant() ?? string.Empty;
        string? kind = text.Contains("DOCUMENT") || text.Contains("NOTA") ? "document" :
                       text.Contains("PARADA") ? "stop" :
                       text.Contains("OCORR") || text.Contains("AVARIA") ? "occurrence" :
                       text.Contains("ABAST") || text.Contains("COMBUST") ? "fuel" :
                       text.Contains("RESUM") ? "summary" : null;
        if (kind is null) return;
        if (Window.GetWindow(button) is MainWindow window)
        {
            e.Handled = true;
            window.ShowOperationalModal(kind);
        }
    }

    private void ShowOperationalModal(string kind)
    {
        if (_documentModalHost != null || Content is not UIElement original) return;
        var host = new Grid();
        Content = host;
        host.Children.Add(original);
        _documentModalHost = host;

        var dim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)),
            Padding = new Thickness(40, 55, 40, 55)
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
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = ModalTitle(kind), FontSize = 23, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
        var close = new Button { Content = "✕", Tag = "modal-action", Style = FindResource("TabletButton") as Style, Width = 48, Height = 44 };
        close.Click += (_, _) => CloseOperationalModal();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 16, 0, 0), Content = BuildModalContent(kind) };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
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
        _ => ModalText("Tela indisponível.")
    };

    private UIElement BuildDocumentsModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "CARGA ATIVA", Style = FindResource("Label") as Style });
        panel.Children.Add(ModalText(TripCargoText?.Text ?? "Nenhuma carga ativa", 20, true));
        panel.Children.Add(ModalText(TripRouteText?.Text ?? "Rota não disponível", 12));
        var box = new Border { Background = FindResource("Panel2") as Brush, CornerRadius = new CornerRadius(16), Padding = new Thickness(16), Margin = new Thickness(0, 12, 0, 0) };
        var inside = new StackPanel();
        inside.Children.Add(ModalText("NOTA FISCAL / DOCUMENTO DA CARGA", 12, true));
        inside.Children.Add(ModalText($"Carga: {TripCargoText?.Text ?? "Não informada"}\nRota: {TripRouteText?.Text ?? "Não informada"}\nPeso: {CargoMassText?.Text ?? "Não informado"}", 13));
        var latest = _documents.OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
        inside.Children.Add(ModalText(latest is null ? "Status: Aguardando nota" : $"Status: {latest.Status} • Ref.: {latest.Reference}", 12));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var reference = new TextBox { Text = latest?.Reference ?? "", FontSize = 14, Padding = new Thickness(10), Background = FindResource("Bg") as Brush, Foreground = FindResource("Text") as Brush, MinWidth = 220 };
        row.Children.Add(reference);
        var stamp = ModalButton("🟠 CARIMBAR NOTA");
        stamp.Margin = new Thickness(10, 0, 0, 0);
        stamp.Click += (_, _) =>
        {
            var value = reference.Text.Trim();
            if (string.IsNullOrWhiteSpace(value)) value = $"NF-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (latest is null) _documents.Add(new DocumentRecord { Id = Guid.NewGuid().ToString("N"), Status = "Carimbado", Reference = value, RecordedAtUtc = DateTime.UtcNow });
            else { latest.Status = "Carimbado"; latest.Reference = value; latest.RecordedAtUtc = DateTime.UtcNow; }
            SaveOperations(); UpdateOpsCounters(); StatusText.Text = "TransPoli • nota fiscal/documento carimbado"; CloseOperationalModal();
        };
        row.Children.Add(stamp);
        inside.Children.Add(row);
        box.Child = inside;
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { Text = "HISTÓRICO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _documents.OrderByDescending(x => x.RecordedAtUtc).Take(20)) panel.Children.Add(ModalText($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Status}\n{x.Reference}", 11));
        if (!_documents.Any()) panel.Children.Add(ModalText("Nenhum documento registrado ainda."));
        return panel;
    }

    private UIElement BuildStopsModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO DA PARADA", Style = FindResource("Label") as Style });
        var type = new ComboBox { ItemsSource = new[] { "Descanso", "Abastecimento", "Refeição", "Manutenção", "Carga/Descarga", "Documentação", "Trânsito", "Outro" }, SelectedIndex = 0, Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(type);
        panel.Children.Add(new TextBlock { Text = "OBSERVAÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var note = new TextBox { AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(note);
        var save = ModalButton("✓ REGISTRAR PARADA");
        save.Click += (_, _) => { var selected = type.SelectedItem?.ToString() ?? "Outro"; _stops.Add(new StopRecord { Id = Guid.NewGuid().ToString("N"), Type = selected, Note = note.Text.Trim(), StartedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer }); SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • parada registrada • {selected}"; CloseOperationalModal(); };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "ÚLTIMAS PARADAS", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _stops.OrderByDescending(x => x.StartedAtUtc).Take(15)) panel.Children.Add(ModalText($"{x.StartedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Type}\n{x.Note}", 11));
        return panel;
    }

    private UIElement BuildOccurrenceModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO", Style = FindResource("Label") as Style });
        var type = new ComboBox { ItemsSource = new[] { "Acidente", "Avaria", "Problema mecânico", "Problema com carga", "Atraso", "Observação" }, SelectedIndex = 0, Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(type);
        panel.Children.Add(new TextBlock { Text = "DESCRIÇÃO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var details = new TextBox { AcceptsReturn = true, Height = 120, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        panel.Children.Add(details);
        var save = ModalButton("✓ REGISTRAR OCORRÊNCIA");
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(details.Text)) return; var selected = type.SelectedItem?.ToString() ?? "Observação"; _occurrences.Add(new OccurrenceRecord { Id = Guid.NewGuid().ToString("N"), Type = selected, Details = details.Text.Trim(), RecordedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer }); SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • ocorrência registrada • {selected}"; CloseOperationalModal(); };
        panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = "ÚLTIMAS OCORRÊNCIAS", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _occurrences.OrderByDescending(x => x.RecordedAtUtc).Take(15)) panel.Children.Add(ModalText($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Type}\n{x.Details}", 11));
        return panel;
    }

    private UIElement BuildFuelModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalText("A telemetria detecta abastecimento automaticamente quando o veículo está parado."));
        panel.Children.Add(new TextBlock { Text = "HISTÓRICO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 18, 0, 8) });
        foreach (var x in _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(20)) panel.Children.Add(ModalText($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Liters:0.0} L • {x.Station}\n{x.Location} • Odômetro {x.OdometerKm:0.0} km", 11));
        if (!_refuelings.Any()) panel.Children.Add(ModalText("Nenhum abastecimento registrado ainda."));
        return panel;
    }

    private UIElement BuildSummaryModal()
    {
        var distance = _tripActive ? Math.Max(0, _lastOdometer - _tripStartOdometer) : 0;
        var fuelUsed = _tripActive ? Math.Max(0, _tripStartFuel - (_lastFuelLiters ?? _tripStartFuel)) : 0;
        return ModalText($"Viagem ativa: {(_tripActive ? "SIM" : "NÃO")}\n\nDistância registrada: {distance:0.0} km\nCombustível consumido: {fuelUsed:0.0} L\nAbastecimentos: {_refuelings.Count}\nParadas: {_stops.Count}\nOcorrências: {_occurrences.Count}\nDocumentos: {_documents.Count}\n\nÚltimo odômetro: {_lastOdometer:0.0} km", 15);
    }

    private Button ModalButton(string text) => new Button { Content = text, Tag = "modal-action", Style = FindResource("TabletButton") as Style, Padding = new Thickness(12, 10, 12, 10) };

    private TextBlock ModalText(string text, double size = 12, bool bold = false) => new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };

    private void CloseOperationalModal()
    {
        if (_documentModalHost == null) return;
        var host = _documentModalHost;
        if (host.Children.Count > 1) host.Children.RemoveAt(1);
        _documentModalHost = null;
    }
}
