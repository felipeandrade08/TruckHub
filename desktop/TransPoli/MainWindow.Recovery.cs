using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly DispatcherTimer RecoveryTimer = CreateRecoveryTimer();
    private bool _recoveryBusy;
    private DateTime _lastRecoveryAtUtc = DateTime.MinValue;

    private static DispatcherTimer CreateRecoveryTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) =>
        {
            if (Application.Current?.MainWindow is MainWindow window)
                await window.TryRecoverActiveTrip();
        };
        timer.Start();
        return timer;
    }

    private async Task TryRecoverActiveTrip()
    {
        if (_tripActive || _recoveryBusy || DateTime.UtcNow - _lastRecoveryAtUtc < TimeSpan.FromSeconds(15)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;

        _recoveryBusy = true;
        _lastRecoveryAtUtc = DateTime.UtcNow;
        try
        {
            using var telemetryResponse = await _http.GetAsync(TelemetryUrl);
            if (!telemetryResponse.IsSuccessStatusCode) return;
            await using var telemetryStream = await telemetryResponse.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(telemetryStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array) return;
            JsonElement? activeTrip = null;
            foreach (var trip in trips.EnumerateArray()) { if (!IsOpenTrip(trip)) continue; if (!TripMatchesTelemetry(trip, data)) continue; activeTrip = trip; break; }
            if (activeTrip is null) return;
            var tripElement = activeTrip.Value;
            if (!tripElement.TryGetProperty("id", out var idElement)) return;
            var tripId = idElement.GetString(); if (string.IsNullOrWhiteSpace(tripId)) return;
            _serverTripId = tripId;
            _tripActive = true;
            _jobMissingTicks = 0;
            _tripStartedAtUtc = ReadDateTime(tripElement, "started_at") ?? DateTime.UtcNow;

            var serverStartOdometer = ReadNumber(tripElement, "start_odometer_km");
            var serverStartFuel = ReadNumber(tripElement, "start_fuel_l");
            var serverPlannedDistance = ReadNumber(tripElement, "planned_distance_km");
            if (serverPlannedDistance > 0) _tripPlannedDistanceKm = (float)serverPlannedDistance;

            // Prioridade: marco salvo localmente > marco registrado no servidor > amostra atual.
            // Uma reconexão do ETS2 nunca transforma o progresso já percorrido em 0%.
            if (_tripStartOdometer <= 0)
                _tripStartOdometer = serverStartOdometer > 0 ? (float)serverStartOdometer : data.OdometerKm;
            if (_tripStartFuel <= 0)
                _tripStartFuel = serverStartFuel > 0 ? (float)serverStartFuel : data.FuelLiters;

            await RestoreTripBaseline(tripId, token, data);
            TripStatusText.Text = "VIAGEM RECUPERADA AUTOMATICAMENTE"; TripRouteText.Text = BuildRoute(data); TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            var planned = _tripPlannedDistanceKm > 0 ? _tripPlannedDistanceKm : (tripElement.TryGetProperty("planned_distance_km", out var plannedElement) ? (float)ReadNumber(tripElement, "planned_distance_km") : data.PlannedDistanceKm);
            var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;
            var remaining = planned > 0 ? Math.Max(0f, planned - distance) : 0f;
            TripDistanceText.Text = distance > 0.1f ? $"{distance:0.0} km" : "Em andamento";
            TripProgressText.Text = planned > 0 ? $"{progress * 100:0}%" : "—";
            TripProgressText2.Text = TripProgressText.Text;
            TripDistanceLiveText.Text = planned > 0 ? $"{distance:0.0} / {planned:0} km" : "— / — km";
            TripDistanceLiveText2.Text = TripDistanceLiveText.Text;
            TripRemainingText.Text = planned > 0 ? $"{remaining:0.0} km restantes" : "distância restante indisponível";
            TripRemainingText2.Text = TripRemainingText.Text;
            TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc);
            StatusText.Text = "ETS2 conectado • viagem recuperada após reinício";
            SaveSessionState();
            await SendTelemetrySample(data, true);
        }
        catch { }
        finally { _recoveryBusy = false; }
    }

    private async Task RestoreTripBaseline(string tripId, string token, TelemetrySnapshot current)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips/{tripId}/telemetry");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (_tripStartOdometer <= 0 &&
                document.RootElement.TryGetProperty("telemetryStartOdometer", out var startOdo) &&
                startOdo.ValueKind == JsonValueKind.Number &&
                startOdo.TryGetSingle(out var serverStartOdo) &&
                serverStartOdo > 0)
                _tripStartOdometer = serverStartOdo;

            if (_tripStartFuel <= 0 &&
                document.RootElement.TryGetProperty("telemetryStartFuel", out var startFuel) &&
                startFuel.ValueKind == JsonValueKind.Number &&
                startFuel.TryGetSingle(out var serverFuel) &&
                serverFuel > 0)
                _tripStartFuel = serverFuel;

            if (_tripStartOdometer <= 0) _tripStartOdometer = current.OdometerKm;
            if (_tripStartFuel <= 0) _tripStartFuel = current.FuelLiters;
        }
        catch
        {
            if (_tripStartOdometer <= 0) _tripStartOdometer = current.OdometerKm;
            if (_tripStartFuel <= 0) _tripStartFuel = current.FuelLiters;
        }
    }

    private static double ReadNumber(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
    private static bool IsOpenTrip(JsonElement trip)
    {
        if (trip.TryGetProperty("finished_at", out var finished) && finished.ValueKind != JsonValueKind.Null && finished.ValueKind != JsonValueKind.Undefined) return false;
        if (trip.TryGetProperty("status", out var status)) { var value = status.GetString(); if (!string.IsNullOrWhiteSpace(value) && !value.Equals("active", StringComparison.OrdinalIgnoreCase)) return false; }
        return true;
    }
    private static bool TripMatchesTelemetry(JsonElement trip, TelemetrySnapshot data)
    {
        var cargo = ReadString(trip, "cargo"); var destination = ReadString(trip, "destination"); var origin = ReadString(trip, "origin");
        if (!string.IsNullOrWhiteSpace(cargo) && !string.IsNullOrWhiteSpace(data.Cargo) && !Same(cargo, data.Cargo)) return false;
        if (!string.IsNullOrWhiteSpace(destination) && !string.IsNullOrWhiteSpace(data.DestinationCity) && !Same(destination, data.DestinationCity)) return false;
        if (!string.IsNullOrWhiteSpace(origin) && !string.IsNullOrWhiteSpace(data.SourceCity) && !Same(origin, data.SourceCity)) return false;
        return true;
    }
    private static string? ReadString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTime? ReadDateTime(JsonElement element, string property) { var value = ReadString(element, property); return DateTime.TryParse(value, out var result) ? result.ToUniversalTime() : null; }
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
