using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransPoli;

internal sealed class LocalEconomyRepository
{
    private readonly TransPoliDb _db;
    public LocalEconomyRepository(TransPoliDb db) => _db = db;

    private static string? CurrentOwnerUserId() => SecureTokenStore.ReadUserId();

    public void AddExpense(string id, string? tripId, string type, string description, decimal amount, DateTime occurredAtUtc)
    {
        var ownerUserId = CurrentOwnerUserId();
        if (amount <= 0 || string.IsNullOrWhiteSpace(ownerUserId)) return;
        using var tx = _db.Connection.BeginTransaction();
        using var c = _db.Connection.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"INSERT OR IGNORE INTO economy_transaction
(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc,owner_user_id)
VALUES(@id,@trip,@type,@description,@amount,@at,@created,@owner);";
        Add(c,"@id",id);
        Add(c,"@trip",tripId);
        Add(c,"@type",type);
        Add(c,"@description",description);
        Add(c,"@amount",-Math.Abs(amount));
        Add(c,"@at",occurredAtUtc.ToString("O",CultureInfo.InvariantCulture));
        Add(c,"@created",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        Add(c,"@owner",ownerUserId);
        c.ExecuteNonQuery();
        tx.Commit();
        RecalculateTrip(tripId);
    }

    public decimal GetBalance()
    {
        var ownerUserId = CurrentOwnerUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return 0m;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
SELECT COALESCE(SUM(CASE WHEN e.owner_user_id=@owner AND (e.type<>'trip_income' OR EXISTS (
        SELECT 1 FROM trip t JOIN trip_closure tc ON tc.trip_id=t.id AND tc.owner_user_id=t.owner_user_id
        WHERE t.id=e.trip_id AND t.owner_user_id=@owner AND t.status='finished' AND tc.remote_queued_at_utc IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
      )) THEN e.amount ELSE 0 END),0)
       + COALESCE((SELECT SUM(t.income_gross)
                   FROM trip t
                   WHERE t.owner_user_id=@owner AND t.status='finished' AND t.income_gross > 0
          AND EXISTS (SELECT 1 FROM trip_closure tc WHERE tc.trip_id=t.id AND tc.owner_user_id=@owner AND tc.remote_queued_at_utc IS NOT NULL)
          AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
                     AND NOT EXISTS (
                         SELECT 1 FROM economy_transaction e2
                         WHERE e2.owner_user_id=@owner AND e2.type='trip_income' AND e2.trip_id=t.id
                     )),0)
FROM economy_transaction e;";
        Add(c,"@owner",ownerUserId);
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public decimal GetTripExpenses(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT COALESCE(-SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),0) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId); Add(c,"@owner",CurrentOwnerUserId() ?? "");
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public decimal GetTripNet(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT COALESCE(SUM(amount),0) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId); Add(c,"@owner",CurrentOwnerUserId() ?? "");
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<LocalEconomyEntry> GetRecent(int limit = 100)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,trip_id,type,description,amount,occurred_at_utc
FROM economy_transaction
WHERE owner_user_id=@owner AND NOT (type='trip_income' AND amount=0)
  AND (type<>'trip_income' OR EXISTS (
      SELECT 1 FROM trip t JOIN trip_closure tc ON tc.trip_id=t.id AND tc.owner_user_id=t.owner_user_id
      WHERE t.id=economy_transaction.trip_id AND t.owner_user_id=@owner
        AND t.status='finished' AND tc.remote_queued_at_utc IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
  ))
ORDER BY occurred_at_utc DESC LIMIT @limit;";
        Add(c,"@owner",CurrentOwnerUserId() ?? ""); Add(c,"@limit",Math.Clamp(limit,1,500));
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

        // Reconciliação de legado: viagens antigas que já possuem receita no trip,
        // mas ainda não tinham um lançamento trip_income na economia.
        using var legacy = _db.Connection.CreateCommand();
        legacy.CommandText = @"
SELECT t.id, t.cargo_name, t.source_city, t.destination_city, t.income_gross, t.finished_at_utc
FROM trip t
WHERE t.owner_user_id=@owner AND t.status='finished' AND t.income_gross > 0
  AND EXISTS (SELECT 1 FROM trip_closure tc WHERE tc.trip_id=t.id AND tc.owner_user_id=@owner AND tc.remote_queued_at_utc IS NOT NULL)
  AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
  AND NOT EXISTS (
      SELECT 1 FROM economy_transaction e
      WHERE e.owner_user_id=@owner AND e.type='trip_income' AND e.trip_id=t.id
  )
ORDER BY t.finished_at_utc DESC
LIMIT @limit;";
        Add(legacy,"@owner",CurrentOwnerUserId() ?? ""); Add(legacy,"@limit",Math.Clamp(limit,1,500));
        using var lr = legacy.ExecuteReader();
        while(lr.Read())
        {
            var tripId = lr.GetString(0);
            var cargo = lr.IsDBNull(1) ? "Carga" : lr.GetString(1);
            var origin = lr.IsDBNull(2) ? "" : lr.GetString(2);
            var destination = lr.IsDBNull(3) ? "" : lr.GetString(3);
            var gross = lr.GetDecimal(4);
            var finished = lr.IsDBNull(5) ? DateTime.UtcNow : DateTime.Parse(lr.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            var route = string.IsNullOrWhiteSpace(origin) && string.IsNullOrWhiteSpace(destination)
                ? ""
                : $" • {origin} → {destination}";
            list.Add(new LocalEconomyEntry(
                $"legacy-trip-income-{tripId}",
                tripId,
                "trip_income",
                $"Você recebeu um Pix • viagem de {cargo}{route}",
                gross,
                finished));
        }

        return list.OrderByDescending(x => x.OccurredAtUtc).Take(Math.Clamp(limit,1,500)).ToList();
    }


    public LocalEconomySummary GetSummary()
    {
        var ownerUserId = CurrentOwnerUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return new LocalEconomySummary(0m,0m,0m,0,0m,0m,0m);
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
SELECT
    COALESCE((SELECT SUM(CASE WHEN et.amount > 0 THEN et.amount ELSE 0 END) FROM economy_transaction et WHERE et.owner_user_id=@owner
        AND (et.type<>'trip_income' OR EXISTS (
          SELECT 1 FROM trip t JOIN trip_closure tc ON tc.trip_id=t.id AND tc.owner_user_id=t.owner_user_id
          WHERE t.id=et.trip_id AND t.owner_user_id=@owner AND t.status='finished' AND tc.remote_queued_at_utc IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
        ))),0)
      + COALESCE((SELECT SUM(t.income_gross) FROM trip t
                  WHERE t.owner_user_id=@owner AND t.status='finished' AND t.income_gross > 0
                    AND EXISTS (SELECT 1 FROM trip_closure tc WHERE tc.trip_id=t.id AND tc.owner_user_id=@owner AND tc.remote_queued_at_utc IS NOT NULL)
                    AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
                    AND NOT EXISTS (SELECT 1 FROM economy_transaction e
                                    WHERE e.owner_user_id=@owner AND e.type='trip_income' AND e.trip_id=t.id)),0),
    COALESCE(-SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),0),
    COALESCE((SELECT SUM(et.amount) FROM economy_transaction et WHERE et.owner_user_id=@owner
        AND (et.type<>'trip_income' OR EXISTS (
          SELECT 1 FROM trip t JOIN trip_closure tc ON tc.trip_id=t.id AND tc.owner_user_id=t.owner_user_id
          WHERE t.id=et.trip_id AND t.owner_user_id=@owner AND t.status='finished' AND tc.remote_queued_at_utc IS NOT NULL
            AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
        ))),0)
      + COALESCE((SELECT SUM(t.income_gross) FROM trip t
                  WHERE t.owner_user_id=@owner AND t.status='finished' AND t.income_gross > 0
                    AND EXISTS (SELECT 1 FROM trip_closure tc WHERE tc.trip_id=t.id AND tc.owner_user_id=@owner AND tc.remote_queued_at_utc IS NOT NULL)
                    AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)
                    AND NOT EXISTS (SELECT 1 FROM economy_transaction e
                                    WHERE e.owner_user_id=@owner AND e.type='trip_income' AND e.trip_id=t.id)),0),
    COALESCE((SELECT COUNT(*) FROM trip t WHERE t.owner_user_id=@owner AND t.status='finished' AND t.income_gross > 0
      AND EXISTS (SELECT 1 FROM trip_closure tc WHERE tc.trip_id=t.id AND tc.owner_user_id=@owner AND tc.remote_queued_at_utc IS NOT NULL)
      AND NOT EXISTS (SELECT 1 FROM sync_queue q WHERE q.trip_id=t.id AND q.owner_user_id=@owner AND q.event_type='trip.finish' AND q.synced_at_utc IS NULL)),0),
    COALESCE(-SUM(CASE WHEN type='fuel_expense' THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN type='maintenance_expense' THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN type NOT IN ('fuel_expense','maintenance_expense') AND amount < 0 THEN amount ELSE 0 END),0)
FROM economy_transaction WHERE owner_user_id=@owner;";
        Add(c,"@owner",ownerUserId);
        using var r = c.ExecuteReader();
        if (!r.Read()) return new LocalEconomySummary(0m,0m,0m,0,0m,0m,0m);
        return new LocalEconomySummary(
            r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2),
            r.GetInt32(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6));
    }

    // Empréstimos são oficiais e corporativos no modelo atual.
    // Crédito, parcelas e quitação são processados pelo servidor (company_loans)
    // e nunca devem gerar uma segunda economia local concorrente.

    public IReadOnlyList<LocalEconomyEntry> GetRecentExpenses(int limit = 100)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,trip_id,type,description,amount,occurred_at_utc
