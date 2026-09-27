using System;
using System.Collections.Generic;
using System.Linq;
using TransPoli.Intelligence.Routes;
using TransPoli.Intelligence.World;

namespace TransPoli;

internal sealed record CargoIntelligenceCandidate(string CargoId,string CargoName,string CompanyId,string OriginCity,string DestinationCity,string TrailerId,string BodyType,CompatibilityState Compatibility,double KnownDistanceKm,RouteConfidence RouteConfidence,string Evidence);

/// <summary>Consulta local do mercado conhecido. Nao altera ofertas do ETS2 e nao calcula dinheiro.</summary>
internal sealed class CargoMarketIntelligence
{
    private readonly RouteIntelligenceRepository _routes;
    public CargoMarketIntelligence(RouteIntelligenceRepository routes)=>_routes=routes;

    public IReadOnlyList<CargoIntelligenceCandidate> Find(WorldCatalog world,string originCity,string destinationCity,string? trailerId=null)
    {
        if(world is null) return Array.Empty<CargoIntelligenceCandidate>();
        var origin=Key(originCity); var destination=Key(destinationCity);
        if(origin.Length==0||destination.Length==0) return Array.Empty<CargoIntelligenceCandidate>();
        var companies=world.CompanyLocations.Where(x=>Key(x.CityId)==origin).Select(x=>x.CompanyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flows=world.CompanyCargoFlows.Where(x=>companies.Contains(x.CompanyId)&&string.Equals(x.Direction,"OUT",StringComparison.OrdinalIgnoreCase));
        var route=_routes.GetEstimate(originCity,destinationCity,world.Fingerprint);
        var result=new List<CargoIntelligenceCandidate>();
        foreach(var flow in flows)
        {
            var cargo=world.Cargoes.FirstOrDefault(x=>string.Equals(x.Id,flow.CargoId,StringComparison.OrdinalIgnoreCase));
            if(cargo is null) continue;
            var compatible=world.CargoCompatibility.Where(x=>string.Equals(x.CargoId,cargo.Id,StringComparison.OrdinalIgnoreCase));
            if(!string.IsNullOrWhiteSpace(trailerId)) compatible=compatible.Where(x=>string.Equals(x.TrailerId,trailerId,StringComparison.OrdinalIgnoreCase));
            foreach(var link in compatible)
                result.Add(new(cargo.Id,string.IsNullOrWhiteSpace(cargo.Name)?cargo.Id:cargo.Name,flow.CompanyId,originCity,destinationCity,link.TrailerId,link.BodyType,link.State,route.DistanceKm,route.Confidence,link.Evidence));
        }
        return result.GroupBy(x=>$"{x.CompanyId}|{x.CargoId}|{x.TrailerId}",StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderByDescending(x=>x.RouteConfidence).ThenBy(x=>x.CargoName,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public CompatibilityState Compatibility(WorldCatalog world,string cargoId,string trailerId)
    {
        if(world is null) return CompatibilityState.Unknown;
        return world.CargoCompatibility.FirstOrDefault(x=>string.Equals(x.CargoId,cargoId,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.TrailerId,trailerId,StringComparison.OrdinalIgnoreCase))?.State??CompatibilityState.Unknown;
    }
    private static string Key(string value)=>string.Join(" ",(value??"").Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
