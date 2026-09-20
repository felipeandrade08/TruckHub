using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace TransPoli;

public partial class MainWindow : Window
{
    private const int HotKeyId = 0x5448;
    private const int WmHotKey = 0x0312;
    private const uint VkF10 = 0x79;
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    internal const string TelemetryUrl = "http://127.0.0.1:17877/telemetry";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _timer;
    private readonly ConnectorSupervisor _connector = new();
    private readonly LocalDataStore? _localData = null;
    private HwndSource? _source;
    private bool _refreshBusy;
    private bool _tripActive;
    private bool _truckLocked = true;
    private DateTime _tripStartedAtUtc;
    private float _tripStartOdometer;
    private float _tripStartFuel;
    private int _jobMissingTicks;
    private DateTime _lastTripFinishedAtUtc = DateTime.MinValue;
    private DateTime _lastTelemetrySentAtUtc = DateTime.MinValue;
    private bool _lastRefuelPayed;
    private DateTime _dashboardBankLastRefreshUtc = DateTime.MinValue;
    private DateTime _lastLiveTelemetrySentAtUtc = DateTime.MinValue;
    private DateTime _telemetryConnectedAtUtc = DateTime.MinValue;
    private string? _serverTripId;
    private string? _localTripId;
    private double _localTripRatePerKm;
    private DateTime _lastLocalTelemetrySavedAtUtc = DateTime.MinValue;
    private DateTime _lastServerTripSyncAttemptUtc = DateTime.MinValue;
    private long _lastProcessedTollgateEventId;
    internal TelemetrySnapshot? LastTelemetry { get; private set; }
    // Legacy bindings kept as explicit fields because the premium compatibility layer is collapsed.

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    private void UpdateTabletStatusBar(bool connected)
    {
        var data = LastTelemetry;
        if (WifiStatusText != null)
        {
            var network = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .OrderByDescending(n => n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211)
                .FirstOrDefault();
            var isWifi = network?.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211;
            var isMobile = network?.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ppp;
            WifiStatusText.Text = connected && network is not null ? (isWifi ? "Wi-Fi" : isMobile ? "4G" : "REDE") : "SEM REDE";
            WifiStatusText.Foreground = FindResource(connected && network is not null ? "Green" : "TextMuted") as System.Windows.Media.Brush;
            if (NetworkTypeText != null) NetworkTypeText.Text = connected && network is not null ? (isWifi ? "CONECTADO" : isMobile ? "DADOS MÓVEIS" : "CONECTADO") : "OFFLINE";
            if (NetworkSignalText != null) NetworkSignalText.Text = connected && network is not null ? "▂▄▆█" : "▂___";
            if (NetworkSignalText != null) NetworkSignalText.Foreground = FindResource(connected && network is not null ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        }

        if (GpsStatusText != null)
        {
            // O ETS2 não expõe um GPS de hardware do Windows; aqui o GPS representa
            // a posição/rota fornecida pela telemetria do jogo.
            var gps = connected && data is not null &&
                      (!string.IsNullOrWhiteSpace(data.SourceCity) || !string.IsNullOrWhiteSpace(data.DestinationCity));
            GpsStatusText.Text = gps ? "● GPS" : "○ GPS";
            GpsStatusText.Foreground = FindResource(gps ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        }

        if (BluetoothStatusText != null)
        {
            // Não há uma fonte real de Bluetooth no protocolo do TransPoli neste momento.
            // Portanto, não simulamos conexão: o tablet informa N/D.
            BluetoothStatusText.Text = "N/D";
            BluetoothStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            BluetoothStatusText.ToolTip = "Bluetooth • dado não disponível";
        }

        if (NotificationStatusText != null)
        {
            var warning = data is not null && (data.FuelWarning || data.AirPressureWarning ||
                                               data.AirPressureEmergency || data.OilPressureWarning ||
                                               data.WaterTemperatureWarning || data.BatteryVoltageWarning ||
                                               data.AdBlueWarning);
            NotificationStatusText.Text = "◇";
            NotificationStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            NotificationStatusText.ToolTip = warning ? "Há alertas de operação" : "Sem notificações operacionais";
        }

        if (BatteryStatusText != null && BatteryPercentText != null)
        {
            if (GetSystemPowerStatus(out var power) && power.BatteryLifePercent <= 100)
            {
                var percent = power.BatteryLifePercent;
                BatteryPercentText.Text = $"{percent}%";
                BatteryStatusText.Text = power.ACLineStatus == 1 ? "⚡" : "▰";
                BatteryStatusText.Foreground = FindResource(percent <= 20 ? "Red" : percent <= 40 ? "GoldBright" : "Green") as System.Windows.Media.Brush;
                BatteryStatusText.ToolTip = power.ACLineStatus == 1 ? "Alimentação externa conectada" : "Bateria do computador";
            }
            else
            {
                BatteryPercentText.Text = "N/D";
                BatteryStatusText.Text = "▰";
                BatteryStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
                BatteryStatusText.ToolTip = "Bateria física não disponível";
            }
        }
    }


    public MainWindow()
    {
        InitializeComponent();

        // Persistência local é inicializada antes dos módulos operacionais.
        // Nenhuma alteração visual é necessária para esta etapa.
        try
        {
            _localData = new LocalDataStore();
            // A migração dos JSON legados é executada pelo LocalDataStore durante a inicialização.
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("LocalDatabase", ex);
        }

        StartMaintenanceNavigationHook();
        StartNotificationSystem();
        LoadSessionState();

        // O cockpit deve sempre nascer visível. O F10 apenas oculta/mostra
        // depois que a janela já está carregada; ele nunca participa da abertura.
        Visibility = Visibility.Visible;
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Topmost = false;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) =>
        {
            try
            {
                UpdateDesktopClock();
                await _connector.EnsureRunningAsync();
                await RefreshTelemetry();
                await RefreshDriverCenterAsync();
            }
            catch (Exception ex)
            {
                App.WriteUiCrashLog("TelemetryTimer", ex);
            }
        };

        Loaded += MainWindow_LoadedSafe;
        Closed += MainWindow_ClosedSafe;
        _timer.Start();
    }

    private async void MainWindow_LoadedSafe(object? sender, RoutedEventArgs e)
    {
        try
        {
            RegisterGlobalHotKey();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("RegisterGlobalHotKey", ex);
        }

        try
        {
            StartUpdateWatcher();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("UpdateWatcher", ex);
        }

        try
        {
            await _connector.StartAsync();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("ConnectorStart", ex);
            StatusText.Text = "TransPoli • aguardando Connector";
        }

        try
        {
            await RefreshTelemetry();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("InitialTelemetry", ex);
            SetDisconnected();
        }

        try
        {
            Activate();
            Focus();
        }
        catch { }
    }

    private void MainWindow_ClosedSafe(object? sender, EventArgs e)
    {
        try { _timer.Stop(); } catch { }
        try { _localData?.Dispose(); } catch { }
        try { UnregisterGlobalHotKey(); } catch { }

        // Fechar pelo X deve realmente encerrar o processo; ocultar com F10
        // continua sendo apenas Hide().
        if (Application.Current is not null &&
            Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
        {
            Application.Current.Shutdown();
        }
    }

    private void RegisterGlobalHotKey()
    {
        var helper = new WindowInteropHelper(this);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
        if (!RegisterHotKey(helper.Handle, HotKeyId, 0, VkF10)) StatusText.Text = "F10 indisponível • outra aplicação pode estar usando o atalho.";
    }
    private void UnregisterGlobalHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) UnregisterHotKey(handle, HotKeyId);
        _source?.RemoveHook(WndProc); _source = null;
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == HotKeyId) { ToggleCockpit(); handled = true; }
        return IntPtr.Zero;
    }
    private void ToggleCockpit()
    {
        if (Visibility == Visibility.Visible) { Hide(); return; }
        Show(); WindowState = WindowState.Normal; Topmost = true; Topmost = false; Topmost = true;
        StatusText.Text = "Tablet TransPoli aberto • F10 para ocultar";
    }
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F10) { ToggleCockpit(); e.Handled = true; }
    }


    private async Task RefreshDashboardBankAsync()
    {
        try
        {
            if (LocalData.Current is not { } store) return;

            // O Dashboard usa o SQLite local como fonte de verdade.
            // A API continua sendo usada apenas para sincronização complementar.
            var economy = new LocalEconomyRepository(store.Db);
            var summary = economy.GetSummary();
            var loan = economy.GetActiveLoan();

            DashboardBankBalanceText.Text = $"R$ {summary.Balance:N2}";
            DashboardBankCreditsText.Text = $"R$ {summary.Credits:N2}";
            DashboardBankDebitsText.Text = $"R$ {summary.Debits:N2}";
            DashboardBankTripsText.Text = $"{summary.TripCount} pagas";

            if (loan is not null)
            {
                DashboardLoanStatusText.Text = $"R$ {loan.Principal:N0} contratado";
                DashboardLoanRemainingText.Text = $"R$ {loan.Remaining:N2}";
                DashboardLoanInstallmentsText.Text = $"{loan.InstallmentsPaid}/{loan.InstallmentsTotal} pagas";
                DashboardLoanInstallmentValueText.Text = $"R$ {loan.InstallmentMin:N2}";
                DashboardLoanInterestText.Text = $"{loan.InterestMonthlyPct:0.##}% a.m.";
                DashboardLoanTotalText.Text = $"R$ {loan.TotalPayable:N2}";
            }
            else
            {
                DashboardLoanStatusText.Text = "Nenhum ativo";
                DashboardLoanRemainingText.Text = "R$ 0,00";
                DashboardLoanInstallmentsText.Text = "—";
                DashboardLoanInstallmentValueText.Text = "—";
                DashboardLoanInterestText.Text = "—";
                DashboardLoanTotalText.Text = "—";
            }

            DashboardBankStatusText.Text =
                $"BANCO LOCAL • atualizado às {DateTime.Now:HH:mm} • offline disponível";
            _dashboardBankLastRefreshUtc = DateTime.UtcNow;
        }
        catch
        {
            DashboardBankStatusText.Text = "BANCO LOCAL • economia temporariamente indisponível";
        }
    }

    private async Task RefreshTelemetry()
    {
        if (_refreshBusy) return;
        _refreshBusy = true;
        try
        {
            using var response = await _http.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode) { SetDisconnected(); return; }
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) { SetDisconnected(); return; }
            var wasConnected = LastTelemetry?.Connected == true;
            if (!wasConnected) _telemetryConnectedAtUtc = DateTime.UtcNow;
            LastTelemetry = data;
            await ProcessTollgateEventAsync(data);
            UpdateRealInstrumentation(data);
            UpdateAutomaticTachographStatus(data);
            ConnectionText.Text = "ETS2 CONECTADO";
            ConnectionText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
            ConnectionDot.Fill = FindResource("Green") as System.Windows.Media.Brush;
            var connectedFor = _telemetryConnectedAtUtc == DateTime.MinValue ? TimeSpan.Zero : DateTime.UtcNow - _telemetryConnectedAtUtc;
            var connectedLabel = connectedFor.TotalHours >= 1 ? $"{(int)connectedFor.TotalHours}h {connectedFor.Minutes:00}min" : $"{connectedFor.Minutes:00}min {connectedFor.Seconds:00}s";
            StatusText.Text = "Telemetria conectada";
            var now = DateTime.Now;
            UpdateTabletStatusBar(true);
            ClockText.Text = now.ToString("HH:mm");
            DateText.Text = now.ToString("dd/MM/yyyy");
            TruckName.Text = string.IsNullOrWhiteSpace(data.TruckModel) ? "Caminhão detectado" : $"{data.TruckBrand} {data.TruckModel}";
            RouteText.Text = BuildRoute(data);
            SpeedText.Text = Math.Abs(data.SpeedKph).ToString("0");
            RpmText.Text = data.Rpm.ToString("0");
            GearText.Text = data.Gear == 0 ? "N" : data.Gear < 0 ? "R" : data.Gear.ToString();
            FuelText.Text = $"{data.FuelLiters:0.0} L";
            RangeText.Text = $"{data.FuelRangeKm:0} km";
            if (FuelConsumptionText != null) FuelConsumptionText.Text = data.FuelAvgConsumption > 0 ? $"Média do caminhão: {data.FuelAvgConsumption:0.00} L/100 km" : "Média do caminhão: aguardando dados";
            OdometerText.Text = $"{data.OdometerKm:0.0} km";
            CruiseText.Text = data.CruiseControl ? "ON" : "OFF";
            CargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Nenhuma carga" : data.Cargo;
            CargoMassText.Text = data.CargoMassKg > 0 ? $"{data.CargoMassKg:0} kg" : "Peso não informado";
            EngineStateText.Text = data.EngineEnabled ? "LIGADO" : "DESLIGADO";
            EngineStateText.Foreground = FindResource(data.EngineEnabled ? "Green" : "Yellow") as System.Windows.Media.Brush;
            TelemetryInfoText.Text = "ETS2 conectado • telemetria ativa";
            UpdateAutomaticLock(data);
            UpdateAutomaticTrip(data);

            // Se a viagem começou offline, tenta sincronizar o contrato automaticamente
            // assim que a sessão voltar. A API é idempotente e reaproveita a viagem ativa.
            if (_tripActive &&
                string.IsNullOrWhiteSpace(_serverTripId) &&
                !string.IsNullOrWhiteSpace(SecureTokenStore.Read()) &&
                DateTime.UtcNow - _lastServerTripSyncAttemptUtc >= TimeSpan.FromSeconds(15))
            {
                _lastServerTripSyncAttemptUtc = DateTime.UtcNow;
                await CreateServerTrip(data);
            }
            if (DateTime.UtcNow - _dashboardBankLastRefreshUtc >= TimeSpan.FromSeconds(30)) await RefreshDashboardBankAsync();
            if (DateTime.UtcNow - _lastLiveTelemetrySentAtUtc >= TimeSpan.FromSeconds(10)) await SendLiveTelemetrySample(data);
            if (_tripActive && !string.IsNullOrWhiteSpace(_localTripId) && DateTime.UtcNow - _lastLocalTelemetrySavedAtUtc >= TimeSpan.FromSeconds(2))
            {
                SaveLocalTelemetrySample(data);
            }
            if (_tripActive && !string.IsNullOrWhiteSpace(_serverTripId) && DateTime.UtcNow - _lastTelemetrySentAtUtc >= TimeSpan.FromSeconds(10)) await SendTelemetrySample(data);
        }
        catch { SetDisconnected(); }
        finally { _refreshBusy = false; }
    }

    private async Task ProcessTollgateEventAsync(TelemetrySnapshot data)
    {
        if (!data.TollgatePaid || data.TollgateAmount <= 0 || data.TollgateEventId <= 0 || data.TollgateEventId == _lastProcessedTollgateEventId) return;
        _lastProcessedTollgateEventId = data.TollgateEventId;
        var amount = Math.Round((decimal)data.TollgateAmount, 2, MidpointRounding.AwayFromZero);
        var sourceKey = $"toll-{data.TollgateEventId}";
        var localTripId = GetLocalTripIdForExpense();
        var payload = new
        {
            amount,
            tripId = _serverTripId,
            localTripId,
            sourceKey,
            odometerKm = data.OdometerKm,
            truckBrand = data.TruckBrand,
            truckModel = data.TruckModel,
            licensePlate = data.LicensePlate
        };

        try
        {
            if (LocalData.Current is { } store)
            {
                new LocalEconomyRepository(store.Db).AddExpense(
                    sourceKey, localTripId, "toll",
                    $"Pedágio ETS2 • R$ {amount:0.00}", amount, DateTime.UtcNow);
            }

            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token))
            {
                _serverSync.QueueExpense(_serverTripId, payload);
                StatusText.Text = $"TransPoli • pedágio detectado pela telemetria • R$ {amount:0.00} • salvo localmente";
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/expenses/toll-payment");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                _serverSync.QueueExpense(_serverTripId, payload);

            StatusText.Text = response.IsSuccessStatusCode
                ? $"TransPoli • pedágio real detectado • R$ {amount:0.00} debitado do banco"
                : $"TransPoli • pedágio salvo localmente • R$ {amount:0.00} • sincronização pendente";
        }
        catch
        {
            try { _serverSync.QueueExpense(_serverTripId, payload); } catch { }
            StatusText.Text = $"TransPoli • pedágio salvo localmente • R$ {amount:0.00} • sincronização pendente";
        }
    }

    private void UpdateRealInstrumentation(TelemetrySnapshot data)
    {
        // Esta camada só apresenta campos que já existem no snapshot real da telemetria.
        RpmGaugeText.Text = data.Rpm > 0 ? data.Rpm.ToString("0") : "0";
        GearGaugeText.Text = data.Gear == 0 ? "N" : data.Gear < 0 ? "R" : data.Gear.ToString();
        EngineGaugeStatusText.Text = data.EngineEnabled ? "LIGADO" : "DESLIGADO";
        EngineGaugeStatusText.Foreground = FindResource(data.EngineEnabled ? "Green" : "TextMuted") as System.Windows.Media.Brush;

        FuelText.Text = $"{data.FuelLiters:0.0} L";
        FuelRangeGaugeText.Text = data.FuelRangeKm > 0 ? $"AUTONOMIA {data.FuelRangeKm:0} km" : "AUTONOMIA N/D";
        FuelStatusDot.Foreground = FindResource(data.FuelWarning ? "Red" : "Green") as System.Windows.Media.Brush;

        if (data.WaterTemperature > 0)
        {
            WaterTempText.Text = $"{data.WaterTemperature:0.0} °C";
            WaterTempStatusText.Text = data.WaterTemperatureWarning ? "ALERTA" : "LEITURA";
            WaterTempStatusText.Foreground = FindResource(data.WaterTemperatureWarning ? "Red" : "Green") as System.Windows.Media.Brush;
            WaterTempDot.Foreground = FindResource(data.WaterTemperatureWarning ? "Red" : "Green") as System.Windows.Media.Brush;
        }
        else
        {
            WaterTempText.Text = "N/D";
            WaterTempStatusText.Text = "SEM DADO";
            WaterTempStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            WaterTempDot.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        }

        if (data.AirPressure > 0)
        {
            AirPressureText.Text = $"{data.AirPressure:0.0} psi";
            var airAlert = data.AirPressureWarning || data.AirPressureEmergency;
            AirPressureStatusText.Text = airAlert ? (data.AirPressureEmergency ? "EMERGÊNCIA" : "ALERTA") : "LEITURA";
            AirPressureStatusText.Foreground = FindResource(airAlert ? "Red" : "Green") as System.Windows.Media.Brush;
            AirPressureDot.Foreground = FindResource(airAlert ? "Red" : "Green") as System.Windows.Media.Brush;
        }
        else
        {
            AirPressureText.Text = "N/D";
            AirPressureStatusText.Text = "SEM DADO";
            AirPressureStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            AirPressureDot.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        }

        SetInstrumentIndicator(IndicatorEngineText, data.EngineEnabled, data.Connected && data.EngineEnabled, "MOTOR");
        SetInstrumentIndicator(IndicatorFuelText, data.FuelWarning, !data.FuelWarning, "COMB");
        IndicatorBrakeText.Text = "FREIO";
        IndicatorBrakeText.Foreground = FindResource(data.ParkingBrake ? "GoldBright" : "Green") as System.Windows.Media.Brush;
        IndicatorBrakeText.ToolTip = data.ParkingBrake ? "FREIO • estacionamento acionado" : "FREIO • estacionamento liberado";
        SetInstrumentIndicator(IndicatorTempText, data.WaterTemperatureWarning, !data.WaterTemperatureWarning, "TEMP");
        SetInstrumentIndicator(IndicatorAirText, data.AirPressureWarning || data.AirPressureEmergency, !(data.AirPressureWarning || data.AirPressureEmergency), "AIR");
        SetInstrumentIndicator(IndicatorOilText, data.OilPressureWarning, !data.OilPressureWarning, "ÓLEO");
        SetInstrumentIndicator(IndicatorBatteryText, data.BatteryVoltageWarning, !data.BatteryVoltageWarning, "BATERIA");
        SetInstrumentIndicator(IndicatorAdBlueText, data.AdBlueWarning, !data.AdBlueWarning, "ADBLUE");

        var hasWarning = data.FuelWarning || data.AirPressureWarning || data.AirPressureEmergency ||
                         data.OilPressureWarning || data.WaterTemperatureWarning ||
                         data.BatteryVoltageWarning || data.AdBlueWarning;
        SystemNominalText.Text = hasWarning ? "ATENÇÃO • ALERTA DE TELEMETRIA" : "TELEMETRIA NOMINAL";
        SystemNominalText.Foreground = FindResource(hasWarning ? "Red" : "Green") as System.Windows.Media.Brush;

        var tripTime = _tripActive ? FormatDuration(DateTime.UtcNow - _tripStartedAtUtc) : "00:00";
        DashboardTachDurationText.Text = tripTime;
        DashboardTachTripTimeText.Text = tripTime;

        var tachDriving = _tripActive && !data.GamePaused && Math.Abs(data.SpeedKph) > 0.5f;
        var tachPaused = _tripActive && (data.GamePaused || Math.Abs(data.SpeedKph) <= 0.5f);
        DashboardTachStateText.Text = !_tripActive ? "AGUARDANDO" : data.GamePaused ? "JOGO PAUSADO" : tachDriving ? "EM MOVIMENTO" : "PARADO";
        DashboardTachStateText.Foreground = FindResource(!_tripActive ? "TextMuted" : data.GamePaused ? "GoldBright" : tachDriving ? "Green" : "TextMain") as System.Windows.Media.Brush;
        DashboardTachSpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";

        if (TripStateDot != null)
            TripStateDot.Fill = FindResource(_tripActive ? "Green" : "TextMuted") as System.Windows.Media.Brush;

        if (TachDrivingBadge != null && TachPauseBadge != null)
        {
            TachDrivingBadge.Background = FindResource(tachDriving ? "Green" : "Surface2") as System.Windows.Media.Brush;
            TachDrivingBadge.BorderBrush = FindResource(tachDriving ? "Green" : "Stroke") as System.Windows.Media.Brush;
            if (TachDrivingBadge.Child is TextBlock drivingText)
                drivingText.Foreground = FindResource(tachDriving ? "Bg" : "TextMuted") as System.Windows.Media.Brush;

            TachPauseBadge.Background = FindResource(tachPaused ? "GoldSoft" : "Surface2") as System.Windows.Media.Brush;
            TachPauseBadge.BorderBrush = FindResource(tachPaused ? "StrokeGold" : "Stroke") as System.Windows.Media.Brush;
            if (TachPauseBadge.Child is TextBlock pauseText)
                pauseText.Foreground = FindResource(tachPaused ? "GoldBright" : "TextMuted") as System.Windows.Media.Brush;
        }
    }

    private static void SetInstrumentIndicator(TextBlock target, bool alert, bool nominal, string label)
    {
        target.Text = label;
        target.Foreground = alert
            ? System.Windows.Application.Current.FindResource("Red") as System.Windows.Media.Brush
            : nominal
                ? System.Windows.Application.Current.FindResource("Green") as System.Windows.Media.Brush
                : System.Windows.Application.Current.FindResource("TextMuted") as System.Windows.Media.Brush;
        target.ToolTip = alert ? $"{label} • ALERTA DE TELEMETRIA" : $"{label} • leitura real da telemetria";
    }

    private void UpdateDesktopClock()
    {
        var now = DateTime.Now;
        if (ClockText != null) ClockText.Text = now.ToString("HH:mm");
        if (DateText != null) DateText.Text = now.ToString("dd/MM/yyyy");
        UpdateTabletStatusBar(LastTelemetry?.Connected == true);
    }

    private static string BuildTelemetryInfo(TelemetrySnapshot data)
    {
        return "ETS2 conectado • telemetria ativa";
    }

    private async void RefreshDashboard_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StatusText.Text = "TransPoli • atualizando sistema e telemetria...";
        _refreshBusy = false;
        await RefreshTelemetry();
    }

    private void UpdateAutomaticLock(TelemetrySnapshot data)
    {
        var stopped = Math.Abs(data.SpeedKph) < 0.5f;
        if (!data.Connected || (!data.EngineEnabled && stopped)) _truckLocked = true;
        if (_garageUnauthorized)
        {
            _truckLocked = true;
            VehicleLockText.Text = _garageReason == "foreign_truck" ? "🔒 CAMINHÃO DE OUTRO MOTORISTA" : "🔒 CAMINHÃO FORA DA SUA GARAGEM";
            VehicleLockText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            UnlockButton.IsEnabled = false; UnlockButton.Opacity = 0.4; AlertText.Text = _garageMessage; AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush; return;
        }
        if (_truckLocked)
        {
            VehicleLockText.Text = "🔒 CAMINHÃO BLOQUEADO"; VehicleLockText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush; UnlockButton.IsEnabled = data.EngineEnabled && !data.GamePaused; UnlockButton.Opacity = UnlockButton.IsEnabled ? 1.0 : 0.45; AlertText.Text = data.EngineEnabled ? "Caminhão ligado • desbloqueio necessário" : stopped ? "Veículo parado e motor desligado • bloqueado" : "Motor desligado • bloqueio aguardando parada"; AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
        }
        else { VehicleLockText.Text = "🟢 CAMINHÃO LIBERADO"; VehicleLockText.Foreground = FindResource("Green") as System.Windows.Media.Brush; UnlockButton.IsEnabled = false; UnlockButton.Opacity = 0.45; AlertText.Text = "Nenhum alerta operacional ativo"; AlertText.Foreground = FindResource("Green") as System.Windows.Media.Brush; }
    }
    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_garageUnauthorized) { StatusText.Text = "TransPoli • desbloqueio negado • caminhão não autorizado na garagem"; AlertText.Text = _garageMessage; AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush; return; }
        _truckLocked = false; SaveSessionState(); VehicleLockText.Text = "🟢 CAMINHÃO LIBERADO"; VehicleLockText.Foreground = FindResource("Green") as System.Windows.Media.Brush; UnlockButton.IsEnabled = false; UnlockButton.Opacity = 0.45; AlertText.Text = "Caminhão liberado para operação"; AlertText.Foreground = FindResource("Green") as System.Windows.Media.Brush; StatusText.Text = "TransPoli • caminhão desbloqueado pelo tablet";
    }

    private void UpdateAutomaticTrip(TelemetrySnapshot data)
    {
        var hasJob = HasActiveJob(data);
        if (!_tripActive)
        {
            if (!hasJob)
            {
                TripStatusText.Text = _truckLocked && data.EngineEnabled ? "Caminhão bloqueado • aguardando desbloqueio" : "Aguardando trabalho do ETS2";
                TripRouteText.Text = "Nenhuma viagem ativa";
                TripCargoText.Text = "";
                TripDistanceText.Text = "0 km";
                TripDurationText.Text = "00:00:00";
                TripOriginText.Text = "—"; TripOriginCompanyText.Text = "—";
                TripDestinationText.Text = "—"; TripDestinationCompanyText.Text = "—";
                TripProgressText.Text = "0%"; TripDistanceLiveText.Text = "0 / 0 km"; TripRemainingText.Text = "— km restantes";
                TripProgressFill.Width = 0;
                DashboardTachRouteText.Text = "Nenhuma viagem ativa";
                DashboardTachCargoText.Text = "Carga: —";
                return;
            }
            if(!string.IsNullOrWhiteSpace(data.SourceCity)) _tripRouteOrigin=data.SourceCity;
            if(!string.IsNullOrWhiteSpace(data.DestinationCity)) _tripRouteDestination=data.DestinationCity;
            if(!string.IsNullOrWhiteSpace(data.SourceCompany)) _tripRouteOriginCompany=data.SourceCompany;
            if(!string.IsNullOrWhiteSpace(data.DestinationCompany)) _tripRouteDestinationCompany=data.DestinationCompany;
            if(!string.IsNullOrWhiteSpace(data.Cargo)) _tripCargo=data.Cargo;
            if(data.CargoValueBrl.HasValue) _tripCargoValue=data.CargoValueBrl;
            UpdateTripCard(data, 0);
            TripDurationText.Text = "Aguardando saída";
            TripStatusText.Text = _truckLocked ? "Carga detectada • desbloqueie o caminhão" : "Trabalho detectado • pronto para iniciar";
            if (!data.GamePaused && (data.CargoLoaded || data.OnJob) && DateTime.UtcNow - _lastTripFinishedAtUtc > TimeSpan.FromSeconds(5))
            {
                if (_tripDocumentPending)
                {
                    if (!_tripGateModalOpen && DateTime.UtcNow >= _tripGateNextPromptUtc && Math.Abs(data.SpeedKph) <= 1.0f)
                        ShowTripDocumentGate(data);
                }
                else if (Math.Abs(data.SpeedKph) <= 1.0f)
                {
                    BeginTripDocumentGate(data);
                }
                else
                {
                    TripStatusText.Text = "DOCUMENTAÇÃO PENDENTE • pare o caminhão para carimbar a nota";
                }
            }
            return;
        }
        if (data.CargoLoaded)
        {
            _jobMissingTicks = 0;
            var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            UpdateTripCard(data, distance);
            TripStatusText.Text = _truckLocked ? "VIAGEM • CAMINHÃO BLOQUEADO" : "VIAGEM EM ANDAMENTO";
            TripDurationText.Text = FormatDuration(elapsed);
            return;
        }
        _jobMissingTicks++;
        TripStatusText.Text = "Carga descarregada • confirmando fim da viagem...";
        TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc);
        if (_jobMissingTicks >= 20) FinishAutomaticTrip(data);
    }

    private void UpdateTripCard(TelemetrySnapshot data, float distance)
    {
        var origin = string.IsNullOrWhiteSpace(_tripRouteOrigin) ? data.SourceCity : _tripRouteOrigin;
        var destination = string.IsNullOrWhiteSpace(_tripRouteDestination) ? data.DestinationCity : _tripRouteDestination;
        var originCompany = string.IsNullOrWhiteSpace(_tripRouteOriginCompany) ? data.SourceCompany : _tripRouteOriginCompany;
        var destinationCompany = string.IsNullOrWhiteSpace(_tripRouteDestinationCompany) ? data.DestinationCompany : _tripRouteDestinationCompany;
        var planned = _tripPlannedDistanceKm > 0 ? _tripPlannedDistanceKm : (data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : data.RouteDistanceKm);
        var remaining = Math.Max(0f, planned - distance);
        var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;

        TripOriginText.Text = string.IsNullOrWhiteSpace(origin) ? "Origem não informada" : origin;
        TripOriginCompanyText.Text = string.IsNullOrWhiteSpace(originCompany) ? "Empresa não informada" : originCompany;
        TripDestinationText.Text = string.IsNullOrWhiteSpace(destination) ? "Destino não informado" : destination;
        TripDestinationCompanyText.Text = string.IsNullOrWhiteSpace(destinationCompany) ? "Empresa não informada" : destinationCompany;
        TripRouteText.Text = string.IsNullOrWhiteSpace(origin) && string.IsNullOrWhiteSpace(destination) ? "Rota não informada" : $"{origin ?? "Origem"} → {destination ?? "Destino"}";
        TripCargoText.Text = string.IsNullOrWhiteSpace(_tripCargo) ? (string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : data.Cargo) : _tripCargo;
        DashboardTachRouteText.Text = string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination) ? "Nenhuma viagem ativa" : $"{origin} → {destination}";
        DashboardTachCargoText.Text = $"Carga: {(string.IsNullOrWhiteSpace(_tripCargo) ? (string.IsNullOrWhiteSpace(data.Cargo) ? "—" : data.Cargo) : _tripCargo)}";
        TripProgressText.Text = $"{progress * 100:0}%";
        TripDistanceLiveText.Text = $"{distance:0.0} / {planned:0} km";
        TripRemainingText.Text = planned > 0 ? $"{remaining:0.0} km restantes" : "Distância não informada";
        TripStartSummaryText.Text = _tripStartedAtUtc == default ? "Aguardando saída" : $"Saída { _tripStartedAtUtc.ToLocalTime():HH:mm}";
        TripLiveText.Text = "MONITORAMENTO ATIVO";
        TripProgressFill.Width = 0;
        if (TripProgressFill.Parent is FrameworkElement parent)
            Dispatcher.BeginInvoke(new Action(() => TripProgressFill.Width = Math.Max(0, parent.ActualWidth * progress)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void StartAutomaticTrip(TelemetrySnapshot data)
    {
        _tripActive = true; _tripStartedAtUtc = DateTime.UtcNow; _tripStartOdometer = data.OdometerKm; _tripStartFuel = data.FuelLiters; _tripFuelConsumedL = 0; _tripLastFuelLiters = data.FuelLiters; _tripMovingSeconds = 0; _tripLastProgressAtUtc = DateTime.UtcNow; _tripPlannedDistanceKm = data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : (data.RouteDistanceKm > 0 ? data.RouteDistanceKm : 0); _tripRouteOrigin=data.SourceCity; _tripRouteDestination=data.DestinationCity; _tripRouteOriginCompany=data.SourceCompany; _tripRouteDestinationCompany=data.DestinationCompany; _tripCargo=data.Cargo; _tripCargoValue=data.CargoValueBrl; _jobMissingTicks = 0; _serverTripId = null; _localTripId = Guid.NewGuid().ToString("N"); _lastTelemetrySentAtUtc = DateTime.MinValue; _lastLocalTelemetrySavedAtUtc = DateTime.MinValue; _lastServerTripSyncAttemptUtc = DateTime.MinValue; SaveSessionState(); EnsureLocalTripDocument(data);

        try
        {
            if (LocalData.Current is { } store)
            {
                var trips = new LocalTripRepository(store.Db);
                _localTripRatePerKm = trips.ResolveRatePerKm(data.Cargo);
                trips.StartTrip(_localTripId, data, null, _localTripRatePerKm);
            }
        }
        catch
        {
            _localTripRatePerKm = 6.00;
        }

        TripStatusText.Text = "VIAGEM INICIADA AUTOMATICAMENTE"; TripRouteText.Text = BuildRoute(data); TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}"; TripDistanceText.Text = "0.0 km"; TripDurationText.Text = "00:00:00"; StatusText.Text = $"TransPoli • viagem iniciada • tarifa local R$ {_localTripRatePerKm:0.00}/km";
        SaveLocalTelemetrySample(data, true);
        await CreateServerTrip(data);
    }

    private async Task CreateServerTrip(TelemetrySnapshot data)
    {
        var token = SecureTokenStore.Read();
        var payload = new
        {
            cargo = data.Cargo,
            origin = data.SourceCity,
            destination = data.DestinationCity,
            truckBrand = data.TruckBrand,
            truckModel = data.TruckModel,
            licensePlate = data.LicensePlate,
            sourceCompany = data.SourceCompany,
            destinationCompany = data.DestinationCompany,
            cargoMassKg = data.CargoMassKg,
            plannedDistanceKm = data.PlannedDistanceKm,
            cargoValueBrl = data.CargoValueBrl,
            startOdometerKm = data.OdometerKm,
            startFuelL = data.FuelLiters,
            startedAt = _tripStartedAtUtc
        };

        try
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (!string.IsNullOrWhiteSpace(_localTripId))
                    _serverSync.QueueTripStart(_localTripId, payload);
                StatusText.Text = "TransPoli • viagem salva localmente • login/sincronização pendente";
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrWhiteSpace(_localTripId))
                    _serverSync.QueueTripStart(_localTripId, payload);
                StatusText.Text = $"TransPoli • viagem salva localmente • servidor respondeu {(int)response.StatusCode} • sincronização pendente";
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            if (root.TryGetProperty("trip", out var trip) &&
                trip.TryGetProperty("id", out var id))
            {
                _serverTripId = id.GetString();
            }

            // A tarifa do contrato é a fonte econômica da viagem.
            // O servidor cria/vincula o contrato automaticamente ao inserir a viagem.
            if (root.TryGetProperty("cargoRateBrlKm", out var rateElement))
            {
                double serverRate = 0;
                if (rateElement.ValueKind == JsonValueKind.Number)
                    serverRate = rateElement.GetDouble();
                else if (rateElement.ValueKind == JsonValueKind.String)
                    double.TryParse(rateElement.GetString(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out serverRate);

                if (serverRate >= 5 && serverRate <= 12)
                {
                    _localTripRatePerKm = serverRate;
                    if (!string.IsNullOrWhiteSpace(_localTripId) && LocalData.Current is { } rateStore)
                        new LocalTripRepository(rateStore.Db).SetRatePerKm(_localTripId, serverRate);
                }
            }

            if (!string.IsNullOrWhiteSpace(_localTripId) &&
                !string.IsNullOrWhiteSpace(_serverTripId) &&
                LocalData.Current is { } localStore)
            {
                new LocalTripRepository(localStore.Db).SetServerId(_localTripId, _serverTripId);
            }

            SaveSessionState();

            if (!string.IsNullOrWhiteSpace(_serverTripId))
            {
                StatusText.Text = $"TransPoli • contrato vinculado • R$ {_localTripRatePerKm:0.00}/km";
                await SendTelemetrySample(data, true);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(_localTripId))
                _serverSync.QueueTripStart(_localTripId, payload);
            StatusText.Text = "TransPoli • viagem salva localmente • sincronização do contrato pendente";
        }
    }

    private async Task SendLiveTelemetrySample(TelemetrySnapshot data)
    {
        var token = SecureTokenStore.Read(); if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var payload = new { deviceId = DeviceIdentity.GetOrCreate(), recordedAt = DateTime.UtcNow, connected = data.Connected, game = data.Game, gamePaused = data.GamePaused, engineEnabled = data.EngineEnabled, electricEnabled = data.ElectricEnabled, speedKph = Math.Abs(data.SpeedKph), speedLimitKph = data.SpeedLimitKph, rpm = data.Rpm, gear = data.Gear, fuelL = data.FuelLiters, fuelRangeKm = data.FuelRangeKm, fuelAvgConsumption = data.FuelAvgConsumption, adblueL = data.AdBlueLiters, oilPressure = data.OilPressure, oilTemperature = data.OilTemperature, waterTemperature = data.WaterTemperature, batteryVoltage = data.BatteryVoltage, odometerKm = data.OdometerKm, airPressure = data.AirPressure, brakeTemperature = data.BrakeTemperature, parkingBrake = data.ParkingBrake, motorBrake = data.MotorBrake, brakeLight = data.BrakeLight, cruiseControl = data.CruiseControl, cruiseSpeedKph = data.CruiseSpeedKph, retarderLevel = data.RetarderLevel, userThrottle = data.UserThrottle, effectiveThrottle = data.EffectiveThrottle, userBrake = data.UserBrake, effectiveBrake = data.EffectiveBrake, wearEngine = data.WearEngine, wearTransmission = data.WearTransmission, wearCabin = data.WearCabin, wearChassis = data.WearChassis, wearWheels = data.WearWheels, cargoDamage = data.CargoDamage, airPressureWarning = data.AirPressureWarning, airPressureEmergency = data.AirPressureEmergency, fuelWarning = data.FuelWarning, adblueWarning = data.AdBlueWarning, oilPressureWarning = data.OilPressureWarning, waterTemperatureWarning = data.WaterTemperatureWarning, batteryVoltageWarning = data.BatteryVoltageWarning, wipers = data.Wipers, blinkerLeftActive = data.BlinkerLeftActive, blinkerRightActive = data.BlinkerRightActive, lightsParking = data.LightsParking, lightsBrake = data.LightsBrake, lightsReverse = data.LightsReverse, lightsHazard = data.LightsHazard, differentialLock = data.DifferentialLock, liftAxle = data.LiftAxle, trailerLiftAxle = data.TrailerLiftAxle, truckBrand = data.TruckBrand, truckModel = data.TruckModel, licensePlate = data.LicensePlate, cargo = data.Cargo, cargoMassKg = data.CargoMassKg, sourceCity = data.SourceCity, destinationCity = data.DestinationCity, sourceCompany = data.SourceCompany, destinationCompany = data.DestinationCompany, plannedDistanceKm = data.PlannedDistanceKm, cargoValueBrl = data.CargoValueBrl, onJob = data.OnJob, specialJob = data.SpecialJob, refuelActive = data.RefuelActive };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/device/telemetry"); request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}"); request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"); using var response = await _http.SendAsync(request); if (response.IsSuccessStatusCode) _lastLiveTelemetrySentAtUtc = DateTime.UtcNow;
        } catch { }
    }

    private async Task SendTelemetrySample(TelemetrySnapshot data, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_serverTripId)) return; if (!force && DateTime.UtcNow - _lastTelemetrySentAtUtc < TimeSpan.FromSeconds(5)) return; var token = SecureTokenStore.Read(); if (string.IsNullOrWhiteSpace(token)) return;
        try { var payload = new { recordedAt = DateTime.UtcNow, speedKph = Math.Abs(data.SpeedKph), rpm = data.Rpm, gear = data.Gear, fuelL = data.FuelLiters, odometerKm = data.OdometerKm, fuelRangeKm = data.FuelRangeKm, gamePaused = data.GamePaused, engineEnabled = data.EngineEnabled, electricEnabled = data.ElectricEnabled, parkingBrake = data.ParkingBrake, motorBrake = data.MotorBrake, brakeLight = data.BrakeLight, userThrottle = data.UserThrottle, effectiveThrottle = data.EffectiveThrottle, userBrake = data.UserBrake, effectiveBrake = data.EffectiveBrake, airPressure = data.AirPressure, brakeTemperature = data.BrakeTemperature, fuelAvgConsumption = data.FuelAvgConsumption, adblueL = data.AdBlueLiters, oilPressure = data.OilPressure, oilTemperature = data.OilTemperature, waterTemperature = data.WaterTemperature, batteryVoltage = data.BatteryVoltage, speedLimitKph = data.SpeedLimitKph, cruiseControl = data.CruiseControl, cruiseSpeedKph = data.CruiseSpeedKph, retarderLevel = data.RetarderLevel, cargoDamage = data.CargoDamage, wearEngine = data.WearEngine, wearTransmission = data.WearTransmission, wearCabin = data.WearCabin, wearChassis = data.WearChassis, wearWheels = data.WearWheels, airPressureWarning = data.AirPressureWarning, airPressureEmergency = data.AirPressureEmergency, fuelWarning = data.FuelWarning, adblueWarning = data.AdBlueWarning, oilPressureWarning = data.OilPressureWarning, waterTemperatureWarning = data.WaterTemperatureWarning, batteryVoltageWarning = data.BatteryVoltageWarning, wipers = data.Wipers, blinkerLeftActive = data.BlinkerLeftActive, blinkerRightActive = data.BlinkerRightActive, lightsParking = data.LightsParking, lightsBrake = data.LightsBrake, lightsReverse = data.LightsReverse, lightsHazard = data.LightsHazard, differentialLock = data.DifferentialLock, liftAxle = data.LiftAxle, trailerLiftAxle = data.TrailerLiftAxle, truckBrand = data.TruckBrand, truckModel = data.TruckModel, licensePlate = data.LicensePlate, sourceCompany = data.SourceCompany, destinationCompany = data.DestinationCompany, cargoMassKg = data.CargoMassKg, plannedDistanceKm = data.PlannedDistanceKm, cargoValueBrl = data.CargoValueBrl }; using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{_serverTripId}/telemetry"); request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}"); request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"); using var response = await _http.SendAsync(request); if (response.IsSuccessStatusCode) _lastTelemetrySentAtUtc = DateTime.UtcNow; } catch { }
    }

    private async void FinishAutomaticTrip(TelemetrySnapshot data)
    {
        var finishingTripId = _serverTripId;
        var localTripId = _localTripId;
        _jobMissingTicks = 0; _lastTripFinishedAtUtc = DateTime.UtcNow;
        var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
        var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
        var fuelUsed = Math.Max(0f, _tripStartFuel - data.FuelLiters);
        if (_localTripRatePerKm <= 0) _localTripRatePerKm = 6.00;
        var gross = Math.Round(distance * _localTripRatePerKm, 2, MidpointRounding.AwayFromZero);

        // A liquidação local é a fonte de verdade. A API é sincronização central e não define o valor pago.
        try
        {
            if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } store)
            {
                var localTrips = new LocalTripRepository(store.Db);
                localTrips.FinishTrip(localTripId, data, distance, fuelUsed, gross, "telemetria_entrega");
                new LocalEconomyRepository(store.Db).RecordTripIncome(localTripId, (decimal)gross, DateTime.UtcNow);

                // Após fechar a viagem, o banco verifica automaticamente a parcela do empréstimo.
                // A cobrança é idempotente por viagem e só ocorre quando houve lucro líquido positivo.
                var localEconomy = new LocalEconomyRepository(store.Db);
                var tripNetBeforeLoan = localEconomy.GetTripNet(localTripId);
                var loanPayment = localEconomy.ApplyAutomaticLoanPayment(localTripId, tripNetBeforeLoan);
                if (loanPayment > 0)
                    StatusText.Text = $"TransPoli • parcela do empréstimo debitada: R$ {loanPayment:0.00}";
            }
        }
        catch
        {
            StatusText.Text = "TransPoli • erro ao salvar a liquidação local da viagem";
            return;
        }

        // Mantém o vínculo central quando disponível. Se estiver offline, a liquidação
        // fica na fila local e será enviada somente após a viagem central existir.
        var finishPayload = new
        {
            distanceKm = distance,
            fuelUsedL = fuelUsed,
            cargoDamage = Math.Clamp(data.CargoDamage, 0f, 1f),
            cargoMassKg = Math.Max(0f, data.CargoMassKg)
        };
        if (!string.IsNullOrWhiteSpace(finishingTripId))
        {
            _ = FinishServerTrip(distance, fuelUsed, data);
        }
        else if (!string.IsNullOrWhiteSpace(localTripId))
        {
            _serverSync.QueueTripFinish(localTripId, finishPayload);
        }

        ArchiveCurrentTachograph(); ClearSessionState();
        var elapsedText = FormatDuration(elapsed);
        TripStatusText.Text = "VIAGEM FINALIZADA AUTOMATICAMENTE";
        TripDistanceText.Text = $"{distance:0.0} km";
        TripDurationText.Text = elapsedText;
        StatusText.Text = $"TransPoli • viagem finalizada • {distance:0.0} km • R$ {gross:0.00} • {elapsedText}";
        SaveSessionState();
    }
    private void SaveLocalTelemetrySample(TelemetrySnapshot data, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_localTripId) || LocalData.Current is not { } store) return;
        if (!force && DateTime.UtcNow - _lastLocalTelemetrySavedAtUtc < TimeSpan.FromSeconds(2)) return;
        try
        {
            new LocalTelemetryRepository(store.Db).Append(_localTripId, data);
            _lastLocalTelemetrySavedAtUtc = DateTime.UtcNow;
        }
        catch { }
    }

    private async Task<bool> FinishServerTrip(float distance, float fuelUsed, TelemetrySnapshot data)
    {
        var token = SecureTokenStore.Read(); if (string.IsNullOrWhiteSpace(token))
        {
            if (!string.IsNullOrWhiteSpace(_localTripId))
                _serverSync.QueueTripFinish(_localTripId, new { distanceKm = distance, fuelUsedL = fuelUsed, cargoDamage = Math.Clamp(data.CargoDamage, 0f, 1f), cargoMassKg = Math.Max(0f, data.CargoMassKg) });
            return false;
        }

        try
        {
            var payload = new { distanceKm = distance, fuelUsedL = fuelUsed, cargoDamage = Math.Clamp(data.CargoDamage, 0f, 1f), cargoMassKg = Math.Max(0f, data.CargoMassKg) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{_serverTripId}/finish");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrWhiteSpace(_localTripId))
                    _serverSync.QueueTripFinish(_localTripId, payload);
                return false;
            }
            var root = J.Parse(await response.Content.ReadAsStringAsync());
            var economy = J.Prop(root, "economy"); if (economy is null) return false;
            var net = J.Dec(economy, "netBrl"); var balance = J.Dec(economy, "balanceBrl");
            StatusText.Text = $"TransPoli • viagem paga • líquido {Money(net)} • saldo {Money(balance)}";
            return true;
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(_localTripId))
                _serverSync.QueueTripFinish(_localTripId, new { distanceKm = distance, fuelUsedL = fuelUsed, cargoDamage = Math.Clamp(data.CargoDamage, 0f, 1f), cargoMassKg = Math.Max(0f, data.CargoMassKg) });
            return false;
        }
    }
    private static string FormatDuration(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private static bool HasActiveJob(TelemetrySnapshot data) => data.OnJob || data.CargoLoaded || (!string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity) && !string.IsNullOrWhiteSpace(data.Cargo));
    private static string BuildRoute(TelemetrySnapshot data) => string.IsNullOrWhiteSpace(data.SourceCity) && string.IsNullOrWhiteSpace(data.DestinationCity) ? "Nenhum trabalho ativo detectado." : $"{data.SourceCity ?? "Origem"}  →  {data.DestinationCity ?? "Destino"}";
    private void SetDisconnected()
    {
        _telemetryConnectedAtUtc = DateTime.MinValue;
        LastTelemetry = null;
        RpmGaugeText.Text = "0";
        GearGaugeText.Text = "N";
        EngineGaugeStatusText.Text = "SEM TELEMETRIA";
        EngineGaugeStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        FuelText.Text = "N/D";
        FuelRangeGaugeText.Text = "AUTONOMIA N/D";
        WaterTempText.Text = "N/D";
        WaterTempStatusText.Text = "SEM DADO";
        WaterTempDot.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        WaterTempStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        AirPressureText.Text = "N/D";
        AirPressureStatusText.Text = "SEM DADO";
        AirPressureDot.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        AirPressureStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        foreach (var indicator in new[] { IndicatorEngineText, IndicatorFuelText, IndicatorBrakeText, IndicatorTempText, IndicatorAirText, IndicatorOilText, IndicatorBatteryText, IndicatorAdBlueText })
        {
            indicator.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        }
        SystemNominalText.Text = "AGUARDANDO TELEMETRIA";
        SystemNominalText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        DashboardTachDurationText.Text = "00:00";
        DashboardTachTripTimeText.Text = "00:00";
        DashboardTachStateText.Text = "AGUARDANDO";
        DashboardTachSpeedText.Text = "0 km/h";
        UpdateTabletStatusBar(false); _truckLocked = true; ConnectionText.Text = "ETS2 DESCONECTADO"; ConnectionText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush; ConnectionDot.Fill = FindResource("Muted") as System.Windows.Media.Brush; VehicleLockText.Text = "🔒 CAMINHÃO BLOQUEADO"; VehicleLockText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush; UnlockButton.IsEnabled = false; UnlockButton.Opacity = 0.45; AlertText.Text = "Aguardando conexão com o ETS2"; AlertText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush; TelemetryInfoText.Text = "TransPoli Connector aguardando telemetria"; StatusText.Text = "Aguardando TransPoli Connector e telemetria do ETS2..."; }
    protected override void OnClosed(EventArgs e) { try { _timer.Stop(); } catch { } try { _connector.Dispose(); } catch { } try { _http.Dispose(); } catch { } try { Application.Current?.Shutdown(0); } catch { } base.OnClosed(e); }
}

