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
        public DateTime TripStartedAtUtc { get; set; }
        public float TripStartOdometer { get; set; }
        public float TripStartFuel { get; set; }
        public float TripPlannedDistanceKm { get; set; }
        public string? RouteOrigin { get; set; }
        public string? RouteDestination { get; set; }
        public string? OriginCompany { get; set; }
        public string? DestinationCompany { get; set; }
        public string? Cargo { get; set; }
        public ulong? CargoValue { get; set; }
        public bool TruckUnlocked { get; set; }
    }

    private void EnsureLocalTripDocument(TelemetrySnapshot data)
    {
        try
        {
            var cargo = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não identificada" : data.Cargo;
            var route = BuildRouteForInvoice(data);
            var existing = _documents.FirstOrDefault(x =>
                x.Status != "Carimbado" &&
                (x.CargoKey == CargoKey(cargo, route) || (!string.IsNullOrWhiteSpace(_serverTripId) && x.TripId == _serverTripId)));
            if (existing != null) return;

            _documents.Add(new DocumentRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Status = "Emitida",
                RecordedAtUtc = DateTime.UtcNow,
                Reference = GenerateInvoiceNumber(),
                CargoKey = string.IsNullOrWhiteSpace(_serverTripId) ? CargoKey(cargo, route) : $"TRIP|{_serverTripId}",
                TripId = _serverTripId ?? "",
                Cargo = cargo,
                Route = route,
                Driver = Environment.UserName,
                Truck = $"{data.TruckBrand} {data.TruckModel}".Trim()
            });
            SaveOperations();
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
            _tripStartedAtUtc = state.TripStartedAtUtc == default ? DateTime.UtcNow : state.TripStartedAtUtc.ToUniversalTime();
            _tripStartOdometer = state.TripStartOdometer;
            _tripStartFuel = state.TripStartFuel;
            _tripPlannedDistanceKm = state.TripPlannedDistanceKm;
            _tripRouteOrigin = state.RouteOrigin;
            _tripRouteDestination = state.RouteDestination;
            _tripRouteOriginCompany = state.OriginCompany;
            _tripRouteDestinationCompany = state.DestinationCompany;
            _tripCargo = state.Cargo;
            _tripCargoValue = state.CargoValue;
            _truckLocked = !state.TruckUnlocked;

            if (_tripActive)
                StatusText.Text = "TransPoli • recuperando a viagem salva...";
        }
        catch
        {
            // Estado local corrompido nunca pode impedir a abertura do tablet.
        }
    }

    private void SaveSessionState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SessionStatePath)!);
            var state = new SessionState
            {
                TripActive = _tripActive,
                ServerTripId = _serverTripId,
                TripStartedAtUtc = _tripStartedAtUtc,
                TripStartOdometer = _tripStartOdometer,
                TripStartFuel = _tripStartFuel,
                TripPlannedDistanceKm = _tripPlannedDistanceKm,
                RouteOrigin = _tripRouteOrigin,
                RouteDestination = _tripRouteDestination,
                OriginCompany = _tripRouteOriginCompany,
                DestinationCompany = _tripRouteDestinationCompany,
                Cargo = _tripCargo,
                CargoValue = _tripCargoValue,
                TruckUnlocked = !_truckLocked
            };
            File.WriteAllText(SessionStatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void ClearSessionState()
    {
        _tripActive = false;
        _serverTripId = null;
        _tripPlannedDistanceKm = 0;
        _tripRouteOrigin = null;
        _tripRouteDestination = null;
        _tripRouteOriginCompany = null;
        _tripRouteDestinationCompany = null;
        _tripCargo = null;
        _tripCargoValue = null;
        try { if (File.Exists(SessionStatePath)) File.Delete(SessionStatePath); } catch { }
    }
}
