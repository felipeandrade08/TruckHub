using System;
using System.IO;
using System.Text.Json;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly string SessionStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransPoli",
        "transpoli-session.json");

    private sealed class SessionState
    {
        public bool TripActive { get; set; }
        public string? ServerTripId { get; set; }
        public string? LocalTripId { get; set; }
        public string? OperationTripId { get; set; }
        public string? InvoiceId { get; set; }
        public DateTime TripStartedAtUtc { get; set; }
        public float TripStartOdometer { get; set; }
        public float TripStartFuel { get; set; }
        public float TripPlannedDistanceKm { get; set; }
        public double TripMovingSeconds { get; set; }
        public float TripDistanceKm { get; set; }
        public float TripFuelConsumedL { get; set; }
        public float TripLastFuelLiters { get; set; }
        public double TripRatePerKm { get; set; }
        public string? RouteOrigin { get; set; }
        public string? RouteDestination { get; set; }
        public string? OriginCompany { get; set; }
        public string? DestinationCompany { get; set; }
        public string? Cargo { get; set; }
        public ulong? CargoValue { get; set; }
        public bool TruckUnlocked { get; set; }
        public string? AuthorizedTripDocumentKey { get; set; }
        public DateTime AuthorizedTripDocumentAtUtc { get; set; }
    }

    private string _operationTripId = string.Empty;
    private string _operationInvoiceId = string.Empty;

    private void EnsureOperationIdentity()
    {
        if (string.IsNullOrWhiteSpace(_operationTripId))
            _operationTripId = !string.IsNullOrWhiteSpace(_localTripId) ? _localTripId! : Guid.NewGuid().ToString("N");

        // A identidade local nasce junto com a operação e permanece canônica.
        // O UUID retornado pelo servidor vive apenas em _serverTripId/mapeamento.
        if (string.IsNullOrWhiteSpace(_localTripId))
            _localTripId = _operationTripId;

        if (string.IsNullOrWhiteSpace(_operationInvoiceId))
            _operationInvoiceId = Guid.NewGuid().ToString("N");
    }

    private void EnsureLocalTripDocument(TelemetrySnapshot data)
    {
        try
        {
            EnsureOperationIdentity();
            var cargo = string.IsNullOrWhiteSpace(data.Cargo) ? "" : data.Cargo.Trim();
            var route = BuildRouteForInvoice(data);
            var existing = _documents.FirstOrDefault(x =>
                string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(_serverTripId) && string.Equals(x.TripId, _serverTripId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(_operationTripId) && string.Equals(x.TripId, _operationTripId, StringComparison.OrdinalIgnoreCase)));
            if (existing != null)
            {
                _operationInvoiceId = existing.Id;
                return;
            }

            var created = new DocumentRecord
            {
                Id = _operationInvoiceId,
                Status = "Emitida",
                RecordedAtUtc = DateTime.UtcNow,
                Reference = GenerateInvoiceNumber(),
                CargoKey = string.IsNullOrWhiteSpace(_serverTripId) ? CargoKey(cargo, route) : $"TRIP|{_serverTripId}",
                TripId = _operationTripId,
                Cargo = cargo,
                Route = route,
                Driver = "",
                Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(),
                TruckBrand = data.TruckBrand ?? "", TruckModel = data.TruckModel ?? "", LicensePlate = data.LicensePlate ?? "",
                CargoMassKg = data.CargoMassKg, OdometerKm = data.OdometerKm, PlannedDistanceKm = data.PlannedDistanceKm, CargoValueBrl = data.CargoValueBrl, CargoDamage = data.CargoDamage,
                SourceCity = data.SourceCity ?? "", DestinationCity = data.DestinationCity ?? "", SourceCompany = data.SourceCompany ?? "", DestinationCompany = data.DestinationCompany ?? ""
            };
            _documents.Add(created);
            if (!TrySaveOperations())
            {
                _documents.Remove(created);
                return;
            }
            UpdateOpsCounters();
        }
        catch { }
    }

    private void LoadSessionState()
    {
        try
        {
            if (!File.Exists(SessionStatePath)) return;
            var state = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(SessionStatePath));
            if (state is null) return;

            _tripActive = state.TripActive;
            _serverTripId = state.ServerTripId;
            _localTripId = state.LocalTripId;
            _operationTripId = state.OperationTripId ?? state.LocalTripId ?? state.ServerTripId ?? string.Empty;
            _operationInvoiceId = state.InvoiceId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_localTripId) && !string.IsNullOrWhiteSpace(_serverTripId) && LocalData.Current is { } localStore)
                _localTripId = new LocalTripRepository(localStore.Db).FindActiveTripIdByServerId(_serverTripId);
            _tripStartedAtUtc = state.TripStartedAtUtc == default ? DateTime.UtcNow : state.TripStartedAtUtc.ToUniversalTime();
            _tripStartOdometer = state.TripStartOdometer;
            _tripStartFuel = state.TripStartFuel;
            _tripPlannedDistanceKm = state.TripPlannedDistanceKm;
            _tripMovingSeconds = state.TripMovingSeconds;
            _tripDistanceKm = Math.Max(0, state.TripDistanceKm);
            _tripFuelConsumedL = Math.Max(0, state.TripFuelConsumedL);
            _tripLastFuelLiters = Math.Max(0, state.TripLastFuelLiters);
            _localTripRatePerKm = state.TripRatePerKm;
            _tripLastProgressAtUtc = DateTime.UtcNow;
            _tripRouteOrigin = state.RouteOrigin;
            _tripRouteDestination = state.RouteDestination;
            _tripRouteOriginCompany = state.OriginCompany;
            _tripRouteDestinationCompany = state.DestinationCompany;
            _tripCargo = state.Cargo;
            _tripCargoValue = state.CargoValue;
            _truckLocked = !state.TruckUnlocked;
            _lastAuthorizedTripDocumentKey = state.AuthorizedTripDocumentKey ?? string.Empty;
            _lastAuthorizedTripDocumentAtUtc = state.AuthorizedTripDocumentAtUtc == default ? DateTime.MinValue : state.AuthorizedTripDocumentAtUtc.ToUniversalTime();

            if (_tripActive)
                StatusText.Text = "TransPoli • recuperando a viagem salva...";
        }
        catch
        {
            // Estado local corrompido nunca pode impedir a abertura do tablet.
        }
    }

    private bool TrySaveSessionState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionStatePath)!);
            var state = new SessionState
            {
                TripActive = _tripActive,
                ServerTripId = _serverTripId,
                LocalTripId = _localTripId,
                OperationTripId = _operationTripId,
                InvoiceId = _operationInvoiceId,
                TripStartedAtUtc = _tripStartedAtUtc,
                TripStartOdometer = _tripStartOdometer,
                TripStartFuel = _tripStartFuel,
                TripPlannedDistanceKm = _tripPlannedDistanceKm,
                TripMovingSeconds = _tripMovingSeconds,
                TripDistanceKm = _tripDistanceKm,
                TripFuelConsumedL = _tripFuelConsumedL,
                TripLastFuelLiters = _tripLastFuelLiters,
                TripRatePerKm = _localTripRatePerKm,
                RouteOrigin = _tripRouteOrigin,
                RouteDestination = _tripRouteDestination,
                OriginCompany = _tripRouteOriginCompany,
                DestinationCompany = _tripRouteDestinationCompany,
                Cargo = _tripCargo,
                CargoValue = _tripCargoValue,
                TruckUnlocked = !_truckLocked,
                AuthorizedTripDocumentKey = _lastAuthorizedTripDocumentKey,
                AuthorizedTripDocumentAtUtc = _lastAuthorizedTripDocumentAtUtc
            };
            var tempPath = SessionStatePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, SessionStatePath, true);
            return true;
        }
        catch { return false; }
    }

    private void SaveSessionState() => _ = TrySaveSessionState();

    private bool TryClearSessionState()
    {
        try
        {
            if (File.Exists(SessionStatePath)) File.Delete(SessionStatePath);
            return !File.Exists(SessionStatePath);
        }
        catch { return false; }
    }

    private bool ClearSessionState()
    {
        if (!TryClearSessionState()) return false;
        _tripActive = false;
        _serverTripId = null;
        _localTripId = null;
        _operationTripId = string.Empty;
        _operationInvoiceId = string.Empty;
        _tripDocumentPending = false;
        _tripGateModalOpen = false;
        _tripDocumentKey = string.Empty;
        _pendingTripTelemetry = null;
        _tripGateNextPromptUtc = DateTime.MinValue;
        _tripGatePreviousTruckLocked = false;
        _invoiceTelemetry = null;
        _tripPlannedDistanceKm = 0;
        _tripDistanceKm = 0;
        _tripFuelConsumedL = 0;
        _tripLastFuelLiters = 0;
        _tripMovingSeconds = 0;
        _tripRouteOrigin = null;
        _tripRouteDestination = null;
        _tripRouteOriginCompany = null;
        _tripRouteDestinationCompany = null;
        _tripCargo = null;
        _tripCargoValue = null;
        _tripStartedAtUtc = default;
        _tripStartOdometer = 0;
        _tripStartFuel = 0;
        _tripLastProgressAtUtc = DateTime.MinValue;
        _lastTelemetrySentAtUtc = DateTime.MinValue;
        _lastLocalTelemetrySavedAtUtc = DateTime.MinValue;
        _lastTripFinancialRefreshId = null;
        _lastTripFinancialRefreshUtc = DateTime.MinValue;
        _lastAuthorizedTripDocumentKey = string.Empty;
        _lastAuthorizedTripDocumentAtUtc = DateTime.MinValue;
        return true;
    }
}
