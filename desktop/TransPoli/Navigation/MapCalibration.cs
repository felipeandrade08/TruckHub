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

    // Extraída do climate.sii não significa calibrada: a confiança geográfica
    // só é liberada após validação contra um ponto conhecido do ETS2.
    public bool IsValidated { get; init; }

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
           MapFactorLongitude != 0d &&
           IsProjectionConfigurationValid();

    private bool IsProjectionConfigurationValid()
    {
        if (!string.Equals(Projection, "lambert_conic", StringComparison.OrdinalIgnoreCase))
            return true;

        return StandardParallel1.HasValue &&
               StandardParallel2.HasValue &&
               IsFinite(StandardParallel1.Value) &&
               IsFinite(StandardParallel2.Value) &&
               StandardParallel1.Value > -90d &&
               StandardParallel1.Value < 90d &&
               StandardParallel2.Value > -90d &&
               StandardParallel2.Value < 90d &&
               StandardParallel1.Value != StandardParallel2.Value;
    }

    private static bool IsSupportedProjection(string value)
        => string.Equals(value, "mercator", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "lambert_conic", StringComparison.OrdinalIgnoreCase);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
