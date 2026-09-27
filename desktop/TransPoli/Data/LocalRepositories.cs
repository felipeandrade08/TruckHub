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
INSERT INTO refueling(id,trip_id,liters,price_per_liter,total_cost,odometer_km,recorded_at_utc,station,location,fuel_before_l,fuel_after_l,truck,license_plate,owner_user_id)
VALUES(@id,@trip,@liters,@price,@cost,@odo,@at,@station,@location,@before,@after,@truck,@plate,@owner)
ON CONFLICT(id) DO UPDATE SET trip_id=CASE WHEN refueling.trip_id IS NULL OR refueling.trip_id='' THEN excluded.trip_id ELSE refueling.trip_id END,liters=excluded.liters,price_per_liter=excluded.price_per_liter,total_cost=excluded.total_cost,odometer_km=excluded.odometer_km,
recorded_at_utc=excluded.recorded_at_utc,station=excluded.station,location=excluded.location,
fuel_before_l=excluded.fuel_before_l,fuel_after_l=excluded.fuel_after_l,truck=excluded.truck,license_plate=excluded.license_plate
WHERE refueling.owner_user_id=excluded.owner_user_id;";
        Add(c,"@id",item.Id); Add(c,"@trip",tripId); Add(c,"@liters",item.Liters); Add(c,"@price",item.PricePerLiter); Add(c,"@cost",item.TotalCost); Add(c,"@odo",item.OdometerKm);
        Add(c,"@at",item.RecordedAtUtc.ToUniversalTime().ToString("O")); Add(c,"@station",item.Station);
        Add(c,"@location",item.Location); Add(c,"@before",item.FuelBefore); Add(c,"@after",item.FuelAfter);
        Add(c,"@truck",item.Truck); Add(c,"@plate",item.LicensePlate); Add(c,"@owner",SecureTokenStore.ReadUserId()); c.ExecuteNonQuery();
    }

    public void UpsertOperationalEvent(string id,string type,string status,string note,string reference,string cargoKey,string? tripId,string driver,string truck,DateTime at,float odo,bool manual)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO operational_event(id,event_type,status,note,reference,cargo_key,trip_id,driver,truck,recorded_at_utc,odometer_km,manual,owner_user_id)
VALUES(@id,@type,@status,@note,@reference,@cargo,@trip,@driver,@truck,@at,@odo,@manual,@owner)
ON CONFLICT(id) DO UPDATE SET event_type=excluded.event_type,status=excluded.status,note=excluded.note,
reference=excluded.reference,cargo_key=excluded.cargo_key,
trip_id=CASE WHEN operational_event.trip_id IS NULL OR operational_event.trip_id='' THEN excluded.trip_id ELSE operational_event.trip_id END,
driver=excluded.driver,truck=CASE WHEN operational_event.truck='' THEN excluded.truck ELSE operational_event.truck END,
recorded_at_utc=excluded.recorded_at_utc,odometer_km=excluded.odometer_km,manual=excluded.manual
WHERE operational_event.owner_user_id=excluded.owner_user_id;";
        Add(c,"@id",id);Add(c,"@type",type);Add(c,"@status",status);Add(c,"@note",note);Add(c,"@reference",reference);Add(c,"@cargo",cargoKey);Add(c,"@trip",tripId);Add(c,"@driver",driver);Add(c,"@truck",truck);Add(c,"@at",at.ToUniversalTime().ToString("O"));Add(c,"@odo",odo);Add(c,"@manual",manual?1:0);Add(c,"@owner",SecureTokenStore.ReadUserId());c.ExecuteNonQuery();
    }

    public void AttachSessionEventsToTrip(string sessionKey,string tripId)
    {
        if(string.IsNullOrWhiteSpace(sessionKey)||string.IsNullOrWhiteSpace(tripId)) return;
        var owner=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(owner)) return;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"UPDATE operational_event SET trip_id=@trip
