using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TransPoli.Intelligence.Routes;

public enum RouteConfidence { None, Low, Medium, High }

public sealed record RouteEstimate(
    string Origin,string Destination,string MapFingerprint,double DistanceKm,
    int AcceptedSamples,int RejectedSamples,RouteConfidence Confidence,
    double MinimumKm,double MaximumKm,DateTime? LastObservedAtUtc);

public sealed record RouteObservationResult(bool Accepted,string Reason,RouteEstimate Estimate);

/// <summary>
/// Aprende distâncias exclusivamente das viagens TransPoli concluídas.
/// A mediana protege o catálogo contra desvios ocasionais e rotas anormais.
/// </summary>
public sealed class RouteIntelligenceRepository
{
    private readonly TransPoliDb _db;
    public RouteIntelligenceRepository(TransPoliDb db)=>_db=db;

    public RouteObservationResult Observe(string origin,string destination,double distanceKm,string mapFingerprint="",string? tripId=null,DateTime? observedAtUtc=null)
    {
        origin=Normalize(origin); destination=Normalize(destination); mapFingerprint=(mapFingerprint??"").Trim();
        if(origin.Length==0 || destination.Length==0) throw new ArgumentException("Origem e destino são obrigatórios.");
        if(!double.IsFinite(distanceKm) || distanceKm<=0) throw new ArgumentOutOfRangeException(nameof(distanceKm));
        var before=GetEstimate(origin,destination,mapFingerprint);
        var accepted=true; var reason="";
        if(before.AcceptedSamples>=3 && before.DistanceKm>0)
        {
            var ratio=distanceKm/before.DistanceKm;
            if(ratio<0.65 || ratio>1.35)
            {
                accepted=false;
                reason=$"outlier: {distanceKm:0.0} km fora da faixa robusta da mediana {before.DistanceKm:0.0} km";
            }
        }
        var owner=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(owner)) throw new InvalidOperationException("Route Intelligence requer usuário autenticado.");
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"INSERT OR IGNORE INTO route_observation
(id,owner_user_id,map_fingerprint,origin_city,destination_city,distance_km,observed_at_utc,trip_id,accepted,rejection_reason)
VALUES(@id,@owner,@map,@origin,@destination,@distance,@at,@trip,@accepted,@reason);";
        Add(c,"@id",ObservationId(owner,mapFingerprint,tripId)); Add(c,"@owner",owner); Add(c,"@map",mapFingerprint);
        Add(c,"@origin",origin); Add(c,"@destination",destination); Add(c,"@distance",distanceKm);
        Add(c,"@at",(observedAtUtc??DateTime.UtcNow).ToString("O")); Add(c,"@trip",tripId);
        Add(c,"@accepted",accepted?1:0); Add(c,"@reason",reason); c.ExecuteNonQuery();
        return new RouteObservationResult(accepted,reason,GetEstimate(origin,destination,mapFingerprint));
    }

    public RouteEstimate GetEstimate(string origin,string destination,string mapFingerprint="")
    {
        origin=Normalize(origin); destination=Normalize(destination); mapFingerprint=(mapFingerprint??"").Trim();
        var owner=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(owner)) return Empty(origin,destination,mapFingerprint);
        var accepted=new List<(double Km,DateTime At)>();
        var rejected=0;
        using var c=_db.Connection.CreateCommand();
        c.CommandText=@"SELECT distance_km,observed_at_utc,accepted FROM route_observation
WHERE owner_user_id=@owner AND map_fingerprint=@map AND origin_city=@origin AND destination_city=@destination
ORDER BY observed_at_utc;";
        Add(c,"@owner",owner); Add(c,"@map",mapFingerprint); Add(c,"@origin",origin); Add(c,"@destination",destination);
        using var r=c.ExecuteReader();
        while(r.Read())
        {
            if(r.GetInt32(2)==0){rejected++;continue;}
            var at=DateTime.TryParse(r.GetString(1),null,DateTimeStyles.RoundtripKind,out var d)?d:DateTime.MinValue;
            accepted.Add((r.GetDouble(0),at));
        }
        if(accepted.Count==0) return Empty(origin,destination,mapFingerprint) with { RejectedSamples=rejected };
        var ordered=accepted.Select(x=>x.Km).OrderBy(x=>x).ToArray();
        var mid=ordered.Length/2;
        var median=ordered.Length%2==0?(ordered[mid-1]+ordered[mid])/2d:ordered[mid];
        var confidence=accepted.Count>=5?RouteConfidence.High:accepted.Count>=3?RouteConfidence.Medium:RouteConfidence.Low;
        return new(origin,destination,mapFingerprint,median,accepted.Count,rejected,confidence,ordered[0],ordered[^1],accepted.Max(x=>x.At));
    }

    private static string ObservationId(string owner,string map,string? tripId) => string.IsNullOrWhiteSpace(tripId) ? Guid.NewGuid().ToString("N") : $"trip-route-{owner}-{tripId}";
    private static RouteEstimate Empty(string o,string d,string m)=>new(o,d,m,0,0,0,RouteConfidence.None,0,0,null);
    private static string Normalize(string value)=>string.Join(" ",(value??"").Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    private static void Add(SqliteCommand c,string name,object? value)=>c.Parameters.AddWithValue(name,value??DBNull.Value);
}
