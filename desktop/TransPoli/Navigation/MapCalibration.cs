using System;

namespace TransPoli.Navigation;

/// <summary>
/// Parâmetros extraídos do climate.sii/mapa do ETS2.
/// Enabled só deve ser true quando os parâmetros forem confirmados para o mapa.
/// </summary>
public sealed record MapCalibration
{
    public bool Enabled { get; init; }
    public string Projection { get; init; } = "mercator";

    public double MapOriginLatitude { get; init; }
    public double MapOriginLongitude { get; init; }

    public double MapFactorLatitude { get; init; }
    public double MapFactorLongitude { get; init; }

    public double MapOffsetZ { get; init; }
    public double MapOffsetX { get; init; }

    public double? StandardParallel1 { get; init; }
    public double? StandardParallel2 { get; init; }

    public bool IsUsable
        => Enabled &&
           IsSupportedProjection(Projection) &&
           IsFinite(MapOriginLatitude) &&
           IsFinite(MapOriginLongitude) &&
           IsFinite(MapFactorLatitude) &&
           IsFinite(MapFactorLongitude) &&
           MapFactorLatitude != 0d &&
           MapFactorLongitude != 0d;

    private static bool IsSupportedProjection(string value)
        => string.Equals(value, "mercator", StringComparison.OrdinalIgnoreCase);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
