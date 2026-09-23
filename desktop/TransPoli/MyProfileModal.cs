using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal void ShowMyProfileModal()
    {
        var body = new StackPanel { Margin = new Thickness(4) };
        body.Children.Add(ModalHero("MOTORISTA TRANSPOLI", "Central do motorista", "Desempenho operacional, conta local, veículo atual e sincronização reunidos em um único perfil.", BuildProfileSessionText(), string.IsNullOrWhiteSpace(SecureTokenStore.Read()) ? "Yellow" : "Green"));
        var data = LastTelemetry;

        AddProfileHero(body, data);
        AddProfileOperational(body, data);
        AddProfileFinancial(body);
        AddProfileVehicleHealth(body, data);
        AddProfileSync(body);

        ShowModalContent(
            "my-profile",
            BuildModalCard("👤 MEU PERFIL", body,
                "Central do motorista • desempenho local • conta operacional • funciona offline"));
    }

    private void AddProfileHero(Panel body, TelemetrySnapshot? data)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 12, 17, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 74, 11)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var row = new StackPanel();
        row.Children.Add(new TextBlock
        {
            Text = "MOTORISTA TRANSPOLI",
            Foreground = FindResource("GoldBright") as Brush,
            FontSize = 8,
            FontWeight = FontWeights.Bold
        });
        row.Children.Add(new TextBlock
        {
            Text = "Perfil operacional",
            Foreground = FindResource("TextMain") as Brush,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 3, 0, 0)
        });
        row.Children.Add(new TextBlock
        {
            Text = BuildProfileSessionText(),
            Foreground = FindResource("TextMuted") as Brush,
            FontSize = 8,
            Margin = new Thickness(0, 3, 0, 0)
        });

        var truck = data is null
            ? "Caminhão aguardando conexão"
            : $"{data.TruckBrand ?? "Caminhão"} {data.TruckModel ?? ""}".Trim();

        row.Children.Add(new TextBlock
        {
            Text = truck,
            Foreground = FindResource("TextMain") as Brush,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 0)
        });

        row.Children.Add(new TextBlock
        {
            Text = data is null
                ? "Placa não disponível"
                : $"Placa: {data.LicensePlate ?? "não informada"}  •  ID: {data.TruckId ?? "não informado"}",
            Foreground = FindResource("TextMuted") as Brush,
            FontSize = 7.5,
            Margin = new Thickness(0, 3, 0, 0)
        });

        card.Child = row;
        body.Children.Add(card);
    }

    private void AddProfileOperational(Panel body, TelemetrySnapshot? data)
    {
        var stats = QueryProfileStats();
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };

        AddProfileMetric(grid, "VIAGENS", stats.Trips.ToString(CultureInfo.InvariantCulture));
        AddProfileMetric(grid, "DISTÂNCIA", $"{stats.DistanceKm:0.0} km");
        AddProfileMetric(grid, "COMBUSTÍVEL", $"{stats.FuelLiters:0.0} L");
        AddProfileMetric(grid, "RECEITA", $"R$ {stats.Revenue:0.00}");
        AddProfileMetric(grid, "DESPESAS", $"R$ {stats.Expenses:0.00}");
        AddProfileMetric(grid, "RESULTADO", $"R$ {stats.Net:0.00}");

        body.Children.Add(grid);

        var state = data is null || !data.Connected
            ? "OFFLINE • conecte o ETS2 para telemetria ao vivo"
            : data.GamePaused
                ? "ETS2 CONECTADO • jogo pausado"
                : "ETS2 CONECTADO • telemetria em tempo real";

        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 11, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(11),
            Child = new TextBlock
            {
                Text = $"●  {state}",
                Foreground = data?.Connected == true
                    ? FindResource("Green") as Brush
                    : FindResource("TextMuted") as Brush,
                FontSize = 8,
                FontWeight = FontWeights.Bold
            }
        });
    }

    private void AddProfileFinancial(Panel body)
    {
        var stats = QueryProfileStats();
        var text = new TextBlock
        {
            Text = $"Conta operacional local  •  média de receita/km: R$ {stats.RevenuePerKm:0.00}  •  custo/km: R$ {stats.CostPerKm:0.00}",
            Foreground = FindResource("TextMuted") as Brush,
            FontSize = 7.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 10, 3, 0)
        };
        body.Children.Add(text);
    }

    private void AddProfileVehicleHealth(Panel body, TelemetrySnapshot? data)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
        var wear = data is null
            ? 0f
            : Math.Clamp(Math.Max(Math.Max(Math.Max(data.WearEngine, data.WearTransmission), data.WearCabin), Math.Max(data.WearChassis, data.WearWheels)) * 100f, 0f, 100f);

        var health = wear >= 75f ? "CRÍTICO" : wear >= 50f ? "ATENÇÃO" : "NORMAL";
        var healthBrush = wear >= 75f ? FindResource("Red") as Brush : wear >= 50f ? FindResource("GoldBright") as Brush : FindResource("Green") as Brush;

        AddProfileMetric(grid, "DESGASTE", $"{wear:0}%");
        AddProfileMetric(grid, "MANUTENÇÃO", data is null ? "—" : health);
        AddProfileMetric(grid, "MOTOR", data?.EngineEnabled == true ? "LIGADO" : "DESLIGADO");

        if (grid.Children.Count >= 2 && grid.Children[1] is Border maintenanceCard && maintenanceCard.Child is StackPanel stack && stack.Children.Count > 1 && stack.Children[1] is TextBlock value)
            value.Foreground = healthBrush;

        body.Children.Add(grid);

        var truckState = data is null
            ? "Caminhão não conectado"
            : string.IsNullOrWhiteSpace(data.TruckId) && string.IsNullOrWhiteSpace(data.LicensePlate)
                ? "Caminhão detectado • identificação não informada"
                : $"Caminhão vinculado • {(data.LicensePlate ?? "placa não informada")}";

        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 12, 17, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 7, 0, 0),
            Child = new TextBlock
            {
                Text = $"🚛  {truckState}  •  {BuildProfileLastTripText()}",
                Foreground = FindResource("TextMuted") as Brush,
                FontSize = 7.5,
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private string BuildProfileLastTripText()
    {
        try
        {
            if (LocalData.Current is not { } store) return "última viagem: —";
            using var c = store.Db.Connection.CreateCommand();
            c.CommandText = @"SELECT cargo_name, source_city, destination_city, finished_at_utc FROM trip WHERE status='finished' ORDER BY finished_at_utc DESC LIMIT 1;";
            using var reader = c.ExecuteReader();
            if (!reader.Read()) return "última viagem: —";
            var cargo = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "Carga";
            var origin = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "Origem";
            var destination = Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? "Destino";
            var finished = Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture);
            return $"última viagem: {cargo} • {origin} → {destination} • {FormatProfileDate(finished)}";
        }
        catch { return "última viagem: —"; }
    }

    private static string FormatProfileDate(string? value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return "—";
        return parsed.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }

    private void AddProfileSync(Panel body)
    {
        var pending = 0;
        try { pending = LocalData.Current?.Db is { } db ? GetProfilePendingSync(db) : 0; } catch { }

        body.Children.Add(new TextBlock
        {
            Text = pending == 0
                ? "🔒 BANCO LOCAL • tudo sincronizado • offline disponível"
                : $"↻ BANCO LOCAL • {pending} item(ns) pendente(s) de sincronização",
            Foreground = pending == 0
                ? FindResource("Green") as Brush
                : FindResource("GoldBright") as Brush,
            FontSize = 7.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(3, 8, 3, 0)
        });
    }

    private void AddProfileMetric(Panel grid, string label, string value)
    {
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 16, 23, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Margin = new Thickness(3),
            Padding = new Thickness(10, 9, 10, 9),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, Foreground = FindResource("TextMuted") as Brush, FontSize = 7, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = value, Foreground = FindResource("TextMain") as Brush, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0) }
                }
            }
        });
    }

    private string BuildProfileSessionText()
    {
        return string.IsNullOrWhiteSpace(SecureTokenStore.Read())
            ? "Sessão central não conectada • dados locais continuam disponíveis"
            : "Sessão TransPoli ativa • sincronização central complementar";
    }

    private static int GetProfilePendingSync(TransPoliDb db)
    {
        using var c = db.Connection.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM sync_queue WHERE synced_at_utc IS NULL;";
        return Convert.ToInt32(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private ProfileStats QueryProfileStats()
    {
        var result = new ProfileStats();
        try
        {
            if (LocalData.Current is not { } store) return result;

            using var c = store.Db.Connection.CreateCommand();
            c.CommandText = @"
SELECT
    COUNT(CASE WHEN status='finished' THEN 1 END),
    COALESCE(SUM(CASE WHEN status='finished' THEN distance_km ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN fuel_consumed_l ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN income_gross ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN expense_total ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN net_value ELSE 0 END),0)
FROM trip;";
            using var reader = c.ExecuteReader();
            if (!reader.Read()) return result;

            result.Trips = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            result.DistanceKm = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
            result.FuelLiters = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
            result.Revenue = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
            result.Expenses = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture);
            result.Net = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture);
            result.RevenuePerKm = result.DistanceKm > 0 ? (double)result.Revenue / result.DistanceKm : 0;
            result.CostPerKm = result.DistanceKm > 0 ? (double)result.Expenses / result.DistanceKm : 0;
        }
        catch { }

        return result;
    }

    private sealed class ProfileStats
    {
        public int Trips;
        public double DistanceKm;
        public double FuelLiters;
        public decimal Revenue;
        public decimal Expenses;
        public decimal Net;
        public double RevenuePerKm;
        public double CostPerKm;
    }
}
