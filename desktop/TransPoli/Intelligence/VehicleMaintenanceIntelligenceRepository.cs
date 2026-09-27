using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace TransPoli;

internal sealed record MaintenanceComponentState(string Component,double Wear,DateTime? LastServiceAtUtc,double LastServiceOdometerKm,double NextServiceOdometerKm,double RemainingKm,bool Overdue);
internal sealed record VehicleMaintenanceIntelligence(string TruckId,double CurrentOdometerKm,IReadOnlyList<MaintenanceComponentState> Components);

/// <summary>
/// Projecao de manutencao sobre snapshots e historico existentes.
/// Intervalos sao politica TransPoli; custos permanecem no repositorio financeiro atual.
/// </summary>
internal sealed class VehicleMaintenanceIntelligenceRepository
{
    private readonly TransPoliDb _db;
    private const double DefaultIntervalKm=30000d;
    public VehicleMaintenanceIntelligenceRepository(TransPoliDb db)=>_db=db;

    public VehicleMaintenanceIntelligence Read(string truckId,double currentOdometerKm,double engine,double transmission,double cabin,double chassis,double wheels)
    {
        var components=new List<MaintenanceComponentState>();
        Add(components,truckId,"engine",engine,currentOdometerKm);
        Add(components,truckId,"transmission",transmission,currentOdometerKm);
        Add(components,truckId,"cabin",cabin,currentOdometerKm);
        Add(components,truckId,"chassis",chassis,currentOdometerKm);
        Add(components,truckId,"wheels",wheels,currentOdometerKm);
        return new(truckId,currentOdometerKm,components);
    }

    private void Add(List<MaintenanceComponentState> list,string truckId,string component,double wear,double currentOdo)
    {
        var owner=SecureTokenStore.ReadUserId();
        DateTime? at=null; double lastOdo=0;
        if(!string.IsNullOrWhiteSpace(owner))
        {
            using var c=_db.Connection.CreateCommand();
            var aliases=Aliases(component);
            c.CommandText=@"SELECT recorded_at_utc,odometer_km FROM maintenance
WHERE truck_id=@truck AND owner_user_id=@owner
AND lower(trim(component)) IN ("+string.Join(",",aliases.ConvertAll((_,i)=>"@component"+i))+@")
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
        var next=baseline+DefaultIntervalKm;
        var remaining=Math.Max(0,next-currentOdo);
        list.Add(new(component,Math.Clamp(wear,0,1),at,lastOdo,next,remaining,currentOdo>=next));
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
