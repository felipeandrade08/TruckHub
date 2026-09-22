using System;
using System.Collections.Generic;

namespace TransPoli.GameSave;

public sealed class GameSaveSnapshot
{
    public DateTime ParsedAtUtc { get; init; }
    public int BlockCount { get; init; }
    public int RawTextLength { get; init; }
    public string? HeadquartersCity { get; set; }

    // Phase B: persistent data for the truck currently assigned to the player.
    // Real-time values continue to come from telemetry.
    public SaveTruck? CurrentTruck { get; set; }

    // Phase C: currently attached trailer plus the owned truck/trailer fleet.
    public SaveTrailer? CurrentTrailer { get; set; }
    public IReadOnlyList<SaveTruck> Trucks { get; set; } = Array.Empty<SaveTruck>();
    public IReadOnlyList<SaveTrailer> Trailers { get; set; } = Array.Empty<SaveTrailer>();

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
    public string LicensePlateCountry { get; init; } = string.Empty;
    public string LicensePlateType { get; init; } = string.Empty;

    public string CabinDefinition { get; init; } = string.Empty;
    public string InteriorDefinition { get; init; } = string.Empty;
    public string TransmissionDefinition { get; init; } = string.Empty;
    public string ChassisDefinition { get; init; } = string.Empty;
    public string EngineDefinition { get; init; } = string.Empty;

    public double OdometerKm { get; init; }
    public double IntegrityOdometerKm { get; init; }
    public double FuelRelative { get; init; }
    public double FuelPercent => Math.Clamp(FuelRelative * 100.0, 0.0, 100.0);

    public double TripFuelLiters { get; init; }
    public double TripDistanceKm { get; init; }
    public double TripTimeMinutes { get; init; }

    public double EngineWear { get; init; }
    public double TransmissionWear { get; init; }
    public double CabinWear { get; init; }
    public double ChassisWear { get; init; }
    public double WheelsWear { get; init; }

    public double EngineWearUnfixable { get; init; }
    public double TransmissionWearUnfixable { get; init; }
    public double CabinWearUnfixable { get; init; }
    public double ChassisWearUnfixable { get; init; }
    public double WheelsWearUnfixable { get; init; }
}

public sealed class SaveTrailer
{
    public string Id { get; init; } = string.Empty;
    public string Definition { get; init; } = string.Empty;
    public string LicensePlate { get; init; } = string.Empty;
    public string LicensePlateCountry { get; init; } = string.Empty;
    public string LicensePlateType { get; init; } = string.Empty;

    public double CargoMassKg { get; init; }
    public double CargoDamage { get; init; }
    public double TrailerBodyWear { get; init; }
    public double ChassisWear { get; init; }
    public double WheelsWear { get; init; }

    public double TrailerBodyWearUnfixable { get; init; }
    public double ChassisWearUnfixable { get; init; }
    public double WheelsWearUnfixable { get; init; }
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
