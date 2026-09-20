using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private string? _tripRouteOrigin;
    private string? _tripRouteDestination;
    private string? _tripRouteOriginCompany;
    private string? _tripRouteDestinationCompany;
    private string? _tripCargo;
    private ulong? _tripCargoValue;
    private float _tripPlannedDistanceKm;
    private double _tripMovingSeconds;
    private float _tripDistanceKm;
    private float _tripFuelConsumedL;
    private float _tripLastFuelLiters;
    private DateTime _tripLastProgressAtUtc = DateTime.UtcNow;
    private static readonly DispatcherTimer _tripProgressTimer = CreateTripProgressTimer();

    private static DispatcherTimer CreateTripProgressTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (Application.Current?.MainWindow is MainWindow window)
                await window.RefreshTripProgressAsync();
        };
        timer.Start();
        return timer;
    }

    private async Task RefreshTripProgressAsync()
    {
        try
        {
            using var response = await _http.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(
                stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;

            UpdateTripRouteHeader(data);
            if (!_tripActive && HasActiveJob(data) && data.CargoLoaded)
                await RecoverTripForProgressAsync(data);
            if (!_tripActive)
            {
                ResetTripProgressUi();
                return;
            }

            var nowUtc = DateTime.UtcNow;
            if (_tripLastFuelLiters <= 0) _tripLastFuelLiters = data.FuelLiters;
            var fuelDelta = _tripLastFuelLiters - data.FuelLiters;
            if (fuelDelta > 0.05f && fuelDelta < 20f && Math.Abs(data.SpeedKph) > 0.5f)
                _tripFuelConsumedL += fuelDelta;
            _tripLastFuelLiters = data.FuelLiters;

            var elapsed = nowUtc - _tripStartedAtUtc;
            var liveDistance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            var distance = Math.Max(_tripDistanceKm, liveDistance);
            _tripDistanceKm = distance;
            var planned = GetTripPlannedDistanceKm(data, distance);
            var remaining = planned > 0 ? Math.Max(0f, planned - distance) : 0f;
            var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;

            if (!data.GamePaused && Math.Abs(data.SpeedKph) > 0.5f)
            {
                var delta = (nowUtc - _tripLastProgressAtUtc).TotalSeconds;
                if (delta > 0 && delta < 10) _tripMovingSeconds += delta;
            }
            _tripLastProgressAtUtc = nowUtc;
            SaveSessionState();

            TripProgressText.Text = planned > 0 ? $"{progress * 100:0}%" : "—";
            TripProgressText2.Text = TripProgressText.Text;
            TripDistanceLiveText.Text = planned > 0 ? $"{distance:0.0} / {planned:0} km" : "— / — km";
            TripDistanceLiveText2.Text = TripDistanceLiveText.Text;
            TripRemainingText.Text = planned > 0 || remaining > 0 ? $"{remaining:0.0} km restantes" : "distância restante indisponível";
            TripRemainingText2.Text = TripRemainingText.Text;
            TripStartText.Text = _tripStartedAtUtc.ToLocalTime().ToString("HH:mm");
            TripDrivingTimeText.Text = FormatDuration(TimeSpan.FromSeconds(_tripMovingSeconds));
            TripLiveText.Text = "MONITORAMENTO ATIVO";
            TripLiveText.Foreground = FindResource("Green") as System.Windows.Media.Brush;

            if (TripProgressTrack.ActualWidth > 0)
            {
                var trackWidth = TripProgressTrack.ActualWidth;
                TripProgressFill.Width = trackWidth * progress;
                var truckLeft = progress <= 0f
                    ? -9d
                    : Math.Min(trackWidth - 20d, Math.Max(0d, trackWidth * progress - 10d));
                TripTruckText.Margin = new Thickness(truckLeft, 0, 0, 0);
            }

            var movingSpeed = Math.Abs(data.SpeedKph);
            var averageSpeed = _tripMovingSeconds > 30 && distance > 0.5f
                ? distance / (float)(_tripMovingSeconds / 3600d)
                : movingSpeed;

            if (remaining <= 0.1f && planned > 0)
            {
                TripArrivalText.Text = "Destino alcançado";
                TripEtaText.Text = "0 min";
                TripEstimateNoteText.Text = "Distância planejada concluída.";
                return;
            }

            var etaSpeed = averageSpeed >= 5f ? averageSpeed : 0f;
            var currentStopSeconds = 0d;
            if (_tachActive != null && !string.Equals(_tachActive.Type, TachDriving, StringComparison.OrdinalIgnoreCase))
                currentStopSeconds = Math.Max(0d, (DateTime.UtcNow - _tachActive.StartedAtUtc).TotalSeconds);

            var etaSeconds = etaSpeed > 0 && remaining > 0.1f
                ? remaining / etaSpeed * 3600d + currentStopSeconds
                : 0d;

            if (etaSeconds <= 0d)
            {
                TripArrivalText.Text = "Calculando…";
                TripEtaText.Text = averageSpeed < 5f ? "aguardando" : "—";
                TripEstimateNoteText.Text = "Aguardando tempo de rota/velocidade real para estabilizar a estimativa.";
                return;
            }

            var arrival = DateTime.UtcNow.AddSeconds(etaSeconds).ToLocalTime();
            TripArrivalText.Text = $"{arrival:HH:mm} • {arrival:dd/MM}";
            TripEtaText.Text = FormatTripEta(etaSeconds);
            TripEstimateNoteText.Text = currentStopSeconds > 0
                ? $"ETA considera {FormatTripEta(currentStopSeconds)} de parada atual ({TachLabel(_tachActive?.Type ?? TachWait)})."
                : averageSpeed >= 5f
                    ? "ETA pela distância restante + velocidade real"
                    : "Aguardando velocidade real para calcular a chegada.";
        }
        catch { }
    }

    private void UpdateTripRouteHeader(TelemetrySnapshot data)
    {
        if (!_tripActive && HasActiveJob(data))
        {
            if (!string.IsNullOrWhiteSpace(data.SourceCity)) _tripRouteOrigin = data.SourceCity;
            if (!string.IsNullOrWhiteSpace(data.DestinationCity)) _tripRouteDestination = data.DestinationCity;
            if (!string.IsNullOrWhiteSpace(data.SourceCompany)) _tripRouteOriginCompany = data.SourceCompany;
            if (!string.IsNullOrWhiteSpace(data.DestinationCompany)) _tripRouteDestinationCompany = data.DestinationCompany;
            if (!string.IsNullOrWhiteSpace(data.Cargo))
            {
                _tripCargo = data.Cargo;
                _ = DiscoverCargoMarketAsync(data.Cargo);
            }
            if (data.CargoValueBrl.HasValue) _tripCargoValue = data.CargoValueBrl;
        }

        if (!string.IsNullOrWhiteSpace(data.SourceCity)) _tripRouteOrigin = data.SourceCity;
        if (!string.IsNullOrWhiteSpace(data.DestinationCity)) _tripRouteDestination = data.DestinationCity;
        if (!string.IsNullOrWhiteSpace(data.SourceCompany)) _tripRouteOriginCompany = data.SourceCompany;
        if (!string.IsNullOrWhiteSpace(data.DestinationCompany)) _tripRouteDestinationCompany = data.DestinationCompany;
        if (!string.IsNullOrWhiteSpace(data.Cargo))
        {
            _tripCargo = data.Cargo;
            _ = DiscoverCargoMarketAsync(data.Cargo);
        }
        if (data.CargoValueBrl.HasValue) _tripCargoValue = data.CargoValueBrl;

        TripOriginText.Text = string.IsNullOrWhiteSpace(_tripRouteOrigin) ? "Origem não informada" : _tripRouteOrigin;
        TripDestinationText.Text = string.IsNullOrWhiteSpace(_tripRouteDestination) ? "Destino não informado" : _tripRouteDestination;
        TripOriginCompanyText.Text = string.IsNullOrWhiteSpace(_tripRouteOriginCompany) ? "Empresa de origem —" : _tripRouteOriginCompany;
        TripDestinationCompanyText.Text = string.IsNullOrWhiteSpace(_tripRouteDestinationCompany) ? "Empresa de destino —" : _tripRouteDestinationCompany;
        TripCargoText.Text = string.IsNullOrWhiteSpace(_tripCargo) ? "Nenhuma carga ativa" : _tripCargo;
        TripValueText.Text = _tripCargoValue.HasValue ? $"R$ {_tripCargoValue.Value:N0}" : "—";

        if (!_tripActive && HasActiveJob(data) && data.CargoLoaded)
        {
            TripLiveText.Text = "MONITORAMENTO ATIVO";
            TripLiveText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        }
    }

    private float GetTripPlannedDistanceKm(TelemetrySnapshot data, float distance)
    {
        if (_tripPlannedDistanceKm > 0) return _tripPlannedDistanceKm;
        if (data.PlannedDistanceKm > 0) _tripPlannedDistanceKm = data.PlannedDistanceKm;
        else if (data.RouteDistanceKm > 0) _tripPlannedDistanceKm = Math.Max(1f, distance + data.RouteDistanceKm);
        return _tripPlannedDistanceKm;
    }

    private async Task RecoverTripForProgressAsync(TelemetrySnapshot data)
    {
        try
        {
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token)) return;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trips", out var trips)) return;

            foreach (var trip in trips.EnumerateArray())
            {
                if (!trip.TryGetProperty("status", out var status) ||
                    !string.Equals(status.GetString(), "active", StringComparison.OrdinalIgnoreCase))
                    continue;

                var cargo = trip.TryGetProperty("cargo", out var c) ? c.GetString() : null;
                if (!string.IsNullOrWhiteSpace(data.Cargo) &&
                    !string.Equals(cargo, data.Cargo, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!trip.TryGetProperty("id", out var idEl)) continue;
                var id = idEl.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;

                _serverTripId = id;
                _tripActive = true;

                if (string.IsNullOrWhiteSpace(_localTripId) && LocalData.Current is { } localStore)
                {
                    _localTripId = "local-" + Guid.NewGuid().ToString("N");
                    _localTripRatePerKm = new LocalTripRepository(localStore.Db).ResolveRatePerKm(data.Cargo);
                    new LocalTripRepository(localStore.Db).StartTrip(_localTripId, data, _serverTripId, _localTripRatePerKm);
                    new LocalTelemetryRepository(localStore.Db).Append(_localTripId, data);
                    _lastLocalTelemetrySavedAtUtc = DateTime.UtcNow;
                }
                _tripStartedAtUtc =
                    trip.TryGetProperty("started_at", out var st) &&
                    DateTime.TryParse(st.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                        ? parsed.ToUniversalTime()
                        : DateTime.UtcNow;

                var distance = 0f;
                try
                {
                    using var pReq = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips/{id}/economy-preview");
                    pReq.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                    pReq.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
                    using var pResp = await _http.SendAsync(pReq);
                    if (pResp.IsSuccessStatusCode)
                    {
                        using var pDoc = JsonDocument.Parse(await pResp.Content.ReadAsStringAsync());
                        if (pDoc.RootElement.TryGetProperty("preview", out var p) &&
                            p.TryGetProperty("distanceKm", out var d))
                            distance = Math.Max(0, d.GetSingle());
                    }
                }
                catch { }

                _tripStartOdometer = Math.Max(0, data.OdometerKm - distance);
                _tripDistanceKm = Math.Max(0, distance);
                _tripStartFuel = data.FuelLiters;
                _tripLastFuelLiters = data.FuelLiters;
                _tripFuelConsumedL = 0;
                _tripPlannedDistanceKm = distance > 0 ? Math.Max(distance, _tripPlannedDistanceKm) : _tripPlannedDistanceKm;
                _jobMissingTicks = 0;
                TripStatusText.Text = "VIAGEM EM ANDAMENTO • TELEMETRIA RECUPERADA";
                TripRouteText.Text = BuildRoute(data);
                TripCargoText.Text = string.IsNullOrWhiteSpace(_tripCargo) ? "Carga não informada" : _tripCargo;
                return;
            }
        }
        catch { }
    }

    private void ResetTripProgressUi()
    {
        _tripPlannedDistanceKm = 0;
        _tripMovingSeconds = 0;
        _tripDistanceKm = 0;
        _tripFuelConsumedL = 0;
        _tripLastFuelLiters = 0;
        _tripLastProgressAtUtc = DateTime.UtcNow;
        _tripRouteOrigin = null;
        _tripRouteDestination = null;
        _tripRouteOriginCompany = null;
        _tripRouteDestinationCompany = null;
        _tripCargo = null;
        _tripCargoValue = null;
        TripProgressText.Text = "0%";
        TripProgressFill.Width = 0;
        TripProgressFill2.Width = 0;
        TripTruckText.Margin = new Thickness(-9, 0, 0, 0);
        TripTruckText2.Margin = new Thickness(-9, 0, 0, 0);
        TripDistanceLiveText.Text = "0 / 0 km";
        TripRemainingText.Text = "— km restantes";
        TripStartText.Text = "—";
        TripArrivalText.Text = "Aguardando saída";
        TripEtaText.Text = "—";
        TripEstimateNoteText.Text = "A estimativa será calculada assim que a viagem começar.";
        TripDrivingTimeText.Text = "00:00:00";
        TripLiveText.Text = "MONITORAMENTO ATIVO";
        TripLiveText.Foreground = (System.Windows.Media.Brush)FindResource("TextMuted");
        TripValueText.Text = "—";
    }

    private static string FormatTripEta(double seconds)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(seconds / 60d));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}h {minutes:00}min" : $"{minutes}min";
    }
}