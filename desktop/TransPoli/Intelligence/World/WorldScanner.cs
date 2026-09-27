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
        var sourceFiles=new List<WorldTextFile>();
        var diagnostics=new List<string>();
        foreach(var source in sources.Where(x=>x.Readable))
        {
            IEnumerable<WorldTextFile> files=source.Kind==WorldSourceKind.Directory
                ? _reader.ReadDirectory(source.Id,source.Path)
                : _reader.ReadZip(source.Id,source.Path);
            var materialized=files.ToList();
            var byPath=materialized.ToDictionary(x=>NormalizePath(x.VirtualPath),x=>x,StringComparer.OrdinalIgnoreCase);
            var expanded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in materialized)
                try
                {
                    foreach(var resolved in ExpandIncludes(file,byPath,expanded,diagnostics))
                    {
                        sourceFiles.Add(resolved);
                        definitions.AddRange(_parser.Parse(resolved.Text,resolved.SourceId,resolved.VirtualPath));
                    }
                }
                catch(Exception ex){ diagnostics.Add($"{file.VirtualPath}: {ex.GetType().Name}"); }
        }
        foreach(var source in sources.Where(x=>!x.Readable)) diagnostics.Add($"{Path.GetFileName(source.Path)}: {source.Note}");
        return Build(gameRoot,sources,definitions,sourceFiles,diagnostics);
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

    public static WorldCatalog? LoadCached()
    {
        try
        {
            if(!File.Exists(DefaultCachePath)) return null;
            return JsonSerializer.Deserialize<WorldCatalog>(File.ReadAllText(DefaultCachePath));
        }
        catch { return null; }
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
        var documentsRoots=Ets2DocumentsRoots();
        var activeMods=ReadActiveModOrder(documentsRoots);
        var discoveredMods=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var documentsRoot in documentsRoots)
        {
            var modRoot=Path.Combine(documentsRoot,"mod");
            foreach(var file in SafeFiles(modRoot,"*.scs").Concat(SafeFiles(modRoot,"*.zip")))
                discoveredMods[Path.GetFileName(file)]=file;
        }
        var ordered=new List<string>();
        foreach(var name in activeMods)
            if(discoveredMods.TryGetValue(name,out var file) && !ordered.Contains(file,StringComparer.OrdinalIgnoreCase)) ordered.Add(file);
        foreach(var file in discoveredMods.Values.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
            if(!ordered.Contains(file,StringComparer.OrdinalIgnoreCase)) ordered.Add(file);
        foreach(var file in ordered)
        {
            var zip=WorldSourceReader.IsZip(file);
            var active=activeMods.Contains(Path.GetFileName(file),StringComparer.OrdinalIgnoreCase);
            result.Add(Make("mod-"+Path.GetFileName(file),file,zip?WorldSourceKind.ZipArchive:WorldSourceKind.ScsArchive,true,zip,
                (active?"Active ":"Discovered ")+(zip?"ZIP mod":"SCS/HashFS mod not readable")));
        }
        return result.ToArray();
    }

    private WorldCatalog Build(string root,IReadOnlyList<WorldSource> sources,IReadOnlyList<WorldDefinition> defs,IReadOnlyList<WorldTextFile> sourceFiles,List<string> diagnostics)
    {
        bool Mod(WorldDefinition d)=>sources.FirstOrDefault(x=>x.Id==d.SourceId)?.IsMod==true;
        List<T> OverrideById<T>(IEnumerable<T> items,Func<T,string> key) =>
            items.GroupBy(key,StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();

        var cities=OverrideById(defs.Where(d=>d.UnitType.Contains("city",StringComparison.OrdinalIgnoreCase))
            .Select(d=>new CityDefinition(d.Id,Name(d),SiiDefinitionParser.Value(d,"country","country_id"),d.SourceId,Mod(d))),x=>x.Id);
        var countries=OverrideById(defs.Where(d=>d.UnitType.Contains("country",StringComparison.OrdinalIgnoreCase))
            .Select(d=>new CountryDefinition(d.Id,Name(d),d.SourceId,Mod(d))),x=>x.Id);
        var companies=OverrideById(defs.Where(d=>d.UnitType.Contains("company",StringComparison.OrdinalIgnoreCase))
            .Select(d=>new CompanyDefinition(d.Id,Name(d),SiiDefinitionParser.Value(d,"city","city_id"),d.SourceId,Mod(d))),x=>x.Id);
        var cargoes=OverrideById(defs.Where(d=>d.UnitType.Contains("cargo",StringComparison.OrdinalIgnoreCase))
            .Select(d=>new CargoDefinition(d.Id,Name(d),Number(d,"mass","mass_kg"),SiiDefinitionParser.ArrayValues(d,"trailers","trailer","body_types"),d.SourceId,Mod(d))),x=>x.Id);
        var trailers=OverrideById(defs.Where(d=>d.UnitType.Contains("trailer",StringComparison.OrdinalIgnoreCase))
            .Select(d=>new TrailerDefinition(d.Id,Name(d),SiiDefinitionParser.Value(d,"body_type","body","body_type_name"),SiiDefinitionParser.ArrayValues(d,"cargo","cargoes","allowed_cargo"),d.SourceId,Mod(d))),x=>x.Id);

        var locations=new List<CompanyLocationDefinition>();
        var flows=new List<CompanyCargoFlow>();
        var compat=new List<CargoCompatibility>();
        foreach(var file in sourceFiles)
        {
            var p=file.VirtualPath.Replace('\\','/').TrimStart('/');
            var parts=p.Split('/',StringSplitOptions.RemoveEmptyEntries);
            var sourceId=file.SourceId;
            var sourceIsMod=sources.FirstOrDefault(x=>x.Id==sourceId)?.IsMod==true;
            if(parts.Length>=5 && parts[0].Equals("def",StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("company",StringComparison.OrdinalIgnoreCase))
            {
                var company=parts[2];
                if(parts[3].Equals("editor",StringComparison.OrdinalIgnoreCase))
                    locations.Add(new(company,Path.GetFileNameWithoutExtension(parts[^1]),sourceId,sourceIsMod));
                else if(parts[3].Equals("in",StringComparison.OrdinalIgnoreCase) || parts[3].Equals("out",StringComparison.OrdinalIgnoreCase))
                    flows.Add(new(company,Path.GetFileNameWithoutExtension(parts[^1]),parts[3].ToUpperInvariant(),sourceId,sourceIsMod));
            }
            if(parts.Length>=4 && parts[0].Equals("def",StringComparison.OrdinalIgnoreCase) &&
               parts[1].Equals("cargo",StringComparison.OrdinalIgnoreCase))
            {
                var cargo=parts[2];
                var trailer=Path.GetFileNameWithoutExtension(parts[^1]);
                if(!string.IsNullOrWhiteSpace(cargo) && !string.IsNullOrWhiteSpace(trailer))
                    compat.Add(new(cargo,trailer,"",CompatibilityState.Compatible,"def/cargo/<cargo>/<trailer>.sii",sourceId));
            }
        }
        foreach(var c in cargoes) foreach(var tr in c.TrailerRefs)
            compat.Add(new(c.Id,tr,"",CompatibilityState.Compatible,"cargo definition reference",c.SourceId));
        foreach(var t in trailers) foreach(var c in t.CargoRefs)
            compat.Add(new(c,t.Id,t.BodyType,CompatibilityState.Compatible,"trailer definition reference",t.SourceId));

        locations=locations.GroupBy(x=>$"{x.CompanyId}|{x.CityId}",StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();
        flows=flows.GroupBy(x=>$"{x.CompanyId}|{x.CargoId}|{x.Direction}",StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();
        compat=compat.GroupBy(x=>$"{x.CargoId}|{x.TrailerId}|{x.BodyType}",StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();
        diagnostics.Add($"Definitions={defs.Count}; Cities={cities.Count}; Companies={companies.Count}; CompanyLocations={locations.Count}; CargoFlows={flows.Count}; Cargoes={cargoes.Count}; Trailers={trailers.Count}; Compatibility={compat.Count}");
        return new WorldCatalog{GameRoot=root,Fingerprint=Fingerprint(sources),Sources=sources.ToList(),Cities=cities,Countries=countries,Companies=companies,CompanyLocations=locations,CompanyCargoFlows=flows,Cargoes=cargoes,Trailers=trailers,CargoCompatibility=compat,Diagnostics=diagnostics};
    }

    private static IEnumerable<WorldTextFile> ExpandIncludes(WorldTextFile file,IReadOnlyDictionary<string,WorldTextFile> files,HashSet<string> expanded,List<string> diagnostics)
    {
        var key=file.SourceId+"|"+NormalizePath(file.VirtualPath);
        if(!expanded.Add(key)) yield break;
        yield return file;
        foreach(var include in SiiDefinitionParser.Includes(file.Text))
        {
            var resolved=ResolveIncludePath(file.VirtualPath,include);
            if(files.TryGetValue(resolved,out var child))
            {
                foreach(var nested in ExpandIncludes(child,files,expanded,diagnostics)) yield return nested;
            }
            else diagnostics.Add($"{file.VirtualPath}: include not found: {include}");
        }
    }

    private static string ResolveIncludePath(string parent,string include)
    {
        var inc=NormalizePath(include).TrimStart('/');
        if(include.StartsWith("/",StringComparison.Ordinal) || inc.StartsWith("def/",StringComparison.OrdinalIgnoreCase)) return inc;
        var dir=Path.GetDirectoryName(NormalizePath(parent))?.Replace('\\','/')??"";
        var parts=(dir+"/"+inc).Split('/',StringSplitOptions.RemoveEmptyEntries);
        var stack=new List<string>();
        foreach(var part in parts){if(part==".")continue;if(part==".."){if(stack.Count>0)stack.RemoveAt(stack.Count-1);}else stack.Add(part);}
        return string.Join("/",stack);
    }
    private static string NormalizePath(string value)=>value.Replace('\\','/').Trim();

    private static IReadOnlyList<string> Ets2DocumentsRoots()
    {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string path){if(Directory.Exists(path)) roots.Add(path);}
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Euro Truck Simulator 2"));
        var oneDrive=Environment.GetEnvironmentVariable("OneDrive");
        if(!string.IsNullOrWhiteSpace(oneDrive)) Add(Path.Combine(oneDrive,"Documents","Euro Truck Simulator 2"));
        return roots.ToArray();
    }

    private static IReadOnlyList<string> ReadActiveModOrder(IReadOnlyList<string> roots)
    {
        var candidates=new List<string>();
        foreach(var root in roots)
            foreach(var profiles in new[]{"profiles","steam_profiles"})
            {
                var dir=Path.Combine(root,profiles);
                if(!Directory.Exists(dir)) continue;
                try { candidates.AddRange(Directory.EnumerateFiles(dir,"mod_settings.sii",SearchOption.AllDirectories)); } catch { }
            }
        var latest=candidates.OrderByDescending(x=>{try{return File.GetLastWriteTimeUtc(x);}catch{return DateTime.MinValue;}}).FirstOrDefault();
        if(string.IsNullOrWhiteSpace(latest)) return Array.Empty<string>();
        try
        {
            var text=File.ReadAllText(latest);
            var names=new List<string>();
            foreach(var line in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed=line.Trim();
                if(!trimmed.Contains("active_mod",StringComparison.OrdinalIgnoreCase)) continue;
                var q=trimmed.Split('"');
                var raw=q.Length>=2?q[^2]:trimmed[(trimmed.IndexOf(':')+1)..].Trim();
                raw=raw.Replace("mod_package.","",StringComparison.OrdinalIgnoreCase).Trim();
                if(raw.Length>0) names.Add(raw.EndsWith(".scs",StringComparison.OrdinalIgnoreCase)||raw.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)?raw:raw+".scs");
            }
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private static string Name(WorldDefinition d)=>SiiDefinitionParser.Value(d,"name","name_localized","display_name","brand_name");
    private static double Number(WorldDefinition d,params string[] keys)=>double.TryParse(SiiDefinitionParser.Value(d,keys),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;
    private static IEnumerable<string> SafeFiles(string root,string pattern){ try{return Directory.Exists(root)?Directory.EnumerateFiles(root,pattern,SearchOption.TopDirectoryOnly).ToArray():Array.Empty<string>();}catch{return Array.Empty<string>();}}
    private static WorldSource Make(string id,string path,WorldSourceKind kind,bool mod,bool readable,string note){var f=new FileInfo(path);return new(id,path,kind,mod,readable,f.Exists?f.Length:0,f.Exists?f.LastWriteTimeUtc:null,note);}
    private static string Fingerprint(IReadOnlyList<WorldSource> sources){using var sha=SHA256.Create();var raw=string.Join("\n",sources.Select(x=>$"{x.Path}|{x.SizeBytes}|{x.LastWriteUtc:O}|{x.Readable}"));return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();}
}