WHERE owner_user_id=@owner AND cargo_key=@session AND (trip_id IS NULL OR trip_id='')
AND recorded_at_utc >= COALESCE((SELECT started_at_utc FROM trip WHERE id=@trip AND owner_user_id=@owner),recorded_at_utc);";
        Add(c,"@trip",tripId);Add(c,"@owner",owner);Add(c,"@session",sessionKey);
        c.ExecuteNonQuery();
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
        c.CommandText=@"INSERT INTO trip_logbook(trip_id,session_key,truck_id,cargo,route,started_at_utc,finished_at_utc,status,distance_km,fuel_consumed_l,income,expenses,net,summary,updated_at_utc,owner_user_id)
SELECT t.id,@session,COALESCE(t.truck_id,''),COALESCE(t.cargo_name,''),COALESCE(t.source_city,'')||' → '||COALESCE(t.destination_city,''),
t.started_at_utc,t.finished_at_utc,CASE WHEN t.status='finished' THEN 'FINALIZADA' WHEN t.status='cancelled' THEN 'CANCELADA' WHEN t.status='active' THEN 'EM_ANDAMENTO' ELSE UPPER(COALESCE(t.status,'INDEFINIDA')) END,
t.distance_km,t.fuel_consumed_l,t.income_gross,t.expense_total,t.net_value,
'Eventos: '||(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner)||
' • Abastecimentos: '||(SELECT COUNT(*) FROM refueling f WHERE f.trip_id=t.id AND f.owner_user_id=@owner)||
' • Pedágios: '||(SELECT COUNT(*) FROM economy_transaction x WHERE x.trip_id=t.id AND x.owner_user_id=@owner AND x.type='toll_expense' AND x.amount<0)||
' • Manutenções: '||(SELECT COUNT(*) FROM maintenance m WHERE m.trip_id=t.id AND m.owner_user_id=@owner)||
' • ETS2 confirmados: '||(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type) IN ('fine','ferry','train','cargo.damage','trip.cancelled'))||
' • Cancelamentos: '||(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='trip.cancelled')||
' • Avarias: '||(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='cargo.damage')||
' • Derivados: '||(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type) IN ('freiada_brusca','aceleracao_brusca','velocidade_elevada','parada_iniciada','parada_finalizada')),
@at,@owner FROM trip t WHERE t.id=@trip AND t.owner_user_id=@owner
ON CONFLICT(trip_id) DO UPDATE SET session_key=CASE WHEN trip_logbook.session_key='' THEN excluded.session_key ELSE trip_logbook.session_key END,truck_id=excluded.truck_id,cargo=excluded.cargo,route=excluded.route,
started_at_utc=excluded.started_at_utc,finished_at_utc=excluded.finished_at_utc,status=excluded.status,distance_km=excluded.distance_km,
fuel_consumed_l=excluded.fuel_consumed_l,income=excluded.income,expenses=excluded.expenses,net=excluded.net,summary=excluded.summary,updated_at_utc=excluded.updated_at_utc
WHERE trip_logbook.owner_user_id=excluded.owner_user_id;";
        Add(c,"@trip",tripId);Add(c,"@session",ResolveSessionKey(tripId,sessionKey));Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@owner",SecureTokenStore.ReadUserId());c.ExecuteNonQuery();
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
        c.CommandText="SELECT COALESCE(session_key,'') FROM trip_logbook WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        return Convert.ToString(c.ExecuteScalar())??"";
    }

    public List<TripLogbookEntry> GetTimeline(string tripId)
    {
        var list=new List<TripLogbookEntry>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT recorded_at_utc,event_type,status,note,odometer_km,manual FROM operational_event WHERE trip_id=@trip AND owner_user_id=@owner
ORDER BY recorded_at_utc;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());using var r=c.ExecuteReader();
        while(r.Read())
        {
            var type=r.GetString(1);var manual=r.GetInt32(5)!=0;
            var evidence=OperationalEvidence(type,manual);
            list.Add(new TripLogbookEntry(DateTime.Parse(r.GetString(0)),type,r.GetString(2),r.GetString(3),r.GetDouble(4),evidence.Source,evidence.Confidence));
        }
        return list;
    }

    private static (string Source,string Confidence) OperationalEvidence(string type,bool manual)
    {
        var t=(type??"").Trim().ToUpperInvariant();
        if(t is "REFUEL" or "TOLL" or "FINE" or "FERRY" or "TRAIN" or "TRIP.CANCELLED" or "CARGO.DAMAGE") return ("SCS_SDK","ALTA");
        if(t=="MAINTENANCE"||manual) return ("USUÁRIO","ALTA");
        if(t is "CARGO.LIFECYCLE" or "CARGA_DETECTADA" or "DOCUMENTO_PENDENTE" or "VIAGEM_AUTORIZADA" or "SAIDA" or "DESTINO_ALCANCADO" or "VIAGEM_ENCERRADA") return ("TRANSPOLI","ALTA");
        if(t is "FREIADA_BRUSCA" or "ACELERACAO_BRUSCA" or "VELOCIDADE_ELEVADA" or "PARADA_INICIADA" or "PARADA_FINALIZADA") return ("DERIVADO","MÉDIA");
        return ("TRANSPOLI","MÉDIA");
    }

    public TripOperationalIntelligence? GetOperationalIntelligence(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT t.id,COALESCE(t.truck_id,''),COALESCE(t.cargo_name,''),COALESCE(t.source_city,''),COALESCE(t.destination_city,''),
COALESCE(t.source_company,''),COALESCE(t.destination_company,''),t.status,t.distance_km,t.fuel_consumed_l,t.income_gross,t.expense_total,t.net_value,
t.started_at_utc,t.finished_at_utc,
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner),
(SELECT COUNT(*) FROM refueling f WHERE f.trip_id=t.id AND f.owner_user_id=@owner),
(SELECT COUNT(*) FROM maintenance m WHERE m.trip_id=t.id AND m.owner_user_id=@owner),
(SELECT COUNT(*) FROM economy_transaction x WHERE x.trip_id=t.id AND x.owner_user_id=@owner AND x.type='toll_expense' AND x.amount<0),
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='fine'),
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='ferry'),
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='train'),
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='cargo.damage'),
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type)='trip.cancelled'),
(SELECT COUNT(*) FROM operational_event e WHERE e.trip_id=t.id AND e.owner_user_id=@owner AND LOWER(e.event_type) IN ('freiada_brusca','aceleracao_brusca','velocidade_elevada','parada_iniciada','parada_finalizada'))
FROM trip t WHERE t.id=@trip AND t.owner_user_id=@owner LIMIT 1;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        using var r=c.ExecuteReader();if(!r.Read())return null;
        return new TripOperationalIntelligence(
            r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),
            r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.GetDouble(11),r.GetDouble(12),
            r.IsDBNull(13)?null:DateTime.Parse(r.GetString(13)),r.IsDBNull(14)?null:DateTime.Parse(r.GetString(14)),
            r.GetInt32(15),r.GetInt32(16),r.GetInt32(17),r.GetInt32(18),r.GetInt32(19),r.GetInt32(20),r.GetInt32(21),r.GetInt32(22),r.GetInt32(23),r.GetInt32(24));
    }

    public TripLogbookSummary? Get(string tripId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT trip_id,truck_id,cargo,route,started_at_utc,finished_at_utc,status,distance_km,fuel_consumed_l,income,expenses,net,summary FROM trip_logbook WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());using var r=c.ExecuteReader();if(!r.Read())return null;
        return new TripLogbookSummary(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:DateTime.Parse(r.GetString(4)),r.IsDBNull(5)?null:DateTime.Parse(r.GetString(5)),r.GetString(6),r.GetDouble(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.GetDouble(11),r.GetString(12));
    }
    public List<TripRefuelingDetail> GetRefuelings(string tripId)
    {
        var list=new List<TripRefuelingDetail>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT id,recorded_at_utc,station,location,liters,price_per_liter,total_cost,odometer_km
FROM refueling WHERE trip_id=@trip AND owner_user_id=@owner ORDER BY recorded_at_utc DESC;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new TripRefuelingDetail(r.GetString(0),DateTime.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetDouble(4),r.GetDouble(5),r.GetDouble(6),r.GetDouble(7)));
        return list;
    }

    public List<TripMaintenanceDetail> GetMaintenance(string tripId)
    {
        var list=new List<TripMaintenanceDetail>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT id,recorded_at_utc,type,component,description,cost,odometer_km
FROM maintenance WHERE trip_id=@trip AND owner_user_id=@owner ORDER BY recorded_at_utc DESC;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new TripMaintenanceDetail(r.GetString(0),DateTime.Parse(r.GetString(1)),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDouble(5),r.GetDouble(6)));
        return list;
    }

    public List<TripTollDetail> GetTolls(string tripId)
    {
        var list=new List<TripTollDetail>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT id,occurred_at_utc,description,-amount
FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner AND type='toll_expense' AND amount<0 ORDER BY occurred_at_utc DESC;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new TripTollDetail(r.GetString(0),DateTime.Parse(r.GetString(1)),r.GetString(2),r.GetDouble(3)));
        return list;
    }

    public TripSyncDetail GetSyncDetail(string tripId)
    {
        var pending=0;var attempts=0;DateTime? last=null;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT COUNT(*),COALESCE(SUM(attempts),0),MAX(last_attempt_at_utc)
FROM sync_queue WHERE trip_id=@trip AND owner_user_id=@owner AND synced_at_utc IS NULL;";
        Add(c,"@trip",tripId);Add(c,"@owner",SecureTokenStore.ReadUserId());
        using var r=c.ExecuteReader();
        if(r.Read()){pending=r.GetInt32(0);attempts=r.GetInt32(1);if(!r.IsDBNull(2)&&DateTime.TryParse(r.GetString(2),out var at))last=at;}
        return new TripSyncDetail(pending,attempts,last);
    }

    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
