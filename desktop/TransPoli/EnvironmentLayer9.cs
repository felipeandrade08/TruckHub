using System;

namespace TransPoli;

public partial class MainWindow
{
    /// <summary>
    /// Camada 9 — Ambiente.
    /// Usa somente sinais que já existem no Connector: horário absoluto do ETS2,
    /// limpadores e iluminação. Chuva, neblina e temperatura ambiente não são
    /// expostas pelo snapshot atual e permanecem como N/D.
    /// </summary>
    private void UpdateEnvironmentLayer9(TelemetrySnapshot data)
    {
        try
        {
            if (data is null || !data.Connected)
            {
                EnvironmentStateText.Text = "AMBIENTE N/D";
                EnvironmentDetailText.Text = "Aguardando telemetria";
                EnvironmentWeatherText.Text = "CLIMA N/D";
                EnvironmentIconText.Text = "◌";
                EnvironmentStateText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
                EnvironmentIconText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
                return;
            }

            var minutes = (int)(data.TimeAbsMinutes % 1440u);
            var hour = minutes / 60;
            var minute = minutes % 60;

            var period = hour >= 6 && hour < 12
                ? "MANHÃ"
                : hour >= 12 && hour < 18
                    ? "TARDE"
                    : hour >= 18 && hour < 22
                        ? "NOITE"
                        : "MADRUGADA";

            EnvironmentStateText.Text = period;
            EnvironmentDetailText.Text = $"ETS2 • {hour:00}:{minute:00}";

            // Não inferimos chuva a partir dos limpadores: o comando pode ser manual.
            // O mesmo vale para iluminação, que não é uma prova de condição climática.
            EnvironmentWeatherText.Text = "CLIMA N/D";

            if (data.Wipers)
            {
                EnvironmentIconText.Text = "▥";
                EnvironmentIconText.Foreground = FindResource("GoldBright") as System.Windows.Media.Brush;
                EnvironmentWeatherText.Text = "LIMPADORES ATIVOS";
            }
            else if (data.LightsBeamHigh || data.LightsBeamLow || data.LightsParking)
            {
                EnvironmentIconText.Text = hour >= 18 || hour < 6 ? "☾" : "◐";
                EnvironmentIconText.Foreground = FindResource("GoldBright") as System.Windows.Media.Brush;
            }
            else
            {
                EnvironmentIconText.Text = hour >= 18 || hour < 6 ? "☾" : "☀";
                EnvironmentIconText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
            }

            EnvironmentStateText.Foreground = FindResource("TextMain") as System.Windows.Media.Brush;
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("EnvironmentLayer9", ex);
            EnvironmentStateText.Text = "AMBIENTE N/D";
            EnvironmentDetailText.Text = "Leitura indisponível";
            EnvironmentWeatherText.Text = "CLIMA N/D";
            EnvironmentIconText.Text = "◌";
            EnvironmentStateText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            EnvironmentIconText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        }
    }
}
