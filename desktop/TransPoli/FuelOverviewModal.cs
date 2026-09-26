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
        var recent = _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(5).ToList();
        var pendingRefuel = _pendingRefuelTelemetry is not null && _pendingRefuelLiters > 0;
        var latest = recent.FirstOrDefault();
        panel.Children.Add(ModalHero(
            "CENTRAL DE ABASTECIMENTOS",
            "Comprovantes e custos de combustível",
            "Abastecimentos nascem de eventos físicos detectados pelo ETS2, são persistidos localmente e seguem para o Banco pela sincronização durável.",
            pendingRefuel ? $"{_pendingRefuelLiters:0.0} L PENDENTES" : recent.Count > 0 ? $"{recent.Count} RECENTES" : "SEM REGISTROS",
            pendingRefuel ? "Yellow" : "GoldBright"));

        var summary = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        summary.Children.Add(MiniCard("ÚLTIMO ABAST.", latest is null ? "—" : $"{latest.Liters:0.0} L"));
        summary.Children.Add(MiniCard("ÚLTIMO POSTO", latest is null || string.IsNullOrWhiteSpace(latest.Station) ? "—" : latest.Station));
        summary.Children.Add(MiniCard("EVENTO PENDENTE", pendingRefuel ? $"{_pendingRefuelLiters:0.0} L" : "NENHUM"));
        panel.Children.Add(summary);

        if (pendingRefuel)
            panel.Children.Add(ModalStatePanel("ABASTECIMENTO DETECTADO", "Confirmação comercial pendente", "Os litros já foram detectados pela telemetria. Informe preço por litro e posto para emitir o comprovante e registrar o custo sem duplicar o evento físico.", "Yellow"));
        else if (telemetry is null || !telemetry.Connected)
            panel.Children.Add(ModalStatePanel("ETS2 OFFLINE", "Histórico continua disponível", "Novos abastecimentos dependem da detecção física pela telemetria. Os comprovantes já persistidos continuam disponíveis localmente.", "Muted"));
        else
            panel.Children.Add(ModalStatusStrip("✓ DETECÇÃO DE ABASTECIMENTO ATIVA • NENHUM EVENTO AGUARDANDO CONFIRMAÇÃO", "Green"));

        panel.Children.Add(ModalSectionTitle("COMPROVANTES RECENTES", $"{recent.Count} REGISTROS"));
        if (recent.Count == 0)
        {
            panel.Children.Add(ModalStatePanel(
                "HISTÓRICO DE COMBUSTÍVEL",
                "Nenhum abastecimento registrado",
                "Os abastecimentos confirmados aparecerão aqui com litros, posto, odômetro e horário.",
                "Muted"));
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

        var confirm = ModalButton(pendingRefuel ? "⛽ CONFIRMAR ABASTECIMENTO DETECTADO" : "⛽ VERIFICAR ABASTECIMENTO PENDENTE");
        confirm.Click += (_, e) =>
        {
            e.Handled = true;
            ShowFuelPaymentModalC();
        };
        panel.Children.Add(confirm);

        ShowModalContent("fuel-overview", BuildModalCard(
            "⛽ CENTRAL DE ABASTECIMENTOS",
            panel,
            "Evento físico ETS2 • comprovante local • custo da viagem • sincronização com o Banco"));
    }

    private static double EstimateTankCapacity(TelemetrySnapshot data)
    {
        if (data.FuelLiters <= 0 || data.FuelRangeKm <= 0 || data.FuelAvgConsumption <= 0)
            return Math.Max(1, data.FuelLiters * 1.25);
        var estimated = data.FuelRangeKm * data.FuelAvgConsumption / 100d;
        return Math.Max(data.FuelLiters, estimated);
    }
}
