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

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
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
        var sourceKey = ExtractSourceKey(payload);
        if (!string.IsNullOrWhiteSpace(sourceKey)) Enqueue("expense-" + sourceKey, "economy.expense", tripId, payload);
        else Enqueue("economy.expense", tripId, payload);
    }

    private static string? ExtractSourceKey(object payload)
    {
        try { var json=JsonSerializer.SerializeToElement(payload); return json.TryGetProperty("sourceKey",out var key)?key.GetString():null; }
        catch { return null; }
    }

    public bool QueueTripStart(string localTripId, object payload)
    {
        if (string.IsNullOrWhiteSpace(localTripId)) return false;
        return Enqueue("trip-start-" + localTripId, "trip.start", localTripId, new { localTripId, payload });
    }

    public bool QueueTripFinish(string localTripId, object payload)
    {
        if (string.IsNullOrWhiteSpace(localTripId)) return false;
        return Enqueue("trip-finish-" + localTripId, "trip.finish", localTripId, new { localTripId, payload });
    }

    private bool Enqueue(string type, string? tripId, object payload)
    {
        return Enqueue(Guid.NewGuid().ToString("N"), type, tripId, payload);
    }

    private bool Enqueue(string id, string type, string? tripId, object payload)
    {
        var store = LocalData.Current;
        if (store is null) return false;
        var ownerUserId = SecureTokenStore.ReadUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return false;
        var created = DateTime.UtcNow;
        return new LocalSyncQueueRepository(store.Db).Enqueue(id, type, tripId, JsonSerializer.Serialize(payload), created, ownerUserId);
    }

    private async Task FlushAsync()
    {
        var store = LocalData.Current;
        if (store is null) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        var ownerUserId = SecureTokenStore.ReadUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return;
        var repo = new LocalSyncQueueRepository(store.Db);
        var pending = repo.GetPending(ownerUserId, 100);
        if (pending.Count == 0) return;
        _sending = true;
        try
        {
            foreach (var item in pending)
            {
                var sync = new SyncEvent(item.Id, item.Type, item.TripId, item.CreatedAtUtc, item.PayloadJson);
                if (!await SendAsync(token, sync))
                {
                    if (!repo.MarkAttempt(item.Id)) break;
                    break;
                }
                // The remote side may already have accepted the idempotent event.
                // Never advance the local outbox unless its acknowledgement is durable.
                if (!repo.MarkSynced(item.Id)) break;
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

            if (item.Type.Equals("trip.start", StringComparison.OrdinalIgnoreCase))
            {
                if (!await SendTripStartAsync(token, item)) return false;
                return true;
            }
            if (item.Type.Equals("trip.finish", StringComparison.OrdinalIgnoreCase))
            {
                if (!await SendTripFinishAsync(token, item)) return false;
                return true;
            }
            if (item.Type.Equals("economy.expense", StringComparison.OrdinalIgnoreCase))
            {
                var action = payload.TryGetProperty("action", out var actionElement) ? actionElement.GetString() : null;
                // Compatibilidade: filas antigas de empréstimo local são descartadas.
                // O modelo atual usa company_loans aprovado pela Diretoria.
                if (string.Equals(action, "loan_credit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(action, "loan_settlement", StringComparison.OrdinalIgnoreCase))
                    return true;
                else if (payload.TryGetProperty("liters", out _))
                {
                    path = "/me/expenses/fuel-payment";
                    body = WithSourceKey(payload, GetString(payload, "sourceKey") ?? item.Id);
                }
                else if (payload.TryGetProperty("truckId", out _) && payload.TryGetProperty("serviceType", out _))
                {
                    path = "/me/maintenance";
                    body = WithSourceKey(payload, item.Id);
                }
                else
                {
                    path = "/me/events";
                    // Mesmo na fila de despesas, identificadores locais não podem ser
                    // enviados como tripId: a API aceita somente UUID do servidor.
                    var expenseTripId = IsUuid(item.TripId) ? item.TripId : null;
                    body = new { id = item.Id, type = item.Type, tripId = expenseTripId, occurredAtUtc = item.CreatedAtUtc, payload };
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
                // /me/events exige UUID no tripId. Viagens antigas/offline podem ter
                // identificadores locais; nesse caso enviamos o evento sem tripId para
                // não transformar uma fila local válida em erro HTTP 400.
                var eventTripId = IsUuid(item.TripId) ? item.TripId : null;
                body = new { id = item.Id, type = item.Type, tripId = eventTripId, occurredAtUtc = item.CreatedAtUtc, payload };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            // Erros 4xx permanentes não devem derrubar a indicação de telemetria nem
            // bloquear toda a fila para sempre. O item será tentado novamente apenas
            // enquanto a sessão/API puder aceitá-lo.
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<bool> SendTripStartAsync(string token, SyncEvent item)
    {
        try
        {
            using var envelope = JsonDocument.Parse(item.PayloadJson);
            var root = envelope.RootElement;
            if (!root.TryGetProperty("payload", out var payload)) return true;

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + "/me/trips");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trip", out var trip) ||
                !trip.TryGetProperty("id", out var serverId) ||
                string.IsNullOrWhiteSpace(serverId.GetString()))
                return false;

            var localTripId = root.TryGetProperty("localTripId", out var localId) ? localId.GetString() : item.TripId;
            if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } store)
                new LocalTripRepository(store.Db).SetServerId(localTripId, serverId.GetString()!);

            return true;
        }
        catch { return false; }
    }

    private async Task<bool> SendTripFinishAsync(string token, SyncEvent item)
    {
        try
        {
            using var envelope = JsonDocument.Parse(item.PayloadJson);
            var root = envelope.RootElement;
            // Um envelope de finalização corrompido nunca é ACK. Mantemos o item
            // pendente para inspeção/recovery em vez de apagá-lo silenciosamente.
            if (!root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return false;

            var localTripId = root.TryGetProperty("localTripId", out var localId) ? localId.GetString() : item.TripId;
            if (string.IsNullOrWhiteSpace(localTripId) || LocalData.Current is not { } store) return false;

            var serverId = GetLocalServerTripId(store.Db, localTripId);
            if (string.IsNullOrWhiteSpace(serverId))
            {
                // A queued start must be processed first. Keep this item pending.
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + $"/me/trips/{serverId}/finish");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            // HTTP 2xx sozinho não conclui o outbox: o servidor precisa devolver a
            // liquidação da viagem. Se a resposta vier truncada após marcar a viagem
            // como finished, mantemos o mesmo item para retry idempotente.
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("economy", out var economy) ||
                economy.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return false;

            // A liquidação já existente também é confirmação válida; settleTripEconomy
            // é idempotente por TripId e o servidor pode estar respondendo a um retry.
            return true;
        }
        catch { return false; }
    }

    private static string? GetLocalServerTripId(TransPoliDb db, string localTripId)
    {
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT server_id FROM trip WHERE id=@id LIMIT 1;";
        command.Parameters.AddWithValue("@id", localTripId);
        return command.ExecuteScalar() is { } value && value != DBNull.Value ? Convert.ToString(value) : null;
    }


    private static bool IsUuid(string? value)
        => Guid.TryParse(value, out _);

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
