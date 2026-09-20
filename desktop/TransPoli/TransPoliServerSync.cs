using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly TransPoliServerSync _serverSync = new();
}

public sealed class TransPoliServerSync
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _hooked;
    private bool _sending;
    private bool _lastTripActive;
    private string? _lastLifecycle;

    public TransPoliServerSync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        Application.Current?.Dispatcher.BeginInvoke(new Action(Hook), DispatcherPriority.Loaded);
    }

    private void Hook()
    {
        if (_hooked) return;
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null) return;
        _hooked = true;
        main.Loaded += (_, _) => Capture(main);
    }

    private async Task TickAsync()
    {
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null || _sending) return;
        Capture(main);
        await FlushAsync();
    }

    private void Capture(MainWindow main)
    {
        var active = GetField(main, "_tripActive", false);
        var serverTripId = GetField<string?>(main, "_serverTripId", null);
        var lifecycle = GetLifecycle(main);
        if (active && !_lastTripActive) Enqueue("trip.lifecycle", serverTripId, new { status = "started", source = "ets2-telemetry", atUtc = DateTime.UtcNow });
        if (!active && _lastTripActive) Enqueue("trip.lifecycle", serverTripId, new { status = "finished", source = "ets2-telemetry", atUtc = DateTime.UtcNow });
        if (!string.IsNullOrWhiteSpace(lifecycle) && lifecycle != _lastLifecycle) Enqueue("cargo.lifecycle", serverTripId, new { status = lifecycle, source = "transpoli-cargo", atUtc = DateTime.UtcNow });
        _lastTripActive = active;
        _lastLifecycle = lifecycle;
    }

    private string? GetLifecycle(MainWindow main)
    {
        var field = main.GetType().GetField("_cargoOperations", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var op = field?.GetValue(main);
        if (op is null) return null;
        var stateField = op.GetType().GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var state = stateField?.GetValue(op);
        return state?.GetType().GetProperty("Lifecycle")?.GetValue(state)?.ToString();
    }

    public void QueueExpense(string? tripId, object payload)
    {
        Enqueue("economy.expense", tripId, payload);
    }

    private void Enqueue(string type, string? tripId, object payload)
    {
        var store = LocalData.Current;
        if (store is null) return;
        var id = Guid.NewGuid().ToString("N");
        var created = DateTime.UtcNow;
        new LocalSyncQueueRepository(store.Db).Enqueue(id, type, tripId, JsonSerializer.Serialize(payload), created);
    }

    private async Task FlushAsync()
    {
        var store = LocalData.Current;
        if (store is null) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        var repo = new LocalSyncQueueRepository(store.Db);
        var pending = repo.GetPending(100);
        if (pending.Count == 0) return;
        _sending = true;
        try
        {
            foreach (var item in pending)
            {
                var sync = new SyncEvent(item.Id, item.Type, item.TripId, item.CreatedAtUtc, item.PayloadJson);
                if (!await SendAsync(token, sync))
                {
                    repo.MarkAttempt(item.Id);
                    break;
                }
                repo.MarkSynced(item.Id);
            }
        }
        finally { _sending = false; }
    }

    private async Task<bool> SendAsync(string token, SyncEvent item)
    {
        try
        {
            var payload = item.Payload;
            string path;
            object body;

            if (item.Type.Equals("economy.expense", StringComparison.OrdinalIgnoreCase))
            {
                var action = payload.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : null;
                if (string.Equals(action, "loan_credit", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/me/economy/loan";
                    body = new
                    {
                        principalBrl = GetDecimal(payload, "amount"),
                        repaymentPct = GetDecimal(payload, "repaymentPct", 20m),
                        installments = GetInt(payload, "installments", 10),
                        localLoanId = GetString(payload, "localLoanId")
                    };
                }
                else if (string.Equals(action, "loan_settlement", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/me/economy/loan/settle";
                    body = new { localLoanId = GetString(payload, "localLoanId") };
                }
                else if (payload.TryGetProperty("liters", out _))
                {
                    path = "/me/expenses/fuel-payment";
                    body = WithSourceKey(payload, item.Id);
                }
                else if (payload.TryGetProperty("truckId", out _) && payload.TryGetProperty("serviceType", out _))
                {
                    path = "/me/maintenance";
                    body = WithSourceKey(payload, item.Id);
                }
                else
                {
                    path = "/me/events";
                    body = new { id = item.Id, type = item.Type, tripId = item.TripId, occurredAtUtc = item.CreatedAtUtc, payload };
                }
            }
            else if (item.Type.Equals("maintenance", StringComparison.OrdinalIgnoreCase))
            {
                path = "/me/maintenance";
                body = WithSourceKey(payload, item.Id);
            }
            else
            {
                path = "/me/events";
                body = new { id = item.Id, type = item.Type, tripId = item.TripId, occurredAtUtc = item.CreatedAtUtc, payload };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string? GetString(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static decimal GetDecimal(JsonElement payload, string name, decimal fallback = 0m)
        => payload.TryGetProperty(name, out var value) && value.TryGetDecimal(out var number) ? number : fallback;

    private static int GetInt(JsonElement payload, string name, int fallback)
        => payload.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static object WithSourceKey(JsonElement payload, string sourceKey)
    {
        var map = new Dictionary<string, object?>();
        foreach (var property in payload.EnumerateObject())
            map[property.Name] = property.Value.Clone();
        map["sourceKey"] = sourceKey;
        return map;
    }

    private static T GetField<T>(object target, string name, T fallback)
    {
        var value = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
        return value is T typed ? typed : fallback;
    }

    private sealed record SyncEvent(string Id, string Type, string? TripId, DateTime CreatedAtUtc, string PayloadJson)
    {
        public JsonElement Payload => JsonSerializer.Deserialize<JsonElement>(PayloadJson);
    }
}
