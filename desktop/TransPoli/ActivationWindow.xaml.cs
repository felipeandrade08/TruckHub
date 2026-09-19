using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace TransPoli;

public partial class ActivationWindow : Window
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private enum FormMode { Login, CreateAccount, RecoverPin, RecoverComputer }
    private FormMode _mode = FormMode.Login;

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
            SetStatus("Entre com seu e-mail e PIN. Se ainda não tem conta, crie uma aqui.", false);
            EmailBox.Focus();
            return;
        }

        SetStatus("Sessão encontrada. Verificando este computador...", false);
        var validation = await ValidateSession(token);
        if (validation == SessionValidation.Valid) { OpenTransPoli(); return; }
        if (validation == SessionValidation.NetworkError)
        {
            SetStatus("Servidor temporariamente indisponível. Tentando abrir a sessão salva...", false);
            OpenTransPoli();
            return;
        }

        SecureTokenStore.Delete();
        SetStatus("A sessão deste computador precisa ser recuperada. Você pode recuperar o computador usando sua senha.", true);
        EmailBox.Focus();
    }

    private async Task<SessionValidation> ValidateSession(string token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/device/heartbeat");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Content = new StringContent(JsonSerializer.Serialize(new { deviceId = DeviceIdentity.GetOrCreate() }), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode) return SessionValidation.Valid;
            if ((int)response.StatusCode >= 500) return SessionValidation.NetworkError;
            return SessionValidation.Invalid;
        }
        catch (HttpRequestException) { return SessionValidation.NetworkError; }
        catch (TaskCanceledException) { return SessionValidation.NetworkError; }
        catch { return SessionValidation.NetworkError; }
    }

    private enum SessionValidation { Valid, Invalid, NetworkError }

    private async void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        await LoginAsync();
    }

    private async Task LoginAsync()
    {
        var email = EmailBox.Text.Trim();
        var pin = PinBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(email) || pin.Length != 6) { SetStatus("Informe um e-mail válido e o PIN de 6 dígitos.", true); return; }

        SetBusy(ActivateButton, "ENTRANDO...");
        try
        {
            var payload = new { email, pin, deviceId = DeviceIdentity.GetOrCreate(), deviceName = Environment.MachineName };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{ApiBaseUrl}/auth/activate", content);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { SetStatus(ApiMessage(json, "Não foi possível entrar no TransPoli."), true); return; }
            var token = JsonProperty(json, "accessToken");
            if (string.IsNullOrWhiteSpace(token)) { SetStatus("A API não retornou uma sessão válida.", true); return; }
            SecureTokenStore.Save(token);
            SetStatus("Login realizado. Abrindo o cockpit...", false);
            OpenTransPoli();
        }
        catch (HttpRequestException) { SetStatus("Não foi possível conectar ao servidor. Verifique a internet.", true); }
        catch (TaskCanceledException) { SetStatus("A conexão demorou demais. Tente novamente.", true); }
        catch { SetStatus("Erro ao entrar no TransPoli.", true); }
        finally { ActivateButton.IsEnabled = true; ActivateButton.Content = "ENTRAR NO COCKPIT  ›"; }
    }

    private void CreateAccount_Click(object sender, RoutedEventArgs e) => ShowMode(FormMode.CreateAccount);
    private void RecoverPin_Click(object sender, RoutedEventArgs e) => ShowMode(FormMode.RecoverPin);
    private void RecoverComputer_Click(object sender, RoutedEventArgs e) => ShowMode(FormMode.RecoverComputer);
    private void BackToLogin_Click(object sender, RoutedEventArgs e) => ShowMode(FormMode.Login);

    private void ShowMode(FormMode mode)
    {
        _mode = mode;
        var login = mode == FormMode.Login;
        LoginPanel.Visibility = login ? Visibility.Visible : Visibility.Collapsed;
        FormPanel.Visibility = login ? Visibility.Collapsed : Visibility.Visible;
        if (login)
        {
            TitleText.Text = "ENTRAR NO TRANSPOLI";
            SubtitleText.Text = "Use seu e-mail e o PIN de 6 dígitos para entrar neste computador.";
            ActivateButton.Content = "ENTRAR NO COCKPIT  ›";
            SetStatus("Pronto para entrar.", false);
        }
        else
        {
            NameBox.Visibility = mode == FormMode.CreateAccount ? Visibility.Visible : Visibility.Collapsed;
            NameLabel.Visibility = mode == FormMode.CreateAccount ? Visibility.Visible : Visibility.Collapsed;
            FormEmailBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = mode == FormMode.CreateAccount ? Visibility.Visible : Visibility.Collapsed;
            ConfirmLabel.Visibility = mode == FormMode.CreateAccount ? Visibility.Visible : Visibility.Collapsed;
            switch (mode)
            {
                case FormMode.CreateAccount:
                    TitleText.Text = "CRIAR SUA CONTA";
                    SubtitleText.Text = "Cadastre-se direto no aplicativo. Ao concluir, o TransPoli gera seu PIN de acesso.";
                    FormActionButton.Content = "CRIAR CONTA E ATIVAR  ›";
                    break;
                case FormMode.RecoverPin:
                    TitleText.Text = "RECUPERAR PIN";
                    SubtitleText.Text = "Informe e-mail e senha. Um novo PIN de 6 dígitos será gerado para este aplicativo.";
                    FormActionButton.Content = "GERAR NOVO PIN  ›";
                    break;
                case FormMode.RecoverComputer:
                    TitleText.Text = "RECUPERAR COMPUTADOR";
                    SubtitleText.Text = "Use sua conta para liberar o vínculo antigo e ativar este computador.";
                    FormActionButton.Content = "RECUPERAR E ATIVAR  ›";
                    break;
            }
            FormStatusText.Text = "";
            FormEmailBox.Focus();
        }
    }

    private async void FormActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == FormMode.CreateAccount) await CreateAccountAsync();
        else if (_mode == FormMode.RecoverPin) await RecoverPinAsync();
        else if (_mode == FormMode.RecoverComputer) await RecoverComputerAsync();
    }

    private async Task CreateAccountAsync()
    {
        var name=NameBox.Text.Trim(); var email=FormEmailBox.Text.Trim(); var password=PasswordBox.Password; var confirm=ConfirmPasswordBox.Password;
        if(name.Length<2){SetFormStatus("Informe seu nome.",true);return;}
        if(!IsEmail(email)){SetFormStatus("Informe um e-mail válido.",true);return;}
        if(password.Length<8){SetFormStatus("A senha precisa ter pelo menos 8 caracteres.",true);return;}
        if(password!=confirm){SetFormStatus("As senhas não conferem.",true);return;}
        SetBusy(FormActionButton,"CRIANDO...");
        try
        {
            var payload=new {name,email,password};
            var (ok,json)=await PostJsonAsync("/auth/register",payload);
            if(!ok){SetFormStatus(ApiMessage(json,"Não foi possível criar a conta."),true);return;}
            var pin=JsonProperty(json,"pin");
            if(string.IsNullOrWhiteSpace(pin)){SetFormStatus("Conta criada, mas o servidor não retornou o PIN.",true);return;}
            SetFormStatus($"Conta criada com sucesso. Seu PIN é: {pin}\nGuarde esse PIN. Ele será usado para entrar no TransPoli.",false);
            await Task.Delay(1200);
            var activated=await ActivateWithPinAsync(email,pin);
            if(activated) OpenTransPoli();
        }
        catch(HttpRequestException){SetFormStatus("Não foi possível conectar ao servidor.",true);}
        catch(TaskCanceledException){SetFormStatus("A conexão demorou demais. Tente novamente.",true);}
        catch{SetFormStatus("Erro ao criar a conta.",true);}
        finally{FormActionButton.IsEnabled=true;FormActionButton.Content="CRIAR CONTA E ATIVAR  ›";}
    }

    private async Task<bool> ActivateWithPinAsync(string email,string pin)
    {
        var payload=new {email,pin,deviceId=DeviceIdentity.GetOrCreate(),deviceName=Environment.MachineName};
        var (ok,json)=await PostJsonAsync("/auth/activate",payload);
        if(!ok){SetFormStatus(ApiMessage(json,"Conta criada, mas não foi possível ativar este computador."),true);return false;}
        var token=JsonProperty(json,"accessToken");
        if(string.IsNullOrWhiteSpace(token)){SetFormStatus("Conta criada, mas a sessão não foi retornada.",true);return false;}
        SecureTokenStore.Save(token);
        return true;
    }

    private async Task RecoverPinAsync()
    {
        var email=FormEmailBox.Text.Trim(); var password=PasswordBox.Password;
        if(!IsEmail(email)||password.Length<8){SetFormStatus("Informe e-mail e senha corretamente.",true);return;}
        SetBusy(FormActionButton,"RECUPERANDO...");
        try
        {
            var (ok,json)=await PostJsonAsync("/auth/pin/recover",new {email,password});
            if(!ok){SetFormStatus(ApiMessage(json,"Não foi possível recuperar o PIN."),true);return;}
            var pin=JsonProperty(json,"pin");
            SetFormStatus($"Novo PIN: {pin}\nGuarde-o em local seguro. Depois volte ao login para entrar.",false);
        }
        catch{SetFormStatus("Erro ao recuperar o PIN.",true);}
        finally{FormActionButton.IsEnabled=true;FormActionButton.Content="GERAR NOVO PIN  ›";}
    }

    private async Task RecoverComputerAsync()
    {
        var email=FormEmailBox.Text.Trim(); var password=PasswordBox.Password;
        if(!IsEmail(email)||password.Length<8){SetFormStatus("Informe e-mail e senha corretamente.",true);return;}
        SetBusy(FormActionButton,"RECUPERANDO...");
        try
        {
            var payload=new {email,password,deviceId=DeviceIdentity.GetOrCreate(),deviceName=Environment.MachineName};
            var (ok,json)=await PostJsonAsync("/auth/device/recover",payload);
            if(!ok){SetFormStatus(ApiMessage(json,"Não foi possível recuperar este computador."),true);return;}
            var token=JsonProperty(json,"accessToken");
            if(string.IsNullOrWhiteSpace(token)){SetFormStatus("O servidor não retornou uma sessão válida.",true);return;}
            SecureTokenStore.Save(token);
            SetFormStatus("Computador recuperado. Abrindo o cockpit...",false);
            await Task.Delay(500);
            OpenTransPoli();
        }
        catch{SetFormStatus("Erro ao recuperar o computador.",true);}
        finally{FormActionButton.IsEnabled=true;FormActionButton.Content="RECUPERAR E ATIVAR  ›";}
    }

    private async Task<(bool ok,string json)> PostJsonAsync(string path,object payload)
    {
        using var content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        using var response=await _http.PostAsync(ApiBaseUrl+path,content);
        return(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
    }

    private static bool IsEmail(string value)=>System.Text.RegularExpressions.Regex.IsMatch(value,@"^\S+@\S+\.\S+$");
    private static string JsonProperty(string json,string name){try{using var doc=JsonDocument.Parse(json);return doc.RootElement.TryGetProperty(name,out var p)?p.GetString()??"":"";}catch{return "";}}
    private static string ApiMessage(string json,string fallback){try{using var doc=JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("error",out var e)?e.GetString()??fallback:fallback;}catch{return fallback;}}
    private void SetBusy(System.Windows.Controls.Button button,string text){button.IsEnabled=false;button.Content=text;}
    private void SetStatus(string message,bool error){StatusText.Text=message;StatusText.Foreground=FindResource(error?"Orange":"Green") as Brush;}
    private void SetFormStatus(string message,bool error){FormStatusText.Text=message;FormStatusText.Foreground=FindResource(error?"Orange":"Green") as Brush;}
    private void OpenTransPoli(){try{var main=new MainWindow();Application.Current.MainWindow=main;main.Show();Close();}catch(Exception ex){MessageBox.Show($"Não foi possível abrir o TransPoli.\n\n{ex.Message}","TransPoli — erro",MessageBoxButton.OK,MessageBoxImage.Error);Application.Current.Shutdown();}}
    protected override void OnClosed(EventArgs e){_http.Dispose();base.OnClosed(e);}
}