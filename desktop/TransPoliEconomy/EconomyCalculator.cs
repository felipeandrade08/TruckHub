using System;

namespace TransPoli.Economy
{
    /// <summary>Pure financial calculations shared by TransPoli. It never writes to game saves.</summary>
    public static class EconomyCalculator
    {
        public static decimal CalculateFuelCost(decimal quantity, decimal unitPrice)
        {
            if (quantity < 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (unitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(unitPrice));
            return Money(quantity * unitPrice);
        }

        public static decimal CalculateUsGallonsToLiters(decimal gallons)
        {
            if (gallons < 0m) throw new ArgumentOutOfRangeException(nameof(gallons));
            return decimal.Round(gallons * 3.785411784m, 4, MidpointRounding.AwayFromZero);
        }

        public static decimal CalculateLitersToUsGallons(decimal liters)
        {
            if (liters < 0m) throw new ArgumentOutOfRangeException(nameof(liters));
            return decimal.Round(liters / 3.785411784m, 4, MidpointRounding.AwayFromZero);
        }

        public static decimal CalculateFuelExpense(EconomyProfile profile, decimal litersPurchased)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.Game == SimulatorGame.AmericanTruckSimulator)
            {
                if (!profile.FuelPricePerUsGallon.HasValue || profile.FuelPricePerUsGallon.Value <= 0m)
                    throw new InvalidOperationException("O preço regional do diesel em USD/galão não foi configurado.");
                return CalculateFuelCost(CalculateLitersToUsGallons(litersPurchased), profile.FuelPricePerUsGallon.Value);
            }
            if (profile.Game == SimulatorGame.EuroTruckSimulator2)
            {
                if (!profile.FuelPricePerLiter.HasValue || profile.FuelPricePerLiter.Value <= 0m)
                    throw new InvalidOperationException("O preço regional do diesel em EUR/litro não foi configurado.");
                return CalculateFuelCost(litersPurchased, profile.FuelPricePerLiter.Value);
            }
            throw new InvalidOperationException("Não é possível calcular combustível sem identificar ATS ou ETS2.");
        }

        public static EconomyTripResult CalculateTrip(EconomyTripInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.DistanceKm < 0d) throw new ArgumentOutOfRangeException(nameof(input.DistanceKm));
            if (input.FuelUsedLiters < 0d) throw new ArgumentOutOfRangeException(nameof(input.FuelUsedLiters));
            var expenses = input.FuelExpense + input.TollExpense + input.FerryExpense +
                           input.TrainExpense + input.FineExpense + input.MaintenanceExpense + input.OtherExpense;
            var distance = Convert.ToDecimal(input.DistanceKm);
            return new EconomyTripResult
            {
                Game = input.Game,
                CurrencyCode = input.CurrencyCode ?? string.Empty,
                Revenue = Money(input.DeliveryRevenue),
                TotalExpenses = Money(expenses),
                NetProfit = Money(input.DeliveryRevenue - expenses),
                ProfitMarginPercent = input.DeliveryRevenue > 0m ? decimal.Round((input.DeliveryRevenue - expenses) / input.DeliveryRevenue * 100m, 2) : 0m,
                CostPerKm = distance > 0m ? Money(expenses / distance) : 0m,
                RevenuePerKm = distance > 0m ? Money(input.DeliveryRevenue / distance) : 0m,
                FuelCostPerKm = distance > 0m ? Money(input.FuelExpense / distance) : 0m,
                DistanceKm = input.DistanceKm,
                FuelUsedLiters = input.FuelUsedLiters,
                FuelConsumptionLitersPer100Km = input.DistanceKm > 0d ? Math.Round(input.FuelUsedLiters / input.DistanceKm * 100d, 2) : 0d,
                CargoDamagePercent = Math.Max(0d, Math.Min(100d, input.CargoDamagePercent))
            };
        }

        public static decimal CalculateWearMaintenance(EconomyProfile profile,
            double previousEngineWear, double currentEngineWear,
            double previousTransmissionWear, double currentTransmissionWear,
            double previousChassisWear, double currentChassisWear,
            double previousWheelWear, double currentWheelWear)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var cost =
                Increase(currentEngineWear, previousEngineWear) * profile.MaintenanceCostPerEngineWearPoint +
                Increase(currentTransmissionWear, previousTransmissionWear) * profile.MaintenanceCostPerTransmissionWearPoint +
                Increase(currentChassisWear, previousChassisWear) * profile.MaintenanceCostPerChassisWearPoint +
                Increase(currentWheelWear, previousWheelWear) * profile.MaintenanceCostPerWheelWearPoint;
            return Money(cost);
        }

        private static decimal Increase(double current, double previous)
        {
            if (double.IsNaN(current) || double.IsInfinity(current) || double.IsNaN(previous) || double.IsInfinity(previous)) return 0m;
            var delta = Math.Max(0d, Math.Min(1d, current) - Math.Min(1d, Math.Max(0d, previous)));
            return Convert.ToDecimal(delta * 100d);
        }

        private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
