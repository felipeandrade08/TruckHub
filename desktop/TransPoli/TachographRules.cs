using System;
using System.Collections.Generic;
using System.Linq;

namespace TransPoli;

/// <summary>
/// Regras de jornada em TEMPO DE JOGO, por jogo:
///  • ETS2 — Reg. (CE) 561/2006: 4h30 contínuas (pausa 45 min), 9h/dia (10h em exceção), 56h/semana.
///  • ATS  — FMCSA (HOS): 8h contínuas (pausa 30 min), 11h/dia, descanso de 10h, 60h em 7 dias.
/// Só calcula; não bloqueia nem multa. Os intervalos vêm do
/// registro persistido do tacógrafo e o tempo real é convertido pela escala medida
/// ao vivo (o ETS2 passa o tempo bem mais rápido que o relógio real).
/// </summary>
internal static class TachographRules
{
    public sealed record Limits(
        string Label, double ContinuousMin, double BreakMin, double DailyMin, double DailyExtendedMin,
        double DailyRestMin, double WeeklyMin, string ContinuousText, string DailyText, string WeeklyText)
    {
        public static readonly Limits Eu = new("ETS2 • CE 561/2006", 270, 45, 540, 600, 540, 3360,
            "Direção contínua (pausa de 45 min)", "Direção do dia (reinicia após 9h de descanso)", "Direção da semana (56h em 7 dias de jogo)");
        public static readonly Limits Us = new("ATS • FMCSA", 480, 30, 660, 660, 600, 3600,
            "Direção contínua (pausa de 30 min após 8h)", "Direção do dia (11h, reinicia após 10h de descanso)", "Direção da semana (60h em 7 dias de jogo)");

        public static Limits For(bool ats) => ats ? Us : Eu;
    }

    public sealed record Snapshot(
        double ContinuousMin, double DailyMin, double WeeklyMin,
        double MinutesToBreak, string State, IReadOnlyList<string> Infractions, Limits Limits);

    public static Snapshot Evaluate(IEnumerable<(bool Driving, DateTime StartUtc, DateTime? EndUtc)> records, DateTime nowUtc, double scale, Limits limits)
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
            if (d.From - cursor >= limits.BreakMin) break;
            continuous += d.To - d.From;
            cursor = Math.Max(cursor, d.To);
        }

        cursor = 0;
        foreach (var d in driving)
        {
            if (d.From - cursor >= limits.DailyRestMin || d.From > 1440) break;
            daily += Math.Min(d.To, 1440) - d.From;
            cursor = Math.Max(cursor, d.To);
        }

        foreach (var d in driving)
        {
            if (d.From >= 10080) break;
            weekly += Math.Min(d.To, 10080) - d.From;
        }

        var infractions = new List<string>();
        if (continuous > limits.ContinuousMin) infractions.Add($"Direção contínua acima de {Format(limits.ContinuousMin)} sem pausa de {limits.BreakMin:0} min");
        if (daily > limits.DailyExtendedMin) infractions.Add($"Direção diária acima de {Format(limits.DailyExtendedMin)}");
        if (weekly > limits.WeeklyMin) infractions.Add($"Direção semanal acima de {Format(limits.WeeklyMin)}");

        var state = infractions.Count > 0 ? "INFRAÇÃO"
            : continuous >= limits.ContinuousMin - 30 || daily >= limits.DailyMin || weekly >= limits.WeeklyMin - 240 ? "ATENÇÃO"
            : "REGULAR";
        return new Snapshot(continuous, daily, weekly, Math.Max(0, limits.ContinuousMin - continuous), state, infractions, limits);
    }

    public static string Format(double minutes)
    {
        var m = (int)Math.Round(Math.Max(0, minutes));
        return $"{m / 60}h{m % 60:00}";
    }
}
