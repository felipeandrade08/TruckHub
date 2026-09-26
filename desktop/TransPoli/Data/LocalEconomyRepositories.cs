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

    public void RecordTripIncome(string tripId, decimal gross, DateTime occurredAtUtc)
    {
        var ownerUserId = CurrentOwnerUserId();
        if (string.IsNullOrWhiteSpace(tripId) || gross <= 0 || string.IsNullOrWhiteSpace(ownerUserId)) return;

        using var tx = _db.Connection.BeginTransaction();
        using var c = _db.Connection.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"INSERT OR IGNORE INTO economy_transaction
(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc,owner_user_id)
VALUES(@id,@trip,'trip_income',@description,@amount,@at,@created,@owner);";
        Add(c,"@id",$"trip-income-{tripId}");
        Add(c,"@trip",tripId);
        Add(c,"@description",BuildTripIncomeDescription(tripId));
        Add(c,"@amount",gross);
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
SELECT COALESCE(SUM(CASE WHEN e.owner_user_id=@owner THEN e.amount ELSE 0 END),0)
       + COALESCE((SELECT SUM(t.income_gross)
                   FROM trip t
                   WHERE t.owner_user_id=@owner AND t.status='finished' AND t.income_gross > 0
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
ORDER BY occurred_at_utc DESC LIMIT @limit;";
        Add(c,"@owner",CurrentOwnerUserId() ?? ""); Add(c,"@owner",CurrentOwnerUserId() ?? ""); Add(c,"@limit",Math.Clamp(limit,1,500));
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
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
SELECT
    COALESCE((SELECT SUM(CASE WHEN amount > 0 THEN amount ELSE 0 END) FROM economy_transaction),0)
      + COALESCE((SELECT SUM(t.income_gross) FROM trip t
                  WHERE t.status='finished' AND t.income_gross > 0
                    AND NOT EXISTS (SELECT 1 FROM economy_transaction e
                                    WHERE e.type='trip_income' AND e.trip_id=t.id)),0),
    COALESCE(-SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END),0),
    COALESCE(SUM(amount),0)
      + COALESCE((SELECT SUM(t.income_gross) FROM trip t
                  WHERE t.status='finished' AND t.income_gross > 0
                    AND NOT EXISTS (SELECT 1 FROM economy_transaction e
                                    WHERE e.type='trip_income' AND e.trip_id=t.id)),0),
    COALESCE((SELECT COUNT(*) FROM trip WHERE status='finished' AND income_gross > 0),0),
    COALESCE(-SUM(CASE WHEN type='fuel_expense' THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN type='maintenance_expense' THEN amount ELSE 0 END),0),
    COALESCE(-SUM(CASE WHEN type NOT IN ('fuel_expense','maintenance_expense') AND amount < 0 THEN amount ELSE 0 END),0)
FROM economy_transaction;";
        using var r = c.ExecuteReader();
        if (!r.Read()) return new LocalEconomySummary(0m,0m,0m,0,0m,0m,0m);
        return new LocalEconomySummary(
            r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2),
            r.GetInt32(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6));
    }

    public LocalLoan? GetActiveLoan()
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,principal,remaining,repayment_pct,installments_total,installments_paid,installment_min,interest_monthly_pct,total_payable,status,created_at_utc,paid_at_utc
FROM local_loan WHERE owner_user_id=@owner AND status='active' ORDER BY created_at_utc DESC LIMIT 1;";
        Add(c,"@owner",CurrentOwnerUserId() ?? "");
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;
        return ReadLoan(r);
    }

    public LocalLoan CreateLoan(decimal principal, int installments, decimal repaymentPct = 20m)
    {
        if (principal != 5000m && principal != 10000m)
            throw new InvalidOperationException("Empréstimo local disponível em R$ 5.000 ou R$ 10.000.");
        var rate = installments <= 6 ? 1.5m : installments <= 12 ? 2m : installments <= 18 ? 2.5m : 3m;
        var total = Math.Round(principal * (decimal)Math.Pow((double)(1m + rate / 100m), installments), 2);
        var installment = Math.Round(total / installments, 2);
        if (GetActiveLoan() is not null) throw new InvalidOperationException("Você já possui um empréstimo ativo.");

        var id = "loan-" + Guid.NewGuid().ToString("N");
        using var tx = _db.Connection.BeginTransaction();
        using var c = _db.Connection.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"INSERT INTO local_loan
(id,principal,remaining,repayment_pct,installments_total,installments_paid,installment_min,interest_monthly_pct,total_payable,status,created_at_utc,owner_user_id)
VALUES(@id,@principal,@remaining,@pct,@total,@paid,@installment,@rate,@payable,'active',@at,@owner);";
        Add(c,"@id",id); Add(c,"@principal",principal); Add(c,"@remaining",total); Add(c,"@pct",repaymentPct);
        Add(c,"@total",installments); Add(c,"@paid",0); Add(c,"@installment",installment); Add(c,"@rate",rate);
        Add(c,"@payable",total); Add(c,"@at",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)); Add(c,"@owner",CurrentOwnerUserId() ?? "");
        c.ExecuteNonQuery();

        using var e = _db.Connection.CreateCommand();
        e.Transaction = tx;
        e.CommandText = @"INSERT INTO economy_transaction(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc)
