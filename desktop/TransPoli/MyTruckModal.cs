using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

/// <summary>
/// Etapa 11 — Meu Caminhão.
/// Visão local-first do veículo atual, alimentada diretamente pela última
/// telemetria recebida. A tela não depende da API para abrir e continua
/// funcional offline.
/// </summary>
public partial class MainWindow
{
    internal async void ShowMyTruckModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("my-truck", BuildModalLoading("🚛 CARREGANDO MEU CAMINHÃO..."));

        TelemetrySnapshot? data = null;
        try { data = LastTelemetry ?? await LoadCurrentTelemetryAsync(); } catch { }

        var body = new StackPanel();

        if (data is null || !data.Connected)
        {
            body.Children.Add(ModalPanel(new TextBlock
            {
                Text = "ETS2 desconectado. Conecte o jogo para o TransPoli preencher automaticamente os dados do seu caminhão.",
                FontSize = 13,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }
        else
        {
            AddTruckHero(body, data);
            AddTruckIdentity(body, data);
            AddTruckPerformance(body, data);
            AddTruckMechanical(body, data);
            AddTruckOperation(body, data);
        }

        var actions = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var maintenance = ModalButton("🔧 MANUTENÇÃO");
        maintenance.Click += async (_, e) =>
        {
            e.Handled = true;
            await ShowMaintenanceTabletModalAsync();
        };
        actions.Children.Add(maintenance);

        var fuel = ModalButton("⛽ COMBUSTÍVEL");
        fuel.Click += (_, e) =>
        {
            e.Handled = true;
            ShowFuelOverviewModal();
        };
        actions.Children.Add(fuel);

        var garage = ModalButton("🔐 GARAGEM E VÍNCULO");
        garage.Click += (_, e) =>
        {
            e.Handled = true;
            ShowGarageTabletModal();
        };
        actions.Children.Add(garage);
        body.Children.Add(actions);

        ShowModalContent("my-truck", BuildModalCard(
            "🚛 MEU CAMINHÃO",
            body,
            "Perfil operacional • telemetria em tempo real • dados locais • funciona offline"));
    }

    private void AddTruckHero(StackPanel body, TelemetrySnapshot data)
    {
        var brand = string.IsNullOrWhiteSpace(data.TruckBrand) ? "Caminhão" : data.TruckBrand.Trim();
        var model = string.IsNullOrWhiteSpace(data.TruckModel) ? "Modelo não informado" : data.TruckModel.Trim();
        var plate = string.IsNullOrWhiteSpace(data.LicensePlate) ? "SEM PLACA" : data.LicensePlate.Trim();

        var hero = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource(_garageUnauthorized ? "Yellow" : "GoldSoft") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = brand.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GoldBright") as Brush
        });
        left.Children.Add(new TextBlock
        {
            Text = model,
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2)
        });
        left.Children.Add(new TextBlock
        {
            Text = $"PLACA • {plate}",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("Muted") as Brush
        });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var status = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        status.Children.Add(new TextBlock
        {
            Text = _garageUnauthorized ? "🔒 NÃO AUTORIZADO" : "● AUTORIZADO",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(_garageUnauthorized ? "Yellow" : "Green") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        status.Children.Add(new TextBlock
        {
            Text = data.EngineEnabled ? "MOTOR LIGADO" : "MOTOR DESLIGADO",
            FontSize = 9,
            Foreground = FindResource("Muted") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetColumn(status, 1);
        grid.Children.Add(status);

        hero.Child = grid;
        body.Children.Add(hero);
    }

    private void AddTruckIdentity(StackPanel body, TelemetrySnapshot data)
    {
        body.Children.Add(ModalLabel("IDENTIFICAÇÃO DO CAMINHÃO"));
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(MiniCard("MARCA", string.IsNullOrWhiteSpace(data.TruckBrand) ? "—" : data.TruckBrand));
        grid.Children.Add(MiniCard("MODELO", string.IsNullOrWhiteSpace(data.TruckModel) ? "—" : data.TruckModel));
        grid.Children.Add(MiniCard("PLACA", string.IsNullOrWhiteSpace(data.LicensePlate) ? "Sem placa" : data.LicensePlate));
        grid.Children.Add(MiniCard("ID", string.IsNullOrWhiteSpace(data.TruckId) ? "Não informado" : data.TruckId));
        body.Children.Add(grid);
    }

    private void AddTruckPerformance(StackPanel body, TelemetrySnapshot data)
    {
        body.Children.Add(ModalLabel("DESEMPENHO"));
        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(MiniCard("VELOCIDADE", $"{data.SpeedKph:0} km/h"));
        grid.Children.Add(MiniCard("RPM", $"{data.Rpm:0}"));
        grid.Children.Add(MiniCard("MARCHA", data.Gear.ToString()));
        grid.Children.Add(MiniCard("ODÔMETRO", $"{data.OdometerKm:0.0} km"));
        grid.Children.Add(MiniCard("AUTONOMIA", data.FuelRangeKm > 0 ? $"{data.FuelRangeKm:0} km" : "—"));
        grid.Children.Add(MiniCard("CONSUMO", data.FuelAvgConsumption > 0 ? $"{data.FuelAvgConsumption:0.00} L/100 km" : "—"));
        body.Children.Add(grid);
    }

    private void AddTruckMechanical(StackPanel body, TelemetrySnapshot data)
    {
        body.Children.Add(ModalLabel("SISTEMAS"));
        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(MiniCard("COMBUSTÍVEL", $"{data.FuelLiters:0.0} L"));
        grid.Children.Add(MiniCard("ADBLUE", data.AdBlueLiters > 0 ? $"{data.AdBlueLiters:0.0} L" : "—"));
        grid.Children.Add(MiniCard("BATERIA", data.BatteryVoltage > 0 ? $"{data.BatteryVoltage:0.0} V" : "—"));
        grid.Children.Add(MiniCard("ÓLEO", data.OilTemperature > 0 ? $"{data.OilTemperature:0} °C" : "—"));
        grid.Children.Add(MiniCard("ÁGUA", data.WaterTemperature > 0 ? $"{data.WaterTemperature:0} °C" : "—"));
        grid.Children.Add(MiniCard("MOTOR", data.EngineEnabled ? "LIGADO" : "DESLIGADO"));
        body.Children.Add(grid);

        var maxWear = Math.Max(Math.Max(data.WearEngine, data.WearTransmission),
            Math.Max(Math.Max(data.WearCabin, data.WearChassis), data.WearWheels));
        var wearText = maxWear >= .75f
            ? "🔴 MANUTENÇÃO CRÍTICA • desgaste elevado detectado."
            : maxWear >= .50f
                ? "🟡 ATENÇÃO • há desgaste que merece manutenção preventiva."
                : "🟢 SISTEMAS EM FAIXA NORMAL • continue acompanhando o desgaste.";

        body.Children.Add(ModalPanel(new TextBlock
        {
            Text = wearText,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(maxWear >= .75f ? "Red" : maxWear >= .50f ? "Yellow" : "Green") as Brush,
            TextWrapping = TextWrapping.Wrap
        }));
    }

    private void AddTruckOperation(StackPanel body, TelemetrySnapshot data)
    {
        body.Children.Add(ModalLabel("ESTADO OPERACIONAL"));
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(MiniCard("TELEMETRIA", data.Connected ? "ONLINE" : "OFFLINE"));
        grid.Children.Add(MiniCard("ETS2", string.IsNullOrWhiteSpace(data.Game) ? "ETS2" : data.Game));
        grid.Children.Add(MiniCard("CARGA", data.CargoLoaded ? "CARREGADA" : "SEM CARGA"));
        grid.Children.Add(MiniCard("CRUISE", data.CruiseControl ? $"{data.CruiseSpeedKph:0} km/h" : "DESLIGADO"));
        body.Children.Add(grid);

        var status = data.GamePaused ? "JOGO PAUSADO" :
            data.OnJob && data.CargoLoaded ? "EM VIAGEM" :
            data.OnJob ? "VIAGEM DETECTADA" : "DISPONÍVEL";

        body.Children.Add(ModalValueRow("Status operacional", status));
    }
}
