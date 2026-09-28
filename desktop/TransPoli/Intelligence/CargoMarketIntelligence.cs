using System;
using System.Collections.Generic;
using System.Linq;
using TransPoli.Intelligence.Routes;
using TransPoli.Intelligence.World;

namespace TransPoli;

internal sealed record CargoIntelligenceCandidate(string CargoId,string CargoName,string CompanyId,string OriginCity,string DestinationCity,string TrailerId,string BodyType,CompatibilityState Compatibility,double KnownDistanceKm,RouteConfidence RouteConfidence,string Evidence);

internal sealed record CargoOperationIntelligence(
    string OriginCityId,string OriginCompanyId,string CargoId,string DestinationCityId,string DestinationCompanyId,
    string TrailerId,string BodyType,CompatibilityState Compatibility,double KnownDistanceKm,RouteConfidence RouteConfidence,
    int RouteSamples,string Evidence);

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

    public CargoOperationIntelligence ResolveOperation(
        WorldCatalog world,string originCity,string? originCompany,string cargo,string destinationCity,string? destinationCompany,
        string? trailerId,string? bodyType)
    {
        if(world is null) return new("","","","","",CanonicalId(trailerId??""),CanonicalId(bodyType??""),CompatibilityState.Unknown,0,RouteConfidence.Unknown,0,"world catalog unavailable");

        var origin=ResolveCityId(world,originCity);
        var destination=ResolveCityId(world,destinationCity);
        var sourceCompany=ResolveCompanyId(world,originCompany??"",origin);
        var targetCompany=ResolveCompanyId(world,destinationCompany??"",destination);
        var cargoId=ResolveCargoId(world,cargo);
        var resolvedTrailer=ResolveTrailerId(world,trailerId??"",bodyType);
        var resolvedBody=ResolveBodyType(world,resolvedTrailer,bodyType);
        var compatibility=cargoId.Length==0
            ? CompatibilityState.Unknown
            : Compatibility(world,cargoId,resolvedTrailer,resolvedBody);

        var route=_routes.GetEstimate(origin,destination,world.Fingerprint);
        if(route.AcceptedSamples==0 && (!SameId(origin,originCity)||!SameId(destination,destinationCity)))
            route=_routes.GetEstimate(originCity,destinationCity,world.Fingerprint);

        var evidence=new List<string>();
        if(world.Cities.Any(x=>SameId(x.Id,origin))) evidence.Add("origin:WORLD_DEF");
        if(sourceCompany.Length>0 && world.Companies.Any(x=>SameId(x.Id,sourceCompany))) evidence.Add("origin_company:WORLD_DEF");
        if(cargoId.Length>0 && world.Cargoes.Any(x=>SameId(x.Id,cargoId))) evidence.Add("cargo:WORLD_DEF");
        if(resolvedTrailer.Length>0 && world.Trailers.Any(x=>SameId(x.Id,resolvedTrailer))) evidence.Add("trailer:WORLD_DEF");
        if(world.Cities.Any(x=>SameId(x.Id,destination))) evidence.Add("destination:WORLD_DEF");
        if(targetCompany.Length>0 && world.Companies.Any(x=>SameId(x.Id,targetCompany))) evidence.Add("destination_company:WORLD_DEF");
        if(route.AcceptedSamples>0) evidence.Add($"route:TRANSPOLI/{route.AcceptedSamples}");

        return new(origin,sourceCompany,cargoId,destination,targetCompany,resolvedTrailer,resolvedBody,compatibility,
            route.DistanceKm,route.Confidence,route.AcceptedSamples,string.Join(" • ",evidence));
    }

    internal static string ResolveCompanyId(WorldCatalog world,string value,string? cityId=null)
    {
        var raw=CanonicalId(value);
        if(raw.Length==0) return "";
        var matches=world.Companies.Where(x=>SameId(x.Id,raw)||Key(x.Name)==Key(value)).ToArray();
        if(!string.IsNullOrWhiteSpace(cityId))
        {
            var located=matches.FirstOrDefault(x=>world.CompanyLocations.Any(l=>SameId(l.CompanyId,x.Id)&&SameId(l.CityId,cityId)));
            if(located is not null) return CanonicalId(located.Id);
        }
        return matches.Length==1?CanonicalId(matches[0].Id):raw;
    }

    internal static string ResolveCargoId(WorldCatalog world,string value)
    {
        var raw=CanonicalId(value);
        if(raw.Length==0) return "";
        var cargo=world.Cargoes.FirstOrDefault(x=>SameId(x.Id,raw))
            ?? world.Cargoes.FirstOrDefault(x=>Key(x.Name)==Key(value));
        return cargo is null?raw:CanonicalId(cargo.Id);
    }

    internal static string ResolveTrailerId(WorldCatalog world,string value,string? bodyType=null)
    {
        var raw=CanonicalId(value);
        if(raw.Length>0)
        {
            var trailer=world.Trailers.FirstOrDefault(x=>SameId(x.Id,raw))
                ?? world.Trailers.FirstOrDefault(x=>Key(x.Name)==Key(value));
            if(trailer is not null) return CanonicalId(trailer.Id);
        }
        if(!string.IsNullOrWhiteSpace(bodyType))
        {
            var matches=world.Trailers.Where(x=>SameId(x.BodyType,bodyType)).ToArray();
            if(matches.Length==1) return CanonicalId(matches[0].Id);
        }
        return raw;
    }

    internal static string ResolveBodyType(WorldCatalog world,string trailerId,string? bodyType)
    {
        if(!string.IsNullOrWhiteSpace(bodyType)) return CanonicalId(bodyType);
        var trailer=world.Trailers.FirstOrDefault(x=>SameId(x.Id,trailerId));
        return trailer is null?"":CanonicalId(trailer.BodyType);
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
