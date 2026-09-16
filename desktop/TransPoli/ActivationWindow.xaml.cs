using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TransPoli;

public partial class ActivationWindow : Window
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public ActivationWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RestoreOrRequireActivation();
    }

    private async Task RestoreOrRequireActivation()
    {
        try
        {
            var token = SecureTokenStore.Read();
            if (!string.IsNullOrWhiteSpace(token))
            {
                SetStatus("Verificando licença neste computador...", false);
                if (await ValidateSession(token))
                {
                    OpenTransPoli();
                    return;
                }

                SecureTokenStore.Delete();
                SetStatus("Sua sessão local expirou, a licença venceu ou este computador foi liberado.", true);
            }
            else
            {
                SetStatus("Informe seu e-mail e o PIN de 6 dígitos.", false);
            }
            EmailBox.Focus();
        }
        catch
        {
            SetStatus("Não foi possível verificar a ativação. Faça a ativação novamente.", true);
            EmailBox.Focus();
        }
    }

    private async Task<bool> ValidateSession(string token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/license");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("license", out var license)) return false;
            var status = license.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
            var type = license.TryGetProperty("license_type", out var typeElement) ? typeElement.GetString() : null;
            if (status is not ("trial" or "active") || type is not ("trial" or "lifetime")) return false;

            using var deviceRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/device");
            deviceRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            deviceRequest.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var deviceResponse = await _http.SendAsync(deviceRequest);
            if (!deviceResponse.IsSuccessStatusCode) return false;

            using var deviceDoc = JsonDocument.Parse(await deviceResponse.Content.ReadAsStringAsync());
            if (!deviceDoc.RootElement.TryGetProperty("device", out var device) || device.ValueKind == JsonValueKind.Null) return false;
            var deviceStatus = device.TryGetProperty("status", out var deviceStatusElement) ? deviceStatusElement.GetString() : null;
            var boundDeviceId = device.TryGetProperty("deviceId", out var deviceIdElement) ? deviceIdElement.GetString() : null;
            return deviceStatus == "active" && string.Equals(boundDeviceId, DeviceIdentity.GetOrCreate(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text.Trim();
        var pin = PinBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(email) || pin.Length != 6)
        {
            SetStatus("Informe seu e-mail e o PIN de 6 dígitos.", true);
            return;
        }

        ActivateButton.IsEnabled = false;
        SetStatus("Validando conta e vinculando este computador...", false);

        try
        {
            var payload = new { email, pin, deviceId = DeviceIdentity.GetOrCreate(), deviceName = Environment.MachineName };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{ApiBaseUrl}/auth/activate", content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string message = "Não foi possível ativar o TransPoli.";
                string? code = null;
                try
                {
                    using var errorDoc = JsonDocument.Parse(json);
                    if (errorDoc.RootElement.TryGetProperty("error", out var error)) message = error.GetString() ?? message;
                    if (errorDoc.RootElement.TryGetProperty("code", out var codeElement)) code = codeElement.GetString();
                }
                catch { }

                if (code == "DEVICE_ALREADY_BOUND") message = "Esta licença já está vinculada a outro computador. Acesse o painel TransPoli e libere o dispositivo atual antes de ativar este computador.";
                else if (code == "LICENSE_INACTIVE") message = "Sua licença não está ativa. Acesse o painel TransPoli para verificar a situação da licença.";
                SetStatus(message, true);
                return;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("accessToken", out var tokenElement))
            {
                SetStatus("A API não retornou um token de ativação válido.", true);
                return;
            }

            var token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token))
            {
                SetStatus("A API não retornou um token de ativação válido.", true);
                return;
            }

            SecureTokenStore.Save(token);
            SetStatus("TransPoli ativado neste computador.", false);
            OpenTransPoli();
        }
        catch (HttpRequestException)
        {
            SetStatus("Não foi possível conectar à API do TransPoli. Verifique sua conexão com a internet.", true);
        }
        catch (TaskCanceledException)
        {
            SetStatus("A conexão demorou demais. Tente novamente.", true);
        }
        catch
        {
            SetStatus("Ocorreu um erro ao ativar o TransPoli.", true);
        }
        finally
        {
            ActivateButton.IsEnabled = true;
        }
    }

    private void OpenTransPoli()
    {
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }

    private void SetStatus(string message, bool error)
    {
        StatusText.Text = message;
        StatusText.Foreground = FindResource(error ? "Orange" : "Green") as System.Windows.Media.Brush;
    }

    protected override void OnClosed(EventArgs e)
    {
        _http.Dispose();
        base.OnClosed(e);
    }
}
