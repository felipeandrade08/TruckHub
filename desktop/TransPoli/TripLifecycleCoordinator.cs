using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TransPoli;

/// <summary>
/// Fonte única do ciclo operacional da viagem. Não cria telemetria nem usa a economia do ETS2.
/// Os demais módulos podem observar este estado em vez de inferir uma viagem separadamente.
/// </summary>
public enum TripLifecycleStage
{
    Idle,
    CargoDetected,
    DocumentationPending,
    Authorized,
    InTransit,
    Arrived,
    Closing,
    Finished,
    Interrupted
}

public sealed class TripLifecycleEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public TripLifecycleStage Stage { get; set; }
    public string Type { get; set; } = "";
    public string Details { get; set; } = "";
    public float OdometerKm { get; set; }
    public float FuelLiters { get; set; }
}

public sealed class TripLifecycleSnapshot
{
    public string SessionKey { get; set; } = "";
    public TripLifecycleStage Stage { get; set; } = TripLifecycleStage.Idle;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Cargo { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Truck { get; set; } = "";
    public string Plate { get; set; } = "";
    public float StartOdometerKm { get; set; }
    public float LastOdometerKm { get; set; }
    public float StartFuelLiters { get; set; }
    public float LastFuelLiters { get; set; }
    public double MovingSeconds { get; set; }
    public double StoppedSeconds { get; set; }
    public float FuelConsumedLiters { get; set; }
    public int HarshBrakingCount { get; set; }
    public int HarshAccelerationCount { get; set; }
    public int SpeedEventCount { get; set; }
    public float PeakWear { get; set; }
    public double IncomeBrl { get; set; }
    public double ExpensesBrl { get; set; }
    public double NetBrl { get; set; }
    public double FuelExpensesBrl { get; set; }
    public double MaintenanceExpensesBrl { get; set; }
    public List<TripLifecycleEvent> Events { get; set; } = new();
}

/// <summary>
/// Diário de bordo persistente e tolerante a reinício/crash. A identidade usa dados estáveis
/// da operação e nunca o odômetro mutável.
/// </summary>
public sealed class TripLifecycleCoordinator
{
    private readonly string _path;
    private DateTime _lastSampleUtc = DateTime.MinValue;
    private bool _wasMoving;
    private float _lastSpeed;
    private DateTime _lastSafetyEventUtc = DateTime.MinValue;
    private DateTime _lastMaintenanceEventUtc = DateTime.MinValue;
    public TripLifecycleSnapshot Current { get; private set; } = new();
    public event Action<TripLifecycleEvent>? EventRecorded;

