using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
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

    public void ShowDisconnected()
    {
        StateText.Text = " • AGUARDANDO";
        StateText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        TripKmText.Text = "0.0";
        OdometerText.Text = "0.0";
        SpeedText.Text = "—";
        SpeedUnitText.Text = " KM/H";
        GearClusterText.Text = "—";
        FuelClusterText.Text = "— L";
        RpmText.Text = "— RPM";
        RangeText.Text = "AUTONOMIA —";
        RouteText.Text = "Aguardando telemetria do ETS2";
        CompaniesText.Text = "Conecte o jogo para carregar rota e dados do caminhão";
        ProgressFill.Width = 0;
        ConnectionText.Text = "● ETS2 DESCONECTADO";
        ConnectionText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        HudShell.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3A4652"));
        FinanceText.Text = "";
        FinanceText.Visibility = Visibility.Collapsed;
        OperationalText.Text = "AGUARDANDO OPERAÇÃO"; EtaText.Text = "ETA —"; FuelText.Text = "COMBUSTÍVEL —"; GearText.Text = "MARCHA —";

        TripKmText.Visibility = _settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed;
        OdometerText.Visibility = _settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed;
        SpeedText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        RouteText.Visibility = _settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed;
        CompaniesText.Visibility = (_settings.ShowCompanies || _settings.ShowCargo) ? Visibility.Visible : Visibility.Collapsed;
        ProgressFill.Visibility = _settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        ProgressTrack.Visibility = ProgressFill.Visibility;
        ConnectionText.Visibility = _settings.ShowConnection ? Visibility.Visible : Visibility.Collapsed;
        RpmText.Visibility = _settings.ShowRpm ? Visibility.Visible : Visibility.Collapsed;
        RangeText.Visibility = _settings.ShowRange ? Visibility.Visible : Visibility.Collapsed;
        SpeedUnitText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        GearClusterText.Visibility = _settings.ShowGear ? Visibility.Visible : Visibility.Collapsed;
        FuelClusterText.Visibility = _settings.ShowFuel ? Visibility.Visible : Visibility.Collapsed;
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

    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive, float tripStartOdometer, float plannedDistanceKm, decimal revenue = 0, decimal expenses = 0, decimal net = 0)
    {
        var tripKm = tripActive ? Math.Max(0, data.OdometerKm - tripStartOdometer) : 0;
        var planned = plannedDistanceKm > 0
            ? plannedDistanceKm
            : data.RouteDistanceKm > 0
                ? Math.Max(1f, tripKm + data.RouteDistanceKm)
                : tripKm;
        var progress = tripActive && planned > 0 ? Math.Clamp((tripKm / planned) * 100.0, 0, 100) : 0;

        var hasCargo = !string.IsNullOrWhiteSpace(data.Cargo);
        StateText.Text = tripActive ? " • EM VIAGEM" : hasCargo ? " • CARGA DETECTADA" : " • DISPONÍVEL";
        StateText.Foreground = FindResource(tripActive ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        TripKmText.Text = $"{tripKm:0.0}";
        OdometerText.Text = $"{data.OdometerKm:0.0}";
        SpeedText.Text = $"{Math.Abs(data.SpeedKph):0}";
        SpeedUnitText.Text = " KM/H";
        GearClusterText.Text = data.Gear == 0 ? "N" : data.Gear < 0 ? "R" : data.Gear.ToString();
        FuelClusterText.Text = $"{Math.Max(0, data.FuelLiters):0} L";
        TripKmText.Visibility = _settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed;
        OdometerText.Visibility = _settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed;
        SpeedText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        RpmText.Visibility = _settings.ShowRpm ? Visibility.Visible : Visibility.Collapsed;
        RangeText.Visibility = _settings.ShowRange ? Visibility.Visible : Visibility.Collapsed;
        RpmText.Text = $"{data.Rpm:0} RPM";
        RangeText.Text = data.FuelRangeKm > 0 ? $"AUTONOMIA {data.FuelRangeKm:0} KM" : "AUTONOMIA —";

        var origin = string.IsNullOrWhiteSpace(data.SourceCity) ? "Origem" : data.SourceCity;
        var destination = string.IsNullOrWhiteSpace(data.DestinationCity) ? "Destino" : data.DestinationCity;
        RouteText.Text = tripActive || hasCargo ? $"{origin}  →  {destination}" : "Aguardando nova viagem";
        CompaniesText.Text = tripActive || hasCargo
            ? $"{Display(data.SourceCompany, "Empresa de origem")}  →  {Display(data.DestinationCompany, "Empresa de destino")}" + (hasCargo ? $"  •  {data.Cargo}" : "")
            : "TransPoli pronto para a próxima operação";
        RouteText.Visibility = _settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed;
        CompaniesText.Visibility = _settings.ShowCompanies || _settings.ShowCargo ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.ShowCompanies && _settings.ShowCargo) CompaniesText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "" : data.Cargo;
        else if (_settings.ShowCompanies && !_settings.ShowCargo) CompaniesText.Text = $"{Display(data.SourceCompany, "Empresa de origem")}  →  {Display(data.DestinationCompany, "Empresa de destino")}";

        var progressTrackWidth = ProgressTrack.ActualWidth;
        ProgressFill.Width = progressTrackWidth > 0 ? progressTrackWidth * (progress / 100.0) : 0;
        ProgressFill.Visibility = _settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        ConnectionText.Text = data.Connected ? "● ETS2 CONECTADO" : "● ETS2 DESCONECTADO";
        HudShell.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(data.Connected ? "#D6A52A" : "#3A4652"));
        ConnectionText.Foreground = FindResource(data.Connected ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        ConnectionText.Visibility = _settings.ShowConnection ? Visibility.Visible : Visibility.Collapsed;
        var finance = new System.Collections.Generic.List<string>();
        if (_settings.ShowProfit) finance.Add($"RECEITA R$ {revenue:0.00} • LÍQUIDO R$ {net:0.00}");
        if (_settings.ShowExpenses) finance.Add($"DESPESAS R$ {expenses:0.00}");
        FinanceText.Text = string.Join("  •  ", finance);
        FinanceText.Visibility = finance.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        OperationalText.Text = tripActive ? "● VIAGEM ATIVA" : hasCargo ? "● CARGA DETECTADA" : "● DISPONÍVEL";
        OperationalText.Visibility = _settings.ShowTripState ? Visibility.Visible : Visibility.Collapsed;
        var remainingKm = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : Math.Max(0, planned - tripKm);
        var speed = Math.Abs(data.SpeedKph);
        var etaMinutes = speed >= 5 && remainingKm > 0 ? (int)Math.Round(remainingKm / speed * 60d) : 0;
        EtaText.Text = etaMinutes > 0 ? $"ETA ~ {etaMinutes / 60}h {etaMinutes % 60:00}m • {remainingKm:0} km" : $"RESTANTE {remainingKm:0} km";
        EtaText.Visibility = _settings.ShowEta && tripActive ? Visibility.Visible : Visibility.Collapsed;
        // Combustível e marcha são renderizados exclusivamente no cluster.
        // Mantemos os elementos legados sem conteúdo/visibilidade para evitar
        // duplicação ao atualizar telemetria antes de ApplyLayoutMode.
        FuelText.Text = "";
        FuelText.Visibility = Visibility.Collapsed;
        GearText.Text = "";
        GearText.Visibility = Visibility.Collapsed;
        ApplyLayoutMode();
    }

    private void ApplyLayoutMode()
    {
        var minimal = _settings.LayoutMode == "Minimalista";
        var compact = _settings.LayoutMode == "Compacta";

        // Cada preset funciona como um instrumento diferente, e não como a mesma HUD
        // com campos escondidos. Completa = central de operação; Compacta = faixa de
        // condução; Minimalista = instrumento essencial de velocidade/estado.
        // Faixa horizontal baixa: ocupa largura útil sem cobrir o para-brisa.
        var area = SystemParameters.WorkArea;
        // A HUD completa agora é uma faixa inferior realmente longa e baixa, como
        // um instrumento de condução. Em telas 16:9 usa quase toda a largura útil.
        Width = minimal ? Math.Min(900, area.Width * .48) : compact ? Math.Min(1580, area.Width * .78) : Math.Max(640, area.Width - 12);
        Height = minimal ? 48 : compact ? 58 : 82;
        HudShell.CornerRadius = new CornerRadius(minimal ? 9 : compact ? 11 : 12);
        HudShell.BorderThickness = new Thickness(minimal ? 0.8 : compact ? 1.0 : 1.0);
        TelemetryClusterShell.CornerRadius = new CornerRadius(minimal ? 9 : compact ? 10 : 11);
        TelemetryClusterShell.Padding = minimal ? new Thickness(10, 3, 10, 3) : compact ? new Thickness(12, 5, 12, 5) : new Thickness(12, 3, 12, 3);
        HudRoot.Margin = minimal
            ? new Thickness(14, 3, 14, 3)
            : compact
                ? new Thickness(16, 4, 16, 4)
                : new Thickness(14, 2, 14, 2);

        HudRoot.ColumnDefinitions[0].Width = minimal ? new GridLength(0) : compact ? new GridLength(250) : new GridLength(300);
        HudRoot.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        HudRoot.ColumnDefinitions[2].Width = minimal ? new GridLength(300) : compact ? new GridLength(410) : new GridLength(500);
        HudRoot.RowDefinitions[1].Height = minimal ? new GridLength(0) : GridLength.Auto;
        HudRoot.RowDefinitions[2].Height = new GridLength(0);

        IdentityPanel.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        TelemetryClusterShell.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(minimal ? "#9907090C" : "#B30E1217"));
        TelemetryClusterShell.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(minimal ? "#26313B" : "#3A4652"));
        RoutePanel.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        RoutePanel.MaxWidth = double.PositiveInfinity;
        RouteText.FontSize = compact ? 10.5 : 11;
        CompaniesText.FontSize = compact ? 8.5 : 9;
        ProgressTrack.Margin = compact ? new Thickness(0, 5, 0, 0) : new Thickness(0, 7, 0, 0);
        FooterPanel.Visibility = minimal || compact ? Visibility.Collapsed : Visibility.Visible;
        TelemetryPanel.Visibility = Visibility.Visible;
        OperationPanel.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(OperationPanel, minimal ? 0 : 1);
        Grid.SetColumn(OperationPanel, minimal ? 0 : 0);
        Grid.SetColumnSpan(OperationPanel, 3);
        OperationPanel.VerticalAlignment = VerticalAlignment.Center;
        OperationPanel.Margin = minimal ? new Thickness(8, 0, 8, 0) : new Thickness(0, 1, 0, 0);

        RouteText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowRoute ? Visibility.Visible : Visibility.Collapsed);
        CompaniesText.Visibility = minimal || compact ? Visibility.Collapsed : ((_settings.ShowCompanies || _settings.ShowCargo) ? Visibility.Visible : Visibility.Collapsed);
        ProgressFill.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowProgress ? Visibility.Visible : Visibility.Collapsed);
        ProgressTrack.Visibility = ProgressFill.Visibility;
        TripKmText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowTripKm ? Visibility.Visible : Visibility.Collapsed);
        OdometerText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowOdometer ? Visibility.Visible : Visibility.Collapsed);
        RpmText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowRpm ? Visibility.Visible : Visibility.Collapsed);
        SpeedUnitText.Visibility = _settings.ShowSpeed ? Visibility.Visible : Visibility.Collapsed;
        GearClusterText.Visibility = _settings.ShowGear ? Visibility.Visible : Visibility.Collapsed;
        FuelClusterText.Visibility = _settings.ShowFuel ? Visibility.Visible : Visibility.Collapsed;
        RangeText.Visibility = _settings.ShowRange ? Visibility.Visible : Visibility.Collapsed;
        FinanceText.Visibility = minimal || compact ? Visibility.Collapsed : FinanceText.Visibility;
        ConnectionText.Visibility = minimal || compact ? Visibility.Collapsed : (_settings.ShowConnection ? Visibility.Visible : Visibility.Collapsed);
        OperationalText.Visibility = minimal ? Visibility.Collapsed : (_settings.ShowTripState ? Visibility.Visible : Visibility.Collapsed);
        EtaText.Visibility = minimal ? Visibility.Collapsed : EtaText.Visibility;
        // Combustível e marcha já pertencem ao cluster principal. As linhas
        // legadas duplicavam a mesma leitura e engrossavam a HUD.
        FuelText.Visibility = Visibility.Collapsed;
        GearText.Visibility = Visibility.Collapsed;

        // Minimalista: velocidade, marcha e combustível dominam como um pequeno
        // cluster digital. Compacta mantém RPM + velocidade + operação em uma faixa.
        SpeedText.FontSize = minimal ? 24 : compact ? 22 : 20;
        SpeedText.FontWeight = FontWeights.Bold;
        RpmText.FontSize = compact ? 9 : 9;
        GearClusterText.FontSize = minimal ? 24 : compact ? 22 : 20;
        FuelClusterText.FontSize = minimal ? 16 : compact ? 17 : 17;
        RangeText.FontSize = minimal ? 8 : 9;
        GearText.FontSize = minimal ? 12 : compact ? 12 : 12;
        FuelText.FontSize = minimal ? 12 : compact ? 12 : 12;
        EtaText.FontSize = compact ? 10 : 9.5;
        TelemetryPanel.HorizontalAlignment = minimal ? HorizontalAlignment.Stretch : HorizontalAlignment.Stretch;
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
        var message = _eventQueue.Dequeue();
        EventPopup.Visibility = Visibility.Collapsed;
        try { _eventWindow?.Close(); } catch { }
        var area = SystemParameters.WorkArea;
        var popup = new Window
        {
            Width = Math.Min(620, Math.Max(420, area.Width * 0.34)),
            Height = 78,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            Left = area.Left + (area.Width - Math.Min(620, Math.Max(420, area.Width * 0.34))) / 2,
            Top = area.Top + (area.Height - 78) / 2
        };
        popup.Content = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(245, 9, 14, 19)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 169, 46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 14, 22, 14),
            Child = new TextBlock { Text = message, Foreground = System.Windows.Media.Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }
        };
        popup.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(popup).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
            SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style | WsExTransparent | WsExNoactivate | WsExToolwindow));
        };
        _eventWindow = popup;
        popup.Show();
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