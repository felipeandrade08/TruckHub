using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TransPoli;

internal sealed class LicenseHeartbeat : IDisposable
{
    // Produção: o mesmo Worker usado pela ativação e pelo restante do aplicativo.
    // Nunca usar localhost aqui: o heartbeat precisa validar a licença no servidor.
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private const string StateFileName = "license-heartbeat.dat";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NetworkGrace = TimeSpan.FromHours(24);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TransPoli-LicenseHeartbeat-v1");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly CancellationTokenSource _cts = new();
    private readonly string _statePath;
    private int _enforcementRunning;
    private bool _disposed;

    public LicenseHeartbeat()
    {
        var folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        System.IO.Directory.CreateDirectory(folder);
        _statePath = System.IO.Path.Combine(folder, StateFileName);
    }

    public void Start() => _ = RunAsync(_cts.Token);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            try { await ValidateAndEnforceAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex) { App.WriteUiCrashLog("LicenseHeartbeat.ValidateLoop", ex); }
            try { await Task.Delay(HeartbeatInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ValidateAndEnforceAsync(CancellationToken cancellationToken)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;

        var result = await SendHeartbeatAsync(token, cancellationToken).ConfigureAwait(false);
        if (result.Kind == HeartbeatResultKind.Valid)
        {
            SaveLastSuccess(DateTimeOffset.UtcNow);
            return;
        }

        if (result.Kind is HeartbeatResultKind.InvalidLicense or HeartbeatResultKind.InvalidDevice or HeartbeatResultKind.InvalidSession)
        {
            await EnforceLockAsync(result.Message).ConfigureAwait(false);
            return;
        }

        // Falha de rede não deve derrubar o aplicativo em poucos minutos.
        // O acesso offline fica tolerado por até 24h desde a última validação bem-sucedida.
        var lastSuccess = ReadLastSuccess();
        if (lastSuccess.HasValue && DateTimeOffset.UtcNow - lastSuccess.Value <= NetworkGrace)
            return;

        // Se nunca houve validação bem-sucedida, damos uma pequena tolerância inicial
        // para permitir que uma conexão lenta se estabeleça após a ativação.
        if (!lastSuccess.HasValue)
            return;

        await EnforceLockAsync("Não foi possível validar sua licença no servidor por mais de 24 horas.").ConfigureAwait(false);
    }

    private async Task<HeartbeatResult> SendHeartbeatAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/device/heartbeat");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { deviceId = DeviceIdentity.GetOrCreate() }),
                Encoding.UTF8,
                "application/json");
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var successDoc = JsonDocument.Parse(json);
                    if (successDoc.RootElement.TryGetProperty("user", out var user) &&
                        user.ValueKind == JsonValueKind.Object &&
                        user.TryGetProperty("id", out var id))
                    {
                        var confirmedUserId = id.GetString() ?? "";
                        var persistedUserId = SecureTokenStore.ReadUserId();
                        if (!string.IsNullOrWhiteSpace(persistedUserId) &&
                            !string.Equals(persistedUserId, confirmedUserId, StringComparison.OrdinalIgnoreCase))
                            return new HeartbeatResult(HeartbeatResultKind.InvalidSession, "A sessão recebida pertence a outra identidade TransPoli. Entre novamente.");
                        SecureTokenStore.SaveUserId(confirmedUserId);
                    }
                }
                catch (Exception ex)
                {
                    App.WriteUiCrashLog("LicenseHeartbeat.ParseIdentity", ex);
                    return new HeartbeatResult(HeartbeatResultKind.InvalidSession, "Não foi possível confirmar a identidade da sessão TransPoli.");
                }
                return new HeartbeatResult(HeartbeatResultKind.Valid, "OK");
            }

            string? code = null;
            string message = "Licença não autorizada neste computador.";
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("code", out var codeElement)) code = codeElement.GetString();
                if (doc.RootElement.TryGetProperty("error", out var errorElement)) message = errorElement.GetString() ?? message;
            }
            catch { }

            return code switch
            {
                "SESSION_INVALID" => new HeartbeatResult(HeartbeatResultKind.InvalidSession, "Sua sessão expirou. Faça a ativação novamente."),
                "LICENSE_INACTIVE" => new HeartbeatResult(HeartbeatResultKind.InvalidLicense, message),
                "DEVICE_NOT_BOUND" => new HeartbeatResult(HeartbeatResultKind.InvalidDevice, message),
                "INVALID_DEVICE_ID" => new HeartbeatResult(HeartbeatResultKind.InvalidDevice, message),
                _ when (int)response.StatusCode is >= 401 and < 500 => new HeartbeatResult(HeartbeatResultKind.InvalidSession, message),
                _ => new HeartbeatResult(HeartbeatResultKind.NetworkError, message)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HeartbeatResult(HeartbeatResultKind.NetworkError, "Tempo limite ao validar a licença.");
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("LicenseHeartbeat.Send", ex);
            return new HeartbeatResult(HeartbeatResultKind.NetworkError, "Não foi possível conectar ao servidor de licença.");
        }
    }

    private async Task EnforceLockAsync(string reason)
    {
        if (Interlocked.Exchange(ref _enforcementRunning, 1) == 1) return;
        try
        {
            // Fecha o cockpit enquanto users.id ainda existe. OnClosed consegue
            // persistir a TripSession ativa antes de a credencial ser revogada.
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (Window window in Application.Current.Windows)
                    if (window is MainWindow)
                    {
                        try { window.Close(); }
                        catch (Exception ex) { App.WriteUiCrashLog("LicenseHeartbeat.CloseMainWindow", ex); }
                    }
            });

            SecureTokenStore.Delete();
            DeleteState();

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is ActivationWindow)
                    {
                        if (!window.IsVisible) window.Show();
                        window.Activate();
                        MessageBox.Show(reason, "TransPoli", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var activation = new ActivationWindow();
                Application.Current.MainWindow = activation;
                activation.Show();
                MessageBox.Show(reason, "TransPoli", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }
        finally { Interlocked.Exchange(ref _enforcementRunning, 0); }
    }

    private void SaveLastSuccess(DateTimeOffset value)
    {
        try
        {
            var plain = Encoding.UTF8.GetBytes(value.ToString("O"));
            var protectedData = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            System.IO.File.WriteAllBytes(_statePath, protectedData);
        }
        catch { }
    }

    private DateTimeOffset? ReadLastSuccess()
    {
        try
        {
            if (!System.IO.File.Exists(_statePath)) return null;
            var plain = ProtectedData.Unprotect(System.IO.File.ReadAllBytes(_statePath), Entropy, DataProtectionScope.CurrentUser);
            return DateTimeOffset.TryParse(Encoding.UTF8.GetString(plain), out var value) ? value : null;
        }
        catch { return null; }
    }

    private void DeleteState()
    {
        try { if (System.IO.File.Exists(_statePath)) System.IO.File.Delete(_statePath); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _http.Dispose();
    }

    private enum HeartbeatResultKind { Valid, NetworkError, InvalidSession, InvalidLicense, InvalidDevice }
    private readonly record struct HeartbeatResult(HeartbeatResultKind Kind, string Message);
}
