using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace TransPoli.Intelligence.World;

internal sealed record WorldTextFile(string SourceId,string VirtualPath,string Text);

public static class Ets2InstallationLocator
{
    public static IReadOnlyList<string> FindSteamLibraryRoots()
    {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            if(string.IsNullOrWhiteSpace(p)) return;
            try
            {
                var full=Path.GetFullPath(p);
                if(Directory.Exists(Path.Combine(full,"steamapps"))) roots.Add(full);
            }
            catch { }
        }

        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam"));
        try
        {
            var steam=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString();
            Add(steam);
            if(!string.IsNullOrWhiteSpace(steam))
            {
                var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
                if(File.Exists(vdf))
                    foreach(var line in File.ReadLines(vdf))
                    {
                        if(!line.Contains("\"path\"",StringComparison.OrdinalIgnoreCase)) continue;
                        var q=line.Split('"',StringSplitOptions.RemoveEmptyEntries);
                        if(q.Length>1) Add(q[^1].Replace(@"\\",@"\"));
                    }
            }
        }
        catch { }
        return roots.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> FindCandidates()
    {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var library in FindSteamLibraryRoots())
        {
            var candidate=Path.Combine(library,"steamapps","common","Euro Truck Simulator 2");
            if(Directory.Exists(candidate)) roots.Add(Path.GetFullPath(candidate));
        }
        return roots.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlyList<string> FindWorkshopRoots()
    {
        var roots=new List<string>();
        foreach(var library in FindSteamLibraryRoots())
        {
            var candidate=Path.Combine(library,"steamapps","workshop","content","227300");
            if(Directory.Exists(candidate)) roots.Add(Path.GetFullPath(candidate));
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal sealed class WorldSourceReader
{
    private static readonly string[] RelevantPrefixes={"def/city/","def/country/","def/company/","def/cargo/","def/vehicle/trailer","def/vehicle/trailer_owned"};
    private static readonly string[] IncludeExtensions={".sii",".sui"};
    public IEnumerable<WorldTextFile> ReadDirectory(string sourceId,string root)
    {
        var def=Path.Combine(root,"def");
        if(!Directory.Exists(def)) yield break;
        IEnumerable<string> files;
        try { files=Directory.EnumerateFiles(def,"*.sii",SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(def,"*.sui",SearchOption.AllDirectories)).ToArray(); }
        catch { yield break; }
        foreach(var file in files)
        {
            string text; try { text=File.ReadAllText(file,Encoding.UTF8); } catch { continue; }
            var relative=Path.GetRelativePath(root,file).Replace('\\','/');
            if(IsRelevant(relative) || IncludeExtensions.Any(x=>relative.EndsWith(x,StringComparison.OrdinalIgnoreCase))) yield return new(sourceId,relative,text);
        }
    }

    public IEnumerable<WorldTextFile> ReadZip(string sourceId,string archivePath)
    {
        ZipArchive zip; try { zip=ZipFile.OpenRead(archivePath); } catch { yield break; }
        using(zip)
        foreach(var entry in zip.Entries)
        {
            var path=entry.FullName.Replace('\\','/');
            if(!(path.EndsWith(".sii",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".sui",StringComparison.OrdinalIgnoreCase))) continue;
            // Evita materializar arquivos textuais gigantes fora do escopo do catálogo.
            // SII/SUI relevantes e includes normais continuam disponíveis.
            if(entry.Length>16L*1024*1024 && !IsRelevant(path)) continue;
            string text; try { using var s=entry.Open(); using var r=new StreamReader(s,Encoding.UTF8,true); text=r.ReadToEnd(); } catch { continue; }
            yield return new(sourceId,path,text);
        }
    }

    public static bool IsZip(string path)
    {
        try { using var s=File.OpenRead(path); return s.ReadByte()==0x50 && s.ReadByte()==0x4B; } catch { return false; }
    }
    private static bool IsRelevant(string path)
    {
        var p=path.TrimStart('/').Replace('\\','/');
        return RelevantPrefixes.Any(x=>p.StartsWith(x,StringComparison.OrdinalIgnoreCase));
    }
}
