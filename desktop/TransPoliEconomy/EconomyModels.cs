using System;

namespace TransPoli.Economy
{
    public enum SimulatorGame { Unknown = 0, AmericanTruckSimulator = 1, EuroTruckSimulator2 = 2 }
    public enum EconomyExpenseType { Fuel, Toll, Ferry, Train, Fine, Maintenance, Repair, Insurance, Other }

    /// <summary>Regional prices are supplied by the active game profile; missing prices are never invented.</summary>
    public sealed class EconomyProfile
    {
        public SimulatorGame Game { get; set; }
        public string CurrencyCode { get; set; } = "USD";
        public string CurrencySymbol { get; set; } = "$";
        public decimal? FuelPricePerLiter { get; set; }
        public decimal? FuelPricePerUsGallon { get; set; }
        public decimal? AverageRevenuePerKm { get; set; }
        public decimal? AverageRevenuePerMile { get; set; }
        public decimal MaintenanceCostPerEngineWearPoint { get; set; }
        public decimal MaintenanceCostPerTransmissionWearPoint { get; set; }
        public decimal MaintenanceCostPerChassisWearPoint { get; set; }
        public decimal MaintenanceCostPerWheelWearPoint { get; set; }
        public DateTimeOffset PricesUpdatedAtUtc { get; set; }
        public string PriceSource { get; set; } = string.Empty;
        public bool HasFuelPrice =>
            (Game == SimulatorGame.AmericanTruckSimulator && FuelPricePerUsGallon.HasValue && FuelPricePerUsGallon.Value > 0m) ||
            (Game == SimulatorGame.EuroTruckSimulator2 && FuelPricePerLiter.HasValue && FuelPricePerLiter.Value > 0m);
    }

    public sealed class EconomyTripInput
    {
        public SimulatorGame Game { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal DeliveryRevenue { get; set; }
        public decimal FuelExpense { get; set; }
        public decimal TollExpense { get; set; }
        public decimal FerryExpense { get; set; }
        public decimal TrainExpense { get; set; }
        public decimal FineExpense { get; set; }
        public decimal MaintenanceExpense { get; set; }
        public decimal OtherExpense { get; set; }
        public double DistanceKm { get; set; }
        public double FuelUsedLiters { get; set; }
        public double CargoDamagePercent { get; set; }
    }

    public sealed class EconomyTripResult
    {
        public SimulatorGame Game { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetProfit { get; set; }
        public decimal ProfitMarginPercent { get; set; }
        public decimal CostPerKm { get; set; }
        public decimal RevenuePerKm { get; set; }
        public decimal FuelCostPerKm { get; set; }
        public double DistanceKm { get; set; }
        public double FuelUsedLiters { get; set; }
        public double FuelConsumptionLitersPer100Km { get; set; }
        public double CargoDamagePercent { get; set; }
    }

    public sealed class EconomyExpense
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public SimulatorGame Game { get; set; }
        public EconomyExpenseType Type { get; set; }
        public decimal Amount { get; set; }
        public string CurrencyCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string SourceEventId { get; set; } = string.Empty;
    }
}
