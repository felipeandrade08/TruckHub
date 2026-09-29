using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
    // A fila é local e durável. Verificar a cada minuto é suficiente: quando uma ação
    // precisa de envio imediato, FlushNowAsync continua disponível. Tick vazio não chama API.
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private bool _sending;
    public event Action<string, string?>? ItemSynced;
    public string? LastFailure { get; private set; }
    public event Action<string>? SyncFailed;

    public TransPoliServerSync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }

    private async Task TickAsync()
    {
        if (_sending) return;
        // A outbox não observa estado da UI nem cria eventos implícitos. Somente
        // operações explicitamente enfileiradas (viagem, despesa, manutenção etc.)
        // podem chegar ao servidor.
        await FlushAsync();
    }

    public bool QueueExpense(string? tripId, object payload)
    {
        var sourceKey = ExtractSourceKey(payload);
        return !string.IsNullOrWhiteSpace(sourceKey)
            ? Enqueue("expense-" + sourceKey, "economy.expense", tripId, payload)
            : Enqueue("economy.expense", tripId, payload);
    }

    public bool QueueEvent(string id, string type, string? tripId, DateTime occurredAtUtc, object payload)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type)) return false;
        var serverTripId = IsUuid(tripId) ? tripId : null;
        return Enqueue(id, "server.event", serverTripId, new { id, type, tripId = serverTripId, occurredAtUtc, payload });
    }

    public async Task FlushNowAsync(bool ignoreBackoff = false)
    {
        if (_sending) return;
        await FlushAsync(ignoreBackoff);
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

    private async Task FlushAsync(bool ignoreBackoff = false)
    {
        var store = LocalData.Current;
        if (store is null) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        var ownerUserId = SecureTokenStore.ReadUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return;
        var repo = new LocalSyncQueueRepository(store.Db);
        var pending = repo.GetPending(ownerUserId, 100);
        if (pending.Count == 0) { LastFailure = null; return; }
        _sending = true;
        try
        {
            // Dependências são calculadas a partir de toda a página antes do envio.
            // Assim um trip.finish não tenta a API só porque o trip.start correspondente
            // está mais atrás em backoff ou apareceu antes no lote sem ACK remoto.
            var pendingTripStarts = new HashSet<string>(
                pending.Where(x => x.Type.Equals("trip.start", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.TripId))
                       .Select(x => x.TripId!),
                StringComparer.OrdinalIgnoreCase);
            var blockedTripStarts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in pending)
            {
                // Falhas repetidas usam backoff progressivo (30s, 1m, 2m, 4m, até 15m).
                // O backoff é por item. Operações independentes podem avançar, mas
                // trip.finish nunca ultrapassa o trip.start da mesma viagem.
                if (!ignoreBackoff && item.Attempts > 0 && item.LastAttemptAtUtc.HasValue)
                {
                    var retrySeconds = Math.Min(900d, 30d * Math.Pow(2d, Math.Min(item.Attempts - 1, 5)));
                    if (DateTime.UtcNow - item.LastAttemptAtUtc.Value.ToUniversalTime() < TimeSpan.FromSeconds(retrySeconds))
                    {
                        if (item.Type.Equals("trip.start", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.TripId))
                            blockedTripStarts.Add(item.TripId);
                        continue;
                    }
                }

                // A fila pertence ao snapshot autenticado que iniciou este flush.
                // Se token ou owner mudarem (logout/troca de conta) no meio do loop,
                // interrompemos antes de qualquer nova operação remota.
                var currentToken = SecureTokenStore.Read();
                var currentOwnerUserId = SecureTokenStore.ReadUserId();
                if (!string.Equals(currentToken, token, StringComparison.Ordinal) ||
                    !string.Equals(currentOwnerUserId, ownerUserId, StringComparison.Ordinal))
                    break;

                if (item.Type.Equals("trip.finish", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.TripId) &&
                    (blockedTripStarts.Contains(item.TripId) || pendingTripStarts.Contains(item.TripId)))
                    continue;

                var sync = new SyncEvent(item.Id, item.Type, item.TripId, item.CreatedAtUtc, item.PayloadJson);
                if (!await SendAsync(token, ownerUserId, sync))
                {
                    var detail = LastFailure ?? $"{item.Type}: falha sem detalhe";
                    SyncFailed?.Invoke(detail);
                    if (!repo.MarkAttempt(item.Id, ownerUserId)) break;
                    if (item.Type.Equals("trip.start", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.TripId))
                        blockedTripStarts.Add(item.TripId);
                    // Preserve the failed row, but do not let an unrelated legacy
                    // expense/event freeze the entire offline-first queue.
                    continue;
                }
                // The remote side may already have accepted the idempotent event.
                // Never advance the local outbox unless its acknowledgement is durable.
                // A resposta pode chegar depois de uma troca de sessão. Nesse caso
                // não marcamos o item como sincronizado sob uma identidade diferente.
                if (!string.Equals(SecureTokenStore.Read(), token, StringComparison.Ordinal) ||
                    !string.Equals(SecureTokenStore.ReadUserId(), ownerUserId, StringComparison.Ordinal))
                    break;
                if (!repo.MarkSynced(item.Id, ownerUserId)) break;
                if (item.Type.Equals("trip.start", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.TripId))
                    pendingTripStarts.Remove(item.TripId);
                LastFailure = null;
                ItemSynced?.Invoke(item.Type, item.TripId);
            }
        }
        finally { _sending = false; }
    }

    private async Task<bool> SendAsync(string token, string ownerUserId, SyncEvent item)
    {
        try
        {
            var payload = item.Payload;
            string path;
            object body;

            if (item.Type.Equals("server.event", StringComparison.OrdinalIgnoreCase))
            {
                path = "/me/events";
                body = payload.Clone();
            }
            else             if (item.Type.Equals("trip.start", StringComparison.OrdinalIgnoreCase))
            {
                if (!await SendTripStartAsync(token, ownerUserId, item)) return false;
                return true;
            }
            if (item.Type.Equals("trip.finish", StringComparison.OrdinalIgnoreCase))
            {
                if (!await SendTripFinishAsync(token, ownerUserId, item)) return false;
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
                else if (string.Equals(action, "toll_payment", StringComparison.OrdinalIgnoreCase))
                {
                    path = "/me/expenses/toll-payment";
                    body = WithSourceKey(payload, GetString(payload, "sourceKey") ?? item.Id);
                }
                else if (payload.TryGetProperty("liters", out _))
                {
                    path = "/me/expenses/fuel-payment";
                    body = WithSourceKey(payload, GetString(payload, "sourceKey") ?? item.Id);
                }
                else if (string.Equals(action, "maintenance", StringComparison.OrdinalIgnoreCase) || payload.TryGetProperty("truckId", out _) && payload.TryGetProperty("serviceType", out _))
                {
                    path = "/me/maintenance";
                    body = WithSourceKey(payload, GetString(payload, "sourceKey") ?? item.Id);
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
                body = WithSourceKey(payload, GetString(payload, "sourceKey") ?? item.Id);
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
            if (!response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                LastFailure = $"{item.Type} → {path} → HTTP {(int)response.StatusCode}: {CompactError(responseBody)}";
                App.WriteUiCrashLog("ServerSync.Http", new InvalidOperationException(LastFailure));
                return false;
            }
            LastFailure = null;
            return true;
        }
        catch (Exception ex) { LastFailure = $"{item.Type}: {ex.Message}"; App.WriteUiCrashLog("ServerSync.Send", ex); return false; }
    }

    private async Task<bool> SendTripStartAsync(string token, string ownerUserId, SyncEvent item)
    {
        try
        {
            using var envelope = JsonDocument.Parse(item.PayloadJson);
            var root = envelope.RootElement;
            if (!root.TryGetProperty("payload", out var payload)) return true;

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + "/me/trips");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            var recoveryBody = new Dictionary<string, object?>();
            foreach (var property in payload.EnumerateObject())
                recoveryBody[property.Name] = property.Value.Clone();
            // trip.start is durable before the HTTP call. These fields let the API
            // distinguish an outbox replay from a fresh interactive trip start.
            recoveryBody["localTripId"] = root.TryGetProperty("localTripId", out var localKey) ? localKey.GetString() : item.TripId;
            recoveryBody["outboxRecovery"] = true;
            recoveryBody["outboxId"] = item.Id;
            request.Content = new StringContent(JsonSerializer.Serialize(recoveryBody), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { LastFailure = $"trip.start → /me/trips → HTTP {(int)response.StatusCode}: {CompactError(responseBody)}"; return false; }

            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("trip", out var trip) ||
                !trip.TryGetProperty("id", out var serverId) ||
                string.IsNullOrWhiteSpace(serverId.GetString()))
                return false;

            var localTripId = root.TryGetProperty("localTripId", out var localId) ? localId.GetString() : item.TripId;
            if (string.IsNullOrWhiteSpace(localTripId) || LocalData.Current is not { } store)
                return false;
            var trips = new LocalTripRepository(store.Db);
            if (!trips.SetServerId(localTripId, serverId.GetString()!, ownerUserId))
                return false;

            // A tarifa devolvida pelo contrato remoto é congelada no mesmo registro
            // local antes do ACK da outbox. Assim o fechamento nunca depende de uma
            // nova consulta à API para descobrir quanto vale a viagem.
            if (doc.RootElement.TryGetProperty("cargoRateBrlKm", out var rateElement))
            {
                double serverRate = 0;
                if (rateElement.ValueKind == JsonValueKind.Number)
                    serverRate = rateElement.GetDouble();
                else if (rateElement.ValueKind == JsonValueKind.String)
                    double.TryParse(rateElement.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out serverRate);
                if (serverRate >= 12 && serverRate <= 22)
                    trips.SetRatePerKm(localTripId, serverRate);
            }

            return true;
        }
        catch (Exception ex) { LastFailure = $"trip.start: {ex.Message}"; App.WriteUiCrashLog("ServerSync.TripStart", ex); return false; }
    }

    private async Task<bool> SendTripFinishAsync(string token, string ownerUserId, SyncEvent item)
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

            var serverId = GetLocalServerTripId(store.Db, localTripId, ownerUserId);
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
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { LastFailure = $"trip.finish → /me/trips/{serverId}/finish → HTTP {(int)response.StatusCode}: {CompactError(responseBody)}"; return false; }

            // HTTP 2xx sozinho não conclui o outbox: o servidor precisa devolver a
            // liquidação da viagem. Se a resposta vier truncada após marcar a viagem
            // como finished, mantemos o mesmo item para retry idempotente.
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("economy", out var economy) ||
                economy.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return false;

            // A liquidação já existente também é confirmação válida; settleTripEconomy
            // é idempotente por TripId e o servidor pode estar respondendo a um retry.
            return true;
        }
        catch (Exception ex) { LastFailure = $"trip.finish: {ex.Message}"; App.WriteUiCrashLog("ServerSync.TripFinish", ex); return false; }
    }

    private static string? GetLocalServerTripId(TransPoliDb db, string localTripId, string ownerUserId)
    {
        if(string.IsNullOrWhiteSpace(ownerUserId)) return null;
        using var command = db.Connection.CreateCommand();
        command.CommandText = "SELECT server_id FROM trip WHERE id=@id AND owner_user_id=@owner LIMIT 1;";
        command.Parameters.AddWithValue("@id", localTripId);
        command.Parameters.AddWithValue("@owner", ownerUserId);
        return command.ExecuteScalar() is { } value && value != DBNull.Value ? Convert.ToString(value) : null;
    }


    private static string CompactError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "sem corpo de resposta";
        var compact = body.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 300 ? compact : compact[..300];
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
