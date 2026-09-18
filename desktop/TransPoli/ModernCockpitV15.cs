using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// V1.0.15: presentation and reliability layer for the desktop cockpit.
/// Keeps legacy telemetry/finance modules intact while replacing the noisy
/// visual areas with a compact live cargo-market ticker and stable trip data.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _v15UiTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _v15MarketTickerTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly HttpClient _v15Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly List<CargoTickerItem> _v15Market = new();
    private bool _v15Initialized;
    private bool _v15FrameApplied;
    private bool _v15MarketLoading;
    private bool _v15RecoveryBusy;
    private bool _v15FinishBusy;
    private int _v15MarketIndex;
    private DateTime _v15MarketAtUtc;
    private string? _v15PopularCargo;
    private int _v15PopularCargoTrips;
    private string? _v15ActiveDriver;
    private int _v15ActiveDriverTrips;
    private string? _v15Trailer;
    private int _v15TrailerUsage;
    private string? _v15LastDiscoveredCargo;
    private TextBlock? _v15MarketMain;
    private TextBlock? _v15MarketMeta;

    private static readonly bool V15ModernRegistration = RegisterV15Modernization();

    private static bool RegisterV15Modernization()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V15Loaded), true);
        return true;
    }

    private static void V15Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v15Initialized) return;
        window._v15Initialized = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                window.ApplyThinTabletFrame();
                window.ReplaceLegacyAlertArea();
                window.HideLegacyOperationButtons();
                window.FixVehicleMiniCards();
                window.StartV15ModernLoop();
            }
            catch { }
        }), DispatcherPriority.ContextIdle);
    }

    private void StartV15ModernLoop()
    {
        if (_v15UiTimer.IsEnabled) return;
        _v15UiTimer.Tick += async (_, _) => await V15ModernTickAsync();
        _v15MarketTickerTimer.Tick += (_, _) => RenderV15MarketTicker();
        _v15UiTimer.Start();
        _v15MarketTickerTimer.Start();
        _ = V15ModernTickAsync();
    }

    private async Task V15ModernTickAsync()
    {
        if (_v15RecoveryBusy) return;
        _v15RecoveryBusy = true;
        try
        {
            await RefreshV15MarketAsync();
            var data = await LoadV15TelemetryAsync();
            if (data is null || !data.Connected) return;
            await DiscoverCargoV15Async(data);
            await ReconcileTripV15Async(data);
        }
        catch { }
        finally { _v15RecoveryBusy = false; }
    }

    private async Task<TelemetrySnapshot?> LoadV15TelemetryAsync()
    {
        try
        {
            using var response = await _v15Http.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private async Task RefreshV15MarketAsync()
    {
        if (_v15MarketLoading) return;
        if (_v15Market.Count > 0 && DateTime.UtcNow - _v15MarketAtUtc < TimeSpan.FromSeconds(20)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        _v15MarketLoading = true;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/cargo-market");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _v15Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("offers", out var offers) || offers.ValueKind != JsonValueKind.Array) return;
            _v15Market.Clear();
            foreach (var offer in offers.EnumerateArray().Take(20))
            {
                var cargo = offer.TryGetProperty("display_name", out var cargoElement) ? cargoElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(cargo)) continue;
                var rate = offer.TryGetProperty("rate_brl_km", out var rateElement) && rateElement.TryGetDecimal(out var rateValue) ? rateValue : 0m;
                var baseRate = offer.TryGetProperty("base_rate_brl_km", out var baseElement) && baseElement.TryGetDecimal(out var baseValue) ? baseValue : rate;
                var discovered = offer.TryGetProperty("discovered_count", out var countElement) && countElement.TryGetInt32(out var count) ? count : 0;
                var status = offer.TryGetProperty("market_status", out var statusElement) ? statusElement.GetString() : "normal";
                _v15Market.Add(new CargoTickerItem(cargo!, rate, baseRate, discovered, status ?? "normal"));
            }
            if (doc.RootElement.TryGetProperty("dashboard", out var dashboard) && dashboard.ValueKind == JsonValueKind.Object)
            {
                if (dashboard.TryGetProperty("popularCargo", out var popular) && popular.ValueKind == JsonValueKind.Object)
                {
                    _v15PopularCargo = popular.TryGetProperty("cargo", out var cargo) ? cargo.GetString() : null;
                    _v15PopularCargoTrips = popular.TryGetProperty("trip_count", out var count) && count.TryGetInt32(out var value) ? value : 0;
                }
                if (dashboard.TryGetProperty("activeDriver", out var driver) && driver.ValueKind == JsonValueKind.Object)
                {
                    _v15ActiveDriver = driver.TryGetProperty("name", out var name) ? name.GetString() : null;
                    _v15ActiveDriverTrips = driver.TryGetProperty("trip_count", out var count) && count.TryGetInt32(out var value) ? value : 0;
                }
                if (dashboard.TryGetProperty("trailerUsage", out var trailer) && trailer.ValueKind == JsonValueKind.Object)
                {
                    _v15Trailer = trailer.TryGetProperty("trailer", out var name) ? name.GetString() : null;
                    _v15TrailerUsage = trailer.TryGetProperty("usage_count", out var count) && count.TryGetInt32(out var value) ? value : 0;
                }
            }
            _v15MarketAtUtc = DateTime.UtcNow;
            _v15MarketIndex = _v15Market.Count == 0 ? 0 : _v15MarketIndex % _v15Market.Count;
            RenderV15MarketTicker();
        }
        catch { }
        finally { _v15MarketLoading = false; }
    }

    private void RenderV15MarketTicker()
    {
        if (_v15MarketMain is null || _v15MarketMeta is null) return;
        if (_v15Market.Count == 0)
        {
            _v15MarketMain.Text = "📦 Mercado aguardando dados reais...";
            _v15MarketMeta.Text = "As categorias serão preenchidas conforme as viagens forem registradas.";
            return;
        }

        var maxRate = _v15Market.OrderByDescending(x => x.Rate).First();
        var mostWanted = _v15Market.OrderByDescending(x => x.Discovered).ThenBy(x => x.Cargo).First();
        var biggestRise = _v15Market.OrderByDescending(x => x.Rate - x.BaseRate).First();
        var biggestDrop = _v15Market.OrderBy(x => x.Rate - x.BaseRate).First();
        var page = _v15MarketIndex++ % 6;

        switch (page)
        {
            case 0:
                _v15MarketMain.Text = $"🔥 MAIS PROCURADA  •  {mostWanted.Cargo}";
                _v15MarketMeta.Text = $"{mostWanted.Discovered} descobertas  •  R$ {mostWanted.Rate:0.00}/km";
                break;
            case 1:
                _v15MarketMain.Text = $"💰 MAIOR TARIFA  •  {maxRate.Cargo}";
                _v15MarketMeta.Text = $"R$ {maxRate.Rate:0.00}/km  •  {FormatMarketStatus(maxRate.Status)}";
                break;
            case 2:
                _v15MarketMain.Text = $"📈 MAIOR ALTA  •  {biggestRise.Cargo}";
                _v15MarketMeta.Text = $"+R$ {Math.Max(0, biggestRise.Rate - biggestRise.BaseRate):0.00}/km sobre a referência";
                break;
            case 3:
                _v15MarketMain.Text = $"📉 MAIOR QUEDA  •  {biggestDrop.Cargo}";
                _v15MarketMeta.Text = $"R$ {Math.Min(0, biggestDrop.Rate - biggestDrop.BaseRate):0.00}/km sobre a referência";
                break;
            case 4:
                _v15MarketMain.Text = string.IsNullOrWhiteSpace(_v15Trailer) ? "🚛 REBOQUE MAIS UTILIZADO" : $"🚛 REBOQUE MAIS UTILIZADO  •  {_v15Trailer}";
                _v15MarketMeta.Text = _v15TrailerUsage > 0 ? $"{_v15TrailerUsage} registros reais de telemetria" : "Tipo de reboque ainda não informado pela telemetria";
                break;
            default:
                _v15MarketMain.Text = string.IsNullOrWhiteSpace(_v15ActiveDriver) ? "👤 MOTORISTA MAIS ATIVO" : $"👤 MOTORISTA MAIS ATIVO  •  {_v15ActiveDriver}";
                _v15MarketMeta.Text = _v15ActiveDriverTrips > 0 ? $"{_v15ActiveDriverTrips} viagens concluídas" : "Ainda não há viagens concluídas suficientes";
                break;
        }
    }

    private async Task DiscoverCargoV15Async(TelemetrySnapshot data)
    {
        var cargo = data.Cargo?.Trim();
        if (string.IsNullOrWhiteSpace(cargo) || cargo.Length < 2) return;
        if (string.Equals(cargo, _v15LastDiscoveredCargo, StringComparison.OrdinalIgnoreCase)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var payload = JsonSerializer.Serialize(new { cargo });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/cargo-market/discover");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _v15Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _v15LastDiscoveredCargo = cargo;
                await RefreshV15MarketAsync();
            }
        }
        catch { }
    }

    private async Task ReconcileTripV15Async(TelemetrySnapshot data)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _v15Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array) return;

            JsonElement? active = null;
            foreach (var trip in trips.EnumerateArray())
            {
                if (!trip.TryGetProperty("status", out var status) || !string.Equals(status.GetString(), "active", StringComparison.OrdinalIgnoreCase)) continue;
                var cargo = trip.TryGetProperty("cargo", out var c) ? c.GetString() : null;
                if (!string.IsNullOrWhiteSpace(data.Cargo) && !string.IsNullOrWhiteSpace(cargo) && !string.Equals(cargo, data.Cargo, StringComparison.OrdinalIgnoreCase)) continue;
                active = trip;
                break;
            }

            if (active is null)
            {
                if (HasActiveJob(data) && data.CargoLoaded && (data.PlannedDistanceKm > 0 || data.RouteDistanceKm > 0))
                    await RecoverMidTripV15Async(data, token);
                return;
            }

            var tripElement = active.Value;
            var id = tripElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) return;
            _serverTripId = id;
            _tripActive = true;
            if (tripElement.TryGetProperty("started_at", out var started) && DateTime.TryParse(started.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedStart))
                _tripStartedAtUtc = parsedStart.ToUniversalTime();

            var planned = data.PlannedDistanceKm > 0 ? data.PlannedDistanceKm : ReadFloat(tripElement, "planned_distance_km");
            var remaining = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : 0;
            var distance = ResolveCurrentTripDistance(data, planned, remaining);
            if (planned <= 0 && remaining > 0) planned = distance + remaining;
            var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;

            TripProgressText.Text = planned > 0 ? $"{progress * 100:0}%" : "—";
            TripRemainingText.Text = planned > 0 ? $"{Math.Max(0, remaining > 0 ? remaining : planned - distance):0.0} km restantes" : "distância restante indisponível";
            TripDistanceText.Text = $"{distance:0.0} km";
            TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc);
            TripStartText.Text = _tripStartedAtUtc.ToLocalTime().ToString("HH:mm");
            TripLiveText.Text = data.GamePaused ? "JOGO PAUSADO" : "AO VIVO";
            TripStatusText.Text = "VIAGEM EM ANDAMENTO";
            TripRouteText.Text = BuildRoute(data);
            TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            UpdateTripTruckV15(progress);

            if (!data.CargoLoaded && DateTime.UtcNow - _lastTripFinishedAtUtc > TimeSpan.FromSeconds(5))
                await FinishRecoveredTripV15Async(id, data, distance);
        }
        catch { }
    }

    private async Task RecoverMidTripV15Async(TelemetrySnapshot data, string token)
    {
        try
        {
            var planned = data.PlannedDistanceKm > 0 ? (float)data.PlannedDistanceKm : Math.Max(0, Math.Abs(data.SpeedKph) > 0 ? data.RouteDistanceKm : 0);
            var remaining = data.RouteDistanceKm > 0 ? data.RouteDistanceKm : 0;
            var traveled = planned > 0 && remaining > 0 ? Math.Max(0, planned - remaining) : 0;
            var startOdometer = Math.Max(0, data.OdometerKm - traveled);
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
                plannedDistanceKm = planned,
                cargoValueBrl = data.CargoValueBrl,
                startOdometerKm = startOdometer,
                startFuelL = data.FuelLiters,
                startedAt = DateTime.UtcNow
            };
            using var create = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips");
            create.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            create.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            create.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _v15Http.SendAsync(create);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("trip", out var trip) || !trip.TryGetProperty("id", out var idElement)) return;
            var id = idElement.GetString();
            if (string.IsNullOrWhiteSpace(id)) return;
            _serverTripId = id;
            _tripActive = true;
            _tripStartedAtUtc = DateTime.UtcNow;
            _tripStartOdometer = startOdometer;
            _tripStartFuel = data.FuelLiters;
            await SendRecoveryBaselineTelemetryAsync(id, data, startOdometer, token);
            await SendRecoveryBaselineTelemetryAsync(id, data, data.OdometerKm, token);
            StatusText.Text = $"TransPoli • viagem recuperada • {traveled:0.0} km já percorridos";
        }
        catch { }
    }

    private async Task SendRecoveryBaselineTelemetryAsync(string tripId, TelemetrySnapshot data, float odometer, string token)
    {
        var payload = new
        {
            recordedAt = DateTime.UtcNow,
            speedKph = Math.Abs(data.SpeedKph),
            rpm = data.Rpm,
            gear = data.Gear,
            fuelL = data.FuelLiters,
            odometerKm = odometer,
            fuelRangeKm = data.FuelRangeKm,
            gamePaused = data.GamePaused,
            engineEnabled = data.EngineEnabled,
            cargoDamage = data.CargoDamage,
            cargoMassKg = data.CargoMassKg,
            plannedDistanceKm = data.PlannedDistanceKm,
            cargoValueBrl = data.CargoValueBrl
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{tripId}/telemetry");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _v15Http.SendAsync(request);
    }

    private async Task FinishRecoveredTripV15Async(string tripId, TelemetrySnapshot data, float distance)
    {
        if (_v15FinishBusy) return;
        _v15FinishBusy = true;
        try
        {
            var token = SecureTokenStore.Read();
            if (string.IsNullOrWhiteSpace(token)) return;
            var payload = new { distanceKm = Math.Max(0, distance), cargoDamage = Math.Clamp(data.CargoDamage, 0f, 1f), cargoMassKg = Math.Max(0, data.CargoMassKg) };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/trips/{tripId}/finish");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _v15Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;
            _lastTripFinishedAtUtc = DateTime.UtcNow;
            _tripActive = false;
            _serverTripId = null;
            StatusText.Text = $"TransPoli • viagem finalizada • {distance:0.0} km • economia liquidada";
        }
        catch { }
        finally { _v15FinishBusy = false; }
    }

    private static float ResolveCurrentTripDistance(TelemetrySnapshot data, float planned, float remaining)
    {
        if (planned > 0 && remaining > 0) return Math.Max(0, planned - remaining);
        if (planned > 0 && data.OdometerKm > 0) return 0;
        return 0;
    }

    private static float ReadFloat(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number)) return number;
        return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private void UpdateTripTruckV15(float progress)
    {
        try
        {
            if (TripProgressFill.Parent is Grid grid && grid.ActualWidth > 0)
            {
                TripProgressFill.Width = grid.ActualWidth * progress;
                TripTruckText.Margin = new Thickness(Math.Max(-10, TripProgressFill.Width - 10), 0, 0, 0);
            }
        }
        catch { }
    }

    private void ReplaceLegacyAlertArea()
    {
        if (_v15MarketMain is not null) return;
        if (AlertText is null || AlertText.Parent is not StackPanel stack) return;
        var alertBorder = AlertText.Parent is StackPanel inner ? inner.Parent as Border : null;
        if (alertBorder is null || alertBorder.Parent is not Panel parent) return;
        var index = parent.Children.IndexOf(alertBorder);
        if (index < 0) return;
        parent.Children.Remove(alertBorder);

        var card = new Border
        {
            Background = FindResource("Panel") as Brush,
            BorderBrush = FindResource("Stroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 10, 0, 0),
            Height = 108,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "MERCADO DE CARGAS", Foreground = FindResource("Muted") as Brush, FontSize = 10, FontWeight = FontWeights.SemiBold });
        _v15MarketMain = new TextBlock { Text = "Carregando ofertas...", Foreground = FindResource("Text") as Brush, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _v15MarketMeta = new TextBlock { Text = "Tarifas por quilômetro • clique para abrir", Foreground = FindResource("Orange") as Brush, FontSize = 10, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        root.Children.Add(_v15MarketMain);
        root.Children.Add(_v15MarketMeta);
        card.Child = root;
        card.MouseLeftButtonUp += (_, _) => ShowCargoMarketModal();
        parent.Children.Insert(index, card);
    }

    private void HideLegacyOperationButtons()
    {
        foreach (var button in FindV15VisualChildren<Button>(this))
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (text.Contains("PARADAS", StringComparison.OrdinalIgnoreCase) || text.Contains("OCORR.", StringComparison.OrdinalIgnoreCase) || text.Contains("DOCS", StringComparison.OrdinalIgnoreCase))
                button.Visibility = Visibility.Collapsed;
        }
        if (OpsCounterText != null) OpsCounterText.Visibility = Visibility.Collapsed;
    }

    private void FixVehicleMiniCards()
    {
        foreach (var text in new[] { RpmText, GearText, CruiseText })
        {
            text.FontSize = 12;
            text.TextWrapping = TextWrapping.NoWrap;
            text.TextAlignment = TextAlignment.Center;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.MinWidth = 0;
        }
    }

    private void ApplyThinTabletFrame()
    {
        if (_v15FrameApplied || Content is not Grid oldRoot) return;
        _v15FrameApplied = true;
        oldRoot.Margin = new Thickness(10);
        var shell = new Grid();
        var frame = new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(45, 49, 55), Color.FromRgb(12, 15, 19), 90),
            BorderBrush = new SolidColorBrush(Color.FromRgb(84, 91, 101)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(34),
            Margin = new Thickness(2)
        };
        shell.Children.Add(frame);
        shell.Children.Add(oldRoot);

        var camera = new Border
        {
            Width = 13, Height = 13, CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromRgb(7, 9, 12)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 81, 91)), BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 7, 0, 0)
        };
        shell.Children.Add(camera);

        var speaker = new Border
        {
            Width = 76, Height = 5, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(42, 47, 54)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8)
        };
        shell.Children.Add(speaker);

        AddSideButton(shell, HorizontalAlignment.Left, new Thickness(0, 0, 0, 0));
        AddSideButton(shell, HorizontalAlignment.Right, new Thickness(0, 0, 0, 0));
        Content = shell;
    }

    private static void AddSideButton(Grid shell, HorizontalAlignment side, Thickness margin)
    {
        var button = new Border
        {
            Width = 5, Height = 58, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(31, 35, 41)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 75, 84)), BorderThickness = new Thickness(1),
            HorizontalAlignment = side, VerticalAlignment = VerticalAlignment.Center, Margin = margin
        };
        shell.Children.Add(button);
    }

    private static IEnumerable<T> FindV15VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in FindV15VisualChildren<T>(child)) yield return nested;
        }
    }

    private sealed record CargoTickerItem(string Cargo, decimal Rate, decimal BaseRate, int Discovered, string Status);
}
