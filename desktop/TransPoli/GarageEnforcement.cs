using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TransPoli.GameSave;

namespace TransPoli;

/// <summary>
/// 🔒 GARAGEM EXCLUSIVA
///
/// O caminhão fica vinculado ao motorista. Se a telemetria mostrar um
/// caminhão que não está na garagem dele — ou que pertence a outro —
/// o tablet trava o veículo e o bloqueio físico entra em ação.
///
/// O que estava errado antes:
///   • o bloqueio só trocava textos na tela; o botão "DESBLOQUEAR"
///     continuava funcionando e o loop de telemetria o reabilitava a
///     cada 500 ms, anulando o bloqueio;
///   • não havia como vincular um caminhão pelo tablet, então a
///     garagem ficava sempre vazia e nunca bloqueava nada;
///   • uma falha de rede era tratada como "autorizado" para sempre.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _garageTimer;
    private bool _garageUnauthorized;
    private string _garageReason = "";
    private string _garageMessage = "";
    private string _garageTruckKey = "";
    private DateTime _lastGarageCheck = DateTime.MinValue;
    private DateTime _lastGarageSuccess = DateTime.MinValue;
    private bool _garageBusy;

    // Cache curto da garagem: a lista e o vínculo atual não mudam a cada
    // abertura do modal. O timer de autorização continua independente.
    private static readonly TimeSpan GarageCacheLifetime = TimeSpan.FromSeconds(15);
    private string? _garageCacheToken;
    private string? _garageCacheJson;
    private DateTime _garageCacheAtUtc;

    /// <summary>Janela de tolerância para instabilidade da API.</summary>
    private static readonly TimeSpan GarageGracePeriod = TimeSpan.FromMinutes(3);

    /// <summary>Verdadeiro quando o veículo não pode ser liberado de jeito nenhum.</summary>
    internal bool GarageBlocksOperation => _garageUnauthorized;

    private void StartGarageEnforcement()
    {
        _garageTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _garageTimer.Tick -= GarageTimer_Tick;
        _garageTimer.Tick += GarageTimer_Tick;
        if (!_garageTimer.IsEnabled) _garageTimer.Start();
    }

    private async void GarageTimer_Tick(object? sender, EventArgs e)
    {
        if (_garageBusy) return;
        if (DateTime.UtcNow - _lastGarageCheck < TimeSpan.FromSeconds(30)) return;
        _lastGarageCheck = DateTime.UtcNow;
        _garageBusy = true;
        try { await CheckGarageAuthorizationAsync(); }
        finally { _garageBusy = false; }
    }

    private async Task CheckGarageAuthorizationAsync()
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;

        TelemetrySnapshot? data;
        try
        {
            using var telemetryResponse = await _http.GetAsync(TelemetryUrl);
            if (!telemetryResponse.IsSuccessStatusCode) return;
            await using var stream = await telemetryResponse.Content.ReadAsStreamAsync();
            data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(
                stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return; }

        if (data is null || !data.Connected)
        {
            ApplyGarageBlock(
                "telemetry_unavailable",
                "Aguardando telemetria do ETS2 para confirmar o caminhão. O bloqueio permanece ativo por segurança.");
            return;
        }

        // A garagem é autorizada somente com a identidade do caminhão
        // entregue pela telemetria ao vivo. O save não participa desta decisão.
        if (string.IsNullOrWhiteSpace(data.TruckBrand) && string.IsNullOrWhiteSpace(data.TruckModel))
        {
            ApplyGarageBlock(
                "telemetry_unavailable",
                "A telemetria ainda não identificou o caminhão. Entre no ETS2 e aguarde a leitura.");
            return;
        }

        try
        {
            var url = $"{ApiBaseUrl}/me/garage/authorize" +
                      $"?brand={Uri.EscapeDataString(data.TruckBrand ?? string.Empty)}" +
                      $"&model={Uri.EscapeDataString(data.TruckModel ?? string.Empty)}" +
                      $"&plate={Uri.EscapeDataString(data.LicensePlate ?? string.Empty)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) { ApplyGarageGrace(); return; }

            var root = J.Parse(await response.Content.ReadAsStringAsync());
            if (root is null) { ApplyGarageGrace(); return; }

            _lastGarageSuccess = DateTime.UtcNow;

            var configured = J.Bool(root, "configured");
            var authorized = !configured || J.Bool(root, "authorized", true);
            _garageTruckKey = J.Str(root, "truckKey");

            if (authorized) { ClearGarageBlock(); return; }

            ApplyGarageBlock(
                J.Str(root, "reason", "not_in_garage"),
                J.Str(root, "message", "Caminhão não autorizado para este motorista."));
        }
        catch
        {
            ApplyGarageGrace();
        }
    }

    /// <summary>
    /// Se a API ficar fora do ar, mantém o último estado conhecido por um
    /// tempo. Passada a tolerância, um bloqueio ativo continua valendo —
    /// derrubar o servidor não pode ser a forma de liberar o caminhão.
    /// </summary>
    private void ApplyGarageGrace()
    {
        if (!_garageUnauthorized) return;
        if (DateTime.UtcNow - _lastGarageSuccess > GarageGracePeriod)
            ApplyGarageBlock(_garageReason, _garageMessage);
    }

    private void ApplyGarageBlock(string reason, string message)
    {
        _garageUnauthorized = true;
        _garageReason = reason;
        _garageMessage = string.IsNullOrWhiteSpace(message)
            ? "Caminhão não autorizado na garagem TransPoli."
            : message;
        _truckLocked = true;

        VehicleLockText.Text = reason == "foreign_truck"
            ? "🔒 CAMINHÃO DE OUTRO MOTORISTA"
            : reason == "telemetry_unavailable"
                ? "🔒 AGUARDANDO TELEMETRIA"
                : "🔒 CAMINHÃO FORA DA SUA GARAGEM";
        VehicleLockText.Foreground = FindResource("Yellow") as Brush;

        UnlockButton.IsEnabled = false;
        UnlockButton.Opacity = 0.4;
        UnlockButton.Content = "🔒 BLOQUEIO DA GARAGEM ATIVO";

        AlertText.Text = _garageMessage;
        AlertText.Foreground = FindResource("Yellow") as Brush;
        StatusText.Text = reason == "telemetry_unavailable"
            ? "TransPoli • aguardando telemetria do ETS2 • bloqueio seguro"
            : "TransPoli • bloqueio de garagem ativo • abra 🚛 GARAGEM para vincular";
    }

    private void ClearGarageBlock()
    {
        if (!_garageUnauthorized) return;
        _garageUnauthorized = false;
        _garageReason = "";
        _garageMessage = "";
        UnlockButton.Content = "🔓 DESBLOQUEAR CAMINHÃO";
        StatusText.Text = "TransPoli • caminhão autorizado na garagem";
    }

    /* ======================= TELA DA GARAGEM ======================= */

    internal async void ShowGarageTabletModal()
    {
        if (EnsureModalHost() == null) return;
        ShowModalContent("garage", BuildModalLoading("🚛 CARREGANDO GARAGEM..."));

        // A telemetria é a única informação dinâmica necessária para o
        // topo. A lista da garagem é carregada separadamente e pode ser
        // reaproveitada por alguns segundos, evitando a sensação de espera
        // toda vez que o motorista abre/fecha a tela.
        var telemetry = await LoadCurrentTelemetryAsync();
        GameSaveSnapshot? save = null;
        try { save = await _gameSaveIntegration.RefreshAsync(); } catch { }
        var panel = new StackPanel();

        var overview = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
        overview.Children.Add(MiniCard("STATUS", _garageUnauthorized ? "BLOQUEADO" : "AUTORIZADO"));
        overview.Children.Add(MiniCard("FROTA NO SAVE", (save?.Trucks.Count ?? 0).ToString()));
        overview.Children.Add(MiniCard("REBOQUES", (save?.Trailers.Count ?? 0).ToString()));
        overview.Children.Add(MiniCard("HQ", string.IsNullOrWhiteSpace(save?.HeadquartersCity) ? "—" : save!.HeadquartersCity!));
        panel.Children.Add(overview);

        /* Estado do bloqueio */
        if (_garageUnauthorized)
        {
            panel.Children.Add(new Border
            {
                Background = FindResource("Panel2") as Brush,
                BorderBrush = FindResource("Yellow") as Brush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = new TextBlock
                {
                    Text = $"🔒 BLOQUEIO ATIVO\n\n{_garageMessage}",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Yellow") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        /* Autorização baseada exclusivamente na telemetria ao vivo.
           O save do ETS2 não participa mais do bloqueio/liberação. */
        panel.Children.Add(ModalLine(
            "🔐 A autorização usa somente a telemetria ao vivo do ETS2. O save não é usado para liberar o caminhão.", 10));

        /* Caminhão atual da telemetria */
        panel.Children.Add(ModalLabel("VEÍCULO OPERACIONAL • TELEMETRIA AO VIVO"));
        var hasTruck = telemetry != null &&
                       (!string.IsNullOrWhiteSpace(telemetry.TruckBrand) || !string.IsNullOrWhiteSpace(telemetry.TruckModel));

        if (!hasTruck)
        {
            panel.Children.Add(ModalLine("Nenhum caminhão detectado. Entre no ETS2 com o caminhão carregado.", 13));
        }
        else
        {
            var current = new StackPanel();
            current.Children.Add(ModalValueRow("Marca", telemetry!.TruckBrand ?? "—"));
            current.Children.Add(ModalValueRow("Modelo", telemetry.TruckModel ?? "—"));
            current.Children.Add(ModalValueRow("Placa",
                string.IsNullOrWhiteSpace(telemetry.LicensePlate) ? "sem placa" : telemetry.LicensePlate!));
            panel.Children.Add(ModalPanel(current));

            var currentTruckKey = GarageTruckKey(telemetry.TruckBrand, telemetry.TruckModel, telemetry.LicensePlate);
            var alreadyLinked = !string.IsNullOrWhiteSpace(_garageTruckKey) &&
                                string.Equals(currentTruckKey, _garageTruckKey, StringComparison.Ordinal);
            var bind = ModalButton(alreadyLinked ? "✓ CAMINHÃO JÁ VINCULADO A VOCÊ" : "🔗 VINCULAR ESTE CAMINHÃO A MIM");
            bind.IsEnabled = !alreadyLinked;
            bind.Opacity = alreadyLinked ? 0.55 : 1.0;
            bind.Click += async (_, e) => { e.Handled = true; await BindCurrentTruckAsync(telemetry); };
            panel.Children.Add(bind);
        }

        if (save?.CurrentTruck is { } savedTruck)
        {
            panel.Children.Add(ModalLabel("VEÍCULO PERSISTENTE • GAME.SII"));
            var saved = new UniformGrid { Columns = 3 };
            saved.Children.Add(MiniCard("PLACA", string.IsNullOrWhiteSpace(savedTruck.LicensePlate) ? "—" : savedTruck.LicensePlate));
            saved.Children.Add(MiniCard("ODÔMETRO", $"{savedTruck.OdometerKm:0.0} km"));
            saved.Children.Add(MiniCard("COMBUSTÍVEL", $"{savedTruck.FuelPercent:0}%"));
            panel.Children.Add(saved);
        }

        if (save?.CurrentTrailer is { } currentTrailer)
        {
            panel.Children.Add(ModalLabel("REBOQUE ACOPLADO"));
            var trailer = new UniformGrid { Columns = 3 };
            trailer.Children.Add(MiniCard("PLACA", string.IsNullOrWhiteSpace(currentTrailer.LicensePlate) ? "—" : currentTrailer.LicensePlate));
            trailer.Children.Add(MiniCard("CARGA", currentTrailer.CargoMassKg > 0 ? $"{currentTrailer.CargoMassKg / 1000.0:0.0} t" : "—"));
            trailer.Children.Add(MiniCard("DANO CARGA", $"{Math.Clamp(currentTrailer.CargoDamage * 100.0, 0, 100):0.0}%"));
            panel.Children.Add(trailer);
        }

        /* Garagem cadastrada */
        panel.Children.Add(ModalLabel("GARAGEM ONLINE • VÍNCULOS EXCLUSIVOS"));
        var token = SecureTokenStore.Read();

        if (string.IsNullOrWhiteSpace(token))
        {
            panel.Children.Add(ModalLine("Ative o computador de bordo para acessar sua garagem.", 13));
        }
        else
        {
            try
            {
                var garageJson = await LoadGarageCachedAsync(token);
                if (garageJson is null)
                {
                    panel.Children.Add(ModalLine(
                        "Não foi possível consultar a garagem agora. O bloqueio continua valendo com o último estado conhecido.", 13));
                }
                else
                {
                    var root = J.Parse(garageJson);
                    var any = false;

                    foreach (var item in J.Array(root, "garage"))
                    {
                        any = true;
                        var brand = J.Str(item, "brand");
                        var model = J.Str(item, "model");
                        var name = J.Str(item, "truck_name", $"{brand} {model}".Trim());
                        var plate = J.Str(item, "license_plate", "sem placa");
                        var exclusive = J.Bool(item, "exclusive", true);
                        var id = J.Str(item, "id");

                        var card = new StackPanel();
                        card.Children.Add(new TextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(name) ? "Caminhão" : name,
                            FontSize = 15,
                            FontWeight = FontWeights.Bold,
                            Foreground = FindResource("Text") as Brush,
                            TextWrapping = TextWrapping.Wrap
                        });
                        card.Children.Add(ModalValueRow("Placa", plate));
                        card.Children.Add(ModalValueRow("Status",
                            exclusive ? "EXCLUSIVO" : "compartilhado",
                            exclusive ? "Green" : "Muted"));

                        var remove = ModalButton("✕ REMOVER DA GARAGEM");
                        remove.Click += async (_, e) => { e.Handled = true; await RemoveFromGarageAsync(id); };
                        card.Children.Add(remove);

                        panel.Children.Add(ModalPanel(card));
                    }

                    if (!any)
                        panel.Children.Add(ModalLine(
                            "Sua garagem está vazia. Enquanto nenhum caminhão estiver vinculado, todos são liberados. Vincule um caminhão para ativar a exclusividade.", 13));
                }
            }
            catch
            {
                panel.Children.Add(ModalLine("Erro de comunicação com a garagem. Tente novamente em instantes.", 13));
            }
        }

                panel.Children.Add(ModalLine(
            "🔐 Caminhão não autorizado tem o freio de estacionamento aplicado automaticamente pelo TransPoli.", 11));

        ShowModalContent("garage", BuildModalCard(
            "🚛 CENTRAL DE GARAGEM & FROTA",
            panel,
            "Telemetria para autorização • game.sii para inventário e contexto • segurança TransPoli"));
    }

    private async Task<string?> LoadGarageCachedAsync(string token)
    {
        if (_garageCacheJson != null &&
            string.Equals(_garageCacheToken, token, StringComparison.Ordinal) &&
            DateTime.UtcNow - _garageCacheAtUtc < GarageCacheLifetime)
            return _garageCacheJson;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/garage");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
        using var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode) return null;

        _garageCacheJson = await response.Content.ReadAsStringAsync();
        _garageCacheToken = token;
        _garageCacheAtUtc = DateTime.UtcNow;
        return _garageCacheJson;
    }

    private void InvalidateGarageCache()
    {
        _garageCacheJson = null;
        _garageCacheToken = null;
        _garageCacheAtUtc = default;
    }

    private async Task BindCurrentTruckAsync(TelemetrySnapshot? telemetry)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token) || telemetry is null) return;

        try
        {
            var payload = new
            {
                brand = telemetry.TruckBrand ?? "",
                model = telemetry.TruckModel ?? "",
                plate = telemetry.LicensePlate ?? "",
                label = $"{telemetry.TruckBrand} {telemetry.TruckModel}".Trim()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/garage/bind-current");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                StatusText.Text = "TransPoli • caminhão vinculado à sua garagem";
                InvalidateGarageCache();
                _lastGarageCheck = DateTime.MinValue;
                await CheckGarageAuthorizationAsync();
            }
            else
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                StatusText.Text = "TransPoli • " + J.Str(root, "error", "não foi possível vincular o caminhão");
            }
        }
        catch
        {
            StatusText.Text = "TransPoli • falha de comunicação com a garagem";
        }

        ShowGarageTabletModal();
    }

    private async Task RemoveFromGarageAsync(string assignmentId)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(assignmentId)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBaseUrl}/me/garage/{assignmentId}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            StatusText.Text = response.IsSuccessStatusCode
                ? "TransPoli • vínculo removido da garagem"
                : "TransPoli • não foi possível remover o vínculo";
            InvalidateGarageCache();
            _lastGarageCheck = DateTime.MinValue;
        }
        catch
        {
            StatusText.Text = "TransPoli • falha de comunicação com a garagem";
        }

        ShowGarageTabletModal();
    }
}
