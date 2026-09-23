using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TransPoli;

public partial class DirectorCenterWindow : Window
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private DateTime _lastDashboardRefreshUtc = DateTime.MinValue;
    private bool _dashboardRefreshInFlight;
    private JsonElement _cachedDashboardRoot;
    private string? _directorToken;

    public DirectorCenterWindow()
    {
        InitializeComponent();
        Loaded += DirectorCenterWindow_Loaded;
    }

    private async void DirectorCenterWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        try
        {
            Loaded -= DirectorCenterWindow_Loaded;
            DirectorEmailBox?.Focus();
            await RefreshSetupAvailabilityAsync();
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("DirectorCenterWindow.Loaded", ex);
            if (StatusText != null) StatusText.Text = "Central carregada. O status inicial não pôde ser consultado.";
        }
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

            try
            {
                await LoadDashboardAsync(force:true);
            }
            catch (HttpRequestException)
            {
                App.WriteUiCrashLog("DirectorCenterWindow.DashboardNetwork", new Exception("Falha de rede ao carregar o dashboard."));
                DashboardView.Visibility = Visibility.Visible;
                LoginView.Visibility = Visibility.Collapsed;
                SetupView.Visibility = Visibility.Collapsed;
                ShowSection(OverviewPanel, "VISÃO GERAL", "Central da Diretoria");
                LastUpdateText.Text = "Falha de rede ao carregar os dados.";
                StatusText.Text = "Login da diretoria realizado.";
            }
            catch (TaskCanceledException)
            {
                App.WriteUiCrashLog("DirectorCenterWindow.DashboardTimeout", new Exception("Tempo esgotado ao carregar o dashboard."));
                DashboardView.Visibility = Visibility.Visible;
                LoginView.Visibility = Visibility.Collapsed;
                SetupView.Visibility = Visibility.Collapsed;
                ShowSection(OverviewPanel, "VISÃO GERAL", "Central da Diretoria");
                LastUpdateText.Text = "A carga dos dados demorou mais que o esperado.";
                StatusText.Text = "Login da diretoria realizado.";
            }
            catch (Exception ex)
            {
                App.WriteUiCrashLog("DirectorCenterWindow.Dashboard", ex);
                DashboardView.Visibility = Visibility.Visible;
                LoginView.Visibility = Visibility.Collapsed;
                SetupView.Visibility = Visibility.Collapsed;
                ShowSection(OverviewPanel, "VISÃO GERAL", "Central da Diretoria");
                LastUpdateText.Text = $"Falha no painel: {ex.GetType().Name}";
                StatusText.Text = "Login da diretoria realizado, mas houve uma falha ao montar o painel.";
            }
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
            StatusText.Text = $"Falha no acesso da diretoria: {ex.GetType().Name} — {ex.Message}";
        }
        finally
        {
            LoginButton.IsEnabled = true;
            LoginButton.Content = "ENTRAR NA CENTRAL  ›";
        }
    }

    private async Task RefreshSetupAvailabilityAsync()
    {
        try
        {
            var (ok, json) = await GetAsync("/director/status");
            var configured = ok && JsonBool(json, "configured");
            FirstAccessButton.IsEnabled = !configured;
            if (configured)
            {
                FirstAccessButton.Content = "PRIMEIRO ACESSO BLOQUEADO • TRANSPOLI JÁ CONFIGURADA";
                FirstAccessButton.ToolTip = "A Central da Diretoria da TransPoli já foi configurada.";
            }
        }
        catch
        {
            FirstAccessButton.IsEnabled = true;
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        var ownerEmail = OwnerEmailBox.Text.Trim();
        var ownerPin = OwnerPinBox.Password.Trim();
        const string companyName = "TransPoli";
        var directorEmail = SetupDirectorEmailBox.Text.Trim();
        var directorPin = SetupDirectorPinBox.Password.Trim();

        if (!IsEmail(ownerEmail) || ownerPin.Length != 6)
        {
            SetupStatusText.Text = "Confirme o e-mail e o PIN da conta proprietária.";
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

    private async Task LoadDashboardAsync(bool force = false)
    {
        if (_dashboardRefreshInFlight) return;
        if (!force && DateTime.UtcNow - _lastDashboardRefreshUtc < TimeSpan.FromSeconds(20)) return;
        _dashboardRefreshInFlight = true;
        try
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
        _cachedDashboardRoot = root.Clone();
        var company = root.TryGetProperty("kpis", out var kpi) ? kpi : default;

        KpiDrivers.Text = $"{NumberText(company, "driversOnline")} / {NumberText(company, "drivers")}";
        KpiTrucks.Text = NumberText(company, "trucks");
        KpiActiveTrips.Text = NumberText(company, "activeTrips");
        KpiCompleted.Text = NumberText(company, "completedToday");
        KpiKmToday.Text = $"{MoneyNumber(company, "kmToday"):N1} km";
        KpiRevenueToday.Text = MoneyText(company, "revenueToday");
        KpiExpensesToday.Text = MoneyText(company, "expensesToday");
        KpiResult.Text = MoneyText(company, "resultToday");

        var drivers = root.TryGetProperty("drivers", out var driverList) ? driverList : default;
        var trucks = root.TryGetProperty("trucks", out var truckList) ? truckList : default;
        var trips = root.TryGetProperty("trips", out var tripList) ? tripList : default;

        DriversText.Text = BuildDrivers(driverList);
        SetGrid(DriversGrid, driverList, new[]
        {
            ("ID","id"),("Nome","name"),("Presença","presence"),("Operação","operation_status"),
            ("Caminhão","live_truck"),("Carga","live_cargo"),("Origem","live_origin"),("Destino","live_destination"),("Velocidade","live_speed_kph"),
            ("Modalidade","employment_type"),("Matrícula","registration_number"),("Vínculo","membership_status"),("Licença","license_status"),("Viagens","trips"),("KM","km")
        });
        SetGrid(TrucksGrid, truckList, new[]
        {
            ("ID","id"),("UserID","user_id"),("Caminhão","truck_name"),("Marca","brand"),
            ("Modelo","model"),("Placa","license_plate"),("Motorista","driver"),("Situação","operational_state"),("Alerta","fleet_alert"),("Combustível","current_fuel_l"),("Desgaste","wear_pct"),("Telemetria","last_telemetry_at"),("KM","km")
        });
        SetGrid(TripsGrid, trips, new[]
        {
            ("ID","id"),("Carga","cargo"),("Origem","origin"),("Destino","destination"),
            ("Motorista","driver"),("Caminhão","truck_name"),("Início","started_at"),("Fim","finished_at"),
            ("KM","distance_km"),("Combustível","fuel_used_l"),("Receita TransPoli","trip_revenue_brl"),("Parte empresa","company_share_brl"),("Motorista líquido","driver_net_brl"),("Status","status")
        });
        UpdateModuleSummaries(driverList, truckList, tripList);
        var expensesList = root.TryGetProperty("expenses", out var expenseList) ? expenseList : default;
        SetGrid(ExpensesGrid, expensesList, new[]
        {
            ("ID","id"),("Tipo","type"),("Valor","amount"),("Data","created_at"),("Motorista","driver"),("Viagem","trip_id")
        });
        var maintenanceList = root.TryGetProperty("maintenance", out var maintenanceListValue) ? maintenanceListValue : default;
        var revenue = MoneyValue(company, "revenue");
        var expenses = MoneyValue(company, "expenses");
        FinancialRevenue.Text = $"R$ {revenue:N2}";
        FinancialExpenses.Text = $"R$ {expenses:N2}";
        FinancialResult.Text = $"R$ {(revenue-expenses):N2}";
        OperationsText.Text = BuildTrips(tripList);
        MaintenanceText.Text = BuildMaintenance(maintenanceList);
        var fleetAlerts = truckList.ValueKind==JsonValueKind.Array ? truckList.EnumerateArray().Where(t=>!string.Equals(JsonString(t,"fleet_alert","NORMAL"),"NORMAL",StringComparison.OrdinalIgnoreCase)).ToList() : new System.Collections.Generic.List<JsonElement>();
        MaintenanceText.Text = fleetAlerts.Count==0 ? "Frota monitorada • nenhum alerta operacional no momento." : string.Join("   •   ",fleetAlerts.Take(4).Select(t=>$"{JsonString(t,"truck_name","Caminhão")}: {JsonString(t,"fleet_alert","ATENÇÃO")}"));
        FleetText.Text = fleetAlerts.Count==0 ? BuildFleet(truckList) : $"{fleetAlerts.Count} ALERTA(S) NA FROTA\n" + string.Join("\n",fleetAlerts.Take(3).Select(t=>$"• {JsonString(t,"truck_name","Caminhão")} — {JsonString(t,"fleet_alert","ATENÇÃO")}"));
        FinancialText.Text = $"Hoje: receita R$ {MoneyValue(company, "revenueToday"):N2}   •   despesas R$ {MoneyValue(company, "expensesToday"):N2}   •   resultado R$ {MoneyValue(company, "resultToday"):N2}";
        if(root.TryGetProperty("companyEconomy",out var companyEconomy)) RenderCompanyEconomy(companyEconomy);

        HeaderCompanyText.Text = "Dados reais da empresa • Central administrativa";
        LastUpdateText.Text = $"Atualizado em {DateTime.Now:dd/MM/yyyy HH:mm}";
        await LoadDirectorIdentityAsync();
        ShowSection(OverviewPanel);
        LoginView.Visibility = Visibility.Collapsed;
        SetupView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
        _lastDashboardRefreshUtc = DateTime.UtcNow;
        }
        finally { _dashboardRefreshInFlight = false; }
    }

    private void RenderCompanyEconomy(JsonElement root)
    {
        CompanyBankBalance.Text=$"R$ {MoneyValue(root,"balance"):N2}";
        if(root.TryGetProperty("recent",out var recent)) SetGrid(CompanyLedgerGrid,recent,new[]{("Tipo","type"),("Valor","amount"),("Motorista","driver_name"),("Descrição","note"),("Data","created_at")});
        if(root.TryGetProperty("loans",out var loans))
        {
            SetGrid(CompanyLoansGrid,loans,new[]{("ID","id"),("Motorista","driver_name"),("Principal","principal"),("Total","total_due"),("Pago","paid_amount"),("Juros","interest_rate"),("Status","status")});
            var pending=loans.ValueKind==JsonValueKind.Array?loans.EnumerateArray().Count(x=>string.Equals(JsonString(x,"status",""),"pending",StringComparison.OrdinalIgnoreCase)):0;
            var active=loans.ValueKind==JsonValueKind.Array?loans.EnumerateArray().Count(x=>string.Equals(JsonString(x,"status",""),"active",StringComparison.OrdinalIgnoreCase)):0;
            CompanyLoanSummary.Text=pending>0?$"{pending} aguardando decisão • {active} crédito(s) ativo(s).":active>0?$"{active} crédito(s) ativo(s) • nenhuma solicitação pendente.":"Nenhuma solicitação pendente.";
        }
    }

    private async Task LoadCompanyEconomyAsync()
    {
        if (string.IsNullOrWhiteSpace(_directorToken)) return;
        var (ok,json)=await GetAsync("/director/company-economy");
        if(!ok) return;
        using var doc=JsonDocument.Parse(json);
        RenderCompanyEconomy(doc.RootElement);
    }

    private async Task DecideCompanyLoanAsync(string decision)
    {
        var row=SelectedRow(CompanyLoansGrid); if(row==null){MessageBox.Show("Selecione uma solicitação de crédito.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var id=row["ID"]?.ToString()??""; if(string.IsNullOrWhiteSpace(id))return;
        var label=decision=="approve"?"aprovar":"rejeitar";
        if(MessageBox.Show($"Deseja {label} este empréstimo?","TransPoli",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var(ok,json)=await PostAsync("/director/company-loans/"+id+"/decision",new{decision});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível analisar o empréstimo."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadCompanyEconomyAsync();
    }
    private async void ApproveCompanyLoan_Click(object sender,RoutedEventArgs e)=>await DecideCompanyLoanAsync("approve");
    private async void RejectCompanyLoan_Click(object sender,RoutedEventArgs e)=>await DecideCompanyLoanAsync("reject");

    private DataRowView? SelectedRow(System.Windows.Controls.DataGrid grid) => grid.SelectedItem as DataRowView;

    private async void ToggleDriver_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(DriversGrid);
        if(row==null){MessageBox.Show("Selecione um motorista.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var id=row["ID"]?.ToString()??""; var current=row["Status"]?.ToString()??"active";
        var next=current=="blocked"?"active":"blocked";
        if(MessageBox.Show(next=="blocked"?"Bloquear este motorista?":"Reativar este motorista?","TransPoli",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var(ok,json)=await PatchAsync("/director/drivers/"+id+"/status",new{status=next});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível alterar a situação."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(DriversPanel,"MOTORISTAS","Gestão de Motoristas");
    }

    private async void NewDriver_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new DirectorDriverEditorWindow(null,null,"active",false){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        var(ok,json)=await PostAsync("/director/drivers",new{name=dialog.DriverName,email=dialog.Email,password=dialog.Password,pin=dialog.Pin});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível cadastrar o motorista."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(DriversPanel,"MOTORISTAS","Gestão de Motoristas");
    }

    private async void EditDriver_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(DriversGrid);
        if(row==null){MessageBox.Show("Selecione um motorista.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new DirectorDriverEditorWindow(row["Nome"]?.ToString(),row["E-mail"]?.ToString(),row["Licença"]?.ToString(),true){Owner=this};
        if(dialog.ShowDialog()!=true)return;
        var(ok,json)=await PatchAsync("/director/drivers/"+row["ID"],new{name=dialog.DriverName,email=dialog.Email,password=dialog.Password,pin=dialog.Pin,licenseStatus=dialog.LicenseStatus});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível editar o motorista."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(DriversPanel,"MOTORISTAS","Gestão de Motoristas");
    }

    private async void UnlinkDriver_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(DriversGrid);
        if(row==null){MessageBox.Show("Selecione um motorista.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(MessageBox.Show("Desvincular este motorista da TransPoli? O histórico permanecerá no banco.","TransPoli",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var(ok,json)=await DeleteAsync("/director/drivers/"+row["ID"]+"/link");
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível desvincular o motorista."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(DriversPanel,"MOTORISTAS","Gestão de Motoristas");
    }

    private async void LinkDriver_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(DriversGrid);
        if(row==null){MessageBox.Show("Selecione um motorista.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var(ok,json)=await PostAsync("/director/drivers/"+row["ID"]+"/link",new{});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível vincular o motorista."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(DriversPanel,"MOTORISTAS","Gestão de Motoristas");
    }

    private void DriverHistory_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(DriversGrid);
        if(row==null){MessageBox.Show("Selecione um motorista.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new DirectorDriverHistoryWindow(_directorToken??"",row["ID"]?.ToString()??"",row["Nome"]?.ToString()??"Motorista"){Owner=this};
        dialog.ShowDialog();
    }

    private async void NewTruck_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new DirectorTruckEditorWindow(null,null,null,null,null);
        dialog.Owner=this;
        var drivers=await GetDashboardArrayAsync("drivers");
        dialog.SetDrivers(drivers);
        if(dialog.ShowDialog()!=true)return;
        var (ok,json)=await PostAsync("/director/trucks",new{userId=dialog.SelectedUserId,truckName=dialog.TruckName,brand=dialog.Brand,model=dialog.Model,licensePlate=dialog.LicensePlate});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível cadastrar o caminhão."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(TrucksPanel,"CAMINHÕES","Gestão da Frota");
    }

    private async void EditTruck_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(TrucksGrid);
        if(row==null){MessageBox.Show("Selecione um caminhão.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new DirectorTruckEditorWindow(row["UserID"]?.ToString(),row["Caminhão"]?.ToString(),row["Marca"]?.ToString(),row["Modelo"]?.ToString(),row["Placa"]?.ToString());
        dialog.Owner=this;
        var drivers=await GetDashboardArrayAsync("drivers"); dialog.SetDrivers(drivers);
        if(dialog.ShowDialog()!=true)return;
        var id=row["ID"]?.ToString()??"";
        var (ok,json)=await PatchAsync("/director/trucks/"+id,new{userId=dialog.SelectedUserId,truckName=dialog.TruckName,brand=dialog.Brand,model=dialog.Model,licensePlate=dialog.LicensePlate});
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível editar o caminhão."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(TrucksPanel,"CAMINHÕES","Gestão da Frota");
    }

    private async void TruckHistory_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(TrucksGrid);
        if(row==null){MessageBox.Show("Selecione um caminhão.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new DirectorTruckHistoryWindow(_directorToken??"",row["ID"]?.ToString()??"",row["Caminhão"]?.ToString()??"Caminhão"){Owner=this};
        dialog.ShowDialog();
    }

    private void TripDetails_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(TripsGrid);
        if(row==null){MessageBox.Show("Selecione uma viagem.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var dialog=new DirectorTripHistoryWindow(_directorToken??"",row["ID"]?.ToString()??"",row["Carga"]?.ToString()??"Viagem"){Owner=this};
        dialog.ShowDialog();
    }

    private async void DeleteTruck_Click(object sender, RoutedEventArgs e)
    {
        var row=SelectedRow(TrucksGrid);
        if(row==null){MessageBox.Show("Selecione um caminhão.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(MessageBox.Show("Remover este caminhão da frota? As viagens antigas permanecerão registradas.","TransPoli",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        var id=row["ID"]?.ToString()??""; var(ok,json)=await DeleteAsync("/director/trucks/"+id);
        if(!ok)MessageBox.Show(ApiMessage(json,"Não foi possível remover o caminhão."),"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        await LoadDashboardAsync(force:true); ShowSection(TrucksPanel,"CAMINHÕES","Gestão da Frota");
    }

    private async Task<JsonElement> GetDashboardArrayAsync(string name)
    {
        if (_cachedDashboardRoot.ValueKind==JsonValueKind.Object && _cachedDashboardRoot.TryGetProperty(name,out var cached))
            return cached.Clone();

        await LoadDashboardAsync(force:true);
        return _cachedDashboardRoot.ValueKind==JsonValueKind.Object && _cachedDashboardRoot.TryGetProperty(name,out var loaded)
            ? loaded.Clone()
            : default;
    }

    private async Task<(bool ok,string json)> PatchAsync(string path,object payload)
    {
        using var content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
        using var request=new HttpRequestMessage(HttpMethod.Patch,ApiBaseUrl+path){Content=content}; request.Headers.TryAddWithoutValidation("Authorization","Bearer "+_directorToken);
        using var response=await _http.SendAsync(request); return(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
    }
    private async Task<(bool ok,string json)> DeleteAsync(string path)
    {
        using var request=new HttpRequestMessage(HttpMethod.Delete,ApiBaseUrl+path); request.Headers.TryAddWithoutValidation("Authorization","Bearer "+_directorToken);
        using var response=await _http.SendAsync(request); return(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
    }

    private void NavOverview_Click(object sender, RoutedEventArgs e) => ShowSection(OverviewPanel, "VISÃO GERAL", "Central da Diretoria");
    private void NavDrivers_Click(object sender, RoutedEventArgs e) => ShowSection(DriversPanel, "MOTORISTAS", "Gestão de Motoristas");
    private void NavTrucks_Click(object sender, RoutedEventArgs e) => ShowSection(TrucksPanel, "CAMINHÕES", "Gestão da Frota");
    private void NavTrips_Click(object sender, RoutedEventArgs e) => ShowSection(TripsPanel, "VIAGENS", "Operações da TransPoli");
    private void NavFinancial_Click(object sender, RoutedEventArgs e) => ShowSection(FinancialPanel, "FINANCEIRO", "Receitas, despesas e resultado");
    private void NavSettings_Click(object sender, RoutedEventArgs e) => ShowSection(SettingsPanel, "CONFIGURAÇÕES", "Instalação centralizada");

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadDashboardAsync(force: true);
    private void RefreshSettings_Click(object sender, RoutedEventArgs e) => _ = LoadDirectorIdentityAsync();

    private void ShowSection(UIElement panel, string eyebrow = "VISÃO GERAL", string title = "Central da Diretoria")
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        DriversPanel.Visibility = Visibility.Collapsed;
        TrucksPanel.Visibility = Visibility.Collapsed;
        TripsPanel.Visibility = Visibility.Collapsed;
        FinancialPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;
        SectionEyebrow.Text = eyebrow;
        SectionTitle.Text = title;
    }

    private async Task LoadDirectorIdentityAsync()
    {
        if (string.IsNullOrWhiteSpace(_directorToken)) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + "/director/me");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _directorToken);
            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return;
            SettingsDirectorEmail.Text = JsonProperty(json, "email", "director") is { Length: > 0 } email ? email : "—";
        }
        catch { }
    }

    private static void SetGrid(System.Windows.Controls.DataGrid grid, JsonElement value, (string Header, string Property)[] columns)
    {
        if (grid == null) return;

        var table = new DataTable();
        foreach (var col in columns)
            table.Columns.Add(col.Property, typeof(string));

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                var row = table.NewRow();
                for (var i = 0; i < columns.Length; i++)
                {
                    row[i] = item.ValueKind == JsonValueKind.Object &&
                             item.TryGetProperty(columns[i].Property, out var property)
                        ? FormatGridValue(columns[i].Property, property)
                        : "";
                }
                table.Rows.Add(row);
            }
        }

        // Não usamos AutoGenerateColumns aqui. O WPF não precisa modificar a coleção
        // de colunas durante a geração, eliminando a InvalidOperationException da Central.
        grid.AutoGenerateColumns = false;
        grid.Columns.Clear();

        foreach (var col in columns)
        {
            if (col.Property is "id" or "user_id") continue;
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn
            {
                Header = col.Header,
                Binding = new System.Windows.Data.Binding(col.Property)
                {
                    Mode = System.Windows.Data.BindingMode.OneWay
                },
                Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star)
            });
        }

        grid.ItemsSource = table.DefaultView;
    }

    private static string FormatGridValue(string property, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) return "";
        if (property is "status" or "license_status" or "membership_status" or "operational_state" or "presence" or "operation_status" or "employment_type")
        {
            var raw = value.ToString();
            return raw switch
            {
                "active" => "● ATIVO",
                "blocked" => "● BLOQUEADO",
                "finished" => "● CONCLUÍDA",
                "cancelled" => "● CANCELADA",
                "paused" => "● PAUSADO",
                "maintenance" => "● MANUTENÇÃO",
                "offline" => "● OFFLINE",
                "normal" => "● NORMAL",
                "expired" => "● EXPIRADA",
                "unlinked" => "● DESVINCULADO",
                "online" => "● ONLINE",
                "aggregate" => "AGREGADO",
                "company_driver" => "MOTORISTA DA EMPRESA",
                _ => raw.ToUpperInvariant()
            };
        }
        if (property is "cargo_value_brl" or "expenses_brl" or "trip_revenue_brl" or "company_share_brl" or "driver_gross_brl" or "loan_payment_brl" or "driver_net_brl")
            return value.TryGetDouble(out var money) ? $"R$ {money:N2}" : value.ToString();
        if (property is "distance_km" or "km")
            return value.TryGetDouble(out var km) ? $"{km:N1} km" : value.ToString();
        if (property == "live_speed_kph")
            return value.TryGetDouble(out var speed) ? $"{speed:N0} km/h" : value.ToString();
        if (property is "fuel_used_l" or "current_fuel_l")
            return value.TryGetDouble(out var fuel) ? $"{fuel:N1} L" : value.ToString();
        if (property == "wear_pct")
            return value.TryGetDouble(out var wear) ? $"{wear:N0}%" : value.ToString();
        if (property is "started_at" or "finished_at" or "last_telemetry_at" or "last_maintenance_at" or "trial_expires_at" or "expires_at")
        {
            if (DateTime.TryParse(value.ToString(), out var dt))
                return dt.ToLocalTime().ToString("dd/MM HH:mm");
        }
        return value.ToString();
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
        _cachedDashboardRoot = default;
        _lastDashboardRefreshUtc = DateTime.MinValue;
        DashboardView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        StatusText.Text = "Sessão encerrada.";
    }

    private void ApplyGridFilter(System.Windows.Controls.DataGrid? grid, string text, string status = "all")
    {
        if (grid == null || grid.ItemsSource is not DataView view) return;
        var table = view.Table;
        if (table == null) return;
        text = (text ?? "").Trim().Replace("'", "''");
        var parts = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var cols = table.Columns.Cast<DataColumn>().Select(col => $"CONVERT([{col.ColumnName}], 'System.String') LIKE '%{text}%'");
            parts.Add("(" + string.Join(" OR ", cols) + ")");
        }
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
        {
            var statusColumn = table.Columns.Contains("Status") ? "Status" : table.Columns.Contains("Situação") ? "Situação" : null;
            if (statusColumn != null) parts.Add($"LOWER(CONVERT([{statusColumn}], 'System.String')) = '{status.ToLowerInvariant().Replace("'", "''")}'");
        }
        view.RowFilter = string.Join(" AND ", parts);
    }

    private void UpdateModuleSummaries(JsonElement drivers, JsonElement trucks, JsonElement trips)
    {
        var driverItems = drivers.ValueKind == JsonValueKind.Array ? drivers.EnumerateArray().ToList() : new System.Collections.Generic.List<JsonElement>();
        var truckItems = trucks.ValueKind == JsonValueKind.Array ? trucks.EnumerateArray().ToList() : new System.Collections.Generic.List<JsonElement>();
        var tripItems = trips.ValueKind == JsonValueKind.Array ? trips.EnumerateArray().ToList() : new System.Collections.Generic.List<JsonElement>();

        var activeDrivers = driverItems.Count(d => JsonString(d, "membership_status", JsonString(d, "status", "")) == "active" && JsonString(d, "status", "") != "blocked");
        DriverSummaryActive.Text = activeDrivers.ToString();
        DriverSummaryTrips.Text = driverItems.Sum(d => (int)JsonNumber(d, "trips")).ToString();
        DriverSummaryKm.Text = $"{driverItems.Sum(d => JsonNumber(d, "km")):N0} km";
        DriverSummaryLicenses.Text = driverItems.Count(d => !string.Equals(JsonString(d, "license_status", ""), "expired", StringComparison.OrdinalIgnoreCase)).ToString();

        var normal = truckItems.Count(t => JsonString(t, "fleet_alert", "NORMAL") == "NORMAL");
        TruckSummaryNormal.Text = normal.ToString();
        TruckSummaryTrips.Text = tripItems.Count(t => JsonString(t, "status", "") == "active").ToString();
        TruckSummaryTelemetry.Text = truckItems.Count(t => JsonString(t, "fleet_alert", "OFFLINE") != "OFFLINE").ToString();
        var wearValues = truckItems.Select(t => JsonNumber(t, "wear_pct")).Where(v => v > 0).ToList();
        TruckSummaryWear.Text = wearValues.Count == 0 ? "0%" : $"{wearValues.Average():N0}%";

        TripSummaryActive.Text = tripItems.Count(t => JsonString(t, "status", "") == "active").ToString();
        TripSummaryFinished.Text = tripItems.Count(t => JsonString(t, "status", "") == "finished").ToString();
        TripSummaryKm.Text = $"{tripItems.Sum(t => JsonNumber(t, "distance_km")):N0} km";
        TripSummaryResult.Text = $"R$ {tripItems.Sum(t => JsonNumber(t, "company_share_brl")):N2}";
    }

    private void DriverSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyGridFilter(DriversGrid, DriverSearchBox?.Text ?? "", GetSelectedTag(DriverStatusFilter));

    private void TruckSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyGridFilter(TrucksGrid, TruckSearchBox?.Text ?? "", GetSelectedTag(TruckStatusFilter));

    private void TripSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => ApplyGridFilter(TripsGrid, TripSearchBox?.Text ?? "", GetSelectedTag(TripStatusFilter));

    private void DriverStatusFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => ApplyGridFilter(DriversGrid, DriverSearchBox?.Text ?? "", GetSelectedTag(DriverStatusFilter));

    private void TruckStatusFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => ApplyGridFilter(TrucksGrid, TruckSearchBox?.Text ?? "", GetSelectedTag(TruckStatusFilter));

    private void TripStatusFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        => ApplyGridFilter(TripsGrid, TripSearchBox?.Text ?? "", GetSelectedTag(TripStatusFilter));

    private static string GetSelectedTag(System.Windows.Controls.ComboBox? box)
        => (box?.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "all";

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

    private static string BuildMaintenance(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0) return "Nenhuma manutenção registrada.";
        var sb = new StringBuilder();
        var i = 0;
        foreach (var m in value.EnumerateArray())
        {
            if (i++ >= 5) { sb.AppendLine("…"); break; }
            var truck = JsonString(m, "truck_name", "Caminhão");
            var service = JsonString(m, "service_type", "Serviço");
            var driver = JsonString(m, "driver", "Sem motorista");
            sb.AppendLine($"• {truck} — {service} • {driver} • R$ {JsonNumber(m, "cost"):N2}");
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

    private static double MoneyNumber(JsonElement value, string property)
        => MoneyValue(value, property);

    private static string MoneyText(JsonElement value, string property)
        => $"R$ {MoneyValue(value, property):N2}";

    private static string JsonString(JsonElement value, string property, string fallback)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() ?? fallback : fallback;

    private static double JsonNumber(JsonElement value, string property)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var p) && p.TryGetDouble(out var n) ? n : 0;

    private static string JsonProperty(string json, string name, string? parent = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (parent != null && root.TryGetProperty(parent, out var parentValue) && parentValue.ValueKind == JsonValueKind.Object) root = parentValue;
            return root.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    private async Task<(bool ok, string json)> GetAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + path);
        if (!string.IsNullOrWhiteSpace(_directorToken))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _directorToken);
        using var response = await _http.SendAsync(request);
        return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static bool JsonBool(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
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
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUrl + path) { Content = content };
        if (!string.IsNullOrWhiteSpace(_directorToken))
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _directorToken);
        using var response = await _http.SendAsync(request);
        return (response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static bool IsEmail(string value) => System.Text.RegularExpressions.Regex.IsMatch(value, @"^\S+@\S+\.\S+$");

    private static void SetBusy(System.Windows.Controls.Button button, string content)
    {
        button.IsEnabled = false;
        button.Content = content;
    }
}