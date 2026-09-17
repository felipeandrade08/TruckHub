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
        string? modal =
            kind.Contains("DOCUMENT") || kind.Contains("DOCS") ? "document" :
            kind.Contains("PARADA") ? "stop" :
            kind.Contains("OCORR") || kind.Contains("AVARIA") ? "occurrence" :
            kind.Contains("ABAST") || kind.Contains("COMBUST") ? "fuel" :
            kind.Contains("RESUM") ? "summary" :
            kind.Contains("VIAGEM") || kind.Contains("CARGA") ? "cargo" : null;

        if (modal is null) return;
        e.Handled = true;
        window.ShowOperationalModal(modal);
    }

    internal async void ShowOperationalModal(string kind)
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent(kind, BuildModalLoading("CARREGANDO..."));

        if (kind is "document" or "cargo")
            _invoiceTelemetry = await LoadCurrentTelemetryAsync();

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
        catch { return null; }
    }

    private static string ModalTitle(string kind) => kind switch
    {
        "cargo" => "🚛 VIAGEM / OPERAÇÃO DA CARGA",
        "document" => "📄 DOCUMENTOS E NOTA DA CARGA",
        "stop" => "🛑 PARADAS",
        "occurrence" => "⚠ OCORRÊNCIAS",
        "fuel" => "⛽ ABASTECIMENTOS",
        "summary" => "📊 RESUMO OPERACIONAL",
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
        panel.Children.Add(ModalCard("CARGA", data?.Cargo ?? "Nenhuma carga ativa", "ROTA", BuildRouteForInvoice(data)));
        panel.Children.Add(ModalCard("STATUS", _tripActive ? "EM VIAGEM" : "VIAGEM NÃO INICIADA",
            "VELOCIDADE", $"{Math.Abs(data?.SpeedKph ?? 0):0} km/h"));
        panel.Children.Add(ModalCard("ODÔMETRO", $"{data?.OdometerKm ?? _lastOdometer:0.0} km",
            "DIST. PLANEJADA", $"{data?.PlannedDistanceKm ?? 0} km"));

        panel.Children.Add(ModalLabel("DADOS DA CARGA"));
        var box = new StackPanel();
        box.Children.Add(ModalValueRow("Carga", data?.Cargo ?? "Não identificada"));
        box.Children.Add(ModalValueRow("Peso", $"{data?.CargoMassKg ?? 0:N0} kg"));
        box.Children.Add(ModalValueRow("Valor declarado", FormatBrl(data?.CargoValueBrl)));
        box.Children.Add(ModalValueRow("Avaria atual", $"{(data?.CargoDamage ?? 0) * 100:0.00}%",
            (data?.CargoDamage ?? 0) > 0.01f ? "Yellow" : "Green"));
        box.Children.Add(ModalValueRow("Empresas",
            $"{data?.SourceCompany ?? "—"} → {data?.DestinationCompany ?? "—"}"));
        panel.Children.Add(ModalPanel(box));

        var invoice = ModalButton("🧾 EMITIR NOTA FISCAL DA CARGA");
        invoice.Click += (_, e) => { e.Handled = true; ShowRealisticInvoiceModal(); };
        panel.Children.Add(invoice);

        var docs = ModalButton("📄 DOCUMENTOS E CARIMBO");
        docs.Click += (_, e) => { e.Handled = true; ShowOperationalModal("document"); };
        panel.Children.Add(docs);

        return panel;
    }

    /* --------------------------- DOCUMENTOS -------------------------- */

    private UIElement BuildDocumentsModal()
    {
        var data = _invoiceTelemetry;
        var cargo = string.IsNullOrWhiteSpace(data?.Cargo) ? "Nenhuma carga ativa" : data!.Cargo!;
        var route = BuildRouteForInvoice(data);
        var key = CargoKey(cargo, route);
        var latest = _documents
            .Where(x => string.IsNullOrWhiteSpace(x.CargoKey) || x.CargoKey == key)
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefault();
        var invoiceNumber = string.IsNullOrWhiteSpace(latest?.Reference)
            ? GenerateInvoiceNumber()
            : latest!.Reference;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "CARGA ATIVA", Style = FindResource("Label") as Style });
        panel.Children.Add(new TextBlock
        {
            Text = cargo,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = route,
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            Margin = new Thickness(0, 4, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        var hasCargo = !cargo.Contains("Nenhuma", StringComparison.OrdinalIgnoreCase);

        var open = ModalButton("🧾 ABRIR NOTA FISCAL COMPLETA");
        open.Click += (_, e) => { e.Handled = true; ShowRealisticInvoiceModal(); };
        panel.Children.Add(open);

        var stamp = ModalButton(latest?.Status == "Carimbado" ? "✓ NOTA JÁ CARIMBADA" : "🟠 CARIMBAR E LANÇAR NOTA");
        stamp.IsEnabled = hasCargo;
        stamp.Opacity = hasCargo ? 1.0 : 0.45;
        stamp.Click += (_, e) =>
        {
            e.Handled = true;
            if (!hasCargo)
            {
                StatusText.Text = "TransPoli • nenhuma carga ativa para registrar";
                return;
            }
            var existing = _documents.FirstOrDefault(x => x.CargoKey == key)
                           ?? new DocumentRecord { Id = Guid.NewGuid().ToString("N"), CargoKey = key };
            if (!_documents.Contains(existing)) _documents.Add(existing);
            existing.Status = "Carimbado";
            existing.Reference = invoiceNumber;
            existing.RecordedAtUtc = DateTime.UtcNow;
            SaveOperations();
            UpdateOpsCounters();
            StatusText.Text = $"TransPoli • nota {invoiceNumber} carimbada e lançada";
            ShowOperationalModal("document");
        };
        panel.Children.Add(stamp);

        panel.Children.Add(ModalLabel("HISTÓRICO DE NOTAS"));
        var history = _documents.OrderByDescending(x => x.RecordedAtUtc).Take(15).ToList();
        if (history.Count == 0)
        {
            panel.Children.Add(ModalLine(
                "Nenhuma nota lançada ainda. O número é gerado automaticamente ao carimbar.", 12));
        }
        else
        {
            var box = new StackPanel();
            foreach (var item in history)
                box.Children.Add(ModalValueRow(
                    $"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {item.Status}", item.Reference));
            panel.Children.Add(ModalPanel(box));
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

        panel.Children.Add(ModalLabel("OBSERVAÇÃO"));
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

        var save = ModalButton("✓ REGISTRAR PARADA");
        save.Click += (_, e) =>
        {
            e.Handled = true;
            var selected = type.SelectedItem?.ToString() ?? "Outro";
            _stops.Add(new StopRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = selected,
                Note = note.Text.Trim(),
                StartedAtUtc = DateTime.UtcNow,
                OdometerKm = _lastOdometer
            });
            SaveOperations();
            UpdateOpsCounters();
            StatusText.Text = $"TransPoli • parada registrada • {selected}";
            CloseOperationalModal();
        };
        panel.Children.Add(save);

        panel.Children.Add(ModalLabel("ÚLTIMAS PARADAS"));
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

        var save = ModalButton("✓ REGISTRAR OCORRÊNCIA");
        save.Click += (_, e) =>
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(details.Text))
            {
                StatusText.Text = "TransPoli • descreva a ocorrência antes de registrar";
                return;
            }
            var selected = type.SelectedItem?.ToString() ?? "Observação";
            _occurrences.Add(new OccurrenceRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = selected,
                Details = details.Text.Trim(),
                RecordedAtUtc = DateTime.UtcNow,
                OdometerKm = _lastOdometer
            });
            SaveOperations();
            UpdateOpsCounters();
            StatusText.Text = $"TransPoli • ocorrência registrada • {selected}";
            CloseOperationalModal();
        };
        panel.Children.Add(save);

        panel.Children.Add(ModalLabel("ÚLTIMAS OCORRÊNCIAS"));
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

        // Antes esta tela abria em branco quando não havia abastecimento
        // pendente — o botão "⛽ ABASTECIMENTO" parecia quebrado.
        if (_pendingRefuelTelemetry is not null)
        {
            var data = _pendingRefuelTelemetry;
            var form = new StackPanel();
            form.Children.Add(new TextBlock
            {
                Text = "⛽ ABASTECIMENTO DETECTADO AUTOMATICAMENTE",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Green") as Brush,
                TextWrapping = TextWrapping.Wrap
            });
            form.Children.Add(new TextBlock
            {
                Text = $"Quantidade: {_pendingRefuelLiters:0.0} L\nOdômetro: {data.OdometerKm:0.0} km\nVeículo: {data.TruckBrand} {data.TruckModel}",
                FontSize = 13,
                Foreground = FindResource("Text") as Brush,
                Margin = new Thickness(0, 8, 0, 12)
            });

            form.Children.Add(new TextBlock { Text = "POSTO", Style = FindResource("Label") as Style });
            var station = new TextBox
            {
                FontSize = 14,
                Padding = new Thickness(10),
                Background = FindResource("Bg") as Brush,
                Foreground = FindResource("Text") as Brush
            };
            form.Children.Add(station);

            form.Children.Add(new TextBlock
            {
                Text = "LOCALIZAÇÃO",
                Style = FindResource("Label") as Style,
                Margin = new Thickness(0, 10, 0, 6)
            });
            var location = new TextBox
            {
                Text = data.DestinationCity ?? data.SourceCity ?? "",
                FontSize = 14,
                Padding = new Thickness(10),
                Background = FindResource("Bg") as Brush,
                Foreground = FindResource("Text") as Brush
            };
            form.Children.Add(location);

            var save = ModalButton("✓ REGISTRAR ABASTECIMENTO");
            save.Click += (_, e) =>
            {
                e.Handled = true;
                _refuelings.Add(new RefuelingRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RecordedAtUtc = DateTime.UtcNow,
                    Station = string.IsNullOrWhiteSpace(station.Text) ? "Posto não informado" : station.Text.Trim(),
                    Location = location.Text.Trim(),
                    Liters = _pendingRefuelLiters,
                    FuelBefore = _fuelBefore,
                    FuelAfter = _fuelAfter,
                    OdometerKm = data.OdometerKm,
                    Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(),
                    LicensePlate = data.LicensePlate ?? ""
                });
                SaveOperations();
                UpdateOpsCounters();
                StatusText.Text = $"TransPoli • abastecimento registrado • {_pendingRefuelLiters:0.0} L";
                _pendingRefuelTelemetry = null;
                _pendingRefuelLiters = 0;
                CloseOperationalModal();
            };
            form.Children.Add(save);

            var discard = ModalButton("✕ DESCARTAR DETECÇÃO");
            discard.Click += (_, e) =>
            {
                e.Handled = true;
                _pendingRefuelTelemetry = null;
                _pendingRefuelLiters = 0;
                ShowOperationalModal("fuel");
            };
            form.Children.Add(discard);

            panel.Children.Add(ModalPanel(form));
        }
        else
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "O TransPoli monitora o tanque em tempo real e abre esta tela sozinho quando detecta um abastecimento. Você também pode lançar um manualmente abaixo.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));

            panel.Children.Add(new TextBlock { Text = "LITROS", Style = FindResource("Label") as Style });
            var liters = new TextBox
            {
                FontSize = 14,
                Padding = new Thickness(10),
                Background = FindResource("Panel2") as Brush,
                Foreground = FindResource("Text") as Brush
            };
            panel.Children.Add(liters);

            panel.Children.Add(ModalLabel("POSTO"));
            var station = new TextBox
            {
                FontSize = 14,
                Padding = new Thickness(10),
                Background = FindResource("Panel2") as Brush,
                Foreground = FindResource("Text") as Brush
            };
            panel.Children.Add(station);

            var manual = ModalButton("✓ LANÇAR ABASTECIMENTO MANUAL");
            manual.Click += (_, e) =>
            {
                e.Handled = true;
                if (!float.TryParse(liters.Text.Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
                {
                    StatusText.Text = "TransPoli • informe a quantidade de litros";
                    return;
                }
                _refuelings.Add(new RefuelingRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    RecordedAtUtc = DateTime.UtcNow,
                    Station = string.IsNullOrWhiteSpace(station.Text) ? "Lançamento manual" : station.Text.Trim(),
                    Location = "",
                    Liters = value,
                    OdometerKm = _lastOdometer
                });
                SaveOperations();
                UpdateOpsCounters();
                StatusText.Text = $"TransPoli • abastecimento manual • {value:0.0} L";
                CloseOperationalModal();
            };
            panel.Children.Add(manual);
        }

        panel.Children.Add(ModalLabel("HISTÓRICO DE ABASTECIMENTOS"));
        var recent = _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(12).ToList();
        if (recent.Count == 0) panel.Children.Add(ModalLine("Nenhum abastecimento registrado.", 12));
        else
        {
            var box = new StackPanel();
            foreach (var item in recent)
                box.Children.Add(ModalValueRow(
                    $"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {item.Station}",
                    $"{item.Liters:0.0} L"));
            panel.Children.Add(ModalPanel(box));

            var total = recent.Sum(x => x.Liters);
            panel.Children.Add(ModalLine($"Total exibido: {total:0.0} L", 12));
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

        var bank = ModalButton("💰 ABRIR BANCO DO MOTORISTA");
        bank.Click += (_, e) => { e.Handled = true; ShowBankModal(); };
        panel.Children.Add(bank);

        return panel;
    }

    /* ----------------------------- HELPERS --------------------------- */

    internal static string GenerateInvoiceNumber() => $"TP-NF-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
    internal static string FormatBrl(ulong? value) => value.HasValue ? $"R$ {value.Value:N2}" : "R$ 0,00";
    internal static string CargoKey(string cargo, string route) => $"{cargo}|{route}".Trim().ToUpperInvariant();

    internal static string BuildRouteForInvoice(TelemetrySnapshot? data) =>
        data == null || (string.IsNullOrWhiteSpace(data.SourceCity) && string.IsNullOrWhiteSpace(data.DestinationCity))
            ? "Rota não disponível"
            : $"{data.SourceCity ?? "Origem"} → {data.DestinationCity ?? "Destino"}";
}
