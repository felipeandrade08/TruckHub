using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TransPoli.GameSave;

namespace TransPoli;

/// <summary>
/// Etapa 11 — Meu Caminhão.
/// Visão local-first do veículo atual, alimentada diretamente pela última
/// telemetria recebida. A tela não depende da API para abrir e continua
/// funcional offline.
/// </summary>
public partial class MainWindow
{
    private readonly GameSaveIntegration _gameSaveIntegration = new();
    internal async void ShowMyTruckModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("my-truck", BuildModalLoading("🚛 CARREGANDO MEU CAMINHÃO..."));

        TelemetrySnapshot? data = null;
        try { data = LastTelemetry ?? await LoadCurrentTelemetryAsync(); }
        catch (Exception ex) { App.WriteUiCrashLog("MyTruck.LoadTelemetry", ex); }

        GameSaveSnapshot? save = null;
        try { save = await _gameSaveIntegration.RefreshAsync(); }
        catch (Exception ex) { App.WriteUiCrashLog("MyTruck.RefreshGameSave", ex); }

        var body = new StackPanel();
        var truckTitle = data is not null && data.Connected ? $"{data.TruckBrand} {data.TruckModel}".Trim() : "Aguardando ETS2";
        body.Children.Add(ModalHero("MEU CAMINHÃO", "Prontuário técnico do veículo", "Identidade, saúde, desgaste, manutenção e histórico operacional. Instrumentos de condução permanecem na HUD.", truckTitle, data is not null && data.Connected ? "GoldBright" : "Yellow"));
        body.Children.Add(ModalStatusStrip(data is not null && data.Connected ? (_garageUnauthorized ? "🔒 TELEMETRIA ATIVA • VEÍCULO NÃO AUTORIZADO NA GARAGEM" : "✓ TELEMETRIA ATIVA • VEÍCULO AUTORIZADO • SISTEMAS ONLINE") : "● ETS2 DESCONECTADO • AGUARDANDO TELEMETRIA", data is not null && data.Connected && !_garageUnauthorized ? "Green" : "Yellow"));

        if (data is null || !data.Connected)
        {
            body.Children.Add(ModalStatePanel(
                "VEÍCULO OFFLINE",
                "Sem telemetria do caminhão",
                "Abra o ETS2 e entre no caminhão. A central técnica será preenchida assim que o link de telemetria voltar. Histórico, manutenção e registros locais continuam disponíveis mesmo offline.",
                "Yellow"));
        }
        else
        {
            AddTruckHero(body, data);
            AddTruckIdentity(body, data);
            AddTruckMechanical(body, data);
            AddTruckOperation(body, data);
            AddTruckLocalHistory(body, data);
            AddTruckIntelligentHistory(body, data);
            AddGameSaveTruckDetails(body, save);
        }

        body.Children.Add(ModalSectionTitle("AÇÕES DO VEÍCULO", "MANUTENÇÃO • COMBUSTÍVEL • GARAGEM"));
        var actions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
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



    private void ShowTruckTripHistory(string truckId)
    {
        if(LocalData.Current is not { } store) return;
        var trips=new LocalTripRepository(store.Db).GetTruckTrips(truckId,25);
        var body=new StackPanel();
        body.Children.Add(ModalSectionTitle("VIAGENS FINALIZADAS","SELECIONE UMA OPERAÇÃO PARA ABRIR O DIÁRIO DE BORDO"));
        foreach(var trip in trips)
        {
            var b=ModalButton($"📘 {trip.Cargo} • {trip.Origin} → {trip.Destination} • {trip.DistanceKm:0.0} km • R$ {trip.Net:N2}");
            var id=trip.Id;
            b.Click+=(_,e)=>{e.Handled=true;ShowTripLogbookModal(id);};
            body.Children.Add(b);
        }
        if(trips.Count==0) body.Children.Add(ModalStatusStrip("Nenhuma viagem finalizada registrada para este caminhão.","Yellow"));
        ShowModalContent("truck-history",BuildModalCard("📚 HISTÓRICO DO CAMINHÃO",body,"TransPoli • viagens locais • diário de bordo"));
    }

