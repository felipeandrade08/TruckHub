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
        // O banco guarda a distância total do contrato. Quando o ETS2 só fornece a distância restante,
        // deixamos 0 e o painel calcula o total pela distância percorrida + restante.
        Add(c,"@planned",data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : 0);
        Add(c,"@fuel",data.FuelLiters);
        Add(c,"@rate",ratePerKm);
        Add(c,"@created",now.ToString("O"));
        Add(c,"@updated",now.ToString("O"));
        c.ExecuteNonQuery();
    }

    public string? FindActiveTripIdByServerId(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId)) return null;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT id FROM trip WHERE server_id=@server AND status='active' ORDER BY started_at_utc DESC LIMIT 1;";
        Add(c, "@server", serverId);
        return c.ExecuteScalar()?.ToString();
    }

    public int FinishOrphanedActiveTrips(TelemetrySnapshot data)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
UPDATE trip
SET status='finished',
    finished_at_utc=COALESCE(finished_at_utc,@finished),
    end_odometer_km=CASE WHEN end_odometer_km > 0 THEN end_odometer_km ELSE @odo END,
    fuel_end_l=CASE WHEN fuel_end_l > 0 THEN fuel_end_l ELSE @fuel END,
    distance_km=CASE WHEN distance_km > 0 THEN distance_km ELSE MAX(0,@odo-start_odometer_km) END,
    finish_reason=CASE WHEN finish_reason IS NULL OR finish_reason='' THEN 'sessao_encerrada_sem_job' ELSE finish_reason END,
    updated_at_utc=@updated
WHERE status='active';";
        Add(c,"@finished",DateTime.UtcNow.ToString("O"));
        Add(c,"@odo",data.OdometerKm);
        Add(c,"@fuel",data.FuelLiters);
        Add(c,"@updated",DateTime.UtcNow.ToString("O"));
        return c.ExecuteNonQuery();
    }

    public int FinishMismatchedActiveTrips(TelemetrySnapshot data)
    {
        var changed = 0;
        using var read = _db.Connection.CreateCommand();
        read.CommandText = "SELECT id,cargo_name,source_city,destination_city FROM trip WHERE status='active'";
        using var reader = read.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            var id = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var cargo = reader.IsDBNull(1) ? "" : reader.GetString(1);
            var origin = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var destination = reader.IsDBNull(3) ? "" : reader.GetString(3);
            var cargoMatches = string.IsNullOrWhiteSpace(data.Cargo) || string.Equals(cargo.Trim(), data.Cargo.Trim(), StringComparison.OrdinalIgnoreCase);
            var originMatches = string.IsNullOrWhiteSpace(data.SourceCity) || string.Equals(origin.Trim(), data.SourceCity.Trim(), StringComparison.OrdinalIgnoreCase);
            var destinationMatches = string.IsNullOrWhiteSpace(data.DestinationCity) || string.Equals(destination.Trim(), data.DestinationCity.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(id) && (!cargoMatches || !originMatches || !destinationMatches)) ids.Add(id);
        }
        reader.Close();
        foreach (var id in ids)
        {
            using var update = _db.Connection.CreateCommand();
            update.CommandText = @"UPDATE trip SET status='finished', finished_at_utc=COALESCE(finished_at_utc,@finished), end_odometer_km=CASE WHEN end_odometer_km > 0 THEN end_odometer_km ELSE @odo END, fuel_end_l=CASE WHEN fuel_end_l > 0 THEN fuel_end_l ELSE @fuel END, distance_km=CASE WHEN distance_km > 0 THEN distance_km ELSE MAX(0,@odo-start_odometer_km) END, finish_reason=CASE WHEN finish_reason IS NULL OR finish_reason='' THEN 'nova_viagem_detectada' ELSE finish_reason END, updated_at_utc=@updated WHERE id=@id AND status='active';";
            Add(update,"@id",id); Add(update,"@finished",DateTime.UtcNow.ToString("O")); Add(update,"@odo",data.OdometerKm); Add(update,"@fuel",data.FuelLiters); Add(update,"@updated",DateTime.UtcNow.ToString("O"));
            changed += update.ExecuteNonQuery();
        }
        return changed;
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
        Add(c,"@wx",data.WorldX);
        Add(c,"@wy",data.WorldY);
        Add(c,"@wz",data.WorldZ);
        Add(c,"@heading",data.HeadingDeg);
        Add(c,"@pitch",data.PitchDeg);
        Add(c,"@roll",data.RollDeg);
        Add(c,"@positionValid",data.PositionValid ? 1 : 0);
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

    public TruckHistorySummary GetTruckHistory(string truckId)
    {
        if (string.IsNullOrWhiteSpace(truckId)) return new();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT
COUNT(*),COALESCE(SUM(distance_km),0),COALESCE(SUM(fuel_consumed_l),0),
COALESCE(SUM(income_gross),0),COALESCE(SUM(expense_total),0),COALESCE(SUM(net_value),0)
FROM trip WHERE truck_id=@truck AND status='finished';";
        Add(c,"@truck",truckId);
        using var r=c.ExecuteReader();
        var result=r.Read()?new TruckHistorySummary(r.GetInt32(0),r.GetDouble(1),r.GetDouble(2),r.GetDouble(3),r.GetDouble(4),r.GetDouble(5)):new();
        r.Close();
        using var m=_db.Connection.CreateCommand();
        m.CommandText="SELECT COUNT(*),COALESCE(SUM(cost),0) FROM maintenance WHERE truck_id=@truck;";
        Add(m,"@truck",truckId);
        using var mr=m.ExecuteReader();
        if(mr.Read()) result=result with { MaintenanceCount=mr.GetInt32(0), MaintenanceCost=mr.GetDouble(1) };
        return result;
    }

    public List<TruckTripHistoryItem> GetTruckTrips(string truckId,int limit=25)
    {
        var list=new List<TruckTripHistoryItem>();
        if(string.IsNullOrWhiteSpace(truckId)) return list;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT id,cargo_name,source_city,destination_city,started_at_utc,finished_at_utc,
distance_km,fuel_consumed_l,income_gross,expense_total,net_value,finish_reason
FROM trip WHERE truck_id=@truck ORDER BY COALESCE(finished_at_utc,started_at_utc) DESC LIMIT @limit;";
        Add(c,"@truck",truckId); Add(c,"@limit",Math.Clamp(limit,1,100));
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new TruckTripHistoryItem(
            r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),
            r.IsDBNull(4)?null:DateTime.Parse(r.GetString(4)),r.IsDBNull(5)?null:DateTime.Parse(r.GetString(5)),
            r.GetDouble(6),r.GetDouble(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.GetString(11)));
        return list;
    }

    public TripFinancialSummary GetFinancialSummary(string tripId)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"SELECT
