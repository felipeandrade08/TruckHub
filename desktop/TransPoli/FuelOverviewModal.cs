using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal async void ShowFuelOverviewModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("fuel-overview", BuildModalLoading("CARREGANDO COMBUSTÍVEL..."));

        TelemetrySnapshot? telemetry = null;
        try { telemetry = await LoadCurrentTelemetryAsync(); } catch { }

        var panel = new StackPanel();
        panel.Children.Add(ModalHero("CENTRAL DE COMBUSTÍVEL", "Gestão de autonomia e consumo", "Leitura direta da telemetria ETS2 • histórico local • lançamentos integrados à economia TransPoli.", telemetry != null && telemetry.Connected ? $"{telemetry.FuelLiters:0.0} L" : "ETS2 OFFLINE", telemetry != null && telemetry.Connected ? "GoldBright" : "Yellow"));

        if (telemetry != null && telemetry.Connected)
        {
            var hero = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tank = new StackPanel();
            tank.Children.Add(new TextBlock
            {
                Text = "NÍVEL ATUAL DO TANQUE",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Muted") as Brush
            });
            tank.Children.Add(new TextBlock
            {
                Text = $"{telemetry.FuelLiters:0.0} L",
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("GoldBright") as Brush,
                Margin = new Thickness(0, 3, 0, 0)
            });
            tank.Children.Add(new TextBlock
            {
                Text = telemetry.FuelRangeKm > 0 ? $"Autonomia estimada: {telemetry.FuelRangeKm:0} km" : "Autonomia indisponível",
                FontSize = 11,
                Foreground = FindResource("Text") as Brush,
                Margin = new Thickness(0, 2, 0, 0)
            });
            var rangeBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = Math.Max(1, EstimateTankCapacity(telemetry)),
                Value = Math.Clamp(telemetry.FuelLiters, 0, Math.Max(1, EstimateTankCapacity(telemetry))),
                Height = 8,
                Margin = new Thickness(0, 10, 24, 0)
            };
            tank.Children.Add(rangeBar);
            Grid.SetColumn(tank, 0);
            hero.Children.Add(tank);

            var metrics = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            metrics.Children.Add(ModalValueRow("Média do caminhão", telemetry.FuelAvgConsumption > 0 ? $"{telemetry.FuelAvgConsumption:0.00} L/100 km" : "Aguardando dados"));
            var tripAverage = _tripActive && _tripDistanceKm > 0.5f && _tripFuelConsumedL > 0 ? (double)_tripFuelConsumedL / _tripDistanceKm * 100d : _drivingAnalytics.CurrentTripConsumptionL100;
            if (tripAverage.HasValue && telemetry.FuelAvgConsumption > 0)
            {
                var deltaPct = (tripAverage.Value - telemetry.FuelAvgConsumption) / telemetry.FuelAvgConsumption * 100d;
                var comparison = Math.Abs(deltaPct) < 0.5d
                    ? "Na média do caminhão"
                    : deltaPct > 0
                        ? $"Gastando {deltaPct:0.0}% mais que a média"
                        : $"Gastando {Math.Abs(deltaPct):0.0}% menos que a média";
                metrics.Children.Add(ModalValueRow("Viagem atual", $"{tripAverage.Value:0.00} L/100 km • {comparison}"));
            }
            else
            {
                metrics.Children.Add(ModalValueRow("Viagem atual", "Aguardando distância e consumo"));
            }
            metrics.Children.Add(ModalValueRow("Autonomia", telemetry.FuelRangeKm > 0 ? $"{telemetry.FuelRangeKm:0} km" : "—"));
            metrics.Children.Add(ModalValueRow("AdBlue", telemetry.AdBlueLiters > 0 ? $"{telemetry.AdBlueLiters:0.0} L" : "—"));
            metrics.Children.Add(ModalValueRow("Odômetro", $"{telemetry.OdometerKm:0.0} km"));
            Grid.SetColumn(metrics, 1);
            hero.Children.Add(metrics);

            panel.Children.Add(ModalPanel(hero));

            var warning = telemetry.FuelWarning || (telemetry.FuelRangeKm > 0 && telemetry.FuelRangeKm < 80);
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = warning
                    ? "⚠ COMBUSTÍVEL EM ATENÇÃO • planeje o próximo abastecimento antes de ficar sem autonomia."
                    : "✓ COMBUSTÍVEL EM OPERAÇÃO NORMAL • os litros, autonomia e consumo são lidos diretamente da telemetria.",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource(warning ? "Yellow" : "Green") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }
        else
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "ETS2 não está conectado. Assim que o plugin enviar telemetria, o TransPoli preencherá automaticamente litros, consumo e autonomia.",
                FontSize = 13,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        var recent = _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(5).ToList();
        panel.Children.Add(ModalSectionTitle("ÚLTIMOS ABASTECIMENTOS", $"{recent.Count} REGISTROS"));
        if (recent.Count == 0)
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Nenhum abastecimento confirmado neste computador ainda.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush
            }));
        }
        else
        {
            foreach (var item in recent)
            {
                var card = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var left = new StackPanel();
                left.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(item.Station) ? "Posto não informado" : item.Station,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Text") as Brush
                });
                left.Children.Add(new TextBlock
                {
                    Text = $"{item.RecordedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} • {item.Location}",
                    FontSize = 10,
                    Foreground = FindResource("Muted") as Brush
                });
                Grid.SetColumn(left, 0);
                card.Children.Add(left);

                var mid = new TextBlock
                {
                    Text = $"{item.Liters:0.0} L",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("GoldBright") as Brush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(mid, 1);
                card.Children.Add(mid);

                var od = new TextBlock
                {
                    Text = $"{item.OdometerKm:0.0} km",
                    FontSize = 10,
                    Foreground = FindResource("Muted") as Brush,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(od, 2);
                card.Children.Add(od);

                panel.Children.Add(ModalPanel(card));
            }
        }

        var confirm = ModalButton("⛽ REGISTRAR ABASTECIMENTO MANUAL");
        confirm.Click += (_, e) =>
        {
            e.Handled = true;
            ShowFuelPaymentModalC();
        };
        panel.Children.Add(confirm);

        ShowModalContent("fuel-overview", BuildModalCard(
            "⛽ CENTRAL DE COMBUSTÍVEL",
            panel,
            "Telemetria em tempo real • histórico local • lançamento financeiro automático após confirmação"));
    }

    private static double EstimateTankCapacity(TelemetrySnapshot data)
    {
        if (data.FuelLiters <= 0 || data.FuelRangeKm <= 0 || data.FuelAvgConsumption <= 0)
            return Math.Max(1, data.FuelLiters * 1.25);
        var estimated = data.FuelRangeKm * data.FuelAvgConsumption / 100d;
        return Math.Max(data.FuelLiters, estimated);
    }
}
