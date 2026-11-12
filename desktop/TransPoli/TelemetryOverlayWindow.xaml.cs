using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Threading;
using TransPoli.Media;
using System.Globalization;
using Wm = System.Windows.Media;

namespace TransPoli;

public partial class TelemetryOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x80;
    private readonly DispatcherTimer _popupTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Queue<(string Message,bool Critical)> _eventQueue = new();
    private readonly Dictionary<string,DateTime> _recentEvents = new(StringComparer.Ordinal);
    private bool _eventVisible;
    private Window? _eventWindow;
    private HudSettings _settings = new();

    public void ApplySettings(HudSettings settings)
    {
        _settings = settings;

        // Desativar alertas também invalida qualquer popup/fila já existente.
        // Caso contrário um alerta antigo podia reaparecer ao reativar a opção.
        if (!settings.ShowAlerts)
        {
            _popupTimer.Stop();
            _eventQueue.Clear();
            _eventVisible = false;
            EventPopup.Visibility = Visibility.Collapsed;
            try { _eventWindow?.Close(); } catch { }
            _eventWindow = null;
        }

        ApplyVisualSettings();

        if (settings.Enabled)
            Show();
        else
            Hide();
    }

    public void ApplyVisualSettings()
    {
        Opacity = Math.Clamp(_settings.Opacity, 0.35, 1.0);
        var scale = Math.Clamp(_settings.Scale, 0.55, 1.00);
        var horizontalAnchor = _settings.Position.Contains("esquerdo", StringComparison.OrdinalIgnoreCase) ? 0d
            : _settings.Position.Contains("direito", StringComparison.OrdinalIgnoreCase) ? 1d : .5d;
        var verticalAnchor = _settings.Position.StartsWith("Superior", StringComparison.OrdinalIgnoreCase) || _settings.Position == "Topo" ? 0d
            : _settings.Position.StartsWith("Inferior", StringComparison.OrdinalIgnoreCase) || _settings.Position == "Inferior" ? 1d : .5d;
        HudShell.RenderTransformOrigin = new Point(horizontalAnchor, verticalAnchor);
        // A escala de preferência não pode encurtar a HUD completa: ela deve ocupar a largura útil inteira.
        var horizontalScale = _settings.LayoutMode == "Completa" ? 1.0 : scale;
        HudShell.RenderTransform = new System.Windows.Media.ScaleTransform(horizontalScale, scale);
        PositionOverlay();
    }

    // ------------------------------------------------------------------
    // Paleta da HUD (congelada: criada uma única vez, sem custo por frame)
    // ------------------------------------------------------------------
    private static readonly Wm.Brush Green = Solid("#4EE59B");
    private static readonly Wm.Brush Amber = Solid("#E7B84A");
    private static readonly Wm.Brush Red = Solid("#FF6262");
    private static readonly Wm.Brush Cyan = Solid("#35C7F4");
    private static readonly Wm.Brush Blue = Solid("#4DA8FF");
    private static readonly Wm.Brush Bright = Solid("#F4F7FA");
    private static readonly Wm.Brush Muted = Solid("#6B7785");
    private const double BarWidth = 104;
    private static readonly CultureInfo PtBr = new("pt-BR");

    private static Wm.SolidColorBrush Solid(string hex)
    {
        var brush = new Wm.SolidColorBrush((Wm.Color)Wm.ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // Verde até 70%, âmbar até 90%, vermelho acima disso (ou quando o jogo acusa alerta).
    private static Wm.Brush HeatBrush(double ratio, bool warning) =>
        warning || ratio >= .9 ? Red : ratio >= .7 ? Amber : Green;

    private static void SetBar(Border fill, double ratio, bool warning)
    {
        var r = double.IsNaN(ratio) ? 0 : Math.Clamp(ratio, 0, 1);
        fill.Width = BarWidth * r;
        fill.Background = HeatBrush(r, warning);
    }

    private static Wm.Brush DamageBrush(double percent) =>
        percent >= 25 ? Red : percent >= 10 ? Amber : Green;

    private static string FormatMinutes(int totalMinutes) =>
        totalMinutes >= 60 ? $"{totalMinutes / 60}h {totalMinutes % 60:00}min" : $"{totalMinutes}min";

    private void SetConnected(bool connected, string tooltip)
    {
        ConnectionText.Foreground = connected ? Green : Muted;
        ConnectionText.ToolTip = tooltip;
    }

    public void ShowDisconnected()
    {
        TruckText.Text = "Aguardando caminhão";
        CargoText.Text = "Sem contrato";
        RestText.Text = "—";
        RemainingText.Text = "—";
        EtaText.Text = "—";

        SpeedText.Text = "—";
        SpeedText.Foreground = Cyan;
        SpeedUnitText.Text = "km/h";
        SpeedLimitText.Text = "";
        GearClusterText.Text = "—";
        RpmText.Text = "";
        CruiseText.Visibility = Visibility.Collapsed;
        WaterText.Text = "—";
        BrakeText.Text = "—";
        WaterBar.Width = 0;
        BrakeBar.Width = 0;
        FuelClusterText.Text = "— L";
        RangeText.Text = "";

        RouteText.Text = "Aguardando telemetria do simulador";
        ValueText.Text = "—";
        DeadlineText.Text = "—";
        DeadlineText.Foreground = Bright;
        MarketBadge.Visibility = Visibility.Collapsed;
        ProgressFill.Width = 0;
        DmgTruckText.Text = "—"; DmgTruckText.Foreground = Bright;
        DmgTrailerText.Text = "—"; DmgTrailerText.Foreground = Bright;
        DmgCargoText.Text = "—"; DmgCargoText.Foreground = Bright;
        AxleTruckText.Text = "—";
        AxleTrailerText.Text = "—";
        TripKmText.Text = "0.0";
        OdometerText.Text = "0.0";
        ScaleCargoText.Text = "Peso da carga indisponível";
        ScaleAxlesText.Text = "—";
        WeighGroup.Visibility = Visibility.Collapsed;
        AxleInfoGroup.Visibility = Visibility.Visible;

        SetConnected(false, "Sem jogo conectado");
        ApplyLayoutMode();

        // A visibilidade pertence ao controlador da HUD (F11/configuração).
        // Atualizar o estado desconectado nunca deve reabrir uma HUD ocultada.
    }

    public TelemetryOverlayWindow()
    {
        InitializeComponent();
        _popupTimer.Tick += (_, _) => { _popupTimer.Stop(); EventPopup.Visibility = Visibility.Collapsed; try { _eventWindow?.Close(); } catch { } _eventWindow = null; _eventVisible = false; ShowNextEvent(); };
        Loaded += (_, _) =>
        {
            MakeClickThrough();
            PositionAtTop();
        };
        SizeChanged += (_, _) => PositionAtTop();
    }

    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive, float tripStartOdometer, float plannedDistanceKm)
    {
        var tripKm = tripActive ? Math.Max(0, data.OdometerKm - tripStartOdometer) : 0;
        var planned = plannedDistanceKm > 0
            ? plannedDistanceKm
            : data.RouteDistanceKm > 0
                ? Math.Max(1f, tripKm + data.RouteDistanceKm)
                : tripKm;
        var progress = tripActive && planned > 0 ? Math.Clamp((tripKm / planned) * 100.0, 0, 100) : 0;
        var hasCargo = !string.IsNullOrWhiteSpace(data.Cargo);

        var isAts = string.Equals(data.Game, "ATS", StringComparison.OrdinalIgnoreCase);
        var speedFactor = isAts ? 0.6213711922 : 1.0;   // km -> mi no ATS
        var speedUnit = isAts ? "mph" : "km/h";
        var distUnit = isAts ? "mi" : "km";

        // ---------- ESQUERDA ----------
        var truckName = $"{Display(data.TruckBrand, "")} {Display(data.TruckModel, "")}".Trim();
        TruckText.Text = string.IsNullOrWhiteSpace(truckName) ? "Não identificado" : truckName;
        CargoText.Text = !hasCargo
            ? "Sem contrato"
            : data.CargoMassKg > 0 ? $"{data.Cargo} ({data.CargoMassKg / 1000f:0.0}t)" : data.Cargo!;
        RestText.Text = data.RestStopMinutes > 0 ? FormatMinutes(data.RestStopMinutes) : "—";

        var remainingKm = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : Math.Max(0, planned - tripKm);
        RemainingText.Text = remainingKm > 0 ? $"{remainingKm * speedFactor:0} {distUnit}" : "—";

        // O ETS2 já fornece o tempo restante da rota (GPS do jogo): é o ETA correto,
        // pois não varia com a velocidade instantânea (semáforos, pedágios etc.).
        var etaMinutes = data.RouteTimeSeconds > 0 ? Math.Max(1, (int)Math.Ceiling(data.RouteTimeSeconds / 60d)) : 0;
        EtaText.Text = etaMinutes > 0
            ? $"{etaMinutes / 60}h {etaMinutes % 60:00}m ({DateTime.Now.AddMinutes(etaMinutes):HH:mm})"
            : "—";

        // ---------- CENTRO ----------
        var speed = Math.Abs(data.SpeedKph) * speedFactor;
        SpeedText.Text = $"{speed:0}";
        SpeedUnitText.Text = speedUnit;
        SpeedLimitText.Text = data.SpeedLimitKph > 0 ? $"(Via: {data.SpeedLimitKph * speedFactor:0} {speedUnit})" : "";
        SpeedText.Foreground = data.SpeedLimitKph > 0 && Math.Abs(data.SpeedKph) > data.SpeedLimitKph + 3 ? Red : Cyan;
        GearClusterText.Text = data.Gear == 0 ? "N" : data.Gear < 0 ? "R" : data.Gear.ToString();
        RpmText.Text = $"{Math.Max(0, data.Rpm):0} rpm";
        CruiseText.Text = data.CruiseControl ? $"CC {Math.Max(0, data.CruiseSpeedKph) * speedFactor:0}" : "";
        CruiseText.Visibility = data.CruiseControl ? Visibility.Visible : Visibility.Collapsed;

        var water = Math.Max(0, data.WaterTemperature);
        var waterLimit = data.WaterTemperatureWarningLimit > 0 ? data.WaterTemperatureWarningLimit : 110;
        WaterText.Text = water > 0 ? $"{water:0.0}°C" : "—";
        SetBar(WaterBar, water / waterLimit, data.WaterTemperatureWarning);

        var brake = Math.Max(0, data.BrakeTemperature);
        BrakeText.Text = brake > 0 ? $"{brake:0.0}°C" : "—";
        SetBar(BrakeBar, brake / 300.0, false);

        FuelClusterText.Text = $"{Math.Max(0, data.FuelLiters):0} L";
        RangeText.Text = data.FuelRangeKm > 0 ? $"({data.FuelRangeKm * speedFactor:0} {distUnit})" : "";
        FuelClusterText.Foreground = data.FuelWarning ? Red : Bright;

        // ---------- DIREITA ----------
        var origin = string.IsNullOrWhiteSpace(data.SourceCity) ? "Origem" : data.SourceCity;
        var destination = string.IsNullOrWhiteSpace(data.DestinationCity) ? "Destino" : data.DestinationCity;
        RouteText.Text = tripActive || hasCargo ? $"{origin} → {destination}" : "Aguardando nova viagem";
        if (!_settings.ShowCompanies)
            RouteText.ToolTip = null;
        else
            RouteText.ToolTip = tripActive || hasCargo
                ? $"{Display(data.SourceCompany, "Empresa de origem")} → {Display(data.DestinationCompany, "Empresa de destino")}"
                : null;

        ValueText.Text = data.CargoValueBrl is > 0 ? $"R$ {data.CargoValueBrl.Value.ToString("N0", PtBr)}" : "—";

        // Prazo: DeliveryTimeAbs e TimeAbsMinutes usam o relógio do jogo (minutos absolutos).
        if (hasCargo && data.DeliveryTimeAbs > 0 && data.TimeAbsMinutes > 0)
        {
            if (data.DeliveryTimeAbs > data.TimeAbsMinutes)
            {
                var left = (int)(data.DeliveryTimeAbs - data.TimeAbsMinutes);
                DeadlineText.Text = left >= 1440 ? $"{left / 1440}d {left % 1440 / 60}h" : FormatMinutes(left);
                DeadlineText.Foreground = left < 120 ? Red : left < 360 ? Amber : Bright;
            }
            else
            {
                DeadlineText.Text = "Atrasada";
                DeadlineText.Foreground = Red;
            }
        }
        else
        {
            DeadlineText.Text = "—";
            DeadlineText.Foreground = Bright;
        }

        MarketText.Text = (data.JobMarket ?? "").Replace('_', ' ').Trim().ToUpperInvariant();
        MarketBadge.Visibility = hasCargo && !string.IsNullOrWhiteSpace(MarketText.Text) ? Visibility.Visible : Visibility.Collapsed;

        var progressTrackWidth = ProgressTrack.ActualWidth;
        ProgressFill.Width = progressTrackWidth > 0 ? progressTrackWidth * (progress / 100.0) : 0;

        var truckWear = (data.WearEngine + data.WearTransmission + data.WearCabin + data.WearChassis + data.WearWheels) / 5.0 * 100.0;
        DmgTruckText.Text = $"{truckWear:0}%";
        DmgTruckText.Foreground = DamageBrush(truckWear);
        var cargoDamage = Math.Max(0, data.CargoDamage) * 100.0;
        DmgCargoText.Text = hasCargo ? $"{cargoDamage:0}%" : "—";
        DmgCargoText.Foreground = hasCargo ? DamageBrush(cargoDamage) : Bright;
        // A telemetria atual não informa o desgaste do reboque separadamente.
        DmgTrailerText.Text = "—";
        DmgTrailerText.Foreground = Muted;

        // ---------- EXTREMO DIREITO ----------
        var attachedTrailers = data.Trailers?.Count(t => t.Attached) ?? 0;
        var roadCombination = RoadCombinationTelemetry.Build(data);
        var showAtsScale = isAts && _settings.ShowScale;
        WeighGroup.Visibility = showAtsScale ? Visibility.Visible : Visibility.Collapsed;
        AxleInfoGroup.Visibility = showAtsScale ? Visibility.Collapsed : Visibility.Visible;
        if (showAtsScale)
        {
            // SCS fornece a massa da carga (job.cargo_mass), não o peso bruto medido na balança.
            // ATS usa unidades imperiais na HUD: convertemos kg para lb sem inventar tara do veículo.
            ScaleCargoText.Text = data.CargoMassKg > 0
                ? $"{data.CargoMassKg * 2.20462262:N0} lb de carga"
                : "Sem massa de carga disponível";
            ScaleAxlesText.Text = roadCombination.TotalAxleCount.HasValue
                ? $"{roadCombination.TotalAxleCount.Value} eixos"
                : "Eixos não identificados";
        }
        AxleTruckText.Text = data.LiftAxle ? "Levantado" : "Baixado";
        AxleTruckText.Foreground = data.LiftAxle ? Amber : Blue;
        AxleTrailerText.Text = attachedTrailers > 0 ? (data.TrailerLiftAxle ? "Levantado" : "Baixado") : "—";
        AxleTrailerText.Foreground = attachedTrailers > 0 && data.TrailerLiftAxle ? Amber : Blue;
        TripKmText.Text = $"{tripKm * speedFactor:0.0}";
        OdometerText.Text = $"{data.OdometerKm * speedFactor:0}";

        var gameLabel = isAts ? "ATS" : "ETS2";
        SetConnected(data.Connected, data.Connected
            ? $"{gameLabel} conectado • {(tripActive ? "em viagem" : hasCargo ? "carga detectada" : "disponível")}"
            : $"{gameLabel} desconectado");

        ApplyLayoutMode();
    }

    private void ApplyLayoutMode()
    {
        var minimal = _settings.LayoutMode == "Minimalista";
        var compact = _settings.LayoutMode == "Compacta";
        var area = SystemParameters.WorkArea;
        var narrow = area.Width < 1500;

        // Completa: faixa larga com 4 blocos. Compacta: sem o bloco de eixos e mais curta.
        // Minimalista: somente o bloco central (velocidade + marcha).
        Width = minimal ? 520 : compact ? Math.Min(1500, Math.Max(1000, area.Width * .82)) : Math.Max(640, area.Width - 12);
        Height = minimal ? 54 : compact ? 74 : 78;

        var showAxles = !minimal && !compact && !narrow;
        HudRoot.ColumnDefinitions[0].Width = minimal ? new GridLength(0) : new GridLength(4, GridUnitType.Star);
        HudRoot.ColumnDefinitions[1].Width = minimal ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        HudRoot.ColumnDefinitions[2].Width = minimal ? new GridLength(0) : new GridLength(5, GridUnitType.Star);
        HudRoot.ColumnDefinitions[3].Width = showAxles ? GridLength.Auto : new GridLength(0);

        LeftPanel.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        RightPanel.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        AxlesPanel.Visibility = showAxles ? Visibility.Visible : Visibility.Collapsed;
        // O último painel visível não precisa da margem lateral entre blocos.
        RightPanel.Margin = showAxles ? new Thickness(0, 0, 6, 0) : new Thickness(0);
        CenterPanel.Margin = minimal ? new Thickness(0) : new Thickness(0, 0, 6, 0);

        // Opções do painel de configurações
        ConnectionText.Visibility = _settings.ShowConnection ? Visibility.Visible : Visibility.Collapsed;
        CargoGroup.Visibility = _settings.ShowCargo ? Visibility.Visible : Visibility.Collapsed;
        EtaGroup.Visibility = _settings.ShowEta ? Visibility.Visible : Visibility.Collapsed;
        RouteGroup.Visibility = _settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed;
        ProgressFill.Visibility = _settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        TripGroup.Visibility = _settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed;
        OdoGroup.Visibility = _settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed;

        SpeedText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        SpeedUnitText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        SpeedLimitText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        GearBadge.Visibility = _settings.ShowGear ? Visibility.Visible : Visibility.Collapsed;
        RpmText.Visibility = !minimal && !compact && _settings.ShowRpm ? Visibility.Visible : Visibility.Collapsed;

        TempRow.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        FuelItem.Visibility = _settings.ShowFuel && !narrow ? Visibility.Visible : Visibility.Collapsed;
        RangeText.Visibility = _settings.ShowRange ? Visibility.Visible : Visibility.Collapsed;

        PositionOverlay();
    }

    public void ShowEvent(string message, bool critical = false)
    {
        if (!_settings.ShowAlerts || string.IsNullOrWhiteSpace(message)) return;
        var normalized=message.Trim();
        var now=DateTime.UtcNow;
        if(_recentEvents.TryGetValue(normalized,out var last)&&now-last<TimeSpan.FromSeconds(20))return;
        _recentEvents[normalized]=now;
        foreach(var stale in _recentEvents.Where(x=>now-x.Value>TimeSpan.FromMinutes(2)).Select(x=>x.Key).ToArray())_recentEvents.Remove(stale);
        if (_eventQueue.Count >= 5) _eventQueue.Dequeue();
        _eventQueue.Enqueue((normalized,critical));
        ShowNextEvent();
    }

    private void ShowNextEvent()
    {
        if (_eventVisible || _eventQueue.Count == 0 || !_settings.ShowAlerts) return;
        _eventVisible = true;
        var next = _eventQueue.Dequeue();
        var message = next.Message;
        var critical = next.Critical;
        EventPopup.Visibility = Visibility.Collapsed;
        try { _eventWindow?.Close(); } catch { }
        var area = SystemParameters.WorkArea;
        var popup = new Window
        {
            Width = Math.Min(620, Math.Max(320, area.Width * 0.34)),
            Height = critical ? 82 : 72,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            Left = area.Left + (area.Width - Math.Min(620, Math.Max(320, area.Width * 0.34))) / 2,
            Top = area.Top + Math.Max(36, area.Height * 0.20)
        };
        popup.Content = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(245, 9, 14, 19)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(critical ? System.Windows.Media.Color.FromRgb(255, 98, 98) : System.Windows.Media.Color.FromRgb(216, 169, 46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 14, 22, 14),
            Child = new TextBlock { Text = message, Foreground = System.Windows.Media.Brushes.White, FontSize = critical ? 14 : 13, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }
        };
        popup.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(popup).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style | WsExTransparent | WsExNoactivate | WsExToolwindow));
        };
        _eventWindow = popup;
        popup.Show();
        _popupTimer.Interval=critical?TimeSpan.FromSeconds(4):TimeSpan.FromSeconds(2.6);
        _popupTimer.Stop();
        _popupTimer.Start();
    }

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void PositionAtTop() => PositionOverlay();

    private void PositionOverlay()
    {
        // Usa a área física da tela em vez de WorkArea: no modo "Inferior" a HUD
        // pode encostar na borda real do jogo, sem ficar elevada pela barra do Windows.
        var area = new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        var edge = 2d;
        var maxX = Math.Max(0, area.Width - Width);
        var maxY = Math.Max(0, area.Height - Height - edge);
        if (_settings.UseCustomPosition || _settings.Position == "Personalizado")
        {
            Left = area.Left + maxX * Math.Clamp(_settings.CustomX, 0, 1);
            Top = area.Top + maxY * Math.Clamp(_settings.CustomY, 0, 1);
            return;
        }
        var (x, y) = _settings.Position switch
        {
            "Superior esquerdo" => (0d, 0d), "Topo" => (.5d, 0d), "Superior direito" => (1d, 0d),
            "Centro esquerdo" => (0d, .5d), "Centro" => (.5d, .5d), "Centro direito" => (1d, .5d),
            "Inferior esquerdo" => (0d, 1d), "Inferior" => (.5d, 1d), "Inferior direito" => (1d, 1d),
            _ => (.5d, .08d)
        };
        Left = area.Left + maxX * x;
        Top = area.Top + maxY * y;
    }

    public void MoveCustomPosition(double normalizedX, double normalizedY)
    {
        _settings.UseCustomPosition = true;
        _settings.Position = "Personalizado";
        _settings.CustomX = Math.Clamp(normalizedX, 0, 1);
        _settings.CustomY = Math.Clamp(normalizedY, 0, 1);
        PositionOverlay();
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style | WsExTransparent | WsExNoactivate | WsExToolwindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}