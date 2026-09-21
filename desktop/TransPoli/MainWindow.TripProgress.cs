using System;

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

    private static string FormatTripEta(double seconds)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(seconds / 60d));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}h {minutes:00}min" : $"{minutes}min";
    }
}