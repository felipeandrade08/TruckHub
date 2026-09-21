using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TransPoli;

public partial class DirectorCenterWindow : Window
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string? _directorToken;

    public DirectorCenterWindow()
    {
        InitializeComponent();
        DirectorEmailBox.Focus();
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void BackToDriver_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowSetup_Click(object sender, RoutedEventArgs e)
    {
        LoginView.Visibility = Visibility.Collapsed;
        SetupView.Visibility = Visibility.Visible;
        SetupStatusText.Text = "";
        OwnerEmailBox.Focus();
    }

    private void BackToLogin_Click(object sender, RoutedEventArgs e)
    {
        SetupView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        StatusText.Text = "Pronto para entrar.";
        DirectorEmailBox.Focus();
    }

    private async void ForgotPin_Click(object sender, RoutedEventArgs e)
    {
        var email = DirectorEmailBox.Text.Trim();
        if (!IsEmail(email))
        {
            StatusText.Text = "Informe primeiro o e-mail da Diretoria.";
            DirectorEmailBox.Focus();
            return;
        }

        try
        {
            StatusText.Text = "Enviando instruções de recuperação...";
            var (ok, json) = await PostAsync("/director/pin-recovery/request", new { email });
            StatusText.Text = ok
                ? "Se o e-mail estiver cadastrado, as instruções foram enviadas. Verifique também o spam."
                : ApiMessage(json, "Não foi possível iniciar a recuperação.");
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DirectorCenterWindow.ForgotPin", ex);
            StatusText.Text = "Não foi possível solicitar a recuperação agora.";
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var email = DirectorEmailBox.Text.Trim();
        var pin = DirectorPinBox.Password.Trim();
        if (!IsEmail(email) || pin.Length != 6)
        {
            StatusText.Text = "Informe o e-mail da diretoria e o PIN de 6 dígitos.";
            return;
        }

        SetBusy(LoginButton, "ENTRANDO...");
        try
        {
            var (ok, json) = await PostAsync("/director/login", new { email, pin });
            if (!ok)
            {
                StatusText.Text = ApiMessage(json, "Não foi possível entrar na Central.");
                return;
            }

            _directorToken = JsonProperty(json, "accessToken");
            if (string.IsNullOrWhiteSpace(_directorToken))
            {
                StatusText.Text = "A API não retornou uma sessão administrativa válida.";
                return;
            }

            await LoadDashboardAsync();
        }
        catch (HttpRequestException)
        {
            StatusText.Text = "Não foi possível conectar ao servidor.";
        }
        catch (TaskCanceledException)
        {
            StatusText.Text = "A conexão demorou demais. Tente novamente.";
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DirectorCenterWindow.Login", ex);
            StatusText.Text = "Erro ao abrir a Central da Diretoria.";
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "ENTRAR NA CENTRAL  ›";
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        var ownerEmail = OwnerEmailBox.Text.Trim();
        var ownerPin = OwnerPinBox.Password.Trim();
        var companyName = CompanyNameBox.Text.Trim();
        var directorEmail = SetupDirectorEmailBox.Text.Trim();
        var directorPin = SetupDirectorPinBox.Password.Trim();

        if (!IsEmail(ownerEmail) || ownerPin.Length != 6)
        {
            SetupStatusText.Text = "Confirme o e-mail e o PIN da conta proprietária.";
            return;
        }
        if (companyName.Length < 2)
        {
            SetupStatusText.Text = "Informe o nome da empresa.";
            return;
        }
        if (!IsEmail(directorEmail) || directorPin.Length != 6)
        {
            SetupStatusText.Text = "Informe um e-mail válido e um PIN de 6 dígitos para a diretoria.";
            return;
        }

        SetBusy(SetupButton, "CRIANDO...");
        try
        {
            var (authOk, authJson) = await PostAsync("/auth/activate", new
            {
                email = ownerEmail,
                pin = ownerPin,
                deviceId = DeviceIdentity.GetOrCreate(),
                deviceName = Environment.MachineName
            });

            if (!authOk)
            {
                SetupStatusText.Text = ApiMessage(authJson, "A conta proprietária não pôde ser autenticada.");
                return;
            }

            var accountToken = JsonProperty(authJson, "accessToken");
            if (string.IsNullOrWhiteSpace(accountToken))
            {
                SetupStatusText.Text = "A conta proprietária não retornou uma sessão válida.";
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + "/director/bootstrap");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accountToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { companyName, directorEmail, directorPin }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                SetupStatusText.Text = ApiMessage(json, "Não foi possível criar a Central.");
                return;
            }

            SetupStatusText.Text = "Central criada. Agora entre com o e-mail e o PIN exclusivo da diretoria.";
            DirectorEmailBox.Text = directorEmail;
            DirectorPinBox.Password = directorPin;
            SetupView.Visibility = Visibility.Collapsed;
            LoginView.Visibility = Visibility.Visible;
            StatusText.Text = "Central criada com sucesso. Faça o primeiro acesso.";
        }
        catch (HttpRequestException)
        {
            SetupStatusText.Text = "Não foi possível conectar ao servidor.";
        }
        catch (TaskCanceledException)
        {
            SetupStatusText.Text = "A conexão demorou demais. Tente novamente.";
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DirectorCenterWindow.Setup", ex);
            SetupStatusText.Text = "Erro ao criar a Central.";
        }
        finally
        {
            SetupButton.IsEnabled = true;
            SetupButton.Content = "CRIAR CENTRAL  ›";
        }
    }

    private async Task LoadDashboardAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + "/director/dashboard");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _directorToken);
        using var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            StatusText.Text = ApiMessage(json, "Não foi possível carregar os dados da empresa.");
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var company = root.TryGetProperty("kpis", out var kpi) ? kpi : default;

        KpiDrivers.Text = NumberText(company, "drivers");
        KpiTrucks.Text = NumberText(company, "trucks");
        KpiTrips.Text = NumberText(company, "tripsToday");
        KpiResult.Text = MoneyText(company, "result");

        var drivers = root.TryGetProperty("drivers", out var driverList) ? driverList : default;
        var trucks = root.TryGetProperty("trucks", out var truckList) ? truckList : default;
        var trips = root.TryGetProperty("trips", out var tripList) ? tripList : default;

        DriversText.Text = BuildDrivers(driverList);
        FleetText.Text = BuildFleet(truckList);
        OperationsText.Text = BuildTrips(tripList);

        var revenue = MoneyValue(company, "revenue");
        var expenses = MoneyValue(company, "expenses");
        FinancialText.Text = $"Receita real registrada: R$ {revenue:N2}   •   Despesas reais: R$ {expenses:N2}   •   Resultado: R$ {revenue - expenses:N2}";

        HeaderCompanyText.Text = "Dados reais da empresa • Central administrativa";
        LoginView.Visibility = Visibility.Collapsed;
        SetupView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_directorToken))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + "/director/logout");
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _directorToken);
                await _http.SendAsync(request);
            }
        }
        catch { }
        _directorToken = null;
        DashboardView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        StatusText.Text = "Sessão encerrada.";
    }

    private static string BuildDrivers(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) return "Nenhum motorista vinculado.";
        var sb = new StringBuilder();
        var i = 0;
        foreach (var d in value.EnumerateArray())
        {
            if (i++ >= 8) { sb.AppendLine("…"); break; }
            sb.AppendLine($"• {JsonString(d, "name", "Motorista")}  —  {JsonNumber(d, "trips")} viagens  •  {JsonNumber(d, "km"):N1} km");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildFleet(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) return "Nenhum caminhão cadastrado.";
        var sb = new StringBuilder();
        var i = 0;
        foreach (var t in value.EnumerateArray())
        {
            if (i++ >= 8) { sb.AppendLine("…"); break; }
            var truck = $"{JsonString(t, "brand", "")} {JsonString(t, "model", "")}".Trim();
            sb.AppendLine($"• {truck}  —  {JsonString(t, "driver", "Sem motorista")}  •  {JsonNumber(t, "km"):N1} km");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildTrips(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) return "Nenhuma viagem registrada.";
        var sb = new StringBuilder();
        var i = 0;
        foreach (var t in value.EnumerateArray())
        {
            if (i++ >= 6) { sb.AppendLine("…"); break; }
            sb.AppendLine($"• {JsonString(t, "cargo", "Carga")}  •  {JsonString(t, "origin", "?")} → {JsonString(t, "destination", "?")}  •  {JsonString(t, "driver", "Motorista")}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string NumberText(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) ? p.ToString() : "—";

    private static double MoneyValue(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.TryGetDouble(out var n) ? n : 0;

    private static string MoneyText(JsonElement value, string property)
        => $"R$ {MoneyValue(value, property):N2}";

    private static string JsonString(JsonElement value, string property, string fallback)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() ?? fallback : fallback;

    private static double JsonNumber(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.TryGetDouble(out var n) ? n : 0;

    private static string JsonProperty(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    private static string ApiMessage(string json, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var p) ? p.GetString() ?? fallback : fallback;
        }
        catch { return fallback; }
    }

    private async Task<(bool ok, string json)> PostAsync(string path, object payload)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(ApiBaseUrl + path, content);
        return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static bool IsEmail(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^\S+@\S+\.\S+$");

    private static void SetBusy(System.Windows.Controls.Button button, string content)
    {
        button.IsEnabled = false;
        button.Content = content;
    }
}
