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
    private DateTime _dashboardRankingLastRefreshUtc = DateTime.MinValue;

    private sealed class DriverCenterItem
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "Motorista";
        public string Truck { get; init; } = "Caminhão não identificado";
        public string Cargo { get; init; } = "Sem carga";
        public string Origin { get; init; } = "—";
        public string Destination { get; init; } = "—";
        public double SpeedKph { get; init; }
        public double TripKm { get; init; }
        public string Status { get; init; } = "DISPONÍVEL";
    }

    private async Task RefreshDriverCenterAsync(bool force = false)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (!force && now - _driverCenterLastRefreshUtc < TimeSpan.FromSeconds(15))
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
                    TripKm = GetNumber("tripKm"),
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
        var shownSecond = first == drivers.Count - 1 ? 1 : second + 1;
        DashboardDriversRotationText.Text = $"Exibindo {first + 1}–{shownSecond} de {drivers.Count} • troca a cada 20s";
    }

    private async Task RefreshDashboardRankingAsync(bool force = false)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (!force && now - _dashboardRankingLastRefreshUtc < TimeSpan.FromSeconds(30)) return;
            _dashboardRankingLastRefreshUtc = now;
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token)) { RenderDashboardRanking(Array.Empty<(int,string,double,double)>()); return; }
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/ranking?period=30&metric=km");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) { RenderDashboardRanking(Array.Empty<(int,string,double,double)>()); return; }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var list = new List<(int,string,double,double)>();
            if (doc.RootElement.TryGetProperty("drivers", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    var pos=item.TryGetProperty("position",out var p)&&p.TryGetInt32(out var pi)?pi:0;
                    var name=item.TryGetProperty("name",out var n)&&n.ValueKind==JsonValueKind.String?(n.GetString()??"Motorista"):"Motorista";
                    var km=item.TryGetProperty("km",out var k)&&k.TryGetDouble(out var kv)?kv:0d;
                    var rate=item.TryGetProperty("rateBrlKm",out var r)&&r.TryGetDouble(out var rv)?rv:0d;
                    list.Add((pos,name,km,rate));
                    if(list.Count>=3) break;
                }
            }
            RenderDashboardRanking(list);
        }
        catch(Exception ex)
        {
            App.WriteUiCrashLog("DashboardRanking", ex);
            RenderDashboardRanking(Array.Empty<(int,string,double,double)>());
        }
    }

    private void RenderDashboardRanking(IReadOnlyList<(int Position,string Name,double Km,double Rate)> rows)
    {
        var names=new[]{DashboardRankingName1,DashboardRankingName2,DashboardRankingName3};
        var pos=new[]{DashboardRankingPos1,DashboardRankingPos2,DashboardRankingPos3};
        var kms=new[]{DashboardRankingKm1,DashboardRankingKm2,DashboardRankingKm3};
        var rates=new[]{DashboardRankingRate1,DashboardRankingRate2,DashboardRankingRate3};
        var borders=new[]{DashboardRankingRow1,DashboardRankingRow2,DashboardRankingRow3};
        for(var i=0;i<3;i++)
        {
            if(i<rows.Count)
            {
                var row=rows[i]; names[i].Text=row.Name; pos[i].Text=$"#{row.Position}"; kms[i].Text=$"{row.Km:N0} km"; rates[i].Text=$"R$ {row.Rate:N2}"; borders[i].Opacity=1;
            }
            else
            {
                names[i].Text=i==0?"Nenhum dado disponível":"Aguardando..."; pos[i].Text=$"#{i+1}"; kms[i].Text="0 km"; rates[i].Text="R$ 0,00"; borders[i].Opacity=0.45;
            }
        }
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
        cardBorder.Padding = new Thickness(8);
        cardBorder.VerticalAlignment = VerticalAlignment.Center;
        cardBorder.Height = 112;

        if (driver is null)
        {
            cardBorder.Opacity = 0.45;
            name.Text = "Aguardando motorista...";
            status.Text = "—";
            status.Foreground = FindResource("TextMuted") as Brush;
            truck.Text = "—";
            cargo.Text = "Carga: —";
            route.Text = "— → —";
            speed.Text = "0,0 km • 0 km/h";
            return;
        }

        cardBorder.Opacity = 1;
        name.Text = driver.Name;
        status.Text = driver.Status;
        name.FontSize = 14;
        status.FontSize = 11;
        truck.FontSize = 12;
        cargo.FontSize = 12;
        route.FontSize = 12;
        speed.FontSize = 12;
        status.Foreground = driver.Status == "EM VIAGEM"
            ? FindResource("Green") as Brush
            : FindResource("GoldBright") as Brush;
        truck.Text = driver.Truck;
        cargo.Text = $"Carga: {driver.Cargo}";
        route.Text = $"{driver.Origin} → {driver.Destination}";
        speed.Text = $"{driver.TripKm:0.0} km • {driver.SpeedKph:0} km/h";
    }
}