FROM economy_transaction WHERE owner_user_id=@owner AND amount < 0 ORDER BY occurred_at_utc DESC LIMIT @limit;";
        Add(c,"@owner",CurrentOwnerUserId() ?? ""); Add(c,"@limit",Math.Clamp(limit,1,500));
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
FROM trip WHERE id=@id AND owner_user_id=@owner;";
        Add(c,"@id",tripId); Add(c,"@owner",CurrentOwnerUserId() ?? "");
        using var r = c.ExecuteReader();
        if (!r.Read()) return new LocalTripFinancialSummary(0d,0m,0m,0m,0d);
        return new LocalTripFinancialSummary(r.GetDouble(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetDouble(4));
    }

    public bool HasPendingSync()
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT EXISTS(SELECT 1 FROM sync_queue WHERE synced_at_utc IS NULL AND owner_user_id=@owner LIMIT 1);"; Add(c,"@owner",CurrentOwnerUserId() ?? "");
        return Convert.ToInt32(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    public int GetPendingSyncCount()
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM sync_queue WHERE synced_at_utc IS NULL AND owner_user_id=@owner;"; Add(c,"@owner",CurrentOwnerUserId() ?? "");
        return Convert.ToInt32(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private void RecalculateTrip(string? tripId)
    {
        if (string.IsNullOrWhiteSpace(tripId)) return;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"UPDATE trip
SET expense_total=COALESCE((SELECT -SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner),0),
    net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner),0),
    updated_at_utc=@at
WHERE id=@trip;";
        Add(c,"@trip",tripId); Add(c,"@owner",CurrentOwnerUserId() ?? "");
        Add(c,"@at",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        c.ExecuteNonQuery();
    }

    private string BuildTripIncomeDescription(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
SELECT COALESCE(cargo_name,'Carga'),
       COALESCE(source_city,''),
       COALESCE(destination_city,'')
FROM trip WHERE id=@id LIMIT 1;";
        Add(c,"@id",tripId);
        using var r = c.ExecuteReader();
        if (!r.Read()) return "Você recebeu um Pix • pagamento da viagem";
        var cargo = r.GetString(0);
        var origin = r.GetString(1);
        var destination = r.GetString(2);
        var route = string.IsNullOrWhiteSpace(origin) && string.IsNullOrWhiteSpace(destination)
            ? ""
            : $" • {origin} → {destination}";
        return $"Você recebeu um Pix • viagem de {cargo}{route}";
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
