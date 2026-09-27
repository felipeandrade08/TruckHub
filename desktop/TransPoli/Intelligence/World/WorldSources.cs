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
    public static IReadOnlyList<string> FindCandidates()
    {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p){ if(!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) roots.Add(Path.GetFullPath(p)); }
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam","steamapps","common","Euro Truck Simulator 2"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam","steamapps","common","Euro Truck Simulator 2"));
        try
        {
            var steam=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath")?.ToString();
            if(!string.IsNullOrWhiteSpace(steam))
            {
                Add(Path.Combine(steam,"steamapps","common","Euro Truck Simulator 2"));
                var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
                if(File.Exists(vdf))
                    foreach(var line in File.ReadLines(vdf))
                    {
                        var marker="\"path\"";
                        if(!line.Contains(marker,StringComparison.OrdinalIgnoreCase)) continue;
                        var q=line.Split('"',StringSplitOptions.RemoveEmptyEntries);
                        if(q.Length>1) Add(Path.Combine(q[^1].Replace(@"\\",@"\"),"steamapps","common","Euro Truck Simulator 2"));
                    }
            }
        } catch { }
        return roots.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal sealed class WorldSourceReader
{
    private static readonly string[] RelevantPrefixes={"def/city/","def/country/","def/company/","def/cargo/","def/vehicle/trailer","def/vehicle/trailer_owned"};
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
            if(IsRelevant(relative)) yield return new(sourceId,relative,text);
        }
    }

    public IEnumerable<WorldTextFile> ReadZip(string sourceId,string archivePath)
    {
        ZipArchive zip; try { zip=ZipFile.OpenRead(archivePath); } catch { yield break; }
        using(zip)
        foreach(var entry in zip.Entries)
        {
            var path=entry.FullName.Replace('\\','/');
            if(!IsRelevant(path) || !(path.EndsWith(".sii",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".sui",StringComparison.OrdinalIgnoreCase))) continue;
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
