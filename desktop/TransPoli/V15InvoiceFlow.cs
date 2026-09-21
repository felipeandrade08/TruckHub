using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{

    private readonly DispatcherTimer _v15InvoiceFlowTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _v15InvoiceFlowStarted;
    private static readonly bool V15InvoiceFlowRegistered = RegisterV15InvoiceFlow();

    private static bool RegisterV15InvoiceFlow()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V15InvoiceFlowLoaded), true);
        return true;
    }

    private static void V15InvoiceFlowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v15InvoiceFlowStarted) return;
        window._v15InvoiceFlowStarted = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window._v15InvoiceFlowTimer.Tick += async (_, _) => await window.PollV15InvoiceFlowAsync();
            window._v15InvoiceFlowTimer.Start();
            _ = window.PollV15InvoiceFlowAsync();
        }), DispatcherPriority.ContextIdle);
    }

    private async Task PollV15InvoiceFlowAsync()
    {
        try
        {
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token) || _v15InvoiceCheckBusy) return;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array || trips.GetArrayLength() == 0) return;
            JsonElement? active = null;
            foreach (var trip in trips.EnumerateArray())
            {
                if (trip.TryGetProperty("status", out var st) && string.Equals(st.GetString(), "active", StringComparison.OrdinalIgnoreCase)) { active = trip; break; }
            }
            if (active.HasValue) await EnsureV15InvoiceStateAsync(active.Value, token);
            else await ShowV15DeliveredStateAsync(token);
        }
        catch { }
    }
    private string? _v15InvoicePromptTripId;
    private bool _v15InvoiceCheckBusy;

    private async Task EnsureV15InvoiceStateAsync(JsonElement trip, string token)
    {
        if (_v15InvoiceCheckBusy) return;
        _v15InvoiceCheckBusy = true;
        try
        {
            var tripId = trip.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(tripId)) return;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/events?tripId={tripId}&type=invoice_stamped&limit=1");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            var stamped = false;
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                stamped = doc.RootElement.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array && events.GetArrayLength() > 0;
            }

            if (stamped)
            {
                TripStatusText.Text = "VIAGEM EM ANDAMENTO";
                return;
            }

            TripStatusText.Text = "AGUARDANDO CARIMBO DA NOTA";
            StatusText.Text = "TransPoli • carimbe a nota fiscal para liberar a liquidação no banco.";

            if (string.Equals(_v15InvoicePromptTripId, tripId, StringComparison.OrdinalIgnoreCase)) return;
            _v15InvoicePromptTripId = tripId;

            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _invoiceTelemetry = LastTelemetry;
                    ShowRealisticInvoiceModal();
                }
                catch { }
            }, DispatcherPriority.Normal);
        }
        catch { }
        finally { _v15InvoiceCheckBusy = false; }
    }

    private async Task ShowV15DeliveredStateAsync(string token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array || trips.GetArrayLength() == 0) return;
            var latest = trips[0];
            var status = latest.TryGetProperty("status", out var st) ? st.GetString() : null;
            if (!_tripActive && string.Equals(status, "finished", StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow - _lastTripFinishedAtUtc < TimeSpan.FromSeconds(12))
            {
                TripStatusText.Text = "CARGA ENTREGUE";
                TripLiveText.Text = "MONITORAMENTO ATIVO";
                TripProgressText.Text = "100%";
                TripRemainingText.Text = "0 km restantes";
                TripTruckText.Margin = new Thickness(Math.Max(-10, TripProgressFill.ActualWidth - 10), 0, 0, 0);
            }
        }
        catch { }
    }
}
