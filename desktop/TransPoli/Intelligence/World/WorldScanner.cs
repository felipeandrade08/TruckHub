using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransPoli.Intelligence.World;

/// <summary>
/// Catálogo local, somente leitura. Nunca altera arquivos do ETS2 nem do mercado.
/// </summary>
public sealed class WorldScanner
{
    private readonly SiiDefinitionParser _parser=new();
    private readonly WorldSourceReader _reader=new();

    public WorldCatalog Scan(string gameRoot)
    {
        if(string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot)) throw new DirectoryNotFoundException(gameRoot);
        var sources=DiscoverSources(gameRoot);
        var definitions=new List<WorldDefinition>();
        var diagnostics=new List<string>();
        foreach(var source in sources.Where(x=>x.Readable))
        {
            IEnumerable<WorldTextFile> files=source.Kind==WorldSourceKind.Directory
                ? _reader.ReadDirectory(source.Id,source.Path)
                : _reader.ReadZip(source.Id,source.Path);
            foreach(var file in files)
                try { definitions.AddRange(_parser.Parse(file.Text,file.SourceId,file.VirtualPath)); }
                catch(Exception ex){ diagnostics.Add($"{file.VirtualPath}: {ex.GetType().Name}"); }
        }
        foreach(var source in sources.Where(x=>!x.Readable)) diagnostics.Add($"{Path.GetFileName(source.Path)}: {source.Note}");
        return Build(gameRoot,sources,definitions,diagnostics);
    }

    public WorldCatalog LoadOrScan(string gameRoot,string? cachePath=null)
    {
        cachePath ??=DefaultCachePath;
        var fingerprint=Fingerprint(DiscoverSources(gameRoot));
        try
        {
            if(File.Exists(cachePath))
            {
                var cached=JsonSerializer.Deserialize<WorldCatalog>(File.ReadAllText(cachePath));
                if(cached is not null && cached.SchemaVersion==1 && cached.Fingerprint==fingerprint) return cached;
            }
        } catch { }
        var catalog=Scan(gameRoot);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath,JsonSerializer.Serialize(catalog,new JsonSerializerOptions{WriteIndented=true}));
        } catch { }
        return catalog;
    }

    public static string DefaultCachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransPoli","Intelligence","world-catalog-v1.json");

    public static IReadOnlyList<WorldSource> DiscoverSources(string gameRoot)
    {
        var result=new List<WorldSource>();
        if(Directory.Exists(Path.Combine(gameRoot,"def"))) result.Add(Make("game-directory",gameRoot,WorldSourceKind.Directory,false,true,"Expanded DEF directory"));
        foreach(var file in SafeFiles(gameRoot,"*.scs"))
        {
            var zip=WorldSourceReader.IsZip(file);
            result.Add(Make("game-"+Path.GetFileName(file),file,zip?WorldSourceKind.ZipArchive:WorldSourceKind.ScsArchive,false,zip,zip?"ZIP-compatible SCS":"SCS/HashFS: não disponível de maneira estável sem leitor de arquivo compatível"));
        }
        var modRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Euro Truck Simulator 2","mod");
        foreach(var file in SafeFiles(modRoot,"*.scs").Concat(SafeFiles(modRoot,"*.zip")))
        {
            var zip=WorldSourceReader.IsZip(file);
            result.Add(Make("mod-"+Path.GetFileName(file),file,zip?WorldSourceKind.ZipArchive:WorldSourceKind.ScsArchive,true,zip,zip?"Mod ZIP legível":"Mod SCS/HashFS não lido"));
        }
        return result.OrderBy(x=>x.IsMod).ThenBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private WorldCatalog Build(string root,IReadOnlyList<WorldSource> sources,IReadOnlyList<WorldDefinition> defs,List<string> diagnostics)
    {
        bool Mod(WorldDefinition d)=>sources.FirstOrDefault(x=>x.Id==d.SourceId)?.IsMod==true;
        var cities=defs.Where(d=>d.UnitType.Contains("city",StringComparison.OrdinalIgnoreCase)).Select(d=>new CityDefinition(d.Id,Name(d),SiiDefinitionParser.Value(d,"country","country_id"),d.SourceId,Mod(d))).DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToList();
        var countries=defs.Where(d=>d.UnitType.Contains("country",StringComparison.OrdinalIgnoreCase)).Select(d=>new CountryDefinition(d.Id,Name(d),d.SourceId,Mod(d))).DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToList();
        var companies=defs.Where(d=>d.UnitType.Contains("company",StringComparison.OrdinalIgnoreCase)).Select(d=>new CompanyDefinition(d.Id,Name(d),SiiDefinitionParser.Value(d,"city","city_id"),d.SourceId,Mod(d))).DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToList();
        var cargoes=defs.Where(d=>d.UnitType.Contains("cargo",StringComparison.OrdinalIgnoreCase)).Select(d=>new CargoDefinition(d.Id,Name(d),Number(d,"mass","mass_kg"),SiiDefinitionParser.ArrayValues(d,"trailers","trailer","body_types"),d.SourceId,Mod(d))).DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToList();
        var trailers=defs.Where(d=>d.UnitType.Contains("trailer",StringComparison.OrdinalIgnoreCase)).Select(d=>new TrailerDefinition(d.Id,Name(d),SiiDefinitionParser.Value(d,"body_type","body","body_type_name"),SiiDefinitionParser.ArrayValues(d,"cargo","cargoes","allowed_cargo"),d.SourceId,Mod(d))).DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).ToList();
        var compat=new List<CargoCompatibility>();
        foreach(var c in cargoes)
            foreach(var tr in c.TrailerRefs)
                compat.Add(new(c.Id,tr,"",CompatibilityState.Compatible,"cargo definition reference",c.SourceId));
        foreach(var t in trailers)
            foreach(var c in t.CargoRefs)
                compat.Add(new(c,t.Id,t.BodyType,CompatibilityState.Compatible,"trailer definition reference",t.SourceId));
        compat=compat.DistinctBy(x=>$"{x.CargoId}|{x.TrailerId}|{x.BodyType}",StringComparer.OrdinalIgnoreCase).ToList();
        diagnostics.Add($"Definitions={defs.Count}; Cities={cities.Count}; Companies={companies.Count}; Cargoes={cargoes.Count}; Trailers={trailers.Count}; Compatibility={compat.Count}");
        return new WorldCatalog{GameRoot=root,Fingerprint=Fingerprint(sources),Sources=sources.ToList(),Cities=cities,Countries=countries,Companies=companies,Cargoes=cargoes,Trailers=trailers,CargoCompatibility=compat,Diagnostics=diagnostics};
    }

    private static string Name(WorldDefinition d)=>SiiDefinitionParser.Value(d,"name","name_localized","display_name","brand_name");
    private static double Number(WorldDefinition d,params string[] keys)=>double.TryParse(SiiDefinitionParser.Value(d,keys),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;
    private static IEnumerable<string> SafeFiles(string root,string pattern){ try{return Directory.Exists(root)?Directory.EnumerateFiles(root,pattern,SearchOption.TopDirectoryOnly).ToArray():Array.Empty<string>();}catch{return Array.Empty<string>();}}
    private static WorldSource Make(string id,string path,WorldSourceKind kind,bool mod,bool readable,string note){var f=new FileInfo(path);return new(id,path,kind,mod,readable,f.Exists?f.Length:0,f.Exists?f.LastWriteTimeUtc:null,note);}
    private static string Fingerprint(IReadOnlyList<WorldSource> sources){using var sha=SHA256.Create();var raw=string.Join("\n",sources.Select(x=>$"{x.Path}|{x.SizeBytes}|{x.LastWriteUtc:O}|{x.Readable}"));return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();}
}
