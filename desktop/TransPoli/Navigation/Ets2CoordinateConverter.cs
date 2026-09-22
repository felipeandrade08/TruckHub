using System;

namespace TransPoli.Navigation;

/// <summary>
/// Converte coordenadas ETS2 usando os parâmetros confirmados do climate.sii.
/// A projeção Mercator usa diretamente origin + factor * [map_z, map_x].
/// </summary>
public sealed class Ets2CoordinateConverter : IWorldCoordinateConverter
{
    private readonly MapCalibration _calibration;

    public Ets2CoordinateConverter(MapCalibration calibration)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
    }

    public bool IsValidated => _calibration.IsValidated;

    public bool TryConvert(
        double worldX,
        double worldZ,
        out double latitude,
        out double longitude)
    {
        latitude = 0d;
        longitude = 0d;

        if (!_calibration.IsUsable ||
            !IsFinite(worldX) ||
            !IsFinite(worldZ))
            return false;

        var adjustedZ = worldZ - _calibration.MapOffsetZ;
        var adjustedX = worldX - _calibration.MapOffsetX;

        latitude = _calibration.MapOriginLatitude +
                   adjustedZ * _calibration.MapFactorLatitude;

        longitude = _calibration.MapOriginLongitude +
                     adjustedX * _calibration.MapFactorLongitude;

        return latitude >= -90d && latitude <= 90d &&
               longitude >= -180d && longitude <= 180d;
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
