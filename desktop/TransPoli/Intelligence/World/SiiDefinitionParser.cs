using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TransPoli.Intelligence.World;

/// <summary>Parser tolerante para definições SII/DEF. Preserva campos desconhecidos.</summary>
public sealed class SiiDefinitionParser
{
    public IReadOnlyList<WorldDefinition> Parse(string text,string sourceId,string virtualPath)
    {
        if(string.IsNullOrWhiteSpace(text)) return Array.Empty<WorldDefinition>();
        var result=new List<WorldDefinition>();
        using var reader=new StringReader(text);
        string? line; string? type=null,id=null; Dictionary<string,string>? fields=null;
        while((line=reader.ReadLine())!=null)
        {
            var clean=StripComment(line).Trim();
            if(clean.Length==0 || clean.StartsWith("SiiNunit",StringComparison.OrdinalIgnoreCase) || clean.StartsWith("@include",StringComparison.OrdinalIgnoreCase)) continue;
            if(fields is null)
            {
                var brace=clean.IndexOf('{');
                var colon=clean.IndexOf(':');
                if(colon>0 && brace>colon)
                {
                    type=clean[..colon].Trim();
                    id=clean[(colon+1)..brace].Trim();
                    if(type.Length>0 && id.Length>0) fields=new(StringComparer.OrdinalIgnoreCase);
                }
                continue;
            }
            if(clean.StartsWith("}"))
            {
                result.Add(new WorldDefinition(type!,id!,fields,sourceId,virtualPath));
                type=id=null; fields=null; continue;
            }
            var split=clean.IndexOf(':');
            if(split<=0) continue;
            var key=clean[..split].Trim();
            var value=CleanValue(clean[(split+1)..]);
            if(key.Length>0) fields[key]=value;
        }
        return result;
    }

    public static IReadOnlyList<string> Includes(string text)
    {
        var list=new List<string>();
        using var reader=new StringReader(text ?? "");
        string? line;
        while((line=reader.ReadLine())!=null)
        {
            var clean=StripComment(line).Trim();
            if(!clean.StartsWith("@include",StringComparison.OrdinalIgnoreCase)) continue;
            var value=CleanValue(clean[8..]);
            if(value.Length>0) list.Add(value.Replace('\\','/'));
        }
        return list;
    }

    internal static string Value(WorldDefinition d,params string[] keys)
    {
        foreach(var key in keys)
            if(d.Fields.TryGetValue(key,out var value) && !string.IsNullOrWhiteSpace(value)) return CleanValue(value);
        return "";
    }

    internal static IReadOnlyList<string> ArrayValues(WorldDefinition d,params string[] names) =>
        d.Fields.Where(x=>names.Any(n=>x.Key.StartsWith(n+"[",StringComparison.OrdinalIgnoreCase)))
            .OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).Select(x=>CleanValue(x.Value))
            .Where(x=>x.Length>0 && !x.Equals("null",StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    internal static string CleanValue(string value)
    {
        var s=value.Trim();
        if(s.Length>=2 && ((s[0]=='"'&&s[^1]=='"')||(s[0]=='\''&&s[^1]=='\''))) s=s[1..^1];
        return s.Trim();
    }

    private static string StripComment(string value)
    {
        var quoted=false;
        for(var i=0;i<value.Length;i++)
        {
            if(value[i]=='"') quoted=!quoted;
            if(!quoted && value[i]=='#') return value[..i];
        }
        return value;
    }
}
