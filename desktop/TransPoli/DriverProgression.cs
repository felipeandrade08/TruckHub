using System;

namespace TransPoli;

/// <summary>
/// Progressão do motorista calculada só com dados reais já registrados
/// (viagens concluídas, km rodados, receita e multas). Não há XP guardado:
/// o valor é sempre recalculado do histórico, então tablet, celular e perfil
/// mostram exatamente o mesmo número.
/// </summary>
internal static class DriverProgression
{
    public sealed record Snapshot(long Xp, int Level, string Title, long XpIntoLevel, long XpForNext, double Progress, bool MaxLevel);

    private const int MaxLevel = 50;
    private static readonly (int From, string Title)[] Titles =
    {
        (1, "Aprendiz"), (4, "Motorista"), (8, "Profissional"), (14, "Veterano"),
        (22, "Mestre da estrada"), (35, "Lenda do asfalto")
    };

    // XP acumulado necessário para ALCANÇAR o nível n: 250·(n-1)·n  (2→500, 3→1.500, 4→3.000…)
    private static long XpAt(int level) => 250L * (level - 1) * level;

    public static long ComputeXp(int trips, decimal distanceKm, decimal grossTotal, int violations)
    {
        var xp = trips * 100m + Math.Max(0m, distanceKm) * 2m + Math.Max(0m, grossTotal) / 50m - violations * 150m;
        return (long)Math.Max(0m, Math.Round(xp));
    }

    public static Snapshot From(long xp)
    {
        var level = 1;
        while (level < MaxLevel && xp >= XpAt(level + 1)) level++;
        var title = Titles[0].Title;
        foreach (var t in Titles) if (level >= t.From) title = t.Title;
        if (level >= MaxLevel) return new Snapshot(xp, level, title, 0, 0, 1, true);
        var into = xp - XpAt(level);
        var span = XpAt(level + 1) - XpAt(level);
        return new Snapshot(xp, level, title, into, span, span <= 0 ? 0 : Math.Clamp((double)into / span, 0, 1), false);
    }
}
