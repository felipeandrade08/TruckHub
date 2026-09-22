using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TransPoli.GameSave;

public static class GameSiiLocator
{
    private static readonly string[] ProfileRoots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Euro Truck Simulator 2"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Euro Truck Simulator 2", "profiles"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Euro Truck Simulator 2", "steam_profiles")
    };

    public static string? FindLatestGameSii()
    {
        foreach (var root in ProfileRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var found = FindLatestUnder(root);
            if (found is not null)
                return found;
        }

        return null;
    }

    public static IReadOnlyList<string> FindAllGameSii()
    {
        var result = new List<string>();

        foreach (var root in ProfileRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                result.AddRange(Directory.EnumerateFiles(root, "game.sii", SearchOption.AllDirectories));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
    }

    private static string? FindLatestUnder(string root)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            return Directory.EnumerateFiles(root, "game.sii", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }
}
