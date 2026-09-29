using System;

namespace TransPoli.Intelligence;

/// <summary>
/// Contrato comum de proveniência. Inteligência pode enriquecer um valor, mas nunca
/// esconder de onde ele veio nem transformar ausência em zero.
/// </summary>
internal enum DataSourceKind
{
    Unknown,
    Telemetry,
    GameSave,
    Server,
    LocalCache,
    Derived
}

internal enum DataFreshnessState
{
    Unknown,
    Live,
    Persisted,
    Offline
}

internal enum DataConfidence
{
    Unknown,
    Low,
    Medium,
    High
}

internal sealed record ProvenancedValue<T>(
    T? Value,
    DataSourceKind Source,
    DataFreshnessState State,
    DateTime? CapturedAtUtc = null,
    DateTime? SyncedAtUtc = null,
    DataConfidence Confidence = DataConfidence.Unknown)
{
    public bool HasValue => Value is not null;
    public string StateLabel => State switch
    {
        DataFreshnessState.Live => "AO VIVO",
        DataFreshnessState.Persisted => "PERSISTIDO",
        DataFreshnessState.Offline => "OFFLINE",
        _ => "N/D"
    };
    public string SourceLabel => Source switch
    {
        DataSourceKind.Telemetry => "TELEMETRIA",
        DataSourceKind.GameSave => "GAME.SII",
        DataSourceKind.Server => "SERVIDOR",
        DataSourceKind.LocalCache => "CACHE LOCAL",
        DataSourceKind.Derived => "DERIVADO",
        _ => "N/D"
    };
}

internal static class DataProvenance
{
    public static ProvenancedValue<T> Live<T>(T value, DateTime? capturedAtUtc = null) =>
        new(value, DataSourceKind.Telemetry, DataFreshnessState.Live, capturedAtUtc, null, DataConfidence.High);

    public static ProvenancedValue<T> Persisted<T>(T value, DataSourceKind source, DateTime? capturedAtUtc = null, DateTime? syncedAtUtc = null, DataConfidence confidence = DataConfidence.High) =>
        new(value, source, DataFreshnessState.Persisted, capturedAtUtc, syncedAtUtc, confidence);

    public static ProvenancedValue<T> Offline<T>(T value, DataSourceKind source, DateTime? capturedAtUtc = null, DataConfidence confidence = DataConfidence.Medium) =>
        new(value, source, DataFreshnessState.Offline, capturedAtUtc, null, confidence);

    public static ProvenancedValue<T> Unknown<T>() =>
        new(default, DataSourceKind.Unknown, DataFreshnessState.Unknown);
}
