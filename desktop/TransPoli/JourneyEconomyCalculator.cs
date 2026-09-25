using System;

namespace TransPoli;

/// <summary>
/// Camada 8 — Economia.
/// A remuneração da viagem é calculada exclusivamente pelo modelo econômico
/// local do TransPoli, usando tarifa por quilômetro e distância da telemetria.
/// O valor de pagamento do contrato do ETS2 nunca entra nesta conta.
/// </summary>
internal static class JourneyEconomyCalculator
{
    public const double DefaultRatePerKm = 17.00;
    public const double MinimumRatePerKm = 12.00;
    public const double MaximumRatePerKm = 22.00;

    public static double SanitizeRate(double ratePerKm) =>
        double.IsFinite(ratePerKm) && ratePerKm >= MinimumRatePerKm && ratePerKm <= MaximumRatePerKm
            ? Math.Round(ratePerKm, 2, MidpointRounding.AwayFromZero)
            : DefaultRatePerKm;

    public static double CalculateGross(double distanceKm, double ratePerKm)
    {
        var distance = Math.Max(0d, double.IsFinite(distanceKm) ? distanceKm : 0d);
        var rate = SanitizeRate(ratePerKm);
        return Math.Round(distance * rate, 2, MidpointRounding.AwayFromZero);
    }
}
