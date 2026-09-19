using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private DateTime _driverCenterLastRefreshUtc = DateTime.MinValue;
    private DateTime _driverCenterLastRotationUtc = DateTime.UtcNow;
    private int _driverCenterRotationIndex;

    private sealed class DriverCenterItem
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "Motorista";
        public string Truck { get; init; } = "Caminhão não identificado";
        public string Cargo { get; init; } = "Sem carga";
        public string Origin { get; init; } = "—";
        public string Destination { get; init; } = "—";
        public double SpeedKph { get; init; }
        public string Status { get; init; } = "DISPONÍVEL";
    }

    private async Task RefreshDriverCenterAsync(bool force = false)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (!force && now - _driverCenterLastRefreshUtc < TimeSpan.FromSeconds(5))
                return;

            _driverCenterLastRefreshUtc = now;
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token))
            {
                RenderDriverCenter(Array.Empty<DriverCenterItem>());
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/drivers/online");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                RenderDriverCenter(Array.Empty<DriverCenterItem>());
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("drivers", out var drivers) || drivers.ValueKind != JsonValueKind.Array)
            {
                RenderDriverCenter(Array.Empty<DriverCenterItem>());
                return;
            }

            var list = new List<DriverCenterItem>();
            foreach (var item in drivers.EnumerateArray())
            {
                string Get(string name, string fallback = "—") =>
                    item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                        ? (p.GetString() ?? fallback)
                        : fallback;

                double GetNumber(string name) =>
                    item.TryGetProperty(name, out var p) && p.TryGetDouble(out var value) ? value : 0d;

                list.Add(new DriverCenterItem
                {
                    Id = Get("id", ""),
                    Name = Get("name", "Motorista"),
                    Truck = Get("truck", "Caminhão não identificado"),
                    Cargo = Get("cargo", "Sem carga"),
                    Origin = Get("origin"),
                    Destination = Get("destination"),
                    SpeedKph = GetNumber("speedKph"),
                    Status = Get("status", "DISPONÍVEL")
                });
            }

            if (list.Count > 2 && now - _driverCenterLastRotationUtc >= TimeSpan.FromSeconds(20))
            {
                _driverCenterRotationIndex = (_driverCenterRotationIndex + 2) % list.Count;
                _driverCenterLastRotationUtc = now;
            }
            else if (list.Count <= 2)
            {
                _driverCenterRotationIndex = 0;
                _driverCenterLastRotationUtc = now;
            }
            else if (_driverCenterRotationIndex >= list.Count)
            {
                _driverCenterRotationIndex = 0;
            }

            RenderDriverCenter(list);
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DriverCenter", ex);
            RenderDriverCenter(Array.Empty<DriverCenterItem>());
        }
    }

    private void RenderDriverCenter(IReadOnlyList<DriverCenterItem> drivers)
    {
        DashboardDriversOnlineText.Text = $"{drivers.Count} ONLINE";

        if (drivers.Count == 0)
        {
            SetDriverCard(1, null);
            SetDriverCard(2, null);
            DashboardDriversRotationText.Text = "Nenhum motorista online no momento";
            return;
        }

        if (drivers.Count <= 2)
        {
            SetDriverCard(1, drivers.Count > 0 ? drivers[0] : null);
            SetDriverCard(2, drivers.Count > 1 ? drivers[1] : null);
            DashboardDriversRotationText.Text = "Presença atualizada automaticamente";
            return;
        }

        var first = _driverCenterRotationIndex % drivers.Count;
        var second = (first + 1) % drivers.Count;
        SetDriverCard(1, drivers[first]);
        SetDriverCard(2, drivers[second]);
        DashboardDriversRotationText.Text = $"Exibindo {first + 1}–{first == drivers.Count - 1 ? 1 : second + 1} de {drivers.Count} • troca a cada 20s";
    }

    private void SetDriverCard(int card, DriverCenterItem? driver)
    {
        var name = card == 1 ? DriverName1 : DriverName2;
        var status = card == 1 ? DriverStatus1 : DriverStatus2;
        var truck = card == 1 ? DriverTruck1 : DriverTruck2;
        var cargo = card == 1 ? DriverCargo1 : DriverCargo2;
        var route = card == 1 ? DriverRoute1 : DriverRoute2;
        var speed = card == 1 ? DriverSpeed1 : DriverSpeed2;
        var cardBorder = card == 1 ? DriverCard1 : DriverCard2;

        if (driver is null)
        {
            cardBorder.Opacity = 0.45;
            name.Text = "Aguardando motorista...";
            status.Text = "—";
            status.Foreground = FindResource("TextMuted") as Brush;
            truck.Text = "—";
            cargo.Text = "Carga: —";
            route.Text = "— → —";
            speed.Text = "0 km/h";
            return;
        }

        cardBorder.Opacity = 1;
        name.Text = driver.Name;
        status.Text = driver.Status;
        status.Foreground = driver.Status == "EM VIAGEM"
            ? FindResource("Green") as Brush
            : FindResource("GoldBright") as Brush;
        truck.Text = driver.Truck;
        cargo.Text = $"Carga: {driver.Cargo}";
        route.Text = $"{driver.Origin} → {driver.Destination}";
        speed.Text = $"{driver.SpeedKph:0} km/h";
    }
}
