using System;
using System.Collections.Generic;
using System.Linq;

namespace TransPoli;

/// <summary>
/// Read-only interpretation of the truck/trailer combination exposed by ETS2 telemetry.
/// No axle count is invented: axles are derived only when wheel longitudinal positions are usable.
/// </summary>
public static class RoadCombinationTelemetry
{
    public static RoadCombinationSnapshot Build(TelemetrySnapshot? data)
    {
        if (data is null || !data.Connected)
            return RoadCombinationSnapshot.Empty;

        var trailers = (data.Trailers ?? Array.Empty<TrailerTelemetry>())
            .Where(t => t is not null && t.Attached)
            .OrderBy(t => t.Index)
            .Select(BuildTrailer)
            .ToArray();

        var truckAxles = EstimateTruckAxles(data);
        var trailerAxlesKnown = trailers.All(t => t.AxleCount.HasValue);
        int? totalAxles = truckAxles.HasValue && trailerAxlesKnown
            ? truckAxles.Value + trailers.Sum(t => t.AxleCount!.Value)
            : null;

        return new RoadCombinationSnapshot(
            true,
            trailers.Length > 0,
            data.TruckBrand ?? "",
            data.TruckModel ?? "",
            data.LicensePlate ?? "",
            data.TruckWheelCount,
            truckAxles,
            trailers,
            Math.Max(0, data.CargoMassKg),
            totalAxles);
    }

    private static RoadTrailerSnapshot BuildTrailer(TrailerTelemetry t)
    {
        var count = Math.Clamp(t.WheelCount, 0, 16);
        var axles = EstimateAxles(count, t.WheelPositionZ, t.WheelSimulated);
        var grounded = CountTrue(t.WheelOnGround, count);
        return new RoadTrailerSnapshot(
            t.Index, t.Brand ?? "", t.Name ?? "", t.BodyType ?? "", t.LicensePlate ?? "",
            count, grounded, axles);
    }

    private static int? EstimateTruckAxles(TelemetrySnapshot data)
    {
        // Current desktop snapshot exposes truck wheel count but not truck wheel positions.
        // Do not guess truck axles from wheel count because dual tyres make that unreliable.
        return null;
    }

    private static int? EstimateAxles(int wheelCount, float[]? longitudinalPositions, bool[]? simulated)
    {
        if (wheelCount <= 0 || longitudinalPositions is null || longitudinalPositions.Length < wheelCount)
            return null;

        var positions = new List<float>();
        for (var i = 0; i < wheelCount; i++)
        {
            if (simulated is { Length: > 0 } && i < simulated.Length && !simulated[i]) continue;
            var z = longitudinalPositions[i];
            if (float.IsNaN(z) || float.IsInfinity(z)) continue;
            positions.Add(z);
        }
        if (positions.Count < 2) return null;

        positions.Sort();
        const float sameAxleToleranceMeters = 0.35f;
        var groups = 1;
        var anchor = positions[0];
        for (var i = 1; i < positions.Count; i++)
        {
            if (Math.Abs(positions[i] - anchor) <= sameAxleToleranceMeters) continue;
            groups++;
            anchor = positions[i];
        }
        return groups > 0 ? groups : null;
    }

    private static int CountTrue(bool[]? values, int count)
    {
        if (values is null) return 0;
        var max = Math.Min(count, values.Length);
        var result = 0;
        for (var i = 0; i < max; i++) if (values[i]) result++;
        return result;
    }
}

public sealed record RoadCombinationSnapshot(
    bool Connected,
    bool HasTrailer,
    string TruckBrand,
    string TruckModel,
    string TruckPlate,
    int TruckWheelCount,
    int? TruckAxleCount,
    IReadOnlyList<RoadTrailerSnapshot> Trailers,
    float CargoMassKg,
    int? TotalAxleCount)
{
    public static RoadCombinationSnapshot Empty { get; } =
        new(false, false, "", "", "", 0, null, Array.Empty<RoadTrailerSnapshot>(), 0, null);
}

public sealed record RoadTrailerSnapshot(
    int Index,
    string Brand,
    string Name,
    string BodyType,
    string LicensePlate,
    int WheelCount,
    int GroundedWheelCount,
    int? AxleCount);
