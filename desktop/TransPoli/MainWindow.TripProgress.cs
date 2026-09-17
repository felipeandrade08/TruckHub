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
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected || !_tripActive)
            {
                ResetTripProgressUi();
                return;
            }

            var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            var planned = data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : data.RouteDistanceKm > 0 ? distance + data.RouteDistanceKm : 0;
            var remaining = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : planned > 0 ? Math.Max(0f, planned - distance) : 0f;
            var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;

            TripProgressText.Text = planned > 0 ? $"{progress * 100:0}%" : "—";
            TripRemainingText.Text = planned > 0 || remaining > 0 ? $"{remaining:0.0} km restantes" : "distância restante indisponível";
            TripStartText.Text = _tripStartedAtUtc.ToLocalTime().ToString("HH:mm");
            TripLiveText.Text = data.GamePaused ? "JOGO PAUSADO" : "AO VIVO";

            if (TripProgressFill.Parent is Grid progressGrid && progressGrid.ActualWidth > 0)
            {
                TripProgressFill.Width = progressGrid.ActualWidth * progress;
                TripTruckText.Margin = new Thickness(Math.Max(-10, TripProgressFill.Width - 10), 0, 0, 0);
            }

            var averageSpeed = elapsed.TotalHours > 0.008 && distance > 0.5f
                ? distance / (float)elapsed.TotalHours
                : Math.Abs(data.SpeedKph);

            if (remaining <= 0.1f && planned > 0)
            {
                TripArrivalText.Text = "Destino alcançado";
                TripEtaText.Text = "0 min";
                TripEstimateNoteText.Text = "Distância planejada concluída.";
                return;
            }

            if (averageSpeed < 5f || remaining <= 0)
            {
                TripArrivalText.Text = "Calculando…";
                TripEtaText.Text = averageSpeed < 5f ? "aguardando movimento" : "—";
                TripEstimateNoteText.Text = "Aguardando distância real para estabilizar a estimativa. O tempo parado também entra no cálculo.";
                return;
            }

            var etaSeconds = remaining / averageSpeed * 3600d;
            var arrival = DateTime.UtcNow.AddSeconds(etaSeconds).ToLocalTime();
            TripArrivalText.Text = $"{arrival:HH:mm} • {arrival:dd/MM}";
            TripEtaText.Text = FormatTripEta(etaSeconds);
            TripEstimateNoteText.Text = $"ETA real: média de {averageSpeed:0.0} km/h. Paradas aumentam o tempo decorrido e empurram a chegada.";
        }
        catch
        {
            // A telemetria principal continua funcionando mesmo se o cálculo visual falhar.
        }
    }

    private void ResetTripProgressUi()
    {
        TripProgressText.Text = "0%";
        TripProgressFill.Width = 0;
        TripTruckText.Margin = new Thickness(-10, 0, 0, 0);
        TripRemainingText.Text = "— km restantes";
        TripStartText.Text = "—";
        TripArrivalText.Text = "Calculando…";
        TripEtaText.Text = "Calculando…";
        TripEstimateNoteText.Text = "Estimativa baseada na telemetria real da viagem.";
        TripLiveText.Text = "OFFLINE";
    }

    private static string FormatTripEta(double seconds)
    {
        var totalMinutes = Math.Max(0, (int)Math.Round(seconds / 60d));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}h {minutes:00}min" : $"{minutes}min";
    }
}
