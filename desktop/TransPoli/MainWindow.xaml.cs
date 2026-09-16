using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
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

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _timer;
    private readonly string _tokenPath;
    private readonly ConnectorSupervisor _connector = new();
    private HwndSource? _source;
    private bool _tripActive;
    private bool _truckLocked = true;
    private DateTime _tripStartedAtUtc;
    private float _tripStartOdometer;
    private float _tripStartFuel;
    private int _jobMissingTicks;
    private DateTime _lastTripFinishedAtUtc = DateTime.MinValue;
    private DateTime _lastTelemetrySentAtUtc = DateTime.MinValue;
    private string? _serverTripId;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _tokenPath = Path.Combine(folder, "access-token.txt");
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += async (_, _) =>
        {
            await _connector.EnsureRunningAsync();
            await RefreshTelemetry();
        };
        Loaded += async (_, _) =>
        {
            RegisterGlobalHotKey();
            StartUpdateWatcher();
            await _connector.StartAsync();
            await RefreshTelemetry();
        };
        Closed += (_, _) => UnregisterGlobalHotKey();
        _timer.Start();
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
        _source?.RemoveHook(WndProc);
        _source = null;
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

    private async Task RefreshTelemetry()
    {
        try
        {
            using var response = await _http.GetAsync("http://127.0.0.1:17877/telemetry");
            if (!response.IsSuccessStatusCode) { SetDisconnected(); return; }
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) { SetDisconnected(); return; }

            ConnectionText.Text = "ETS2 CONECTADO";
            ConnectionText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
            ConnectionDot.Fill = FindResource("Green") as System.Windows.Media.Brush;
            StatusText.Text = data.GamePaused ? "ETS2 conectado • jogo pausado" : "ETS2 conectado • telemetria em tempo real";
            TruckName.Text = string.IsNullOrWhiteSpace(data.TruckModel) ? "Caminhão detectado" : $"{data.TruckBrand} {data.TruckModel}";
            RouteText.Text = BuildRoute(data);
            SpeedText.Text = Math.Abs(data.SpeedKph).ToString("0");
            RpmText.Text = data.Rpm.ToString("0");
            GearText.Text = data.Gear == 0 ? "N" : data.Gear < 0 ? "R" : data.Gear.ToString();
            FuelText.Text = $"{data.FuelLiters:0.0} L";
            RangeText.Text = $"{data.FuelRangeKm:0} km";
            OdometerText.Text = $"{data.OdometerKm:0.0} km";
            CruiseText.Text = data.CruiseControl ? "ON" : "OFF";
            CargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Nenhuma carga" : data.Cargo;
            CargoMassText.Text = data.CargoMassKg > 0 ? $"{data.CargoMassKg:0} kg" : "Peso não informado";
            EngineStateText.Text = data.EngineEnabled ? "LIGADO" : "DESLIGADO";
            EngineStateText.Foreground = FindResource(data.EngineEnabled ? "Green" : "Yellow") as System.Windows.Media.Brush;
            TelemetryInfoText.Text = $"{data.Game ?? "ETS2"} • atualização {data.Timestamp}";

            UpdateAutomaticLock(data);
            UpdateAutomaticTrip(data);

            if (_tripActive && !string.IsNullOrWhiteSpace(_serverTripId) && DateTime.UtcNow - _lastTelemetrySentAtUtc >= TimeSpan.FromSeconds(5))
                await SendTelemetrySample(data);
        }
        catch { SetDisconnected(); }
    }

    private void UpdateAutomaticLock(TelemetrySnapshot data)
    {
        var stopped = Math.Abs(data.SpeedKph) < 0.5f;
        if (!data.EngineEnabled && stopped)
            _truckLocked = true;

        if (_truckLocked)
        {
            VehicleLockText.Text = "🔒 CAMINHÃO BLOQUEADO";
            VehicleLockText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            UnlockButton.IsEnabled = data.EngineEnabled;
            UnlockButton.Opacity = data.EngineEnabled ? 1.0 : 0.45;

            if (data.EngineEnabled)
            {
                AlertText.Text = "Caminhão ligado • desbloqueio necessário";
                AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            }
            else
            {
                AlertText.Text = "Veículo bloqueado • motor desligado";
                AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
            }
        }
        else
        {
            VehicleLockText.Text = "🟢 CAMINHÃO LIBERADO";
            VehicleLockText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
            UnlockButton.IsEnabled = false;
            UnlockButton.Opacity = 0.45;
            AlertText.Text = "Nenhum alerta operacional ativo";
            AlertText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        }
    }

    private void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        _truckLocked = false;
        VehicleLockText.Text = "🟢 CAMINHÃO LIBERADO";
        VehicleLockText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        UnlockButton.IsEnabled = false;
        UnlockButton.Opacity = 0.45;
        AlertText.Text = "Caminhão liberado para operação";
        AlertText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        StatusText.Text = "TransPoli • caminhão desbloqueado pelo tablet";
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
                return;
            }
            TripRouteText.Text = BuildRoute(data);
            TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            TripDistanceText.Text = data.PlannedDistanceKm > 0 ? $"{data.PlannedDistanceKm:0} km" : "— km";
            TripDurationText.Text = "Aguardando saída";
            TripStatusText.Text = _truckLocked ? "Carga detectada • desbloqueie o caminhão" : "Trabalho detectado • pronto para iniciar";
            if (!_truckLocked && !data.GamePaused && data.EngineEnabled && data.CargoLoaded && Math.Abs(data.SpeedKph) >= 3f && DateTime.UtcNow - _lastTripFinishedAtUtc > TimeSpan.FromSeconds(5))
                StartAutomaticTrip(data);
            return;
        }

        if (data.CargoLoaded)
        {
            _jobMissingTicks = 0;
            var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            TripStatusText.Text = _truckLocked ? "VIAGEM • CAMINHÃO BLOQUEADO" : "VIAGEM EM ANDAMENTO";
            TripRouteText.Text = BuildRoute(data);
            TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            TripDistanceText.Text = distance > 0.1f ? $"{distance:0.0} km" : "0.0 km";
            TripDurationText.Text = FormatDuration(elapsed);
            return;
        }

        _jobMissingTicks++;
        TripStatusText.Text = "Carga descarregada • confirmando fim da viagem...";
        TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc);
        if (_jobMissingTicks >= 20) FinishAutomaticTrip(data);
    }

    private async void StartAutomaticTrip(TelemetrySnapshot data)
    {
        _tripActive = true;
        _tripStartedAtUtc = DateTime.UtcNow;
        _tripStartOdometer = data.OdometerKm;
        _tripStartFuel = data.FuelLiters;
        _jobMissingTicks = 0;
        _serverTripId = null;
        _lastTelemetrySentAtUtc = DateTime.MinValue;
        TripStatusText.Text = "VIAGEM INICIADA AUTOMATICAMENTE";
        TripRouteText.Text = BuildRoute(data);
        TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
        TripDistanceText.Text = "0.0 km";
        TripDurationText.Text = "00:00:00";
        StatusText.Text = "TransPoli • viagem iniciada pela telemetria";
        await CreateServerTrip(data);
    }

    private async Task CreateServerTrip(TelemetrySnapshot data)
    {
        var token = ReadToken();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
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
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("trip", out var trip) && trip.TryGetProperty("id", out var id)) _serverTripId = id.GetString();
            if (!string.IsNullOrWhiteSpace(_serverTripId)) await SendTelemetrySample(data, true);
        }
        catch { }
    }

    private async Task SendTelemetrySample(TelemetrySnapshot data, bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_serverTripId)) return;
        if (!force && DateTime.UtcNow - _lastTelemetrySentAtUtc < TimeSpan.FromSeconds(5)) return;
        var token = ReadToken();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var payload = new
            {
                recordedAt = DateTime.UtcNow,
                speedKph = Math.Abs(data.SpeedKph),
                rpm = data.Rpm,
                gear = data.Gear,
                fuelL = data.FuelLiters,
                odometerKm = data.OdometerKm,
                fuelRangeKm = data.FuelRangeKm,
                gamePaused = data.GamePaused,
                truckBrand = data.TruckBrand,
                truckModel = data.TruckModel,
                licensePlate = data.LicensePlate,
                sourceCompany = data.SourceCompany,
                destinationCompany = data.DestinationCompany,
                cargoMassKg = data.CargoMassKg,
                plannedDistanceKm = data.PlannedDistanceKm,
                cargoValueBrl = data.CargoValueBrl
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{_serverTripId}/telemetry");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) _lastTelemetrySentAtUtc = DateTime.UtcNow;
        }
        catch { }
    }

    private async void FinishAutomaticTrip(TelemetrySnapshot data)
    {
        _tripActive = false;
        _jobMissingTicks = 0;
        _lastTripFinishedAtUtc = DateTime.UtcNow;
        var elapsed = DateTime.UtcNow - _tripStartedAtUtc;
        var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
        var fuelUsed = Math.Max(0f, _tripStartFuel - data.FuelLiters);
        if (!string.IsNullOrWhiteSpace(_serverTripId)) await FinishServerTrip(distance, fuelUsed);
        var elapsedText = FormatDuration(elapsed);
        TripStatusText.Text = "VIAGEM FINALIZADA AUTOMATICAMENTE";
        TripDistanceText.Text = $"{distance:0.0} km";
        TripDurationText.Text = elapsedText;
        StatusText.Text = $"TransPoli • viagem finalizada • {distance:0.0} km • {elapsedText}";
        _serverTripId = null;
    }

    private async Task FinishServerTrip(float distance, float fuelUsed)
    {
        var token = ReadToken();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var payload = new { distanceKm = distance, fuelUsedL = fuelUsed };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{_serverTripId}/finish");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _http.SendAsync(request);
        }
        catch { }
    }

    private string? ReadToken()
    {
        try { return File.Exists(_tokenPath) ? File.ReadAllText(_tokenPath).Trim() : null; } catch { return null; }
    }

    private static string FormatDuration(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private static bool HasActiveJob(TelemetrySnapshot data) => data.CargoLoaded || (!string.IsNullOrWhiteSpace(data.SourceCity) && !string.IsNullOrWhiteSpace(data.DestinationCity) && !string.IsNullOrWhiteSpace(data.Cargo));
    private static string BuildRoute(TelemetrySnapshot data) => string.IsNullOrWhiteSpace(data.SourceCity) && string.IsNullOrWhiteSpace(data.DestinationCity) ? "Nenhum trabalho ativo detectado." : $"{data.SourceCity ?? "Origem"}  →  {data.DestinationCity ?? "Destino"}";
    private void SetDisconnected()
    {
        _truckLocked = true;
        ConnectionText.Text = "ETS2 DESCONECTADO";
        ConnectionText.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        ConnectionDot.Fill = FindResource("Muted") as System.Windows.Media.Brush;
        VehicleLockText.Text = "🔒 CAMINHÃO BLOQUEADO";
        VehicleLockText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
        UnlockButton.IsEnabled = false;
        UnlockButton.Opacity = 0.45;
        AlertText.Text = "Aguardando conexão com o ETS2";
        AlertText.Foreground = FindResource("Muted") as System.Windows.Media.Brush;
        TelemetryInfoText.Text = "TransPoli Connector aguardando telemetria";
        StatusText.Text = "Aguardando TransPoli Connector e telemetria do ETS2...";
    }
    protected override void OnClosed(EventArgs e) { _timer.Stop(); _connector.Dispose(); _http.Dispose(); base.OnClosed(e); }
}

public sealed class TelemetrySnapshot
{
    public bool Connected { get; set; }
    public bool Updated { get; set; }
    public ulong Timestamp { get; set; }
    public string? Game { get; set; }
    public bool GamePaused { get; set; }
    public string? TruckBrand { get; set; }
    public string? TruckModel { get; set; }
    public string? TruckId { get; set; }
    public string? LicensePlate { get; set; }
    public bool EngineEnabled { get; set; }
    public bool CargoLoaded { get; set; }
    public float SpeedKph { get; set; }
    public float SpeedMps { get; set; }
    public float Rpm { get; set; }
    public int Gear { get; set; }
    public float FuelLiters { get; set; }
    public float FuelRangeKm { get; set; }
    public float OdometerKm { get; set; }
    public bool CruiseControl { get; set; }
    public string? SourceCity { get; set; }
    public string? DestinationCity { get; set; }
    public string? SourceCompany { get; set; }
    public string? DestinationCompany { get; set; }
    public string? Cargo { get; set; }
    public float CargoMassKg { get; set; }
    public uint PlannedDistanceKm { get; set; }
    public ulong? CargoValueBrl { get; set; }
}