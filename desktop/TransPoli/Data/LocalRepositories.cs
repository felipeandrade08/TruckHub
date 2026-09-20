using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TransPoli;

internal static class LocalData
{
    public static LocalDataStore? Current { get; set; }
}

internal sealed class LocalOperationsRepository
{
    private readonly TransPoliDb _db;
    public LocalOperationsRepository(TransPoliDb db) => _db = db;

    public void UpsertRefueling(RefuelingRecord item, string? tripId = null)
    {
        using var c = _db.Connection.CreateCommand();
        c.CommandText = @"
INSERT INTO refueling(id,trip_id,liters,price_per_liter,total_cost,odometer_km,recorded_at_utc,station,location,fuel_before_l,fuel_after_l,truck,license_plate)
VALUES(@id,@trip,@liters,0,0,@odo,@at,@station,@location,@before,@after,@truck,@plate)
ON CONFLICT(id) DO UPDATE SET trip_id=excluded.trip_id,liters=excluded.liters,odometer_km=excluded.odometer_km,
recorded_at_utc=excluded.recorded_at_utc,station=excluded.station,location=excluded.location,
fuel_before_l=excluded.fuel_before_l,fuel_after_l=excluded.fuel_after_l,truck=excluded.truck,license_plate=excluded.license_plate;";
        Add(c,"@id",item.Id); Add(c,"@trip",tripId); Add(c,"@liters",item.Liters); Add(c,"@odo",item.OdometerKm);
        Add(c,"@at",item.RecordedAtUtc.ToUniversalTime().ToString("O")); Add(c,"@station",item.Station);
        Add(c,"@location",item.Location); Add(c,"@before",item.FuelBefore); Add(c,"@after",item.FuelAfter);
        Add(c,"@truck",item.Truck); Add(c,"@plate",item.LicensePlate); c.ExecuteNonQuery();
    }

    public void UpsertOperationalEvent(string id,string type,string status,string note,string reference,string cargoKey,string? tripId,string driver,string truck,DateTime at,float odo,bool manual)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT OR REPLACE INTO operational_event(id,event_type,status,note,reference,cargo_key,trip_id,driver,truck,recorded_at_utc,odometer_km,manual)
VALUES(@id,@type,@status,@note,@reference,@cargo,@trip,@driver,@truck,@at,@odo,@manual);";
        Add(c,"@id",id);Add(c,"@type",type);Add(c,"@status",status);Add(c,"@note",note);Add(c,"@reference",reference);Add(c,"@cargo",cargoKey);Add(c,"@trip",tripId);Add(c,"@driver",driver);Add(c,"@truck",truck);Add(c,"@at",at.ToUniversalTime().ToString("O"));Add(c,"@odo",odo);Add(c,"@manual",manual?1:0);c.ExecuteNonQuery();
    }

    public void UpsertCargo(CargoOperationState state)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO cargo_operation(id,lifecycle,route,cargo,speed_kph,cargo_loaded,trip_active,updated,last_update_utc,last_transition_utc)
VALUES(1,@life,@route,@cargo,@speed,@loaded,@active,@updated,@last,@transition)
ON CONFLICT(id) DO UPDATE SET lifecycle=excluded.lifecycle,route=excluded.route,cargo=excluded.cargo,speed_kph=excluded.speed_kph,
cargo_loaded=excluded.cargo_loaded,trip_active=excluded.trip_active,updated=excluded.updated,last_update_utc=excluded.last_update_utc,last_transition_utc=excluded.last_transition_utc;";
        Add(c,"@life",state.Lifecycle.ToString());Add(c,"@route",state.Route);Add(c,"@cargo",state.Cargo);Add(c,"@speed",state.SpeedKph);
        Add(c,"@loaded",state.CargoLoaded?1:0);Add(c,"@active",state.TripActive?1:0);Add(c,"@updated",state.Updated?1:0);
        Add(c,"@last",state.LastUpdateUtc.ToUniversalTime().ToString("O"));Add(c,"@transition",state.LastTransitionUtc.ToUniversalTime().ToString("O"));c.ExecuteNonQuery();
    }

    public void AppendCargoTimeline(CargoTimelineEntry entry)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="INSERT INTO cargo_timeline(recorded_at_utc,lifecycle,details) VALUES(@at,@life,@details);";
        Add(c,"@at",entry.AtUtc.ToUniversalTime().ToString("O"));Add(c,"@life",entry.Lifecycle.ToString());Add(c,"@details",entry.Details);c.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand c,string name,object? value)=>c.Parameters.AddWithValue(name,value??DBNull.Value);
}

internal sealed class LocalSyncQueueRepository
{
    private readonly TransPoliDb _db;
    public LocalSyncQueueRepository(TransPoliDb db)=>_db=db;

    public void Enqueue(string id,string type,string? tripId,string payload,DateTime createdAtUtc)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT OR IGNORE INTO sync_queue(id,event_type,trip_id,payload_json,created_at_utc,attempts,last_attempt_at_utc,synced_at_utc)
VALUES(@id,@type,@trip,@payload,@created,0,NULL,NULL);";
        Add(c,"@id",id);Add(c,"@type",type);Add(c,"@trip",tripId);Add(c,"@payload",payload);Add(c,"@created",createdAtUtc.ToUniversalTime().ToString("O"));c.ExecuteNonQuery();
    }

    public List<LocalSyncItem> GetPending(int limit=100)
    {
        var list=new List<LocalSyncItem>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT id,event_type,trip_id,payload_json,created_at_utc,attempts FROM sync_queue WHERE synced_at_utc IS NULL ORDER BY created_at_utc LIMIT @limit;";
        Add(c,"@limit",limit);
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new LocalSyncItem(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),DateTime.Parse(r.GetString(4)),r.GetInt32(5)));
        return list;
    }

    public void MarkSynced(string id)
    {
        using var c=_db.Connection.CreateCommand();c.CommandText="UPDATE sync_queue SET synced_at_utc=@at WHERE id=@id;";Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@id",id);c.ExecuteNonQuery();
    }

    public void MarkAttempt(string id)
    {
        using var c=_db.Connection.CreateCommand();c.CommandText="UPDATE sync_queue SET attempts=attempts+1,last_attempt_at_utc=@at WHERE id=@id;";Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@id",id);c.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand c,string name,object? value)=>c.Parameters.AddWithValue(name,value??DBNull.Value);
}

internal sealed record LocalSyncItem(string Id,string Type,string? TripId,string PayloadJson,DateTime CreatedAtUtc,int Attempts);
