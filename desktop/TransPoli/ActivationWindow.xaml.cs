using System;
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
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            SetStatus("Informe seu e-mail e o PIN de 6 dígitos.", false);
            EmailBox.Focus();
            return;
        }

        SetStatus("Sessão encontrada. Verificando este computador...", false);
        var validation = await ValidateSession(token);

        if (validation == SessionValidation.Valid || validation == SessionValidation.NetworkError)
        {
            OpenTransPoli();
            return;
        }

        SecureTokenStore.Delete();
        SetStatus("Sua sessão expirou, a licença venceu ou este computador foi liberado. Ative novamente.", true);
        EmailBox.Focus();
    }

    private async Task<SessionValidation> ValidateSession(string token)
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

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return SessionValidation.Valid;
            if ((int)response.StatusCode >= 500) return SessionValidation.NetworkError;

            return (int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                ? SessionValidation.Invalid
                : SessionValidation.NetworkError;
        }
        catch (HttpRequestException) { return SessionValidation.NetworkError; }
        catch (TaskCanceledException) { return SessionValidation.NetworkError; }
        catch { return SessionValidation.NetworkError; }
    }

    private enum SessionValidation
    {
        Valid,
        Invalid,
        NetworkError
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
                try
                {
                    using var errorDoc = JsonDocument.Parse(json);
                    if (errorDoc.RootElement.TryGetProperty("error", out var error))
                        message = error.GetString() ?? message;
                }
                catch { }

                SetStatus(message, true);
                return;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("accessToken", out var tokenElement))
            {
                SetStatus("A API não retornou um token de ativação válido.", true);
                return;
            }

            var newToken = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(newToken))
            {
                SetStatus("A API não retornou um token de ativação válido.", true);
                return;
            }

            SecureTokenStore.Save(newToken);
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
        try
        {
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível abrir o TransPoli.\n\n{ex.Message}",
                "TransPoli — erro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
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
