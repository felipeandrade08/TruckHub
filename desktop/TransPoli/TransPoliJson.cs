using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TransPoli;

/// <summary>
/// Leitura tolerante de JSON. A API pode omitir campos ou devolver null;
/// usar GetProperty direto derrubava os modais inteiros com uma única
/// propriedade ausente.
/// </summary>
internal static class J
{
    public static JsonElement? Prop(JsonElement? element, string name)
    {
        if (element is not JsonElement e || e.ValueKind != JsonValueKind.Object) return null;
        return e.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Undefined
            ? value
            : null;
    }

    public static string Str(JsonElement? element, string name, string fallback = "")
    {
        var value = Prop(element, name);
        if (value is not JsonElement v) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? fallback,
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    public static decimal Dec(JsonElement? element, string name, decimal fallback = 0)
    {
        var value = Prop(element, name);
        if (value is not JsonElement v) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(
                v.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return fallback;
    }

    public static int Int(JsonElement? element, string name, int fallback = 0)
    {
        var value = Prop(element, name);
        if (value is not JsonElement v) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var parsed)) return parsed;
        return fallback;
    }

    public static bool Bool(JsonElement? element, string name, bool fallback = false)
    {
        var value = Prop(element, name);
        if (value is not JsonElement v) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.TryGetDouble(out var d) && Math.Abs(d) > 0.0001,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var parsed) ? parsed : fallback,
            _ => fallback
        };
    }

    public static DateTime? Date(JsonElement? element, string name)
    {
        var text = Str(element, name);
        return DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;
    }

    /// <summary>Enumera um array com segurança; devolve vazio se ausente.</summary>
    public static IEnumerable<JsonElement> Array(JsonElement? element, string name)
    {
        var value = Prop(element, name);
        if (value is not JsonElement v || v.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in v.EnumerateArray()) yield return item;
    }

    public static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }
}
