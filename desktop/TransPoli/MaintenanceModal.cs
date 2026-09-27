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

namespace TransPoli;

public partial class MainWindow
{
    private const string MaintenanceApiBaseUrl="https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _maintenanceHttp=new(){Timeout=TimeSpan.FromSeconds(5)};
    private async void MaintenanceButton_Click(object sender,RoutedEventArgs e)
    {
        e.Handled=true;
        await ShowMaintenanceTabletModalAsync();
    }

    internal async Task ShowMaintenanceTabletModalAsync()
    {
        ShowModalContent("maintenance",BuildModalLoading("CENTRAL TÉCNICA • LENDO MANUTENÇÃO..."));
        var data=LastTelemetry;
        var body=new StackPanel();
        body.Children.Add(ModalHero("CENTRAL DE MANUTENÇÃO", "Prontuário técnico e serviços", "A telemetria sinaliza desgaste; serviços confirmados formam o histórico técnico e seus custos seguem a operação financeira TransPoli.", data==null||!data.Connected ? "HISTÓRICO DISPONÍVEL" : "DIAGNÓSTICO ATIVO", data==null||!data.Connected ? "Yellow" : "Green"));
        body.Children.Add(ModalSectionTitle("DIAGNÓSTICO", "CONDIÇÃO MECÂNICA ATUAL"));

        if(data==null||!data.Connected)
            body.Children.Add(ModalStatePanel("TELEMETRIA OFFLINE", "Diagnóstico em tempo real indisponível", "Conecte o ETS2 para consultar desgaste de motor, transmissão, cabine, chassi e rodas. O histórico de serviços continua disponível.", "Yellow"));
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

            // Intelligence local: transforma histórico + odômetro em planejamento,
            // sem criar custo nem substituir o desgaste oficial da telemetria.
            if(LocalData.Current is { } intelligenceStore)
            {
                try
                {
                    var truckKey=string.IsNullOrWhiteSpace(data.TruckId)?(data.LicensePlate??""):data.TruckId;
                    var plan=new VehicleMaintenanceIntelligenceRepository(intelligenceStore.Db).Read(
                        truckKey,data.OdometerKm,data.WearEngine,data.WearTransmission,data.WearCabin,data.WearChassis,data.WearWheels);
                    body.Children.Add(ModalSectionTitle("PLANO PREVENTIVO", "HISTÓRICO LOCAL + ODÔMETRO"));
                    var planGrid=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,10)};
                    foreach(var item in plan.Components)
                    {
                        var label=item.Component switch{"engine"=>"MOTOR","transmission"=>"TRANSMISSÃO","cabin"=>"CABINE","chassis"=>"CHASSI","wheels"=>"RODAS",_=>item.Component.ToUpperInvariant()};
                        var value=item.Overdue?"REVISÃO VENCIDA":$"{item.RemainingKm:0} km restantes";
                        planGrid.Children.Add(MiniCard(label,value));
                    }
                    body.Children.Add(planGrid);
                    var urgent=plan.Components.OrderByDescending(x=>x.Overdue).ThenBy(x=>x.RemainingKm).ThenByDescending(x=>x.Wear).FirstOrDefault();
                    if(urgent is not null)
                    {
                        var urgentLabel=urgent.Component switch{"engine"=>"MOTOR","transmission"=>"TRANSMISSÃO","cabin"=>"CABINE","chassis"=>"CHASSI","wheels"=>"RODAS",_=>urgent.Component.ToUpperInvariant()};
                        var reason=urgent.Overdue
                            ? $"Revisão por quilometragem vencida • próxima referência {urgent.NextServiceOdometerKm:0} km"
                            : $"Próxima referência {urgent.NextServiceOdometerKm:0} km • desgaste atual {urgent.Wear*100:0}%";
                        body.Children.Add(ModalValueRow("Prioridade preventiva",urgentLabel+" • "+reason));
                    }
                }
                catch(Exception intelligenceEx){App.WriteUiCrashLog("Maintenance.Intelligence",intelligenceEx);}
            }
            var attachedTrailer=data.Trailers?.FirstOrDefault(x=>x.Attached);
            if(attachedTrailer is not null)
            {
                body.Children.Add(ModalSectionTitle("REBOQUE ACOPLADO", "TELEMETRIA OFICIAL ETS2"));
                var trailerGrid=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,10)};
                trailerGrid.Children.Add(MiniCard("CARROCERIA",WearText(attachedTrailer.WearBody)));
                trailerGrid.Children.Add(MiniCard("CHASSI",WearText(attachedTrailer.WearChassis)));
                trailerGrid.Children.Add(MiniCard("RODAS",WearText(attachedTrailer.WearWheels)));
                body.Children.Add(trailerGrid);
                var trailerMax=Math.Max(attachedTrailer.WearBody,Math.Max(attachedTrailer.WearChassis,attachedTrailer.WearWheels));
                body.Children.Add(ModalValueRow("Identificação do reboque",
                    string.IsNullOrWhiteSpace(attachedTrailer.LicensePlate)
                        ? (attachedTrailer.Name??attachedTrailer.Id??"Reboque acoplado")
                        : attachedTrailer.LicensePlate));
                if(!string.IsNullOrWhiteSpace(attachedTrailer.BodyType))
                    body.Children.Add(ModalValueRow("Carroceria / body type",attachedTrailer.BodyType));
                if(attachedTrailer.CargoDamage>0)
                    body.Children.Add(ModalValueRow("Dano da carga",$"{Math.Clamp(attachedTrailer.CargoDamage*100f,0f,100f):0.0}% • telemetria ETS2"));
                if(trailerMax>=.50f)
                    body.Children.Add(ModalStatePanel(
                        trailerMax>=.75f?"REBOQUE • MANUTENÇÃO CRÍTICA":"REBOQUE • ATENÇÃO MECÂNICA",
                        trailerMax>=.75f?"Intervenção recomendada":"Planeje inspeção preventiva",
                        $"Maior desgaste informado pela telemetria: {trailerMax*100:0.0}%",
                        trailerMax>=.75f?"Red":"Yellow"));
            }

            var alert=BuildWearAlerts(data);
            body.Children.Add(ModalStatePanel(alert.Contains("CRÍTICO") ? "MANUTENÇÃO CRÍTICA" : alert.Contains("ATENÇÃO") ? "ATENÇÃO MECÂNICA" : "SISTEMAS NOMINAIS", alert.Contains("CRÍTICO") ? "Intervenção recomendada" : alert.Contains("ATENÇÃO") ? "Planeje manutenção preventiva" : "Caminhão dentro da faixa operacional", alert, alert.Contains("CRÍTICO") ? "Red" : alert.Contains("ATENÇÃO") ? "Yellow" : "Green"));
        }

        var root=await LoadMaintenanceAsync();
        body.Children.Add(ModalSectionTitle("PRONTUÁRIO DE SERVIÇOS", "HISTÓRICO E CUSTOS"));
        var summary=root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("summary",out var s)?s:default;
        var summaryGrid=new UniformGrid{Columns=3,Margin=new Thickness(0,0,0,10)};
        summaryGrid.Children.Add(MiniCard("SERVIÇOS",JsonText(summary,"services","0")));
        summaryGrid.Children.Add(MiniCard("GASTO TOTAL",$"R$ {JsonNumber(summary,"cost_brl"):N2}"));
        summaryGrid.Children.Add(MiniCard("ÚLTIMO SERVIÇO",JsonDate(summary,"last_service_at")));
        body.Children.Add(summaryGrid);

        var register=ModalButton("＋ REGISTRAR SERVIÇO REALIZADO");
        register.Click+=async(_,e)=>{e.Handled=true;await RegisterMaintenanceAsync();};
        body.Children.Add(register);

        body.Children.Add(ModalSectionTitle("HISTÓRICO RECENTE"));
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("records",out var records)||records.GetArrayLength()==0)
            body.Children.Add(ModalStatePanel("HISTÓRICO TÉCNICO", "Nenhum serviço registrado", "Revisões e reparos confirmados aparecerão aqui com componente, odômetro, custo e observações.", "Muted"));
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

        ShowModalContent("maintenance",BuildModalCard("MANUTENÇÃO DO CAMINHÃO",body,"Desgaste em tempo real • histórico de serviços • custos no banco"));
    }

    private JsonElement _maintenanceServerCache;
    private DateTime _maintenanceServerCacheUtc = DateTime.MinValue;

    private async Task<JsonElement> LoadMaintenanceAsync()
    {
        if (_maintenanceServerCache.ValueKind == JsonValueKind.Object &&
            DateTime.UtcNow - _maintenanceServerCacheUtc < TimeSpan.FromMinutes(10))
            return _maintenanceServerCache;
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
            _maintenanceServerCache = doc.RootElement.Clone();
            _maintenanceServerCacheUtc = DateTime.UtcNow;
            return _maintenanceServerCache;
        }
        catch(Exception ex){App.WriteUiCrashLog("Maintenance.Load",ex);return default;}
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
                var truckKey=string.IsNullOrWhiteSpace(data.TruckId)?data.LicensePlate:data.TruckId;
                new LocalMaintenanceRepository(store.Db).Add(
                    localId,truckKey,
                    service,component,description,cost,data.OdometerKm,now,localTripId);
                new LocalOperationsRepository(store.Db).UpsertOperationalEvent(
                    "event-"+localId,"maintenance","CONFIRMADO",
                    $"{service} • {component} • R$ {cost:N2}",
                    localId,_tripLifecycle.Current.SessionKey,localTripId,"",truckKey??"",
                    now,data.OdometerKm,true);
                RefreshActiveTripFinancials(force: true);
            }

            // O registro remoto segue sempre pelo outbox durável. O UUID do caminhão
            // é opcional aqui; o sourceKey mantém a operação idempotente.
            var payload=new
            {
                action="maintenance",truckId=(string?)null,serviceType=service,component,description,costBrl=(double)cost,
                odometerKm=(double)data.OdometerKm,wearEngine=(double)data.WearEngine,
                wearTransmission=(double)data.WearTransmission,wearCabin=(double)data.WearCabin,
                wearChassis=(double)data.WearChassis,wearWheels=(double)data.WearWheels,
                sourceKey=localId,tripId=_serverTripId,localTripId,licensePlate=data.LicensePlate
            };
            var queued=_serverSync.QueueExpense(_serverTripId,payload);
            StatusText.Text=queued
                ? $"TransPoli • manutenção salva • R$ {cost:N2} • sincronizando banco"
                : $"TransPoli • manutenção local preservada • falha ao persistir sincronização";
            if(queued)
            {
                InvalidatePhoneOfficialCache(economy: true);
                // A outbox periódica sincroniza sem criar uma chamada remota extra
                // no clique. O registro já está durável e idempotente localmente.
            }
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("Maintenance.Register", ex);
            // Local-first: se a transação atômica manutenção + despesa falhou,
            // não crie um débito remoto órfão. O motorista pode tentar novamente
            // com uma nova operação somente depois que o SQLite estiver saudável.
            StatusText.Text="TransPoli • manutenção não registrada • falha ao persistir operação local";
        }
        _maintenanceServerCache = default;
        _maintenanceServerCacheUtc = DateTime.MinValue;
        await ShowMaintenanceTabletModalAsync();
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