using System;
using System.Globalization;
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
    private readonly TripLifecycleCoordinator _tripLifecycle = new();
    private DateTime _lastTruckHealthSnapshotUtc = DateTime.MinValue;
    private float _lastTruckHealthWear = -1f;
    private string _lastTruckHealthTruckId = "";
    private const int HotKeyId = 0x5448;
    private const int PhoneHotKeyId = 0x5447;
    private const int HudHotKeyId = 0x5449;
    private const int WmHotKey = 0x0312;
    private const uint VkF9 = 0x78;
    private const uint VkF10 = 0x79;
    private const uint VkF11 = 0x7A;
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    internal const string TelemetryUrl = "http://127.0.0.1:17877/telemetry";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _timer;
    private readonly ConnectorSupervisor _connector = new();
    private TelemetryOverlayWindow? _telemetryOverlay;
    private DriverPhoneWindow? _driverPhone;
    private bool _hudHotkeyVisible = true;
    private HudSettings _hudSettings = new();
    private readonly LocalDataStore? _localData = null;
    private HwndSource? _source;
    private bool _refreshBusy;
    private bool _logoutToActivation;
    private bool _tripActive;
    private bool _truckLocked = true;
    private DateTime _tripStartedAtUtc;
    private float _tripStartOdometer;
    private float _tripStartFuel;
    private DateTime _lastTripFinishedAtUtc = DateTime.MinValue;
    private DateTime _lastTelemetrySentAtUtc = DateTime.MinValue;
    private bool _lastRefuelPayed;
    private bool _tripFinishBusy;
    // Evento de entrega precisa surgir durante esta execução. Flags antigas do Connector
    // após reiniciar o tablet nunca podem liquidar uma viagem ainda em andamento.
    private bool _deliveryEventArmed;
    // Evita recriar imediatamente uma viagem que o motorista acabou de encerrar manualmente.
    private string? _manualTripFinishSignature;
    private DateTime _lastLiveTelemetrySentAtUtc = DateTime.MinValue;
    private DateTime _telemetryConnectedAtUtc = DateTime.MinValue;
    private string? _serverTripId;
    private string? _localTripId;
    private double _localTripRatePerKm;
    private DateTime _lastLocalTelemetrySavedAtUtc = DateTime.MinValue;
    private DateTime _lastServerTripSyncAttemptUtc = DateTime.MinValue;
    private long _lastProcessedTollgateEventId;
    private readonly HashSet<string> _tollgateEventsInFlight = new(StringComparer.Ordinal);
    private readonly List<PhoneTollItem> _phoneTollHistory = new();
    private long _lastHudFineAmount;
    private bool _lastHudFuelWarning;
    private bool _lastHudAirWarning;
    private bool _lastHudOilWarning;
    private bool _lastHudTemperatureWarning;
    private bool _lastHudBatteryWarning;
    private bool _lastHudAdBlueWarning;
    private bool _lastHudTripActive;
    private DateTime _lastHudEventAtUtc = DateTime.MinValue;
    private string _lastHudEventKey = "";
    private float _lastHudCargoDamage;
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
            // Indicador de rota vindo exclusivamente da telemetria ETS2.
            // O tablet não oferece interface de mapa/GPS.
            var routeAvailable = connected && data is not null &&
                                 (!string.IsNullOrWhiteSpace(data.SourceCity) || !string.IsNullOrWhiteSpace(data.DestinationCity));
            GpsStatusText.Text = routeAvailable ? "●" : "○";
            GpsStatusText.Foreground = FindResource(routeAvailable ? "Green" : "TextMuted") as System.Windows.Media.Brush;
            GpsStatusText.ToolTip = routeAvailable ? "Rota ETS2 disponível" : "Rota ETS2 indisponível";
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
                BatteryStatusText.Text = power.ACLineStatus == 1
                    ? "⚡"
                    : percent <= 10 ? "▂"
                    : percent <= 25 ? "▃"
                    : percent <= 50 ? "▅"
                    : percent <= 75 ? "▆"
                    : "▇";
                BatteryStatusText.Foreground = FindResource(percent <= 20 ? "Red" : percent <= 40 ? "GoldBright" : "Green") as System.Windows.Media.Brush;
                BatteryModeText.Text = power.ACLineStatus == 1 ? "ALIMENTAÇÃO EXTERNA" : "BATERIA";
                BatteryModeText.Foreground = FindResource(power.ACLineStatus == 1 ? "Green" : "TextMuted") as System.Windows.Media.Brush;
                BatteryStatusText.ToolTip = power.ACLineStatus == 1 ? "Alimentação externa conectada" : "Bateria do computador";
            }
            else
            {
                BatteryPercentText.Text = "N/D";
                BatteryStatusText.Text = "—";
                BatteryStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
                BatteryModeText.Text = "ENERGIA N/D";
                BatteryModeText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
                BatteryStatusText.ToolTip = "Bateria física não disponível";
            }
        }
    }


    public MainWindow()
    {
        InitializeComponent();
        _tripLifecycle.EventRecorded += evt =>
        {
            try
            {
                if (LocalData.Current is not { } store) return;
                var data = LastTelemetry;
                var truck = data is null ? "" : (string.IsNullOrWhiteSpace(data.TruckId) ? data.LicensePlate : data.TruckId);
                new LocalOperationsRepository(store.Db).UpsertOperationalEvent(
                    "lifecycle-" + evt.Id, evt.Type, evt.Stage.ToString(), evt.Details,
                    _tripLifecycle.Current.SessionKey, _tripLifecycle.Current.Cargo, _localTripId,
                    "", truck ?? "", evt.AtUtc, evt.OdometerKm, false);
            }
            catch (Exception ex) { App.WriteUiCrashLog("TripLifecycle.PersistEvent", ex); }
        };
        _telemetryOverlay = new TelemetryOverlayWindow();
        _hudSettings = HudSettings.Load();
        _telemetryOverlay.ApplySettings(_hudSettings);

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

    private void RegisterGlobalHotKey()
    {
        var helper = new WindowInteropHelper(this);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
        if (!RegisterHotKey(helper.Handle, PhoneHotKeyId, 0, VkF9)) StatusText.Text = "F9 indisponível • outra aplicação pode estar usando o atalho do celular.";
        if (!RegisterHotKey(helper.Handle, HotKeyId, 0, VkF10)) StatusText.Text = "F10 indisponível • outra aplicação pode estar usando o atalho.";
        if (!RegisterHotKey(helper.Handle, HudHotKeyId, 0, VkF11)) StatusText.Text = "F11 indisponível • outra aplicação pode estar usando o atalho da HUD.";
    }
    private void UnregisterGlobalHotKey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) { UnregisterHotKey(handle, PhoneHotKeyId); UnregisterHotKey(handle, HotKeyId); UnregisterHotKey(handle, HudHotKeyId); }
        _source?.RemoveHook(WndProc); _source = null;
    }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == PhoneHotKeyId) { TogglePhone(); handled = true; }
        else if (msg == WmHotKey && wParam.ToInt32() == HotKeyId) { ToggleCockpit(); handled = true; }
        else if (msg == WmHotKey && wParam.ToInt32() == HudHotKeyId) { ToggleHud(); handled = true; }
        return IntPtr.Zero;
    }
    private DateTime _phoneEconomyLastRefreshUtc = DateTime.MinValue;
    private bool _phoneEconomyRefreshBusy;
    private bool _phoneHistoryRefreshBusy;
    private readonly List<PhoneTripItem> _phoneOfficialTrips = new();
    private readonly List<PhoneDocumentItem> _phoneOfficialDocuments = new();
    private DateTime _phoneTripsLastRefreshUtc = DateTime.MinValue;
    private DateTime _phoneDocumentsLastRefreshUtc = DateTime.MinValue;
    private DateTime _phoneProfileLastRefreshUtc = DateTime.MinValue;
    private bool _phoneProfileRefreshBusy;
    private string _phoneOfficialDriverName = "";

    private void UpdateDriverPhone(TelemetrySnapshot data)
    {
        if (_driverPhone is null) return;
        var distance = _tripActive ? Math.Max(_tripDistanceKm, Math.Max(0f, data.OdometerKm - _tripStartOdometer)) : 0f;
        var total = data.PlannedDistanceKm > 0 ? (float)data.PlannedDistanceKm : _tripPlannedDistanceKm;
        var remaining = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : Math.Max(0f, total - distance);
        _driverPhone.UpdateTelemetry(data, _tripActive, distance, remaining);

        try
        {
            var bank = LoadBankDataLocal();
            var stamped = _documents.Count(x => string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase));
            var hasOfficialSession = !string.IsNullOrWhiteSpace(SecureTokenStore.Read());
            // Telemetria e contadores operacionais podem usar o cache local imediatamente.
            // Em sessão autenticada, saldo/ledger/viagens são oficiais e não podem ser
            // sobrescritos a cada tick pelo fallback SQLite enquanto a API atualiza.
            if (!hasOfficialSession)
            {
                _driverPhone.UpdateOperationalSummary(bank.Balance, bank.TripCount, (double)bank.StatsDistanceKm, _documents.Count, stamped, null);
                _driverPhone.UpdateBankHistory(bank.Ledger.Select(x => new PhoneLedgerItem(
                    string.IsNullOrWhiteSpace(x.Description) ? x.Type : x.Description,
                    x.Amount,
                    x.CreatedAt)));
                _driverPhone.UpdateTripHistory(bank.TripHistory.Select(x => new PhoneTripItem(
                    x.Cargo, x.Origin, x.Destination, x.DistanceKm, x.RatePerKm, x.Gross, x.FinishedAtUtc)));
            }
            else
            {
                _driverPhone.UpdateOperationalCounters(bank.TripCount, (double)bank.StatsDistanceKm, _documents.Count, stamped);
            }
            _driverPhone.UpdateDataSourceState(
                hasOfficialSession,
                hasOfficialSession && _phoneEconomyLastRefreshUtc != DateTime.MinValue,
                hasOfficialSession && _phoneTripsLastRefreshUtc != DateTime.MinValue,
                hasOfficialSession && _phoneDocumentsLastRefreshUtc != DateTime.MinValue);
            _driverPhone.UpdateDocumentGate(_tripDocumentPending);
            if (!hasOfficialSession)
            {
                _driverPhone.UpdateDocumentHistory(_documents
                    .OrderByDescending(x => x.RecordedAtUtc)
                    .Select(x => new PhoneDocumentItem(
                        x.Reference,
                        string.IsNullOrWhiteSpace(x.Cargo) ? "Carga" : x.Cargo,
                        string.IsNullOrWhiteSpace(x.Route) ? "Rota não registrada" : x.Route,
                        string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase),
                        x.RecordedAtUtc)));
            }
            else
            {
                UpdatePhoneOfficialDocumentsWithPendingLocal();
            }
            // O app Ranking do celular usa exclusivamente o último snapshot oficial
            // recebido de /me/ranking. O histórico local continua disponível em
            // Banco/Viagens, mas não pode alterar métricas oficiais do ranking.
            _driverPhone.UpdateRankingSummary(
                _lastKnownRankingPosition > 0 ? _lastKnownRankingPosition : null,
                _lastKnownRankingRevenue,
                _lastKnownRankingRate,
                _lastKnownRankingTrips,
                _lastKnownRankingKm);
            _driverPhone.UpdateRoadCombination(RoadCombinationTelemetry.Build(data));
            if (_phoneTollHistory.Count == 0 && _poliPassRecords.Count > 0)
            {
                _phoneTollHistory.AddRange(_poliPassRecords.OrderByDescending(x => x.RecordedAtUtc).Take(30)
                    .Select(x => new PhoneTollItem(x.EventId, x.Amount > 0 ? x.Amount : null, x.RecordedAtUtc, x.TotalAxles.HasValue ? $"{x.TotalAxles.Value} eixos detectados" : "eixos não confirmados")));
            }
            _driverPhone.UpdateTollHistory(_phoneTollHistory);
            _driverPhone.UpdateRefuelPrompt(_pendingRefuelTelemetry is not null && _pendingRefuelLiters > 0, _pendingRefuelLiters);
            _driverPhone.UpdateNotifications(_notifications.Select(x => new PhoneNotificationItem(
                x.Title, x.Message, (int)x.Priority, x.CreatedAtUtc)));
            var phoneTruck = $"{data.TruckBrand ?? ""} {data.TruckModel ?? ""}".Trim();
            _driverPhone.UpdateProfile(
                string.IsNullOrWhiteSpace(SecureTokenStore.Read()) ? "PERFIL LOCAL" : "TRANSPOLI CONECTADO",
                phoneTruck,
                data.LicensePlate ?? "—",
                _phoneOfficialDriverName);
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DriverPhone.UpdateOperational", ex);
            // A telemetria do celular continua funcional mesmo se o cache operacional estiver indisponível.
        }
    }

    private async Task RefreshPhoneOfficialProfileAsync()
    {
        if (_driverPhone is null || _phoneProfileRefreshBusy ||
            DateTime.UtcNow - _phoneProfileLastRefreshUtc < TimeSpan.FromMinutes(15)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        _phoneProfileRefreshBusy = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/company-employment");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("employment", out var employment) ||
                employment.ValueKind != JsonValueKind.Object) return;
            var driverName = employment.TryGetProperty("driver_name", out var name) ? name.GetString()?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(driverName)) return;
            _phoneOfficialDriverName = driverName;
            var changed = false;
            foreach (var document in _documents.Where(x => string.IsNullOrWhiteSpace(x.Driver)))
            {
                document.Driver = driverName;
                changed = true;
            }
            if (changed && !TrySaveOperations())
                App.WriteUiCrashLog("DriverPhone.OfficialProfile", new InvalidOperationException("Falha ao persistir o motorista oficial nos documentos locais."));
            _phoneProfileLastRefreshUtc = DateTime.UtcNow;
        }
        catch (Exception ex) { App.WriteUiCrashLog("DriverPhone.OfficialProfile", ex); }
        finally { _phoneProfileRefreshBusy = false; }
    }

    private void InvalidatePhoneOfficialCache(bool economy = false, bool trips = false, bool documents = false)
    {
        if (economy)
        {
            _phoneEconomyLastRefreshUtc = DateTime.MinValue;
            // Banco do tablet e Banco do celular representam a mesma conta oficial.
            // Um evento financeiro local invalida ambos os snapshots, sem fazer GET
            // imediato: a próxima abertura/refresh consolida uma única vez.
            InvalidateBankCache();
        }
        if (trips) _phoneTripsLastRefreshUtc = DateTime.MinValue;
        if (documents) _phoneDocumentsLastRefreshUtc = DateTime.MinValue;
    }

    private void UpdatePhoneOfficialDocumentsWithPendingLocal()
    {
        var pendingLocal = _documents
            .Where(x => !string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.RecordedAtUtc)
            .Select(x => new PhoneDocumentItem(
                x.Reference,
                string.IsNullOrWhiteSpace(x.Cargo) ? "Carga" : x.Cargo,
                string.IsNullOrWhiteSpace(x.Route) ? "Rota não registrada" : x.Route,
                false,
                x.RecordedAtUtc));
        _driverPhone?.UpdateDocumentHistory(pendingLocal.Concat(_phoneOfficialDocuments).Take(20));
    }

    private async Task RefreshPhoneOfficialHistoryAsync()
    {
        if (_driverPhone is null || _phoneHistoryRefreshBusy) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;

        var now = DateTime.UtcNow;
        var refreshTrips = now - _phoneTripsLastRefreshUtc >= TimeSpan.FromMinutes(5);
        var refreshDocuments = now - _phoneDocumentsLastRefreshUtc >= TimeSpan.FromMinutes(5);
        if (!refreshTrips && !refreshDocuments) return;

        _phoneHistoryRefreshBusy = true;
        try
        {
            if (refreshTrips)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips/history");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var trips = new List<PhoneTripItem>();
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("trips", out var rows) && rows.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in rows.EnumerateArray().Where(x =>
                            x.TryGetProperty("status", out var status) &&
                            string.Equals(status.GetString(), "finished", StringComparison.OrdinalIgnoreCase)).Take(20))
                        {
                            decimal Dec(string name) => row.TryGetProperty(name, out var v) && decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0m;
                            string Str(string name) => row.TryGetProperty(name, out var v) ? v.ToString() : "";
                            var when = DateTime.TryParse(Str("finished_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.UtcNow;
                            trips.Add(new PhoneTripItem(Str("cargo"), Str("origin"), Str("destination"), (double)Dec("distance_km"), Dec("contract_rate_brl_km"), Dec("net_brl"), when));
                        }
                    }
                    _phoneOfficialTrips.Clear();
                    _phoneOfficialTrips.AddRange(trips);
                    _phoneTripsLastRefreshUtc = DateTime.UtcNow;
                    _driverPhone?.UpdateTripHistory(_phoneOfficialTrips);
                }
            }

            if (refreshDocuments)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/documents");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
                using var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var documents = new List<PhoneDocumentItem>();
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("documents", out var rows) && rows.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var row in rows.EnumerateArray().Take(20))
                        {
                            string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.ToString() : "";
                            var data = row.TryGetProperty("document_data", out var payload) && payload.ValueKind == JsonValueKind.Object ? payload : default;
                            var cargo = data.ValueKind == JsonValueKind.Object ? Str(data, "cargo") : "";
                            var origin = data.ValueKind == JsonValueKind.Object ? Str(data, "origin") : "";
                            var destination = data.ValueKind == JsonValueKind.Object ? Str(data, "destination") : "";
                            var when = DateTime.TryParse(Str(row, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTime.UtcNow;
                            documents.Add(new PhoneDocumentItem(Str(row, "title"), cargo, $"{origin} → {destination}", true, when));
                        }
                    }
                    _phoneOfficialDocuments.Clear();
                    _phoneOfficialDocuments.AddRange(documents);
                    _phoneDocumentsLastRefreshUtc = DateTime.UtcNow;
                    UpdatePhoneOfficialDocumentsWithPendingLocal();
                }
            }
        }
        catch (Exception ex)
        {
            // Nunca apague o último snapshot oficial por falha transitória.
            App.WriteUiCrashLog("DriverPhone.OfficialHistory", ex);
        }
        finally { _phoneHistoryRefreshBusy = false; }
    }

    private async Task RefreshPhoneOfficialEconomyAsync()
    {
        if (_driverPhone is null || _phoneEconomyRefreshBusy ||
            DateTime.UtcNow - _phoneEconomyLastRefreshUtc < TimeSpan.FromMinutes(10)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        _phoneEconomyRefreshBusy = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/economy");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            static decimal DecimalValue(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) && decimal.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : 0m;
            static string StringValue(JsonElement e, string name) =>
                e.TryGetProperty(name, out var v) ? v.ToString() : "";

            var balance = root.TryGetProperty("account", out var account) ? DecimalValue(account, "balanceBrl") : 0m;
            var ledger = new List<PhoneLedgerItem>();
            if (root.TryGetProperty("ledger", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray().Take(20))
                {
                    var at = DateTime.TryParse(StringValue(row, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                        ? parsed : DateTime.UtcNow;
                    ledger.Add(new PhoneLedgerItem(
                        StringValue(row, "description"),
                        DecimalValue(row, "amount_brl"),
                        at));
                }
            }
            _phoneEconomyLastRefreshUtc = DateTime.UtcNow;
            _driverPhone?.UpdateOfficialBank(balance, ledger);
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DriverPhone.OfficialEconomy", ex);
        }
        finally { _phoneEconomyRefreshBusy = false; }
    }

    private async void DriverPhone_StampCurrentInvoiceRequested(object? sender, EventArgs e)
    {
        if (!_tripDocumentPending || _pendingTripTelemetry is null) return;
        var data = _pendingTripTelemetry;
        // O gate físico já mantém o freio de estacionamento aplicado. Não usamos
        // o snapshot antigo capturado quando a carga foi detectada para validar o freio,
        // pois ele pode continuar false mesmo depois do bloqueio ter sido aplicado.
        var live = LastTelemetry ?? data;
        if (Math.Abs(live.SpeedKph) > 1.0f)
        {
            StatusText.Text = "TransPoli • pare o caminhão para carimbar a nota pelo celular";
            _driverPhone?.SetStampResult(false, "PARE O CAMINHÃO E TENTE NOVAMENTE");
            return;
        }
        EnsureLocalTripDocument(data);
        // O celular carimba somente o documento identificado da operação atual.
        // Carga/rota são dados de apresentação e nunca podem autorizar uma viagem moderna.
        var current = _documents
            .Where(x => !string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(_operationInvoiceId)
                 && string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(_operationTripId)
                    && string.Equals(x.TripId, _operationTripId, StringComparison.OrdinalIgnoreCase)));
        if (current is null)
        {
            _driverPhone?.SetStampResult(false, "NOTA ATUAL NÃO LOCALIZADA");
            return;
        }
        if (_invoiceStampBusy) return;
        _invoiceStampBusy = true;
        try
        {
        if (string.Equals(current.Status, "Carimbado", StringComparison.OrdinalIgnoreCase)) { _driverPhone?.SetStampResult(true, "NOTA JÁ CARIMBADA"); return; }
        var previousStatus = current.Status;
        var previousRecordedAtUtc = current.RecordedAtUtc;
        var previousStampedAtUtc = current.StampedAtUtc;
        current.Status = "Carimbado";
        if (current.RecordedAtUtc == default) current.RecordedAtUtc = DateTime.UtcNow;
        current.StampedAtUtc ??= DateTime.UtcNow;
        if (!TrySaveOperations())
        {
            current.Status = previousStatus;
            current.RecordedAtUtc = previousRecordedAtUtc;
            current.StampedAtUtc = previousStampedAtUtc;
            _driverPhone?.SetStampResult(false, "FALHA AO SALVAR CARIMBO • TENTE NOVAMENTE");
            return;
        }
        UpdateOpsCounters();
        await AuthorizePendingTripAsync(data);
        StatusText.Text = $"TransPoli • nota {current.Reference} carimbada pelo celular • viagem liberada";
        UpdateDriverPhone(data);
        _driverPhone?.SetStampResult(true, "NOTA CARIMBADA • VIAGEM LIBERADA");
        }
        finally { _invoiceStampBusy = false; }
    }

    private void DriverPhone_InvoiceViewRequested(string reference)
    {
        var document = _documents
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefault(x => string.Equals(x.Reference, reference, StringComparison.OrdinalIgnoreCase));
        if (document is not null)
        {
            if (Visibility != Visibility.Visible) Show();
            WindowState = WindowState.Normal;
            Activate();
            ShowStoredInvoiceDocument(document);
        }
        else StatusText.Text = "TransPoli • documento arquivado não localizado";
    }

        private void DriverPhone_PoliPassReceiptRequested(long eventId)
    {
        // EventId pode reiniciar entre sessões do conector. A lista do celular é
        // cronológica; para eventos repetidos abrimos sempre o registro persistido
        // mais recente, evitando cair em um comprovante antigo com o mesmo número.
        var record = _poliPassRecords
            .Where(x => x.EventId == eventId && x.Amount > 0)
            .OrderByDescending(x => x.RecordedAtUtc)
            .FirstOrDefault();
        if (record is null) { StatusText.Text = "TransPoli • comprovante PoliPass não localizado"; return; }
        if (Visibility != Visibility.Visible) Show();
        WindowState = WindowState.Normal;
        Activate();
        ShowPoliPassReceipt(record);
    }

    private async void DriverPhone_CompleteRefuelRequested(decimal pricePerLiter, string station, string city)
    {
        // O celular coleta apenas os dados comerciais. Litros, identidade física,
        // persistência, recibo, outbox e Banco continuam no fluxo único existente.
        var telemetry = _pendingRefuelTelemetry;
        var liters = _pendingRefuelLiters;
        if (telemetry is null || liters <= 0)
        {
            StatusText.Text = "TransPoli • abastecimento pendente não localizado";
            return;
        }
        try
        {
            await RegisterFuelPaymentV13Async(telemetry, liters, pricePerLiter, station, city);
            if (LastTelemetry is { } live) UpdateDriverPhone(live);
        }
        catch (Exception ex) { App.WriteUiCrashLog("PhoneRefuel", ex); }
    }

    private void TogglePhone()
    {
        if (_driverPhone is null || !_driverPhone.IsLoaded)
        {
            _driverPhone = new DriverPhoneWindow();
            _driverPhone.StampCurrentInvoiceRequested += DriverPhone_StampCurrentInvoiceRequested;
            _driverPhone.CompleteRefuelRequested += DriverPhone_CompleteRefuelRequested;
            _driverPhone.PoliPassReceiptRequested += DriverPhone_PoliPassReceiptRequested;
            _driverPhone.InvoiceViewRequested += DriverPhone_InvoiceViewRequested;
            _driverPhone.Closed += (_, _) => _driverPhone = null;
            _driverPhone.Show();
            if (LastTelemetry is { } phoneTelemetry) UpdateDriverPhone(phoneTelemetry);
            _ = RefreshPhoneOfficialEconomyAsync();
            _ = RefreshPhoneOfficialHistoryAsync();
            _ = RefreshPhoneOfficialProfileAsync();
            return;
        }
        if (_driverPhone.IsVisible) _driverPhone.Hide(); else { _driverPhone.Show(); if (LastTelemetry is { } phoneTelemetry) UpdateDriverPhone(phoneTelemetry); _ = RefreshPhoneOfficialEconomyAsync(); _ = RefreshPhoneOfficialHistoryAsync(); _ = RefreshPhoneOfficialProfileAsync(); }
    }
    private void ToggleHud()
    {
        if (_telemetryOverlay is null) return;
        _hudHotkeyVisible = !_hudHotkeyVisible;
        if (!_hudHotkeyVisible) _telemetryOverlay.Hide();
        else { _telemetryOverlay.Show(); _telemetryOverlay.ApplySettings(_hudSettings); if (LastTelemetry?.Connected == true) UpdateTelemetryOverlay(LastTelemetry); }
    }
    private void ToggleCockpit()
    {
        if (Visibility == Visibility.Visible) { Hide(); return; }
        Show(); WindowState = WindowState.Normal; Topmost = true; Topmost = false; Topmost = true;
        StatusText.Text = "Tablet TransPoli aberto • F10 para ocultar";
    }
    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F9) { TogglePhone(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.F10) { ToggleCockpit(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.F11) { ToggleHud(); e.Handled = true; }
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
            var liveTruckKey = GarageTruckKey(data.TruckBrand, data.TruckModel, data.LicensePlate);
            if (!string.IsNullOrWhiteSpace(_garageTruckKey) &&
                !string.Equals(liveTruckKey, _garageTruckKey, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(liveTruckKey, _garageObservedTruckKey, StringComparison.OrdinalIgnoreCase))
            {
                // Uma identidade nova dispara uma única autorização imediata. Se a rede
                // falhar, o mesmo caminhão aguarda o timer normal em vez de repetir por tick.
                _garageObservedTruckKey = liveTruckKey;
                _lastGarageCheck = DateTime.UtcNow;
                if (!_garageBusy)
                {
                    _garageBusy = true;
                    try { await CheckGarageAuthorizationAsync(); }
                    finally { _garageBusy = false; }
                }
            }
            if (_driverPhone?.IsVisible == true) UpdateDriverPhone(data);
            _tripLifecycle.Observe(data, _tripActive, _tripDocumentPending);
            if (LocalData.Current is { } healthStore)
            {
                var truckKey = string.IsNullOrWhiteSpace(data.TruckId) ? data.LicensePlate : data.TruckId;
                var maxWear = Math.Max(Math.Max(data.WearEngine, data.WearTransmission), Math.Max(Math.Max(data.WearCabin, data.WearChassis), data.WearWheels));
                var truckChanged = !string.Equals(_lastTruckHealthTruckId, truckKey, StringComparison.OrdinalIgnoreCase);
                var periodic = truckChanged || DateTime.UtcNow - _lastTruckHealthSnapshotUtc >= TimeSpan.FromMinutes(15);
                var changed = truckChanged || _lastTruckHealthWear < 0 || Math.Abs(maxWear - _lastTruckHealthWear) >= .025f;
                var critical = !truckChanged && maxWear >= .75f && _lastTruckHealthWear < .75f;
                if (!string.IsNullOrWhiteSpace(truckKey) && (periodic || changed || critical))
                {
                    new LocalTripRepository(healthStore.Db).AppendTruckHealth(truckKey, _localTripId, data);
                    _lastTruckHealthSnapshotUtc = DateTime.UtcNow;
                    _lastTruckHealthWear = maxWear;
                    _lastTruckHealthTruckId = truckKey;
                }
            }
            RefreshActiveTripFinancials();
            UpdateTelemetryOverlay(data);
            ProcessHudEvents(data);
            await ProcessTollgateEventAsync(data);
            UpdateRealInstrumentation(data);
            UpdateDashboardRankingSummary();
            UpdateAutomaticTachographStatus(data);
            UpdateJourneyLayer7(data);
            UpdateEnvironmentLayer9(data);
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

            // Uma única rotina é responsável por recuperar viagens ativas do servidor.
            // A frequência é limitada internamente para não consultar a API a cada tick.
            if (!_tripActive)
                await TryRecoverActiveTrip();

            UpdateAutomaticTrip(data);

            // O job ao vivo do ETS2 é a fonte operacional. Se a sessão local ainda
            // estiver sendo recuperada, mantenha rota/carga e progresso visíveis.
            if (!_tripActive && HasActiveJob(data))
            {
                if (!string.IsNullOrWhiteSpace(data.SourceCity)) _tripRouteOrigin = data.SourceCity;
                if (!string.IsNullOrWhiteSpace(data.DestinationCity)) _tripRouteDestination = data.DestinationCity;
                if (!string.IsNullOrWhiteSpace(data.SourceCompany)) _tripRouteOriginCompany = data.SourceCompany;
                if (!string.IsNullOrWhiteSpace(data.DestinationCompany)) _tripRouteDestinationCompany = data.DestinationCompany;
                if (!string.IsNullOrWhiteSpace(data.Cargo)) _tripCargo = data.Cargo;
                if (data.CargoValueBrl.HasValue) _tripCargoValue = data.CargoValueBrl;
            }
            await RefreshTripProgressAsync(data);

            // Se a viagem começou offline, tenta sincronizar o contrato automaticamente
            // assim que a sessão voltar. A API é idempotente e reaproveita a viagem ativa.
            if (_tripActive &&
                string.IsNullOrWhiteSpace(_serverTripId) &&
                !string.IsNullOrWhiteSpace(SecureTokenStore.Read()) &&
                DateTime.UtcNow - _lastServerTripSyncAttemptUtc >= TimeSpan.FromMinutes(1))
            {
                _lastServerTripSyncAttemptUtc = DateTime.UtcNow;
                var ownerUserId = SecureTokenStore.ReadUserId();
                var startAlreadyQueued = !string.IsNullOrWhiteSpace(_localTripId) &&
                    !string.IsNullOrWhiteSpace(ownerUserId) &&
                    LocalData.Current is { } syncStore &&
                    new LocalSyncQueueRepository(syncStore.Db).HasPendingTripStart(_localTripId, ownerUserId);
                if (!startAlreadyQueued)
                    await CreateServerTrip(data);
            }
            // O outbox pode criar o contrato remoto em segundo plano. Assim que o
            // server_id estiver durável no SQLite, promova-o para a sessão em memória
            // sem consultar a API novamente.
            if (_tripActive && string.IsNullOrWhiteSpace(_serverTripId) &&
                !string.IsNullOrWhiteSpace(_localTripId) && LocalData.Current is { } mappedStore)
            {
                var ownerUserId = SecureTokenStore.ReadUserId();
                if (!string.IsNullOrWhiteSpace(ownerUserId))
                {
                    var mappedServerId = new LocalTripRepository(mappedStore.Db).GetServerId(_localTripId, ownerUserId);
                    if (!string.IsNullOrWhiteSpace(mappedServerId))
                    {
                        var mappedTrips = new LocalTripRepository(mappedStore.Db);
                        var persistedRate = mappedTrips.GetRatePerKm(_localTripId, ownerUserId);
                        if (persistedRate.HasValue)
                            _localTripRatePerKm = JourneyEconomyCalculator.SanitizeRate(persistedRate.Value);
                        _serverTripId = mappedServerId;
                        if (!TrySaveSessionState())
                        {
                            _truckLocked = true;
                            App.WriteUiCrashLog("TripSync.PromoteServerId",
                                new InvalidOperationException("Server trip ID obtido pela outbox, mas a TripSession não pôde ser persistida."));
                            StatusText.Text = "TransPoli • contrato sincronizado • falha ao persistir vínculo local";
                            return;
                        }
                        // O vínculo remoto já está durável. O próximo checkpoint normal
                        // leva telemetria; não há motivo para um POST extra neste tick.
                    }
                }
            }
            // Telemetria bruta permanece local. O servidor recebe apenas um snapshot operacional
            // espaçado para presença/visão compartilhada; 15 min evita consumir a cota
            // diária com RPM, marcha e instrumentos que não precisam de persistência oficial.
            if (DateTime.UtcNow - _lastLiveTelemetrySentAtUtc >= TimeSpan.FromMinutes(15)) await SendLiveTelemetrySample(data);
            if (_tripActive && !string.IsNullOrWhiteSpace(_localTripId) && DateTime.UtcNow - _lastLocalTelemetrySavedAtUtc >= TimeSpan.FromSeconds(2))
            {
                SaveLocalTelemetrySample(data);
            }
            // A viagem é registrada localmente a cada poucos segundos; o servidor precisa apenas
            // de checkpoints para Diretoria/recuperação. Eventos críticos (início/fim)
            // continuam usando force=true nos pontos próprios.
            if (_tripActive && !string.IsNullOrWhiteSpace(_serverTripId) && DateTime.UtcNow - _lastTelemetrySentAtUtc >= TimeSpan.FromMinutes(10)) await SendTelemetrySample(data);
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("Telemetry.RefreshLoop", ex);
            SetDisconnected();
        }
        finally { _refreshBusy = false; }
    }

    private decimal GetHudRevenue() => _tripActive ? Math.Round((decimal)Math.Max(0f, _lastOdometer - _tripStartOdometer) * (decimal)Math.Max(0.01, _localTripRatePerKm), 2) : 0m;
    private decimal GetHudExpenses() => _tripActive && _localTripId != null && LocalData.Current is { } s ? GetLocalEconomy(s.Db, _localTripId!, "-SUM(CASE WHEN amount < 0 THEN amount ELSE 0 END)") : 0m;
    private decimal GetHudNet() => GetHudRevenue() - GetHudExpenses();
    private static decimal GetLocalEconomy(TransPoliDb db, string tripId, string expression)
    {
        using var c = db.Connection.CreateCommand();
        c.CommandText = $"SELECT COALESCE({expression},0) FROM economy_transaction WHERE trip_id=@trip AND owner_user_id=@owner;";
        c.Parameters.AddWithValue("@trip", tripId);
        c.Parameters.AddWithValue("@owner", SecureTokenStore.ReadUserId() ?? "");
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
    }

    private void UpdateTelemetryOverlay(TelemetrySnapshot data)
    {
        try
        {
            if (!_hudSettings.Enabled || !_hudHotkeyVisible)
            {
                _telemetryOverlay?.Hide();
                return;
            }
            if (_telemetryOverlay is null) _telemetryOverlay = new TelemetryOverlayWindow();
            _telemetryOverlay.ApplyVisualSettings();
            if (!_telemetryOverlay.IsVisible) _telemetryOverlay.Show();
            _telemetryOverlay.Topmost = true;
            _telemetryOverlay.UpdateTelemetry(data, _tripActive, _tripStartOdometer,
                data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : data.RouteDistanceKm);
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("TelemetryOverlay", ex);
        }
    }

    private void ProcessHudEvents(TelemetrySnapshot data)
    {
        if (_telemetryOverlay is null || !_hudSettings.Enabled || !_hudSettings.ShowAlerts) return;
        void Alert(string key, string message, bool critical = false)
        {
            var now = DateTime.UtcNow;
            var cooldown = critical ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(7);
            if (key == _lastHudEventKey && now - _lastHudEventAtUtc < TimeSpan.FromSeconds(20)) return;
            if (now - _lastHudEventAtUtc < cooldown) return;
            _lastHudEventKey = key;
            _lastHudEventAtUtc = now;
            _telemetryOverlay.ShowEvent(message);
        }

        if (data.TollgatePaid && data.TollgateAmount > 0 && data.TollgateEventId > 0)
        {
            var tollAlertKey = $"toll-{data.TollgateEventId}-{data.TollgateAmount}-{Math.Round(data.OdometerKm, 1):0.0}";
            var alreadyArchived = _poliPassRecords.Any(x => x.EventId == data.TollgateEventId &&
                Math.Abs(x.SourceAmount - (decimal)data.TollgateAmount) < 0.01m &&
                Math.Abs(x.OdometerKm - data.OdometerKm) < 0.5f);
            if (!alreadyArchived) Alert(tollAlertKey, "POLIPASS • PASSAGEM DETECTADA • PROCESSANDO NO BANCO TRANSPOLI");
        }
        if (data.FineAmount > 0 && data.FineAmount != _lastHudFineAmount)
        {
            _lastHudFineAmount = data.FineAmount;
            Alert("fine-" + data.FineAmount, string.IsNullOrWhiteSpace(data.FineOffence) ? $"MULTA DETECTADA • {data.FineAmount:0.00}" : $"MULTA • {data.FineOffence} • {data.FineAmount:0.00}");
        }
        if (data.FuelWarning && !_lastHudFuelWarning) Alert("fuel", "ALERTA • COMBUSTÍVEL BAIXO");
        var airWarning = data.AirPressureWarning || data.AirPressureEmergency;
        if (airWarning && !_lastHudAirWarning) Alert("air", data.AirPressureEmergency ? "CRÍTICO • PRESSÃO DE AR" : "ALERTA • PRESSÃO DE AR", data.AirPressureEmergency);
        if (data.OilPressureWarning && !_lastHudOilWarning) Alert("oil", "CRÍTICO • PRESSÃO DO ÓLEO", true);
        if (data.WaterTemperatureWarning && !_lastHudTemperatureWarning) Alert("temperature", "CRÍTICO • TEMPERATURA DO MOTOR", true);
        if (data.BatteryVoltageWarning && !_lastHudBatteryWarning) Alert("battery", "ALERTA • TENSÃO DA BATERIA");
        if (data.AdBlueWarning && !_lastHudAdBlueWarning) Alert("adblue", "ALERTA • ADBLUE BAIXO");
        if (data.CargoDamage + 0.001f < _lastHudCargoDamage) _lastHudCargoDamage = data.CargoDamage;
        if (data.CargoDamage > _lastHudCargoDamage + 0.001f && data.CargoDamage > 0)
            Alert("cargo-damage", $"ATENÇÃO • DANO À CARGA {data.CargoDamage * 100:0.0}%");
        if (_tripActive && !_lastHudTripActive) Alert("trip-start", "VIAGEM INICIADA • BOA ROTA");
        if (data.JobCancelled && _lastHudTripActive) Alert("trip-cancel", "ATENÇÃO • TRABALHO CANCELADO");
        if (!_tripActive && _lastHudTripActive && (data.JobDelivered || data.JobFinished)) Alert("trip-finish", "ENTREGA CONFIRMADA • VIAGEM FINALIZADA");

        _lastHudFuelWarning = data.FuelWarning;
        _lastHudAirWarning = airWarning;
        _lastHudOilWarning = data.OilPressureWarning;
        _lastHudTemperatureWarning = data.WaterTemperatureWarning;
        _lastHudBatteryWarning = data.BatteryVoltageWarning;
        _lastHudAdBlueWarning = data.AdBlueWarning;
        _lastHudTripActive = _tripActive;
        _lastHudCargoDamage = data.CargoDamage;
    }

    private void HudSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new HudSettingsWindow(_hudSettings, ApplyHudSettings) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("HudSettings", ex);
            MessageBox.Show("Não foi possível abrir as configurações da HUD.\\n\\n" + ex.Message, "TransPoli", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyHudSettings(HudSettings settings)
    {
        _hudSettings = settings;
        _telemetryOverlay?.ApplySettings(_hudSettings);
        if (_hudSettings.Enabled)
        {
            if (LastTelemetry?.Connected == true)
                UpdateTelemetryOverlay(LastTelemetry);
            else
                _telemetryOverlay?.ShowDisconnected();
        }
    }

    private void HideTelemetryOverlay()
    {
        try { _telemetryOverlay?.Hide(); } catch { }
    }

    private async Task ProcessTollgateEventAsync(TelemetrySnapshot data)
    {
        if (!data.TollgatePaid || data.TollgateAmount <= 0 || data.TollgateEventId <= 0) return;

        var basePerAxle = Math.Round((decimal)data.TollgateAmount, 2, MidpointRounding.AwayFromZero);
        var combination = RoadCombinationTelemetry.Build(data);
        var axleCount = combination.TotalAxleCount ?? Math.Max(1, combination.TruckAxleCount ?? 1);
        var amountBrl = Math.Round(basePerAxle * axleCount, 2, MidpointRounding.AwayFromZero);
        var eventKey = $"{data.TollgateEventId}:{data.TollgateAmount}:{Math.Round(data.OdometerKm, 1)}";
        if (!_tollgateEventsInFlight.Add(eventKey)) return;
        try
        {
            var persistedPass = _poliPassRecords.FirstOrDefault(x => x.EventId == data.TollgateEventId &&
                Math.Abs(x.SourceAmount - basePerAxle) < 0.01m &&
                Math.Abs(x.OdometerKm - data.OdometerKm) < 0.5f);
            if (persistedPass is not null)
            {
                // O comprovante persistido confirma a passagem física, mas não prova
                // que a outbox da cobrança foi gravada. Recrie a mesma operação
                // deterministicamente antes de considerar o pedágio concluído.
                var persistedAxles = persistedPass.TotalAxles ?? persistedPass.TruckAxles ?? axleCount;
                var persistedTripId = Guid.TryParse(_serverTripId,out _) ? _serverTripId : null;
                var retryPayload = new
                {
                    action="toll_payment",
                    amount=persistedPass.Amount,
                    baseAmount=persistedPass.SourceAmount,
                    axleCount=persistedAxles,
                    currency="BRL",
                    tripId=persistedTripId,
                    sourceKey=$"polipass-{persistedPass.EventId}-{Math.Round(persistedPass.OdometerKm,1):0.0}",
                    odometerKm=persistedPass.OdometerKm,
                    truckBrand=persistedPass.TruckBrand,
                    truckModel=persistedPass.TruckModel,
                    licensePlate=persistedPass.LicensePlate
                };
                var retryQueued = _serverSync.QueueExpense(persistedTripId,retryPayload);
                if (retryQueued)
                {
                    InvalidatePhoneOfficialCache(economy: true);
                    _lastProcessedTollgateEventId = data.TollgateEventId;
                    StatusText.Text=$"TransPoli • PoliPass já registrado • sincronização garantida";
                    // A outbox periódica fará o envio. Não force telemetria nem flush
                    // a cada repetição do mesmo pulso físico do pedágio.
                }
                else
                {
                    StatusText.Text="TransPoli • PoliPass preservado • sincronização ainda não pôde ser persistida";
                }
                return;
            }

            // PoliPass é local-first: o evento real do ETS2 gera cobrança e comprovante
            // imediatamente. A API apenas sincroniza o mesmo lançamento depois.
            var savedPass = new PoliPassRecord
            {
                EventId=data.TollgateEventId,
                RecordedAtUtc=DateTime.UtcNow,
                Amount=amountBrl,
                SourceAmount=basePerAxle,
                SourceCurrency="ETS2 / eixo",
                TruckBrand=data.TruckBrand ?? "",
                TruckModel=data.TruckModel ?? "",
                LicensePlate=data.LicensePlate ?? "",
                CargoMassKg=data.CargoMassKg,
                OdometerKm=data.OdometerKm,
                TotalAxles=combination.TotalAxleCount,
                TruckAxles=combination.TruckAxleCount,
                Trailers=combination.Trailers.Select(x=>new PoliPassTrailerRecord{Index=x.Index,Brand=x.Brand ?? "",Name=x.Name ?? "",BodyType=x.BodyType ?? "",LicensePlate=x.LicensePlate ?? "",Axles=x.AxleCount}).ToList()
            };
            _poliPassRecords.Insert(0,savedPass);
            if (!TrySaveOperations())
            {
                // O comprovante é parte da mesma operação do débito. Se ele não puder
                // ser persistido, não avance silenciosamente para Banco/outbox.
                _poliPassRecords.Remove(savedPass);
                App.WriteUiCrashLog("PoliPass.PersistReceipt",
                    new InvalidOperationException("Falha ao persistir o comprovante PoliPass antes da cobrança."));
                StatusText.Text = "TransPoli • pedágio detectado, mas o comprovante não pôde ser salvo • cobrança não confirmada";
                _telemetryOverlay?.ShowEvent("POLIPASS • FALHA AO SALVAR COMPROVANTE • TENTANDO NOVAMENTE");
                return;
            }

            var localSourceKey = $"toll-{data.TollgateEventId}-{Math.Round(data.OdometerKm, 1):0.0}";
            if (LocalData.Current is { } tollStore)
            {
                new LocalEconomyRepository(tollStore.Db).AddExpense(
                    localSourceKey,
                    string.IsNullOrWhiteSpace(_localTripId) ? null : _localTripId,
                    "toll_expense",
                    $"PoliPass • {axleCount} eixos × R$ {basePerAxle:0.00} • R$ {amountBrl:0.00}",
                    amountBrl,
                    savedPass.RecordedAtUtc);
            }

            _phoneTollHistory.RemoveAll(x => x.EventId == data.TollgateEventId && !x.Paid);
            _phoneTollHistory.Insert(0,new PhoneTollItem(
                data.TollgateEventId,amountBrl,savedPass.RecordedAtUtc,
                $"{axleCount} eixos • R$ {basePerAxle:0.00}/eixo"));
            if (_phoneTollHistory.Count > 30) _phoneTollHistory.RemoveRange(30,_phoneTollHistory.Count-30);
            _driverPhone?.UpdateTollHistory(_phoneTollHistory);
            RefreshActiveTripFinancials(force:true);
            _lastProcessedTollgateEventId=data.TollgateEventId;
            StatusText.Text=$"TransPoli • PoliPass cobrado • {axleCount} eixos × R$ {basePerAxle:0.00} = R$ {amountBrl:0.00}";
            _telemetryOverlay?.ShowEvent($"POLIPASS • {axleCount} EIXOS • R$ {amountBrl:0.00}");

            // PoliPass usa a mesma outbox durável das demais operações. O sourceKey
            // é estável por passagem e o servidor aplica o débito de forma idempotente,
            // então queda de internet/restart não perde nem duplica a cobrança.
            var tripId=Guid.TryParse(_serverTripId,out _)?_serverTripId:null;
            var payload=new
            {
                action="toll_payment",
                amount=amountBrl,
                baseAmount=basePerAxle,
                axleCount,
                currency="BRL",
                tripId,
                sourceKey=$"polipass-{data.TollgateEventId}-{Math.Round(data.OdometerKm,1):0.0}",
                odometerKm=data.OdometerKm,
                truckBrand=data.TruckBrand,
                truckModel=data.TruckModel,
                licensePlate=data.LicensePlate
            };
            var queued=_serverSync.QueueExpense(tripId, payload);
            if (queued) InvalidatePhoneOfficialCache(economy: true);
            StatusText.Text += queued
                ? " • sincronização segura enfileirada"
                : " • ALERTA: cobrança local preservada, mas a sincronização não foi persistida";
        }
        finally { _tollgateEventsInFlight.Remove(eventKey); }
    }

    private void ApplyCockpitOperatingState(TelemetrySnapshot data)
    {
        var moving = Math.Abs(data.SpeedKph) > 0.5f;
        var warning = data.FuelWarning || data.WaterTemperatureWarning ||
                      data.AirPressureWarning || data.AirPressureEmergency ||
                      data.OilPressureWarning || data.BatteryVoltageWarning ||
                      data.AdBlueWarning;

        if (!data.Connected)
        {
            ConnectionText.Text = "ETS2 DESCONECTADO";
            StatusText.Text = "Aguardando telemetria";
            ConnectionDot.Fill = FindResource("TextMuted") as System.Windows.Media.Brush;
            NotificationStatusText.Text = "◇";
            NotificationStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            TripLiveText.Text = "AGUARDANDO";
            TripLiveText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
            return;
        }

        ConnectionText.Text = "ETS2 CONECTADO";
        ConnectionDot.Fill = FindResource(warning ? "Red" : "Green") as System.Windows.Media.Brush;

        if (warning)
        {
            StatusText.Text = "Diagnóstico • atenção necessária";
            NotificationStatusText.Text = "◆";
            NotificationStatusText.Foreground = FindResource("Red") as System.Windows.Media.Brush;
        }
        else if (data.GamePaused)
        {
            StatusText.Text = "ETS2 pausado • sistema em espera";
            NotificationStatusText.Text = "◇";
            NotificationStatusText.Foreground = FindResource("GoldBright") as System.Windows.Media.Brush;
        }
        else if (moving)
        {
            StatusText.Text = "Condução ativa • telemetria nominal";
            NotificationStatusText.Text = "◇";
            NotificationStatusText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        }
        else if (data.EngineEnabled)
        {
            StatusText.Text = "Motor ligado • veículo parado";
            NotificationStatusText.Text = "◇";
            NotificationStatusText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        }
        else
        {
            StatusText.Text = "Sistema em espera • motor desligado";
            NotificationStatusText.Text = "◇";
            NotificationStatusText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        }

        TripLiveText.Text = moving ? "EM MOVIMENTO" : data.EngineEnabled ? "EM ESPERA" : "AGUARDANDO";
        TripLiveText.Foreground = FindResource(warning ? "Red" : moving ? "Green" : "GoldBright") as System.Windows.Media.Brush;
    }

    private void UpdateDashboardOperationGuidance(TelemetrySnapshot data)
    {
        if (DashboardActionTitleText == null || DashboardActionHintText == null ||
            DashboardDocumentStateText == null || DashboardDocumentHintText == null ||
            DashboardSyncStateText == null || DashboardSyncHintText == null) return;

        var status = TripStatusText?.Text?.Trim().ToUpperInvariant() ?? string.Empty;
        var hasJob = HasActiveJob(data);

        if (!data.Connected)
        {
            DashboardActionTitleText.Text = "RECONECTAR AO ETS2";
            DashboardActionHintText.Text = "O computador de bordo mantém os dados locais. Abra o ETS2 para retomar detecção, viagem e jornada.";
            DashboardDocumentStateText.Text = _tripActive ? "ARQUIVO LOCAL PRESERVADO" : "SEM OPERAÇÃO ATIVA";
            DashboardDocumentStateText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            DashboardDocumentHintText.Text = "Documentos já persistidos continuam disponíveis offline.";
            DashboardSyncStateText.Text = "MODO OFFLINE";
            DashboardSyncStateText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            DashboardSyncHintText.Text = "Eventos ficam locais/outbox até a conexão voltar.";
            return;
        }

        DashboardSyncStateText.Text = "LOCAL-FIRST";
        DashboardSyncStateText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        DashboardSyncHintText.Text = "Eventos importantes são persistidos antes da sincronização oficial.";

        if (status.Contains("PENDENTE") || status.Contains("CARIMBO"))
        {
            DashboardActionTitleText.Text = "REGULARIZAR DOCUMENTAÇÃO";
            DashboardActionHintText.Text = "Abra Documentos, confira a DANFE e conclua o carimbo antes de liberar o início da operação.";
            DashboardDocumentStateText.Text = "DANFE PENDENTE";
            DashboardDocumentStateText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            DashboardDocumentHintText.Text = "A viagem permanece protegida pelo gate documental.";
            return;
        }

        if (status.Contains("ENTREGUE") || status.Contains("FINALIZADA"))
        {
            DashboardActionTitleText.Text = "CONFERIR FECHAMENTO";
            DashboardActionHintText.Text = "A entrega foi reconhecida. Acompanhe sincronização, acerto oficial e documentos pela Central de Viagens.";
            DashboardDocumentStateText.Text = "ARQUIVO DA VIAGEM";
            DashboardDocumentStateText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
            DashboardDocumentHintText.Text = "DANFE e jornada permanecem vinculadas ao mesmo TripId.";
            return;
        }

        if (_tripActive || status.Contains("ANDAMENTO") || status.Contains("INICIADA") || status.Contains("RECUPERADA"))
        {
            DashboardActionTitleText.Text = "ACOMPANHAR OPERAÇÃO";
            DashboardActionHintText.Text = "Continue a viagem no ETS2. O TransPoli registra jornada, custos, pedágios e eventos no mesmo prontuário.";
            DashboardDocumentStateText.Text = "DOCUMENTAÇÃO VINCULADA";
            DashboardDocumentStateText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
            DashboardDocumentHintText.Text = "Abra Documentos para consultar DANFE e carimbo desta operação.";
            return;
        }

        if (hasJob)
        {
            DashboardActionTitleText.Text = "PREPARAR DOCUMENTAÇÃO";
            DashboardActionHintText.Text = "Carga real reconhecida no ETS2. Confirme o fluxo documental para preparar a operação TransPoli.";
            DashboardDocumentStateText.Text = "CARGA RECONHECIDA";
            DashboardDocumentStateText.Foreground = FindResource("GoldBright") as System.Windows.Media.Brush;
            DashboardDocumentHintText.Text = "A DANFE será criada e vinculada à identidade da viagem.";
            return;
        }

        DashboardActionTitleText.Text = "ESCOLHER PRÓXIMA CARGA";
        DashboardActionHintText.Text = "Abra o Mercado de Cargas e aceite um trabalho real no ETS2. O TransPoli fará a detecção automaticamente.";
        DashboardDocumentStateText.Text = "AGUARDANDO VIAGEM";
        DashboardDocumentStateText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
        DashboardDocumentHintText.Text = "Nenhuma documentação operacional precisa de ação agora.";
    }

    private void UpdateRealInstrumentation(TelemetrySnapshot data)
    {
        // Esta camada só apresenta campos que já existem no snapshot real da telemetria.
        ApplyCockpitOperatingState(data);
        UpdateDashboardOperationGuidance(data);

        RpmGaugeText.Text = data.Rpm > 0 ? data.Rpm.ToString("0") : "0";

        // Camada 3: ponteiros visuais respondem somente à telemetria real já disponível.
        var speed = Math.Clamp(Math.Abs(data.SpeedKph), 0f, 160f);
        var speedAngle = -130d + (speed / 160d) * 260d;
        if (SpeedNeedleRotation != null) SpeedNeedleRotation.Angle = speedAngle;
        if (SpeedNeedle != null) SpeedNeedle.Opacity = data.Connected ? 1.0 : 0.32;

        GearGaugeText.Text = data.Gear == 0 ? "N" : data.Gear < 0 ? "R" : data.Gear.ToString();
        EngineGaugeStatusText.Text = data.EngineEnabled ? "LIGADO" : "DESLIGADO";
        EngineGaugeStatusText.Foreground = FindResource(data.EngineEnabled ? "Green" : "TextMuted") as System.Windows.Media.Brush;

        FuelText.Text = $"{data.FuelLiters:0.0} L";
        FuelRangeGaugeText.Text = data.FuelRangeKm > 0 ? $"AUTONOMIA {data.FuelRangeKm:0} km" : "AUTONOMIA N/D";
        if (DashboardFuelVisualFill != null)
        {
            var fuelPercent = data.FuelCapacityLiters > 0 ? Math.Clamp(data.FuelLiters / data.FuelCapacityLiters * 100.0, 0.0, 100.0) : 0.0;
            DashboardFuelVisualFill.Width = Math.Max(0, fuelPercent * 2.1);
        }
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
                ? target.TryFindResource("Green") as System.Windows.Media.Brush
                : target.TryFindResource("TextMuted") as System.Windows.Media.Brush;
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
        if (!data.JobDelivered && !data.JobFinished)
            _deliveryEventArmed = true;

        if (!_tripActive && !string.IsNullOrWhiteSpace(_manualTripFinishSignature))
        {
            var currentSignature = BuildJobSignature(data);
            if (!HasActiveJob(data))
            {
                _manualTripFinishSignature = null;
            }
            else if (string.Equals(currentSignature, _manualTripFinishSignature, StringComparison.OrdinalIgnoreCase))
            {
                TripStatusText.Text = "VIAGEM ENCERRADA MANUALMENTE • aguardando nova carga";
                TripRouteText.Text = "Nenhuma viagem ativa";
                TripCargoText.Text = "";
                TripDistanceText.Text = "0 km";
                TripDurationText.Text = "00:00:00";
                TripProgressText.Text = "0%";
                TripDistanceLiveText.Text = "0 / 0 km";
                TripRemainingText.Text = "— km restantes";
                TripProgressFill.Width = 0;
                DashboardTachRouteText.Text = "Nenhuma viagem ativa";
                DashboardTachCargoText.Text = "Carga: —";
                return;
            }
            else
            {
                _manualTripFinishSignature = null;
            }
        }

        // Se o estado salvo aponta para uma viagem antiga, mas o ETS2 já mudou
        // claramente para outra carga/rota, a sessão antiga não pode bloquear a nova.
        if (_tripActive && IsClearlyDifferentJob(data))
        {
            _ = CloseStaleTripAndPrepareNewAsync(data);
            return;
        }

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
                    // O gate permanece ativo, mas não reabre o modal do computador
                    // de bordo em polling. A liberação primária é feita em Documentos
                    // no celular; a tela grande continua disponível sob ação explícita.
                    TripStatusText.Text = "DOCUMENTAÇÃO PENDENTE • carimbe a DANFE no celular";
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
        if (_tripActive)
        {
            var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            UpdateTripCard(data, distance);

            // Somente uma entrega/finalização explícita do ETS2 liquida a viagem.
            // Perder a carga temporariamente durante uma reconexão não encerra nada.
            // A flag explícita de entrega/finalização do ETS2 é a autoridade para
            // encerrar a viagem que já está ativa. O jogo pode limpar ou trocar os textos
            // Cargo/Origem/Destino no mesmo frame da entrega; por isso esses textos não
            // podem bloquear o fechamento. _deliveryEventArmed garante que não estamos
            // reutilizando uma flag antiga que já estava ligada ao iniciar/reconectar.
            if (_deliveryEventArmed && (data.JobDelivered || data.JobFinished))
            {
                _deliveryEventArmed = false;
                TripStatusText.Text = "ENTREGA CONFIRMADA PELO ETS2 • finalizando viagem...";
                TripDurationText.Text = FormatDuration(elapsed);
                _ = FinishAutomaticTrip(data);
                return;
            }

            TripStatusText.Text = data.CargoLoaded
                ? (_truckLocked ? "VIAGEM • CAMINHÃO BLOQUEADO" : "VIAGEM EM ANDAMENTO")
                : "VIAGEM EM ANDAMENTO • AGUARDANDO TELEMETRIA DO ETS2";
            TripDurationText.Text = FormatDuration(elapsed);
            return;
        }

        // Sem viagem ativa no estado local, a ausência de carga não é motivo para
        // criar nem liquidar nada. A próxima telemetria/recuperação decide o estado.
        TripStatusText.Text = "AGUARDANDO CONFIRMAÇÃO DO ETS2 • viagem preservada";
        TripDurationText.Text = "00:00:00";
    }

    private bool IsClearlyDifferentJob(TelemetrySnapshot data)
    {
        if (!HasActiveJob(data)) return false;
        if (!string.IsNullOrWhiteSpace(_tripCargo) && !string.IsNullOrWhiteSpace(data.Cargo) &&
            !string.Equals(_tripCargo.Trim(), data.Cargo.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(_tripRouteOrigin) && !string.IsNullOrWhiteSpace(data.SourceCity) &&
            !string.Equals(_tripRouteOrigin.Trim(), data.SourceCity.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(_tripRouteDestination) && !string.IsNullOrWhiteSpace(data.DestinationCity) &&
            !string.Equals(_tripRouteDestination.Trim(), data.DestinationCity.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private async Task CloseStaleTripAndPrepareNewAsync(TelemetrySnapshot data)
    {
        // Um job diferente não prova que a TripSession anterior foi entregue.
        // Portanto este caminho nunca liquida economia nem usa a telemetria do novo
        // job para fabricar o encerramento da viagem anterior.
        if (_tripFinishBusy) return;

        var oldLocalId = _localTripId;
        var oldSessionKey = _tripLifecycle.Current.SessionKey;
        if (!string.IsNullOrWhiteSpace(oldLocalId) && LocalData.Current is { } store)
        {
            try
            {
                var closures = new LocalTripClosureRepository(store.Db);
                // Se já existe um snapshot de fechamento, ele é a única fonte válida.
                // ResumePendingTripClosuresAsync concluirá o fechamento sem misturar
                // nenhuma métrica da nova viagem.
                var ownerUserId = SecureTokenStore.ReadUserId();
                if (!string.IsNullOrWhiteSpace(ownerUserId) && closures.GetPending(ownerUserId).Any(x => string.Equals(x.TripId, oldLocalId, StringComparison.OrdinalIgnoreCase)))
                {
                    await ResumePendingTripClosuresAsync(data);
                    return;
                }
            }
            catch
            {
                StatusText.Text = "TransPoli • viagem anterior preservada • fechamento pendente";
                return;
            }
        }

        // Sem snapshot imutável não há evidência suficiente para encerrar a viagem.
        // Preservamos a sessão anterior e impedimos que o job novo a contamine.
        StatusText.Text = string.IsNullOrWhiteSpace(oldSessionKey)
            ? "TransPoli • viagem anterior preservada • aguardando encerramento confirmado"
            : "TransPoli • TripSession anterior preservada • aguardando encerramento confirmado";
    }

    private bool IsTelemetryForCurrentTrip(TelemetrySnapshot data)
    {
        // A entrega é um evento explícito do ETS2. Depois que a carga é entregue,
        // o jogo pode limpar Cargo/Origem/Destino na mesma amostra em que dispara
        // JobDelivered/JobFinished. Portanto, não exigimos que esses textos ainda
        // estejam presentes para autorizar a liquidação da viagem que já está ativa.
        if (!data.JobDelivered && !data.JobFinished) return false;

        var cargoMatches = string.IsNullOrWhiteSpace(_tripCargo) || string.IsNullOrWhiteSpace(data.Cargo)
            || string.Equals(_tripCargo.Trim(), data.Cargo.Trim(), StringComparison.OrdinalIgnoreCase);
        var originMatches = string.IsNullOrWhiteSpace(_tripRouteOrigin) || string.IsNullOrWhiteSpace(data.SourceCity)
            || string.Equals(_tripRouteOrigin.Trim(), data.SourceCity.Trim(), StringComparison.OrdinalIgnoreCase);
        var destinationMatches = string.IsNullOrWhiteSpace(_tripRouteDestination) || string.IsNullOrWhiteSpace(data.DestinationCity)
            || string.Equals(_tripRouteDestination.Trim(), data.DestinationCity.Trim(), StringComparison.OrdinalIgnoreCase);

        // Se o ETS2 ainda fornecer os dados do job, usamos-os como confirmação.
        // Se já tiver limpado os campos após a entrega, a própria flag do evento
        // continua sendo suficiente porque _tripActive identifica a viagem em curso.
        return cargoMatches && originMatches && destinationMatches
            && data.OdometerKm >= _tripStartOdometer - 0.1f;
    }

    private void UpdateTripCard(TelemetrySnapshot data, float distance)
    {
        var origin = string.IsNullOrWhiteSpace(_tripRouteOrigin) ? data.SourceCity : _tripRouteOrigin;
        var destination = string.IsNullOrWhiteSpace(_tripRouteDestination) ? data.DestinationCity : _tripRouteDestination;
        var originCompany = string.IsNullOrWhiteSpace(_tripRouteOriginCompany) ? data.SourceCompany : _tripRouteOriginCompany;
        var destinationCompany = string.IsNullOrWhiteSpace(_tripRouteDestinationCompany) ? data.DestinationCompany : _tripRouteDestinationCompany;
        var planned = GetTripPlannedDistanceKm(data, distance);
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
        var ownerUserId = SecureTokenStore.ReadUserId();
        var store = LocalData.Current;
        if (string.IsNullOrWhiteSpace(ownerUserId) || store is null)
        {
            _tripActive = false;
            _truckLocked = true;
            StatusText.Text = "TransPoli • viagem não iniciada • identidade/armazenamento local indisponível";
            return;
        }

        _tripActive = true; _tripStartedAtUtc = DateTime.UtcNow; _tripStartOdometer = data.OdometerKm; _tripStartFuel = data.FuelLiters; _tripFuelConsumedL = 0; _tripLastFuelLiters = data.FuelLiters; _tripMovingSeconds = 0; _tripLastProgressAtUtc = DateTime.UtcNow;
        // RouteDistanceKm é distância restante; não pode ser usada como total da viagem.
        _tripPlannedDistanceKm = data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : 0; _tripRouteOrigin=data.SourceCity; _tripRouteDestination=data.DestinationCity; _tripRouteOriginCompany=data.SourceCompany; _tripRouteDestinationCompany=data.DestinationCompany; _tripCargo=data.Cargo; _tripCargoValue=data.CargoValueBrl; _serverTripId = null; _localTripId = Guid.NewGuid().ToString("N"); _lastTelemetrySentAtUtc = DateTime.MinValue; _lastLocalTelemetrySavedAtUtc = DateTime.MinValue; _lastServerTripSyncAttemptUtc = DateTime.MinValue;

        try
        {
            var trips = new LocalTripRepository(store.Db);
            _localTripRatePerKm = trips.ResolveRatePerKm(data.Cargo);
            trips.StartTrip(_localTripId, data, null, _localTripRatePerKm, ownerUserId);
            if (!TrySaveSessionState())
                throw new InvalidOperationException("TripSession não pôde ser persistida após o início local.");
            EnsureLocalTripDocument(data);
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("MainWindow.StartAutomaticTrip.LocalPersistence", ex);
            _tripActive = false;
            _truckLocked = true;
            StatusText.Text = "TransPoli • falha ao persistir início da viagem • operação bloqueada";
            return;
        }

        TripStatusText.Text = "VIAGEM INICIADA AUTOMATICAMENTE"; TripRouteText.Text = BuildRoute(data); TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}"; TripDistanceText.Text = "0.0 km"; TripDurationText.Text = "00:00:00"; StatusText.Text = $"TransPoli • viagem iniciada • tarifa local R$ {_localTripRatePerKm:0.00}/km";
        SaveLocalTelemetrySample(data, true);
        await CreateServerTrip(data);
    }

    private Task CreateServerTrip(TelemetrySnapshot data)
    {
        if (string.IsNullOrWhiteSpace(_localTripId))
        {
            StatusText.Text = "TransPoli • viagem local sem identidade • sincronização não iniciada";
            return Task.CompletedTask;
        }

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

        // Início remoto também tem caminho único. A outbox usa trip-start-<localTripId>,
        // recebe o contrato/tarifa oficial e só então confirma o item. Não fazemos
        // POST direto + fallback, evitando criação duplicada e chamadas redundantes.
        var queued = _serverSync.QueueTripStart(_localTripId, payload);
        StatusText.Text = queued
            ? $"TransPoli • viagem iniciada • tarifa local R$ {_localTripRatePerKm:0.00}/km • contrato sincronizando"
            : "TransPoli • viagem local ativa • não foi possível persistir a sincronização do contrato";
        return Task.CompletedTask;
    }

    private async Task SendLiveTelemetrySample(TelemetrySnapshot data)
    {
        var token = SecureTokenStore.Read(); if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var payload = new { deviceId = DeviceIdentity.GetOrCreate(), recordedAt = DateTime.UtcNow, connected = data.Connected, game = data.Game, gamePaused = data.GamePaused, engineEnabled = data.EngineEnabled, electricEnabled = data.ElectricEnabled, speedKph = Math.Abs(data.SpeedKph), speedLimitKph = data.SpeedLimitKph, rpm = data.Rpm, gear = data.Gear, fuelL = data.FuelLiters, fuelRangeKm = data.FuelRangeKm, fuelAvgConsumption = data.FuelAvgConsumption, adblueL = data.AdBlueLiters, oilPressure = data.OilPressure, oilTemperature = data.OilTemperature, waterTemperature = data.WaterTemperature, batteryVoltage = data.BatteryVoltage, odometerKm = data.OdometerKm, airPressure = data.AirPressure, brakeTemperature = data.BrakeTemperature, parkingBrake = data.ParkingBrake, motorBrake = data.MotorBrake, brakeLight = data.BrakeLight, cruiseControl = data.CruiseControl, cruiseSpeedKph = data.CruiseSpeedKph, retarderLevel = data.RetarderLevel, userThrottle = data.UserThrottle, effectiveThrottle = data.EffectiveThrottle, userBrake = data.UserBrake, effectiveBrake = data.EffectiveBrake, wearEngine = data.WearEngine, wearTransmission = data.WearTransmission, wearCabin = data.WearCabin, wearChassis = data.WearChassis, wearWheels = data.WearWheels, cargoDamage = data.CargoDamage, airPressureWarning = data.AirPressureWarning, airPressureEmergency = data.AirPressureEmergency, fuelWarning = data.FuelWarning, adblueWarning = data.AdBlueWarning, oilPressureWarning = data.OilPressureWarning, waterTemperatureWarning = data.WaterTemperatureWarning, batteryVoltageWarning = data.BatteryVoltageWarning, wipers = data.Wipers, blinkerLeftActive = data.BlinkerLeftActive, blinkerRightActive = data.BlinkerRightActive, lightsParking = data.LightsParking, lightsBrake = data.LightsBrake, lightsReverse = data.LightsReverse, lightsHazard = data.LightsHazard, differentialLock = data.DifferentialLock, liftAxle = data.LiftAxle, trailerLiftAxle = data.TrailerLiftAxle, truckBrand = data.TruckBrand, truckModel = data.TruckModel, licensePlate = data.LicensePlate, cargo = data.Cargo, cargoMassKg = data.CargoMassKg, sourceCity = data.SourceCity, destinationCity = data.DestinationCity, sourceCompany = data.SourceCompany, destinationCompany = data.DestinationCompany, plannedDistanceKm = data.PlannedDistanceKm, cargoValueBrl = data.CargoValueBrl, onJob = data.OnJob, specialJob = data.SpecialJob, refuelActive = data.RefuelActive, worldX = data.WorldX, worldY = data.WorldY, worldZ = data.WorldZ, headingDeg = data.HeadingDeg, pitchDeg = data.PitchDeg, rollDeg = data.RollDeg, positionValid = data.PositionValid };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/device/telemetry"); request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}"); request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"); using var response = await _http.SendAsync(request); if (response.IsSuccessStatusCode) _lastLiveTelemetrySentAtUtc = DateTime.UtcNow;
        } catch (Exception ex) { App.WriteUiCrashLog("Telemetry.LiveUpload", ex); }
    }

    private async Task SendTelemetrySample(TelemetrySnapshot data, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_serverTripId)) return; if (!force && DateTime.UtcNow - _lastTelemetrySentAtUtc < TimeSpan.FromSeconds(5)) return; var token = SecureTokenStore.Read(); if (string.IsNullOrWhiteSpace(token)) return;
        try { var payload = new { recordedAt = DateTime.UtcNow, speedKph = Math.Abs(data.SpeedKph), rpm = data.Rpm, gear = data.Gear, fuelL = data.FuelLiters, odometerKm = data.OdometerKm, fuelRangeKm = data.FuelRangeKm, gamePaused = data.GamePaused, engineEnabled = data.EngineEnabled, electricEnabled = data.ElectricEnabled, parkingBrake = data.ParkingBrake, motorBrake = data.MotorBrake, brakeLight = data.BrakeLight, userThrottle = data.UserThrottle, effectiveThrottle = data.EffectiveThrottle, userBrake = data.UserBrake, effectiveBrake = data.EffectiveBrake, airPressure = data.AirPressure, brakeTemperature = data.BrakeTemperature, fuelAvgConsumption = data.FuelAvgConsumption, adblueL = data.AdBlueLiters, oilPressure = data.OilPressure, oilTemperature = data.OilTemperature, waterTemperature = data.WaterTemperature, batteryVoltage = data.BatteryVoltage, speedLimitKph = data.SpeedLimitKph, cruiseControl = data.CruiseControl, cruiseSpeedKph = data.CruiseSpeedKph, worldX = data.WorldX, worldY = data.WorldY, worldZ = data.WorldZ, headingDeg = data.HeadingDeg, pitchDeg = data.PitchDeg, rollDeg = data.RollDeg, positionValid = data.PositionValid, retarderLevel = data.RetarderLevel, cargoDamage = data.CargoDamage, wearEngine = data.WearEngine, wearTransmission = data.WearTransmission, wearCabin = data.WearCabin, wearChassis = data.WearChassis, wearWheels = data.WearWheels, airPressureWarning = data.AirPressureWarning, airPressureEmergency = data.AirPressureEmergency, fuelWarning = data.FuelWarning, adblueWarning = data.AdBlueWarning, oilPressureWarning = data.OilPressureWarning, waterTemperatureWarning = data.WaterTemperatureWarning, batteryVoltageWarning = data.BatteryVoltageWarning, wipers = data.Wipers, blinkerLeftActive = data.BlinkerLeftActive, blinkerRightActive = data.BlinkerRightActive, lightsParking = data.LightsParking, lightsBrake = data.LightsBrake, lightsReverse = data.LightsReverse, lightsHazard = data.LightsHazard, differentialLock = data.DifferentialLock, liftAxle = data.LiftAxle, trailerLiftAxle = data.TrailerLiftAxle, truckBrand = data.TruckBrand, truckModel = data.TruckModel, licensePlate = data.LicensePlate, sourceCompany = data.SourceCompany, destinationCompany = data.DestinationCompany, cargoMassKg = data.CargoMassKg, plannedDistanceKm = data.PlannedDistanceKm, cargoValueBrl = data.CargoValueBrl }; using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{_serverTripId}/telemetry"); request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}"); request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"); using var response = await _http.SendAsync(request); if (response.IsSuccessStatusCode) _lastTelemetrySentAtUtc = DateTime.UtcNow; } catch (Exception ex) { App.WriteUiCrashLog("Telemetry.TripSampleUpload", ex); }
    }

    private static string BuildJobSignature(TelemetrySnapshot data)
    {
        return string.Join("|", new[]
        {
            data.Cargo?.Trim() ?? "",
            data.SourceCity?.Trim() ?? "",
            data.DestinationCity?.Trim() ?? ""
        });
    }

    private string? GetLocalActiveTripId()
    {
        if (LocalData.Current is not { } store) return null;
        using var command = store.Db.Connection.CreateCommand();
        var ownerUserId = SecureTokenStore.ReadUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return null;
        command.CommandText = "SELECT id FROM trip WHERE status='active' AND owner_user_id=@owner ORDER BY started_at_utc DESC LIMIT 1;";
        command.Parameters.AddWithValue("@owner", ownerUserId);
        return command.ExecuteScalar()?.ToString();
    }

    private async void ManualFinishTrip_Click(object sender, RoutedEventArgs e)
    {
        if (_tripFinishBusy) return;
        if (sender is System.Windows.Controls.Button button)
        {
            button.IsEnabled = false;
            try { await ManualFinishCurrentTripAsync(); }
            finally { button.IsEnabled = true; }
            return;
        }
        await ManualFinishCurrentTripAsync();
    }

    private void ResetCurrentTripForRecovery()
    {
        var answer = MessageBox.Show(
            "Descartar a viagem atual travada e começar uma nova operação?\n\n" +
            "Esta ação é somente de recuperação: não registra pagamento, ranking ou entrega concluída.",
            "TransPoli • Resetar viagem",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var localTripId = !string.IsNullOrWhiteSpace(_localTripId) ? _localTripId : GetLocalActiveTripId();
            if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } store)
                new LocalTripRepository(store.Db).DiscardActiveTrip(localTripId);

            if (!ClearSessionState())
                throw new InvalidOperationException("Não foi possível limpar o estado persistido da viagem.");

            StatusText.Text = "TransPoli • viagem travada descartada • pronto para nova operação";
            ShowTripCenterModal();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("MainWindow.ResetCurrentTripForRecovery", ex);
            MessageBox.Show("Não foi possível resetar a viagem.\n\n" + ex.Message,
                "TransPoli • Resetar viagem", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ManualFinishCurrentTripAsync()
    {
        if (!_tripActive)
        {
            var localTripId = GetLocalActiveTripId();
            if (string.IsNullOrWhiteSpace(localTripId) || LocalData.Current is not { } localStore)
            {
                MessageBox.Show("Não existe uma viagem ativa para finalizar.", "TransPoli • Viagem", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dataLocal = LastTelemetry;
            if (dataLocal is null || !dataLocal.Connected)
            {
                MessageBox.Show("A telemetria do ETS2 não está disponível. Conecte o jogo antes de finalizar a viagem.", "TransPoli • Viagem", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var answerLocal = MessageBox.Show(
                "Existe uma viagem ativa salva no banco local, mas a sessão da tela não está carregada. Deseja finalizá-la manualmente?\\n\\nEla será encerrada e não voltará a aparecer como 100% em Viagem Atual.",
                "Finalizar viagem", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answerLocal != MessageBoxResult.Yes) return;

            double startOdo = 0, startFuel = dataLocal.FuelLiters, rate = 12.0;
            DateTime startedAtUtc = DateTime.UtcNow;
            string? serverId = null;
            using (var command = localStore.Db.Connection.CreateCommand())
            {
                command.CommandText = "SELECT server_id,start_odometer_km,fuel_start_l,rate_per_km,started_at_utc FROM trip WHERE id=@id AND owner_user_id=@owner LIMIT 1;";
                command.Parameters.AddWithValue("@id", localTripId);
                command.Parameters.AddWithValue("@owner", SecureTokenStore.ReadUserId() ?? "");
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    serverId = reader.IsDBNull(0) ? null : reader.GetString(0);
                    startOdo = reader.IsDBNull(1) ? 0 : reader.GetDouble(1);
                    startFuel = reader.IsDBNull(2) ? dataLocal.FuelLiters : reader.GetDouble(2);
                    rate = reader.IsDBNull(3) ? 12.0 : reader.GetDouble(3);
                    if (!reader.IsDBNull(4) && DateTime.TryParse(reader.GetString(4), out var parsedStartedAt))
                        startedAtUtc = parsedStartedAt.ToUniversalTime();
                }
            }

            // Reidrata a TripSession local e encaminha o encerramento manual para o
            // mesmo pipeline imutável usado pela entrega normal. Nenhum atalho grava
            // diretamente trip/economia/tacógrafo fora do checkpoint de fechamento.
            _localTripId = localTripId;
            _serverTripId = serverId;
            _tripActive = true;
            _tripStartedAtUtc = startedAtUtc;
            _tripStartOdometer = (float)startOdo;
            _tripStartFuel = (float)startFuel;
            _localTripRatePerKm = JourneyEconomyCalculator.SanitizeRate(rate);
            await FinishAutomaticTrip(dataLocal, manual: true);
            return;
        }
        var data = LastTelemetry;
        if (data is null || !data.Connected)
        {
            MessageBox.Show("A telemetria do ETS2 não está disponível. Conecte o jogo antes de finalizar a viagem.", "TransPoli • Viagem", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var answer = MessageBox.Show(
            "Finalizar a viagem atual manualmente?\\n\\nA viagem será encerrada, o contrato será marcado como entregue e o painel Viagem Atual ao Vivo será zerado. Se o ETS2 ainda estiver mostrando a mesma carga, ela não será recriada automaticamente.",
            "Finalizar viagem",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            StatusText.Text = "TransPoli • finalização manual cancelada";
            return;
        }

        StatusText.Text = "TransPoli • finalizando viagem manualmente...";
        await FinishAutomaticTrip(data, manual: true);
    }

    private async Task FinishAutomaticTrip(TelemetrySnapshot data, bool manual = false)
    {
        // No modo automático, somente a telemetria de entrega/encerramento pode liquidar.
        // No modo manual, o clique explícito do motorista autoriza a liquidação.
        if (!manual && !data.JobDelivered && !data.JobFinished) return;
        if (_tripFinishBusy) return;
        _tripFinishBusy = true;
        try
        {
        var finishingTripId = _serverTripId;
        var localTripId = _localTripId;
        _lastTripFinishedAtUtc = DateTime.UtcNow;
        var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
        var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
        // Consumo final da TripSession considera somente abastecimentos vinculados
        // à própria viagem: combustível inicial + litros abastecidos - combustível final.
        // O acumulador de telemetria fica como fallback quando não há vínculo local disponível.
        var fuelUsed = Math.Max(0f, _tripFuelConsumedL);
        if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } fuelStore)
        {
            var refueledLiters = new LocalTripClosureRepository(fuelStore.Db).GetRefueledLiters(localTripId);
            fuelUsed = (float)Math.Max(0d, _tripStartFuel + refueledLiters - data.FuelLiters);
        }
        // O rate congelado e persistido na TripSession é a fonte econômica do fechamento.
        // A memória pode ter sido iniciada offline e ficar desatualizada até a outbox
        // receber o contrato oficial; por isso relê o SQLite antes de congelar o snapshot.
        if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } rateStore)
        {
            var ownerUserId = SecureTokenStore.ReadUserId();
            if (!string.IsNullOrWhiteSpace(ownerUserId))
            {
                var persistedRate = new LocalTripRepository(rateStore.Db).GetRatePerKm(localTripId, ownerUserId);
                if (persistedRate.HasValue)
                    _localTripRatePerKm = persistedRate.Value;
            }
        }
        _localTripRatePerKm = JourneyEconomyCalculator.SanitizeRate(_localTripRatePerKm);
        var gross = JourneyEconomyCalculator.CalculateGross(distance, _localTripRatePerKm);
        var closureSessionKey = _tripLifecycle.Current.SessionKey;

        // A liquidação local é a fonte de verdade. A API é sincronização central e não define o valor pago.
        try
        {
            if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } store)
            {
                var localTrips = new LocalTripRepository(store.Db);
                var closure = new LocalTripClosureRepository(store.Db);
                // Snapshot final é gravado uma única vez. Recovery nunca recalcula a viagem
                // usando telemetria de uma sessão posterior.
                var closureOwnerUserId = SecureTokenStore.ReadUserId();
                if (string.IsNullOrWhiteSpace(closureOwnerUserId))
                    throw new InvalidOperationException("Identidade autenticada indisponível para o fechamento da viagem.");
                closure.Begin(localTripId, manual ? "manual" : "telemetria_entrega", data, closureSessionKey, finishingTripId, distance, fuelUsed, gross, closureOwnerUserId);
                if (!closure.IsMarked(localTripId, "local_settled_at_utc"))
                {
                    localTrips.FinishTrip(localTripId, data, distance, fuelUsed, gross, manual ? "manual" : "telemetria_entrega");
                    closure.Mark(localTripId, "local_settled_at_utc");
                }
                if (!closure.IsMarked(localTripId, "health_captured_at_utc"))
                {
                    var truckKey = string.IsNullOrWhiteSpace(data.TruckId) ? data.LicensePlate : data.TruckId;
                    localTrips.AppendTruckHealth(truckKey ?? "", localTripId, data);
                    closure.Mark(localTripId, "health_captured_at_utc");
                }

                // Empréstimos são corporativos e liquidados pelo servidor. O fechamento
                // local da TripSession nunca cria/debita parcela de um empréstimo legado.
            }
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("MainWindow.FinishAutomaticTrip.LocalSettlement", ex);
            StatusText.Text = "TransPoli • erro ao salvar a liquidação local da viagem";
            MessageBox.Show("Falha ao salvar a liquidação local.\n\n" + ex.GetType().Name + ": " + ex.Message, "TransPoli • Falha ao finalizar", MessageBoxButton.OK, MessageBoxImage.Error);
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
        // O fechamento remoto tem um único caminho: outbox determinística por
        // localTripId. Isso elimina a janela HTTP-direto + fallback e garante retry
        // idempotente mesmo quando o servidor processa e a resposta se perde.
        var remoteDurable = !string.IsNullOrWhiteSpace(localTripId)
            && _serverSync.QueueTripFinish(localTripId, finishPayload);
        if (remoteDurable)
            InvalidatePhoneOfficialCache(economy: true, trips: true, documents: true);
        if (remoteDurable) _lastOfficialRankingRefreshUtc = DateTime.MinValue;

        // O bloqueio contra recriação da mesma carga só passa a valer quando o
        // fechamento local já possui um caminho remoto durável. Se a fila/checkpoint
        // falhar, a TripSession permanece recuperável sem parecer encerrada na UI.
        if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } closureStore)
            {
                var closure = new LocalTripClosureRepository(closureStore.Db);
                if (!closure.IsMarked(localTripId, "tachograph_closed_at_utc"))
                {
                    if (!ArchiveTachographForTrip(localTripId, closureSessionKey))
                    {
                        closure.Fail(localTripId, "Falha ao persistir o arquivo do tacógrafo.");
                        StatusText.Text = "TransPoli • fechamento preservado • tacógrafo não persistido";
                        return;
                    }
                    if (!closure.Mark(localTripId, "tachograph_closed_at_utc"))
                    {
                        closure.Fail(localTripId, "Tacógrafo arquivado, mas checkpoint não persistiu.");
                        StatusText.Text = "TransPoli • fechamento preservado • checkpoint do tacógrafo falhou";
                        return;
                    }
                }
                if (remoteDurable && !closure.Mark(localTripId, "remote_queued_at_utc"))
                {
                    closure.Fail(localTripId, "Outbox remota durável, mas checkpoint não persistiu.");
                    StatusText.Text = "TransPoli • fechamento preservado • checkpoint remoto falhou";
                    return;
                }
            }
            else
            {
                // Uma operação moderna só pode abandonar a TripSession depois de
                // existir identidade local + armazenamento durável para o checkpoint.
                // Sem isso preservamos a sessão para recuperação em vez de perder a
                // única identidade que ainda liga DANFE, tacógrafo e financeiro.
                if (_tripActive || !string.IsNullOrWhiteSpace(_operationTripId) || !string.IsNullOrWhiteSpace(_operationInvoiceId))
                {
                    ArchiveTachographForTrip(localTripId, closureSessionKey);
                    StatusText.Text = "TransPoli • fechamento preservado • armazenamento local indisponível";
                    return;
                }
                ArchiveCurrentTachograph();
            }

            // O evento final pertence à TripSession ainda ativa. Ele precisa existir
            // antes da consolidação para que o Diário de Bordo inclua VIAGEM_ENCERRADA.
            if (!_tripLifecycle.MarkFinished(data, manual ? "Viagem encerrada manualmente." : "Entrega confirmada pelo ETS2."))
            {
                if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } lifecycleStore)
                    new LocalTripClosureRepository(lifecycleStore.Db).Fail(localTripId, "Evento final do lifecycle não pôde ser persistido.");
                StatusText.Text = "TransPoli • fechamento preservado • lifecycle final não persistido";
                return;
            }

            if (!string.IsNullOrWhiteSpace(localTripId) && LocalData.Current is { } logStore)
            {
                var trips = new LocalTripRepository(logStore.Db);
                trips.RefreshFinancialSummary(localTripId);
                _tripLifecycle.ApplyFinancialSummary(trips.GetFinancialSummary(localTripId));
                new LocalTripLogbookRepository(logStore.Db).Consolidate(localTripId, closureSessionKey);

                // O checkpoint só vira concluído depois que todos os artefatos locais
                // do fechamento, inclusive o diário final, já foram consolidados.
                var finalClosure = new LocalTripClosureRepository(logStore.Db);
                if (finalClosure.IsMarked(localTripId, "local_settled_at_utc")
                    && finalClosure.IsMarked(localTripId, "tachograph_closed_at_utc")
                    && finalClosure.IsMarked(localTripId, "health_captured_at_utc")
                    && finalClosure.IsMarked(localTripId, "remote_queued_at_utc"))
                {
                    if (!finalClosure.Complete(localTripId))
                    {
                        finalClosure.Fail(localTripId, "Checkpoint final recusado: etapas duráveis incompletas.");
                        StatusText.Text = "TransPoli • fechamento preservado • checkpoint incompleto";
                        return;
                    }
                    if (!ClearSessionState())
                    {
                        StatusText.Text = "TransPoli • fechamento concluído • sessão será limpa na próxima recuperação";
                        return;
                    }
                    // Só bloqueia a recriação visual da mesma carga depois de todos os
                    // checkpoints e da limpeza da TripSession terem sido concluídos.
                    if (manual)
                        _manualTripFinishSignature = BuildJobSignature(data);
                }
                else
                {
                    finalClosure.Fail(localTripId, "Fechamento local preservado: sincronização remota ainda não está durável.");
                    StatusText.Text = "TransPoli • fechamento preservado • sincronização pendente";
                    return;
                }
            }
            else
            {
                // Somente o caminho legado sem identidade operacional pode limpar
                // diretamente. Operações modernas já retornaram acima e permanecem
                // preservadas para recuperação.
                ClearSessionState();
            }
        var elapsedText = FormatDuration(elapsed);
        TripStatusText.Text = manual ? "VIAGEM FINALIZADA MANUALMENTE" : "VIAGEM FINALIZADA AUTOMATICAMENTE";
        TripDistanceText.Text = $"{distance:0.0} km";
        TripDurationText.Text = elapsedText;
        StatusText.Text = $"TransPoli • viagem finalizada • {distance:0.0} km • R$ {gross:0.00} • {elapsedText}";
        }
        finally
        {
            _tripFinishBusy = false;
        }
    }
    private DateTime _lastTripFinancialRefreshUtc = DateTime.MinValue;
    private string? _lastTripFinancialRefreshId;

    private void RefreshActiveTripFinancials(bool force = false)
    {
        if (!_tripActive || string.IsNullOrWhiteSpace(_localTripId) || LocalData.Current is not { } store) return;
        var tripChanged = !string.Equals(_lastTripFinancialRefreshId, _localTripId, StringComparison.Ordinal);
        if (!force && !tripChanged && DateTime.UtcNow - _lastTripFinancialRefreshUtc < TimeSpan.FromSeconds(30)) return;
        try
        {
            var financialRepo = new LocalTripRepository(store.Db);
            financialRepo.RefreshFinancialSummary(_localTripId);
            _tripLifecycle.ApplyFinancialSummary(financialRepo.GetFinancialSummary(_localTripId));
            _lastTripFinancialRefreshId = _localTripId;
            _lastTripFinancialRefreshUtc = DateTime.UtcNow;
        }
        catch (Exception ex) { App.WriteUiCrashLog("TripFinancials.RefreshLocal", ex); }
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
        catch (Exception ex) { App.WriteUiCrashLog("Telemetry.SaveLocalTripSample", ex); }
    }

    private static string FormatDuration(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private static bool HasActiveJob(TelemetrySnapshot data) => data.OnJob || data.CargoLoaded || (!string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity) && !string.IsNullOrWhiteSpace(data.Cargo));
    private static string BuildRoute(TelemetrySnapshot data) => string.IsNullOrWhiteSpace(data.SourceCity) && string.IsNullOrWhiteSpace(data.DestinationCity) ? "Nenhum trabalho ativo detectado." : $"{data.SourceCity ?? "Origem"}  →  {data.DestinationCity ?? "Destino"}";
    private DirectorCenterWindow? _directorCenterWindow;

    private void OpenDirectorCenter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_directorCenterWindow is { IsLoaded: true })
            {
                if (!_directorCenterWindow.IsVisible) _directorCenterWindow.Show();
                if (_directorCenterWindow.WindowState == WindowState.Minimized) _directorCenterWindow.WindowState = WindowState.Normal;
                _directorCenterWindow.Activate();
                return;
            }
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token))
            {
                MessageBox.Show("Sua sessão TransPoli não está disponível. Entre novamente na conta.", "TransPoli • Diretoria", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var director = new DirectorCenterWindow(token, openedFromCockpit: true)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            _directorCenterWindow = director;
            director.Closed += (_, _) =>
            {
                _directorCenterWindow = null;
                if (IsLoaded) { Show(); Activate(); }
            };
            director.Show();
            director.Activate();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("MainWindow.OpenDirectorCenter", ex);
            MessageBox.Show("Não foi possível abrir a Central da Diretoria.\n\n" + ex.Message, "TransPoli", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LogoutAccount_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show("Deseja sair da conta neste computador?", "TransPoli • Sair", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (_tripActive && !TrySaveSessionState())
                throw new InvalidOperationException("A viagem ativa não pôde ser preservada antes de sair da conta.");
            _logoutToActivation = true;
            SecureTokenStore.Delete();
            var activation = new ActivationWindow();
            Application.Current.MainWindow = activation;
            activation.Show();
            activation.Activate();
            Close();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("MainWindow.LogoutAccount", ex);
            MessageBox.Show("Não foi possível sair da conta agora.\n\n" + ex.Message, "TransPoli", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetDisconnected()
    {
        _telemetryConnectedAtUtc = DateTime.MinValue;
        LastTelemetry = null;
        if (_hudSettings.Enabled && _hudHotkeyVisible)
            _telemetryOverlay?.ShowDisconnected();
        else
            HideTelemetryOverlay();
        EnvironmentStateText.Text = "AMBIENTE N/D"; EnvironmentDetailText.Text = "Aguardando telemetria"; EnvironmentWeatherText.Text = "CLIMA N/D"; EnvironmentIconText.Text = "◌"; EnvironmentStateText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush; EnvironmentIconText.Foreground = FindResource("TextMuted") as System.Windows.Media.Brush;
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
    protected override void OnClosed(EventArgs e)
    {
        // Fechar o tablet não encerra contrato. Persiste exatamente a mesma TripSession.
        if (_tripActive && !string.IsNullOrWhiteSpace(SecureTokenStore.ReadUserId())) _ = TrySaveSessionState();
        try { _timer.Stop(); } catch { }
        try { _physicalLockTimer?.Stop(); } catch { }
        try { _notificationTimer?.Stop(); } catch { }
        try { _v15InvoiceFlowTimer.Stop(); } catch { }
        try { _garageTimer?.Stop(); } catch { }
        try { _tachTimer?.Stop(); } catch { }
        try { _opsTimer.Stop(); } catch { }
        try { _connector.Dispose(); } catch { }
        try { _serverSync.Dispose(); } catch { }
        try { _drivingAnalytics.Dispose(); } catch { }
        try { _cargoOperations.Dispose(); } catch { }
        try { _phaseI.Dispose(); } catch { }
        try { _phaseJ.Dispose(); } catch { }
        try { _localData?.Dispose(); } catch { }
        try { UnregisterGlobalHotKey(); } catch { }
        try { _http.Dispose(); } catch { }
        if (!_logoutToActivation)
        {
            try { Application.Current?.Shutdown(0); } catch { }
        }
        base.OnClosed(e);
    }


}

public sealed class TelemetrySnapshot
{
    public bool Connected { get; set; } public bool Updated { get; set; } public ulong Timestamp { get; set; } public uint TimeAbsMinutes { get; set; } public string? Game { get; set; } public bool TollgatePaid { get; set; } public long TollgateAmount { get; set; } public long TollgateEventId { get; set; } public bool GamePaused { get; set; }
    public string? TruckBrand { get; set; } public string? TruckModel { get; set; } public string? TruckId { get; set; } public string? LicensePlate { get; set; } public bool EngineEnabled { get; set; } public bool ElectricEnabled { get; set; } public bool CargoLoaded { get; set; } public bool SpecialJob { get; set; } public bool OnJob { get; set; } public bool JobFinished { get; set; } public bool JobCancelled { get; set; } public bool JobDelivered { get; set; } public bool RefuelActive { get; set; } public bool RefuelPayed { get; set; } public float RefuelAmountLiters { get; set; }
    public double WorldX { get; set; } public double WorldY { get; set; } public double WorldZ { get; set; } public double HeadingDeg { get; set; } public double PitchDeg { get; set; } public double RollDeg { get; set; } public bool PositionValid { get; set; }
    public float UserSteer { get; set; } public float UserClutch { get; set; } public float GameSteer { get; set; } public float GameClutch { get; set; } public float LightsDashboard { get; set; }
    public float FuelCapacityLiters { get; set; } public float FuelWarningFactor { get; set; } public float AdBlueCapacityLiters { get; set; } public float AdBlueWarningFactor { get; set; } public float AirPressureWarningLimit { get; set; } public float AirPressureEmergencyLimit { get; set; } public float OilPressureWarningLimit { get; set; } public float WaterTemperatureWarningLimit { get; set; } public float BatteryVoltageWarningLimit { get; set; } public float EngineRpmMax { get; set; } public float GearDifferential { get; set; } public float UnitMassKg { get; set; } public uint ForwardGearCount { get; set; } public uint ReverseGearCount { get; set; } public uint RetarderStepCount { get; set; } public uint DeliveryTimeAbs { get; set; } public uint SelectorCount { get; set; } public uint MaxTrailerCount { get; set; } public uint UnitCount { get; set; } public uint ShifterSlot { get; set; } public uint RetarderBrake { get; set; } public uint LightsAuxFront { get; set; } public uint LightsAuxRoof { get; set; } public float[] GearRatiosForward { get; set; } = new float[24]; public float[] GearRatiosReverse { get; set; } = new float[8];
    public float[] TruckWheelPositionsX { get; set; } = new float[16]; public float[] TruckWheelPositionsY { get; set; } = new float[16]; public float[] TruckWheelPositionsZ { get; set; } = new float[16];
    public float[] TruckWheelRadius { get; set; } = new float[16]; public float[] TruckWheelSuspDeflection { get; set; } = new float[16]; public float[] TruckWheelVelocity { get; set; } = new float[16]; public float[] TruckWheelSteering { get; set; } = new float[16]; public float[] TruckWheelRotation { get; set; } = new float[16]; public float[] TruckWheelLift { get; set; } = new float[16]; public float[] TruckWheelLiftOffset { get; set; } = new float[16];
    public bool[] TruckWheelSteerable { get; set; } = new bool[16]; public bool[] TruckWheelSimulated { get; set; } = new bool[16]; public bool[] TruckWheelPowered { get; set; } = new bool[16]; public bool[] TruckWheelLiftable { get; set; } = new bool[16]; public bool[] TruckWheelOnGround { get; set; } = new bool[16]; public uint[] TruckWheelSubstance { get; set; } = new uint[16]; public int TruckWheelCount { get; set; }
    public float CabinOffsetX { get; set; } public float CabinOffsetY { get; set; } public float CabinOffsetZ { get; set; } public float CabinOffsetRotationX { get; set; } public float CabinOffsetRotationY { get; set; } public float CabinOffsetRotationZ { get; set; }
    public float HeadOffsetX { get; set; } public float HeadOffsetY { get; set; } public float HeadOffsetZ { get; set; } public float HeadOffsetRotationX { get; set; } public float HeadOffsetRotationY { get; set; } public float HeadOffsetRotationZ { get; set; }
    public float SpeedKph { get; set; } public float SpeedMps { get; set; } public float SpeedLimitKph { get; set; } public float Rpm { get; set; } public int Gear { get; set; } public float UserThrottle { get; set; } public float EffectiveThrottle { get; set; } public float UserBrake { get; set; } public float EffectiveBrake { get; set; }
    public float FuelLiters { get; set; } public float FuelAvgConsumption { get; set; } public float FuelRangeKm { get; set; } public float AdBlueLiters { get; set; } public float OilPressure { get; set; } public float OilTemperature { get; set; } public float WaterTemperature { get; set; } public float BatteryVoltage { get; set; }
    public float OdometerKm { get; set; } public float RouteDistanceKm { get; set; } public float RouteTimeSeconds { get; set; } public bool CruiseControl { get; set; } public float CruiseSpeedKph { get; set; } public string? SourceCity { get; set; } public string? DestinationCity { get; set; } public string? SourceCompany { get; set; } public string? DestinationCompany { get; set; } public string? Cargo { get; set; } public float CargoMassKg { get; set; } public uint PlannedDistanceKm { get; set; } public ulong? CargoValueBrl { get; set; }
    public float AirPressure { get; set; } public float BrakeTemperature { get; set; } public bool MotorBrake { get; set; } public bool ParkingBrake { get; set; } public bool BrakeLight { get; set; } public bool AirPressureWarning { get; set; } public bool AirPressureEmergency { get; set; } public bool FuelWarning { get; set; } public bool AdBlueWarning { get; set; } public bool OilPressureWarning { get; set; } public bool WaterTemperatureWarning { get; set; } public bool BatteryVoltageWarning { get; set; }
    public bool Wipers { get; set; } public bool LightsBeamLow { get; set; } public bool LightsBeamHigh { get; set; } public bool LightsBeacon { get; set; } public bool BlinkerLeftActive { get; set; } public bool BlinkerRightActive { get; set; } public bool BlinkerLeftOn { get; set; } public bool BlinkerRightOn { get; set; } public bool LightsParking { get; set; } public bool LightsBrake { get; set; } public bool LightsReverse { get; set; } public bool LightsHazard { get; set; } public bool DifferentialLock { get; set; } public bool LiftAxle { get; set; } public bool LiftAxleIndicator { get; set; } public bool TrailerLiftAxle { get; set; } public bool TrailerLiftAxleIndicator { get; set; }
    public bool FerryActive { get; set; } public bool TrainActive { get; set; } public long JobCancelledPenalty { get; set; } public long JobDeliveredRevenue { get; set; } public long FineAmount { get; set; } public long TollgatePayAmount { get; set; } public long FerryPayAmount { get; set; } public long TrainPayAmount { get; set; } public float DeliveredCargoDamage { get; set; } public float DeliveredDistanceKm { get; set; } public string? JobMarket { get; set; } public string? FineOffence { get; set; } public uint RetarderLevel { get; set; } public float WearEngine { get; set; } public float WearTransmission { get; set; } public float WearCabin { get; set; } public float WearChassis { get; set; } public float WearWheels { get; set; } public float CargoDamage { get; set; }
    public TrailerTelemetry[] Trailers { get; set; } = Array.Empty<TrailerTelemetry>();
}

public sealed class TrailerTelemetry
{
    public int Index { get; set; } public bool Attached { get; set; } public double WorldX { get; set; } public double WorldY { get; set; } public double WorldZ { get; set; } public double HeadingDeg { get; set; } public double PitchDeg { get; set; } public double RollDeg { get; set; } public bool PositionValid { get; set; }
    public int WheelCount { get; set; } public bool[] WheelSteerable { get; set; } = new bool[16]; public bool[] WheelSimulated { get; set; } = new bool[16]; public bool[] WheelPowered { get; set; } = new bool[16]; public bool[] WheelLiftable { get; set; } = new bool[16]; public bool[] WheelOnGround { get; set; } = new bool[16]; public uint[] WheelSubstance { get; set; } = new uint[16];
    public float[] WheelRadius { get; set; } = new float[16]; public float[] WheelSuspDeflection { get; set; } = new float[16]; public float[] WheelVelocity { get; set; } = new float[16]; public float[] WheelSteering { get; set; } = new float[16]; public float[] WheelRotation { get; set; } = new float[16]; public float[] WheelLift { get; set; } = new float[16]; public float[] WheelLiftOffset { get; set; } = new float[16];
    public float[] WheelPositionX { get; set; } = new float[16]; public float[] WheelPositionY { get; set; } = new float[16]; public float[] WheelPositionZ { get; set; } = new float[16];
    public float LinearVelocityX { get; set; } public float LinearVelocityY { get; set; } public float LinearVelocityZ { get; set; } public float AngularVelocityX { get; set; } public float AngularVelocityY { get; set; } public float AngularVelocityZ { get; set; }
    public float LinearAccelerationX { get; set; } public float LinearAccelerationY { get; set; } public float LinearAccelerationZ { get; set; } public float AngularAccelerationX { get; set; } public float AngularAccelerationY { get; set; } public float AngularAccelerationZ { get; set; }
    public float HookPositionX { get; set; } public float HookPositionY { get; set; } public float HookPositionZ { get; set; } public float CargoDamage { get; set; } public float WearChassis { get; set; } public float WearWheels { get; set; } public float WearBody { get; set; }
    public string? Id { get; set; } public string? CargoAccessoryId { get; set; } public string? BodyType { get; set; } public string? BrandId { get; set; } public string? Brand { get; set; } public string? Name { get; set; } public string? ChainType { get; set; } public string? LicensePlate { get; set; } public string? LicensePlateCountry { get; set; } public string? LicensePlateCountryId { get; set; }
}
