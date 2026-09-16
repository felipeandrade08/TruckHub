using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private DispatcherTimer? _garageTimer;
    private bool _garageUnauthorized;
    private DateTime _lastGarageCheck = DateTime.MinValue;

    private void StartGarageEnforcement()
    {
        _garageTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _garageTimer.Tick -= GarageTimer_Tick;
        _garageTimer.Tick += GarageTimer_Tick;
        if (!_garageTimer.IsEnabled) _garageTimer.Start();
    }

    private async void GarageTimer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastGarageCheck < TimeSpan.FromMilliseconds(700)) return;
        _lastGarageCheck = DateTime.UtcNow;
        await CheckGarageAuthorizationAsync();
    }

    private async Task CheckGarageAuthorizationAsync()
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        if (string.IsNullOrWhiteSpace(TruckName?.Text) || TruckName.Text.Contains("Aguardando", StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            using var telemetryResponse = await _http.GetAsync("http://127.0.0.1:17877/telemetry");
            if (!telemetryResponse.IsSuccessStatusCode) return;
            await using var stream = await telemetryResponse.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;

            var url = $"{ApiBaseUrl}/me/garage/authorize?brand={Uri.EscapeDataString(data.TruckBrand ?? string.Empty)}&model={Uri.EscapeDataString(data.TruckModel ?? string.Empty)}&plate={Uri.EscapeDataString(data.LicensePlate ?? string.Empty)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var configured = doc.RootElement.TryGetProperty("configured", out var configuredNode) && configuredNode.GetBoolean();
            var authorized = !configured || (doc.RootElement.TryGetProperty("authorized", out var authNode) && authNode.GetBoolean());
            if (authorized)
            {
                _garageUnauthorized = false;
                return;
            }

            _garageUnauthorized = true;
            _truckLocked = true;
            VehicleLockText.Text = "🔒 CAMINHÃO EXCLUSIVO — ACESSO NEGADO";
            VehicleLockText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            UnlockButton.IsEnabled = false;
            UnlockButton.Opacity = 0.45;
            AlertText.Text = "Este caminhão está vinculado a outro motorista na garagem TransPoli.";
            AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            StatusText.Text = "TransPoli • caminhão exclusivo não autorizado para este motorista";
        }
        catch
        {
            // A indisponibilidade da API não altera o estado local para evitar bloqueio indevido.
        }
    }
}
