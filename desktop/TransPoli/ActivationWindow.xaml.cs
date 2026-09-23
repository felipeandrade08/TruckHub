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
    private bool _openingMainWindow;
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private enum FormMode { Login, CreateAccount, RecoverPin, RecoverComputer }
    private FormMode _mode = FormMode.Login;
    private string _pendingPinEmail = "";
    private string _pendingPin = "";
    private bool _pendingPinActivatesDesktop;
    private string _authenticatedRole = "driver";
    private bool _authenticatedDirector;

    public ActivationWindow()
    {
        InitializeComponent();
        VersionText.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"} • TransPoli";
        Loaded += async (_, _) => await RestoreOrRequireActivation();
    }

    private async Task RestoreOrRequireActivation()
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            SetStatus("Entre com seu e-mail e senha. A licença deste computador será verificada depois.", false);
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
        var email=EmailBox.Text.Trim();var password=PinBox.Password;
        if(!IsEmail(email)||password.Length<8){SetStatus("Informe seu e-mail e sua senha.",true);return;}
        SetBusy(ActivateButton,"AUTENTICANDO...");
        try
        {
            var(ok,json)=await PostJsonAsync("/auth/login",new{email,password});
            if(!ok){SetStatus(ApiMessage(json,"Não foi possível entrar na conta TransPoli."),true);return;}
            using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
            var role="";
            var isDirector=false;
            if(root.TryGetProperty("access",out var access)&&access.ValueKind==JsonValueKind.Object)
            {
                role=access.TryGetProperty("role",out var r)?r.GetString()??"":"";
                isDirector=access.TryGetProperty("isDirector",out var d)&&d.ValueKind==JsonValueKind.True;
                _authenticatedRole=string.IsNullOrWhiteSpace(role)?"driver":role;
                _authenticatedDirector=isDirector||role=="director";
            }
            SetStatus("Conta autenticada. Verificando ativação deste computador...",false);
            var activated=await ActivateAccountDeviceAsync(email,password);
            if(!activated)return;
            if(isDirector||role=="director"||role=="manager")
            {
                SetStatus("Acesso empresarial identificado. Abrindo ambiente TransPoli...",false);
            }
            OpenAuthorizedWorkspace();
        }
        catch(HttpRequestException){SetStatus("Não foi possível conectar ao servidor. Verifique a internet.",true);}
        catch(TaskCanceledException){SetStatus("A conexão demorou demais. Tente novamente.",true);}
        catch{SetStatus("Erro ao entrar no TransPoli.",true);}
        finally{ActivateButton.IsEnabled=true;ActivateButton.Content="ENTRAR NA CONTA  ›";}
    }

    private async Task<bool> ActivateAccountDeviceAsync(string email,string password)
    {
        var payload=new{email,password,deviceId=DeviceIdentity.GetOrCreate(),deviceName=Environment.MachineName};
        var(ok,json)=await PostJsonAsync("/auth/device/recover",payload);
        if(!ok){SetStatus(ApiMessage(json,"Conta válida, mas este computador precisa ser ativado ou recuperado."),true);return false;}
        var token=JsonProperty(json,"accessToken");
        if(string.IsNullOrWhiteSpace(token)){SetStatus("A ativação não retornou uma sessão válida para o computador.",true);return false;}
        SecureTokenStore.Save(token);return true;
    }

    private void OpenAuthorizedWorkspace()
    {
        if(_authenticatedDirector||_authenticatedRole=="director"||_authenticatedRole=="manager")
        {
            try
            {
                _openingMainWindow=true;
                var director=new DirectorCenterWindow{WindowStartupLocation=WindowStartupLocation.CenterScreen,ShowInTaskbar=true};
                Application.Current.MainWindow=director;director.Show();Close();return;
            }
            catch(Exception ex)
            {
                App.WriteUiCrashLog("ActivationWindow.OpenAuthorizedWorkspace",ex);
                _openingMainWindow=false;
                SetStatus("Conta autenticada, mas não foi possível abrir o ambiente empresarial.",true);
                return;
            }
        }
        OpenTransPoli();
    }

    private async void DirectorAccess_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Task.Yield();

            var director = new DirectorCenterWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = true
            };

            director.Closed += (_, _) =>
            {
                if (!IsVisible)
                {
                    Show();
                    Activate();
                }
            };

            Hide();
            director.Show();
            director.Activate();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("ActivationWindow.DirectorAccess", ex);
            Show();
            Activate();
            MessageBox.Show(
                "Não foi possível abrir a Central da Diretoria.\n\n" +
                ex.GetType().Name + ": " + ex.Message,
                "TransPoli • Central da Diretoria",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    private async void UpdateApp_Click(object sender, RoutedEventArgs e)
    {
        await UpdateFromLoginAsync();
    }

    private async Task UpdateFromLoginAsync()
    {
        try
        {
            SetStatus("Procurando a versão mais recente do TransPoli...", false);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                "https://github.com/felipeandrade08/TruckHub/releases/latest/download/manifest.json?t=" +
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            request.Headers.UserAgent.ParseAdd("TransPoli-Updater");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                SetStatus("Não foi possível consultar a atualização. Tente novamente em alguns segundos.", true);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var remoteVersion = root.TryGetProperty("version", out var v) ? v.GetString() : null;
            var downloadUrl = root.TryGetProperty("downloadUrl", out var u) ? u.GetString() : null;
            var checksum = root.TryGetProperty("checksumSha256", out var h) ? h.GetString() : null;
            var changelog = root.TryGetProperty("changelog", out var c) ? c.GetString() : null;
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            if (string.IsNullOrWhiteSpace(remoteVersion) || string.IsNullOrWhiteSpace(downloadUrl) ||
                !Uri.TryCreate(downloadUrl, UriKind.Absolute, out var downloadUri) ||
                downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                SetStatus("O manifesto de atualização é inválido.", true);
                return;
            }

            if (!Version.TryParse(remoteVersion.TrimStart('v','V'), out var remote) ||
                !Version.TryParse(currentVersion, out var current))
            {
                SetStatus("Não foi possível comparar as versões.", true);
                return;
            }

            if (remote <= current)
            {
                SetStatus($"TransPoli {currentVersion} já está atualizado.", false);
                return;
            }

            var details = string.IsNullOrWhiteSpace(changelog) ? "" : $"\n\nNovidades:\n{changelog}";
            var answer = MessageBox.Show(
                $"Nova versão disponível: {remoteVersion}\n\nVersão instalada: {currentVersion}{details}\n\nDeseja baixar e instalar agora?",
                "TransPoli • Atualização",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes) return;

            var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TransPoli-update");
            System.IO.Directory.CreateDirectory(tempRoot);
            var setupPath = System.IO.Path.Combine(tempRoot, "TransPoli-Setup.exe");
            try { if (System.IO.File.Exists(setupPath)) System.IO.File.Delete(setupPath); } catch { }

            SetStatus($"Baixando o TransPoli {remoteVersion}...", false);
            using var downloadResponse = await _http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
            downloadResponse.EnsureSuccessStatusCode();
            var total = downloadResponse.Content.Headers.ContentLength ?? -1L;
            if (total > 500L * 1024 * 1024) throw new InvalidOperationException("O pacote de atualização é maior que o limite permitido.");

            await using (var source = await downloadResponse.Content.ReadAsStreamAsync())
            await using (var target = new System.IO.FileStream(setupPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    received += read;
                    if (received > 500L * 1024 * 1024) throw new InvalidOperationException("O pacote de atualização é maior que o limite permitido.");
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    if (total > 0) SetStatus($"Baixando o TransPoli {remoteVersion}... {(int)(received * 100 / total)}%", false);
                }
            }

            if (!string.IsNullOrWhiteSpace(checksum))
            {
                await using var stream = System.IO.File.OpenRead(setupPath);
                using var sha = System.Security.Cryptography.SHA256.Create();
                var actual = Convert.ToHexString(await sha.ComputeHashAsync(stream));
                if (!string.Equals(actual, checksum.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { System.IO.File.Delete(setupPath); } catch { }
                    throw new InvalidOperationException("O pacote baixado não confere com o SHA-256 publicado.");
                }
            }

            SetStatus("Instalando a atualização... o TransPoli será reaberto.", false);
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NOCANCEL /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true,
                WorkingDirectory = tempRoot
            };
            System.Diagnostics.Process.Start(startInfo);
            await Task.Delay(1000);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetStatus("Falha ao atualizar: " + ex.Message, true);
        }
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
        PinRevealPanel.Visibility = Visibility.Collapsed;
        PinCopyStatus.Text = "";
        if (login)
        {
            TitleText.Text = "ENTRAR NO TRANSPOLI";
            SubtitleText.Text = "Entre com e-mail e senha. A licença deste computador é verificada separadamente.";
            ActivateButton.Content = "ENTRAR NA CONTA  ›";
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
                    SubtitleText.Text = "Crie sua conta TransPoli. Depois validaremos a ativação deste computador.";
                    FormActionButton.Content = "CRIAR CONTA  ›";
                    break;
                case FormMode.RecoverPin:
                    TitleText.Text = "RECUPERAR PIN";
                    SubtitleText.Text = "O PIN é uma credencial secundária de segurança e recuperação. O login normal continua sendo e-mail + senha.";
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
            _pendingPinEmail = email;
            _pendingPin = pin;
            _pendingPinActivatesDesktop = true;
            ShowPinReveal("PIN DE SEGURANÇA — ANOTE AGORA", "Sua conta usa e-mail + senha. Este PIN é apenas uma credencial secundária para segurança e recuperação.");
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
            _pendingPinEmail = email;
            _pendingPin = pin;
            _pendingPinActivatesDesktop = false;
            ShowPinReveal("NOVO PIN DE SEGURANÇA", "O login continua sendo e-mail + senha. Guarde este PIN somente para segurança e recuperação.");
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
            if(!ok){SetFormStatus(ApiMessage(json,"Não foi possível recuperar este computador. Confira e-mail e senha e tente novamente."),true);return;}
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
    private void ShowPinReveal(string title, string message)
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Collapsed;
        PinRevealPanel.Visibility = Visibility.Visible;
        TitleText.Text = title;
        SubtitleText.Text = message;
        PinRevealText.Text = _pendingPin;
        PinCopyStatus.Text = "";
        PinContinueButton.IsEnabled = true;
        PinContinueButton.Content = _pendingPinActivatesDesktop ? "ATIVAR E ENTRAR  ›" : "VOLTAR AO LOGIN  ›";
    }

    private void CopyPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingPin))
        {
            PinCopyStatus.Text = "Nenhum PIN disponível.";
            return;
        }

        try
        {
            Clipboard.SetText(_pendingPin);
            PinCopyStatus.Text = "PIN copiado para a área de transferência.";
        }
        catch
        {
            PinCopyStatus.Text = "Não foi possível copiar. Anote o PIN acima.";
        }
    }

    private async void PinContinueButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingPin))
        {
            ShowMode(FormMode.Login);
            return;
        }

        if (!_pendingPinActivatesDesktop)
        {
            _pendingPin = "";
            _pendingPinEmail = "";
            ShowMode(FormMode.Login);
            return;
        }

        SetBusy(PinContinueButton, "ATIVANDO...");
        CopyPinButton.IsEnabled = false;
        PinCopyStatus.Text = "Ativando este computador...";

        try
        {
            var activated = await ActivateWithPinAsync(_pendingPinEmail, _pendingPin);
            if (!activated)
            {
                PinContinueButton.IsEnabled = true;
                PinContinueButton.Content = "ATIVAR E ENTRAR  ›";
                CopyPinButton.IsEnabled = true;
                PinCopyStatus.Text = "O PIN continua visível. Corrija o problema e tente novamente.";
                return;
            }

            _pendingPin = "";
            _pendingPinEmail = "";
            _pendingPinActivatesDesktop = false;
            OpenTransPoli();
        }
        catch (Exception ex)
        {
            PinContinueButton.IsEnabled = true;
            PinContinueButton.Content = "ATIVAR E ENTRAR  ›";
            CopyPinButton.IsEnabled = true;
            PinCopyStatus.Text = "Não foi possível ativar: " + ex.Message;
        }
    }

    private void SetBusy(System.Windows.Controls.Button button,string text){button.IsEnabled=false;button.Content=text;}
    private void SetStatus(string message,bool error){StatusText.Text=message;StatusText.Foreground=FindResource(error?"Orange":"Green") as Brush;}
    private void SetFormStatus(string message,bool error){FormStatusText.Text=message;FormStatusText.Foreground=FindResource(error?"Orange":"Green") as Brush;}
    private void OpenTransPoli(){try{_openingMainWindow=true;var main=new MainWindow();Application.Current.MainWindow=main;main.Show();Close();}catch(Exception ex){MessageBox.Show($"Não foi possível abrir o TransPoli.\n\n{ex.Message}","TransPoli — erro",MessageBoxButton.OK,MessageBoxImage.Error);Application.Current.Shutdown();}}
    protected override void OnClosed(EventArgs e){_http.Dispose();base.OnClosed(e);if(!_openingMainWindow) Application.Current?.Shutdown();}
}