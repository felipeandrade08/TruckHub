using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private const string MaintenanceApiBaseUrl="https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _maintenanceHttp=new(){Timeout=TimeSpan.FromSeconds(5)};
    private DispatcherTimer? _maintenanceHookTimer;
    private readonly HashSet<Button> _maintenanceButtons=new();

    private void StartMaintenanceNavigationHook()
    {
        _maintenanceHookTimer ??= new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        _maintenanceHookTimer.Tick-=MaintenanceHookTimer_Tick;
        _maintenanceHookTimer.Tick+=MaintenanceHookTimer_Tick;
        _maintenanceHookTimer.Start();
        MaintenanceHookTimer_Tick(null,EventArgs.Empty);
    }

    private void MaintenanceHookTimer_Tick(object? sender,EventArgs e)
    {
        foreach(var button in FindVisualChildren<Button>(this))
        {
            if(_maintenanceButtons.Contains(button))continue;
            if(!(button.Content?.ToString()??"").Contains("MANUT",StringComparison.OrdinalIgnoreCase))continue;
            _maintenanceButtons.Add(button);
            button.Click-=MaintenanceButton_Click;
            button.Click+=MaintenanceButton_Click;
        }
    }

    private async void MaintenanceButton_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        await ShowMaintenanceTabletModalAsync();
    }

    internal async Task ShowMaintenanceTabletModalAsync()
    {
        ShowModalContent("maintenance",BuildModalLoading("🔧 CARREGANDO MANUTENÇÃO..."));
        var data=LastTelemetry;
        var body=new StackPanel();
        body.Children.Add(ModalHero("CENTRAL DE MANUTENÇÃO", "Saúde mecânica do caminhão", "Desgaste em tempo real, histórico de serviços e custos integrados ao banco TransPoli.", data==null||!data.Connected ? "ETS2 OFFLINE" : "TELEMETRIA ATIVA", data==null||!data.Connected ? "Yellow" : "Green"));
        body.Children.Add(ModalSectionTitle("ESTADO ATUAL DO CAMINHÃO"));

        if(data==null||!data.Connected)
            body.Children.Add(ModalLine("Conecte o ETS2 para consultar o desgaste em tempo real.",13));
        else
        {
            body.Children.Add(ModalStatusStrip("● MONITORAMENTO MECÂNICO • DESGASTE LIDO DIRETAMENTE DA TELEMETRIA ETS2", "Green"));
            var grid=new UniformGrid{Columns=3};
            grid.Children.Add(MiniCard("MOTOR",WearText(data.WearEngine)));
            grid.Children.Add(MiniCard("TRANSMISSÃO",WearText(data.WearTransmission)));
            grid.Children.Add(MiniCard("CABINE",WearText(data.WearCabin)));
            grid.Children.Add(MiniCard("CHASSI",WearText(data.WearChassis)));
            grid.Children.Add(MiniCard("RODAS",WearText(data.WearWheels)));
            grid.Children.Add(MiniCard("ODÔMETRO",$"{data.OdometerKm:0.0} km"));
            body.Children.Add(grid);
            var alert=BuildWearAlerts(data);
            body.Children.Add(ModalPanel(new TextBlock{Text=alert,FontSize=14,Foreground=FindResource(alert.Contains("CRÍTICO")?"Red":alert.Contains("ATENÇÃO")?"Yellow":"Green") as Brush,TextWrapping=TextWrapping.Wrap}));
        }

        var root=await LoadMaintenanceAsync();
        body.Children.Add(ModalSectionTitle("RESUMO DA MANUTENÇÃO", "HISTÓRICO E CUSTOS"));
        var summary=root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("summary",out var s)?s:default;
        var summaryGrid=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,10)};
        summaryGrid.Children.Add(MiniCard("SERVIÇOS",JsonText(summary,"services","0")));
        summaryGrid.Children.Add(MiniCard("GASTO TOTAL",$"R$ {JsonNumber(summary,"cost_brl"):N2}"));
        summaryGrid.Children.Add(MiniCard("ÚLTIMO SERVIÇO",JsonDate(summary,"last_service_at")));
        body.Children.Add(summaryGrid);

        var register=ModalButton("🔧 REGISTRAR MANUTENÇÃO");
        register.Click+=async(_,e)=>{e.Handled=true;await RegisterMaintenanceAsync();};
        body.Children.Add(register);

        body.Children.Add(ModalSectionTitle("HISTÓRICO RECENTE"));
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("records",out var records)||records.GetArrayLength()==0)
            body.Children.Add(ModalLine("Nenhum serviço registrado ainda.",12));
        else foreach(var record in records.EnumerateArray().Take(20))
        {
            var service=JsonText(record,"service_type","Manutenção");
            var component=JsonText(record,"component","Geral");
            var desc=JsonText(record,"description","");
            var cost=JsonNumber(record,"cost_brl");
            var odo=JsonNumber(record,"odometer_km");
            var date=FormatDate(JsonText(record,"created_at",""));
            body.Children.Add(ModalPanel(new StackPanel{Children={
                new TextBlock{Text=$"{service} • {component}",FontSize=14,FontWeight=FontWeights.Bold,Foreground=FindResource("Text") as Brush},
                new TextBlock{Text=$"{date} • {odo:0.0} km • R$ {cost:N2}",FontSize=11,Foreground=FindResource("Muted") as Brush,Margin=new Thickness(0,4,0,0)},
                new TextBlock{Text=desc,FontSize=11,Foreground=FindResource("Text") as Brush,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)}
            }}));
        }

        ShowModalContent("maintenance",BuildModalCard("🔧 MANUTENÇÃO DO CAMINHÃO",body,"Desgaste em tempo real • histórico de serviços • custos no banco"));
    }

    private async Task<JsonElement> LoadMaintenanceAsync()
    {
        var token=SecureTokenStore.Read();
        if(string.IsNullOrWhiteSpace(token))return default;
        try
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,$"{MaintenanceApiBaseUrl}/me/maintenance");
            req.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
            using var res=await _maintenanceHttp.SendAsync(req);
            if(!res.IsSuccessStatusCode)return default;
            using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }
        catch{return default;}
    }

    private async Task RegisterMaintenanceAsync()
    {
        var data=LastTelemetry;
        if(data==null||!data.Connected){StatusText.Text="TransPoli • conecte o ETS2 antes de registrar manutenção";return;}
        var service=PromptText("Manutenção","Tipo de serviço","Revisão preventiva");if(service==null)return;
        var component=PromptText("Componente","Componente atendido","Geral");if(component==null)return;
        var costText=PromptText("Custo","Custo da manutenção em R$","0,00");if(costText==null)return;
        costText=costText.Replace(".","").Replace(",",".");
        if(!decimal.TryParse(costText,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var cost)||cost<0){StatusText.Text="TransPoli • custo inválido";return;}
        var description=PromptText("Observação","Descrição do serviço","")??"";
        var now=DateTime.UtcNow;
        var localId="maintenance-"+Guid.NewGuid().ToString("N");
        var localTripId=string.IsNullOrWhiteSpace(_localTripId)?null:_localTripId;

        try
        {
            if(LocalData.Current is { } store)
            {
                new LocalMaintenanceRepository(store.Db).Add(
                    localId,
                    string.IsNullOrWhiteSpace(data.TruckId)?data.LicensePlate:data.TruckId,
                    service,component,description,cost,data.OdometerKm,now,localTripId);
                RefreshActiveTripFinancials(force: true);
            }

            var token=SecureTokenStore.Read();
            var truckId=string.IsNullOrWhiteSpace(token)?null:await ResolveCurrentTruckIdAsync(token,data);
            var payload=new
            {
                truckId,serviceType=service,component,description,costBrl=(double)cost,
                odometerKm=(double)data.OdometerKm,wearEngine=(double)data.WearEngine,
                wearTransmission=(double)data.WearTransmission,wearCabin=(double)data.WearCabin,
                wearChassis=(double)data.WearChassis,wearWheels=(double)data.WearWheels,
                sourceKey=localId,tripId=_serverTripId,localTripId
            };

            if(string.IsNullOrWhiteSpace(token)||string.IsNullOrWhiteSpace(truckId))
            {
                _serverSync.QueueExpense(_serverTripId,payload);
                StatusText.Text=$"TransPoli • manutenção salva localmente • R$ {cost:N2} • sincronização pendente";
                await ShowMaintenanceTabletModalAsync();
                return;
            }

            using var req=new HttpRequestMessage(HttpMethod.Post,$"{MaintenanceApiBaseUrl}/me/maintenance");
            req.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
            req.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
            using var res=await _maintenanceHttp.SendAsync(req);
            if(!res.IsSuccessStatusCode)
                _serverSync.QueueExpense(_serverTripId,payload);
            StatusText.Text=res.IsSuccessStatusCode
                ? $"TransPoli • manutenção registrada • R$ {cost:N2}"
                : $"TransPoli • manutenção salva localmente • R$ {cost:N2} • sincronização pendente";
        }
        catch
        {
            StatusText.Text=$"TransPoli • manutenção salva localmente • R$ {cost:N2} • sincronização pendente";
        }
        await ShowMaintenanceTabletModalAsync();
    }

    private async Task<string?> ResolveCurrentTruckIdAsync(string token,TelemetrySnapshot data)
    {
        try
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,$"{MaintenanceApiBaseUrl}/me/garage/fleet");
            req.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
            using var res=await _maintenanceHttp.SendAsync(req);if(!res.IsSuccessStatusCode)return null;
            using var doc=JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            if(!doc.RootElement.TryGetProperty("trucks",out var trucks))return null;
            foreach(var truck in trucks.EnumerateArray())
                if(string.Equals(JsonText(truck,"brand",""),data.TruckBrand,StringComparison.OrdinalIgnoreCase)&&string.Equals(JsonText(truck,"model",""),data.TruckModel,StringComparison.OrdinalIgnoreCase)&&string.Equals(JsonText(truck,"license_plate",""),data.LicensePlate,StringComparison.OrdinalIgnoreCase))
                    return JsonText(truck,"truck_id","");
        }
        catch{}
        return null;
    }

    private static string WearText(float wear)=>$"{Math.Clamp(wear,0,1)*100:0.0}% desgaste";
    private static string BuildWearAlerts(TelemetrySnapshot d)
    {
        var max=Math.Max(Math.Max(d.WearEngine,d.WearTransmission),Math.Max(Math.Max(d.WearCabin,d.WearChassis),d.WearWheels));
        if(max>=.75f)return "🔴 CRÍTICO • componente com 75% ou mais de desgaste. Faça manutenção preventiva.";
        if(max>=.50f)return "🟡 ATENÇÃO • desgaste elevado detectado. Planeje uma manutenção preventiva.";
        return "🟢 NORMAL • nenhum componente atingiu a faixa de atenção.";
    }
    private static string JsonText(JsonElement e,string p,string fallback)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(p,out var v)&&v.ValueKind!=JsonValueKind.Null?v.ToString():fallback;
    private static double JsonNumber(JsonElement e,string p)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(p,out var v)&&v.ValueKind==JsonValueKind.Number?v.GetDouble():0;
    private static string JsonDate(JsonElement e,string p)=>e.ValueKind==JsonValueKind.Object&&e.TryGetProperty(p,out var v)&&v.ValueKind!=JsonValueKind.Null?FormatDate(v.ToString()):"Nenhum";
    private static string FormatDate(string value)=>DateTime.TryParse(value,out var d)?d.ToLocalTime().ToString("dd/MM/yyyy HH:mm"):"—";
}