using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly TransPoliDrivingAnalytics _drivingAnalytics = new();
}

public sealed class TransPoliDrivingAnalytics
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<DrivingEventRecord> _events = new();
    private readonly string _path;
    private float _lastSpeed;
    private float _lastOdometer;
    private DateTime _lastSampleUtc;
    private DateTime _stopStartedUtc;
    private float _stopOdometer;
    private bool _stopped;
    private int _stationaryTicks;
    private int _movingTicks;
    private float _tripDistance;
    private float _tripFuelStart;
    private float _tripFuelLast;
    private DateTime _tripStartedUtc;
    private string _lastTripKey = "";

    public TransPoliDrivingAnalytics()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "transpoli-driving-events.json");
        Load();
        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();
    }

    private async Task PollAsync()
    {
        try
        {
            var window = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
            if (window is null) return;
            using var response = await _http.GetAsync(MainWindow.TelemetryUrl);
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;
            var now = DateTime.UtcNow;
            var speed = Math.Abs(data.SpeedKph);
            var dt = _lastSampleUtc == default ? TimeSpan.Zero : now - _lastSampleUtc;
            if (dt > TimeSpan.FromSeconds(5)) dt = TimeSpan.FromSeconds(1);
            DetectStopResume(data, now, speed);
            DetectDrivingEvents(data, speed, dt);
            UpdateTripMetrics(window, data, now, speed, dt);
            _lastSpeed = speed;
            _lastOdometer = data.OdometerKm;
            _lastSampleUtc = now;
        }
        catch { }
    }

    private void DetectStopResume(TelemetrySnapshot data, DateTime now, float speed)
    {
        if (speed < 0.8f)
        {
            _stationaryTicks++; _movingTicks = 0;
            if (!_stopped && _stationaryTicks >= 5)
            {
                _stopped = true; _stopStartedUtc = now.AddSeconds(-4); _stopOdometer = data.OdometerKm;
                AddEvent("PARADA_INICIADA", "Veículo parado por mais de 5 segundos.", data, now);
            }
        }
        else
        {
            _movingTicks++; _stationaryTicks = 0;
            if (_stopped && _movingTicks >= 2)
            {
                var duration = now - _stopStartedUtc;
                _stopped = false;
                AddEvent("PARADA_FINALIZADA", $"Parada encerrada • duração {FormatDuration(duration)} • {Math.Max(0, data.OdometerKm - _stopOdometer):0.0} km depois do ponto anterior.", data, now);
            }
        }
    }

    private void DetectDrivingEvents(TelemetrySnapshot data, float speed, TimeSpan dt)
    {
        if (dt <= TimeSpan.Zero || dt > TimeSpan.FromSeconds(5)) return;
        var delta = speed - _lastSpeed;
        var acceleration = delta / Math.Max(0.5, dt.TotalSeconds);
        if (_lastSampleUtc != default && speed >= 25 && acceleration <= -18 && !RecentEvent("FREIADA_BRUSCA", 15))
            AddEvent("FREIADA_BRUSCA", $"Redução estimada de {Math.Abs(acceleration):0} km/h por segundo.", data, DateTime.UtcNow);
        else if (_lastSampleUtc != default && speed >= 15 && acceleration >= 15 && !RecentEvent("ACELERACAO_BRUSCA", 15))
            AddEvent("ACELERACAO_BRUSCA", $"Aceleração estimada de {acceleration:0} km/h por segundo.", data, DateTime.UtcNow);
        if (speed > 110 && !RecentEvent("VELOCIDADE_ELEVADA", 30))
            AddEvent("VELOCIDADE_ELEVADA", $"Velocidade registrada: {speed:0} km/h.", data, DateTime.UtcNow);
    }

    private void UpdateTripMetrics(MainWindow window, TelemetrySnapshot data, DateTime now, float speed, TimeSpan dt)
    {
        var tripKey = $"{data.SourceCity}|{data.DestinationCity}|{data.Cargo}|{data.TruckId}";
        var active = window.GetType().GetField("_tripActive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(window) is true;
        if (active && _lastTripKey != tripKey)
        {
            _lastTripKey = tripKey; _tripStartedUtc = now; _tripDistance = 0; _tripFuelStart = data.FuelLiters; _tripFuelLast = data.FuelLiters;
        }
        if (!active || _tripStartedUtc == default) return;
        if (_lastOdometer > 0 && data.OdometerKm >= _lastOdometer) _tripDistance += data.OdometerKm - _lastOdometer;
        _tripFuelLast = data.FuelLiters;
        var elapsed = now - _tripStartedUtc;
        var average = elapsed.TotalHours > 0 ? _tripDistance / (float)elapsed.TotalHours : 0;
        var fuelUsed = Math.Max(0, _tripFuelStart - _tripFuelLast);
        var l100 = _tripDistance > 0.5 ? fuelUsed / _tripDistance * 100 : 0;
        var stop = _stopped ? $"PARADO {FormatDuration(now - _stopStartedUtc)}" : "EM MOVIMENTO";
        window.TripDistanceText.Text = $"{_tripDistance:0.0} km • {stop}";
        window.TripDurationText.Text = $"{FormatDuration(elapsed)} • média {average:0.0} km/h • {l100:0.0} L/100 km";
    }

    private void AddEvent(string type, string details, TelemetrySnapshot data, DateTime now)
    {
        _events.Insert(0, new DrivingEventRecord { Id = Guid.NewGuid().ToString("N"), Type = type, Details = details, RecordedAtUtc = now, OdometerKm = data.OdometerKm, SpeedKph = Math.Abs(data.SpeedKph), Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(), Cargo = data.Cargo ?? "" });
        if (_events.Count > 500) _events.RemoveRange(500, _events.Count - 500);
        Save();
    }

    private bool RecentEvent(string type, int seconds) => _events.Any(x => x.Type == type && DateTime.UtcNow - x.RecordedAtUtc < TimeSpan.FromSeconds(seconds));
    private void Save() { try { File.WriteAllText(_path, JsonSerializer.Serialize(_events, new JsonSerializerOptions { WriteIndented = true })); } catch { } }
    private void Load() { try { if (File.Exists(_path)) _events.AddRange(JsonSerializer.Deserialize<List<DrivingEventRecord>>(File.ReadAllText(_path)) ?? new()); } catch { } }

    public void ShowHistory()
    {
        var window = System.Windows.Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        window?.OpenOperationalModalFromShortcut("summary");
    }

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
}

public sealed class DrivingEventRecord
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public float OdometerKm { get; set; }
    public float SpeedKph { get; set; }
    public string Truck { get; set; } = "";
    public string Cargo { get; set; } = "";
}
