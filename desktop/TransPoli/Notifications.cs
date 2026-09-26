using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public enum NotificationPriority { Info, Attention, Critical }

public sealed record TransPoliNotification(
    string Key,
    NotificationPriority Priority,
    string Title,
    string Message,
    DateTime CreatedAtUtc);

public partial class MainWindow
{
    private readonly List<TransPoliNotification> _notifications = new();
    private readonly HashSet<string> _activeNotificationKeys = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _notificationTimer;

    private void StartNotificationSystem()
    {
        _notificationTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _notificationTimer.Tick -= NotificationTimer_Tick;
        _notificationTimer.Tick += NotificationTimer_Tick;
        _notificationTimer.Start();

        if (NotificationStatusText != null)
        {
            NotificationStatusText.MouseLeftButtonUp -= NotificationStatusText_MouseLeftButtonUp;
            NotificationStatusText.MouseLeftButtonUp += NotificationStatusText_MouseLeftButtonUp;
            NotificationStatusText.Cursor = Cursors.Hand;
            NotificationStatusText.ToolTip = "Abrir central de notificações";
        }
    }

    private void NotificationTimer_Tick(object? sender, EventArgs e)
    {
        EvaluateOperationalNotifications(LastTelemetry);
        UpdateNotificationIndicator();
    }

    private void EvaluateOperationalNotifications(TelemetrySnapshot? data)
    {
        if (data == null || !data.Connected)
        {
            RemoveNotification("telemetry-offline");
            return;
        }

        var pendingSync = 0;
        try
        {
            if (LocalData.Current is { } store)
                pendingSync = new LocalEconomyRepository(store.Db).GetPendingSyncCount();
        }
        catch { }

        AddOrRefresh(_tripDocumentPending, "invoice-stamp-required", NotificationPriority.Critical,
            "Carimbo obrigatório", "A nova carga está bloqueada aguardando o carimbo da nota.",
            "Mantenha o caminhão parado com o freio de estacionamento aplicado e carimbe pelo celular ou tablet.");

        AddOrRefresh(pendingSync > 0, "sync-pending", NotificationPriority.Attention,
            "Sincronização pendente", $"{pendingSync} item(ns) aguardando sincronização central.",
            "Os dados continuam seguros no dispositivo e serão enviados automaticamente.");

        AddOrRefresh(data.FuelWarning, "fuel-low", NotificationPriority.Critical,
            "Combustível baixo", $"Restam {data.FuelLiters:0.0} L • autonomia {data.FuelRangeKm:0} km.",
            "Abasteça assim que for seguro.");

        AddOrRefresh(data.AdBlueWarning, "adblue-low", NotificationPriority.Attention,
            "AdBlue baixo", $"Nível atual: {data.AdBlueLiters:0.0} L.",
            "Planeje o próximo abastecimento de AdBlue.");

        AddOrRefresh(data.OilPressureWarning, "oil-pressure", NotificationPriority.Critical,
            "Pressão do óleo", $"Pressão detectada: {data.OilPressure:0.0}.",
            "Verifique o motor antes de continuar.");

        AddOrRefresh(data.WaterTemperatureWarning, "water-temperature", NotificationPriority.Critical,
            "Temperatura do motor", $"Temperatura da água: {data.WaterTemperature:0.0} °C.",
            "Reduza a operação e verifique o sistema de arrefecimento.");

        AddOrRefresh(data.BatteryVoltageWarning, "battery", NotificationPriority.Attention,
            "Bateria", $"Tensão detectada: {data.BatteryVoltage:0.0} V.",
            "Verifique a alimentação elétrica.");

        AddOrRefresh(data.AirPressureEmergency, "air-emergency", NotificationPriority.Critical,
            "Pressão de ar crítica", $"Pressão atual: {data.AirPressure:0.0} psi.",
            "Pare em local seguro e verifique o sistema de freios.");

        AddOrRefresh(!data.AirPressureEmergency && data.AirPressureWarning, "air-low", NotificationPriority.Attention,
            "Pressão de ar baixa", $"Pressão atual: {data.AirPressure:0.0} psi.",
            "Acompanhe a pressão antes de continuar.");

        var maxWear = Math.Max(Math.Max(data.WearEngine, data.WearTransmission),
            Math.Max(Math.Max(data.WearCabin, data.WearChassis), data.WearWheels));

        AddOrRefresh(maxWear >= .75f, "maintenance-critical", NotificationPriority.Critical,
            "Manutenção necessária", $"Desgaste máximo: {maxWear * 100:0.0}%.",
            "Abra Manutenção e programe o serviço.");

        AddOrRefresh(maxWear >= .50f && maxWear < .75f, "maintenance-attention", NotificationPriority.Attention,
            "Manutenção preventiva", $"Desgaste máximo: {maxWear * 100:0.0}%.",
            "Planeje uma manutenção preventiva.");

        AddOrRefresh(data.CargoDamage >= .10f, "cargo-damage", NotificationPriority.Attention,
            "Avaria na carga", $"Avaria atual: {data.CargoDamage * 100:0.0}%.",
            "Conduza com atenção para preservar a carga.");

        AddOrRefresh(data.RefuelPayed, "refuel", NotificationPriority.Info,
            "Abastecimento detectado", $"Foram detectados {data.RefuelAmountLiters:0.0} L no último abastecimento.",
            "Abra a Central de Combustível para registrar os detalhes.");

        AddOrRefresh(_tripActive, "trip-active", NotificationPriority.Info,
            "Viagem em andamento",
            $"{BuildNotificationRoute(data)} • {Math.Max(0, data.SpeedKph):0} km/h.",
            "Telemetria e registro local ativos.");

        AddOrRefresh(data.GamePaused, "game-paused", NotificationPriority.Info,
            "ETS2 pausado", "A telemetria continua conectada, mas o jogo está pausado.",
            "Retome o jogo para continuar a operação.");
    }