    public TripLifecycleCoordinator()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "trip-lifecycle.json");
        Load();
    }

    public void Observe(TelemetrySnapshot data, bool tripActive, bool documentPending)
    {
        if (!data.Connected) return;
        var now = DateTime.UtcNow;
        var hasJob = !string.IsNullOrWhiteSpace(data.Cargo) &&
                     (!string.IsNullOrWhiteSpace(data.SourceCity) || !string.IsNullOrWhiteSpace(data.DestinationCity));
        var key = BuildKey(data);

        if (!hasJob && !tripActive)
        {
            if (Current.Stage is not TripLifecycleStage.Idle and not TripLifecycleStage.Finished)
                Transition(TripLifecycleStage.Interrupted, "JOB_INDISPONIVEL", "A operação deixou de ser reportada pela telemetria.", data);
            return;
        }

        if (hasJob && !string.Equals(Current.SessionKey, key, StringComparison.OrdinalIgnoreCase))
        {
            Current = new TripLifecycleSnapshot
            {
                SessionKey = key,
                Stage = TripLifecycleStage.CargoDetected,
                UpdatedAtUtc = now,
                Cargo = data.Cargo ?? "",
                Origin = data.SourceCity ?? "",
                Destination = data.DestinationCity ?? "",
                Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(),
                Plate = data.LicensePlate ?? "",
                StartOdometerKm = data.OdometerKm,
                LastOdometerKm = data.OdometerKm,
                StartFuelLiters = data.FuelLiters,
                LastFuelLiters = data.FuelLiters
            };
            AddEvent("CARGA_DETECTADA", "Nova operação real detectada pela telemetria ETS2.", data);
        }

        if (documentPending)
            Transition(TripLifecycleStage.DocumentationPending, "DOCUMENTO_PENDENTE", "Aguardando liberação documental.", data);
        else if (tripActive && Current.Stage < TripLifecycleStage.Authorized)
            Transition(TripLifecycleStage.Authorized, "VIAGEM_AUTORIZADA", "Documento liberado; sessão operacional iniciada.", data);

        if (tripActive)
        {
            var moving = !data.GamePaused && Math.Abs(data.SpeedKph) > 0.8f;
            if (moving && Current.Stage is TripLifecycleStage.Authorized or TripLifecycleStage.CargoDetected)
                Transition(TripLifecycleStage.InTransit, "SAIDA", "Movimento da viagem confirmado.", data);

            var dt = _lastSampleUtc == DateTime.MinValue ? 0d : Math.Clamp((now - _lastSampleUtc).TotalSeconds, 0d, 10d);
            if (!data.GamePaused)
            {
                if (moving) Current.MovingSeconds += dt;
                else Current.StoppedSeconds += dt;
            }

            if (moving != _wasMoving && _lastSampleUtc != DateTime.MinValue)
                AddEvent(moving ? "MOVIMENTO_RETOMADO" : "PARADA", moving ? "Veículo voltou a se mover." : "Veículo parado durante a viagem.", data);

            if (data.RouteDistanceKm > 0 && data.RouteDistanceKm <= 0.3f)
                Transition(TripLifecycleStage.Arrived, "DESTINO_ALCANCADO", "Distância restante da rota chegou ao destino.", data);

            _wasMoving = moving;
        }

        var fuelDrop = Current.LastFuelLiters - data.FuelLiters;
        if (tripActive && fuelDrop > 0 && fuelDrop <= 5f)
            Current.FuelConsumedLiters += fuelDrop;

        var sampleSeconds = _lastSampleUtc == DateTime.MinValue ? 0d : Math.Max(0.5d, (now - _lastSampleUtc).TotalSeconds);
        var acceleration = (Math.Abs(data.SpeedKph) - _lastSpeed) / sampleSeconds;
        if (tripActive && now - _lastSafetyEventUtc > TimeSpan.FromSeconds(15))
        {
            if (_lastSpeed >= 25 && acceleration <= -18)
            {
                Current.HarshBrakingCount++;
                _lastSafetyEventUtc = now;
                AddEvent("FREIADA_BRUSCA", $"Redução estimada de {Math.Abs(acceleration):0} km/h por segundo.", data);
            }
            else if (_lastSpeed >= 15 && acceleration >= 15)
            {
                Current.HarshAccelerationCount++;
                _lastSafetyEventUtc = now;
                AddEvent("ACELERACAO_BRUSCA", $"Aceleração estimada de {acceleration:0} km/h por segundo.", data);
            }
            else if (Math.Abs(data.SpeedKph) > 110)
            {
                Current.SpeedEventCount++;
                _lastSafetyEventUtc = now;
                AddEvent("VELOCIDADE_ELEVADA", $"Velocidade registrada: {Math.Abs(data.SpeedKph):0} km/h.", data);
            }
        }

        var wear = Math.Max(Math.Max(data.WearEngine, data.WearTransmission), Math.Max(Math.Max(data.WearCabin, data.WearChassis), data.WearWheels));
        Current.PeakWear = Math.Max(Current.PeakWear, wear);
        if (tripActive && wear >= 0.75f && now - _lastMaintenanceEventUtc > TimeSpan.FromMinutes(30))
        {
            _lastMaintenanceEventUtc = now;
            AddEvent("MANUTENCAO_CRITICA", $"Desgaste máximo detectado em {wear * 100f:0}%.", data);
        }

        _lastSpeed = Math.Abs(data.SpeedKph);
        Current.LastOdometerKm = data.OdometerKm;
        Current.LastFuelLiters = data.FuelLiters;
        Current.UpdatedAtUtc = now;
        _lastSampleUtc = now;
        Save();
    }

    internal void ApplyFinancialSummary(TripFinancialSummary summary)
    {
        Current.IncomeBrl = summary.Income;
        Current.ExpensesBrl = summary.Expenses;
        Current.NetBrl = summary.Net;
        Current.FuelExpensesBrl = summary.FuelExpenses;
        Current.MaintenanceExpensesBrl = summary.MaintenanceExpenses;
        Current.UpdatedAtUtc = DateTime.UtcNow;
        Save();
    }

    public void MarkFinished(TelemetrySnapshot data, string details)
    {
        Transition(TripLifecycleStage.Finished, "VIAGEM_ENCERRADA", details, data);
        Save();
    }

    private void Transition(TripLifecycleStage stage, string type, string details, TelemetrySnapshot data)
    {
        if (Current.Stage == stage) return;
        Current.Stage = stage;
        AddEvent(type, details, data);
    }

    private void AddEvent(string type, string details, TelemetrySnapshot data)
    {
        var recorded = new TripLifecycleEvent
        {
            Stage = Current.Stage,
            Type = type,
            Details = details,
            OdometerKm = data.OdometerKm,
            FuelLiters = data.FuelLiters
        };
        Current.Events.Add(recorded);
        EventRecorded?.Invoke(recorded);
        if (Current.Events.Count > 250) Current.Events.RemoveRange(0, Current.Events.Count - 250);
        Current.UpdatedAtUtc = DateTime.UtcNow;
        _ = TrySave();
    }

    private static string BuildKey(TelemetrySnapshot data) =>
        $"{N(data.Cargo)}|{N(data.SourceCity)}|{N(data.SourceCompany)}|{N(data.DestinationCity)}|{N(data.DestinationCompany)}";

    private static string N(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant();

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            Current = JsonSerializer.Deserialize<TripLifecycleSnapshot>(File.ReadAllText(_path)) ?? new();
        }
        catch { Current = new(); }
    }

    private bool TrySave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp=_path+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(Current,new JsonSerializerOptions { WriteIndented=true }));
            File.Move(temp,_path,true);
            return true;
        }
        catch { return false; }
    }
}
