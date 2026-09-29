using System;
using System.Collections.Generic;

namespace TransPoli.Intelligence.World;

public enum WorldSourceKind { Directory, ZipArchive, ScsArchive, Unknown }
public enum CompatibilityState { Unknown, Compatible, Incompatible }
public enum WorldSourceActivation { BaseGame, Active, Installed, Unresolved }

public sealed record WorldSource(
    string Id, string Path, WorldSourceKind Kind, bool IsMod, bool Readable,
    long SizeBytes = 0, DateTime? LastWriteUtc = null, string Note = "",
    WorldSourceActivation Activation = WorldSourceActivation.BaseGame,
    int LoadOrder = -1);

public sealed record WorldDefinition(
    string UnitType, string Id, IReadOnlyDictionary<string,string> Fields,
    string SourceId, string VirtualPath);

public sealed record CityDefinition(string Id,string Name,string CountryId,string SourceId,bool IsMod);
public sealed record CountryDefinition(string Id,string Name,string SourceId,bool IsMod);
public sealed record CompanyDefinition(string Id,string Name,string CityId,string SourceId,bool IsMod);
public sealed record CompanyLocationDefinition(string CompanyId,string CityId,string SourceId,bool IsMod);
public sealed record CompanyCargoFlow(string CompanyId,string CargoId,string Direction,string SourceId,bool IsMod);
public sealed record CargoDefinition(string Id,string Name,double MassKg,IReadOnlyList<string> TrailerRefs,string SourceId,bool IsMod);
public sealed record TrailerDefinition(string Id,string Name,string BodyType,IReadOnlyList<string> CargoRefs,string SourceId,bool IsMod);
public sealed record CargoCompatibility(string CargoId,string TrailerId,string BodyType,CompatibilityState State,string Evidence,string SourceId);

public sealed class WorldCatalog
{
    public int SchemaVersion { get; init; } = 2;
    public string Fingerprint { get; init; } = "";
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
    public string GameRoot { get; init; } = "";
    public List<WorldSource> Sources { get; init; } = new();
    public List<CityDefinition> Cities { get; init; } = new();
    public List<CountryDefinition> Countries { get; init; } = new();
    public List<CompanyDefinition> Companies { get; init; } = new();
    public List<CompanyLocationDefinition> CompanyLocations { get; init; } = new();
    public List<CompanyCargoFlow> CompanyCargoFlows { get; init; } = new();
    public List<CargoDefinition> Cargoes { get; init; } = new();
    public List<TrailerDefinition> Trailers { get; init; } = new();
    public List<CargoCompatibility> CargoCompatibility { get; init; } = new();
    public List<string> Diagnostics { get; init; } = new();
    public int ReadableSourceCount => Sources.FindAll(x=>x.Readable).Count;
    public int ActiveModSourceCount => Sources.FindAll(x=>x.IsMod && x.Readable && x.Activation==WorldSourceActivation.Active).Count;
    public int InstalledModSourceCount => Sources.FindAll(x=>x.IsMod && x.Activation==WorldSourceActivation.Installed).Count;
    public int UnresolvedModSourceCount => Sources.FindAll(x=>x.IsMod && x.Activation==WorldSourceActivation.Unresolved).Count;
    public bool ActiveModLoadoutResolved => UnresolvedModSourceCount==0 &&
        Sources.FindAll(x=>x.IsMod).TrueForAll(x=>x.Activation!=WorldSourceActivation.Installed);
    public int KnownCompatibilityCount => CargoCompatibility.FindAll(x=>x.State==CompatibilityState.Compatible).Count;
    public bool IsPartial => Sources.Exists(x =>
        (!x.Readable && x.Activation!=WorldSourceActivation.Installed) ||
        (x.IsMod && x.Activation==WorldSourceActivation.Unresolved));
    public string DataState => IsPartial ? "PERSISTIDO • PARCIAL" : "PERSISTIDO";
}
