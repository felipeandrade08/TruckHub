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
        // O cockpit 2.0 não renderiza mais cards de outros motoristas.
        // Mantemos a coleta isolada para futura Central da Diretoria, sem bindings XAML legados.
    }

}
