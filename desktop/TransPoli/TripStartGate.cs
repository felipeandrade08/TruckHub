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

    private async void BeginTripDocumentGate(TelemetrySnapshot data)
    {
        if (_tripDocumentPending || _tripGateModalOpen) return;
        if (data.GamePaused || Math.Abs(data.SpeedKph) > 1.0f) return;

        _tripDocumentPending = true;
        _pendingTripTelemetry = data;
        _tripDocumentKey = BuildTripDocumentKey(data);
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

        var warning = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("StrokeGold") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
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
        open.Height = 46;
        open.FontSize = 11;
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
            FontSize = 10,
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

        _tripActive = true;
        _tripStartedAtUtc = DateTime.UtcNow;
        _tripStartOdometer = data.OdometerKm;
        _tripStartFuel = data.FuelLiters;
        _tripFuelConsumedL = 0;
        _tripLastFuelLiters = data.FuelLiters;
        _tripMovingSeconds = 0;
        _tripLastProgressAtUtc = DateTime.UtcNow;
        _tripPlannedDistanceKm = data.PlannedDistanceKm > 0
            ? data.PlannedDistanceKm
            : data.RouteDistanceKm > 0 ? data.RouteDistanceKm : 0;
        _tripRouteOrigin = data.SourceCity;
        _tripRouteDestination = data.DestinationCity;
        _tripRouteOriginCompany = data.SourceCompany;
        _tripRouteDestinationCompany = data.DestinationCompany;
        _tripCargo = data.Cargo;
        _tripCargoValue = data.CargoValueBrl;
        _serverTripId = null;
        _localTripId = Guid.NewGuid().ToString("N");
        _localTripRatePerKm = 6.00;
        _lastTelemetrySentAtUtc = DateTime.MinValue;
        _lastLocalTelemetrySavedAtUtc = DateTime.MinValue;

        try
        {
            if (LocalData.Current is { } store)
            {
                var localTrips = new LocalTripRepository(store.Db);
                _localTripRatePerKm = localTrips.ResolveRatePerKm(data.Cargo);
                localTrips.StartTrip(_localTripId, data, null, _localTripRatePerKm);
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

        await CreateServerTrip(data);

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

    private string BuildTripDocumentKey(TelemetrySnapshot data)
    {
        return $"{data.Cargo}|{data.SourceCity}|{data.DestinationCity}|{data.OdometerKm:0.0}";
    }
}
