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
ON CONFLICT(id) DO UPDATE SET trip_id=CASE WHEN refueling.trip_id IS NULL OR refueling.trip_id='' THEN excluded.trip_id ELSE refueling.trip_id END,liters=excluded.liters,odometer_km=excluded.odometer_km,
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
        c.CommandText=@"INSERT INTO operational_event(id,event_type,status,note,reference,cargo_key,trip_id,driver,truck,recorded_at_utc,odometer_km,manual)
VALUES(@id,@type,@status,@note,@reference,@cargo,@trip,@driver,@truck,@at,@odo,@manual)
ON CONFLICT(id) DO UPDATE SET event_type=excluded.event_type,status=excluded.status,note=excluded.note,
reference=excluded.reference,cargo_key=excluded.cargo_key,
trip_id=CASE WHEN operational_event.trip_id IS NULL OR operational_event.trip_id='' THEN excluded.trip_id ELSE operational_event.trip_id END,
driver=excluded.driver,truck=CASE WHEN operational_event.truck='' THEN excluded.truck ELSE operational_event.truck END,
recorded_at_utc=excluded.recorded_at_utc,odometer_km=excluded.odometer_km,manual=excluded.manual;";
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


internal sealed class LocalTripLogbookRepository
{
    private readonly TransPoliDb _db;
    public LocalTripLogbookRepository(TransPoliDb db)=>_db=db;

    public void Consolidate(string tripId,string sessionKey)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO trip_logbook(trip_id,session_key,truck_id,cargo,route,started_at_utc,finished_at_utc,status,distance_km,fuel_consumed_l,income,expenses,net,summary,updated_at_utc)
SELECT t.id,@session,COALESCE(t.truck_id,''),COALESCE(t.cargo_name,''),COALESCE(t.source_city,'')||' → '||COALESCE(t.destination_city,''),
t.started_at_utc,t.finished_at_utc,CASE WHEN t.status='finished' THEN 'FINALIZADA' ELSE 'EM_ANDAMENTO' END,
t.distance_km,t.fuel_consumed_l,t.income_gross,t.expense_total,t.net_value,
'Eventos: '||(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id)||
' • Abastecimentos: '||(SELECT COUNT(*) FROM refueling f WHERE f.trip_id=t.id)||
' • Manutenções: '||(SELECT COUNT(*) FROM maintenance m WHERE m.trip_id=t.id),
@at FROM trip t WHERE t.id=@trip
ON CONFLICT(trip_id) DO UPDATE SET session_key=CASE WHEN trip_logbook.session_key='' THEN excluded.session_key ELSE trip_logbook.session_key END,truck_id=excluded.truck_id,cargo=excluded.cargo,route=excluded.route,
started_at_utc=excluded.started_at_utc,finished_at_utc=excluded.finished_at_utc,status=excluded.status,distance_km=excluded.distance_km,
fuel_consumed_l=excluded.fuel_consumed_l,income=excluded.income,expenses=excluded.expenses,net=excluded.net,summary=excluded.summary,updated_at_utc=excluded.updated_at_utc;";
        Add(c,"@trip",tripId);Add(c,"@session",ResolveSessionKey(tripId,sessionKey));Add(c,"@at",DateTime.UtcNow.ToString("O"));c.ExecuteNonQuery();
    }

    private string ResolveSessionKey(string tripId,string? requested)
    {
        var existing=GetSessionKey(tripId);
        if(!string.IsNullOrWhiteSpace(existing)) return existing;
        if(!string.IsNullOrWhiteSpace(requested)) return requested;

        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT COALESCE(session_key,'') FROM trip_closure WHERE trip_id=@trip AND snapshot_captured_at_utc IS NOT NULL LIMIT 1;";
        Add(c,"@trip",tripId);
        return Convert.ToString(c.ExecuteScalar())??"";
    }

    public string GetSessionKey(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT COALESCE(session_key,'') FROM trip_logbook WHERE trip_id=@trip;";
        Add(c,"@trip",tripId);
        return Convert.ToString(c.ExecuteScalar())??"";
    }

    public List<TripLogbookEntry> GetTimeline(string tripId)
    {
        var list=new List<TripLogbookEntry>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT recorded_at_utc,event_type,status,note,odometer_km FROM operational_event WHERE trip_id=@trip
UNION ALL SELECT recorded_at_utc,'ABASTECIMENTO',station,printf('%.1f L',liters),odometer_km FROM refueling WHERE trip_id=@trip
UNION ALL SELECT recorded_at_utc,'MANUTENCAO',type,description,odometer_km FROM maintenance WHERE trip_id=@trip
ORDER BY recorded_at_utc;";
        Add(c,"@trip",tripId);using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new TripLogbookEntry(DateTime.Parse(r.GetString(0)),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4)));
        return list;
    }

    public TripLogbookSummary? Get(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT trip_id,truck_id,cargo,route,started_at_utc,finished_at_utc,status,distance_km,fuel_consumed_l,income,expenses,net,summary FROM trip_logbook WHERE trip_id=@trip;";
        Add(c,"@trip",tripId);using var r=c.ExecuteReader();if(!r.Read())return null;
        return new TripLogbookSummary(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:DateTime.Parse(r.GetString(4)),r.IsDBNull(5)?null:DateTime.Parse(r.GetString(5)),r.GetString(6),r.GetDouble(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.GetDouble(11),r.GetString(12));
    }
    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
internal sealed record TripLogbookEntry(DateTime At,string Type,string Status,string Details,double OdometerKm);
internal sealed record TripLogbookSummary(string TripId,string TruckId,string Cargo,string Route,DateTime? StartedAt,DateTime? FinishedAt,string Status,double DistanceKm,double FuelLiters,double Income,double Expenses,double Net,string Summary);

internal sealed class LocalSyncQueueRepository
{
    private readonly TransPoliDb _db;
    public LocalSyncQueueRepository(TransPoliDb db)=>_db=db;

    public bool Enqueue(string id,string type,string? tripId,string payload,DateTime createdAtUtc)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO sync_queue(id,event_type,trip_id,payload_json,created_at_utc,attempts,last_attempt_at_utc,synced_at_utc)
VALUES(@id,@type,@trip,@payload,@created,0,NULL,NULL)
ON CONFLICT(id) DO UPDATE SET
payload_json=CASE WHEN sync_queue.synced_at_utc IS NULL THEN excluded.payload_json ELSE sync_queue.payload_json END,
trip_id=CASE WHEN sync_queue.synced_at_utc IS NULL THEN excluded.trip_id ELSE sync_queue.trip_id END
WHERE sync_queue.synced_at_utc IS NULL;";
        Add(c,"@id",id);Add(c,"@type",type);Add(c,"@trip",tripId);Add(c,"@payload",payload);Add(c,"@created",createdAtUtc.ToUniversalTime().ToString("O"));c.ExecuteNonQuery();
        using var verify=_db.Connection.CreateCommand();
        verify.CommandText="SELECT COUNT(1) FROM sync_queue WHERE id=@id AND synced_at_utc IS NULL;";
        Add(verify,"@id",id);
        return Convert.ToInt32(verify.ExecuteScalar()??0)>0;
    }

    public bool HasPendingTripFinish(string tripId)
    {
        if(string.IsNullOrWhiteSpace(tripId)) return false;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT COUNT(1) FROM sync_queue
WHERE trip_id=@trip AND event_type='trip.finish' AND synced_at_utc IS NULL;";
        Add(c,"@trip",tripId);
        return Convert.ToInt32(c.ExecuteScalar()??0)>0;
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
