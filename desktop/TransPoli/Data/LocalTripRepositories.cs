using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransPoli;

internal sealed class LocalTripRepository
{
    private readonly TransPoliDb _db;
    public LocalTripRepository(TransPoliDb db) => _db = db;

    public void StartTrip(string tripId, TelemetrySnapshot data, string? serverId, double ratePerKm)
    {
        var now = DateTime.UtcNow;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
INSERT INTO trip(id,server_id,truck_id,cargo_id,source_city,destination_city,source_company,destination_company,cargo_name,status,
started_at_utc,start_odometer_km,end_odometer_km,planned_distance_km,fuel_start_l,fuel_end_l,fuel_consumed_l,
calculated_value,rate_per_km,distance_km,income_gross,expense_total,net_value,finish_reason,created_at_utc,updated_at_utc)
VALUES(@id,@server,@truck,@cargo,@source,@destination,@sourceCompany,@destinationCompany,@cargoName,'active',
@started,@odo,@odo,@planned,@fuel,@fuel,0,0,@rate,0,0,0,0,'',@created,@updated)
ON CONFLICT(id) DO UPDATE SET server_id=excluded.server_id, status='active', updated_at_utc=excluded.updated_at_utc;";
        Add(c,"@id",tripId);
        Add(c,"@server",serverId);
        Add(c,"@truck",string.IsNullOrWhiteSpace(data.TruckId) ? data.LicensePlate : data.TruckId);
        Add(c,"@cargo",data.Cargo);
        Add(c,"@source",data.SourceCity);
        Add(c,"@destination",data.DestinationCity);
        Add(c,"@sourceCompany",data.SourceCompany);
        Add(c,"@destinationCompany",data.DestinationCompany);
        Add(c,"@cargoName",data.Cargo);
        Add(c,"@started",now.ToString("O"));
        Add(c,"@odo",data.OdometerKm);
        Add(c,"@planned",data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : data.RouteDistanceKm);
        Add(c,"@fuel",data.FuelLiters);
        Add(c,"@rate",ratePerKm);
        Add(c,"@created",now.ToString("O"));
        Add(c,"@updated",now.ToString("O"));
        c.ExecuteNonQuery();
    }

    public void SetServerId(string tripId, string serverId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "UPDATE trip SET server_id=@server,updated_at_utc=@at WHERE id=@id;";
        Add(c,"@server",serverId); Add(c,"@at",DateTime.UtcNow.ToString("O")); Add(c,"@id",tripId); c.ExecuteNonQuery();
    }

    public void SetRatePerKm(string tripId, double ratePerKm)
    {
        if (!double.IsFinite(ratePerKm) || ratePerKm <= 0) return;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "UPDATE trip SET rate_per_km=@rate,updated_at_utc=@at WHERE id=@id AND status='active';";
        Add(c,"@rate",ratePerKm); Add(c,"@at",DateTime.UtcNow.ToString("O")); Add(c,"@id",tripId); c.ExecuteNonQuery();
    }

