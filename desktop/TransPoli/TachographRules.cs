using System;
using System.Collections.Generic;
using System.Linq;

namespace TransPoli;

/// <summary>
/// Regras de jornada inspiradas no Regulamento (CE) 561/2006, em TEMPO DE JOGO:
/// direção contínua máx. 4h30 (pausa de 45 min), direção diária 9h (até 10h em exceção),
/// direção semanal 56h. Só calcula; não bloqueia nem multa. Os intervalos vêm do
/// registro persistido do tacógrafo e o tempo real é convertido pela escala medida
/// ao vivo (o ETS2 passa o tempo bem mais rápido que o relógio real).
/// </summary>
internal static class TachographRules
{
    public const double ContinuousLimitMin = 270;
    public const double BreakMin = 45;
    public const double DailyLimitMin = 540;
    public const double DailyExtendedLimitMin = 600;
    public const double DailyRestMin = 540;      // descanso diário reduzido (9h) reinicia o dia
    public const double WeeklyLimitMin = 3360;

    public sealed record Snapshot(
        double ContinuousMin, double DailyMin, double WeeklyMin,
        double MinutesToBreak, string State, IReadOnlyList<string> Infractions);

    public static Snapshot Evaluate(IEnumerable<(bool Driving, DateTime StartUtc, DateTime? EndUtc)> records, DateTime nowUtc, double scale)
    {
        scale = Math.Clamp(scale, 1, 100);
        // Condução em minutos de jogo contados para trás a partir de agora (0 = agora).
        var driving = records
            .Where(r => r.Driving)
            .Select(r => (From: (nowUtc - (r.EndUtc ?? nowUtc)).TotalMinutes * scale, To: (nowUtc - r.StartUtc).TotalMinutes * scale))
            .Where(r => r.To > r.From)
            .OrderBy(r => r.From)                 // mais recente primeiro
            .ToList();

        double continuous = 0, daily = 0, weekly = 0;

        var cursor = 0d;
        foreach (var d in driving)
        {
            if (d.From - cursor >= BreakMin) break;
            continuous += d.To - d.From;
            cursor = Math.Max(cursor, d.To);
        }

        cursor = 0;
        foreach (var d in driving)
        {
            if (d.From - cursor >= DailyRestMin || d.From > 1440) break;
            daily += Math.Min(d.To, 1440) - d.From;
            cursor = Math.Max(cursor, d.To);
        }

        foreach (var d in driving)
        {
            if (d.From >= 10080) break;
            weekly += Math.Min(d.To, 10080) - d.From;
        }

        var infractions = new List<string>();
        if (continuous > ContinuousLimitMin) infractions.Add("Direção contínua acima de 4h30 sem pausa de 45 min");
        if (daily > DailyExtendedLimitMin) infractions.Add("Direção diária acima de 10h");
        if (weekly > WeeklyLimitMin) infractions.Add("Direção semanal acima de 56h");

        var state = infractions.Count > 0 ? "INFRAÇÃO"
            : continuous >= 240 || daily >= DailyLimitMin || weekly >= WeeklyLimitMin - 240 ? "ATENÇÃO"
            : "REGULAR";
        return new Snapshot(continuous, daily, weekly, Math.Max(0, ContinuousLimitMin - continuous), state, infractions);
    }

    public static string Format(double minutes)
    {
        var m = (int)Math.Round(Math.Max(0, minutes));
        return $"{m / 60}h{m % 60:00}";
    }
}
