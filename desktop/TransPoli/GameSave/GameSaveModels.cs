using System;
using System.Collections.Generic;

namespace TransPoli.GameSave;

public sealed class GameSaveSnapshot
{
    public DateTime ParsedAtUtc { get; init; }
    public int BlockCount { get; init; }
    public int RawTextLength { get; init; }
    public string? HeadquartersCity { get; init; }

    // Intentionally no ETS2 money/economy properties.
    public SaveParseStatus Status { get; init; } = SaveParseStatus.Success;
}

public enum SaveParseStatus
{
    Success,
    FileNotFound,
    Empty,
    UnsupportedBinaryOrEncrypted,
    ParseError
}

public sealed record GameSiiFileInfo(
    string Path,
    DateTime LastWriteTimeUtc,
    long Length);

public sealed class SaveTruck
{
    public string Id { get; init; } = string.Empty;
    public string Definition { get; init; } = string.Empty;
    public string LicensePlate { get; init; } = string.Empty;
}

public sealed class SaveTrailer
{
    public string Id { get; init; } = string.Empty;
    public string Definition { get; init; } = string.Empty;
    public string LicensePlate { get; init; } = string.Empty;
}

public sealed class SaveDriverStats
{
    public int DrivingMinutes { get; init; }
    public int MinutesSinceMandatoryBreak { get; init; }
    public int BreakMinutes { get; init; }
    public int VisitedCities { get; init; }
    public int ServiceVisits { get; init; }
    public int GasStationVisits { get; init; }
    public int CrashCount { get; init; }
    public int CancelledJobs { get; init; }
    public double TotalFuelLiters { get; init; }
}
