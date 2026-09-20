using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.CompilerServices;

namespace TransPoli;

public partial class MainWindow
{
    private DispatcherTimer? _v13FixTimer; private bool _v13EngineInitialized; private bool _v13LastEngine; private bool _v13RecoveryBusy; private bool _v13GarageOpening; private static readonly TimeSpan V13Poll=TimeSpan.FromMilliseconds(750);
    internal void StartV13Fixes(){if(_v13FixTimer!=null)return;_v13FixTimer=new DispatcherTimer{Interval=V13Poll};_v13FixTimer.Tick+=async(_,_)=>await V13TickAsync();_v13FixTimer.Start();_=V13TickAsync();}
    private async Task V13TickAsync(){if(_v13RecoveryBusy)return;try{using var response=await _http.GetAsync(TelemetryUrl);if(!response.IsSuccessStatusCode)return;await using var stream=await response.Content.ReadAsStreamAsync();var data=await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});if(data is null||!data.Connected)return;var stopped=Math.Abs(data.SpeedKph)<0.5f;if(!_v13EngineInitialized){_v13EngineInitialized=true;_v13LastEngine=data.EngineEnabled;if(data.EngineEnabled&&!stopped&&!_garageUnauthorized)_truckLocked=false;}else{if(!_v13LastEngine&&data.EngineEnabled&&!_garageUnauthorized)_truckLocked=false;if(_v13LastEngine&&!data.EngineEnabled&&stopped)_truckLocked=true;_v13LastEngine=data.EngineEnabled;}if(_garageUnauthorized)_truckLocked=true;if(!_tripActive&&HasActiveJob(data)&&data.CargoLoaded)await RecoverExistingTripAsync(data);}catch{}}
    private async Task RecoverExistingTripAsync(TelemetrySnapshot data){if(_v13RecoveryBusy)return;_v13RecoveryBusy=true;try{var token=SecureTokenStore.Read();if(string.IsNullOrWhiteSpace(token))return;using var request=new HttpRequestMessage(HttpMethod.Get,$"{ApiBaseUrl}/me/trips");request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");request.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");using var response=await _http.SendAsync(request);if(!response.IsSuccessStatusCode)return;using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());if(!doc.RootElement.TryGetProperty("trips",out var trips)||trips.ValueKind!=JsonValueKind.Array)return;JsonElement? active=null;foreach(var trip in trips.EnumerateArray()){if(!trip.TryGetProperty("status",out var status)||!string.Equals(status.GetString(),"active",StringComparison.OrdinalIgnoreCase))continue;var cargo=trip.TryGetProperty("cargo",out var c)?c.GetString():null;if(!string.IsNullOrWhiteSpace(data.Cargo)&&!string.Equals(cargo,data.Cargo,StringComparison.OrdinalIgnoreCase))continue;active=trip;break;}if(active is null)return;var tripElement=active.Value;var id=tripElement.TryGetProperty("id",out var idElement)?idElement.GetString():null;if(string.IsNullOrWhiteSpace(id))return;_serverTripId=id;_tripActive=true;_tripStartedAtUtc=tripElement.TryGetProperty("started_at",out var started)&&DateTime.TryParse(started.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal,out var parsedStart)?parsedStart.ToUniversalTime():DateTime.UtcNow;try{using var previewRequest=new HttpRequestMessage(HttpMethod.Get,$"{ApiBaseUrl}/me/trips/{id}/economy-preview");previewRequest.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");previewRequest.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");using var previewResponse=await _http.SendAsync(previewRequest);if(previewResponse.IsSuccessStatusCode){using var previewDoc=JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());if(previewDoc.RootElement.TryGetProperty("preview",out var preview)&&preview.TryGetProperty("distanceKm",out var distanceElement)){var distance=Math.Max(0,distanceElement.GetSingle());_tripStartOdometer=Math.Max(0,data.OdometerKm-(float)distance);}else _tripStartOdometer=data.OdometerKm;}else _tripStartOdometer=data.OdometerKm;}catch{_tripStartOdometer=data.OdometerKm;}_tripStartFuel=data.FuelLiters;_jobMissingTicks=0;TripStatusText.Text="VIAGEM EM ANDAMENTO • TELEMETRIA RECUPERADA";TripRouteText.Text=BuildRoute(data);TripCargoText.Text=string.IsNullOrWhiteSpace(data.Cargo)?"Carga não informada":$"Carga: {data.Cargo}";TripDistanceText.Text=$"{Math.Max(0,data.OdometerKm-_tripStartOdometer):0.0} km";TripDurationText.Text=FormatDuration(DateTime.UtcNow-_tripStartedAtUtc);TripLiveText.Text=data.GamePaused?"JOGO PAUSADO":"AO VIVO";StatusText.Text="TransPoli • viagem ativa recuperada da telemetria";}catch{}finally{_v13RecoveryBusy=false;}}
    // Não escreve mais no AlertText. O alerta visual tem uma única fonte de verdade.
    private void StabilizeOperationsAlert(){try{if(_garageUnauthorized){FuelAutoText.Text=string.IsNullOrWhiteSpace(_garageMessage)?"Abastecimento automático: monitorando":_garageMessage;return;}if(_lastFuelLiters.HasValue)FuelAutoText.Text=$"Abastecimento automático: monitorando • {_lastFuelLiters.Value:0.0} L";}catch{}}
    internal void ShowFuelPaymentModalV13(){var telemetry=_pendingRefuelTelemetry;var liters=_pendingRefuelLiters;if(telemetry is null||liters<=0){ShowFuelManualModalV13();return;}var panel=new StackPanel();panel.Children.Add(ModalPanel(new TextBlock{Text=$"⛽ ABASTECIMENTO DETECTADO\n{liters:0.0} litros adicionados ao tanque. Informe o valor pago, cidade e posto. O débito só será lançado depois de confirmar.",FontSize=13,FontWeight=FontWeights.Bold,Foreground=FindResource("Text") as Brush,TextWrapping=TextWrapping.Wrap}));var price=NewV13TextBox("Preço por litro (R$)");var station=NewV13TextBox("Nome do posto");var city=NewV13TextBox("Cidade");panel.Children.Add(ModalLabel("VALOR POR LITRO"));panel.Children.Add(price);panel.Children.Add(ModalLabel("POSTO"));panel.Children.Add(station);panel.Children.Add(ModalLabel("CIDADE"));panel.Children.Add(city);panel.Children.Add(ModalLine($"Total: {liters:0.0} L × preço informado.",12));var save=ModalButton("✓ CONFIRMAR ABASTECIMENTO E DESCONTAR DO BANCO");save.Click+=async(_,e)=>{e.Handled=true;if(!TryMoney(price.Text,out var priceValue)||priceValue<=0||string.IsNullOrWhiteSpace(station.Text)||string.IsNullOrWhiteSpace(city.Text)){StatusText.Text="TransPoli • informe preço, posto e cidade para concluir o abastecimento";return;}await RegisterFuelPaymentV13Async(telemetry,liters,priceValue,station.Text.Trim(),city.Text.Trim());};panel.Children.Add(save);var cancel=ModalButton("✕ CANCELAR");cancel.Click+=(_,e)=>{e.Handled=true;_pendingRefuelTelemetry=null;_pendingRefuelLiters=0;CloseOperationalModal();};panel.Children.Add(cancel);ShowModalContent("fuel-v13",BuildModalCard("⛽ ABASTECIMENTO",panel,"Pagamento manual após a detecção da telemetria"));}
    private void ShowFuelManualModalV13(){var panel=new StackPanel();panel.Children.Add(ModalLine("O valor será debitado do Banco do Motorista somente após a confirmação.",12));var liters=NewV13TextBox("Litros");var price=NewV13TextBox("Preço por litro (R$)");var station=NewV13TextBox("Nome do posto");var city=NewV13TextBox("Cidade");panel.Children.Add(ModalLabel("LITROS"));panel.Children.Add(liters);panel.Children.Add(ModalLabel("PREÇO POR LITRO"));panel.Children.Add(price);panel.Children.Add(ModalLabel("POSTO"));panel.Children.Add(station);panel.Children.Add(ModalLabel("CIDADE"));panel.Children.Add(city);var save=ModalButton("✓ CONFIRMAR E DESCONTAR DO BANCO");save.Click+=async(_,e)=>{e.Handled=true;if(!float.TryParse(liters.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var l)||l<=0||!TryMoney(price.Text,out var p)||p<=0||string.IsNullOrWhiteSpace(station.Text)||string.IsNullOrWhiteSpace(city.Text)){StatusText.Text="TransPoli • informe litros, preço, posto e cidade";return;}var data=await LoadCurrentTelemetryAsync();if(data is null){StatusText.Text="TransPoli • telemetria indisponível";return;}await RegisterFuelPaymentV13Async(data,l,p,station.Text.Trim(),city.Text.Trim());};panel.Children.Add(save);ShowModalContent("fuel-v13",BuildModalCard("⛽ ABASTECIMENTO",panel,"Lançamento manual"));}
    private async Task RegisterFuelPaymentV13Async(TelemetrySnapshot data,float liters,decimal price,string station,string city)
    {
        var amount=Math.Round((decimal)liters*price,2);
        var now=DateTime.UtcNow;
        var localTripId=GetLocalTripIdForExpense();
        try
        {
            if(LocalData.Current is { } store)
            {
                var refuelId=Guid.NewGuid().ToString("N");
                _refuelings.Add(new RefuelingRecord
                {
                    Id=refuelId,RecordedAtUtc=now,Station=station,Location=city,Liters=liters,
                    FuelBefore=_fuelBefore,FuelAfter=_fuelAfter,OdometerKm=data.OdometerKm,
                    Truck=$"{data.TruckBrand} {data.TruckModel}".Trim(),LicensePlate=data.LicensePlate??""
                });
                SaveOperations();
                new LocalEconomyRepository(store.Db).AddExpense(
                    "fuel-"+refuelId,localTripId,"fuel_expense",
                    $"Abastecimento • {station} • {liters:0.0} L",
                    amount,now);
            }

            var token=SecureTokenStore.Read();
            var payload=new
            {
                liters,pricePerLiter=price,amount,station,city,odometerKm=data.OdometerKm,
                truckBrand=data.TruckBrand,truckModel=data.TruckModel,licensePlate=data.LicensePlate,
                tripId=_serverTripId,localTripId
            };

            if(string.IsNullOrWhiteSpace(token))
            {
                _serverSync.QueueExpense(_serverTripId,payload);
                _pendingRefuelTelemetry=null;_pendingRefuelLiters=0;
                StatusText.Text=$"TransPoli • abastecimento salvo localmente • R$ {amount:0.00} debitado";
                CloseOperationalModal();
                return;
            }

            using var request=new HttpRequestMessage(HttpMethod.Post,$"{ApiBaseUrl}/me/expenses/fuel-payment");
            request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
            request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
            using var response=await _http.SendAsync(request);
            var text=await response.Content.ReadAsStringAsync();

            if(!response.IsSuccessStatusCode)
                _serverSync.QueueExpense(_serverTripId,payload);

            _pendingRefuelTelemetry=null;_pendingRefuelLiters=0;
            StatusText.Text=response.IsSuccessStatusCode
                ? $"TransPoli • abastecimento confirmado • R$ {amount:0.00} debitado do banco"
                : $"TransPoli • abastecimento salvo localmente • R$ {amount:0.00} • sincronização pendente";
            CloseOperationalModal();
        }
        catch
        {
            try
            {
                _serverSync.QueueExpense(_serverTripId,new
                {
                    liters,pricePerLiter=price,amount,station,city,odometerKm=data.OdometerKm,
                    truckBrand=data.TruckBrand,truckModel=data.TruckModel,licensePlate=data.LicensePlate,
                    tripId=_serverTripId,localTripId
                });
            }
            catch { }
            StatusText.Text=$"TransPoli • abastecimento salvo localmente • R$ {amount:0.00} • sincronização pendente";
            _pendingRefuelTelemetry=null;_pendingRefuelLiters=0;
            CloseOperationalModal();
        }
    }

    private string? GetLocalTripIdForExpense()
    {
        if(!string.IsNullOrWhiteSpace(_localTripId)) return _localTripId;
        return null;
    }
    private static TextBox NewV13TextBox(string placeholder)=>new(){ToolTip=placeholder,FontSize=15,Padding=new Thickness(10),Background=Application.Current.FindResource("Panel2") as Brush,Foreground=Application.Current.FindResource("Text") as Brush,BorderBrush=Application.Current.FindResource("Panel2") as Brush,Margin=new Thickness(0,0,0,2)};
    private static bool TryMoney(string text,out decimal value)=>decimal.TryParse(text.Trim().Replace('.',','),NumberStyles.Number,CultureInfo.GetCultureInfo("pt-BR"),out value)||decimal.TryParse(text.Trim().Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out value);
    internal void ShowGarageFromSaveV13(){if(_v13GarageOpening)return;_v13GarageOpening=true;try{var scan=Ets2SaveScanner.Scan();var panel=new StackPanel();panel.Children.Add(ModalLine(scan.Description,12));panel.Children.Add(ModalLabel("CAMINHÕES ENCONTRADOS NO PERFIL / SAVE"));if(scan.Trucks.Count==0)panel.Children.Add(ModalPanel(new TextBlock{Text=scan.ProtectedSaveFound?"O save foi encontrado, mas está protegido/criptografado. Abra o ETS2 e deixe o perfil/save carregado; o TransPoli não modifica o save.":"Nenhum caminhão legível foi encontrado em Documents\\Euro Truck Simulator 2\\profiles/steam_profiles. Se o Steam Cloud estiver sendo usado, carregue o perfil no jogo e tente novamente.",FontSize=13,Foreground=FindResource("Text") as Brush,TextWrapping=TextWrapping.Wrap}));else foreach(var truck in scan.Trucks){var card=new StackPanel();card.Children.Add(new TextBlock{Text=truck.DisplayName,FontSize=16,FontWeight=FontWeights.Bold,Foreground=FindResource("Text") as Brush,TextWrapping=TextWrapping.Wrap});card.Children.Add(ModalValueRow("Placa",string.IsNullOrWhiteSpace(truck.Plate)?"sem placa":truck.Plate));card.Children.Add(ModalValueRow("Quilometragem",truck.OdometerKm>0?$"{truck.OdometerKm:0.0} km":"não informado"));card.Children.Add(ModalValueRow("Combustível",truck.FuelLiters>0?$"{truck.FuelLiters:0.0} L":"não informado"));card.Children.Add(ModalValueRow("Perfil",truck.ProfileName));var bind=ModalButton("🔐 VINCULAR EXCLUSIVAMENTE A MIM");bind.Click+=async(_,e)=>{e.Handled=true;await BindScannedTruckV13Async(truck);};card.Children.Add(bind);panel.Children.Add(ModalPanel(card));}panel.Children.Add(ModalLine("🔒 Ao vincular, o caminhão fica exclusivo deste motorista. Outro motorista com a mesma chave marca|modelo|placa recebe bloqueio de garagem. O save original é somente leitura.",11));ShowModalContent("garage-v13",BuildModalCard("🚛 GARAGEM TRANSPOLI",panel,"Fonte: perfil/save local do ETS2/ATS"));}finally{_v13GarageOpening=false;}}
    private async Task BindScannedTruckV13Async(Ets2TruckInfo truck){var token=SecureTokenStore.Read();if(string.IsNullOrWhiteSpace(token))return;try{var payload=new{brand=truck.Brand,model=truck.Model,plate=truck.Plate,label=truck.DisplayName};using var request=new HttpRequestMessage(HttpMethod.Post,$"{ApiBaseUrl}/me/garage/bind-current");request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");request.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");using var response=await _http.SendAsync(request);var text=await response.Content.ReadAsStringAsync();if(!response.IsSuccessStatusCode){StatusText.Text="TransPoli • "+TryApiError(text,"não foi possível vincular o caminhão");return;}StatusText.Text=$"TransPoli • {truck.DisplayName} vinculado exclusivamente à sua garagem";_lastGarageCheck=DateTime.MinValue;await CheckGarageAuthorizationAsync();ShowGarageFromSaveV13();}catch{StatusText.Text="TransPoli • falha ao vincular caminhão do save";}}
    private static string TryApiError(string json,string fallback){try{using var doc=JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("error",out var e)?(e.GetString()??fallback):fallback;}catch{return fallback;}}
}
public sealed class Ets2TruckInfo{public string ProfileName{get;init;}="";public string Brand{get;init;}="";public string Model{get;init;}="";public string Plate{get;init;}="";public float OdometerKm{get;init;}public float FuelLiters{get;init;}public string DisplayName=>string.IsNullOrWhiteSpace(Brand)&&string.IsNullOrWhiteSpace(Model)?"Caminhão":$"{Brand} {Model}".Trim();}
public sealed class Ets2SaveScanResult{public List<Ets2TruckInfo> Trucks{get;}=new();public bool ProtectedSaveFound{get;init;}public string Description{get;init;}="";}
public static class Ets2SaveScanner{public static Ets2SaveScanResult Scan(){var result=new Ets2SaveScanResult{Description="Leitura somente. Nenhum arquivo do jogo será alterado."};var docs=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);var roots=new[]{Path.Combine(docs,"Euro Truck Simulator 2"),Path.Combine(docs,"American Truck Simulator")};foreach(var root in roots)foreach(var saveRoot in new[]{Path.Combine(root,"profiles"),Path.Combine(root,"steam_profiles")})if(Directory.Exists(saveRoot))foreach(var profile in SafeDirectories(saveRoot))foreach(var file in SafeFiles(profile,"game.sii")){try{var text=Encoding.UTF8.GetString(File.ReadAllBytes(file));if(!text.Contains("truck",StringComparison.OrdinalIgnoreCase)){result=new Ets2SaveScanResult{ProtectedSaveFound=true,Description=$"Save encontrado em {profile}, mas o conteúdo não está em formato textual legível."};continue;}ParseTruckBlocks(text,Path.GetFileName(profile),result.Trucks);}catch{result=new Ets2SaveScanResult{ProtectedSaveFound=true,Description=$"Save encontrado em {profile}, mas não pôde ser lido com segurança."};}}var unique=result.Trucks.GroupBy(x=>$"{Normalize(x.Brand)}|{Normalize(x.Model)}|{Normalize(x.Plate)}").Select(g=>g.First()).ToList();result.Trucks.Clear();result.Trucks.AddRange(unique);if(result.Trucks.Count>0){var output=new Ets2SaveScanResult{ProtectedSaveFound=result.ProtectedSaveFound,Description=$"{result.Trucks.Count} caminhão(ões) legível(is) encontrado(s) no perfil/save local. Leitura somente."};output.Trucks.AddRange(result.Trucks);return output;}return result;}private static void ParseTruckBlocks(string text,string profileName,List<Ets2TruckInfo> output){var lines=text.Replace("\r","").Split('\n');var depth=0;var inTruck=false;var buffer=new List<string>();foreach(var line in lines){if(!inTruck&&Regex.IsMatch(line,@"^\s*truck\s*:\s*",RegexOptions.IgnoreCase)){inTruck=true;buffer.Clear();depth=Count(line,'{')-Count(line,'}');buffer.Add(line);if(depth<=0){Parse(buffer,profileName,output);inTruck=false;}continue;}if(!inTruck)continue;buffer.Add(line);depth+=Count(line,'{')-Count(line,'}');if(depth<=0){Parse(buffer,profileName,output);inTruck=false;buffer.Clear();}}}private static void Parse(List<string> block,string profile,string[] unused){ } private static void Parse(List<string> block,string profile,List<Ets2TruckInfo> output){string Field(string key){var source=string.Join("\n",block);var pattern="\\b"+Regex.Escape(key)+"\\s*:\s*(?:\"(?<q>[^\"]*)\"|(?<v>[^\\s}]+))";var m=Regex.Match(source,pattern,RegexOptions.IgnoreCase);return m.Success?(m.Groups["q"].Success?m.Groups["q"].Value:m.Groups["v"].Value):"";}var brand=Field("brand");var model=Field("model");var plate=Field("license_plate");if(string.IsNullOrWhiteSpace(brand)&&string.IsNullOrWhiteSpace(model)&&string.IsNullOrWhiteSpace(plate))return;float Number(string key)=>float.TryParse(Field(key),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:0;output.Add(new Ets2TruckInfo{ProfileName=profile,Brand=brand,Model=model,Plate=plate,OdometerKm=Number("odometer"),FuelLiters=Number("fuel")});}private static int Count(string text,char c)=>text.Count(x=>x==c);private static string Normalize(string value)=>(value??"").Trim().ToLowerInvariant();private static IEnumerable<string> SafeDirectories(string path){try{return Directory.EnumerateDirectories(path).ToArray();}catch{return Array.Empty<string>();}}private static IEnumerable<string> SafeFiles(string path,string name){try{return Directory.EnumerateFiles(path,name,SearchOption.AllDirectories).Take(20).ToArray();}catch{return Array.Empty<string>();}}}
internal static class V13ModuleBootstrap{[ModuleInitializer]internal static void Initialize(){EventManager.RegisterClassHandler(typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is MainWindow main)main.StartV13Fixes();}));EventManager.RegisterClassHandler(typeof(Button),UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler((sender,e)=>{if(e.OriginalSource is not Button button)return;if(Window.GetWindow(button) is not MainWindow main)return;var tag=button.Tag?.ToString()??"";if(button.Tag!=null&&!tag.Equals("feature-garage",StringComparison.OrdinalIgnoreCase))return;var text=button.Content?.ToString()??"";if(tag.Equals("feature-garage",StringComparison.OrdinalIgnoreCase)||text.Contains("GARAGEM",StringComparison.OrdinalIgnoreCase)){e.Handled=true;_=main.ShowGarageSaveInventoryAsync();}else if(button.Tag==null&&(text.Contains("ABASTECIMENTO",StringComparison.OrdinalIgnoreCase)||text.Contains("COMBUSTÍVEL",StringComparison.OrdinalIgnoreCase))){e.Handled=true;main.ShowFuelPaymentModalV13();}}),true);}}
