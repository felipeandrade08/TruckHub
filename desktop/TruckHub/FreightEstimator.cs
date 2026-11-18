using System;

namespace TruckHub;

/// <summary>
/// Espelha a fórmula de fechamento do servidor (api/src/economy.ts, calculateTripEconomy) com os
/// parâmetros padrão: combustível R$ 5,98/L a 0,45 L/km, manutenção R$ 0,42/km, margem mínima de 20%
/// sobre o custo, receita mínima de R$ 150 por viagem e bônus de entrega sem dano (5%).
/// É só uma ESTIMATIVA para comparar cargas; o valor pago é o do fechamento oficial.
/// </summary>
internal static class FreightEstimator
{
    public const decimal FuelPricePerLiter = 5.98m, ConsumptionLPerKm = 0.45m, MaintenancePerKm = 0.42m;
    public const decimal MinMarginPct = 20m, MinTripRevenue = 150m, CleanBonusPct = 5m, DamagePenaltyPct = 15m;

    public sealed record Result(decimal Gross, decimal OperatingCost, decimal Net, decimal MarginPct, bool FloorApplied);

    public static Result Estimate(double distanceKm, double ratePerKm)
    {
        var km = (decimal)Math.Max(0d, double.IsFinite(distanceKm) ? distanceKm : 0d);
        var rate = (decimal)Math.Max(0d, double.IsFinite(ratePerKm) ? ratePerKm : 0d);
        var baseRevenue = km * rate;
        var cost = km * ConsumptionLPerKm * FuelPricePerLiter + km * MaintenancePerKm;
        var floor = km > 0 ? Math.Max(cost * (1 + MinMarginPct / 100m), MinTripRevenue) : 0m;
        var protectedRevenue = Math.Max(baseRevenue, floor);
        var gross = Math.Round(protectedRevenue * (1 + CleanBonusPct / 100m), 2);
        cost = Math.Round(cost, 2);
        var net = gross - cost;
        return new Result(gross, cost, net, gross > 0 ? Math.Round(net / gross * 100m, 1) : 0m, baseRevenue < floor);
    }

    /// <summary>Perda estimada por dano de carga acima da tolerância de 1% (15% do frete × fração de dano).</summary>
    public static decimal DamageLoss(decimal gross, double damageFraction) =>
        damageFraction <= 0.01 ? 0m : Math.Round(gross * DamagePenaltyPct / 100m * (decimal)Math.Min(1d, damageFraction), 2);

    /// <summary>Nível de motorista exigido para recomendar a carga (distância e urgência).</summary>
    public static int RequiredLevel(double distanceKm, int urgency)
    {
        var level = 1;
        if (distanceKm > 500) level = Math.Max(level, 3);
        if (distanceKm > 800) level = Math.Max(level, 5);
        if (distanceKm > 1200) level = Math.Max(level, 10);
        if (urgency >= 1) level = Math.Max(level, 3);
        if (urgency >= 2) level = Math.Max(level, 6);
        return level;
    }
}
