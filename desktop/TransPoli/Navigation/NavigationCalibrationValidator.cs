using System;

namespace TransPoli.Navigation;

public static class NavigationCalibrationValidator
{
    private const double EarthRadiusKm = 6371.0088;

    public static CalibrationValidationResult Validate(
        MapCalibration calibration,
        double worldX,
        double worldZ,
        double expectedLatitude,
        double expectedLongitude,
        double toleranceKm = 2d)
    {
        if (calibration is null)
            throw new ArgumentNullException(nameof(calibration));

        if (!IsFinite(expectedLatitude) ||
            !IsFinite(expectedLongitude) ||
            expectedLatitude < -90d ||
            expectedLatitude > 90d ||
            expectedLongitude < -180d ||
            expectedLongitude > 180d)
        {
            return new CalibrationValidationResult
            {
                Message = "A posição geográfica esperada é inválida."
            };
        }

        if (!IsFinite(toleranceKm) || toleranceKm < 0d)
        {
            return new CalibrationValidationResult
            {
                ExpectedLatitude = expectedLatitude,
                ExpectedLongitude = expectedLongitude,
                Message = "A tolerância de validação é inválida."
            };
        }

        if (!calibration.IsUsable)
        {
            return new CalibrationValidationResult
            {
                ExpectedLatitude = expectedLatitude,
                ExpectedLongitude = expectedLongitude,
                Message = "A calibração não possui parâmetros utilizáveis."
            };
        }

        if (!string.Equals(calibration.Projection, "mercator", StringComparison.OrdinalIgnoreCase))
        {
            return new CalibrationValidationResult
            {
                ExpectedLatitude = expectedLatitude,
                ExpectedLongitude = expectedLongitude,
                ProjectionSupported = false,
                Message = "A projeção atual ainda não possui conversão geográfica implementada com segurança."
            };
        }

        var converter = new Ets2CoordinateConverter(calibration);

        if (!converter.TryConvert(
                worldX,
                worldZ,
                out var calculatedLatitude,
                out var calculatedLongitude))
        {
            return new CalibrationValidationResult
            {
                ExpectedLatitude = expectedLatitude,
                ExpectedLongitude = expectedLongitude,
                ProjectionSupported = IsConverterSupported(calibration.Projection),
                Message = "A conversão WorldX/WorldZ para latitude/longitude não pôde ser realizada."
            };
        }

        var errorDistanceKm = HaversineDistanceKm(
            calculatedLatitude,
            calculatedLongitude,
            expectedLatitude,
            expectedLongitude);

        return new CalibrationValidationResult
        {
            IsValid = errorDistanceKm <= toleranceKm,
            ProjectionSupported = true,
            PositionConversionSucceeded = true,
            ExpectedLatitude = expectedLatitude,
            ExpectedLongitude = expectedLongitude,
            CalculatedLatitude = calculatedLatitude,
            CalculatedLongitude = calculatedLongitude,
            ErrorDistanceKm = errorDistanceKm,
            Message = errorDistanceKm <= toleranceKm
                ? "Calibração aprovada para o ponto de controle."
                : $"Erro de calibração acima da tolerância: {errorDistanceKm:F3} km."
        };
    }

    public static double HaversineDistanceKm(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);
        var deltaLat = lat2 - lat1;
        var deltaLon = DegreesToRadians(longitude2 - longitude1);

        var a = Math.Pow(Math.Sin(deltaLat / 2d), 2d) +
                Math.Cos(lat1) *
                Math.Cos(lat2) *
                Math.Pow(Math.Sin(deltaLon / 2d), 2d);

        var clampedA = Math.Max(0d, Math.Min(1d, a));
        var c = 2d * Math.Atan2(
            Math.Sqrt(clampedA),
            Math.Sqrt(1d - clampedA));

        return EarthRadiusKm * c;
    }

    private static bool IsConverterSupported(string projection)
        => string.Equals(projection, "mercator", StringComparison.OrdinalIgnoreCase);

    private static double DegreesToRadians(double degrees)
        => degrees * Math.PI / 180d;

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