    public void AppendTelemetry(string tripId, TelemetrySnapshot data)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
INSERT INTO trip_telemetry(trip_id,recorded_at_utc,speed_kph,rpm,odometer_km,fuel_l,fuel_range_km)
VALUES(@trip,@at,@speed,@rpm,@odo,@fuel,@range);";
        Add(c,"@trip",tripId);
        Add(c,"@at",DateTime.UtcNow.ToString("O"));
        Add(c,"@speed",Math.Abs(data.SpeedKph));
        Add(c,"@rpm",data.Rpm);
        Add(c,"@odo",data.OdometerKm);
        Add(c,"@fuel",data.FuelLiters);
        Add(c,"@range",data.FuelRangeKm);
        c.ExecuteNonQuery();
    }

    public void FinishTrip(string tripId, TelemetrySnapshot data, double distanceKm, double fuelUsedL, double gross, string reason)
    {
        using var tx = _db.Connection.BeginTransaction();
        using var c = _db.Connection.CreateCommand();
        c.Transaction = tx;
        c.CommandText = @"
UPDATE trip SET status='finished',finished_at_utc=@finished,end_odometer_km=@odo,fuel_end_l=@fuel,
fuel_consumed_l=@used,distance_km=@distance,calculated_value=@gross,income_gross=@gross,
net_value=@net,finish_reason=@reason,updated_at_utc=@updated
WHERE id=@id AND status='active';";
        Add(c,"@finished",DateTime.UtcNow.ToString("O"));
        Add(c,"@odo",data.OdometerKm);
        Add(c,"@fuel",data.FuelLiters);
        Add(c,"@used",Math.Max(0,distanceKm > 0 ? fuelUsedL : 0));
        Add(c,"@distance",Math.Max(0,distanceKm));
        Add(c,"@gross",gross);
        Add(c,"@net",gross);
        Add(c,"@reason",reason);
        Add(c,"@updated",DateTime.UtcNow.ToString("O"));
        Add(c,"@id",tripId);
        if (c.ExecuteNonQuery() == 0)
        {
            tx.Commit();
            return;
        }

        var transactionId = "trip-income-" + tripId;
        if (gross > 0)
        {
        using var e = _db.Connection.CreateCommand();
        e.Transaction = tx;
        e.CommandText = @"
INSERT OR IGNORE INTO economy_transaction(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc)
VALUES(@id,@trip,'trip_income',@description,@amount,@at,@created);";
        Add(e,"@id",transactionId);
        Add(e,"@trip",tripId);
        Add(e,"@description",$"Pagamento da viagem • {distanceKm:0.0} km • tarifa local");
        Add(e,"@amount",gross);
        Add(e,"@at",DateTime.UtcNow.ToString("O"));
        Add(e,"@created",DateTime.UtcNow.ToString("O"));
        e.ExecuteNonQuery();
        }

        using var summary = _db.Connection.CreateCommand();
        summary.Transaction = tx;
        summary.CommandText = @"UPDATE trip SET
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0)
WHERE id=@trip;";
        Add(summary,"@trip",tripId);
        summary.ExecuteNonQuery();
        tx.Commit();
    }

    public double ResolveRatePerKm(string? cargoName)
    {
        SeedRates();
        var normalized = Normalize(cargoName);
        if (normalized.Length == 0) return 6.00;

        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT rate_per_km FROM cargo WHERE active=1 AND lower(name)=lower(@name) ORDER BY source='local' DESC LIMIT 1;";
        Add(c,"@name",cargoName?.Trim() ?? "");
        var exact = c.ExecuteScalar();
        if (exact is not null && exact != DBNull.Value) return Convert.ToDouble(exact, CultureInfo.InvariantCulture);

        c.CommandText = "SELECT rate_per_km FROM cargo WHERE active=1 AND lower(@name) LIKE '%' || lower(name) || '%' ORDER BY length(name) DESC LIMIT 1;";
        var partial = c.ExecuteScalar();
        if (partial is not null && partial != DBNull.Value) return Convert.ToDouble(partial, CultureInfo.InvariantCulture);
        return 6.00;
    }

    private void SeedRates()
    {
        var rates = new (string Name,double Rate)[] {
            ("Carvão",5.40),("Algodão",6.20),("Roupas",7.00),("Eletrônicos",8.50),
            ("Alimentos",6.80),("Máquinas",9.00),("Madeira",5.80),("Aço",7.40)
        };
        using var tx = _db.Connection.BeginTransaction();
        foreach (var item in rates)
        {
            using var c = _db.Connection.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR IGNORE INTO cargo(id,name,rate_per_km,source,active,created_at_utc,updated_at_utc) VALUES(@id,@name,@rate,'local',1,@at,@at);";
            Add(c,"@id","local-rate-" + Normalize(item.Name));
            Add(c,"@name",item.Name);
            Add(c,"@rate",item.Rate);
            Add(c,"@at",DateTime.UtcNow.ToString("O"));
            c.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant().Replace(" ","").Replace("-","").Replace("_","");

    private static void Add(SqliteCommand c,string name,object? value) => c.Parameters.AddWithValue(name,value ?? DBNull.Value);
}

internal sealed class LocalTelemetryRepository
{
    private readonly TransPoliDb _db;
    public LocalTelemetryRepository(TransPoliDb db) => _db = db;

    public void Append(string tripId, TelemetrySnapshot data) =>
        new LocalTripRepository(_db).AppendTelemetry(tripId, data);
}
