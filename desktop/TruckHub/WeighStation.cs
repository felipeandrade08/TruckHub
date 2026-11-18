// Sistema de balança (weigh station) do TruckHub.
//
// O que a telemetria da SCS realmente informa (SDK 1.14):
//   • multa "avoid_weighing"    -> o motorista passou direto pela balança (ATS e ETS2 Road to the Black Sea)
//   • multa "avoid_inspection"  -> o motorista ignorou a inspeção
//   • massa da CARGA (kg)       -> usada para estimar o peso bruto
// O que a telemetria NÃO informa: leitura da balança, tara do caminhão/reboque, abertura da balança,
// localização das balanças. Por isso o peso bruto exibido é sempre ESTIMADO (carga real + tara configurável).
using System.IO;
using System.Text.Json;

namespace TruckHub;

public sealed record WeighEstimate(
    bool Valid,
    bool Over,
    float CargoKg,
    float TareKg,
    float GrossKg,
    float LimitKg,
    int Trailers)
{
    public float Ratio => LimitKg > 0 ? GrossKg / LimitKg : 0;
    public float MarginKg => LimitKg - GrossKg;
    public string Text => Valid ? $"≈{GrossKg / 1000f:0.0}t / {LimitKg / 1000f:0.0}t" : "—";
    public string Pounds => Valid ? $"{GrossKg * 2.2046226f:N0} lb / {LimitKg * 2.2046226f:N0} lb" : "—";
}

public sealed class WeighEvent
{
    public DateTime AtUtc { get; set; }
    public string Offence { get; set; } = "";
    public string Game { get; set; } = "";
    public long FineAmount { get; set; }
    public float OdometerKm { get; set; }
    public string Cargo { get; set; } = "";
    public float CargoKg { get; set; }
    public string Truck { get; set; } = "";
}

public static class WeighStation
{
    /// <summary>Limite federal dos EUA para peso bruto combinado: 80.000 lb.</summary>
    public const float UsFederalGrossLimitKg = 36287f;

    private static readonly object Gate = new();
    private static List<WeighEvent>? _events;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TruckHub", "weigh-stations.json");

    public static bool IsWeighOffence(string? offence)
    {
        var id = Normalize(offence);
        return id is "avoid_weighing" or "avoid_weighting" or "avoid_inspection";
    }

    private static string Normalize(string? offence) =>
        (offence ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    /// <summary>Nome amigável (pt-BR) das infrações que a SCS envia em inglês técnico.</summary>
    public static string OffenceName(string? offence) => Normalize(offence) switch
    {
        "" => "Infração",
        "crash" => "Colisão",
        "avoid_sleeping" => "Sem descanso",
        "wrong_way" => "Contramão",
        "speeding_camera" => "Radar de velocidade",
        "no_lights" => "Faróis apagados",
        "red_signal" => "Sinal vermelho",
        "speeding" => "Excesso de velocidade",
        "avoid_weighing" or "avoid_weighting" => "Balança ignorada",
        "illegal_trailer" => "Reboque irregular",
        "avoid_inspection" => "Inspeção ignorada",
        "illegal_border_crossing" => "Fronteira irregular",
        "hard_shoulder_violation" => "Acostamento indevido",
        "damaged_vehicle_usage" => "Veículo danificado",
        "generic" => "Infração",
        var other => other.Replace('_', ' ')
    };

    /// <summary>Estimativa do peso bruto do conjunto (ATS). Sempre aproximada: tara vem das configurações.</summary>
    public static WeighEstimate Estimate(TelemetrySnapshot data, HudSettings settings)
    {
        if (!data.Connected) return new WeighEstimate(false, false, 0, 0, 0, UsFederalGrossLimitKg, 0);
        var trailers = data.Trailers?.Count(t => t.Attached) ?? 0;
        var cargo = Math.Max(0f, data.CargoMassKg);
        var tare = (float)Math.Max(0, settings.TruckTareKg) + trailers * (float)Math.Max(0, settings.TrailerTareKg);
        var gross = cargo + tare;
        return new WeighEstimate(true, gross > UsFederalGrossLimitKg, cargo, tare, gross, UsFederalGrossLimitKg, trailers);
    }

    // ---------------------------------------------------------------- histórico persistido

    public static IReadOnlyList<WeighEvent> History
    {
        get { lock (Gate) { Load(); return _events!.ToList(); } }
    }

    public static int ViolationCount
    {
        get { lock (Gate) { Load(); return _events!.Count; } }
    }

    public static WeighEvent? Last
    {
        get { lock (Gate) { Load(); return _events!.Count == 0 ? null : _events[^1]; } }
    }

    /// <summary>Registra uma infração de balança. Retorna false se for duplicada (mesma multa em poucos segundos).</summary>
    public static bool Register(TelemetrySnapshot data)
    {
        lock (Gate)
        {
            Load();
            var now = DateTime.UtcNow;
            var last = _events!.Count == 0 ? null : _events[^1];
            if (last is not null && last.FineAmount == data.FineAmount &&
                string.Equals(Normalize(last.Offence), Normalize(data.FineOffence), StringComparison.Ordinal) &&
                (now - last.AtUtc).TotalSeconds < 60) return false;

            _events.Add(new WeighEvent
            {
                AtUtc = now,
                Offence = Normalize(data.FineOffence),
                Game = TpGame.Label,
                FineAmount = data.FineAmount,
                OdometerKm = data.OdometerKm,
                Cargo = data.Cargo ?? "",
                CargoKg = Math.Max(0f, data.CargoMassKg),
                Truck = $"{data.TruckBrand} {data.TruckModel}".Trim()
            });
            if (_events.Count > 500) _events.RemoveRange(0, _events.Count - 500);
            Save();
            return true;
        }
    }

    private static void Load()
    {
        if (_events is not null) return;
        try
        {
            _events = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<WeighEvent>>(File.ReadAllText(FilePath)) ?? new()
                : new();
        }
        catch { _events = new(); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_events, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
