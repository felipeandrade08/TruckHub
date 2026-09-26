using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

/// <summary>
/// 🧾 NOTA FISCAL — layout DANFE
///
/// Reproduz a estrutura de quadros da DANFE (Documento Auxiliar da Nota
/// Fiscal Eletrônica): canhoto de recebimento, identificação do emitente,
/// chave de acesso com código de barras, natureza da operação,
/// destinatário/remetente, cálculo do imposto, transportador/volumes,
/// dados do produto e dados adicionais.
///
/// O documento é gerado a partir da carga do ETS2 e marcado como
/// simulação: é um documento de jogo, não tem validade fiscal.
/// </summary>
public partial class MainWindow
{
    private bool _invoiceStampBusy;
    private static readonly CultureInfo InvoiceCulture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly SolidColorBrush Ink = Brushes.Black;
    private static readonly SolidColorBrush InkSoft = new(Color.FromRgb(90, 90, 90));
    private const string InvoiceFont = "Arial";

    internal async void ShowRealisticInvoiceModal()
    {
        if (EnsureModalHost() == null) return;

        ShowModalContent("invoice", BuildModalLoading("🧾 EMITINDO DOCUMENTO FISCAL..."));

        // A telemetria é a fonte principal: funciona mesmo sem a API.
        _invoiceTelemetry ??= await LoadCurrentTelemetryAsync();
        var telemetry = _invoiceTelemetry;

        JsonElement? trip = null;
        var token = SecureTokenStore.Read();
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var root = J.Parse(await response.Content.ReadAsStringAsync());
                    // Prefere a viagem ativa; se não houver, usa a mais recente.
                    foreach (var item in J.Array(root, "trips"))
                    {
                        if (string.Equals(J.Str(item, "status"), "active", StringComparison.OrdinalIgnoreCase))
                        {
                            trip = item;
                            break;
                        }
                        trip ??= item;
                    }
                }
            }
            catch (Exception ex) { App.WriteUiCrashLog("Invoice.LoadServerTrip", ex); /* sem API o documento sai só com a telemetria */ }
        }

        var invoiceBody = new StackPanel();
        var cargoLabel = string.IsNullOrWhiteSpace(telemetry?.Cargo) ? "CARGA NÃO IDENTIFICADA" : telemetry!.Cargo!;
        invoiceBody.Children.Add(ModalHero("DOCUMENTAÇÃO DE VIAGEM", "Nota da carga", "Documento operacional simulado gerado a partir da telemetria ETS2. Não possui validade fiscal ou jurídica.", cargoLabel, "GoldBright"));
        invoiceBody.Children.Add(ModalStatusStrip(_tripDocumentPending ? "🔒 DOCUMENTO PENDENTE • CARIMBE PARA LIBERAR A VIAGEM" : "✓ DOCUMENTO OPERACIONAL • CONSULTE O ESTADO DO CARIMBO ABAIXO", _tripDocumentPending ? "Yellow" : "Green"));
        invoiceBody.Children.Add(BuildDanfe(telemetry, trip));
        ShowModalContent("invoice", BuildModalCard(
            "🧾 DOCUMENTO FISCAL",
            invoiceBody,
            "DANFE simulado da viagem • documento sem validade fiscal"));
    }

    private void ShowStoredInvoiceDocument(DocumentRecord item)
    {
        if (EnsureModalHost() == null) return;

        var parts = (item.Route ?? "").Split('→', StringSplitOptions.TrimEntries);
        _invoiceTelemetry = new TelemetrySnapshot
        {
            Connected = false,
            Cargo = item.Cargo,
            SourceCity = FirstNonEmpty(item.SourceCity, parts.Length > 0 ? parts[0] : null),
            DestinationCity = FirstNonEmpty(item.DestinationCity, parts.Length > 1 ? parts[^1] : null),
            SourceCompany = item.SourceCompany,
            DestinationCompany = item.DestinationCompany,
            TruckBrand = FirstNonEmpty(item.TruckBrand, item.Truck),
            TruckModel = item.TruckModel,
            LicensePlate = item.LicensePlate,
            OdometerKm = item.OdometerKm,
            PlannedDistanceKm = item.PlannedDistanceKm > 0 ? (uint)Math.Round(item.PlannedDistanceKm) : 0u,
            CargoMassKg = item.CargoMassKg,
            CargoValueBrl = item.CargoValueBrl > 0 ? (ulong?)decimal.ToUInt64(decimal.Round(item.CargoValueBrl, 0)) : null,
            CargoDamage = item.CargoDamage
        };

        var isCurrentOperation =
            (!string.IsNullOrWhiteSpace(_operationInvoiceId)
             && string.Equals(item.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(_operationTripId)
                && string.Equals(item.TripId, _operationTripId, StringComparison.OrdinalIgnoreCase));

        if (isCurrentOperation && !string.Equals(item.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
        {
            // A nota atual ainda emitida volta ao pipeline principal para que o
            // carimbo execute exatamente a mesma autorização/idempotência do gate.
            ShowRealisticInvoiceModal();
            return;
        }

        ShowModalContent("invoice", BuildModalCard(
            "🧾 DOCUMENTO FISCAL",
            BuildDanfe(_invoiceTelemetry, null, item),
            "Documento arquivado da viagem • somente leitura"));
    }

    /* ======================== DOCUMENTO ======================== */

    private UIElement BuildDanfe(TelemetrySnapshot? t, JsonElement? trip, DocumentRecord? archivedDocument = null)
    {
        var cargo = FirstNonEmpty(J.Str(trip, "cargo"), t?.Cargo, "NAO INFORMADO");
        var origin = FirstNonEmpty(J.Str(trip, "origin"), t?.SourceCity, "NAO INFORMADO");
        var destination = FirstNonEmpty(J.Str(trip, "destination"), t?.DestinationCity, "NAO INFORMADO");
        var sourceCompany = FirstNonEmpty(J.Str(trip, "sourceCompany"), t?.SourceCompany, "NAO INFORMADO");
        var destCompany = FirstNonEmpty(J.Str(trip, "destinationCompany"), t?.DestinationCompany, "NAO INFORMADO");

        var massKg = (decimal)Math.Max(0, t?.CargoMassKg ?? 0);
        var distance = J.Dec(trip, "distance_km", (decimal)(t?.PlannedDistanceKm ?? 0));
        var cargoValue = J.Dec(trip, "cargo_value_brl", t?.CargoValueBrl ?? 0);
        // O ETS2 fornece o valor declarado da carga, mas não fornece apuração
        // fiscal brasileira nem valor de frete da TransPoli. Dados ausentes ficam
        // zerados/não informados; nunca inventamos imposto ou preço operacional.
        cargoValue = Math.Max(0, cargoValue);
        var icmsBase = 0m;
        var icmsRate = 0m;
        var icms = 0m;
        var freight = 0m;
        var total = Math.Round(cargoValue, 2);

        var tripId = FirstNonEmpty(J.Str(trip, "id"), _operationTripId, _serverTripId, _localTripId);
        var routeKey = CargoKey(cargo, BuildRouteForInvoice(t));
        DocumentRecord? document = archivedDocument;
        if (document == null && !string.IsNullOrWhiteSpace(_operationInvoiceId))
            document = _documents.FirstOrDefault(x => string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase));
        if (document == null && !string.IsNullOrWhiteSpace(tripId))
            document = _documents.Where(x => string.Equals(x.TripId, tripId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
        // Compatibilidade exclusivamente para legado sem identidade persistida.
        // Uma operação moderna nunca reaproveita DANFE por coincidência de carga/rota.
        if (document == null
            && string.IsNullOrWhiteSpace(_operationInvoiceId)
            && string.IsNullOrWhiteSpace(_operationTripId)
            && string.IsNullOrWhiteSpace(_serverTripId)
            && string.IsNullOrWhiteSpace(tripId))
            document = _documents.Where(x =>
                    string.IsNullOrWhiteSpace(x.Id)
                    && string.IsNullOrWhiteSpace(x.TripId)
                    && (string.Equals(x.CargoKey, routeKey, StringComparison.OrdinalIgnoreCase)
                        || (string.Equals(x.Cargo, cargo, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(x.Route, BuildRouteForInvoice(t), StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();

        var number = string.IsNullOrWhiteSpace(document?.Reference) ? GenerateInvoiceNumber() : document.Reference;
        var documentKey = string.IsNullOrWhiteSpace(tripId) ? routeKey : $"TRIP|{tripId}";
        var stamped = string.Equals(document?.Status, "Carimbado", StringComparison.OrdinalIgnoreCase);
        // Nome do usuário do Windows não é identidade de motorista. A DANFE só
        // exibe um nome que tenha sido persistido pela própria operação.
        var driverName = FirstNonEmpty(document?.Driver, "NAO INFORMADO");

        if (document == null && archivedDocument == null)
        {
            document = new DocumentRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "Emitida",
                RecordedAtUtc = DateTime.UtcNow,
                Reference = number,
                CargoKey = documentKey,
                TripId = tripId ?? "",
                Cargo = cargo,
                Route = BuildRouteForInvoice(t),
                Driver = driverName,
                Truck = $"{t?.TruckBrand} {t?.TruckModel}".Trim(),
                TruckBrand = t?.TruckBrand ?? "", TruckModel = t?.TruckModel ?? "", LicensePlate = t?.LicensePlate ?? "",
                CargoMassKg = t?.CargoMassKg ?? 0, OdometerKm = t?.OdometerKm ?? 0, PlannedDistanceKm = t?.PlannedDistanceKm ?? 0, CargoValueBrl = t?.CargoValueBrl ?? 0, CargoDamage = t?.CargoDamage ?? 0, SourceCity = t?.SourceCity ?? "", DestinationCity = t?.DestinationCity ?? "",
                SourceCompany = t?.SourceCompany ?? "", DestinationCompany = t?.DestinationCompany ?? ""
            };
            _documents.Add(document);
            if (!TrySaveOperations())
            {
                _documents.Remove(document);
                document = null;
            }
            else UpdateOpsCounters();
        }

        // Chave interna persistida: não é chave fiscal de NF-e. Para legado sem
        // valor salvo, derivamos uma vez da identidade imutável do documento.
        if (document is not null && string.IsNullOrWhiteSpace(document.AccessKey))
        {
            var previousAccessKey = document.AccessKey;
            document.AccessKey = BuildDocumentAccessKey(document.Id, document.TripId, document.Reference);
            if (archivedDocument is null && !TrySaveOperations())
                document.AccessKey = previousAccessKey;
        }
        var accessKey = document?.AccessKey ?? BuildDocumentAccessKey("", tripId, number);
        var hasIssuedAt = document is not null && document.RecordedAtUtc != default;
        var displayAt = hasIssuedAt ? document!.RecordedAtUtc.ToLocalTime() : default;
        var paper = new Border
        {
            Background = Brushes.White,
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18),
            MaxWidth = 1080
        };

        var doc = new StackPanel();

        /* --- CANHOTO DE RECEBIMENTO --- */
        doc.Children.Add(BuildReceiptStub(number, destination));
        doc.Children.Add(new Border { Height = 7 });

        /* --- CABEÇALHO: EMITENTE + DANFE + CHAVE --- */
        doc.Children.Add(BuildHeaderBlock(number, accessKey));
        if (stamped) doc.Children.Add(BuildTransPoliStamp(document?.StampedAtUtc));

        /* --- NATUREZA DA OPERAÇÃO / PROTOCOLO --- */
        doc.Children.Add(BuildRow(
            (Field("NATUREZA DA OPERACAO", "SIMULACAO OPERACIONAL TRANSPOLI"), 3),
            (Field("PROTOCOLO DE AUTORIZACAO DE USO", "NAO INFORMADO • DOCUMENTO SEM VALIDADE FISCAL"), 2)));

        doc.Children.Add(BuildRow(
            (Field("INSCRICAO ESTADUAL", "NAO INFORMADO"), 1),
            (Field("INSC. EST. DO SUBST. TRIBUTARIO", "NAO INFORMADO"), 1),
            (Field("CNPJ / CPF", "NAO INFORMADO"), 1)));

        /* --- DESTINATÁRIO / REMETENTE --- */
        doc.Children.Add(SectionTitle("DESTINATARIO / REMETENTE"));
        doc.Children.Add(BuildRow(
            (Field("NOME / RAZAO SOCIAL", Up(destCompany)), 3),
            (Field("CNPJ / CPF", "NAO INFORMADO"), 1),
            (Field("DATA DA EMISSAO", hasIssuedAt ? displayAt.ToString("dd/MM/yyyy") : "NAO INFORMADO"), 1)));
        doc.Children.Add(BuildRow(
            (Field("ENDERECO", "NAO INFORMADO"), 3),
            (Field("BAIRRO / DISTRITO", "NAO INFORMADO"), 1),
            (Field("CEP", "NAO INFORMADO"), 1)));
        doc.Children.Add(BuildRow(
            (Field("MUNICIPIO", Up(destination)), 2),
            (Field("FONE / FAX", "NAO INFORMADO"), 1),
            (Field("UF", "NAO INFORMADO"), 1),
            (Field("DATA DA SAIDA / ENTRADA", hasIssuedAt ? displayAt.ToString("dd/MM/yyyy") : "NAO INFORMADO"), 1)));

        /* --- CÁLCULO DO IMPOSTO --- */
        doc.Children.Add(SectionTitle("CALCULO DO IMPOSTO"));
        doc.Children.Add(BuildRow(
            (Field("BASE DE CALCULO DO ICMS", InvoiceMoney(icmsBase), TextAlignment.Right), 1),
            (Field("VALOR DO ICMS", InvoiceMoney(icms), TextAlignment.Right), 1),
            (Field("BASE DE CALCULO ICMS ST", InvoiceMoney(0), TextAlignment.Right), 1),
            (Field("VALOR DO ICMS SUBSTITUICAO", InvoiceMoney(0), TextAlignment.Right), 1),
            (Field("VALOR TOTAL DOS PRODUTOS", InvoiceMoney(cargoValue), TextAlignment.Right), 1)));
        doc.Children.Add(BuildRow(
            (Field("VALOR DO FRETE", InvoiceMoney(freight), TextAlignment.Right), 1),
            (Field("VALOR DO SEGURO", InvoiceMoney(0), TextAlignment.Right), 1),
            (Field("DESCONTO", InvoiceMoney(0), TextAlignment.Right), 1),
            (Field("OUTRAS DESPESAS", InvoiceMoney(0), TextAlignment.Right), 1),
            (Field("VALOR TOTAL DA NOTA", InvoiceMoney(total), TextAlignment.Right, bold: true), 1)));

        /* --- TRANSPORTADOR / VOLUMES --- */
        doc.Children.Add(SectionTitle("TRANSPORTADOR / VOLUMES TRANSPORTADOS"));
        doc.Children.Add(BuildRow(
            (Field("MOTORISTA", Up(driverName)), 2),
            (Field("FRETE POR CONTA", "NAO INFORMADO"), 1),
            (Field("CODIGO ANTT", "NAO INFORMADO"), 1),
            (Field("PLACA DO VEICULO", Up(FirstNonEmpty(t?.LicensePlate, "NAO INFORMADO"))), 1),
            (Field("UF", "NAO INFORMADO"), 1)));
        doc.Children.Add(BuildRow(
            (Field("QUANTIDADE", "1"), 1),
            (Field("ESPECIE", "CARGA"), 1),
            (Field("MARCA", Up(FirstNonEmpty(t?.TruckBrand, "NAO INFORMADO"))), 1),
            (Field("NUMERACAO", number), 1),
            (Field("PESO BRUTO", $"{massKg:N3} KG", TextAlignment.Right), 1),
            (Field("PESO LIQUIDO", $"{massKg:N3} KG", TextAlignment.Right), 1)));

        /* --- DADOS DO PRODUTO --- */
        doc.Children.Add(SectionTitle("DADOS DO PRODUTO / SERVICO"));
        doc.Children.Add(BuildProductTable(cargo, massKg, cargoValue, icmsBase, icms, icmsRate));

        /* --- DADOS ADICIONAIS --- */
        doc.Children.Add(SectionTitle("DADOS ADICIONAIS"));
        var truckLabel = Up($"{t?.TruckBrand} {t?.TruckModel}".Trim());
        var complementary =
            $"Rota: {Up(origin)} -> {Up(destination)}. Expedidor: {Up(sourceCompany)}. " +
            $"Veiculo: {truckLabel}. " +
            $"Odometro na emissao: {t?.OdometerKm ?? 0:0.0} km. " +
            $"Distancia planejada: {distance:0.0} km. " +
            $"Avaria registrada: {(t?.CargoDamage ?? 0) * 100:0.00}%.\n" +
            "DOCUMENTO GERADO PELO TRANSPOLI A PARTIR DA TELEMETRIA DO SIMULADOR.";
        doc.Children.Add(BuildRow(
            (Field("INFORMACOES COMPLEMENTARES", complementary, height: 76), 3),
            (Field("RESERVADO AO FISCO", "SIMULACAO TRANSPOLI\nSEM VALOR FISCAL OU JURIDICO", height: 76), 2)));

        paper.Child = doc;

        /* --- MOLDURA DO MODAL --- */
        var wrapper = new Grid();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = paper
        };

        var shell = new Grid();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(new TextBlock
        {
            Text = $"NOTA FISCAL {number}",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal };

        var archive = new Button
        {
            Content = "📚 ARQUIVO DE NOTAS",
            Tag = ModalActionTag,
            Style = FindResource("TabletButton") as Style,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        archive.Click += (_, e) => { e.Handled = true; ShowOperationalModal("document"); };
        actions.Children.Add(archive);

        var stamp = new Button
        {
            Content = stamped ? "✓ NOTA CARIMBADA" : "🟠 CARIMBAR NOTA",
            Tag = ModalActionTag,
            Style = FindResource("TabletButton") as Style,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        stamp.IsEnabled = archivedDocument == null && !stamped;
        stamp.Opacity = stamp.IsEnabled ? 1.0 : 0.65;
        if (archivedDocument != null && !stamped) stamp.Content = "ARQUIVADA • SOMENTE LEITURA";
        stamp.Click += async (_, e) =>
        {
            e.Handled = true;
            if (_invoiceStampBusy) return;
            _invoiceStampBusy = true; stamp.IsEnabled = false;
            try
            {
            var changed = RegisterInvoiceDocument(cargo, BuildRouteForInvoice(t), number, tripId);
            if (changed) await RegisterInvoiceTripEventAsync(trip, number, cargo, driverName);

            if (_tripDocumentPending && _pendingTripTelemetry is not null)
            {
                await AuthorizePendingTripAsync(_pendingTripTelemetry);
            }
            else
            {
                TripStatusText.Text = "VIAGEM EM ANDAMENTO";
                TripCargoText.Text = $"Carga: {cargo}";
                StatusText.Text = $"TransPoli • nota {number} carimbada • viagem liberada";
            }

            ShowRealisticInvoiceModal();
            }
            finally { _invoiceStampBusy = false; }
        };
        actions.Children.Add(stamp);

        var close = new Button
        {
            Content = "✕ FECHAR",
            Tag = ModalActionTag,
            Style = FindResource("TabletButton") as Style,
            Padding = new Thickness(14, 8, 14, 8)
        };
        close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
        actions.Children.Add(close);

        Grid.SetColumn(actions, 1);
        bar.Children.Add(actions);
        shell.Children.Add(bar);
        Grid.SetRow(scroll, 1);
        shell.Children.Add(scroll);

        wrapper.Children.Add(new Border
        {
            Background = FindResource("Bg") as Brush,
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(20),
            Child = shell
        });

        return wrapper;
    }

    private Task RegisterInvoiceTripEventAsync(JsonElement? trip, string number, string cargo, string driverName)
    {
        try
        {
            var tripId = FirstNonEmpty(J.Str(trip, "id"), _serverTripId);
            var document = _documents.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(_operationInvoiceId) && string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(_operationTripId) && string.Equals(x.TripId, _operationTripId, StringComparison.OrdinalIgnoreCase)));
            var invoiceId = document?.Id ?? _operationInvoiceId;
            if (string.IsNullOrWhiteSpace(invoiceId)) return Task.CompletedTask;

            var eventId = $"invoice-stamped:{invoiceId}".ToLowerInvariant();
            var occurredAtUtc = document?.StampedAtUtc ?? DateTime.UtcNow;
            var eventDriver = !string.IsNullOrWhiteSpace(document?.Driver) ? document!.Driver : driverName;

            // O carimbo já é durável localmente. A publicação remota segue pela
            // mesma outbox idempotente das demais operações para sobreviver offline.
            _serverSync.QueueEvent(eventId, "invoice_stamped", tripId, occurredAtUtc,
                new { invoiceId, invoiceNumber = number, cargo, driver = eventDriver, source = "TransPoli" });
        }
        catch (Exception ex) { App.WriteUiCrashLog("Invoice.QueueTripEvent", ex); }
        return Task.CompletedTask;
    }

    private UIElement BuildTransPoliStamp(DateTime? stampedAtUtc)
    {
        var stamp = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(184, 30, 30)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 8, 8),
            RenderTransform = new RotateTransform(-5)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "TRANSPOLI", FontFamily = new FontFamily(InvoiceFont), FontSize = 16, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(Color.FromRgb(184, 30, 30)), HorizontalAlignment = HorizontalAlignment.Center });
        var stampAt = stampedAtUtc?.ToLocalTime();
        var stampText = stampAt.HasValue ? $"CARIMBADO • {stampAt:dd/MM/yyyy HH:mm:ss}" : "CARIMBADO • DOCUMENTO CONFERIDO";
        stack.Children.Add(new TextBlock { Text = stampText, FontFamily = new FontFamily(InvoiceFont), FontSize = 6.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(184, 30, 30)), HorizontalAlignment = HorizontalAlignment.Center });
        stamp.Child = stack;
        return stamp;
    }

    private bool RegisterInvoiceDocument(string cargo, string route, string number, string? tripId = null)
    {
        var effectiveTripId = FirstNonEmpty(tripId, _operationTripId, _serverTripId, _localTripId);
        var key = string.IsNullOrWhiteSpace(effectiveTripId) ? CargoKey(cargo, route) : $"TRIP|{effectiveTripId}";
        DocumentRecord? existing = null;
        if (!string.IsNullOrWhiteSpace(_operationInvoiceId))
            existing = _documents.FirstOrDefault(x => string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase));
        if (existing is null && !string.IsNullOrWhiteSpace(effectiveTripId))
            existing = _documents
                .Where(x => string.Equals(x.TripId, effectiveTripId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();

        // Cargo/rota só entram como compatibilidade quando esta operação ainda não
        // possui nenhuma identidade persistida. Nunca atravessam duas viagens novas.
        if (existing is null && string.IsNullOrWhiteSpace(_operationInvoiceId) && string.IsNullOrWhiteSpace(effectiveTripId))
            existing = _documents
                .Where(x => string.Equals(x.CargoKey, CargoKey(cargo, route), StringComparison.OrdinalIgnoreCase)
                         || (string.Equals(x.Cargo, cargo, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(x.Route, route, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();

        existing ??= new DocumentRecord
        {
            Id = string.IsNullOrWhiteSpace(_operationInvoiceId) ? Guid.NewGuid().ToString("N") : _operationInvoiceId,
            CargoKey = key,
            TripId = effectiveTripId,
            Cargo = cargo,
            Route = route
        };
        if (string.Equals(existing.Status, "Carimbado", StringComparison.OrdinalIgnoreCase)) return false;
        var wasAdded = !_documents.Contains(existing);
        var previousStatus = existing.Status;
        var previousReference = existing.Reference;
        var previousCargo = existing.Cargo;
        var previousRoute = existing.Route;
        var previousRecordedAtUtc = existing.RecordedAtUtc;
        var previousStampedAtUtc = existing.StampedAtUtc;
        if (wasAdded) _documents.Add(existing);
        existing.Status = "Carimbado";
        existing.Reference = number;
        existing.Cargo = cargo;
        existing.Route = route;
        if (existing.RecordedAtUtc == default) existing.RecordedAtUtc = DateTime.UtcNow;
        existing.StampedAtUtc ??= DateTime.UtcNow;
        if (!TrySaveOperations())
        {
            if (wasAdded) _documents.Remove(existing);
            else
            {
                existing.Status = previousStatus;
                existing.Reference = previousReference;
                existing.Cargo = previousCargo;
                existing.Route = previousRoute;
                existing.RecordedAtUtc = previousRecordedAtUtc;
                existing.StampedAtUtc = previousStampedAtUtc;
            }
            return false;
        }
        UpdateOpsCounters();
        return true;
    }

    /* ======================== BLOCOS ======================== */

    /// <summary>Canhoto destacável do topo da DANFE.</summary>
    private static UIElement BuildReceiptStub(string number, string destination)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = $"RECEBEMOS DE TRANSPOLI TRANSPORTES LTDA OS PRODUTOS CONSTANTES DA NOTA FISCAL INDICADA AO LADO. DESTINO: {Up(destination)}",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 7,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 3, 4, 4)
        });

        var sign = new Grid();
        sign.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sign.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var date = Field("DATA DE RECEBIMENTO", " ", height: 26);
        var signature = Field("IDENTIFICACAO E ASSINATURA DO RECEBEDOR", " ", height: 26);
        Grid.SetColumn(signature, 1);
        sign.Children.Add(date);
        sign.Children.Add(signature);
        left.Children.Add(sign);

        grid.Children.Add(left);

        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) };
        right.Children.Add(new TextBlock
        {
            Text = "NF-e",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        right.Children.Add(new TextBlock
        {
            Text = $"Nº {number}\nSERIE 001",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 8,
            Foreground = Ink,
            TextAlignment = TextAlignment.Center
        });
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        return new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            Child = grid
        };
    }

    /// <summary>Quadro do emitente, bloco DANFE e chave de acesso.</summary>
    private UIElement BuildHeaderBlock(string number, string accessKey)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });

        /* Emitente */
        var emitter = new StackPanel { Margin = new Thickness(8) };
        var logo = new Image { Source = FindResource("BrandLogo") as ImageSource, Width = 52, Height = 52, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 5) };
        emitter.Children.Add(logo);
        emitter.Children.Add(new TextBlock
        {
            Text = "TRANSPOLI",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 22,
            FontWeight = FontWeights.ExtraBold,
            Foreground = Ink
        });
        emitter.Children.Add(new TextBlock
        {
            Text = "TRANSPOLI • OPERACAO SIMULADA\nDADOS CADASTRAIS FISCAIS NAO INFORMADOS\nDOCUMENTO INTERNO SEM VALIDADE FISCAL",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 7.5,
            Foreground = Ink,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        grid.Children.Add(Boxed(emitter));

        /* DANFE */
        var danfe = new StackPanel { Margin = new Thickness(6, 8, 6, 8), HorizontalAlignment = HorizontalAlignment.Center };
        danfe.Children.Add(new TextBlock
        {
            Text = "DANFE",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        danfe.Children.Add(new TextBlock
        {
            Text = "DOCUMENTO AUXILIAR DA\nNOTA FISCAL ELETRONICA",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 7,
            Foreground = Ink,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 5)
        });

        var inout = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        inout.Children.Add(new TextBlock
        {
            Text = "0 - ENTRADA\n1 - SAIDA",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 7,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 6, 0)
        });
        inout.Children.Add(new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "1",
                FontFamily = new FontFamily(InvoiceFont),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        danfe.Children.Add(inout);

        danfe.Children.Add(new TextBlock
        {
            Text = $"Nº {number}\nSERIE 001    FOLHA 1/1",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });
        var danfeBox = Boxed(danfe);
        Grid.SetColumn(danfeBox, 1);
        grid.Children.Add(danfeBox);

        /* Chave de acesso */
        var keyPanel = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        keyPanel.Children.Add(BuildBarcode(accessKey));
        keyPanel.Children.Add(new TextBlock
        {
            Text = "CHAVE DE ACESSO",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 6.5,
            Foreground = InkSoft,
            Margin = new Thickness(0, 6, 0, 1)
        });
        keyPanel.Children.Add(new TextBlock
        {
            Text = FormatAccessKey(accessKey),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 8.5,
            FontWeight = FontWeights.Bold,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap
        });
        keyPanel.Children.Add(new TextBlock
        {
            Text = "DOCUMENTO DE SIMULACAO GERADO PELO TRANSPOLI. NAO POSSUI VALIDADE FISCAL E NAO E CONSULTAVEL NO PORTAL DA NF-e.",
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 6.5,
            Foreground = InkSoft,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        var keyBox = Boxed(keyPanel);
        Grid.SetColumn(keyBox, 2);
        grid.Children.Add(keyBox);

        return new Border { BorderBrush = Ink, BorderThickness = new Thickness(1, 1, 1, 0), Child = grid };
    }

    /// <summary>Tabela de itens da nota.</summary>
    private static UIElement BuildProductTable(
        string cargo, decimal massKg, decimal value, decimal icmsBase, decimal icms, decimal icmsRate)
    {
        var columns = new (string Header, double Width, TextAlignment Align)[]
        {
            ("CODIGO",      0.9, TextAlignment.Left),
            ("DESCRICAO DO PRODUTO / SERVICO", 3.4, TextAlignment.Left),
            ("NCM/SH",      0.8, TextAlignment.Center),
            ("CST",         0.6, TextAlignment.Center),
            ("CFOP",        0.6, TextAlignment.Center),
            ("UN",          0.5, TextAlignment.Center),
            ("QUANT",       0.9, TextAlignment.Right),
            ("V. UNIT",     1.0, TextAlignment.Right),
            ("V. TOTAL",    1.0, TextAlignment.Right),
            ("BC ICMS",     1.0, TextAlignment.Right),
            ("V. ICMS",     0.9, TextAlignment.Right),
            ("ALIQ",        0.6, TextAlignment.Right),
        };

        var tons = Math.Max(0.001m, massKg / 1000m);
        var unitValue = Math.Round(value / tons, 2);

        var values = new[]
        {
            "0001",
            Up(cargo),
            "8704.22",
            "000",
            "5353",
            "TON",
            tons.ToString("N3", InvoiceCulture),
            InvoiceMoney(unitValue),
            InvoiceMoney(value),
            InvoiceMoney(icmsBase),
            InvoiceMoney(icms),
            icmsRate.ToString("N2", InvoiceCulture),
        };

        var table = new Grid();
        foreach (var column in columns)
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(column.Width, GridUnitType.Star) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        for (var i = 0; i < columns.Length; i++)
        {
            var header = new Border
            {
                BorderBrush = Ink,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 1),
                Padding = new Thickness(3, 2, 3, 2),
                Child = new TextBlock
                {
                    Text = columns[i].Header,
                    FontFamily = new FontFamily(InvoiceFont),
                    FontSize = 6,
                    Foreground = InkSoft,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetColumn(header, i);
            table.Children.Add(header);

            var cell = new Border
            {
                BorderBrush = Ink,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0),
                Padding = new Thickness(3, 3, 3, 3),
                Child = new TextBlock
                {
                    Text = values[i],
                    FontFamily = new FontFamily(InvoiceFont),
                    FontSize = 7.5,
                    Foreground = Ink,
                    TextAlignment = columns[i].Align,
                    TextWrapping = TextWrapping.Wrap
                }
            };
            Grid.SetColumn(cell, i);
            Grid.SetRow(cell, 1);
            table.Children.Add(cell);

            // Espaço em branco do corpo da nota, como na DANFE impressa.
            var filler = new Border
            {
                BorderBrush = Ink,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 0)
            };
            Grid.SetColumn(filler, i);
            Grid.SetRow(filler, 2);
            table.Children.Add(filler);
        }

        return new Border { BorderBrush = Ink, BorderThickness = new Thickness(1, 0, 1, 1), Child = table };
    }

    /* ======================== PRIMITIVAS ======================== */

    /// <summary>Célula rotulada da DANFE: rótulo pequeno em cima, valor embaixo.</summary>
    private static Border Field(
        string label, string value,
        TextAlignment align = TextAlignment.Left,
        bool bold = false, double height = 0)
    {
        var panel = new StackPanel { Margin = new Thickness(4, 2, 4, 3) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 6,
            Foreground = InkSoft,
            TextWrapping = TextWrapping.NoWrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = bold ? 10 : 8,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Ink,
            TextAlignment = align,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0)
        });

        var border = new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = panel
        };
        if (height > 0) border.MinHeight = height;
        return border;
    }

    /// <summary>Linha de células com larguras proporcionais.</summary>
    private static UIElement BuildRow(params (Border Cell, int Weight)[] cells)
    {
        var grid = new Grid();
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(1, cells[i].Weight), GridUnitType.Star)
            });
            var cell = cells[i].Cell;
            // A última coluna não repete a borda direita do quadro.
            if (i == cells.Length - 1) cell.BorderThickness = new Thickness(0, 0, 0, 1);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        return new Border { BorderBrush = Ink, BorderThickness = new Thickness(1, 0, 1, 0), Child = grid };
    }

    private static UIElement SectionTitle(string text) => new Border
    {
        BorderBrush = Ink,
        BorderThickness = new Thickness(1, 1, 1, 1),
        Background = new SolidColorBrush(Color.FromRgb(232, 232, 232)),
        Padding = new Thickness(5, 2, 5, 2),
        Child = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily(InvoiceFont),
            FontSize = 7.5,
            FontWeight = FontWeights.Bold,
            Foreground = Ink
        }
    };

    private static Border Boxed(UIElement child) => new()
    {
        BorderBrush = Ink,
        BorderThickness = new Thickness(0, 0, 1, 1),
        Child = child
    };

    /// <summary>Código de barras desenhado a partir dos dígitos da chave.</summary>
    private static UIElement BuildBarcode(string key)
    {
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Height = 34 };
        foreach (var c in key)
        {
            var digit = char.IsDigit(c) ? c - '0' : 0;
            // Largura e espaçamento derivados do dígito: leitura visual densa.
            bars.Children.Add(new Border
            {
                Background = Ink,
                Width = 1 + digit % 2,
                Margin = new Thickness(0, 0, digit % 3 == 0 ? 2 : 1, 0)
            });
        }
        return new Border { Child = bars, HorizontalAlignment = HorizontalAlignment.Left };
    }

    /// <summary>Identificador visual interno de 44 dígitos; não representa chave fiscal de NF-e.</summary>
    private static string BuildDocumentAccessKey(string invoiceId, string? tripId, string? reference)
    {
        var identity = FirstNonEmpty(invoiceId, tripId, reference, "TRANSPOLI");
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
        var digits = new StringBuilder(44);
        foreach (var value in bytes)
        {
            digits.Append((value % 10).ToString(InvoiceCulture));
            if (digits.Length >= 44) break;
        }
        while (digits.Length < 44)
        {
            var index = digits.Length % bytes.Length;
            digits.Append((bytes[index] / 10 % 10).ToString(InvoiceCulture));
        }
        return digits.ToString(0, 44);
    }

    private static string FormatAccessKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(key.Substring(i, Math.Min(4, key.Length - i)));
        }
        return builder.ToString();
    }

    private static string InvoiceMoney(decimal value) => value.ToString("N2", InvoiceCulture);

    private static string Up(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value.Trim().ToUpperInvariant();

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value!;
        return "";
    }
}
