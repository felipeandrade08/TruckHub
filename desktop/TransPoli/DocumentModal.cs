using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private TelemetrySnapshot? _pendingRefuelTelemetry;
    private TelemetrySnapshot? _invoiceTelemetry;
    private float _pendingRefuelLiters;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent,
            new RoutedEventHandler(InterceptOperationalButton), true);
    }

    /// <summary>
    /// Roteia os botões do tablet para os modais.
    ///
    /// Correção importante: antes o roteamento era feito só pelo texto do
    /// botão, com <c>handledEventsToo</c>. Isso engolia o clique de botões
    /// que já tinham a própria ação — "🧾 NOTA FISCAL" caía na regra de
    /// "NOTA" e abria a tela errada, e o handler real nunca rodava. Agora
    /// qualquer botão com Tag definida é respeitado e sai daqui intacto.
    /// </summary>
    private static void InterceptOperationalButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button) return;

        // Botões com Tag têm dono: modais, atalhos diretos, janelas próprias.
        if (button.Tag != null) return;

        var text = button.Content?.ToString();
        if (string.IsNullOrWhiteSpace(text)) return;

        var window = Window.GetWindow(button) as MainWindow;
        if (window is null) return;

        var kind = text.ToUpperInvariant();
        if (kind.Contains("VIAGEM") && !kind.Contains("RESUM"))
        {
            e.Handled = true;
            window.ShowTripsOperationsCenter();
            return;
        }
        string? modal =
            kind.Contains("DOCUMENT") || kind.Contains("DOCS") ? "document" :
            kind.Contains("PARADA") ? "stop" :
            kind.Contains("OCORR") || kind.Contains("AVARIA") ? "occurrence" :
            kind.Contains("ABAST") || kind.Contains("COMBUST") ? "fuel" :
            kind.Contains("RESUM") ? "summary" :
            kind.Contains("CARGA") ? "cargo" : null;

        if (modal is null) return;
        e.Handled = true;
        window.ShowOperationalModal(modal);
    }

    private string? _documentTripFilterLocalId;
    private string? _documentTripFilterServerId;
    private bool _documentTripFilterArmed;

    internal void ShowTripDocuments(string localTripId, string? serverTripId = null)
    {
        _documentTripFilterLocalId = localTripId;
        _documentTripFilterServerId = serverTripId;
        _documentTripFilterArmed = true;
        ShowOperationalModal("document");
    }

    internal async void ShowOperationalModal(string kind)
    {
        if (kind == "document")
        {
            if (!_documentTripFilterArmed)
            {
                _documentTripFilterLocalId = null;
                _documentTripFilterServerId = null;
            }
            _documentTripFilterArmed = false;
        }
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent(kind, BuildModalLoading("TRANSPOLI • CARREGANDO MÓDULO OPERACIONAL..."));

        if (kind is "document" or "cargo")
            _invoiceTelemetry = LastTelemetry?.Connected == true ? LastTelemetry : await LoadCurrentTelemetryAsync();

        ShowModalContent(kind, BuildModalCard(ModalTitle(kind), BuildModalContent(kind)));
    }

    private async Task<TelemetrySnapshot?> LoadCurrentTelemetryAsync()
    {
        try
        {
            using var response = await _http.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(
                stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) { App.WriteUiCrashLog("DocumentModal.LoadTelemetry", ex); return null; }
    }

    private static string ModalTitle(string kind) => kind switch
    {
        "cargo" => "VIAGEM / OPERAÇÃO DA CARGA",
        "document" => "DOCUMENTOS E NOTA DA CARGA",
        "stop" => "PARADAS",
        "occurrence" => "OCORRÊNCIAS",
        "fuel" => "ABASTECIMENTOS",
        "summary" => "RESUMO OPERACIONAL",
        _ => "TRANSPOLI"
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

    /* ----------------------------- CARGA ----------------------------- */

    private UIElement BuildCargoModal()
    {
        var data = _invoiceTelemetry;
        var panel = new StackPanel();
        panel.Children.Add(ModalHero("OPERAÇÃO DA CARGA", "Viagem atual", "Carga, rota, integridade e documentação reunidas a partir da telemetria ETS2.", data?.Cargo ?? "SEM CARGA", data?.Connected == true ? "GoldBright" : "Yellow"));
        panel.Children.Add(ModalStatusStrip(_tripActive ? "✓ VIAGEM LIBERADA • DOCUMENTO VALIDADO PELO TRANSPOLI" : "● VIAGEM AGUARDANDO LIBERAÇÃO OPERACIONAL", _tripActive ? "Green" : "Yellow"));
        panel.Children.Add(ModalCard("CARGA", data?.Cargo ?? "Nenhuma carga ativa", "ROTA", BuildRouteForInvoice(data)));
        panel.Children.Add(ModalCard("STATUS", _tripActive ? "EM VIAGEM" : "VIAGEM NÃO INICIADA",
            "VELOCIDADE", $"{Math.Abs(data?.SpeedKph ?? 0):0} km/h"));
        panel.Children.Add(ModalCard("ODÔMETRO", $"{data?.OdometerKm ?? _lastOdometer:0.0} km",
            "DIST. PLANEJADA", $"{data?.PlannedDistanceKm ?? 0} km"));

        panel.Children.Add(ModalSectionTitle("DADOS DA CARGA", "TELEMETRIA ETS2"));
        var box = new StackPanel();
        box.Children.Add(ModalValueRow("Carga", data?.Cargo ?? "Não identificada"));
        box.Children.Add(ModalValueRow("Peso", $"{data?.CargoMassKg ?? 0:N0} kg"));
        box.Children.Add(ModalValueRow("Valor declarado", FormatBrl(data?.CargoValueBrl)));
        box.Children.Add(ModalValueRow("Avaria atual", $"{(data?.CargoDamage ?? 0) * 100:0.00}%",
            (data?.CargoDamage ?? 0) > 0.01f ? "Yellow" : "Green"));
        box.Children.Add(ModalValueRow("Empresas",
            $"{data?.SourceCompany ?? "—"} → {data?.DestinationCompany ?? "—"}"));
        panel.Children.Add(ModalPanel(box));

        var invoice = ModalButton("EMITIR NOTA FISCAL DA CARGA");
        invoice.Click += (_, e) => { e.Handled = true; ShowRealisticInvoiceModal(); };
        panel.Children.Add(invoice);

        var docs = ModalButton("DOCUMENTOS E CARIMBO");
        docs.Click += (_, e) => { e.Handled = true; ShowOperationalModal("document"); };
        panel.Children.Add(docs);

        return panel;
    }

    /* --------------------------- DOCUMENTOS -------------------------- */


    private void ConsolidateDuplicateDocuments()
    {
        var snapshot = _documents.Select(x => new
        {
            Record = x, x.Status, x.StampedAtUtc, x.TripId, x.CargoKey, x.Reference
        }).ToList();
        var groups = _documents
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) || !string.IsNullOrWhiteSpace(x.TripId))
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.Id)
                ? $"INV|{x.Id.Trim()}"
                : $"TRIP|{x.TripId.Trim()}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        var changed = false;
        foreach (var group in groups)
        {
            var keep = group.OrderByDescending(x => string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
                            .ThenByDescending(x => x.RecordedAtUtc).First();
            foreach (var duplicate in group.Where(x => !ReferenceEquals(x, keep)).ToList())
            {
                if (string.Equals(duplicate.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
                    keep.Status = "Carimbado";
                if (keep.StampedAtUtc is null && duplicate.StampedAtUtc is not null) keep.StampedAtUtc = duplicate.StampedAtUtc;
                if (string.IsNullOrWhiteSpace(keep.TripId)) keep.TripId = duplicate.TripId;
                if (string.IsNullOrWhiteSpace(keep.CargoKey)) keep.CargoKey = duplicate.CargoKey;
                if (string.IsNullOrWhiteSpace(keep.Reference)) keep.Reference = duplicate.Reference;
                _documents.Remove(duplicate);
                changed = true;
            }
        }
        if (!changed) return;
        if (!TrySaveOperations())
        {
            _documents.Clear();
            foreach (var item in snapshot)
            {
                item.Record.Status = item.Status;
                item.Record.StampedAtUtc = item.StampedAtUtc;
                item.Record.TripId = item.TripId;
                item.Record.CargoKey = item.CargoKey;
                item.Record.Reference = item.Reference;
                _documents.Add(item.Record);
            }
            return;
        }
        UpdateOpsCounters();
    }

    private UIElement BuildDocumentsModal()
    {
        ConsolidateDuplicateDocuments();
        var data = _invoiceTelemetry;
        var cargo = string.IsNullOrWhiteSpace(data?.Cargo) ? "Nenhuma carga ativa" : data!.Cargo!;
        var route = BuildRouteForInvoice(data);
        var key = CargoKey(cargo, route);
        DocumentRecord? latest = null;
        if (!string.IsNullOrWhiteSpace(_documentTripFilterLocalId))
            latest = _documents
                .Where(x => string.Equals(x.TripId, _documentTripFilterLocalId, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(_documentTripFilterServerId)
                        && string.Equals(x.TripId, _documentTripFilterServerId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();
        if (latest is null && string.IsNullOrWhiteSpace(_documentTripFilterLocalId) && !string.IsNullOrWhiteSpace(_operationInvoiceId))
            latest = _documents.FirstOrDefault(x => string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase));
        if (latest is null && string.IsNullOrWhiteSpace(_documentTripFilterLocalId) && !string.IsNullOrWhiteSpace(_operationTripId))
            latest = _documents
                .Where(x => string.Equals(x.TripId, _operationTripId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();
        if (latest is null && string.IsNullOrWhiteSpace(_documentTripFilterLocalId) && !string.IsNullOrWhiteSpace(_serverTripId))
            latest = _documents
                .Where(x => string.Equals(x.TripId, _serverTripId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();

        // Compatibilidade visual apenas para arquivos antigos sem identidade de operação.
        // Este fallback nunca autoriza nem carimba uma viagem moderna identificada.
        if (latest is null && string.IsNullOrWhiteSpace(_documentTripFilterLocalId) && string.IsNullOrWhiteSpace(_operationInvoiceId)
                           && string.IsNullOrWhiteSpace(_operationTripId)
                           && string.IsNullOrWhiteSpace(_serverTripId))
            latest = _documents
                .Where(x => string.IsNullOrWhiteSpace(x.CargoKey) || x.CargoKey == key)
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();

        var panel = new StackPanel();
        var scopedDocuments = _documents
            .Where(x => string.IsNullOrWhiteSpace(_documentTripFilterLocalId)
                || string.Equals(x.TripId, _documentTripFilterLocalId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(_documentTripFilterServerId)
                    && string.Equals(x.TripId, _documentTripFilterServerId, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var stampedCount = scopedDocuments.Count(x => string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase));
        var pendingCount = scopedDocuments.Count - stampedCount;
        var tripScoped = !string.IsNullOrWhiteSpace(_documentTripFilterLocalId);
        var documentTone = latest != null && string.Equals(latest.Status, "Carimbado", StringComparison.OrdinalIgnoreCase) ? "Green" : "Yellow";

        var executive = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        executive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.55, GridUnitType.Star) });
        executive.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var documentHero = ModalHero("CENTRAL DE DOCUMENTOS",
            tripScoped ? "Prontuário documental da viagem" : "Arquivo operacional TransPoli",
            tripScoped ? "DANFE, carimbo e arquivo isolados pela identidade desta viagem." : "Documentos operacionais organizados por viagem e estado de conformidade.",
            tripScoped ? "VIAGEM SELECIONADA" : $"{scopedDocuments.Count} DOCUMENTO(S)", "GoldBright");
        documentHero.Margin = new Thickness(0, 0, 8, 0);
        executive.Children.Add(documentHero);

        var compliance = new StackPanel();
        compliance.Children.Add(ModalSectionTitle("Conformidade"));
        compliance.Children.Add(ModalValueRow("Arquivados", scopedDocuments.Count.ToString()));
        compliance.Children.Add(ModalValueRow("Carimbados", stampedCount.ToString(), stampedCount > 0 ? "Green" : "Muted"));
        compliance.Children.Add(ModalValueRow("Pendentes", pendingCount.ToString(), pendingCount > 0 ? "Yellow" : "Green"));
        var compliancePanel = ModalPanel(compliance);
        compliancePanel.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(compliancePanel, 1);
        executive.Children.Add(compliancePanel);
        panel.Children.Add(executive);

        var metrics = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        metrics.Children.Add(DocumentExecutiveMetric("DOCUMENTOS", scopedDocuments.Count.ToString(), "GoldBright"));
        metrics.Children.Add(DocumentExecutiveMetric("CARIMBADOS", stampedCount.ToString(), stampedCount > 0 ? "Green" : "Text"));
        metrics.Children.Add(DocumentExecutiveMetric("PENDENTES", pendingCount.ToString(), pendingCount > 0 ? "Yellow" : "Green"));
        panel.Children.Add(metrics);

        panel.Children.Add(ModalStatusStrip(
            latest != null && string.Equals(latest.Status, "Carimbado", StringComparison.OrdinalIgnoreCase)
                ? "✓ DOCUMENTAÇÃO REGULAR • ÚLTIMA NOTA CARIMBADA"
                : latest is null
                    ? "● NENHUM DOCUMENTO DISPONÍVEL NESTE CONTEXTO"
                    : "● DOCUMENTAÇÃO PENDENTE • ABRA A NOTA PARA REVISAR O CARIMBO",
            latest is null ? "Muted" : documentTone));

        panel.Children.Add(ModalSectionTitle("DOCUMENTO EM FOCO", tripScoped ? "viagem selecionada" : "operação atual"));
        panel.Children.Add(new TextBlock { Text = tripScoped ? "NOTA ARQUIVADA DA VIAGEM" : "NOTA DA CARGA ATUAL", Style = FindResource("Label") as Style });
        panel.Children.Add(new TextBlock
        {
            Text = cargo,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = route,
            FontSize = 11,
            Foreground = FindResource("Muted") as Brush,
            Margin = new Thickness(0, 3, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        var open = ModalButton(string.IsNullOrWhiteSpace(_documentTripFilterLocalId)
            ? "ABRIR NOTA FISCAL DA VIAGEM ATUAL"
            : "ABRIR NOTA ARQUIVADA DESTA VIAGEM");
        open.IsEnabled = string.IsNullOrWhiteSpace(_documentTripFilterLocalId) || latest is not null;
        open.Click += (_, e) =>
        {
            e.Handled = true;
            if (!string.IsNullOrWhiteSpace(_documentTripFilterLocalId) && latest is not null)
                ShowStoredInvoiceDocument(latest);
            else
                ShowRealisticInvoiceModal();
        };
        panel.Children.Add(open);

        panel.Children.Add(ModalSectionTitle("TODAS AS NOTAS EMITIDAS", "ARQUIVO OPERACIONAL"));
        var history = _documents
            .Where(x => string.IsNullOrWhiteSpace(_documentTripFilterLocalId)
                || string.Equals(x.TripId, _documentTripFilterLocalId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(_documentTripFilterServerId)
                    && string.Equals(x.TripId, _documentTripFilterServerId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.RecordedAtUtc)
            .ToList();
        if (history.Count == 0)
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Nenhuma nota emitida ainda. Ao abrir uma nota fiscal de uma viagem, ela passa a aparecer neste arquivo como EMITIDA. Depois do carimbo, o estado muda para CARIMBADA.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }
        else
        {
            foreach (var item in history)
            {
                var stamped = string.Equals(item.Status, "Carimbado", StringComparison.OrdinalIgnoreCase);
                var statusBrush = FindResource(stamped ? "Green" : "Yellow") as Brush;

                var card = new Border
                {
                    Background = FindResource("Panel2") as Brush,
                    BorderBrush = stamped ? FindResource("Green") as Brush : FindResource("Stroke") as Brush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var details = new StackPanel();
                details.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(item.Reference) ? "NF sem número" : item.Reference,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Text") as Brush
                });
                details.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(item.Cargo) ? "NÃO INFORMADO" : item.Cargo,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FindResource("Text") as Brush,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                details.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(item.Route) ? "NÃO INFORMADO" : item.Route,
                    FontSize = 10,
                    Foreground = FindResource("Muted") as Brush,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                details.Children.Add(new TextBlock
                {
                    Text = $"{item.RecordedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}  •  {(string.IsNullOrWhiteSpace(item.Driver) ? "NÃO INFORMADO" : item.Driver)}  •  {(string.IsNullOrWhiteSpace(item.Truck) ? "NÃO INFORMADO" : item.Truck)}",
                    FontSize = 9,
                    Foreground = FindResource("Muted") as Brush,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(details, 0);
                grid.Children.Add(details);

                var badge = new Border
                {
                    Background = stamped ? new SolidColorBrush(Color.FromRgb(19, 55, 39)) : new SolidColorBrush(Color.FromRgb(62, 50, 16)),
                    BorderBrush = statusBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(9, 5, 9, 5),
                    VerticalAlignment = VerticalAlignment.Top
                };
                badge.Child = new TextBlock
                {
                    Text = stamped ? "✓ CARIMBADA" : "● EMITIDA",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = statusBrush
                };
                Grid.SetColumn(badge, 1);
                grid.Children.Add(badge);

                var openNote = ModalButton(stamped ? "ABRIR NOTA" : "ABRIR / CARIMBAR");
                openNote.Margin = new Thickness(0, 10, 0, 0);
                openNote.Click += (_, e) =>
                {
                    e.Handled = true;
                    ShowStoredInvoiceDocument(item);
                };
                details.Children.Add(openNote);

                card.Child = grid;
                panel.Children.Add(card);
            }
        }

        if (latest != null && !string.Equals(latest.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
        {
            // A Central de Documentos não possui um segundo caminho de carimbo.
            // Abrir a nota encaminha para o mesmo pipeline que registra o documento,
            // envia invoice_stamped e, quando houver gate pendente, autoriza a viagem.
            var stamp = ModalButton("ABRIR NOTA PARA CARIMBAR");
            stamp.Click += (_, e) =>
            {
                e.Handled = true;
                ShowStoredInvoiceDocument(latest);
            };
            panel.Children.Add(stamp);
        }

        return panel;
    }

    /* ----------------------------- PARADAS --------------------------- */

    private UIElement BuildStopsModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO DA PARADA", Style = FindResource("Label") as Style });
        var type = new ComboBox
        {
            ItemsSource = new[] { "Descanso", "Abastecimento", "Refeição", "Manutenção", "Carga/Descarga", "Documentação", "Trânsito", "Outro" },
            SelectedIndex = 0,
            FontSize = 14,
            Padding = new Thickness(8),
            Background = FindResource("Panel2") as Brush,
            Foreground = FindResource("Text") as Brush
        };
        panel.Children.Add(type);

        panel.Children.Add(ModalSectionTitle("OBSERVAÇÃO", "REGISTRO OPERACIONAL"));
        var note = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(10),
            Background = FindResource("Panel2") as Brush,
            Foreground = FindResource("Text") as Brush,
            AcceptsReturn = true,
            Height = 90,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(note);

        var save = ModalButton("REGISTRAR PARADA");
        save.Click += (_, e) =>
        {
            e.Handled = true;
            var selected = type.SelectedItem?.ToString() ?? "Outro";
            var record = new StopRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = selected,
                Note = note.Text.Trim(),
                StartedAtUtc = DateTime.UtcNow,
                OdometerKm = _lastOdometer
            };
            _stops.Add(record);
            if (!TrySaveOperations())
            {
                _stops.Remove(record);
                StatusText.Text = "TransPoli • não foi possível persistir a parada";
                return;
            }
            UpdateOpsCounters();
            StatusText.Text = $"TransPoli • parada registrada • {selected}";
            CloseOperationalModal();
        };
        panel.Children.Add(save);

        panel.Children.Add(ModalSectionTitle("ÚLTIMAS PARADAS", "HISTÓRICO OPERACIONAL"));
        var recent = _stops.OrderByDescending(x => x.StartedAtUtc).Take(10).ToList();
        if (recent.Count == 0) panel.Children.Add(ModalLine("Nenhuma parada registrada.", 12));
        else
        {
            var box = new StackPanel();
            foreach (var stop in recent)
                box.Children.Add(ModalValueRow(
                    $"{stop.StartedAtUtc.ToLocalTime():dd/MM HH:mm} • {stop.Type}",
                    $"{stop.OdometerKm:0.0} km"));
            panel.Children.Add(ModalPanel(box));
        }

        return panel;
    }

    /* --------------------------- OCORRÊNCIAS ------------------------- */

    private UIElement BuildOccurrenceModal()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "TIPO", Style = FindResource("Label") as Style });
        var type = new ComboBox
        {
            ItemsSource = new[] { "Acidente", "Avaria na carga", "Problema mecânico", "Problema com carga", "Atraso", "Observação" },
            SelectedIndex = 0,
            FontSize = 14,
            Padding = new Thickness(8),
            Background = FindResource("Panel2") as Brush,
            Foreground = FindResource("Text") as Brush
        };
        panel.Children.Add(type);

        panel.Children.Add(ModalLabel("DESCRIÇÃO"));
        var details = new TextBox
        {
            FontSize = 14,
            Padding = new Thickness(10),
            Background = FindResource("Panel2") as Brush,
            Foreground = FindResource("Text") as Brush,
            AcceptsReturn = true,
            Height = 110,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(details);

        var save = ModalButton("REGISTRAR OCORRÊNCIA");
        save.Click += (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(details.Text))
            {
                StatusText.Text = "TransPoli • descreva a ocorrência antes de registrar";
                return;
            }
            var selected = type.SelectedItem?.ToString() ?? "Observação";
            var record = new OccurrenceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = selected,
                Details = details.Text.Trim(),
                RecordedAtUtc = DateTime.UtcNow,
                OdometerKm = _lastOdometer
            };
            _occurrences.Add(record);
            if (!TrySaveOperations())
            {
                _occurrences.Remove(record);
                StatusText.Text = "TransPoli • não foi possível persistir a ocorrência";
                return;
            }
            UpdateOpsCounters();
            StatusText.Text = $"TransPoli • ocorrência registrada • {selected}";
            CloseOperationalModal();
        };
        panel.Children.Add(save);

        panel.Children.Add(ModalSectionTitle("ÚLTIMAS OCORRÊNCIAS", "HISTÓRICO OPERACIONAL"));
        var recent = _occurrences.OrderByDescending(x => x.RecordedAtUtc).Take(10).ToList();
        if (recent.Count == 0) panel.Children.Add(ModalLine("Nenhuma ocorrência registrada.", 12));
        else
        {
            var box = new StackPanel();
            foreach (var item in recent)
                box.Children.Add(ModalValueRow(
                    $"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {item.Type}", $"{item.OdometerKm:0.0} km"));
            panel.Children.Add(ModalPanel(box));
        }

        return panel;
    }

    /* ------------------------- ABASTECIMENTOS ------------------------ */

    private UIElement BuildFuelModal()
    {
        var panel = new StackPanel();

        // Há uma única fonte de registro de abastecimento. Este modal legado
        // apenas apresenta o estado/histórico e encaminha a confirmação para o
        // fluxo V13, que mantém identidade, banco e outbox idempotentes.
        if (_pendingRefuelTelemetry is not null && _pendingRefuelLiters > 0)
        {
            var data = _pendingRefuelTelemetry;
            EnsurePendingRefuelIdentity(data, _pendingRefuelLiters);
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = $"⛽ ABASTECIMENTO PENDENTE\n{_pendingRefuelLiters:0.0} L detectados em {data.OdometerKm:0.0} km. A confirmação financeira será feita pelo fluxo único de abastecimento.",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Green") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
            var continueButton = ModalButton("CONTINUAR CONFIRMAÇÃO");
            continueButton.Click += (_, e) => { e.Handled = true; ShowFuelPaymentModalV13(); };
            panel.Children.Add(continueButton);
            var close = ModalButton("FECHAR");
            close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
            panel.Children.Add(close);
        }
        else
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Nenhum abastecimento detectado está pendente. O lançamento financeiro exige uma detecção real da telemetria para preservar a identidade do evento.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        panel.Children.Add(ModalLabel("HISTÓRICO DE ABASTECIMENTOS"));
        var recent = _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(12).ToList();
        if (recent.Count == 0) panel.Children.Add(ModalLine("Nenhum abastecimento registrado.", 12));
        else
        {
            var box = new StackPanel();
            foreach (var item in recent)
                box.Children.Add(ModalValueRow(
                    $"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {(string.IsNullOrWhiteSpace(item.Station) ? "NÃO INFORMADO" : item.Station)}",
                    $"{item.Liters:0.0} L"));
            panel.Children.Add(ModalPanel(box));
            panel.Children.Add(ModalLine($"Total exibido: {recent.Sum(x => x.Liters):0.0} L", 12));
        }

        return panel;
    }

    /* ----------------------------- RESUMO ---------------------------- */

    private UIElement BuildSummaryModal()
    {
        var distance = _tripActive ? Math.Max(0, _lastOdometer - _tripStartOdometer) : 0;
        var fuelUsed = _tripActive ? Math.Max(0, _tripStartFuel - (_lastFuelLiters ?? _tripStartFuel)) : 0;
        var consumption = distance > 0.5f ? fuelUsed / distance : 0;

        var panel = new StackPanel();
        panel.Children.Add(ModalCard("VIAGEM ATIVA", _tripActive ? "SIM" : "NÃO",
            "DURAÇÃO", _tripActive ? FormatDuration(DateTime.UtcNow - _tripStartedAtUtc) : "—"));

        var box = new StackPanel();
        box.Children.Add(ModalValueRow("Distância da viagem", $"{distance:0.0} km"));
        box.Children.Add(ModalValueRow("Combustível consumido", $"{fuelUsed:0.0} L"));
        box.Children.Add(ModalValueRow("Consumo médio", consumption > 0 ? $"{consumption:0.000} L/km" : "—"));
        box.Children.Add(ModalValueRow("Último odômetro", $"{_lastOdometer:0.0} km"));
        panel.Children.Add(ModalPanel(box));

        panel.Children.Add(ModalLabel("REGISTROS OPERACIONAIS"));
        var counters = new StackPanel();
        counters.Children.Add(ModalValueRow("⛽ Abastecimentos", _refuelings.Count.ToString()));
        counters.Children.Add(ModalValueRow("🛑 Paradas", _stops.Count.ToString()));
        counters.Children.Add(ModalValueRow("⚠ Ocorrências", _occurrences.Count.ToString()));
        counters.Children.Add(ModalValueRow("📄 Documentos", _documents.Count.ToString()));
        panel.Children.Add(ModalPanel(counters));

        var bank = ModalButton("ABRIR BANCO DO MOTORISTA");
        bank.Click += (_, e) => { e.Handled = true; ShowBankModal(); };
        panel.Children.Add(bank);

        return panel;
    }

    /* ----------------------------- HELPERS --------------------------- */

    internal string GenerateInvoiceNumber()
    {
        // O número visível não é a identidade da nota (InvoiceId continua sendo
        // a fonte de verdade), mas também não pode colidir. Mantemos documentos
        // legados intactos e, para novas emissões, geramos um número persistido
        // com entropia suficiente e validamos contra todo o arquivo local.
        string number;
        do
        {
            number = $"TP-NF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}";
        }
        while (_documents.Any(x => string.Equals(x.Reference, number, StringComparison.OrdinalIgnoreCase)));

        return number;
    }
    internal static string FormatBrl(ulong? value) => value.HasValue ? $"R$ {value.Value:N2}" : "R$ 0,00";
    internal static string CargoKey(string cargo, string route) => $"{cargo}|{route}".Trim().ToUpperInvariant();

    internal static string BuildRouteForInvoice(TelemetrySnapshot? data) =>
        data == null || (string.IsNullOrWhiteSpace(data.SourceCity) && string.IsNullOrWhiteSpace(data.DestinationCity))
            ? "Rota não disponível"
            : $"{data.SourceCity ?? "Origem"} → {data.DestinationCity ?? "Destino"}";
    private Border DocumentExecutiveMetric(string label, string value, string resource)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = FindResource("Muted") as Brush });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.Bold,
            Foreground = FindResource(resource) as Brush, Margin = new Thickness(0, 5, 0, 0) });
        return new Border { Background = FindResource("Panel2") as Brush, BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(16),
            Margin = new Thickness(4, 0, 4, 0), Child = stack };
    }

}
