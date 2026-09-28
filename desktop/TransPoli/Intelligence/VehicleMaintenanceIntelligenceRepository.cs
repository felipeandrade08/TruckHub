using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace TransPoli;

internal sealed record MaintenanceComponentState(
    string Component,double Wear,DateTime? LastServiceAtUtc,double LastServiceOdometerKm,
    double? NextServiceOdometerKm,double? RemainingKm,bool? Overdue,string WearSource,string ServicePolicySource);
internal sealed record VehicleMaintenanceIntelligence(string TruckId,double CurrentOdometerKm,IReadOnlyList<MaintenanceComponentState> Components);
internal sealed record MaintenanceHistoryEntry(string Id,string Component,string Type,string Description,decimal Cost,double OdometerKm,DateTime? RecordedAtUtc,string? TripId,string Source);
internal sealed record VehicleHealthHistoryEntry(string? TripId,DateTime RecordedAtUtc,double OdometerKm,double Engine,double Transmission,double Cabin,double Chassis,double Wheels,string Source);

/// <summary>
/// Projecao de manutencao sobre snapshots e historico existentes.
/// Intervalos sao politica TransPoli; custos permanecem no repositorio financeiro atual.
/// </summary>
internal sealed class VehicleMaintenanceIntelligenceRepository
{
    private readonly TransPoliDb _db;
    // Não existe aqui um intervalo "oficial do ETS2". Próximo serviço só pode ser
    // projetado quando uma política TransPoli explícita for fornecida pelo consumidor.
    public VehicleMaintenanceIntelligenceRepository(TransPoliDb db)=>_db=db;

    public VehicleMaintenanceIntelligence Read(string truckId,double currentOdometerKm,double engine,double transmission,double cabin,double chassis,double wheels,double? serviceIntervalKm=null,string wearSource="TELEMETRY")
    {
        var components=new List<MaintenanceComponentState>();
        Add(components,truckId,"engine",engine,currentOdometerKm,serviceIntervalKm,wearSource);
        Add(components,truckId,"transmission",transmission,currentOdometerKm,serviceIntervalKm,wearSource);
        Add(components,truckId,"cabin",cabin,currentOdometerKm,serviceIntervalKm,wearSource);
        Add(components,truckId,"chassis",chassis,currentOdometerKm,serviceIntervalKm,wearSource);
        Add(components,truckId,"wheels",wheels,currentOdometerKm,serviceIntervalKm,wearSource);
        return new(truckId,currentOdometerKm,components);
    }

