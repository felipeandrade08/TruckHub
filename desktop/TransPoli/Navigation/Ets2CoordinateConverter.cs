using System;

namespace TransPoli.Navigation;

/// <summary>
/// Conversor para mapas ETS2 cuja climate profile usa projeção mercator.
/// A fórmula segue a documentação de modding da SCS:
/// latitude  = origin[0] + factor[0] * map_z
/// longitude = origin[1] + factor[1] * map_x
/// </summary>
public sealed class Ets2CoordinateConverter : IWorldCoordinateConverter
{
    private readonly MapCalibration _calibration;

    public Ets2CoordinateConverter(MapCalibration calibration)
    {
        _calibration = calibration ?? throw new ArgumentNullException(nameof(calibration));
    }

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
        {
            return false;
        }

        var adjustedZ = worldZ - _calibration.MapOffsetZ;
        var adjustedX = worldX - _calibration.MapOffsetX;

        latitude = _calibration.MapOriginLatitude +
                   adjustedZ * _calibration.MapFactorLatitude;

        longitude = _calibration.MapOriginLongitude +
                    adjustedX * _calibration.MapFactorLongitude;

        return IsValidLatitude(latitude) && IsValidLongitude(longitude);
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsValidLatitude(double value)
        => IsFinite(value) && value >= -90d && value <= 90d;

    private static bool IsValidLongitude(double value)
        => IsFinite(value) && value >= -180d && value <= 180d;
}
