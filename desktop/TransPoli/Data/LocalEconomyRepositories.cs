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
        if (!r.Read()) return new LocalEconomySummary(0m,0m,0m,0,0m,0m,0m);
        return new LocalEconomySummary(
            r.GetDecimal(0), r.GetDecimal(1), r.GetDecimal(2),
            r.GetInt32(3), r.GetDecimal(4), r.GetDecimal(5), r.GetDecimal(6));
    }

    public LocalLoan? GetActiveLoan()
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT id,principal,remaining,repayment_pct,installments_total,installments_paid,installment_min,interest_monthly_pct,total_payable,status,created_at_utc,paid_at_utc
FROM local_loan WHERE status='active' ORDER BY created_at_utc DESC LIMIT 1;";
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
(id,principal,remaining,repayment_pct,installments_total,installments_paid,installment_min,interest_monthly_pct,total_payable,status,created_at_utc)
VALUES(@id,@principal,@remaining,@pct,@total,@paid,@installment,@rate,@payable,'active',@at);";
        Add(c,"@id",id); Add(c,"@principal",principal); Add(c,"@remaining",total); Add(c,"@pct",repaymentPct);
        Add(c,"@total",installments); Add(c,"@paid",0); Add(c,"@installment",installment); Add(c,"@rate",rate);
        Add(c,"@payable",total); Add(c,"@at",DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
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

        // Uma parcela é debitada no fechamento de cada viagem com lucro líquido positivo.
        // O ID determinístico impede cobrança duplicada caso o fechamento seja reprocessado.
        var payment = Math.Min(loan.Remaining, loan.InstallmentMin);
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
        if (!r.Read()) return new LocalTripFinancialSummary(0d,0m,0m,0m,0d);
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