internal sealed record TripRefuelingDetail(string Id,DateTime At,string Station,string Location,double Liters,double PricePerLiter,double TotalCost,double OdometerKm);
internal sealed record TripMaintenanceDetail(string Id,DateTime At,string Type,string Component,string Description,double Cost,double OdometerKm);
internal sealed record TripTollDetail(string Id,DateTime At,string Description,double Amount);
internal sealed record TripSyncDetail(int Pending,int Attempts,DateTime? LastAttemptAt);
internal sealed record TripLogbookEntry(DateTime At,string Type,string Status,string Details,double OdometerKm,string Source,string Confidence);
internal sealed record TripOperationalIntelligence(string TripId,string TruckId,string Cargo,string Origin,string Destination,string OriginCompany,string DestinationCompany,string Status,double DistanceKm,double FuelLiters,double Income,double Expenses,double Net,DateTime? StartedAt,DateTime? FinishedAt,int Events,int Refuelings,int Maintenance,int Tolls,int Fines,int Ferries,int Trains,int CargoDamageEvents,int CancellationEvents,int DerivedDrivingEvents);
internal sealed record TripLogbookSummary(string TripId,string TruckId,string Cargo,string Route,DateTime? StartedAt,DateTime? FinishedAt,string Status,double DistanceKm,double FuelLiters,double Income,double Expenses,double Net,string Summary);

