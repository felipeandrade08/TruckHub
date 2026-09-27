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

    public IReadOnlyList<CargoIntelligenceCandidate> Find(WorldCatalog world,string originCity,string destinationCity,string? trailerId=null,string? bodyType=null)
    {
        if(world is null) return Array.Empty<CargoIntelligenceCandidate>();
        var origin=ResolveCityId(world,originCity); var destination=ResolveCityId(world,destinationCity);
        if(origin.Length==0||destination.Length==0) return Array.Empty<CargoIntelligenceCandidate>();
        var companies=world.CompanyLocations.Where(x=>SameId(x.CityId,origin)).Select(x=>CanonicalId(x.CompanyId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flows=world.CompanyCargoFlows.Where(x=>companies.Contains(CanonicalId(x.CompanyId))&&string.Equals(x.Direction,"OUT",StringComparison.OrdinalIgnoreCase));
        var route=_routes.GetEstimate(origin,destination,world.Fingerprint);
        if(route.AcceptedSamples==0 && (!SameId(origin,originCity)||!SameId(destination,destinationCity)))
            route=_routes.GetEstimate(originCity,destinationCity,world.Fingerprint);
        var result=new List<CargoIntelligenceCandidate>();
        foreach(var flow in flows)
        {
            var cargo=world.Cargoes.FirstOrDefault(x=>SameId(x.Id,flow.CargoId));
            if(cargo is null) continue;
            var compatible=world.CargoCompatibility.Where(x=>SameId(x.CargoId,cargo.Id));
            if(!string.IsNullOrWhiteSpace(trailerId) || !string.IsNullOrWhiteSpace(bodyType))
            {
                var byTrailer=compatible.Where(x=>!string.IsNullOrWhiteSpace(trailerId) && SameId(x.TrailerId,trailerId));
                var byBody=compatible.Where(x=>!string.IsNullOrWhiteSpace(bodyType) && SameId(x.BodyType,bodyType));
                compatible=byTrailer.Concat(byBody).Distinct();
            }
            foreach(var link in compatible)
                result.Add(new(cargo.Id,string.IsNullOrWhiteSpace(cargo.Name)?cargo.Id:cargo.Name,flow.CompanyId,origin,destination,link.TrailerId,link.BodyType,link.State,route.DistanceKm,route.Confidence,link.Evidence));
        }
        return result.GroupBy(x=>$"{x.CompanyId}|{x.CargoId}|{x.TrailerId}|{x.BodyType}",StringComparer.OrdinalIgnoreCase)
            .Select(g=>g.OrderByDescending(x=>EvidenceStrength(x.Evidence)).First())
            .OrderByDescending(x=>x.Compatibility==CompatibilityState.Compatible)
            .ThenByDescending(x=>x.RouteConfidence)
            .ThenByDescending(x=>EvidenceStrength(x.Evidence))
            .ThenBy(x=>x.CargoName,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public CompatibilityState Compatibility(WorldCatalog world,string cargoId,string trailerId)
    {
        if(world is null) return CompatibilityState.Unknown;
        return world.CargoCompatibility.FirstOrDefault(x=>SameId(x.CargoId,cargoId)&&SameId(x.TrailerId,trailerId))?.State??CompatibilityState.Unknown;
    }

    public CompatibilityState Compatibility(WorldCatalog world,string cargoId,string trailerId,string? bodyType)
    {
        var direct=Compatibility(world,cargoId,trailerId);
        if(direct!=CompatibilityState.Unknown) return direct;
        if(string.IsNullOrWhiteSpace(bodyType)) return CompatibilityState.Unknown;
        var cargo=world.Cargoes.FirstOrDefault(x=>SameId(x.Id,cargoId));
        if(cargo is null) return CompatibilityState.Unknown;
        var body=CanonicalId(bodyType);
        if(cargo.TrailerRefs.Any(x=>SameId(x,bodyType))) return CompatibilityState.Compatible;
        return world.CargoCompatibility.Any(x=>SameId(x.CargoId,cargo.Id) && CanonicalId(x.BodyType)==body && x.State==CompatibilityState.Compatible)
            ? CompatibilityState.Compatible
            : CompatibilityState.Unknown;
    }
    internal static string ResolveCityId(WorldCatalog world,string value)
    {
        var raw=CanonicalId(value);
        if(raw.Length==0) return "";
        var city=world.Cities.FirstOrDefault(x=>SameId(x.Id,raw))
            ?? world.Cities.FirstOrDefault(x=>Key(x.Name)==Key(value));
        return city is null?raw:CanonicalId(city.Id);
    }
    internal static string CanonicalId(string value)
    {
        var raw=(value??"").Trim();
        foreach(var prefix in new[]{"city.","city:","company.","company:","cargo.","cargo:","trailer.","trailer:"})
            if(raw.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){raw=raw[prefix.Length..];break;}
        return Key(raw);
    }
    internal static int EvidenceStrength(string evidence)
    {
        if(string.IsNullOrWhiteSpace(evidence)) return 0;
        if(evidence.Contains("definition reference",StringComparison.OrdinalIgnoreCase)) return 3;
        if(evidence.Contains("def/cargo/",StringComparison.OrdinalIgnoreCase)) return 2;
        return 1;
    }
    private static bool SameId(string a,string b)=>CanonicalId(a)==CanonicalId(b);
    private static string Key(string value)=>string.Join(" ",(value??"").Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
