using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TransPoli;

public partial class TelemetryOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x80;
    private readonly DispatcherTimer _popupTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Queue<string> _eventQueue = new();
    private bool _eventVisible;
    private HudSettings _settings = new();

    public void ApplySettings(HudSettings settings)
    {
        _settings = settings;
        ApplyVisualSettings();

        if (settings.Enabled)
            Show();
        else
            Hide();
    }

    public void ApplyVisualSettings()
    {
        Opacity = Math.Clamp(_settings.Opacity, 0.35, 1.0);
        var scale = Math.Clamp(_settings.Scale, 0.40, 2.00);
        HudShell.RenderTransformOrigin = new Point(0, 0);
        HudShell.RenderTransform = new System.Windows.Media.ScaleTransform(scale, scale);
        PositionOverlay();
    }

    public void ShowDisconnected()
    {
        StateText.Text = " • AGUARDANDO";
        StateText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        TripKmText.Text = "0.0";
        OdometerText.Text = "0.0";
        SpeedText.Text = "N/D";
        RpmText.Text = "N/D";
        RangeText.Text = "N/D";
        RouteText.Text = "Aguardando telemetria do ETS2";
        CompaniesText.Text = "Conecte o jogo para carregar rota e dados do caminhão";
        ProgressFill.Width = 0;
        ConnectionText.Text = "● SEM TELEMETRIA";
        ConnectionText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        FinanceText.Text = "";
        FinanceText.Visibility = Visibility.Collapsed;
        OperationalText.Text = "AGUARDANDO OPERAÇÃO"; EtaText.Text = "ETA —"; FuelText.Text = "COMBUSTÍVEL —"; GearText.Text = "MARCHA —";

        TripKmText.Visibility = _settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed;
        OdometerText.Visibility = _settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed;
        SpeedText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        RouteText.Visibility = _settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed;
        CompaniesText.Visibility = (_settings.ShowCompanies || _settings.ShowCargo) ? Visibility.Visible : Visibility.Collapsed;
        ProgressFill.Visibility = _settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        ApplyLayoutMode();

        if (_settings.Enabled)
        {
            ApplyVisualSettings();
            if (!IsVisible) Show();
        }
        else
        {
            Hide();
        }
    }

    public TelemetryOverlayWindow()
    {
        InitializeComponent();
        _popupTimer.Tick += (_, _) => { _popupTimer.Stop(); EventPopup.Visibility = Visibility.Collapsed; _eventVisible = false; ShowNextEvent(); };
        Loaded += (_, _) =>
        {
            MakeClickThrough();
            PositionAtTop();
        };
        SizeChanged += (_, _) => PositionAtTop();
    }

    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive, float tripStartOdometer, float plannedDistanceKm, decimal revenue = 0, decimal expenses = 0, decimal net = 0)
    {
        var tripKm = tripActive ? Math.Max(0, data.OdometerKm - tripStartOdometer) : 0;
        var planned = plannedDistanceKm > 0
            ? plannedDistanceKm
            : data.RouteDistanceKm > 0
                ? Math.Max(1f, tripKm + data.RouteDistanceKm)
                : tripKm;
        var progress = tripActive && planned > 0 ? Math.Clamp((tripKm / planned) * 100.0, 0, 100) : 0;

        StateText.Text = tripActive ? " • EM VIAGEM" : " • AGUARDANDO";
        StateText.Foreground = FindResource(tripActive ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        TripKmText.Text = $"{tripKm:0.0}";
        OdometerText.Text = $"{data.OdometerKm:0.0}";
        SpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";
        TripKmText.Visibility = _settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed;
        OdometerText.Visibility = _settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed;
        SpeedText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        RpmText.Visibility = _settings.ShowRpm ? Visibility.Visible : Visibility.Collapsed;
        RangeText.Visibility = _settings.ShowRange ? Visibility.Visible : Visibility.Collapsed;
        RpmText.Text = $"{data.Rpm:0}";
        RangeText.Text = data.FuelRangeKm > 0 ? $"{data.FuelRangeKm:0} km" : "N/D";

        var origin = string.IsNullOrWhiteSpace(data.SourceCity) ? "Origem" : data.SourceCity;
        var destination = string.IsNullOrWhiteSpace(data.DestinationCity) ? "Destino" : data.DestinationCity;
        RouteText.Text = $"{origin}  →  {destination}";
        CompaniesText.Text = $"{Display(data.SourceCompany, "Empresa de origem")}  →  {Display(data.DestinationCompany, "Empresa de destino")}" +
                             (string.IsNullOrWhiteSpace(data.Cargo) ? "" : $"  •  {data.Cargo}");
        RouteText.Visibility = _settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed;
        CompaniesText.Visibility = _settings.ShowCompanies || _settings.ShowCargo ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.ShowCompanies && _settings.ShowCargo) CompaniesText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "" : data.Cargo;
        else if (_settings.ShowCompanies && !_settings.ShowCargo) CompaniesText.Text = $"{Display(data.SourceCompany, "Empresa de origem")}  →  {Display(data.DestinationCompany, "Empresa de destino")}";

        ProgressFill.Width = 430 * (progress / 100.0);
        ProgressFill.Visibility = _settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        ConnectionText.Text = data.Connected ? "● ETS2 CONECTADO" : "● SEM TELEMETRIA";
        ConnectionText.Foreground = FindResource(data.Connected ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        ConnectionText.Visibility = _settings.ShowConnection ? Visibility.Visible : Visibility.Collapsed;
        var finance = new System.Collections.Generic.List<string>();
        if (_settings.ShowProfit) finance.Add($"RECEITA R$ {revenue:0.00} • LÍQUIDO R$ {net:0.00}");
        if (_settings.ShowExpenses) finance.Add($"DESPESAS R$ {expenses:0.00}");
        FinanceText.Text = string.Join("  •  ", finance);
        FinanceText.Visibility = finance.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        OperationalText.Text = tripActive ? "● VIAGEM ATIVA" : (!string.IsNullOrWhiteSpace(data.Cargo) ? "● CARGA DETECTADA" : "AGUARDANDO OPERAÇÃO");
        OperationalText.Visibility = _settings.ShowTripState ? Visibility.Visible : Visibility.Collapsed;
        var remainingKm = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : Math.Max(0, planned - tripKm);
        var speed = Math.Abs(data.SpeedKph);
        var etaMinutes = speed >= 5 && remainingKm > 0 ? (int)Math.Round(remainingKm / speed * 60d) : 0;
        EtaText.Text = etaMinutes > 0 ? $"ETA ~ {etaMinutes / 60}h {etaMinutes % 60:00}m • {remainingKm:0} km" : $"RESTANTE {remainingKm:0} km";
        EtaText.Visibility = _settings.ShowEta && tripActive ? Visibility.Visible : Visibility.Collapsed;
        FuelText.Text = $"COMBUSTÍVEL {data.FuelLiters:0} L";
        FuelText.Visibility = _settings.ShowFuel ? Visibility.Visible : Visibility.Collapsed;
        GearText.Text = $"MARCHA {data.Gear}";
        GearText.Visibility = _settings.ShowGear ? Visibility.Visible : Visibility.Collapsed;
        ApplyLayoutMode();
    }

    private void ApplyLayoutMode()
    {
        var minimal = _settings.LayoutMode == "Minimalista";
        var compact = _settings.LayoutMode == "Compacta";

        // Os presets mudam a composição física da HUD, não apenas a visibilidade dos textos.
        Width = minimal ? 620 : compact ? 860 : 1120;
        Height = minimal ? 82 : compact ? 102 : 128;
        HudShell.CornerRadius = new CornerRadius(minimal ? 16 : compact ? 18 : 20);
        HudRoot.Margin = new Thickness(minimal ? 14 : compact ? 16 : 18, minimal ? 8 : compact ? 9 : 11, minimal ? 14 : compact ? 16 : 18, minimal ? 8 : compact ? 9 : 11);
        HudRoot.ColumnDefinitions[0].Width = new GridLength(minimal ? 150 : compact ? 230 : 290);
        HudRoot.ColumnDefinitions[1].Width = minimal ? new GridLength(0) : compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        HudRoot.ColumnDefinitions[2].Width = new GridLength(minimal ? 430 : compact ? 590 : 270);
        HudRoot.RowDefinitions[1].Height = minimal ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        HudRoot.RowDefinitions[2].Height = minimal || compact ? new GridLength(0) : GridLength.Auto;

        RoutePanel.Visibility = minimal || compact ? Visibility.Collapsed : Visibility.Visible;
        FooterPanel.Visibility = minimal || compact ? Visibility.Collapsed : Visibility.Visible;
        IdentityPanel.Visibility = Visibility.Visible;
        TelemetryPanel.Visibility = Visibility.Visible;
        OperationPanel.Visibility = Visibility.Visible;

        RouteText.Visibility = minimal || compact ? Visibility.Collapsed : (_settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed);
        CompaniesText.Visibility = minimal || compact ? Visibility.Collapsed : ((_settings.ShowCompanies || _settings.ShowCargo) ? Visibility.Visible : Visibility.Collapsed);
        ProgressFill.Visibility = minimal || compact ? Visibility.Collapsed : (_settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed);
        TripKmText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed);
        OdometerText.Visibility = minimal || compact ? Visibility.Collapsed : (_settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed);
        RpmText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowRpm ? Visibility.Visible : Visibility.Collapsed);
        RangeText.Visibility = minimal || compact ? Visibility.Collapsed : (_settings.ShowRange ? Visibility.Visible : Visibility.Collapsed);
        FinanceText.Visibility = minimal || compact ? Visibility.Collapsed : FinanceText.Visibility;
        ConnectionText.Visibility = minimal || compact ? Visibility.Collapsed : (_settings.ShowConnection ? Visibility.Visible : Visibility.Collapsed);
        OperationalText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowTripState ? Visibility.Visible : Visibility.Collapsed);

        // No minimalista, velocidade vira o instrumento dominante.
        SpeedText.FontSize = minimal ? 22 : compact ? 18 : 15;
        GearText.FontSize = minimal ? 13 : 11;
        FuelText.FontSize = minimal ? 13 : 11;
        EtaText.FontSize = minimal ? 13 : 11;
        PositionOverlay();
    }

    public void ShowEvent(string message)
    {
        if (!_settings.ShowAlerts || string.IsNullOrWhiteSpace(message)) return;
        if (_eventQueue.Count >= 6) _eventQueue.Dequeue();
        _eventQueue.Enqueue(message.Trim());
        ShowNextEvent();
    }

    private void ShowNextEvent()
    {
        if (_eventVisible || _eventQueue.Count == 0 || !_settings.ShowAlerts) return;
        _eventVisible = true;
        EventText.Text = _eventQueue.Dequeue();
        EventPopup.Visibility = Visibility.Visible;
        _popupTimer.Stop();
        _popupTimer.Start();
    }

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void PositionAtTop() => PositionOverlay();

    private void PositionOverlay()
    {
        var area = SystemParameters.WorkArea;
        var scale = Math.Clamp(_settings.Scale, 0.40, 2.00);
        var scaledWidth = Width * scale;
        var scaledHeight = Height * scale;
        var maxX = Math.Max(0, area.Width - scaledWidth);
        var maxY = Math.Max(0, area.Height - scaledHeight);
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