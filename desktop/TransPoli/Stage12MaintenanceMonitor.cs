using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Etapa 12 — monitor automático de manutenção e saúde do caminhão.
/// Registra eventos locais quando o desgaste ou dano evolui de forma relevante.
/// Não depende da API para monitorar ou alertar.
/// </summary>
public partial class MainWindow
{
    private static readonly bool _stage12MaintenanceMonitor = RegisterStage12MaintenanceMonitor();
    private DispatcherTimer? _stage12MaintenanceTimer;
    private float? _stage12LastMaxWear;
    private float? _stage12LastCargoDamage;
    private DateTime _stage12LastWearEventUtc = DateTime.MinValue;
    private DateTime _stage12LastDamageEventUtc = DateTime.MinValue;
    private DateTime _stage12LastAlertUtc = DateTime.MinValue;

    private static bool RegisterStage12MaintenanceMonitor()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(Stage12Loaded), true);
        return true;
    }

    private static void Stage12Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._stage12MaintenanceTimer != null) return;

        window._stage12MaintenanceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        window._stage12MaintenanceTimer.Tick += (_, _) => window.EvaluateStage12Maintenance();
        window._stage12MaintenanceTimer.Start();
        window.EvaluateStage12Maintenance();
    }

    private void EvaluateStage12Maintenance()
    {
        var data = LastTelemetry;
        if (data is null || !data.Connected) return;

        var maxWear = Math.Clamp(Math.Max(
            Math.Max(data.WearEngine, data.WearTransmission),
            Math.Max(Math.Max(data.WearCabin, data.WearChassis), data.WearWheels)), 0f, 1f);
        var damage = Math.Clamp(data.CargoDamage, 0f, 1f);
        var now = DateTime.UtcNow;

        if (_stage12LastMaxWear.HasValue && maxWear - _stage12LastMaxWear.Value >= 0.01f &&
            now - _stage12LastWearEventUtc >= TimeSpan.FromMinutes(2))
        {
            RegisterStage12HealthEvent("wear_increase",
                $"Desgaste aumentado • {FormatStage12Percent(_stage12LastMaxWear.Value)} → {FormatStage12Percent(maxWear)}", data);
            _stage12LastWearEventUtc = now;
        }

        if (_stage12LastCargoDamage.HasValue && damage - _stage12LastCargoDamage.Value >= 0.01f &&
            now - _stage12LastDamageEventUtc >= TimeSpan.FromMinutes(2))
        {
            RegisterStage12HealthEvent("cargo_damage",
                $"Dano de carga aumentado • {FormatStage12Percent(_stage12LastCargoDamage.Value)} → {FormatStage12Percent(damage)}", data);
            _stage12LastDamageEventUtc = now;
        }

        _stage12LastMaxWear = maxWear;
        _stage12LastCargoDamage = damage;

        var critical = maxWear >= 0.75f;
        var attention = maxWear >= 0.50f || damage >= 0.50f;

        if (critical && now - _stage12LastAlertUtc >= TimeSpan.FromMinutes(2))
        {
            AlertText.Text = "🔧 MANUTENÇÃO CRÍTICA • desgaste elevado detectado no caminhão";
            AlertText.Foreground = FindResource("Red") as Brush;
            _stage12LastAlertUtc = now;
        }
        else if (attention && now - _stage12LastAlertUtc >= TimeSpan.FromMinutes(2))
        {
            AlertText.Text = damage >= 0.50f
                ? "⚠ ATENÇÃO • dano elevado na carga / verifique a viagem"
                : "🔧 ATENÇÃO • desgaste elevado • planeje manutenção";
            AlertText.Foreground = FindResource("Yellow") as Brush;
            _stage12LastAlertUtc = now;
        }
    }

    private void RegisterStage12HealthEvent(string type, string note, TelemetrySnapshot data)
    {
        try
        {
            if (LocalData.Current is not { } store) return;

            var truck = !string.IsNullOrWhiteSpace(data.TruckId) ? data.TruckId! : data.LicensePlate ?? string.Empty;
            var minuteKey = DateTime.UtcNow.ToString("yyyyMMddHHmm");
            var id = $"auto-health-{type}-{truck}-{data.OdometerKm:0.0}-{minuteKey}";

            new LocalOperationsRepository(store.Db).UpsertOperationalEvent(
                id, type, "detected", note, string.Empty, data.Cargo ?? string.Empty,
                _localTripId, string.Empty, truck, DateTime.UtcNow, data.OdometerKm, false);
        }
        catch
        {
            // Monitoramento nunca interrompe a telemetria.
        }
    }

    private static string FormatStage12Percent(float value) => $"{value * 100:0}%";
}
