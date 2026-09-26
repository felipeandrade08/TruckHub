using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransPoli;

internal sealed class LocalTripRepository
{
    private readonly TransPoliDb _db;
    public LocalTripRepository(TransPoliDb db) => _db = db;

    public void StartTrip(string tripId, TelemetrySnapshot data, string? serverId, double ratePerKm, string ownerUserId)
    {
        var now = DateTime.UtcNow;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
INSERT INTO trip(id,server_id,truck_id,cargo_id,source_city,destination_city,source_company,destination_company,cargo_name,status,
started_at_utc,start_odometer_km,end_odometer_km,planned_distance_km,fuel_start_l,fuel_end_l,fuel_consumed_l,
calculated_value,rate_per_km,distance_km,income_gross,expense_total,net_value,finish_reason,created_at_utc,updated_at_utc,owner_user_id)
VALUES(@id,@server,@truck,@cargo,@source,@destination,@sourceCompany,@destinationCompany,@cargoName,'active',
@started,@odo,@odo,@planned,@fuel,@fuel,0,0,@rate,0,0,0,0,'',@created,@updated,@owner)
ON CONFLICT(id) DO UPDATE SET server_id=excluded.server_id, status='active', updated_at_utc=excluded.updated_at_utc
WHERE trip.owner_user_id=excluded.owner_user_id;";
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
        Add(c,"@owner",ownerUserId);
        c.ExecuteNonQuery();
    }

    public string? FindActiveTripIdByServerId(string serverId, string ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(serverId)) return null;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT id FROM trip WHERE server_id=@server AND owner_user_id=@owner AND status='active' ORDER BY started_at_utc DESC LIMIT 1;";
        Add(c, "@server", serverId); Add(c, "@owner", ownerUserId);
        return c.ExecuteScalar()?.ToString();
    }

    // Encerramentos operacionais não podem ser fabricados por rotinas de limpeza.
    // Uma viagem ativa só muda para finished pelo pipeline de trip_closure, que
    // congela o snapshot e garante trip.finish durável até a liquidação remota.
    public void DiscardActiveTrip(string tripId)
    {
        if (string.IsNullOrWhiteSpace(tripId)) return;
        using var tx = _db.Connection.BeginTransaction();
        foreach (var sql in new[]
        {
            "DELETE FROM trip_closure WHERE trip_id=@id AND owner_user_id=@owner;",
            "DELETE FROM trip_telemetry WHERE trip_id=@id;",
            "DELETE FROM trip_event WHERE trip_id=@id;",
            "DELETE FROM trip WHERE id=@id AND status='active' AND owner_user_id=@owner;"
        })
        {
            using var c = _db.Connection.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            Add(c, "@id", tripId);
            Add(c, "@owner", SecureTokenStore.ReadUserId());
            c.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public int FinishOrphanedActiveTrips(TelemetrySnapshot data) => 0;

    public int FinishMismatchedActiveTrips(TelemetrySnapshot data) => 0;

    public bool SetServerId(string tripId, string serverId, string ownerUserId)
    {
        if(string.IsNullOrWhiteSpace(tripId)||string.IsNullOrWhiteSpace(serverId)||string.IsNullOrWhiteSpace(ownerUserId)) return false;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "UPDATE trip SET server_id=@server,updated_at_utc=@at WHERE id=@id AND owner_user_id=@owner;";
        Add(c,"@server",serverId); Add(c,"@at",DateTime.UtcNow.ToString("O")); Add(c,"@id",tripId); Add(c,"@owner",ownerUserId);
        if(c.ExecuteNonQuery()<=0) return false;
        using var verify=_db.Connection.CreateCommand();
        verify.CommandText="SELECT COUNT(1) FROM trip WHERE id=@id AND owner_user_id=@owner AND server_id=@server;";
        Add(verify,"@id",tripId);Add(verify,"@owner",ownerUserId);Add(verify,"@server",serverId);
        return Convert.ToInt32(verify.ExecuteScalar()??0)>0;
    }

    public void SetRatePerKm(string tripId, double ratePerKm)
    {
        if (!double.IsFinite(ratePerKm) || ratePerKm <= 0) return;
        using var c = _db.Connection.CreateCommand();
        c.CommandText = "UPDATE trip SET rate_per_km=@rate,updated_at_utc=@at WHERE id=@id AND status='active' AND owner_user_id=@owner;";
        Add(c,"@rate",ratePerKm); Add(c,"@at",DateTime.UtcNow.ToString("O")); Add(c,"@id",tripId); Add(c,"@owner",SecureTokenStore.ReadUserId() ?? ""); c.ExecuteNonQuery();
    }

    public void UpdateLiveProgress(string tripId, TelemetrySnapshot data, double distanceKm, double fuelUsedL)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"UPDATE trip SET
end_odometer_km=@odo,
fuel_end_l=@fuel,
fuel_consumed_l=@used,
distance_km=@distance,
calculated_value=MAX(0,@distance * rate_per_km),
income_gross=MAX(0,@distance * rate_per_km),
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount<0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@id),0),
net_value=MAX(0,@distance * rate_per_km)-COALESCE((SELECT -SUM(CASE WHEN amount<0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@id),0),
updated_at_utc=@at
WHERE id=@id AND status='active';";
        Add(c,"@odo",data.OdometerKm);
        Add(c,"@fuel",data.FuelLiters);
        Add(c,"@used",Math.Max(0,fuelUsedL));
        Add(c,"@distance",Math.Max(0,distanceKm));
        Add(c,"@at",DateTime.UtcNow.ToString("O"));
        Add(c,"@id",tripId);
        c.ExecuteNonQuery();
    }

    public void AppendTelemetry(string tripId, TelemetrySnapshot data)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