    private void ShowTripLogbookModal(string tripId)
    {
        if(LocalData.Current is not { } store) return;
        var repo=new LocalTripLogbookRepository(store.Db);
        var historicalSessionKey=repo.GetSessionKey(tripId);
        // A consolidação é idempotente e resolve a identidade pela própria viagem/
        // snapshot congelado. Assim o histórico reflete eventos financeiros e operacionais
        // tardios sem jamais adotar a TripSession que estiver ativa na tela.
        repo.Consolidate(tripId,historicalSessionKey);
        var summary=repo.Get(tripId);
        if(summary is null) return;
        var timeline=repo.GetTimeline(tripId);
        var body=new StackPanel();
        body.Children.Add(ModalHero("DIÁRIO DE BORDO","Registro consolidado da operação",summary.Route,summary.Status,summary.Status=="FINALIZADA"?"Green":"Yellow"));
        var metrics=new UniformGrid{Columns=3};
        metrics.Children.Add(MiniCard("DISTÂNCIA",$"{summary.DistanceKm:0.0} km"));
        metrics.Children.Add(MiniCard("COMBUSTÍVEL",$"{summary.FuelLiters:0.0} L"));
        metrics.Children.Add(MiniCard("RECEITA",$"R$ {summary.Income:N2}"));
        metrics.Children.Add(MiniCard("DESPESAS",$"R$ {summary.Expenses:N2}"));
        metrics.Children.Add(MiniCard("RESULTADO LÍQUIDO",$"R$ {summary.Net:N2}"));
        metrics.Children.Add(MiniCard("EVENTOS",timeline.Count.ToString()));
        body.Children.Add(metrics);
        body.Children.Add(ModalSectionTitle("LINHA DO TEMPO",$"{summary.Cargo} • {summary.Summary}"));
        foreach(var evt in timeline)
        {
            var detail=string.IsNullOrWhiteSpace(evt.Details)?evt.Status:$"{evt.Status} • {evt.Details}";
            body.Children.Add(ModalValueRow(evt.At.ToLocalTime().ToString("dd/MM HH:mm:ss")+ " • "+evt.Type,$"{detail} • {evt.OdometerKm:0.0} km"));
        }
        ShowModalContent("trip-logbook",BuildModalCard("📘 DIÁRIO DE BORDO",body,"TripSession • documentação • eventos • combustível • manutenção • financeiro TransPoli"));
    }

