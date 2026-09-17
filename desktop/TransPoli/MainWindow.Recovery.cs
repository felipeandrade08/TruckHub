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
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
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
        if (_tripActive || _recoveryBusy || DateTime.UtcNow - _lastRecoveryAtUtc < TimeSpan.FromSeconds(3)) return;
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
            if (data is null || !data.Connected || !HasActiveJob(data)) return;

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
            _serverTripId = tripId; _tripActive = true; _jobMissingTicks = 0; _tripStartedAtUtc = ReadDateTime(tripElement, "started_at") ?? DateTime.UtcNow; _tripStartOdometer = data.OdometerKm; _tripStartFuel = data.FuelLiters;
            await RestoreTripBaseline(tripId, token, data);
            TripStatusText.Text = "VIAGEM RECUPERADA AUTOMATICAMENTE"; TripRouteText.Text = BuildRoute(data); TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer); TripDistanceText.Text = distance > 0.1f ? $"{distance:0.0} km" : "Em andamento"; TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc); StatusText.Text = "ETS2 conectado • viagem recuperada após reinício";
            await SendTelemetrySample(data, true);
        }
        catch { }
        finally { _recoveryBusy = false; }
    }

    private async Task RestoreTripBaseline(string tripId, string token, TelemetrySnapshot current)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/tachographs/{tripId}/samples"); request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request); if (!response.IsSuccessStatusCode) return;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); if (!document.RootElement.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array || samples.GetArrayLength() == 0) return;
            var first = samples[0]; if (first.TryGetProperty("odometer_km", out var odometer) && odometer.TryGetSingle(out var startOdometer)) _tripStartOdometer = startOdometer; if (first.TryGetProperty("fuel_l", out var fuel) && fuel.TryGetSingle(out var startFuel)) _tripStartFuel = startFuel;
        }
        catch { _tripStartOdometer = current.OdometerKm; _tripStartFuel = current.FuelLiters; }
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
