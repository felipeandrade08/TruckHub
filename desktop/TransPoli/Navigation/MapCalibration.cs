using System;

namespace TransPoli.Navigation;

/// <summary>
/// Parâmetros extraídos do climate.sii/mapa do ETS2.
/// Não contém valores inventados: Enabled só deve ser true quando
/// os parâmetros forem confirmados para o mapa em execução.
/// </summary>
public sealed record MapCalibration
{
    public bool Enabled { get; init; }
    public string Projection { get; init; } = "mercator";

    public double MapOriginLatitude { get; init; }
    public double MapOriginLongitude { get; init; }

    public double MapFactorLatitude { get; init; }
    public double MapFactorLongitude { get; init; }

    // Opcional para mapas que explicitamente definem deslocamento.
    public double MapOffsetZ { get; init; }
    public double MapOffsetX { get; init; }

    public bool IsUsable
        => Enabled &&
           string.Equals(Projection, "mercator", StringComparison.OrdinalIgnoreCase) &&
           IsFinite(MapOriginLatitude) &&
           IsFinite(MapOriginLongitude) &&
           IsFinite(MapFactorLatitude) &&
           IsFinite(MapFactorLongitude) &&
           MapFactorLatitude != 0d &&
           MapFactorLongitude != 0d;

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