    public IReadOnlyList<VehicleHealthHistoryEntry> ReadHealthHistory(string truckId,int limit=20)
    {
        var owner=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(owner)||string.IsNullOrWhiteSpace(truckId)) return Array.Empty<VehicleHealthHistoryEntry>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT trip_id,recorded_at_utc,odometer_km,wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels
FROM truck_health_snapshot
WHERE owner_user_id=@owner AND truck_id=@truck
ORDER BY recorded_at_utc DESC LIMIT @limit;";
        Param(c,"@owner",owner);Param(c,"@truck",truckId);Param(c,"@limit",Math.Clamp(limit,1,100));
        var rows=new List<VehicleHealthHistoryEntry>();
        using var r=c.ExecuteReader();
        while(r.Read())
        {
            if(r.IsDBNull(1)||!DateTime.TryParse(r.GetString(1),out var at)) continue;
            rows.Add(new(
                r.IsDBNull(0)?null:r.GetString(0),at,
                r.IsDBNull(2)?0:r.GetDouble(2),
                r.IsDBNull(3)?0:r.GetDouble(3),r.IsDBNull(4)?0:r.GetDouble(4),
                r.IsDBNull(5)?0:r.GetDouble(5),r.IsDBNull(6)?0:r.GetDouble(6),
                r.IsDBNull(7)?0:r.GetDouble(7),"SCS_SDK"));
        }
        return rows;
    }

    public IReadOnlyList<MaintenanceHistoryEntry> ReadHistory(string truckId,int limit=20)
    {
        var owner=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(owner)||string.IsNullOrWhiteSpace(truckId)) return Array.Empty<MaintenanceHistoryEntry>();
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT id,component,type,description,cost,odometer_km,recorded_at_utc,trip_id
FROM maintenance
WHERE owner_user_id=@owner AND truck_id=@truck
ORDER BY recorded_at_utc DESC LIMIT @limit;";
        Param(c,"@owner",owner);Param(c,"@truck",truckId);Param(c,"@limit",Math.Clamp(limit,1,100));
        var rows=new List<MaintenanceHistoryEntry>();
        using var r=c.ExecuteReader();
        while(r.Read())
        {
            DateTime? at=null;
            if(!r.IsDBNull(6)&&DateTime.TryParse(r.GetString(6),out var parsed)) at=parsed;
            rows.Add(new(
                r.IsDBNull(0)?"":r.GetString(0),
                r.IsDBNull(1)?"":r.GetString(1),
                r.IsDBNull(2)?"":r.GetString(2),
                r.IsDBNull(3)?"":r.GetString(3),
                r.IsDBNull(4)?0m:Convert.ToDecimal(r.GetDouble(4)),
                r.IsDBNull(5)?0:r.GetDouble(5),
                at,
                r.IsDBNull(7)?null:r.GetString(7),
                "TRANSPOLI"));
        }
        return rows;
    }

    private void Add(List<MaintenanceComponentState> list,string truckId,string component,double wear,double currentOdo,double? serviceIntervalKm,string wearSource)
    {
        var owner=SecureTokenStore.ReadUserId();
        DateTime? at=null; double lastOdo=0;
        if(!string.IsNullOrWhiteSpace(owner))
        {
            using var c=_db.Connection.CreateCommand();
            var aliases=Aliases(component);
            var aliasParameters=new List<string>(aliases.Count);
            for(var i=0;i<aliases.Count;i++) aliasParameters.Add("@component"+i);
            c.CommandText=@"SELECT recorded_at_utc,odometer_km FROM maintenance
WHERE truck_id=@truck AND owner_user_id=@owner
AND lower(trim(component)) IN ("+string.Join(",",aliasParameters)+@")
ORDER BY recorded_at_utc DESC LIMIT 1;";
            Param(c,"@truck",truckId); Param(c,"@owner",owner);
            for(var i=0;i<aliases.Count;i++) Param(c,"@component"+i,aliases[i]);
            using var r=c.ExecuteReader();
            if(r.Read()){ if(DateTime.TryParse(r.GetString(0),out var d)) at=d; lastOdo=r.GetDouble(1); }
        }
        // Sem manutenção registrada, ancora a política no primeiro odômetro
        // conhecido do caminhão. Assim a previsão não "anda para frente" a cada leitura.
        var baseline=lastOdo;
        if(baseline<=0 && !string.IsNullOrWhiteSpace(owner))
        {
            using var h=_db.Connection.CreateCommand();
            h.CommandText=@"SELECT MIN(odometer_km) FROM truck_health_snapshot WHERE truck_id=@truck AND owner_user_id=@owner AND odometer_km>0;";
            Param(h,"@truck",truckId); Param(h,"@owner",owner);
            var first=h.ExecuteScalar();
            if(first is not null && first!=DBNull.Value) baseline=Convert.ToDouble(first);
        }
        if(baseline<=0) baseline=Math.Max(0,currentOdo);
        var hasPolicy=serviceIntervalKm is > 0 && double.IsFinite(serviceIntervalKm.Value);
        double? next=hasPolicy?baseline+serviceIntervalKm!.Value:null;
        double? remaining=next.HasValue?Math.Max(0,next.Value-currentOdo):null;
        bool? overdue=next.HasValue?currentOdo>=next.Value:null;
        list.Add(new(component,Math.Clamp(wear,0,1),at,lastOdo,next,remaining,overdue,
            string.IsNullOrWhiteSpace(wearSource)?"UNKNOWN":wearSource,
            hasPolicy?"TRANSPOLI":"UNAVAILABLE"));
    }
    private static List<string> Aliases(string component)=>component switch
    {
        "engine"=>new(){"engine","motor"},
        "transmission"=>new(){"transmission","transmissao","transmissão","cambio","câmbio"},
        "cabin"=>new(){"cabin","cabine"},
        "chassis"=>new(){"chassis","chassi"},
        "wheels"=>new(){"wheels","rodas","pneus","tires"},
        _=>new(){component.ToLowerInvariant()}
    };
    private static void Param(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
