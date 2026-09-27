using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace TransPoli;

internal sealed record TripIntelligenceSnapshot(string TripId,string Status,string TruckId,string Cargo,string Origin,string OriginCompany,string Destination,string DestinationCompany,double DistanceKm,double PlannedDistanceKm,double FuelConsumedL,double Income,double Expenses,double Net,DateTime? StartedAtUtc,DateTime? FinishedAtUtc,string FinishReason,IReadOnlyList<TripIntelligenceEvent> Timeline,int Refuelings=0,int Maintenance=0,int Tolls=0);
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
        c.CommandText=@"SELECT id,status,COALESCE(truck_id,''),COALESCE(cargo_name,''),COALESCE(source_city,''),COALESCE(source_company,''),COALESCE(destination_city,''),COALESCE(destination_company,''),COALESCE(distance_km,0),COALESCE(planned_distance_km,0),COALESCE(fuel_consumed_l,0),COALESCE(income_gross,0),COALESCE(expense_total,0),COALESCE(net_value,0),started_at_utc,finished_at_utc,COALESCE(finish_reason,'') FROM trip WHERE id=@trip AND owner_user_id=@owner LIMIT 1;";
        Add(c,"@trip",tripId); Add(c,"@owner",owner);
        TripIntelligenceSnapshot snapshot;
        using(var r=c.ExecuteReader())
        {
            if(!r.Read()) return null;
            snapshot=new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetDouble(8),r.GetDouble(9),r.GetDouble(10),r.GetDouble(11),r.GetDouble(12),r.GetDouble(13),Date(r,14),Date(r,15),r.GetString(16),Array.Empty<TripIntelligenceEvent>());
        }
        var timeline=ReadTimeline(tripId,owner);
        return snapshot with
        {
            Timeline=timeline,
            Refuelings=Count("refueling",tripId,owner),
            Maintenance=Count("maintenance",tripId,owner),
            Tolls=CountTolls(tripId,owner)
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

    private IReadOnlyList<TripIntelligenceEvent> ReadTimeline(string tripId,string owner)
    {
        var result=new List<TripIntelligenceEvent>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT recorded_at_utc,event_type,status,note,odometer_km,manual FROM operational_event WHERE trip_id=@trip AND owner_user_id=@owner
ORDER BY recorded_at_utc;";
        Add(c,"@trip",tripId); Add(c,"@owner",owner);
        using var r=c.ExecuteReader();
        while(r.Read())
        {
            var type=r.GetString(1); var manual=r.GetInt32(5)!=0;
            var (source,confidence)=Classify(type,manual);
            result.Add(new(Parse(r.GetString(0))??DateTime.MinValue,type,r.IsDBNull(2)?"":r.GetString(2),r.IsDBNull(3)?"":r.GetString(3),r.GetDouble(4),source,confidence));
        }
        return result;
    }

    private static (string Source,string Confidence) Classify(string type,bool manual)
    {
        var t=(type??"").Trim().ToUpperInvariant();
        if(t is "REFUEL" or "TOLL" or "FINE" or "FERRY" or "TRAIN" or "TRIP.CANCELLED") return ("SCS_SDK","HIGH");
        if(t is "MAINTENANCE") return ("USER","HIGH");
        if(manual) return ("USER","HIGH");
        if(t is "CARGO.LIFECYCLE") return ("TRANSPOLI","HIGH");
        if(t is "FREIADA_BRUSCA" or "ACELERACAO_BRUSCA" or "VELOCIDADE_ELEVADA" or "MANUTENCAO_CRITICA") return ("DERIVED","MEDIUM");
        // operational_event ainda não persiste a origem física do evento.
        // Não promover pedágio/multa/ferry/etc. a SCS_SDK apenas pelo nome.
        return ("TRANSPOLI","MEDIUM");
    }
    private static DateTime? Date(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:Parse(r.GetString(i));
    private static DateTime? Parse(string value)=>DateTime.TryParse(value,null,DateTimeStyles.RoundtripKind,out var d)?d:null;
    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