    private void AddOrRefresh(bool condition, string key, NotificationPriority priority, string title, string message, string detail)
    {
        if (!condition)
        {
            RemoveNotification(key);
            return;
        }

        var text = string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}";
        var existing = _notifications.FirstOrDefault(n => n.Key == key);
        if (existing != null)
        {
            if (existing.Priority == priority && existing.Title == title && existing.Message == text) return;
            _notifications.Remove(existing);
        }

        _notifications.Add(new TransPoliNotification(key, priority, title, text, DateTime.UtcNow));
        _activeNotificationKeys.Add(key);
    }

    private void RemoveNotification(string key)
    {
        _activeNotificationKeys.Remove(key);
        _notifications.RemoveAll(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildNotificationRoute(TelemetrySnapshot data)
    {
        var origin = string.IsNullOrWhiteSpace(data.SourceCity) ? "Origem" : data.SourceCity;
        var destination = string.IsNullOrWhiteSpace(data.DestinationCity) ? "Destino" : data.DestinationCity;
        var cargo = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga em operação" : data.Cargo;
        return $"{cargo} • {origin} → {destination}";
    }

    private void UpdateNotificationIndicator()
    {
        if (NotificationStatusText == null) return;

        var critical = _notifications.Any(n => n.Priority == NotificationPriority.Critical);
        var attention = _notifications.Any(n => n.Priority == NotificationPriority.Attention);

        NotificationStatusText.Text = _notifications.Count == 0 ? "○" : "●";
        NotificationStatusText.Foreground = FindResource(
            critical ? "Red" : attention ? "GoldBright" : _notifications.Count > 0 ? "Green" : "TextMuted") as Brush;

        NotificationStatusText.ToolTip = _notifications.Count == 0
            ? "Sem notificações operacionais"
            : $"{_notifications.Count} notificação(ões) • clique para abrir";
    }

    private async void NotificationStatusText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ShowNotificationsTabletModalAsync();
    }

    internal Task ShowNotificationsTabletModalAsync()
    {
        var body = new StackPanel();

        var critical = _notifications.Count(n => n.Priority == NotificationPriority.Critical);
        var attention = _notifications.Count(n => n.Priority == NotificationPriority.Attention);
        var info = _notifications.Count(n => n.Priority == NotificationPriority.Info);

        body.Children.Add(ModalHero("CENTRAL DE OCORRÊNCIAS", "Prioridades da operação", "Somente situações que pedem atenção, acompanhamento ou registro do motorista. Instrumentação contínua permanece na HUD.", _notifications.Count == 0 ? "OPERAÇÃO NORMAL" : $"{_notifications.Count} OCORRÊNCIA(S)", critical > 0 ? "Red" : attention > 0 ? "Yellow" : "Green"));
        body.Children.Add(ModalStatusStrip(critical > 0 ? "● ATENÇÃO IMEDIATA • EXISTEM ALERTAS CRÍTICOS ATIVOS" : attention > 0 ? "● OPERAÇÃO EM ATENÇÃO • REVISE OS AVISOS ABAIXO" : "✓ SISTEMAS MONITORADOS • SEM ALERTAS CRÍTICOS", critical > 0 ? "Red" : attention > 0 ? "Yellow" : "Green"));
        body.Children.Add(ModalSectionTitle("RESUMO", "PRIORIDADE DOS ALERTAS"));
        var summary = new UniformGrid { Columns = 3 };
        summary.Children.Add(MiniCard("CRÍTICOS", critical.ToString()));
        summary.Children.Add(MiniCard("ATENÇÃO", attention.ToString()));
        summary.Children.Add(MiniCard("INFORMAÇÕES", info.ToString()));
        body.Children.Add(summary);

        body.Children.Add(ModalSectionTitle("OCORRÊNCIAS ATIVAS", "PRIORIDADE OPERACIONAL"));

        if (_notifications.Count == 0)
        {
            body.Children.Add(ModalStatePanel(
                "OPERAÇÃO NORMAL",
                "Nenhuma ocorrência ativa",
                "Não há situação crítica, atenção mecânica, pendência documental ou sincronização operacional exigindo ação neste momento.",
                "Green"));
        }
        else
        {
            foreach (var notification in _notifications
                .OrderByDescending(n => n.Priority)
                .ThenByDescending(n => n.CreatedAtUtc))
            {
                var color = notification.Priority switch
                {
                    NotificationPriority.Critical => "Red",
                    NotificationPriority.Attention => "GoldBright",
                    _ => "Green"
                };

                var label = notification.Priority switch
                {
                    NotificationPriority.Critical => "CRÍTICO",
                    NotificationPriority.Attention => "ATENÇÃO",
                    _ => "INFO"
                };

                body.Children.Add(ModalPanel(new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{label} • {notification.Title}",
                            FontSize = 16,
                            FontWeight = FontWeights.Bold,
                            Foreground = FindResource(color) as Brush
                        },
                        new TextBlock
                        {
                            Text = notification.Message,
                            FontSize = 13,
                            Foreground = FindResource("TextMain") as Brush,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(0, 5, 0, 0)
                        },
                        new TextBlock
                        {
                            Text = notification.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),
                            FontSize = 10,
                            Foreground = FindResource("TextMuted") as Brush,
                            Margin = new Thickness(0, 5, 0, 0)
                        }
                    }
                }));
            }
        }

        var refresh = ModalButton("↻ REAVALIAR OCORRÊNCIAS");
        refresh.Click += (_, e) =>
        {
            e.Handled = true;
            EvaluateOperationalNotifications(LastTelemetry);
            ShowNotificationsTabletModalAsync();
        };
        body.Children.Add(refresh);

        ShowModalContent("notifications",
            BuildModalCard("🔔 CENTRAL DE OCORRÊNCIAS", body,
                "Prioridades reais • viagem • documentação • manutenção • sincronização"));

        return Task.CompletedTask;
    }
}