public sealed class TelemetrySnapshot
{
    public bool Connected { get; set; } public bool Updated { get; set; } public ulong Timestamp { get; set; } public string? Game { get; set; } public bool TollgatePaid { get; set; } public long TollgateAmount { get; set; } public long TollgateEventId { get; set; } public bool GamePaused { get; set; }
    public string? TruckBrand { get; set; } public string? TruckModel { get; set; } public string? TruckId { get; set; } public string? LicensePlate { get; set; } public bool EngineEnabled { get; set; } public bool ElectricEnabled { get; set; } public bool CargoLoaded { get; set; } public bool SpecialJob { get; set; } public bool OnJob { get; set; } public bool JobFinished { get; set; } public bool JobCancelled { get; set; } public bool JobDelivered { get; set; } public bool RefuelActive { get; set; } public bool RefuelPayed { get; set; } public float RefuelAmountLiters { get; set; }
    public float SpeedKph { get; set; } public float SpeedMps { get; set; } public float SpeedLimitKph { get; set; } public float Rpm { get; set; } public int Gear { get; set; } public float UserThrottle { get; set; } public float EffectiveThrottle { get; set; } public float UserBrake { get; set; } public float EffectiveBrake { get; set; }
    public float FuelLiters { get; set; } public float FuelAvgConsumption { get; set; } public float FuelRangeKm { get; set; } public float AdBlueLiters { get; set; } public float OilPressure { get; set; } public float OilTemperature { get; set; } public float WaterTemperature { get; set; } public float BatteryVoltage { get; set; }
    public float OdometerKm { get; set; } public float RouteDistanceKm { get; set; } public float RouteTimeSeconds { get; set; } public bool CruiseControl { get; set; } public float CruiseSpeedKph { get; set; } public string? SourceCity { get; set; } public string? DestinationCity { get; set; } public string? SourceCompany { get; set; } public string? DestinationCompany { get; set; } public string? Cargo { get; set; } public float CargoMassKg { get; set; } public uint PlannedDistanceKm { get; set; } public ulong? CargoValueBrl { get; set; }
    public float AirPressure { get; set; } public float BrakeTemperature { get; set; } public bool MotorBrake { get; set; } public bool ParkingBrake { get; set; } public bool BrakeLight { get; set; } public bool AirPressureWarning { get; set; } public bool AirPressureEmergency { get; set; } public bool FuelWarning { get; set; } public bool AdBlueWarning { get; set; } public bool OilPressureWarning { get; set; } public bool WaterTemperatureWarning { get; set; } public bool BatteryVoltageWarning { get; set; }
    public bool Wipers { get; set; } public bool BlinkerLeftActive { get; set; } public bool BlinkerRightActive { get; set; } public bool BlinkerLeftOn { get; set; } public bool BlinkerRightOn { get; set; } public bool LightsParking { get; set; } public bool LightsBrake { get; set; } public bool LightsReverse { get; set; } public bool LightsHazard { get; set; } public bool DifferentialLock { get; set; } public bool LiftAxle { get; set; } public bool LiftAxleIndicator { get; set; } public bool TrailerLiftAxle { get; set; } public bool TrailerLiftAxleIndicator { get; set; }
    public uint RetarderLevel { get; set; } public float WearEngine { get; set; } public float WearTransmission { get; set; } public float WearCabin { get; set; } public float WearChassis { get; set; } public float WearWheels { get; set; } public float CargoDamage { get; set; }
}