COALESCE(SUM(CASE WHEN amount>0 THEN amount ELSE 0 END),0),
COALESCE(-SUM(CASE WHEN amount<0 THEN amount ELSE 0 END),0),
COALESCE(SUM(amount),0),
COALESCE(-SUM(CASE WHEN amount<0 AND type='fuel_expense' THEN amount ELSE 0 END),0),
COALESCE(-SUM(CASE WHEN amount<0 AND type='maintenance_expense' THEN amount ELSE 0 END),0)
FROM economy_transaction WHERE trip_id=@trip;";
        Add(c,"@trip",tripId);
        using var r=c.ExecuteReader();
        return r.Read() ? new TripFinancialSummary(r.GetDouble(0),r.GetDouble(1),r.GetDouble(2),r.GetDouble(3),r.GetDouble(4)) : new();
    }

    public void RefreshFinancialSummary(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"UPDATE trip SET
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount<0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0),
updated_at_utc=@at WHERE id=@trip;";
        Add(c,"@trip",tripId); Add(c,"@at",DateTime.UtcNow.ToString("O")); c.ExecuteNonQuery();
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

internal sealed record TruckHistorySummary(int Trips=0,double DistanceKm=0,double FuelLiters=0,double Income=0,double Expenses=0,double Net=0,int MaintenanceCount=0,double MaintenanceCost=0);
internal sealed record TruckTripHistoryItem(string Id,string Cargo,string Origin,string Destination,DateTime? StartedAt,DateTime? FinishedAt,double DistanceKm,double FuelLiters,double Income,double Expenses,double Net,string FinishReason);
internal sealed record TripFinancialSummary(double Income=0,double Expenses=0,double Net=0,double FuelExpenses=0,double MaintenanceExpenses=0);

internal sealed class LocalTelemetryRepository
{
    private readonly TransPoliDb _db;
    public LocalTelemetryRepository(TransPoliDb db) => _db = db;

    public void Append(string tripId, TelemetrySnapshot data) =>
        new LocalTripRepository(_db).AppendTelemetry(tripId, data);
}