INSERT INTO trip_telemetry(trip_id,recorded_at_utc,speed_kph,rpm,odometer_km,fuel_l,fuel_range_km,
world_x,world_y,world_z,heading_deg,pitch_deg,roll_deg,position_valid)
VALUES(@trip,@at,@speed,@rpm,@odo,@fuel,@range,@wx,@wy,@wz,@heading,@pitch,@roll,@positionValid);";
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
        Add(c,"@owner",SecureTokenStore.ReadUserId());
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
INSERT OR IGNORE INTO economy_transaction(id,trip_id,type,description,amount,occurred_at_utc,created_at_utc,owner_user_id)
VALUES(@id,@trip,'trip_income',@description,@amount,@at,@created,@owner);";
        Add(e,"@id",transactionId);
        Add(e,"@trip",tripId);
        Add(e,"@description",$"Pagamento da viagem • {distanceKm:0.0} km • tarifa local");
        Add(e,"@amount",gross);
        Add(e,"@at",DateTime.UtcNow.ToString("O"));
        Add(e,"@created",DateTime.UtcNow.ToString("O"));
        Add(e,"@owner",SecureTokenStore.ReadUserId());
        e.ExecuteNonQuery();
        }

        using var summary = _db.Connection.CreateCommand();
        summary.Transaction = tx;
        summary.CommandText = @"UPDATE trip SET
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip),0),
net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip),0)
WHERE id=@trip AND owner_user_id=@owner;";
        Add(summary,"@trip",tripId);Add(summary,"@owner",SecureTokenStore.ReadUserId());
        summary.ExecuteNonQuery();
        tx.Commit();
    }

    public TruckOperationalProfile GetTruckOperationalProfile(string truckId,string? plate)
    {
        var result=new TruckOperationalProfile();
        if(string.IsNullOrWhiteSpace(truckId) && string.IsNullOrWhiteSpace(plate)) return result;
        using(var e=_db.Connection.CreateCommand())
        {
            e.CommandText=@"SELECT COUNT(*) FROM operational_event
WHERE owner_user_id=@owner AND lower(truck)=lower(@truck) AND event_type IN ('FREIADA_BRUSCA','ACELERACAO_BRUSCA','VELOCIDADE_ELEVADA','MANUTENCAO_CRITICA');";
            Add(e,"@truck",truckId);Add(e,"@owner",SecureTokenStore.ReadUserId() ?? "");
            result=result with { Occurrences=Convert.ToInt32(e.ExecuteScalar()??0) };
        }
        using(var f=_db.Connection.CreateCommand())
        {
            f.CommandText=@"SELECT COUNT(*),COALESCE(SUM(liters),0),COALESCE(SUM(total_cost),0)
FROM refueling WHERE owner_user_id=@owner AND ((@plate<>'' AND lower(license_plate)=lower(@plate)) OR trip_id IN (SELECT id FROM trip WHERE truck_id=@truck AND owner_user_id=@owner));";
            Add(f,"@plate",plate??""); Add(f,"@truck",truckId);Add(f,"@owner",SecureTokenStore.ReadUserId() ?? "");
            using var r=f.ExecuteReader();
            if(r.Read()) result=result with { Refuelings=r.GetInt32(0),RefueledLiters=r.GetDouble(1),FuelCost=r.GetDouble(2) };
        }
        using(var h=_db.Connection.CreateCommand())
        {
            h.CommandText=@"SELECT wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels,recorded_at_utc
FROM truck_health_snapshot WHERE truck_id=@truck AND owner_user_id=@owner ORDER BY recorded_at_utc DESC LIMIT 1;";
            Add(h,"@truck",truckId);Add(h,"@owner",SecureTokenStore.ReadUserId() ?? "");
            using var r=h.ExecuteReader();
            if(r.Read()) result=result with { WearEngine=r.GetDouble(0),WearTransmission=r.GetDouble(1),WearCabin=r.GetDouble(2),WearChassis=r.GetDouble(3),WearWheels=r.GetDouble(4),LastHealthAt=DateTime.Parse(r.GetString(5)) };
        }
        return result;
    }

    public void AppendTruckHealth(string truckId,string? tripId,TelemetrySnapshot data)
    {
        if(string.IsNullOrWhiteSpace(truckId)) return;
        // Recovery pode repetir esta etapa se o processo cair entre o INSERT e o
        // checkpoint. Para uma viagem identificada, a saúde final é única por TripId.
        if(!string.IsNullOrWhiteSpace(tripId))
        {
            using var exists=_db.Connection.CreateCommand();
            exists.CommandText="SELECT COUNT(1) FROM truck_health_snapshot WHERE trip_id=@trip;";
            Add(exists,"@trip",tripId);
            if(Convert.ToInt32(exists.ExecuteScalar()??0)>0) return;
        }
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO truck_health_snapshot(truck_id,trip_id,recorded_at_utc,odometer_km,wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels)
VALUES(@truck,@trip,@at,@odo,@engine,@transmission,@cabin,@chassis,@wheels);";
        Add(c,"@truck",truckId);Add(c,"@trip",tripId);Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@odo",data.OdometerKm);
        Add(c,"@engine",data.WearEngine);Add(c,"@transmission",data.WearTransmission);Add(c,"@cabin",data.WearCabin);Add(c,"@chassis",data.WearChassis);Add(c,"@wheels",data.WearWheels);
        c.ExecuteNonQuery();
    }

    public TruckHistorySummary GetTruckHistory(string truckId)
    {
        if (string.IsNullOrWhiteSpace(truckId)) return new();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT
COUNT(*),COALESCE(SUM(distance_km),0),COALESCE(SUM(fuel_consumed_l),0),
COALESCE(SUM(income_gross),0),COALESCE(SUM(expense_total),0),COALESCE(SUM(net_value),0)
FROM trip WHERE truck_id=@truck AND status='finished' AND owner_user_id=@owner;";
        Add(c,"@truck",truckId);Add(c,"@owner",SecureTokenStore.ReadUserId() ?? "");
        using var r=c.ExecuteReader();
        var result=r.Read()?new TruckHistorySummary(r.GetInt32(0),r.GetDouble(1),r.GetDouble(2),r.GetDouble(3),r.GetDouble(4),r.GetDouble(5)):new();
        r.Close();
        using var m=_db.Connection.CreateCommand();
        m.CommandText="SELECT COUNT(*),COALESCE(SUM(cost),0) FROM maintenance WHERE truck_id=@truck AND owner_user_id=@owner;";
        Add(m,"@truck",truckId);Add(m,"@owner",SecureTokenStore.ReadUserId() ?? "");
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
FROM trip WHERE truck_id=@truck AND owner_user_id=@owner ORDER BY COALESCE(finished_at_utc,started_at_utc) DESC LIMIT @limit;";
        Add(c,"@truck",truckId);Add(c,"@owner",SecureTokenStore.ReadUserId() ?? ""); Add(c,"@limit",Math.Clamp(limit,1,100));
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new TruckTripHistoryItem(
            r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),
            r.IsDBNull(4)?null:DateTime.Parse(r.GetString(4)),r.IsDBNull(5)?null:DateTime.Parse(r.GetString(5)),
            r.GetDouble(6),r.GetDouble(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.IsDBNull(11)?"":r.GetString(11)));
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
FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        using var r=c.ExecuteReader();
        return r.Read() ? new TripFinancialSummary(r.GetDouble(0),r.GetDouble(1),r.GetDouble(2),r.GetDouble(3),r.GetDouble(4)) : new();
    }

    public void RefreshFinancialSummary(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"UPDATE trip SET
expense_total=COALESCE((SELECT -SUM(CASE WHEN amount<0 THEN amount ELSE 0 END) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner),0),
net_value=COALESCE((SELECT SUM(amount) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner),0),
updated_at_utc=@at WHERE id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId); Add(c,"@owner",SecureTokenStore.ReadUserId()); Add(c,"@at",DateTime.UtcNow.ToString("O")); c.ExecuteNonQuery();
    }

    public double ResolveRatePerKm(string? cargoName)
    {
        SeedRates();
        var normalized = Normalize(cargoName);
        if (normalized.Length == 0) return 12.00;

        using var c = _db.Connection.CreateCommand();
        c.CommandText = "SELECT rate_per_km FROM cargo WHERE active=1 AND lower(name)=lower(@name) ORDER BY source='local' DESC LIMIT 1;";
        Add(c,"@name",cargoName?.Trim() ?? "");
        var exact = c.ExecuteScalar();
        if (exact is not null && exact != DBNull.Value) return Convert.ToDouble(exact, CultureInfo.InvariantCulture);

        c.CommandText = "SELECT rate_per_km FROM cargo WHERE active=1 AND lower(@name) LIKE '%' || lower(name) || '%' ORDER BY length(name) DESC LIMIT 1;";
        var partial = c.ExecuteScalar();
        if (partial is not null && partial != DBNull.Value) return Convert.ToDouble(partial, CultureInfo.InvariantCulture);
        return normalized switch
        {
            var n when n.Contains("perigos") || n.Contains("quimic") => 22.00,
            var n when n.Contains("especial") => 21.00,
            var n when n.Contains("pesad") || n.Contains("maquina") || n.Contains("industrial") => 20.00,
            var n when n.Contains("refriger") || n.Contains("congel") => 19.00,
            var n when n.Contains("eletron") || n.Contains("comput") => 18.00,
            var n when n.Contains("veiculo") || n.Contains("carro") => 17.00,
            var n when n.Contains("carvao") || n.Contains("miner") => 16.00,
            var n when n.Contains("madeira") || n.Contains("tora") => 15.00,
            var n when n.Contains("milho") || n.Contains("soja") || n.Contains("agric") => 14.00,
            _ => 12.00
        };
    }

    private void SeedRates()
    {
        var rates = new (string Name,double Rate)[] {
            ("Milho",14.00),("Soja",14.50),("Carvão",16.00),("Veículos",17.00),
            ("Carga pesada",20.00),("Carga especial",21.00),("Carga refrigerada",19.00),
            ("Carga perigosa",22.00),("Construção",16.00),("Agrícola",14.00),
            ("Madeira",15.00),("Minerais",16.00),("Eletrônicos",18.00),
            ("Industrial",20.00),("Logística",12.00),("Ar Condicionado",20.00)
        };
        using var tx = _db.Connection.BeginTransaction();
        foreach (var item in rates)
        {
            using var c = _db.Connection.CreateCommand();
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO cargo(id,name,rate_per_km,source,active,created_at_utc,updated_at_utc)
VALUES(@id,@name,@rate,'local',1,@at,@at)
ON CONFLICT(id) DO UPDATE SET name=excluded.name,rate_per_km=excluded.rate_per_km,active=1,updated_at_utc=excluded.updated_at_utc;";
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


internal sealed class LocalTripClosureRepository
{
    private readonly TransPoliDb _db;
    public LocalTripClosureRepository(TransPoliDb db)=>_db=db;

    public List<PendingTripClosure> GetPending(string ownerUserId)
    {
        var list=new List<PendingTripClosure>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT tc.trip_id,COALESCE(tc.server_id,t.server_id),COALESCE(NULLIF(tc.truck_id,''),t.truck_id,''),
tc.session_key,tc.final_odometer_km,tc.final_fuel_l,tc.distance_km,tc.fuel_consumed_l,tc.gross_value,
tc.cargo_damage,tc.cargo_mass_kg,tc.wear_engine,tc.wear_transmission,tc.wear_cabin,tc.wear_chassis,tc.wear_wheels,tc.reason,
tc.local_settled_at_utc IS NOT NULL,tc.tachograph_closed_at_utc IS NOT NULL,tc.health_captured_at_utc IS NOT NULL,tc.remote_queued_at_utc IS NOT NULL,tc.owner_user_id
FROM trip_closure tc LEFT JOIN trip t ON t.id=tc.trip_id
WHERE tc.snapshot_captured_at_utc IS NOT NULL
  AND tc.owner_user_id=@owner
  AND (
    tc.state='closing'
    OR tc.completed_at_utc IS NULL
    OR tc.local_settled_at_utc IS NULL
    OR tc.tachograph_closed_at_utc IS NULL
    OR tc.health_captured_at_utc IS NULL
    OR tc.remote_queued_at_utc IS NULL
  )
ORDER BY tc.requested_at_utc;";
        Add(c,"@owner",ownerUserId);
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new PendingTripClosure(
            r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),r.GetString(2),r.GetString(3),
            r.GetDouble(4),r.GetDouble(5),r.GetDouble(6),r.GetDouble(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),
            r.GetDouble(11),r.GetDouble(12),r.GetDouble(13),r.GetDouble(14),r.GetDouble(15),r.GetString(16),
            r.GetInt64(17)!=0,r.GetInt64(18)!=0,r.GetInt64(19)!=0,r.GetInt64(20)!=0,r.GetString(21)));
        return list;
    }

    public double GetRefueledLiters(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT COALESCE(SUM(liters),0) FROM refueling WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        return Convert.ToDouble(c.ExecuteScalar()??0);
    }

    public void Begin(string tripId,string reason,string ownerUserId) => Begin(tripId,reason,null,null,null,0,0,0,ownerUserId);

    public void Begin(string tripId,string reason,TelemetrySnapshot? data,string? sessionKey,string? serverId,double distanceKm,double fuelConsumedL,double grossValue,string ownerUserId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO trip_closure(trip_id,state,reason,requested_at_utc,attempts,session_key,server_id,truck_id,
final_odometer_km,final_fuel_l,distance_km,fuel_consumed_l,gross_value,cargo_damage,cargo_mass_kg,
wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels,snapshot_captured_at_utc,owner_user_id)
VALUES(@trip,'closing',@reason,@at,1,@session,@server,@truck,@odo,@fuel,@distance,@used,@gross,@damage,@mass,@engine,@transmission,@cabin,@chassis,@wheels,@snapshot,@owner)
ON CONFLICT(trip_id) DO UPDATE SET attempts=attempts+1,last_error=''
WHERE trip_closure.owner_user_id=excluded.owner_user_id;";
        Add(c,"@trip",tripId);Add(c,"@reason",reason);Add(c,"@at",DateTime.UtcNow.ToString("O"));
        Add(c,"@session",sessionKey??"");Add(c,"@server",serverId);Add(c,"@truck",data is null?"":(!string.IsNullOrWhiteSpace(data.TruckId)?data.TruckId:data.LicensePlate??""));
        Add(c,"@odo",data?.OdometerKm??0);Add(c,"@fuel",data?.FuelLiters??0);Add(c,"@distance",Math.Max(0,distanceKm));Add(c,"@used",Math.Max(0,fuelConsumedL));Add(c,"@gross",Math.Max(0,grossValue));
        Add(c,"@damage",Math.Clamp(data?.CargoDamage??0,0f,1f));Add(c,"@mass",Math.Max(0,data?.CargoMassKg??0));
        Add(c,"@engine",data?.WearEngine??0);Add(c,"@transmission",data?.WearTransmission??0);Add(c,"@cabin",data?.WearCabin??0);Add(c,"@chassis",data?.WearChassis??0);Add(c,"@wheels",data?.WearWheels??0);
        Add(c,"@snapshot",data is null?null:DateTime.UtcNow.ToString("O"));Add(c,"@owner",ownerUserId);c.ExecuteNonQuery();
    }
    public bool Mark(string tripId,string column)
    {
        var allowed=new HashSet<string>(StringComparer.Ordinal){"local_settled_at_utc","tachograph_closed_at_utc","health_captured_at_utc","remote_queued_at_utc"};
        if(!allowed.Contains(column)) throw new ArgumentOutOfRangeException(nameof(column));
        using var c=_db.Connection.CreateCommand();c.CommandText=$"UPDATE trip_closure SET {column}=@at WHERE trip_id=@trip AND owner_user_id=@owner AND {column} IS NULL;";
        Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        return c.ExecuteNonQuery()>0 || IsMarked(tripId,column);
    }
    public bool IsMarked(string tripId,string column)
    {
        var allowed=new HashSet<string>(StringComparer.Ordinal){"local_settled_at_utc","tachograph_closed_at_utc","health_captured_at_utc","remote_queued_at_utc","completed_at_utc"};
        if(!allowed.Contains(column)) return false;
        using var c=_db.Connection.CreateCommand();c.CommandText=$"SELECT {column} IS NOT NULL FROM trip_closure WHERE trip_id=@trip AND owner_user_id=@owner;";Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        return Convert.ToInt32(c.ExecuteScalar()??0)!=0;
    }
    public bool Complete(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"UPDATE trip_closure SET state='finished',completed_at_utc=COALESCE(completed_at_utc,@at),last_error=''
WHERE trip_id=@trip AND owner_user_id=@owner AND local_settled_at_utc IS NOT NULL AND tachograph_closed_at_utc IS NOT NULL
  AND health_captured_at_utc IS NOT NULL AND remote_queued_at_utc IS NOT NULL;";
        Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        return c.ExecuteNonQuery()>0 || IsMarked(tripId,"completed_at_utc");
    }
    public void Fail(string tripId,string error)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"UPDATE trip_closure
SET state=CASE WHEN completed_at_utc IS NULL THEN 'closing' ELSE 'finished' END,last_error=@error
WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@error",error);Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());c.ExecuteNonQuery();
    }
    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}

internal sealed record PendingTripClosure(string TripId,string? ServerId,string TruckId,string SessionKey,double FinalOdometer,double FinalFuel,double DistanceKm,double FuelConsumedL,double GrossValue,double CargoDamage,double CargoMassKg,double WearEngine,double WearTransmission,double WearCabin,double WearChassis,double WearWheels,string Reason,bool LocalSettled,bool TachographClosed,bool HealthCaptured,bool RemoteQueued,string OwnerUserId);
internal sealed record TruckOperationalProfile(int Occurrences=0,int Refuelings=0,double RefueledLiters=0,double FuelCost=0,double WearEngine=0,double WearTransmission=0,double WearCabin=0,double WearChassis=0,double WearWheels=0,DateTime? LastHealthAt=null);
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
