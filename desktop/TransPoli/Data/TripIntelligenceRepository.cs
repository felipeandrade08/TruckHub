using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using TransPoli.Intelligence;

namespace TransPoli;

internal sealed record TripIntelligenceSnapshot(string TripId,string Status,string TruckId,string Cargo,string Origin,string OriginCompany,string Destination,string DestinationCompany,double DistanceKm,double PlannedDistanceKm,double FuelConsumedL,double Income,double Expenses,double Net,DateTime? StartedAtUtc,DateTime? FinishedAtUtc,string FinishReason,IReadOnlyList<TripIntelligenceEvent> Timeline,int Refuelings=0,int Maintenance=0,int Tolls=0,int Fines=0,int Ferries=0,int Trains=0,int CargoDamageEvents=0,int CancellationEvents=0,int DerivedDrivingEvents=0,DateTime? LastEventAtUtc=null,bool HasDistance=false,bool HasPlannedDistance=false,bool HasFuel=false,bool HasIncome=false,bool HasExpenses=false,bool HasNet=false,DataSourceKind Source=DataSourceKind.LocalCache,DataFreshnessState State=DataFreshnessState.Offline);
internal sealed record TripIntelligenceEvent(DateTime AtUtc,string Type,string Status,string Details,double OdometerKm,string Source,string Confidence);

/// <summary>Projecao somente leitura das fontes locais existentes. Nao cria lifecycle nem economia paralelos.</summary>
internal sealed class TripIntelligenceRepository
{
    private readonly TransPoliDb _db;
    public TripIntelligenceRepository(TransPoliDb db)=>_db=db;

    public TripIntelligenceSnapshot? Read(string tripId)
    {
        var owner=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(owner)||string.IsNullOrWhiteSpace(tripId)) return null;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT id,status,COALESCE(truck_id,''),COALESCE(cargo_name,''),COALESCE(source_city,''),COALESCE(source_company,''),COALESCE(destination_city,''),COALESCE(destination_company,''),distance_km,planned_distance_km,fuel_consumed_l,income_gross,expense_total,net_value,started_at_utc,finished_at_utc,COALESCE(finish_reason,'') FROM trip WHERE id=@trip AND owner_user_id=@owner LIMIT 1;";
        Add(c,"@trip",tripId); Add(c,"@owner",owner);
        TripIntelligenceSnapshot snapshot;
        using(var r=c.ExecuteReader())
        {
            if(!r.Read()) return null;
            snapshot=new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),Number(r,8),Number(r,9),Number(r,10),Number(r,11),Number(r,12),Number(r,13),Date(r,14),Date(r,15),r.GetString(16),Array.Empty<TripIntelligenceEvent>(),HasDistance:!r.IsDBNull(8),HasPlannedDistance:!r.IsDBNull(9),HasFuel:!r.IsDBNull(10),HasIncome:!r.IsDBNull(11),HasExpenses:!r.IsDBNull(12),HasNet:!r.IsDBNull(13));
        }
        var timeline=ReadTimeline(tripId,owner);
        return snapshot with
        {
            Timeline=timeline,
            Refuelings=Count("refueling",tripId,owner),
            Maintenance=Count("maintenance",tripId,owner),
            Tolls=CountTolls(tripId,owner),
            Fines=CountOperationalEvent("fine",tripId,owner),
            Ferries=CountOperationalEvent("ferry",tripId,owner),
            Trains=CountOperationalEvent("train",tripId,owner),
            CargoDamageEvents=CountOperationalEvent("cargo.damage",tripId,owner),
            CancellationEvents=CountOperationalEvent("trip.cancelled",tripId,owner),
            DerivedDrivingEvents=CountOperationalEvents(new[]{"freiada_brusca","aceleracao_brusca","velocidade_elevada","parada_iniciada","parada_finalizada"},tripId,owner),
            LastEventAtUtc=timeline.Count==0?null:timeline[^1].AtUtc
        };
    }

    private int Count(string table,string tripId,string owner)
    {
        if(table is not ("refueling" or "maintenance")) return 0;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=$"SELECT COUNT(*) FROM {table} WHERE trip_id=@trip AND owner_user_id=@owner;";
        Add(c,"@trip",tripId);Add(c,"@owner",owner);
        return Convert.ToInt32(c.ExecuteScalar()??0);
    }
    private int CountTolls(string tripId,string owner)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT COUNT(*) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner AND type='toll_expense' AND amount<0;";
        Add(c,"@trip",tripId);Add(c,"@owner",owner);
        return Convert.ToInt32(c.ExecuteScalar()??0);
    }

    private int CountOperationalEvent(string type,string tripId,string owner)
    {
        using var c=_db.Connection.CreateCommand();
        c.CommandText="SELECT COUNT(*) FROM operational_event WHERE trip_id=@trip AND owner_user_id=@owner AND LOWER(event_type)=LOWER(@type);";
        Add(c,"@trip",tripId);Add(c,"@owner",owner);Add(c,"@type",type);
        return Convert.ToInt32(c.ExecuteScalar()??0);
    }

    private int CountOperationalEvents(string[] types,string tripId,string owner)
    {
        var total=0;
        foreach(var type in types) total+=CountOperationalEvent(type,tripId,owner);
        return total;
    }

    private IReadOnlyList<TripIntelligenceEvent> ReadTimeline(string tripId,string owner)
    {
        var result=new List<TripIntelligenceEvent>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT recorded_at_utc,event_type,status,note,odometer_km,manual,
COALESCE(source,'UNKNOWN'),COALESCE(confidence,'MEDIUM')
FROM operational_event WHERE trip_id=@trip AND owner_user_id=@owner
ORDER BY recorded_at_utc;";
        Add(c,"@trip",tripId); Add(c,"@owner",owner);
        using var r=c.ExecuteReader();
        while(r.Read())
        {
            var source=r.IsDBNull(6)?"UNKNOWN":r.GetString(6);
            var confidence=r.IsDBNull(7)?"MEDIUM":r.GetString(7);
            // Legado permanece UNKNOWN: Trip Intelligence não adivinha mais a
            // origem física pelo nome do evento.
            result.Add(new(Parse(r.GetString(0))??DateTime.MinValue,r.GetString(1),r.IsDBNull(2)?"":r.GetString(2),r.IsDBNull(3)?"":r.GetString(3),r.GetDouble(4),source,confidence));
        }
        return result;
    }
    private static double Number(SqliteDataReader r,int i)=>r.IsDBNull(i)?0d:r.GetDouble(i);
    private static DateTime? Date(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:Parse(r.GetString(i));
    private static DateTime? Parse(string value)=>DateTime.TryParse(value,null,DateTimeStyles.RoundtripKind,out var d)?d:null;
    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
