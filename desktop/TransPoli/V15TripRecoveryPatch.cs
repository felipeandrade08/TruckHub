using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Final V1.0.15 trip reconciliation pass. It runs after the legacy progress
/// modules and uses the live ETS2 route remaining distance to reconstruct the
/// actual travelled distance when the cockpit opens in the middle of a job.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _v15TripPatchTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _v15TripPatchStarted;
    private bool _v15TripPatchBusy;

    private static readonly bool V15TripPatchRegistration = RegisterV15TripPatch();

    private static bool RegisterV15TripPatch()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V15TripPatchLoaded), true);
        return true;
    }

    private static void V15TripPatchLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v15TripPatchStarted) return;
        window._v15TripPatchStarted = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window._v15TripPatchTimer.Tick += async (_, _) => await window.RunV15TripPatchAsync();
            window._v15TripPatchTimer.Start();
            _ = window.RunV15TripPatchAsync();
        }), DispatcherPriority.ContextIdle);
    }

    private async Task RunV15TripPatchAsync()
    {
        if (_v15TripPatchBusy) return;
        _v15TripPatchBusy = true;
        try
        {
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token)) return;
            var data = await LoadV15TripPatchTelemetryAsync();
            if (data is null || !data.Connected) return;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _v15Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array) return;

            JsonElement? active = null;
            foreach (var trip in trips.EnumerateArray())
            {
                if (!trip.TryGetProperty("status", out var status) || !string.Equals(status.GetString(), "active", StringComparison.OrdinalIgnoreCase)) continue;
                var cargo = trip.TryGetProperty("cargo", out var cargoElement) ? cargoElement.GetString() : null;
                if (!string.IsNullOrWhiteSpace(data.Cargo) && !string.IsNullOrWhiteSpace(cargo) && !string.Equals(data.Cargo, cargo, StringComparison.OrdinalIgnoreCase)) continue;
                active = trip;
                break;
            }

            if (active is null)
            {
                if (HasActiveJob(data) && data.CargoLoaded && data.PlannedDistanceKm > 0 && data.RouteDistanceKm > 0)
                    await RecoverMidTripV15Async(data, token);
                return;
            }

            var tripElement = active.Value;
            var id = tripElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) return;
            _serverTripId = id;
            _tripActive = true;

            if (tripElement.TryGetProperty("started_at", out var started) && DateTime.TryParse(started.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedStart))
                _tripStartedAtUtc = parsedStart.ToUniversalTime();

            var planned = data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : ReadPatchFloat(tripElement, "planned_distance_km");
            var remaining = Math.Max(0, data.RouteDistanceKm);
            var distance = 0f;

            if (planned > 0 && remaining > 0)
            {
                distance = Math.Max(0, planned - remaining);
                _tripStartOdometer = Math.Max(0, data.OdometerKm - distance);
            }
            else if (_tripStartOdometer > 0)
            {
                distance = Math.Max(0, data.OdometerKm - _tripStartOdometer);
            }

            if (planned <= 0 && remaining > 0) planned = distance + remaining;
            var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;
            var remainingForUi = planned > 0 ? Math.Max(0, planned - distance) : remaining;

            TripStatusText.Text = "VIAGEM EM ANDAMENTO";
            TripRouteText.Text = BuildRoute(data);
            TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            TripProgressText.Text = planned > 0 ? $"{progress * 100:0}%" : "—";
            TripRemainingText.Text = planned > 0 ? $"{remainingForUi:0.0} km restantes" : "distância restante indisponível";
            TripDistanceText.Text = $"{distance:0.0} km";
            TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc);
            TripStartText.Text = _tripStartedAtUtc.ToLocalTime().ToString("HH:mm");
            TripLiveText.Text = "MONITORAMENTO ATIVO";
            if (TripProgressFill.Parent is System.Windows.Controls.Grid progressGrid && progressGrid.ActualWidth > 0)
            {
                TripProgressFill.Width = progressGrid.ActualWidth * progress;
                TripTruckText.Margin = new Thickness(Math.Max(-10, TripProgressFill.Width - 10), 0, 0, 0);
            }

            if (!data.CargoLoaded && DateTime.UtcNow - _lastTripFinishedAtUtc > TimeSpan.FromSeconds(5))
                await FinishRecoveredTripV15Async(id, data, distance);
        }
        catch { }
        finally { _v15TripPatchBusy = false; }
    }

    private async Task<TelemetrySnapshot?> LoadV15TripPatchTelemetryAsync()
    {
        try
        {
            using var response = await _v15Http.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static float ReadPatchFloat(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number)) return number;
        return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}
