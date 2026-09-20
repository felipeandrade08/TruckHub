using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransPoli;

internal sealed class LocalEconomyRepository
{
    private readonly TransPoliDb _db;
    public LocalEconomyRepository(TransPoliDb db) => _db = db;

    public void AddExpense(string id, string? tripId, string type, string description, decimal amount, DateTime occurredAtUtc)
    {
        if (amount <= 0) return;
        using var tx = _db.Connection.BeginTransaction();
        using var c = _db.Connection.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"INSERT OR IGNORE INTO economy_transaction
(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc)
VALUES(@id,@trip,@type,@description,@amount,@at,@created);";
        Add(c,"@id",id);
        Add(c,"@trip",tripId);
        Add(c,"@type",type);
        Add(c,"@description",description);
        Add(c,"@amount",-Math.Abs(amount));
        Add(c,"@at",occurredAtUtc.ToString("O",CultureInfo.InvariantCulture));
        Add(c,"@created",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        c.ExecuteNonQuery();
        tx.Commit();
        RecalculateTrip(tripId);
    }

    public decimal GetBalance()
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT COALESCE(SUM(amount),0) FROM economy_transaction;";
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public decimal GetTripExpenses(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT COALESCE(-SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),0) FROM economy_transaction WHERE trip_id=@trip;";
        Add(c,"@trip",tripId);
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public decimal GetTripNet(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT COALESCE(SUM(amount),0) FROM economy_transaction WHERE trip_id=@trip;";
        Add(c,"@trip",tripId);
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<LocalEconomyEntry> GetRecent(int limit = 100)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,trip_id,type,description,amount,occurred_at_utc
FROM economy_transaction ORDER BY occurred_at_utc DESC LIMIT @limit;";
        Add(c,"@limit",Math.Clamp(limit,1,500));
        using var r = c.ExecuteReader();
        var list = new List<LocalEconomyEntry>();
        while(r.Read())
        {
            list.Add(new LocalEconomyEntry(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetDecimal(4),
                DateTime.Parse(r.GetString(5),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)));
        }
        return list;
    }


    public LocalEconomySummary GetSummary()
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
SELECT
    COALESCE(SUM(CASE WHEN amount > 0 THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),0),
    COALESCE(SUM(amount),0),
    COUNT(CASE WHEN type='trip_income' THEN 1 END),
    COALESCE(-SUM(CASE WHEN type='fuel_expense' THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN type='maintenance_expense' THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN type NOT IN ('fuel_expense','maintenance_expense') AND amount < 0 THEN amount ELSE 0 END),0)
FROM economy_transaction;";
        using var r = c.ExecuteReader();
        if (!r.Read()) return new LocalEconomySummary();
        return new LocalEconomySummary(
            r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2),
            r.GetInt32(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6));
    }

    public IReadOnlyList<LocalEconomyEntry> GetRecentExpenses(int limit = 100)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,trip_id,type,description,amount,occurred_at_utc
FROM economy_transaction WHERE amount < 0 ORDER BY occurred_at_utc DESC LIMIT @limit;";
        Add(c,"@limit",Math.Clamp(limit,1,500));
        using var r = c.ExecuteReader();
        var list = new List<LocalEconomyEntry>();
        while(r.Read())
        {
            list.Add(new LocalEconomyEntry(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetDecimal(4),
                DateTime.Parse(r.GetString(5),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)));
        }
        return list;
    }

    public LocalTripFinancialSummary GetTripFinancialSummary(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT COALESCE(distance_km,0), COALESCE(income_gross,0),
COALESCE(expense_total,0), COALESCE(net_value,0), COALESCE(rate_per_km,0)
FROM trip WHERE id=@id;";
        Add(c,"@id",tripId);
        using var r = c.ExecuteReader();
        if (!r.Read()) return new LocalTripFinancialSummary();
        return new LocalTripFinancialSummary(r.GetDouble(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDouble(4));
    }

    private void RecalculateTrip(string? tripId)
    {
        if (string.IsNullOrWhiteSpace(tripId)) return;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"UPDATE trip
SET expense_total=COALESCE((SELECT -SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
    net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0),
    updated_at_utc=@at
WHERE id=@trip;";
        Add(c,"@trip",tripId);
        Add(c,"@at",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        c.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand c,string name,object? value) => c.Parameters.AddWithValue(name,value ?? DBNull.Value);
}

internal sealed record LocalEconomySummary(
    decimal Credits,
    decimal Debits,
    decimal Balance,
    int TripCount,
    decimal FuelExpenses,
    decimal MaintenanceExpenses,
    decimal OtherExpenses);

internal sealed record LocalTripFinancialSummary(
    double DistanceKm,
    decimal Gross,
    decimal Expenses,
    decimal Net,
    double RatePerKm);

internal sealed record LocalEconomyEntry(
    string Id,
    string? TripId,
    string Type,
    string Description,
    decimal Amount,
    DateTime OccurredAtUtc);
