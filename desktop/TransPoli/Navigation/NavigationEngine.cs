using System;

namespace TransPoli.Navigation;

/// <summary>
/// Camada de navegação independente da interface.
/// Normaliza a telemetria do Connector e, quando recebe um conversor
/// explicitamente calibrado, também produz latitude/longitude.
/// </summary>
public static class NavigationEngine
{
    public static NavigationState FromTelemetry(
        TelemetrySnapshot data,
        IWorldCoordinateConverter? coordinateConverter = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        var positionValid = data.PositionValid &&
                            IsFinite(data.WorldX) &&
                            IsFinite(data.WorldY) &&
                            IsFinite(data.WorldZ);

        double? latitude = null;
        double? longitude = null;

        if (positionValid &&
            coordinateConverter is not null &&
            coordinateConverter is Ets2CoordinateConverter ets2Converter &&
            ets2Converter.IsValidated &&
            coordinateConverter.TryConvert(
                data.WorldX,
                data.WorldZ,
                out var convertedLatitude,
                out var convertedLongitude))
        {
            latitude = convertedLatitude;
            longitude = convertedLongitude;
        }

        return new NavigationState
        {
            IsValid = data.Connected && positionValid,
            PositionValid = positionValid,
            WorldX = SafeFinite(data.WorldX),
            WorldY = SafeFinite(data.WorldY),
            WorldZ = SafeFinite(data.WorldZ),
            HeadingDeg = NormalizeDegrees(data.HeadingDeg),
            PitchDeg = NormalizeSignedDegrees(data.PitchDeg),
            RollDeg = NormalizeSignedDegrees(data.RollDeg),
            Latitude = latitude,
            Longitude = longitude,
            RemainingDistanceKm = PositiveOrNull(data.RouteDistanceKm),
            RemainingTimeMinutes = PositiveOrNull(data.RouteTimeSeconds / 60d),
            SpeedLimitKph = PositiveOrNull(data.SpeedLimitKph),
            Origin = NullIfWhiteSpace(data.SourceCity),
            Destination = NullIfWhiteSpace(data.DestinationCity),
            PositionSource = positionValid ? "ETS2_WORLD" : "UNAVAILABLE",
            MapSource = latitude.HasValue && longitude.HasValue ? "ETS2_CALIBRATED" : "NONE"
        };
    }

    private static double? PositiveOrNull(double value)
        => IsFinite(value) && value > 0 ? value : null;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double SafeFinite(double value)
        => IsFinite(value) ? value : 0d;

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double NormalizeDegrees(double value)
    {
        if (!IsFinite(value)) return 0d;
        var normalized = value % 360d;
        if (normalized < 0d) normalized += 360d;
        return normalized;
    }

    private static double NormalizeSignedDegrees(double value)
    {
        if (!IsFinite(value)) return 0d;
        var normalized = value % 360d;
        if (normalized > 180d) normalized -= 360d;
        if (normalized < -180d) normalized += 360d;
        return normalized;
    }
}