    private void AddTruckIntelligentHistory(StackPanel body, TelemetrySnapshot data)
    {
        if(LocalData.Current is not { } store) return;
        var truckId=string.IsNullOrWhiteSpace(data.TruckId)?data.LicensePlate:data.TruckId;
        if(string.IsNullOrWhiteSpace(truckId)) return;
        var repo=new LocalTripRepository(store.Db);
        var history=repo.GetTruckHistory(truckId);
        var profile=repo.GetTruckOperationalProfile(truckId,data.LicensePlate);
        body.Children.Add(ModalSectionTitle("PRONTUÁRIO INTELIGENTE", "HISTÓRICO DESTE CAMINHÃO"));
        var grid=new UniformGrid{Columns=3};
        grid.Children.Add(MiniCard("VIAGENS",history.Trips.ToString()));
        grid.Children.Add(MiniCard("KM OPERACIONAIS",$"{history.DistanceKm:N0} km"));
        grid.Children.Add(MiniCard("COMBUSTÍVEL CONSUMIDO",$"{history.FuelLiters:N1} L"));
        grid.Children.Add(MiniCard("ABASTECIMENTOS",profile.Refuelings.ToString()));
        grid.Children.Add(MiniCard("LITROS ABASTECIDOS",$"{profile.RefueledLiters:N1} L"));
        grid.Children.Add(MiniCard("CUSTO COMBUSTÍVEL",$"R$ {profile.FuelCost:N2}"));
        grid.Children.Add(MiniCard("MANUTENÇÕES",history.MaintenanceCount.ToString()));
        grid.Children.Add(MiniCard("CUSTO MANUTENÇÃO",$"R$ {history.MaintenanceCost:N2}"));
        grid.Children.Add(MiniCard("OCORRÊNCIAS",profile.Occurrences.ToString()));
        grid.Children.Add(MiniCard("RECEITA GERADA",$"R$ {history.Income:N2}"));
        grid.Children.Add(MiniCard("DESPESAS",$"R$ {history.Expenses:N2}"));
        grid.Children.Add(MiniCard("RESULTADO LÍQUIDO",$"R$ {history.Net:N2}"));
        body.Children.Add(grid);
        var wear=Math.Max(Math.Max(profile.WearEngine,profile.WearTransmission),Math.Max(Math.Max(profile.WearCabin,profile.WearChassis),profile.WearWheels));
        body.Children.Add(ModalStatusStrip($"SAÚDE HISTÓRICA • último desgaste registrado: {wear*100:0.0}% máximo",wear>=.75?"Red":wear>=.5?"Yellow":"Green"));
        var historyButton=ModalButton("📚 ABRIR HISTÓRICO DE VIAGENS");
        historyButton.Click+=(_,e)=>{e.Handled=true;ShowTruckTripHistory(truckId);};
        body.Children.Add(historyButton);
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
            Padding = new Thickness(22, 18, 22, 18),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(new TextBlock
        {
            Text = brand.ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GoldBright") as Brush
        });
        left.Children.Add(new TextBlock
        {
            Text = model,
            FontSize = 27,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2)
        });
        left.Children.Add(new TextBlock
        {
            Text = $"PLACA • {plate}",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("Muted") as Brush
        });
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var status = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        status.Children.Add(new TextBlock
        {
            Text = _garageUnauthorized ? "🔒 NÃO AUTORIZADO" : "● AUTORIZADO",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(_garageUnauthorized ? "Yellow" : "Green") as Brush,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        status.Children.Add(new TextBlock
        {
            Text = data.EngineEnabled ? "MOTOR LIGADO" : "MOTOR DESLIGADO",
            FontSize = 12,
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
        body.Children.Add(ModalSectionTitle("IDENTIFICAÇÃO DO CAMINHÃO", "TELEMETRIA"));
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(MiniCard("MARCA", string.IsNullOrWhiteSpace(data.TruckBrand) ? "—" : data.TruckBrand));
        grid.Children.Add(MiniCard("MODELO", string.IsNullOrWhiteSpace(data.TruckModel) ? "—" : data.TruckModel));
        grid.Children.Add(MiniCard("PLACA", string.IsNullOrWhiteSpace(data.LicensePlate) ? "Sem placa" : data.LicensePlate));
        grid.Children.Add(MiniCard("ID", string.IsNullOrWhiteSpace(data.TruckId) ? "Não informado" : data.TruckId));
        body.Children.Add(grid);
    }

    private void AddTruckPerformance(StackPanel body, TelemetrySnapshot data)
    {
        body.Children.Add(ModalSectionTitle("INSTRUMENTOS DE CONDUÇÃO", "TELEMETRIA EM TEMPO REAL"));
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 2, 0, 4) };
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
        body.Children.Add(ModalSectionTitle("SAÚDE DO VEÍCULO", "MECÂNICA • CONSUMO • DESGASTE"));
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

        var wearGrid = new UniformGrid { Columns = 3 };
        wearGrid.Children.Add(MiniCard("MOTOR", FormatWear(data.WearEngine)));
        wearGrid.Children.Add(MiniCard("CÂMBIO", FormatWear(data.WearTransmission)));
        wearGrid.Children.Add(MiniCard("CABINE", FormatWear(data.WearCabin)));
        wearGrid.Children.Add(MiniCard("CHASSI", FormatWear(data.WearChassis)));
        wearGrid.Children.Add(MiniCard("RODAS", FormatWear(data.WearWheels)));
        body.Children.Add(wearGrid);

        body.Children.Add(ModalStatePanel(
            maxWear >= .75f ? "MANUTENÇÃO CRÍTICA" : maxWear >= .50f ? "ATENÇÃO MECÂNICA" : "SISTEMAS NOMINAIS",
            maxWear >= .75f ? "Intervenção recomendada" : maxWear >= .50f ? "Planeje manutenção preventiva" : "Veículo dentro da faixa operacional",
            wearText.Replace("🔴 ", "").Replace("🟡 ", "").Replace("🟢 ", ""),
            maxWear >= .75f ? "Red" : maxWear >= .50f ? "Yellow" : "Green"));
    }

    private void AddTruckLocalHistory(StackPanel body, TelemetrySnapshot data)
    {
        if (LocalData.Current is not { } store) return;

        try
        {
            using var c = store.Db.Connection.CreateCommand();
            c.CommandText = @"
SELECT
    COUNT(*),
    COALESCE(SUM(distance_km), 0),
    COALESCE(SUM(fuel_consumed_l), 0),
    MAX(finished_at_utc)
FROM trip
WHERE status='finished' AND owner_user_id=@owner
  AND (@truck='' OR truck_id=@truck OR truck_id=@plate);";
            var truck = data.TruckId?.Trim() ?? string.Empty;
            var plate = data.LicensePlate?.Trim() ?? string.Empty;
            c.Parameters.AddWithValue("@truck", truck);
            c.Parameters.AddWithValue("@plate", plate);
            c.Parameters.AddWithValue("@owner", SecureTokenStore.ReadUserId() ?? "");

            using var reader = c.ExecuteReader();
            if (!reader.Read()) return;

            var trips = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var km = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
            var fuel = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
            var last = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

            body.Children.Add(ModalSectionTitle("HISTÓRICO DO CAMINHÃO", "DADOS LOCAIS"));
            var grid = new UniformGrid { Columns = 2 };
            grid.Children.Add(MiniCard("VIAGENS CONCLUÍDAS", trips.ToString("0")));
            grid.Children.Add(MiniCard("DISTÂNCIA REGISTRADA", $"{km:0.0} km"));
            grid.Children.Add(MiniCard("COMBUSTÍVEL CONSUMIDO", $"{fuel:0.0} L"));
            grid.Children.Add(MiniCard("ÚLTIMA VIAGEM", FormatTruckDate(last)));
            body.Children.Add(grid);
        }
        catch
        {
            // A tela continua operacional mesmo se o banco local estiver indisponível.
        }
    }

    private static string FormatTruckDate(string value)
    {
        if (!DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date))
            return "Ainda não registrada";
        return date.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    private static string FormatWear(float value)
    {
        if (value <= 0) return "Sem desgaste";
        var percent = Math.Clamp(value * 100f, 0f, 100f);
        return $"{percent:0}%";
    }

    private void AddTruckOperation(StackPanel body, TelemetrySnapshot data)
    {
        body.Children.Add(ModalSectionTitle("ESTADO OPERACIONAL", "ETS2"));
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
    private void AddGameSaveTruckDetails(StackPanel body, GameSaveSnapshot? save)
    {
        if (save is null) return;

        var truck = save.CurrentTruck;
        if (truck is not null)
        {
            body.Children.Add(SectionHeader("INTEGRIDADE DO VEÍCULO", "Desgaste persistente salvo pelo ETS2"));
            var wearGrid = new UniformGrid { Columns = 5 };
            wearGrid.Children.Add(WearCard("MOTOR", truck.EngineWear, truck.EngineWearUnfixable));
            wearGrid.Children.Add(WearCard("CÂMBIO", truck.TransmissionWear, truck.TransmissionWearUnfixable));
            wearGrid.Children.Add(WearCard("CABINE", truck.CabinWear, truck.CabinWearUnfixable));
            wearGrid.Children.Add(WearCard("CHASSI", truck.ChassisWear, truck.ChassisWearUnfixable));
            wearGrid.Children.Add(WearCard("RODAS", truck.WheelsWear, truck.WheelsWearUnfixable));
            body.Children.Add(wearGrid);
        }

        body.Children.Add(SectionHeader("FROTA", "Resumo rápido do patrimônio salvo"));
        var fleet = new UniformGrid { Columns = 3 };
        fleet.Children.Add(MiniCard("CAMINHÕES", save.Trucks.Count.ToString()));
        fleet.Children.Add(MiniCard("REBOQUES", save.Trailers.Count.ToString()));
        fleet.Children.Add(MiniCard("REBOQUE ATUAL", save.CurrentTrailer is null ? "Não acoplado" :
            string.IsNullOrWhiteSpace(save.CurrentTrailer.LicensePlate) ? "Acoplado" : save.CurrentTrailer.LicensePlate));
        body.Children.Add(fleet);

        if (save.CurrentTrailer is { } trailer)
        {
            var trailerGrid = new UniformGrid { Columns = 3 };
            trailerGrid.Children.Add(MiniCard("CARGA NO REBOQUE", trailer.CargoMassKg > 0 ? $"{trailer.CargoMassKg / 1000.0:0.0} t" : "—"));
            trailerGrid.Children.Add(MiniCard("DANO DA CARGA", $"{Math.Clamp(trailer.CargoDamage * 100.0, 0, 100):0.0}%"));
            trailerGrid.Children.Add(MiniCard("DESGASTE REBOQUE", FormatSaveWear(
                Math.Max(trailer.TrailerBodyWear, Math.Max(trailer.ChassisWear, trailer.WheelsWear)),
                Math.Max(trailer.TrailerBodyWearUnfixable, Math.Max(trailer.ChassisWearUnfixable, trailer.WheelsWearUnfixable)))));
            body.Children.Add(trailerGrid);
        }

        var stats = save.DriverStats;
        body.Children.Add(SectionHeader("HISTÓRICO E ESTATÍSTICAS", "Indicadores persistentes do perfil"));
        var statsGrid = new UniformGrid { Columns = 4 };
        statsGrid.Children.Add(MiniCard("ENTREGAS", save.DeliveryHistory.Count.ToString()));
        statsGrid.Children.Add(MiniCard("CIDADES", stats.VisitedCities.ToString()));
        statsGrid.Children.Add(MiniCard("TIPOS DE CARGA", save.TransportedCargoTypes.Count.ToString()));
        statsGrid.Children.Add(MiniCard("XP", stats.ExperiencePoints.ToString("N0")));
        statsGrid.Children.Add(MiniCard("POSTOS", stats.GasStationVisits.ToString()));
        statsGrid.Children.Add(MiniCard("OFICINAS", stats.ServiceVisits.ToString()));
        statsGrid.Children.Add(MiniCard("ACIDENTES IA", stats.CrashCount.ToString()));
        statsGrid.Children.Add(MiniCard("CANCELADOS", stats.CancelledJobs.ToString()));
        body.Children.Add(statsGrid);

        var actions = new UniformGrid { Columns = 3, Margin = new Thickness(0, 8, 0, 0) };
        var fleetButton = ModalButton("🚚 VER FROTA");
        fleetButton.Click += (_, e) => { e.Handled = true; ShowSaveFleetModal(save); };
        actions.Children.Add(fleetButton);

        var historyButton = ModalButton("📊 HISTÓRICO");
        historyButton.Click += (_, e) => { e.Handled = true; ShowSaveHistoryModal(save); };
        actions.Children.Add(historyButton);

        var refresh = ModalButton("↻ ATUALIZAR SAVE");
        refresh.Click += (_, e) => { e.Handled = true; ShowMyTruckModal(); };
        actions.Children.Add(refresh);
        body.Children.Add(actions);
    }

    private void ShowSaveFleetModal(GameSaveSnapshot save)
    {
        var body = new StackPanel();

        body.Children.Add(SectionHeader($"CAMINHÕES • {save.Trucks.Count}", "O veículo atual recebe destaque dourado"));
        foreach (var truck in save.Trucks.Take(12))
        {
            var plate = string.IsNullOrWhiteSpace(truck.LicensePlate) ? "SEM PLACA" : truck.LicensePlate;
            var current = IsCurrentTruck(save.CurrentTruck, truck);
            body.Children.Add(FleetCard(
                current ? "● VEÍCULO ATUAL" : plate,
                $"{FriendlyDefinition(truck.Definition)} • {truck.OdometerKm:0.0} km • combustível {truck.FuelPercent:0}%",
                current,
                plate));
        }
        if (save.Trucks.Count > 12)
            body.Children.Add(ModalValueRow("Outros caminhões", $"+{save.Trucks.Count - 12}"));

        body.Children.Add(SectionHeader($"REBOQUES • {save.Trailers.Count}", "O reboque acoplado recebe destaque dourado"));
        foreach (var trailer in save.Trailers.Take(12))
        {
            var plate = string.IsNullOrWhiteSpace(trailer.LicensePlate) ? "SEM PLACA" : trailer.LicensePlate;
            var current = IsCurrentTrailer(save.CurrentTrailer, trailer);
            body.Children.Add(FleetCard(
                current ? "● REBOQUE ACOPLADO" : plate,
                $"{FriendlyDefinition(trailer.Definition)} • desgaste {FormatPercent(Math.Max(trailer.TrailerBodyWear, Math.Max(trailer.ChassisWear, trailer.WheelsWear)))}",
                current,
                plate));
        }
        if (save.Trailers.Count > 12)
            body.Children.Add(ModalValueRow("Outros reboques", $"+{save.Trailers.Count - 12}"));

        ShowModalContent("save-fleet", BuildModalCard(
            "🚚 FROTA TRANSPOLI",
            body,
            "Dados persistentes do game.sii • sem importar economia do ETS2"));
    }

    private void ShowSaveHistoryModal(GameSaveSnapshot save)
    {
        var body = new StackPanel();
        var stats = save.DriverStats;

        var grid = new UniformGrid { Columns = 3 };
        grid.Children.Add(MiniCard("ENTREGAS", save.DeliveryHistory.Count.ToString()));
        grid.Children.Add(MiniCard("CIDADES", stats.VisitedCities.ToString()));
        grid.Children.Add(MiniCard("CARGAS DIFERENTES", save.TransportedCargoTypes.Count.ToString()));
        grid.Children.Add(MiniCard("POSTOS", stats.GasStationVisits.ToString()));
        grid.Children.Add(MiniCard("OFICINAS", stats.ServiceVisits.ToString()));
        grid.Children.Add(MiniCard("COMBUSTÍVEL TOTAL", $"{stats.TotalFuelLiters:0.0} L"));
        grid.Children.Add(MiniCard("ACIDENTES IA", stats.CrashCount.ToString()));
        grid.Children.Add(MiniCard("MULTAS SINAL", stats.RedLightFineCount.ToString()));
        grid.Children.Add(MiniCard("CANCELADOS", stats.CancelledJobs.ToString()));
        body.Children.Add(grid);

        if (save.TransportedCargoTypes.Count > 0)
        {
            body.Children.Add(ModalLabel("CARGAS TRANSPORTADAS"));
            body.Children.Add(ModalPanel(new TextBlock
            {
                Text = string.Join("  •  ", save.TransportedCargoTypes.Take(20)),
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        body.Children.Add(ModalLabel("HISTÓRICO DE ENTREGAS"));
        foreach (var delivery in save.DeliveryHistory.Take(8))
        {
            var detail = delivery.Parameters.Count == 0
                ? "Registro persistente"
                : string.Join(" • ", delivery.Parameters.Take(4));
            body.Children.Add(ModalValueRow("Entrega salva", detail));
        }
        if (save.DeliveryHistory.Count > 8)
            body.Children.Add(ModalValueRow("Mais registros", $"+{save.DeliveryHistory.Count - 8} entregas"));

        ShowModalContent("save-history", BuildModalCard(
            "📊 HISTÓRICO E ESTATÍSTICAS",
            body,
            "Perfil persistente do ETS2 • valores financeiros do jogo são ignorados"));
    }

    private UIElement SectionHeader(string title, string subtitle)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 16, 2, 7) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GoldBright") as Brush
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            Margin = new Thickness(0, 2, 0, 0)
        });
        return panel;
    }

    private UIElement WearCard(string label, double wear, double unfixable)
    {
        var percent = Math.Clamp(wear * 100.0, 0, 100);
        var permanent = Math.Clamp(unfixable * 100.0, 0, 100);
        var resource = percent >= 75 ? "Red" : percent >= 50 ? "Yellow" : "Green";
        var accent = FindResource(resource) as Brush ?? Brushes.White;

        var root = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(3)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        stack.Children.Add(new TextBlock { Text = $"{percent:0.0}%", FontSize = 19, FontWeight = FontWeights.Bold, Foreground = accent, Margin = new Thickness(0, 3, 0, 5) });

        var track = new Border { Height = 5, Background = FindResource("Panel") as Brush, CornerRadius = new CornerRadius(3) };
        var fill = new Border { Background = accent, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        fill.Width = Math.Max(2, Math.Min(100, percent));
        track.Child = fill;
        stack.Children.Add(track);
        stack.Children.Add(new TextBlock
        {
            Text = permanent > 0.05 ? $"permanente {permanent:0.0}%" : "reparável",
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            Margin = new Thickness(0, 5, 0, 0)
        });
        root.Child = stack;
        return root;
    }

    private UIElement FleetCard(string title, string detail, bool current, string plate)
    {
        var border = new Border
        {
            Background = FindResource(current ? "Panel2" : "Panel") as Brush,
            BorderBrush = FindResource(current ? "GoldBright" : "Stroke") as Brush,
            BorderThickness = new Thickness(current ? 1.5 : 1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 3, 0, 3)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource(current ? "GoldBright" : "Text") as Brush });
        left.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(left);
        var badge = new TextBlock { Text = plate, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        border.Child = grid;
        return border;
    }

    private static bool IsCurrentTruck(SaveTruck? current, SaveTruck item) =>
        current is not null &&
        ((!string.IsNullOrWhiteSpace(current.Id) && current.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)) ||
         (!string.IsNullOrWhiteSpace(current.LicensePlate) && current.LicensePlate.Equals(item.LicensePlate, StringComparison.OrdinalIgnoreCase)));

    private static bool IsCurrentTrailer(SaveTrailer? current, SaveTrailer item) =>
        current is not null &&
        ((!string.IsNullOrWhiteSpace(current.Id) && current.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase)) ||
         (!string.IsNullOrWhiteSpace(current.LicensePlate) && current.LicensePlate.Equals(item.LicensePlate, StringComparison.OrdinalIgnoreCase)));

    private static string FormatSaveWear(double wear, double unfixable)
    {
        var total = Math.Clamp(wear * 100.0, 0, 100);
        var permanent = Math.Clamp(unfixable * 100.0, 0, 100);
        return permanent > 0.05 ? $"{total:0.0}% • perm. {permanent:0.0}%" : $"{total:0.0}%";
    }

    private static string FormatPercent(double value) =>
        $"{Math.Clamp(value * 100.0, 0, 100):0.0}%";

    private static string FriendlyDefinition(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var normalized = value.Replace('\\', '/').Trim('/');
        var slash = normalized.LastIndexOf('/');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        return name.EndsWith(".sii", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

}
