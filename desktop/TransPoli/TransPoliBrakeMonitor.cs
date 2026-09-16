using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly TransPoliBrakeMonitor _brakeMonitor = new();
}

public sealed class TransPoliBrakeMonitor
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _historyPath;
    private double _operationalWear;
    private double _peakBrakeTemp;
    private double _peakThermalLoad;
    private int _hardBrakings;
    private int _impactEvents;
    private int _brakeOverheatEvents;
    private bool _ready;
    private bool _previousImpact;
    private double _previousSpeed;
    private double _previousDamage;
    private double _previousWheelWear;
    private DateTime _previousAt;

    public TransPoliBrakeMonitor()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _historyPath = Path.Combine(folder, "transpoli-brake-monitor.json");
        Load();
        _timer.Tick += async (_, _) => await TickAsync();
        Application.Current?.Dispatcher.BeginInvoke(new Action(() => _timer.Start()), DispatcherPriority.Loaded);
    }

    private async Task TickAsync()
    {
        try
        {
            using var response = await _http.GetAsync("http://127.0.0.1:17877/telemetry");
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;

            var now = DateTime.UtcNow;
            var dt = _ready ? Math.Clamp((now - _previousAt).TotalSeconds, 0.2, 2.5) : 1.0;
            var speed = Math.Max(0, data.SpeedKph);
            var acceleration = _ready ? (speed - _previousSpeed) / dt : 0;
            var hardBraking = acceleration <= -12 && speed >= 25 && data.EffectiveBrake >= 0.25f;
            var damage = Math.Clamp(data.CargoDamage, 0, 1);
            var wheelWear = Math.Clamp(data.WearWheels, 0, 1);
            var damageJump = _ready ? damage - _previousDamage : 0;
            var wheelWearJump = _ready ? wheelWear - _previousWheelWear : 0;
            var impactDetected = damageJump >= 0.002 || wheelWearJump >= 0.0015;
            var temp = Math.Max(0, data.BrakeTemperature);
            var brake = Math.Clamp(Math.Max(data.UserBrake, data.EffectiveBrake), 0, 1);
            var thermalLoad = Math.Clamp((temp - 80.0) / 420.0 * 100.0, 0, 100);

            _peakBrakeTemp = Math.Max(_peakBrakeTemp, temp);
            var crossedOverheat = thermalLoad >= 85 && _peakThermalLoad < 85;
            _peakThermalLoad = Math.Max(_peakThermalLoad, thermalLoad);
            if (hardBraking) _hardBrakings++;
            if (impactDetected && !_previousImpact) _impactEvents++;
            if (crossedOverheat) _brakeOverheatEvents++;
            _previousImpact = impactDetected;

            var wearPerSecond = brake * brake * 0.000012;
            if (thermalLoad >= 65) wearPerSecond += 0.000018 * Math.Clamp((thermalLoad - 64) / 36.0, 0, 1);
            if (hardBraking) wearPerSecond += 0.00008;
            if (impactDetected) wearPerSecond += 0.00012;
            _operationalWear = Math.Clamp(_operationalWear + wearPerSecond, 0, 1);

            UpdateUi(data, thermalLoad, acceleration, hardBraking, impactDetected, damage);
            Save();
            _previousSpeed = speed; _previousDamage = damage; _previousWheelWear = wheelWear; _previousAt = now; _ready = true;
        }
        catch { }
    }

    private void UpdateUi(TelemetrySnapshot data, double thermalLoad, double acceleration, bool hardBraking, bool impactDetected, double cargoDamage)
    {
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null) return;
        var tempText = data.BrakeTemperature > 1 ? $"{data.BrakeTemperature:0}°C" : "não disponível";
        var thermalState = thermalLoad >= 85 ? "CRÍTICO" : thermalLoad >= 65 ? "ALTO" : thermalLoad >= 40 ? "AQUECIMENTO" : "NORMAL";
        SetText(main, "TelemetryInfoText", $"Freios: {thermalState} • {tempText} • carga {thermalLoad:0}% • desgaste operacional {_operationalWear * 100:0.00}% • rodas ETS2 {data.WearWheels * 100:0.0}%");
        if (impactDetected) SetText(main, "AlertText", $"⚠ IMPACTO/AVARIA detectado. Dano de carga ETS2: {cargoDamage * 100:0.0}% • manutenção dos freios atualizada.");
        else if (thermalLoad >= 85) SetText(main, "AlertText", "⚠ FREIOS: temperatura/carga térmica crítica. Reduza a frenagem e deixe o conjunto resfriar.");
        else if (hardBraking) SetText(main, "AlertText", $"⚠ FREADA BRUSCA detectada ({Math.Abs(acceleration):0} km/h/s). Temperatura: {tempText}.");
    }

    private static void SetText(MainWindow main, string name, string value)
    {
        try { var field = main.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); if (field?.GetValue(main) is System.Windows.Controls.TextBlock text) text.Text = value; } catch { }
    }
    private void Load()
    {
        try { if (!File.Exists(_historyPath)) return; var data = JsonSerializer.Deserialize<BrakeHistory>(File.ReadAllText(_historyPath)); if (data is null) return; _operationalWear = Math.Clamp(data.OperationalWear, 0, 1); _peakBrakeTemp = Math.Max(0, data.PeakBrakeTemp); _peakThermalLoad = Math.Clamp(data.PeakThermalLoad, 0, 100); _hardBrakings = Math.Max(0, data.HardBrakings); _impactEvents = Math.Max(0, data.ImpactEvents); _brakeOverheatEvents = Math.Max(0, data.BrakeOverheatEvents); } catch { }
    }
    private void Save()
    {
        try { File.WriteAllText(_historyPath, JsonSerializer.Serialize(new BrakeHistory { OperationalWear = _operationalWear, PeakBrakeTemp = _peakBrakeTemp, PeakThermalLoad = _peakThermalLoad, HardBrakings = _hardBrakings, ImpactEvents = _impactEvents, BrakeOverheatEvents = _brakeOverheatEvents, UpdatedAtUtc = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
    private sealed class BrakeHistory { public double OperationalWear { get; set; } public double PeakBrakeTemp { get; set; } public double PeakThermalLoad { get; set; } public int HardBrakings { get; set; } public int ImpactEvents { get; set; } public int BrakeOverheatEvents { get; set; } public DateTime UpdatedAtUtc { get; set; } }
    private sealed class TelemetrySnapshot { public bool Connected { get; set; } public float SpeedKph { get; set; } public float UserBrake { get; set; } public float EffectiveBrake { get; set; } public float BrakeTemperature { get; set; } public float WearWheels { get; set; } public float CargoDamage { get; set; } }
}
