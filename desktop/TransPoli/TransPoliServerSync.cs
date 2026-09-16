using System;
using System.Collections.Generic;
using System.IO;
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
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly string _queuePath;
    private readonly List<SyncEvent> _queue = new();
    private bool _hooked;
    private bool _sending;
    private bool _lastTripActive;
    private string? _lastLifecycle;

    public TransPoliServerSync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _queuePath = Path.Combine(folder, "transpoli-server-sync.json");
        Load();
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

    private void Enqueue(string type, string? tripId, object payload)
    {
        _queue.Add(new SyncEvent { Id = Guid.NewGuid().ToString("N"), Type = type, TripId = tripId, CreatedAtUtc = DateTime.UtcNow, Payload = JsonSerializer.SerializeToElement(payload) });
        if (_queue.Count > 500) _queue.RemoveRange(0, _queue.Count - 500);
        Save();
    }

    private async Task FlushAsync()
    {
        if (_queue.Count == 0) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        _sending = true;
        try
        {
            foreach (var item in _queue.ToList())
            {
                if (!await SendAsync(token, item)) break;
                _queue.Remove(item);
                Save();
            }
        }
        finally { _sending = false; }
    }

    private async Task<bool> SendAsync(string token, SyncEvent item)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/events");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(JsonSerializer.Serialize(new { id = item.Id, type = item.Type, tripId = item.TripId, occurredAtUtc = item.CreatedAtUtc, payload = item.Payload }), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_queuePath)) return;
            var data = JsonSerializer.Deserialize<List<SyncEvent>>(File.ReadAllText(_queuePath));
            if (data != null) _queue.AddRange(data);
        }
        catch { _queue.Clear(); }
    }

    private void Save()
    {
        try { File.WriteAllText(_queuePath, JsonSerializer.Serialize(_queue, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    private static T GetField<T>(object target, string name, T fallback)
    {
        var value = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
        return value is T typed ? typed : fallback;
    }

    private sealed class SyncEvent
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string? TripId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public JsonElement Payload { get; set; }
    }
}
