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
        foreach(var source in sources.Where(x=>x.Readable && x.Activation!=WorldSourceActivation.Installed))
        {
            IEnumerable<WorldTextFile> files=source.Kind==WorldSourceKind.Directory
                ? _reader.ReadDirectory(source.Id,source.Path)
                : _reader.ReadZip(source.Id,source.Path);
            var materialized=files.ToList();
            var byPath=materialized.ToDictionary(x=>NormalizePath(x.VirtualPath),x=>x,StringComparer.OrdinalIgnoreCase);
            foreach(var file in materialized)
                try
                {
                    sourceFiles.Add(file);
                    var expandedText=ExpandIncludeText(file,byPath,new HashSet<string>(StringComparer.OrdinalIgnoreCase),diagnostics,0);
                    definitions.AddRange(_parser.Parse(expandedText,file.SourceId,file.VirtualPath));
                }
                catch(Exception ex){ diagnostics.Add($"{file.VirtualPath}: {ex.GetType().Name}"); }
        }
        foreach(var source in sources.Where(x=>!x.Readable && x.Activation!=WorldSourceActivation.Installed))
            diagnostics.Add($"{(string.IsNullOrWhiteSpace(source.Path)?source.Id:Path.GetFileName(source.Path))}: {source.Note}");
        var unreadable=sources.Count(x=>!x.Readable && x.Activation!=WorldSourceActivation.Installed);
        var unresolvedMods=sources.Count(x=>x.IsMod && x.Activation==WorldSourceActivation.Unresolved);
        var installedOnly=sources.Count(x=>x.IsMod && x.Activation==WorldSourceActivation.Installed);
        if(unreadable>0) diagnostics.Add($"World sources unreadable={unreadable}; catálogo pode ser parcial.");
        if(unresolvedMods>0) diagnostics.Add($"Mod loadout unresolved={unresolvedMods}; catálogo usa descoberta conservadora e não afirma prioridade.");
        if(installedOnly>0) diagnostics.Add($"Mods installed but not proven active={installedOnly}; não participam de overrides ativos.");
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
                if(cached is not null && cached.SchemaVersion==2 && cached.Fingerprint==fingerprint) return cached;
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

    public static string DefaultCachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransPoli","Intelligence","world-catalog-v2.json");

    public static IReadOnlyList<WorldSource> DiscoverSources(string gameRoot)
    {
        var result=new List<WorldSource>();
        if(Directory.Exists(Path.Combine(gameRoot,"def"))) result.Add(Make("game-directory",gameRoot,WorldSourceKind.Directory,false,true,"Expanded DEF directory",WorldSourceActivation.BaseGame,0));
        foreach(var file in SafeFiles(gameRoot,"*.scs"))
        {
            var zip=WorldSourceReader.IsZip(file);
            result.Add(Make("game-"+Path.GetFileName(file),file,zip?WorldSourceKind.ZipArchive:WorldSourceKind.ScsArchive,false,zip,zip?"ZIP-compatible SCS":"SCS/HashFS: não disponível de maneira estável sem leitor de arquivo compatível",WorldSourceActivation.BaseGame,result.Count));
        }
        var documentsRoots=Ets2DocumentsRoots();
        var activeLoadout=ReadActiveModOrder(documentsRoots);
        var activeMods=activeLoadout.ModIds;
        var discoveredMods=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        void RegisterMod(string alias,string file)
        {
            if(string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(file)) return;
            discoveredMods.TryAdd(alias,file);
        }
        foreach(var documentsRoot in documentsRoots)
        {
            var modRoot=Path.Combine(documentsRoot,"mod");
            foreach(var file in SafeFiles(modRoot,"*.scs").Concat(SafeFiles(modRoot,"*.zip")))
            {
                RegisterMod(Path.GetFileName(file),file);
                RegisterMod(Path.GetFileNameWithoutExtension(file),file);
            }
        }

        // Steam Workshop do ETS2 (AppId 227300). Descobrir conteúdo instalado não
        // significa afirmar que ele está ativo: essa decisão continua pertencendo ao
        // mod_settings.sii do perfil. Diretórios expandidos e pacotes ZIP-compatible
        // podem ser lidos; HashFS continua explicitamente indisponível.
        foreach(var workshopRoot in Ets2InstallationLocator.FindWorkshopRoots())
        {
            IEnumerable<string> itemDirs;
            try { itemDirs=Directory.EnumerateDirectories(workshopRoot).ToArray(); }
            catch { itemDirs=Array.Empty<string>(); }
            foreach(var itemDir in itemDirs)
            {
                var workshopId=Path.GetFileName(itemDir);
                if(Directory.Exists(Path.Combine(itemDir,"def")))
                {
                    RegisterMod("workshop:"+workshopId,itemDir);
                    RegisterMod(workshopId,itemDir);
                }
                foreach(var file in SafeFiles(itemDir,"*.scs").Concat(SafeFiles(itemDir,"*.zip")))
                {
                    RegisterMod(Path.GetFileName(file),file);
                    RegisterMod(Path.GetFileNameWithoutExtension(file),file);
                    RegisterMod("workshop:"+workshopId,file);
                    RegisterMod(workshopId,file);
                }
            }
        }

        var ordered=new List<string>();
        var unresolvedIds=new List<string>();
        foreach(var name in activeMods)
        {
            var raw=Path.GetFileNameWithoutExtension(name);
            var aliases=new[]{name,raw,"workshop:"+raw};
            var file=aliases.Select(x=>discoveredMods.TryGetValue(x,out var hit)?hit:null).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
            if(file is not null)
            {
                if(!ordered.Contains(file,StringComparer.OrdinalIgnoreCase)) ordered.Add(file);
            }
            else unresolvedIds.Add(raw);
        }

        // Quando existe mod_settings.sii, apenas fontes efetivamente resolvidas entram
        // como Active. Conteúdo extra encontrado no disco permanece Installed e não
        // participa do override canônico. Se não há loadout legível, mantemos a
        // descoberta conservadora como Unresolved e não inventamos prioridade.
        var hasDeclaredLoadout=activeLoadout.SettingsPath.Length>0;
        var activeLoadoutResolved=hasDeclaredLoadout && unresolvedIds.Count==0;
        var activeSet=new HashSet<string>(ordered,StringComparer.OrdinalIgnoreCase);
        var allDiscovered=discoveredMods.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var selected=hasDeclaredLoadout
            ? ordered.Concat(allDiscovered.Where(x=>!activeSet.Contains(x))).ToArray()
            : allDiscovered.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();

        var activeIndex=0;
        foreach(var file in selected)
        {
            var directory=Directory.Exists(file);
            var zip=!directory && WorldSourceReader.IsZip(file);
            var kind=directory?WorldSourceKind.Directory:zip?WorldSourceKind.ZipArchive:WorldSourceKind.ScsArchive;
            var readable=directory||zip;
            var workshop=file.Contains($"{Path.DirectorySeparatorChar}workshop{Path.DirectorySeparatorChar}",StringComparison.OrdinalIgnoreCase);
            var origin=workshop?"Workshop ":"";
            var isActive=activeSet.Contains(file);
            var activation=hasDeclaredLoadout
                ? (isActive?WorldSourceActivation.Active:WorldSourceActivation.Installed)
                : WorldSourceActivation.Unresolved;
            var loadOrder=isActive?activeIndex++:-1;
            var note=activation switch
            {
                WorldSourceActivation.Active => "Active ",
                WorldSourceActivation.Installed => "Installed; not proven active; ",
                _ => "Discovered; active loadout unavailable; "
            };
            result.Add(Make("mod-"+StableSourceId(file),file,kind,true,readable,
                note+origin+(directory?"expanded DEF":zip?"ZIP-compatible mod":"SCS/HashFS mod not readable"),activation,loadOrder));
        }
        foreach(var unresolved in unresolvedIds)
            result.Add(new WorldSource("unresolved-"+unresolved,"",WorldSourceKind.Unknown,true,false,0,null,
                $"Active mod id unresolved from {Path.GetFileName(activeLoadout.SettingsPath)}: {unresolved}",WorldSourceActivation.Unresolved,-1));

        // Se todos os ativos foram resolvidos, a sequência Active preserva exatamente
        // a ordem declarada no mod_settings.sii. Fontes Installed vêm depois apenas
        // para diagnóstico e nunca devem vencer overrides ativos.
        _=activeLoadoutResolved;
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
                // Nem todo SII dentro de def/cargo/<cargo>/ representa um implemento.
                // Só promovemos a relação para COMPATÍVEL quando o leaf também resolve
                // para um trailer conhecido no catálogo. Ausência de evidência permanece Unknown.
                var knownTrailer=trailers.Any(t=>
                    string.Equals(t.Id,trailer,StringComparison.OrdinalIgnoreCase) ||
                    t.Id.EndsWith("."+trailer,StringComparison.OrdinalIgnoreCase));
                if(!string.IsNullOrWhiteSpace(cargo) && !string.IsNullOrWhiteSpace(trailer) && knownTrailer)
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

    private static string ExpandIncludeText(WorldTextFile file,IReadOnlyDictionary<string,WorldTextFile> files,HashSet<string> stack,List<string> diagnostics,int depth)
    {
        if(depth>32){diagnostics.Add($"{file.VirtualPath}: include depth exceeded");return file.Text;}
        var key=file.SourceId+"|"+NormalizePath(file.VirtualPath);
        if(!stack.Add(key)){diagnostics.Add($"{file.VirtualPath}: cyclic include ignored");return "";}
        var output=new StringBuilder();
        using var reader=new StringReader(file.Text);
        string? line;
        while((line=reader.ReadLine()) is not null)
        {
            var trimmed=line.TrimStart();
            if(trimmed.StartsWith("@include",StringComparison.OrdinalIgnoreCase))
            {
                var include=SiiDefinitionParser.Includes(line).FirstOrDefault();
                if(!string.IsNullOrWhiteSpace(include))
                {
                    var resolved=ResolveIncludePath(file.VirtualPath,include);
                    if(files.TryGetValue(resolved,out var child))
                    {
                        output.AppendLine(ExpandIncludeText(child,files,stack,diagnostics,depth+1));
                        continue;
                    }
                    diagnostics.Add($"{file.VirtualPath}: include not found: {include}");
                }
            }
            output.AppendLine(line);
        }
        stack.Remove(key);
        return output.ToString();
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

    private sealed record ActiveModLoadout(string SettingsPath,IReadOnlyList<string> ModIds);

    private static ActiveModLoadout ReadActiveModOrder(IReadOnlyList<string> roots)
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
        if(string.IsNullOrWhiteSpace(latest)) return new("",Array.Empty<string>());
        try
        {
            var text=File.ReadAllText(latest);
            var indexed=new List<(int Index,string Id)>();
            foreach(var line in text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed=line.Trim();
                if(!trimmed.StartsWith("active_mod",StringComparison.OrdinalIgnoreCase)) continue;
                var colon=trimmed.IndexOf(':');
                if(colon<0) continue;
                var key=trimmed[..colon];
                var indexStart=key.IndexOf('[');
                var indexEnd=key.IndexOf(']');
                var index=indexStart>=0 && indexEnd>indexStart &&
                    int.TryParse(key[(indexStart+1)..indexEnd],NumberStyles.Integer,CultureInfo.InvariantCulture,out var parsed)
                    ? parsed : indexed.Count;
                var q=trimmed.Split('"');
                var raw=q.Length>=2?q[^2]:trimmed[(colon+1)..].Trim();
                raw=raw.Replace("mod_package.","",StringComparison.OrdinalIgnoreCase).Trim();
                if(raw.Length>0) indexed.Add((index,raw));
            }
            var ids=indexed.OrderBy(x=>x.Index).Select(x=>x.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new(latest,ids);
        }
        catch { return new(latest,Array.Empty<string>()); }
    }

    private static string Name(WorldDefinition d)=>SiiDefinitionParser.Value(d,"name","name_localized","display_name","brand_name");
    private static double Number(WorldDefinition d,params string[] keys)=>double.TryParse(SiiDefinitionParser.Value(d,keys),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;
    private static IEnumerable<string> SafeFiles(string root,string pattern){ try{return Directory.Exists(root)?Directory.EnumerateFiles(root,pattern,SearchOption.TopDirectoryOnly).ToArray():Array.Empty<string>();}catch{return Array.Empty<string>();}}
    private static WorldSource Make(string id,string path,WorldSourceKind kind,bool mod,bool readable,string note,WorldSourceActivation activation=WorldSourceActivation.BaseGame,int loadOrder=-1)
    {
        if(Directory.Exists(path))
        {
            DateTime? lastWrite=null;
            try { lastWrite=Directory.GetLastWriteTimeUtc(path); } catch { }
            return new(id,path,kind,mod,readable,0,lastWrite,note,activation,loadOrder);
        }
        var f=new FileInfo(path);
        return new(id,path,kind,mod,readable,f.Exists?f.Length:0,f.Exists?f.LastWriteTimeUtc:null,note,activation,loadOrder);
    }
    private static string StableSourceId(string path)
    {
        var name=Directory.Exists(path)?new DirectoryInfo(path).Name:Path.GetFileName(path);
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToLowerInvariant()))).ToLowerInvariant()[..10];
        return $"{name}-{hash}";
    }
    private static string Fingerprint(IReadOnlyList<WorldSource> sources)
    {
        using var sha=SHA256.Create();
        // A ordem das fontes faz parte da identidade do catálogo: mudar prioridade/loadout
        // deve invalidar o cache mesmo quando os arquivos em si não mudaram.
        var raw=string.Join("\n",sources.Select((x,i)=>$"{i}|{x.Path}|{x.SizeBytes}|{x.LastWriteUtc:O}|{x.Readable}|{x.Note}"));
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
