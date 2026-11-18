using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TruckHub;

/// <summary>
/// Histórico de desgaste por componente (hodômetro × desgaste) para prever quando cada peça chega a 75%.
/// Só guarda amostras reais da telemetria; uma queda de desgaste (conserto) reinicia a série daquele componente.
/// </summary>
internal static class WearHistory
{
    public sealed record Sample(float Km, float Wear);
    public sealed record Prediction(float Wear, float? PerThousandKm, float? KmToTarget);

    private static readonly object Gate = new();
    private static Dictionary<string, List<Sample>>? _data;
    private static string PathFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TruckHub", "wear-history.json");

    private static Dictionary<string, List<Sample>> Load()
    {
        if (_data != null) return _data;
        try { _data = JsonSerializer.Deserialize<Dictionary<string, List<Sample>>>(File.ReadAllText(PathFile)) ?? new(); }
        catch { _data = new(); }
        return _data;
    }

    public static void Record(string key, float km, float wear)
    {
        if (!float.IsFinite(km) || !float.IsFinite(wear) || km <= 0) return;
        lock (Gate)
        {
            var all = Load();
            if (!all.TryGetValue(key, out var list)) all[key] = list = new();
            var last = list.Count > 0 ? list[^1] : null;
            if (last != null && wear < last.Wear - 0.005f) list.Clear();                 // conserto
            else if (last != null && (Math.Abs(wear - last.Wear) < 0.01f || km <= last.Km)) return;
            list.Add(new Sample(km, wear));
            if (list.Count > 40) list.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
                File.WriteAllText(PathFile, JsonSerializer.Serialize(all));
            }
            catch { }
        }
    }

    public static Prediction Predict(string key, float currentWear, float target = 0.75f)
    {
        lock (Gate)
        {
            if (!Load().TryGetValue(key, out var list) || list.Count < 2) return new Prediction(currentWear, null, null);
            var first = list[0]; var last = list[^1];
            var dKm = last.Km - first.Km; var dW = last.Wear - first.Wear;
            if (dKm < 20f || dW <= 0f) return new Prediction(currentWear, null, null);
            var perKm = dW / dKm;
            var remaining = Math.Max(0f, target - currentWear);
            return new Prediction(currentWear, perKm * 1000f, currentWear >= target ? 0f : remaining / perKm);
        }
    }
}
