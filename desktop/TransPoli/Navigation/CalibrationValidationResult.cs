namespace TransPoli.Navigation;

public sealed record CalibrationValidationResult
{
    public bool IsValid { get; init; }
    public bool ProjectionSupported { get; init; }
    public bool PositionConversionSucceeded { get; init; }

    public double ExpectedLatitude { get; init; }
    public double ExpectedLongitude { get; init; }

    public double? CalculatedLatitude { get; init; }
    public double? CalculatedLongitude { get; init; }

    public double? ErrorDistanceKm { get; init; }

    public string Message { get; init; } = string.Empty;
}