VALUES(@id,NULL,'loan_credit',@description,@amount,@at,@created);";
        Add(e,"@id","loan-credit-"+id); Add(e,"@description",$"Empréstimo local • {installments} parcelas");
        Add(e,"@amount",principal); Add(e,"@at",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture)); Add(e,"@created",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
        e.ExecuteNonQuery();
        tx.Commit();
        return GetActiveLoan()!;
    }

    public decimal ApplyAutomaticLoanPayment(string tripId, decimal tripNetBeforeLoan)
    {
        if (string.IsNullOrWhiteSpace(tripId) || tripNetBeforeLoan <= 0) return 0m;

        var loan = GetActiveLoan();
        if (loan is null) return 0m;

        // A regra do empréstimo é percentual sobre o lucro líquido da viagem.
        // O padrão é 20% e o valor nunca pode ultrapassar o saldo restante.
        // O ID determinístico impede cobrança duplicada caso o fechamento seja reprocessado.
        var repaymentPct = loan.RepaymentPct > 0m ? loan.RepaymentPct : 20m;
        var paymentByTrip = Math.Round(tripNetBeforeLoan * repaymentPct / 100m, 2);
        var minimumInstallment = Math.Min(loan.InstallmentMin, tripNetBeforeLoan);
        var targetPayment = Math.Max(paymentByTrip, minimumInstallment);
        var payment = Math.Min(loan.Remaining, targetPayment);
        if (payment <= 0) return 0m;

        var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var transactionId = $"loan-installment-{loan.Id}-{tripId}";

        using var tx = _db.Connection.BeginTransaction();
        using var check = _db.Connection.CreateCommand();
        check.Transaction = tx;
        check.CommandText = "SELECT COUNT(1) FROM economy_transaction WHERE id=@id;";
        Add(check,"@id",transactionId);
        if (Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
        {
            tx.Commit();
            return 0m;
        }

        using var e = _db.Connection.CreateCommand();
        e.Transaction = tx;
        e.CommandText = @"INSERT INTO economy_transaction
(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc)
VALUES(@id,@trip,'loan_installment',@description,@amount,@at,@created);";
        Add(e,"@id",transactionId);
        Add(e,"@trip",tripId);
        Add(e,"@description",$"Parcela do empréstimo • {loan.InstallmentsPaid + 1}/{loan.InstallmentsTotal}");
        Add(e,"@amount",-payment);
        Add(e,"@at",now);
        Add(e,"@created",now);
        e.ExecuteNonQuery();

        var newRemaining = Math.Max(0m, loan.Remaining - payment);
        var newPaid = loan.InstallmentsPaid + 1;
        var paidOff = newRemaining <= 0.01m || newPaid >= loan.InstallmentsTotal;
        using var u = _db.Connection.CreateCommand();
        u.Transaction = tx;
        u.CommandText = @"UPDATE local_loan SET
remaining=@remaining,
installments_paid=@paid,
status=@status,
paid_at_utc=@paidAt
WHERE id=@id AND status='active';";
        Add(u,"@remaining",paidOff ? 0m : newRemaining);
        Add(u,"@paid",newPaid);
        Add(u,"@status",paidOff ? "paid" : "active");
        Add(u,"@paidAt",paidOff ? now : null);
        Add(u,"@id",loan.Id);
        u.ExecuteNonQuery();

        using var t = _db.Connection.CreateCommand();
        t.Transaction = tx;
        t.CommandText = @"UPDATE trip SET
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0),
updated_at_utc=@at
WHERE id=@trip;";
        Add(t,"@trip",tripId);
        Add(t,"@at",now);
        t.ExecuteNonQuery();

        tx.Commit();
        return payment;
    }

    public void SettleLoan()
    {
        var loan = GetActiveLoan();
        if (loan is null) throw new InvalidOperationException("Nenhum empréstimo ativo.");
        var balance = GetBalance();
        if (balance < loan.Remaining) throw new InvalidOperationException($"Saldo insuficiente. Faltam {Money(loan.Remaining - balance)}.");

        var now = DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
        using var tx = _db.Connection.BeginTransaction();
        using var c = _db.Connection.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "UPDATE local_loan SET remaining=0,status='paid',paid_at_utc=@at WHERE id=@id;";
        Add(c,"@at",now); Add(c,"@id",loan.Id); c.ExecuteNonQuery();
        using var e = _db.Connection.CreateCommand();
        e.Transaction = tx;
        e.CommandText = @"INSERT INTO economy_transaction(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc)
VALUES(@id,NULL,'loan_settlement',@description,@amount,@at,@created);";
        Add(e,"@id","loan-settlement-"+loan.Id); Add(e,"@description","Quitação antecipada do empréstimo");
        Add(e,"@amount",-loan.Remaining); Add(e,"@at",now); Add(e,"@created",now); e.ExecuteNonQuery();
        tx.Commit();
    }

    private static LocalLoan ReadLoan(Microsoft.Data.Sqlite.SqliteDataReader r) =>
        new(
            r.GetString(0),r.GetDecimal(1),r.GetDecimal(2),r.GetDecimal(3),r.GetInt32(4),
            r.GetInt32(5),r.GetDecimal(6),r.GetDecimal(7),r.GetDecimal(8),r.GetString(9),
            DateTime.Parse(r.GetString(10),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),
            r.IsDBNull(11) ? null : DateTime.Parse(r.GetString(11),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind));

    private static string Money(decimal value) => value.ToString("C2",CultureInfo.GetCultureInfo("pt-BR"));

    public IReadOnlyList<LocalEconomyEntry> GetRecentExpenses(int limit = 100)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,trip_id,type,description,amount,occurred_at_utc
FROM economy_transaction WHERE owner_user_id=@owner AND amount < 0 ORDER BY occurred_at_utc DESC LIMIT @limit;";
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
SET expense_total=COALESCE((SELECT -SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
    net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0),
    updated_at_utc=@at
WHERE id=@trip;";
        Add(c,"@trip",tripId);
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

internal sealed record LocalLoan(
    string Id, decimal Principal, decimal Remaining, decimal RepaymentPct, int InstallmentsTotal,
    int InstallmentsPaid, decimal InstallmentMin, decimal InterestMonthlyPct, decimal TotalPayable,
    string Status, DateTime CreatedAtUtc, DateTime? PaidAtUtc);

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
