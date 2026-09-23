using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private bool _tripDocumentPending;
    private bool _tripGateModalOpen;
    private string _tripDocumentKey = string.Empty;
    private TelemetrySnapshot? _pendingTripTelemetry;
    private bool _tripGatePreviousTruckLocked;
    private DateTime _tripGateNextPromptUtc = DateTime.MinValue;
    private string _lastAuthorizedTripDocumentKey = string.Empty;
    private DateTime _lastAuthorizedTripDocumentAtUtc = DateTime.MinValue;

    private async void BeginTripDocumentGate(TelemetrySnapshot data)
    {
        if (_tripDocumentPending || _tripGateModalOpen) return;
        if (data.GamePaused || Math.Abs(data.SpeedKph) > 1.0f) return;

        var detectedKey = BuildTripDocumentKey(data);

        // A fonte de verdade é o documento persistido. Se a nota desta mesma
        // carga/rota já está CARIMBADA, nunca reabra o gate nem bloqueie o tablet.
        var persistedRoute = BuildRouteForInvoice(data);
        var persistedCargoKey = CargoKey(data.Cargo ?? "Carga não identificada", persistedRoute);
        var stampedDocument = _documents
            .Where(x => string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(_serverTripId) &&
                 string.Equals(x.TripId, _serverTripId, StringComparison.OrdinalIgnoreCase))
                || string.Equals(x.CargoKey, persistedCargoKey, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(x.Cargo, data.Cargo, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Route, persistedRoute, StringComparison.OrdinalIgnoreCase)));

        if (stampedDocument is not null)
        {
            _tripDocumentPending = false;
            _tripGateModalOpen = false;
            _tripGateNextPromptUtc = DateTime.MaxValue;
            _tripDocumentKey = detectedKey;
            _lastAuthorizedTripDocumentKey = detectedKey;
            _lastAuthorizedTripDocumentAtUtc = stampedDocument.RecordedAtUtc == default
                ? DateTime.UtcNow
                : stampedDocument.RecordedAtUtc.ToUniversalTime();
            _truckLocked = false;
            SaveSessionState();
            return;
        }
        // A mesma viagem pode permanecer reportada pela telemetria por vários ciclos
        // antes/depois do carimbo. Não devemos tratá-la como uma nova carga novamente.
        if (!string.IsNullOrWhiteSpace(_lastAuthorizedTripDocumentKey) &&
            IsSameTripDocumentKey(_lastAuthorizedTripDocumentKey, detectedKey))
        {
            // O carimbo vale para a TripSession, não para a vida do processo.
            // Reiniciar/atualizar o tablet durante a mesma carga nunca exige novo carimbo.
            if (_tripActive || DateTime.UtcNow - _lastAuthorizedTripDocumentAtUtc < TimeSpan.FromHours(12))
                return;
        }

        // Compatibilidade com builds anteriores que ainda não persistiam a chave do
        // documento: se a sessão ativa salva corresponde à mesma carga/rota, ela já
        // passou pelo gate e deve ser retomada diretamente.
        if (_tripActive &&
            IsSameTripDocumentKey(BuildTripDocumentKeyFromActiveSession(), detectedKey))
        {
            _lastAuthorizedTripDocumentKey = detectedKey;
            _lastAuthorizedTripDocumentAtUtc = _tripStartedAtUtc == default ? DateTime.UtcNow : _tripStartedAtUtc;
            SaveSessionState();
            return;
        }

        _tripDocumentPending = true;
        // Nova carga real detectada: zera a cotação anterior antes de consultar
        // o servidor. A resposta de CreateServerTrip preencherá a tarifa dinâmica
        // vigente e ela será preservada/congelada nesta viagem.
        _localTripRatePerKm = 0;
        _pendingTripTelemetry = data;
        _tripDocumentKey = detectedKey;
        _tripGatePreviousTruckLocked = _truckLocked;

        _truckLocked = true;
        EnsureLocalTripDocument(data);
        SaveSessionState();

        TripStatusText.Text = "DOCUMENTAÇÃO PENDENTE";
        TripLiveText.Text = "AGUARDANDO CARIMBO";
        StatusText.Text = "TransPoli • nova viagem detectada • carimbe a nota para iniciar";
        VehicleLockText.Text = "CAMINHÃO AGUARDANDO DOCUMENTAÇÃO";
        VehicleLockText.Foreground = FindResource("GoldBright") as Brush;
        UnlockButton.IsEnabled = false;
        UnlockButton.Opacity = 0.45;
        AlertText.Text = "DOCUMENTAÇÃO PENDENTE • abra a nota e carimbe para liberar a viagem";
        AlertText.Foreground = FindResource("GoldBright") as Brush;

        // A viagem é criada no servidor ANTES do carimbo. Assim o Banco
        // enxerga a operação como ativa e o evento invoice_stamped consegue
        // ficar ligado ao trip_id correto.
        await CreateServerTrip(data);
        _tripGateNextPromptUtc = DateTime.UtcNow.AddSeconds(2);
        _ = Dispatcher.BeginInvoke(new Action(() => ShowTripDocumentGate(data)), DispatcherPriority.Normal);
    }

    private void ShowTripDocumentGate(TelemetrySnapshot data)
    {
        if (!_tripDocumentPending || _tripGateModalOpen) return;
        if (data.GamePaused || Math.Abs(data.SpeedKph) > 1.0f) return;

        _tripGateModalOpen = true;
        _documentModalKind = "trip-gate";

        var cargo = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : data.Cargo;
        var route = BuildRouteForInvoice(data);
        var truck = $"{data.TruckBrand} {data.TruckModel}".Trim();
        if (string.IsNullOrWhiteSpace(truck)) truck = "Caminhão conectado";

        var body = new StackPanel();
        body.Children.Add(ModalHero("NOVA VIAGEM DETECTADA", "Liberação documental da carga", "O ETS2 confirmou uma nova operação. Revise a carga e carimbe o documento para liberar o monitoramento da viagem.", cargo, "GoldBright"));
        body.Children.Add(ModalStatusStrip("🔒 OPERAÇÃO BLOQUEADA • AGUARDANDO CARIMBO DO DOCUMENTO", "Yellow"));

        var warning = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("StrokeGold") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var warningStack = new StackPanel();
        warningStack.Children.Add(new TextBlock
        {
            Text = "DOCUMENTAÇÃO OBRIGATÓRIA",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GoldBright") as Brush
        });
        warningStack.Children.Add(new TextBlock
        {
            Text = "A telemetria detectou uma nova viagem. A carga ainda não foi liberada pelo TransPoli.",
            FontSize = 13,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        });
        warningStack.Children.Add(new TextBlock
        {
            Text = "Carimbe a nota no tablet antes de seguir viagem.",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("GoldBright") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0)
        });
        warning.Child = warningStack;
        body.Children.Add(warning);

        var info = new Grid();
        info.ColumnDefinitions.Add(new ColumnDefinition());
        info.ColumnDefinitions.Add(new ColumnDefinition());
        info.Children.Add(MiniCard("CARGA", cargo));
        var routeCard = MiniCard("ROTA", route);
        Grid.SetColumn(routeCard, 1);
        info.Children.Add(routeCard);
        body.Children.Add(info);

        var details = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        details.ColumnDefinitions.Add(new ColumnDefinition());
        details.ColumnDefinitions.Add(new ColumnDefinition());
        details.Children.Add(MiniCard("VEÍCULO", truck));
        var distance = data.PlannedDistanceKm > 0
            ? $"{data.PlannedDistanceKm:N0} km"
            : data.RouteDistanceKm > 0
                ? $"{data.RouteDistanceKm:N0} km"
                : "Distância não informada";
        var distanceCard = MiniCard("DISTÂNCIA", distance);
        Grid.SetColumn(distanceCard, 1);
        details.Children.Add(distanceCard);
        body.Children.Add(details);

        var open = ModalButton("ABRIR NOTA NO TABLET");
        open.Margin = new Thickness(0, 14, 0, 0);
        open.Height = 54;
        open.FontSize = 13;
        open.Click += (_, e) =>
        {
            e.Handled = true;
            _invoiceTelemetry = data;
            _tripGateModalOpen = false;
            CloseOperationalModal();
            _ = Dispatcher.BeginInvoke(new Action(ShowRealisticInvoiceModal), DispatcherPriority.Loaded);
        };
        body.Children.Add(open);

        body.Children.Add(new TextBlock
        {
            Text = "A viagem permanece bloqueada até o documento ser carimbado.",
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });

        ShowModalContent("trip-gate", BuildModalCard(
            "NOVA VIAGEM DETECTADA",
            body,
            "TransPoli • liberação segura da operação"));
    }

    private async Task AuthorizePendingTripAsync(TelemetrySnapshot data)
    {
        if (!_tripDocumentPending) return;

        _tripDocumentPending = false;
        _tripGateModalOpen = false;
        _tripGateNextPromptUtc = DateTime.MinValue;
        _lastAuthorizedTripDocumentKey = string.IsNullOrWhiteSpace(_tripDocumentKey)
            ? BuildTripDocumentKey(data)
            : _tripDocumentKey;
        _lastAuthorizedTripDocumentAtUtc = DateTime.UtcNow;

        _tripActive = true;
        _tripStartedAtUtc = DateTime.UtcNow;
        _tripStartOdometer = data.OdometerKm;
        _tripStartFuel = data.FuelLiters;
        // Toda viagem começa com contador próprio. Nunca herdamos os km da viagem anterior.
        _tripDistanceKm = 0;
        _tripFuelConsumedL = 0;
        _tripLastFuelLiters = data.FuelLiters;
        _tripMovingSeconds = 0;
        _tripLastProgressAtUtc = DateTime.UtcNow;
        // PlannedDistanceKm é a distância total do contrato. RouteDistanceKm é a distância restante do GPS do ETS2;
        // portanto não gravamos RouteDistanceKm como "total" (isso fazia a barra chegar a 100% cedo demais).
        _tripPlannedDistanceKm = data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : 0;
        _tripRouteOrigin = data.SourceCity;
        _tripRouteDestination = data.DestinationCity;
        _tripRouteOriginCompany = data.SourceCompany;
        _tripRouteDestinationCompany = data.DestinationCompany;
        _tripCargo = data.Cargo;
        _tripCargoValue = data.CargoValueBrl;
        // A viagem do servidor já foi criada no início do gate para garantir o vínculo do documento.
        // Não apagamos o ID aqui e não criamos uma segunda viagem após o carimbo.
        _localTripId = Guid.NewGuid().ToString("N");
        // Se o servidor já retornou a cotação TransPoli no gate, preserve-a.
        // Só recorremos à tabela local quando estamos offline/sem cotação.
        var serverQuotedRate = _localTripRatePerKm;
        _lastTelemetrySentAtUtc = DateTime.MinValue;
        _lastLocalTelemetrySavedAtUtc = DateTime.MinValue;

        try
        {
            if (LocalData.Current is { } store)
            {
                var localTrips = new LocalTripRepository(store.Db);
                _localTripRatePerKm = serverQuotedRate >= 5 && serverQuotedRate <= 12
                    ? serverQuotedRate
                    : localTrips.ResolveRatePerKm(data.Cargo);
                localTrips.StartTrip(_localTripId, data, _serverTripId, _localTripRatePerKm);
                new LocalTelemetryRepository(store.Db).Append(_localTripId, data);
                _lastLocalTelemetrySavedAtUtc = DateTime.UtcNow;
            }
        }
        catch
        {
            if (_localTripRatePerKm <= 0) _localTripRatePerKm = 6.00;
        }

        _truckLocked = _tripGatePreviousTruckLocked;
        SaveSessionState();

        TripStatusText.Text = "VIAGEM INICIADA • DOCUMENTO CARIMBADO";
        TripRouteText.Text = BuildRoute(data);
        TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
        TripDistanceText.Text = "0.0 km";
        TripDurationText.Text = "00:00:00";
        TripLiveText.Text = "MONITORAMENTO ATIVO";
        StatusText.Text = "TransPoli • nota carimbada • viagem liberada";
        AlertText.Text = "Viagem liberada pelo documento";
        AlertText.Foreground = FindResource("Green") as Brush;

        // O servidor já possui esta mesma viagem criada pelo gate antes do carimbo.
        // O carimbo apenas libera a operação; não cria uma nova viagem.
        if (!string.IsNullOrWhiteSpace(_serverTripId))
        {
            var route = BuildRouteForInvoice(data);
            var stamped = _documents
                .Where(x => string.IsNullOrWhiteSpace(x.TripId)
                         && x.CargoKey == CargoKey(data.Cargo ?? "Carga não identificada", route)
                         && string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();

            if (stamped is not null)
            {
                stamped.TripId = _serverTripId;
                SaveOperations();
            }
        }

        SaveSessionState();
    }

    private string BuildTripDocumentKeyFromActiveSession() =>
        $"{NormalizeTripKeyPart(_tripCargo)}|{NormalizeTripKeyPart(_tripRouteOrigin)}|{NormalizeTripKeyPart(_tripRouteOriginCompany)}|{NormalizeTripKeyPart(_tripRouteDestination)}|{NormalizeTripKeyPart(_tripRouteDestinationCompany)}";

    private string BuildTripDocumentKey(TelemetrySnapshot data)
    {
        // Não use o odômetro na identidade: ele muda a cada atualização e fazia
        // a mesma viagem parecer uma carga nova repetidamente.
        return $"{NormalizeTripKeyPart(data.Cargo)}|{NormalizeTripKeyPart(data.SourceCity)}|{NormalizeTripKeyPart(data.SourceCompany)}|{NormalizeTripKeyPart(data.DestinationCity)}|{NormalizeTripKeyPart(data.DestinationCompany)}";
    }

    private static bool IsSameTripDocumentKey(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTripKeyPart(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant();
}
