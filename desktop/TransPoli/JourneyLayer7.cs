using System;

namespace TransPoli;

public partial class MainWindow
{
    /// <summary>
    /// Camada 7 — Jornada: consolida no cockpit os dados que já vêm do Connector
    /// e dos módulos operacionais, sem criar telemetria artificial.
    /// </summary>
    private void UpdateJourneyLayer7(TelemetrySnapshot data)
    {
        try
        {
            var moving = _tripActive && !data.GamePaused && Math.Abs(data.SpeedKph) > 0.5f;
            var tripElapsed = _tripActive
                ? DateTime.UtcNow - _tripStartedAtUtc
                : TimeSpan.Zero;

            if (_tripActive)
            {
                if (tripElapsed < TimeSpan.Zero) tripElapsed = TimeSpan.Zero;

                // Mantém os indicadores de jornada alinhados ao mesmo relógio UTC
                // usado pelo registro da viagem e pelo estado persistido.
                TripDrivingTimeText.Text = FormatDuration(TimeSpan.FromSeconds(_tripMovingSeconds));
                DashboardTachDurationText.Text = FormatDuration(tripElapsed);
                DashboardTachTripTimeText.Text = FormatDuration(tripElapsed);

                var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
                if (data.FuelAvgConsumption > 0)
                    FuelConsumptionText.Text = $"Média do caminhão: {data.FuelAvgConsumption:0.00} L/100 km";
                else if (_tripFuelConsumedL > 0.05f && distance > 0.5f)
                    FuelConsumptionText.Text = $"Consumo da viagem: {_tripFuelConsumedL / distance * 100f:0.00} L/100 km";
                else
                    FuelConsumptionText.Text = "Consumo da viagem: aguardando dados";

                DashboardTachStateText.Text = data.GamePaused
                    ? "JOGO PAUSADO"
                    : moving
                        ? "EM MOVIMENTO"
                        : "PARADO";
                DashboardTachSpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";
            }
            else
            {
                DashboardTachDurationText.Text = "00:00";
                DashboardTachTripTimeText.Text = "00:00";
                DashboardTachStateText.Text = data.GamePaused ? "JOGO PAUSADO" : "AGUARDANDO";
                DashboardTachSpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";
            }

            // A manutenção entra na jornada como alerta operacional, mas nunca
            // substitui alertas de segurança já produzidos pelo módulo de operações.
            var maxWear = Math.Max(
                Math.Max(data.WearEngine, data.WearTransmission),
                Math.Max(Math.Max(data.WearCabin, data.WearChassis), data.WearWheels));

            if (_tripActive && maxWear >= 0.75f &&
                string.IsNullOrWhiteSpace(_garageMessage) &&
                !data.FuelWarning &&
                !data.AirPressureEmergency)
            {
                AlertText.Text = "🔧 MANUTENÇÃO CRÍTICA • desgaste ≥ 75% detectado";
                AlertText.Foreground = FindResource("Red") as System.Windows.Media.Brush;
            }
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("JourneyLayer7", ex);
        }
    }
}
