using System;
using System.Linq;
using TransPoli.GameSave;

namespace TransPoli;

/// <summary>
/// Projeção somente-leitura do veículo atual.
/// TELEMETRY é autoridade para estado vivo; GAME_SAVE apenas complementa/persiste.
/// Não contém economia e não altera o ETS2.
/// </summary>
internal sealed record VehicleIntelligenceSnapshot(
    string TruckId,string LicensePlate,string Brand,string Model,double? OdometerKm,
    double? EngineWear,double? TransmissionWear,double? CabinWear,double? ChassisWear,double? WheelsWear,
    string EngineDefinition,string TransmissionDefinition,string CabinDefinition,string ChassisDefinition,
    string LiveSource,string PersistentSource,bool IsLive,DateTime? SaveParsedAtUtc);

internal sealed record TrailerIntelligenceSnapshot(
    string TrailerId,string LicensePlate,string Definition,string BodyType,int? WheelCount,
    double? CargoMassKg,double? CargoDamage,double? BodyWear,double? ChassisWear,double? WheelsWear,
    string LiveSource,string PersistentSource,bool IsLive,DateTime? SaveParsedAtUtc);

internal static class VehicleIntelligence
{
    public static VehicleIntelligenceSnapshot Resolve(TelemetrySnapshot? live,GameSaveSnapshot? save)
    {
        var telemetryLive=live is { Connected:true };
        var persisted=save?.CurrentTruck;

        string Pick(string? liveValue,string? saveValue)=>
            telemetryLive && !string.IsNullOrWhiteSpace(liveValue) ? liveValue!.Trim() :
            !string.IsNullOrWhiteSpace(saveValue) ? saveValue!.Trim() : "";

        double? LiveOrSave(double liveValue,double saveValue)=>
            telemetryLive ? liveValue : persisted is not null ? saveValue : null;

        return new(
            Pick(live?.TruckId,persisted?.Id),
            Pick(live?.LicensePlate,persisted?.LicensePlate),
            telemetryLive ? (live?.TruckBrand?.Trim()??"") : "",
            telemetryLive ? (live?.TruckModel?.Trim()??"") : "",
            LiveOrSave(live?.OdometerKm??0,persisted?.OdometerKm??0),
            LiveOrSave(live?.WearEngine??0,persisted?.EngineWear??0),
            LiveOrSave(live?.WearTransmission??0,persisted?.TransmissionWear??0),
            LiveOrSave(live?.WearCabin??0,persisted?.CabinWear??0),
            LiveOrSave(live?.WearChassis??0,persisted?.ChassisWear??0),
            LiveOrSave(live?.WearWheels??0,persisted?.WheelsWear??0),
            persisted?.EngineDefinition??"",
            persisted?.TransmissionDefinition??"",
            persisted?.CabinDefinition??"",
            persisted?.ChassisDefinition??"",
            telemetryLive?"SCS_SDK":"UNAVAILABLE",
            persisted is not null?"GAME_SAVE":"UNAVAILABLE",
            telemetryLive,
            save is null?null:save.ParsedAtUtc);
    }

    public static TrailerIntelligenceSnapshot ResolveTrailer(TelemetrySnapshot? live,GameSaveSnapshot? save)
    {
        var telemetryLive=live is { Connected:true };
        var liveTrailer=telemetryLive?live!.Trailers.FirstOrDefault(x=>x.Attached):null;
        var persisted=save?.CurrentTrailer;

        string Pick(string? liveValue,string? saveValue)=>
            !string.IsNullOrWhiteSpace(liveValue)?liveValue!.Trim():
            !string.IsNullOrWhiteSpace(saveValue)?saveValue!.Trim():"";

        return new(
            Pick(liveTrailer?.Id,persisted?.Id),
            Pick(liveTrailer?.LicensePlate,persisted?.LicensePlate),
            persisted?.Definition??"",
            liveTrailer?.BodyType?.Trim()??"",
            liveTrailer is null?null:liveTrailer.WheelCount,
            telemetryLive?live?.CargoMassKg:persisted?.CargoMassKg,
            liveTrailer is not null?liveTrailer.CargoDamage:persisted?.CargoDamage,
            liveTrailer is not null?liveTrailer.WearBody:persisted?.TrailerBodyWear,
            liveTrailer is not null?liveTrailer.WearChassis:persisted?.ChassisWear,
            liveTrailer is not null?liveTrailer.WearWheels:persisted?.WheelsWear,
            liveTrailer is not null?"SCS_SDK":"UNAVAILABLE",
            persisted is not null?"GAME_SAVE":"UNAVAILABLE",
            liveTrailer is not null,
            save is null?null:save.ParsedAtUtc);
    }
}
