using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TransPoli.GameSave;

public sealed class GameSiiParser
{
    private static readonly Regex BlockRegex = new(
        @"(?m)^\s*(?<type>[A-Za-z0-9_\.]+)\s*:\s*(?<id>[^\s\\r\\n]+)\s*\\r?\\n(?<body>.*?)(?=^\s*[A-Za-z0-9_\.]+\s*:\s*[^\s\\r\\n]+\s*\\r?$|\z)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex FieldRegex = new(
        @"(?m)^\s*(?<key>[A-Za-z0-9_]+)\s*:\s*(?<value>.*?)\s*$",
        RegexOptions.Compiled);

    public IReadOnlyList<SiiBlock> ParseBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<SiiBlock>();

        var blocks = new List<SiiBlock>();

        foreach (Match match in BlockRegex.Matches(text))
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match field in FieldRegex.Matches(match.Groups["body"].Value))
                fields[field.Groups["key"].Value] = field.Groups["value"].Value.Trim();

            blocks.Add(new SiiBlock(
                match.Groups["type"].Value,
                match.Groups["id"].Value,
                fields));
        }

        return blocks;
    }

    public GameSaveSnapshot ParseSnapshot(string text)
    {
        var blocks = ParseBlocks(text);

        var snapshot = new GameSaveSnapshot
        {
            ParsedAtUtc = DateTime.UtcNow,
            BlockCount = blocks.Count,
            RawTextLength = text?.Length ?? 0
        };

        var player = blocks.FirstOrDefault(b =>
            b.Type.Equals("player", StringComparison.OrdinalIgnoreCase));

        snapshot.HeadquartersCity = player?.GetCleanString("hq_city");

        // Phase A deliberately does not interpret ETS2 money/economy fields.
        // Vehicle/trailer/stat extraction belongs to phases B/C/D/E.
        return snapshot;
    }

    internal static string CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var result = value.Trim();

        var commentIndex = result.IndexOf('#');
        if (commentIndex >= 0)
            result = result[..commentIndex].Trim();

        if (result.Length >= 2 &&
            ((result[0] == '"' && result[^1] == '"') ||
             (result[0] == '\'' && result[^1] == '\'')))
            result = result[1..^1];

        return result;
    }

    internal static bool TryParseDouble(string? value, out double result)
    {
        return double.TryParse(
            CleanValue(value).Replace("&", string.Empty),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }
}

public sealed record SiiBlock(
    string Type,
    string Id,
    IReadOnlyDictionary<string, string> Fields)
{
    public string? Get(string key) =>
        Fields.TryGetValue(key, out var value) ? value : null;

    public string? GetCleanString(string key)
    {
        var value = Get(key);
        return string.IsNullOrWhiteSpace(value) ? null : GameSiiParser.CleanValue(value);
    }
}