internal sealed class LocalSyncQueueRepository
{
    private readonly TransPoliDb _db;
    public LocalSyncQueueRepository(TransPoliDb db)=>_db=db;

    public bool Enqueue(string id,string type,string? tripId,string payload,DateTime createdAtUtc,string ownerUserId)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT INTO sync_queue(id,event_type,trip_id,payload_json,created_at_utc,attempts,last_attempt_at_utc,synced_at_utc,owner_user_id)
VALUES(@id,@type,@trip,@payload,@created,0,NULL,NULL,@owner)
ON CONFLICT(id) DO UPDATE SET
payload_json=CASE WHEN sync_queue.synced_at_utc IS NULL THEN excluded.payload_json ELSE sync_queue.payload_json END,
trip_id=CASE WHEN sync_queue.synced_at_utc IS NULL THEN excluded.trip_id ELSE sync_queue.trip_id END
WHERE sync_queue.synced_at_utc IS NULL AND sync_queue.owner_user_id=excluded.owner_user_id;";
        Add(c,"@id",id);Add(c,"@type",type);Add(c,"@trip",tripId);Add(c,"@payload",payload);Add(c,"@created",createdAtUtc.ToUniversalTime().ToString("O"));Add(c,"@owner",ownerUserId);c.ExecuteNonQuery();
        using var verify=_db.Connection.CreateCommand();
        // Enqueue means "this idempotency key is durably known for this owner".
        // A deterministic operation that was already synchronized is also success:
        // treating it as failure makes a replayed ETS2 pulse look unsaved forever.
        verify.CommandText="SELECT COUNT(1) FROM sync_queue WHERE id=@id AND owner_user_id=@owner;";
        Add(verify,"@id",id);Add(verify,"@owner",ownerUserId);
        return Convert.ToInt32(verify.ExecuteScalar()??0)>0;
    }

    public bool HasPendingTripStart(string tripId,string ownerUserId)
    {
        if(string.IsNullOrWhiteSpace(tripId)||string.IsNullOrWhiteSpace(ownerUserId)) return false;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT COUNT(1) FROM sync_queue
WHERE trip_id=@trip AND event_type='trip.start' AND owner_user_id=@owner AND synced_at_utc IS NULL;";
        Add(c,"@trip",tripId);Add(c,"@owner",ownerUserId);
        return Convert.ToInt32(c.ExecuteScalar()??0)>0;
    }

    public bool HasPendingTripFinish(string tripId,string ownerUserId)
    {
        if(string.IsNullOrWhiteSpace(tripId)||string.IsNullOrWhiteSpace(ownerUserId)) return false;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT COUNT(1) FROM sync_queue
WHERE trip_id=@trip AND event_type='trip.finish' AND owner_user_id=@owner AND synced_at_utc IS NULL;";
        Add(c,"@trip",tripId);Add(c,"@owner",ownerUserId);
        return Convert.ToInt32(c.ExecuteScalar()??0)>0;
    }

    public List<LocalSyncItem> GetPending(string ownerUserId,int limit=100)
    {
        var list=new List<LocalSyncItem>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT id,event_type,trip_id,payload_json,created_at_utc,attempts,last_attempt_at_utc,owner_user_id FROM sync_queue WHERE synced_at_utc IS NULL AND owner_user_id=@owner ORDER BY created_at_utc LIMIT @limit;";
        Add(c,"@owner",ownerUserId);Add(c,"@limit",limit);
        using var r=c.ExecuteReader();
        while(r.Read()) list.Add(new LocalSyncItem(r.GetString(0),r.GetString(1),r.IsDBNull(2)?null:r.GetString(2),r.GetString(3),DateTime.Parse(r.GetString(4)),r.GetInt32(5),r.IsDBNull(6)?null:DateTime.Parse(r.GetString(6)),r.GetString(7)));
        return list;
    }

    public bool MarkSynced(string id,string ownerUserId)
    {
        if(string.IsNullOrWhiteSpace(ownerUserId)) return false;
        using var c=_db.Connection.CreateCommand();c.CommandText="UPDATE sync_queue SET synced_at_utc=COALESCE(synced_at_utc,@at) WHERE id=@id AND owner_user_id=@owner;";Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@id",id);Add(c,"@owner",ownerUserId);
        if(c.ExecuteNonQuery()<=0)return false;
        using var verify=_db.Connection.CreateCommand();verify.CommandText="SELECT synced_at_utc IS NOT NULL FROM sync_queue WHERE id=@id AND owner_user_id=@owner;";Add(verify,"@id",id);Add(verify,"@owner",ownerUserId);
        return Convert.ToInt32(verify.ExecuteScalar()??0)!=0;
    }

    public bool MarkAttempt(string id,string ownerUserId)
    {
        if(string.IsNullOrWhiteSpace(ownerUserId)) return false;
        using var c=_db.Connection.CreateCommand();c.CommandText="UPDATE sync_queue SET attempts=attempts+1,last_attempt_at_utc=@at WHERE id=@id AND owner_user_id=@owner AND synced_at_utc IS NULL;";Add(c,"@at",DateTime.UtcNow.ToString("O"));Add(c,"@id",id);Add(c,"@owner",ownerUserId);
        return c.ExecuteNonQuery()>0;
    }

    private static void Add(SqliteCommand c,string name,object? value)=>c.Parameters.AddWithValue(name,value??DBNull.Value);
}

internal sealed record LocalSyncItem(string Id,string Type,string? TripId,string PayloadJson,DateTime CreatedAtUtc,int Attempts,DateTime? LastAttemptAtUtc,string OwnerUserId);
