using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TransPoli.Navigation;

/// <summary>
/// Lê os parâmetros de projeção de um climate.sii já extraído.
/// Não descompacta .scs por conta própria e não assume valores de mapa.
/// </summary>
public static class ClimateProfileParser
{
    private static readonly Regex ProjectionRegex =
        new(@"map_projection\s*:\s*([A-Za-z0-9_]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VectorRegex =
        new(@"map_(origin|factor|offset)\s*:\s*\(\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Parallel1Regex =
        new(@"standard_paralel_1\s*:\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Parallel2Regex =
        new(@"standard_paralel_2\s*:\s*([-+0-9.eE]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryReadFile(string path, out MapCalibration calibration)
    {
        calibration = new MapCalibration();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            return TryParse(File.ReadAllText(path), out calibration);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryParse(string text, out MapCalibration calibration)
    {
        calibration = new MapCalibration();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var projectionMatch = ProjectionRegex.Match(text);
        var origin = FindVector(text, "origin");
        var factor = FindVector(text, "factor");
        var offset = FindVector(text, "offset");

        if (!origin.HasValue || !factor.HasValue)
            return false;

        var projection = projectionMatch.Success
            ? projectionMatch.Groups[1].Value.Trim()
            : "mercator";

        var standardParallel1 = FindScalar(text, Parallel1Regex);
        var standardParallel2 = FindScalar(text, Parallel2Regex);

        calibration = new MapCalibration
        {
            Enabled = true,
            Projection = projection,
            MapOriginLatitude = origin.Value.Item1,
            MapOriginLongitude = origin.Value.Item2,
            MapFactorLatitude = factor.Value.Item1,
            MapFactorLongitude = factor.Value.Item2,
            MapOffsetZ = offset?.Item1 ?? 0d,
            MapOffsetX = offset?.Item2 ?? 0d,
            StandardParallel1 = standardParallel1,
            StandardParallel2 = standardParallel2
        };

        return calibration.IsUsable;
    }

    private static (double, double)? FindVector(string text, string name)
    {
        foreach (Match match in VectorRegex.Matches(text))
        {
            if (!string.Equals(match.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParseDouble(match.Groups[2].Value, out var first) &&
                TryParseDouble(match.Groups[3].Value, out var second))
            {
                return (first, second);
            }
        }

        return null;
    }

    private static double? FindScalar(string text, Regex regex)
    {
        var match = regex.Match(text);
        return match.Success && TryParseDouble(match.Groups[1].Value, out var value)
            ? value
            : null;
    }

    private static bool TryParseDouble(string value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
