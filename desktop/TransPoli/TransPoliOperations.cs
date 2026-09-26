using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly HttpClient _opsHttp = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _opsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<RefuelingRecord> _refuelings = new();
    private readonly List<StopRecord> _stops = new();
    private readonly List<OccurrenceRecord> _occurrences = new();
    private readonly List<DocumentRecord> _documents = new();
    private readonly List<PoliPassRecord> _poliPassRecords = new();
    private long _nextRefuelingNumber = 1;
    private string? _operationsPath;
    private float? _lastFuelLiters;
    private float _lastOdometer;
    private bool _fuelingCandidate;
    private float _fuelBefore;
    private float _fuelAfter;
    private float _fuelPeak;
    private float _fuelOdometer;
    private float _refuelBaselineFuel;
    private bool _refuelBaselineInitialized;
    private DateTime _lastRefuelDetectedAt = DateTime.MinValue;
    private int _fuelStableTicks;
    private bool _refuelDialogOpen;
    private bool _lastRefuelActive;
    private bool _refuelTelemetryInitialized;

    protected override void OnInitialized(EventArgs e){base.OnInitialized(e);InitTransPoliOperations();}
    private void InitTransPoliOperations(){var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransPoli");Directory.CreateDirectory(folder);var owner=SecureTokenStore.ReadUserId();_operationsPath=string.IsNullOrWhiteSpace(owner)?null:Path.Combine(folder,$"transpoli-operations-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))).ToLowerInvariant()[..16]}.json");if(_operationsPath!=null)LoadOperations();_opsTimer.Tick+=async (_,_)=>await PollOperationalTelemetry();_opsTimer.Start();UpdateOpsCounters();}
    private Task PollOperationalTelemetry(){try{var data=LastTelemetry;if(data is null||!data.Connected)return Task.CompletedTask;
        if(!_refuelTelemetryInitialized){_refuelTelemetryInitialized=true;_lastRefuelActive=data.RefuelActive;_lastRefuelPayed=data.RefuelPayed;_lastFuelLiters=data.FuelLiters;_refuelBaselineFuel=data.FuelLiters;_refuelBaselineInitialized=true;_lastOdometer=data.OdometerKm;UpdateOperationsAlert(data);return Task.CompletedTask;}
        
        if(data.RefuelActive&&!_lastRefuelActive){_fuelBefore=data.FuelLiters;_fuelAfter=data.FuelLiters;_fuelOdometer=data.OdometerKm;_fuelingCandidate=true;_fuelStableTicks=0;try{TachSetStatus(TachFuel, manual: false);}catch(Exception ex){App.WriteUiCrashLog("Operations.TachographFuelStatus",ex);}} if(!data.RefuelActive&&_lastRefuelActive&&!_refuelDialogOpen){var liters=Math.Max(data.RefuelAmountLiters,Math.Max(0,data.FuelLiters-_fuelBefore));if(liters>=0.5f){_fuelAfter=data.FuelLiters;_fuelOdometer=data.OdometerKm;_pendingRefuelTelemetry=data;_pendingRefuelLiters=liters;EnsurePendingRefuelIdentity(data,liters);_refuelDialogOpen=true;_fuelingCandidate=false;_ = Dispatcher.BeginInvoke(new Action(()=>{try{ShowFuelPaymentModalC();}finally{_refuelDialogOpen=false;}}),DispatcherPriority.Normal);}} if(data.RefuelPayed&&!_lastRefuelPayed&&data.RefuelAmountLiters>=0.5f&&!_refuelDialogOpen){_pendingRefuelTelemetry=data;_pendingRefuelLiters=data.RefuelAmountLiters;EnsurePendingRefuelIdentity(data,data.RefuelAmountLiters);_refuelDialogOpen=true;_ = Dispatcher.BeginInvoke(new Action(()=>{try{ShowFuelPaymentModalC();}finally{_refuelDialogOpen=false;}}),DispatcherPriority.Normal);}
        _lastRefuelPayed=data.RefuelPayed;_lastRefuelActive=data.RefuelActive;
        if(!data.RefuelPayed) DetectAutomaticRefueling(data);_lastOdometer=data.OdometerKm;UpdateOperationsAlert(data);}catch(Exception ex){App.WriteUiCrashLog("Operations.PollTelemetry",ex);}return Task.CompletedTask;}
    private void DetectAutomaticRefueling(TelemetrySnapshot data)
    {
        var now = DateTime.UtcNow;
        var fuel = data.FuelLiters;
        var stopped = Math.Abs(data.SpeedKph) < 0.5f;

        if (!_refuelBaselineInitialized || !_lastFuelLiters.HasValue)
        {
            _refuelBaselineFuel = fuel;
            _lastFuelLiters = fuel;
            _refuelBaselineInitialized = true;
            return;
        }

        var delta = fuel - _lastFuelLiters.Value;

        if (!_fuelingCandidate)
        {
            // Não usamos mais um baseline antigo: depois de rodar, o caminhão pode
            // consumir combustível e depois voltar exatamente ao nível anterior.
            // O abastecimento precisa ser detectado pelo aumento entre amostras.
            if (stopped && delta >= 0.5f && !_refuelDialogOpen &&
                now - _lastRefuelDetectedAt >= TimeSpan.FromSeconds(5))
            {
                _fuelingCandidate = true;
                _fuelBefore = _lastFuelLiters.Value;
                _fuelPeak = fuel;
                _fuelOdometer = data.OdometerKm;
                _fuelStableTicks = 0;
            }
        }
        else
        {
            if (!stopped)
            {
                _refuelBaselineFuel = fuel;
                ResetFuelingCandidate();
            }
            else
            {
                if (fuel > _fuelPeak + 0.2f)
                {
                    _fuelPeak = fuel;
                    _fuelStableTicks = 0;
                }
                else
                {
                    _fuelStableTicks++;
                }

                var liters = _fuelPeak - _fuelBefore;
                if (_fuelStableTicks >= 2 && liters >= 1f && !_refuelDialogOpen)
                {
                    _fuelAfter = _fuelPeak;
                    _fuelingCandidate = false;
                    _fuelStableTicks = 0;
                    _lastRefuelDetectedAt = now;
                    _refuelBaselineFuel = _fuelPeak;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { RegisterDetectedRefueling(data, liters); }
                        finally { _refuelDialogOpen = false; }
                    }), DispatcherPriority.Normal);
                }
            }
        }

        _lastFuelLiters = fuel;
    }

    private void ResetFuelingCandidate(){_fuelingCandidate=false;_fuelStableTicks=0;_fuelPeak=0;}
    private void RegisterDetectedRefueling(TelemetrySnapshot data,float liters)
    {
        _pendingRefuelTelemetry=data;_pendingRefuelLiters=liters;EnsurePendingRefuelIdentity(data,liters);
        try { _telemetryOverlay?.ShowEvent($"ABASTECIMENTO DETECTADO • {liters:0.0} L • CONFIRME PARA CARIMBAR A NOTA"); }
        catch (Exception ex) { App.WriteUiCrashLog("Fuel.ShowDetectedOverlay", ex); }
        ShowFuelPaymentModalC();
    }
    private void UpdateOperationsAlert(TelemetrySnapshot data){
        if(_garageUnauthorized){AlertText.Text=string.IsNullOrWhiteSpace(_garageMessage)?"🔒 CAMINHÃO NÃO AUTORIZADO NA GARAGEM":_garageMessage;AlertText.Foreground=FindResource("Yellow") as System.Windows.Media.Brush;FuelAutoText.Text=$"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";return;}
        if(_truckLocked){AlertText.Text=data.EngineEnabled?"Caminhão ligado • desbloqueio necessário":"🔒 CAMINHÃO BLOQUEADO • DESBLOQUEIO NECESSÁRIO";AlertText.Foreground=FindResource("Yellow") as System.Windows.Media.Brush;FuelAutoText.Text=$"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";return;}
        if(data.FuelRangeKm>0&&data.FuelRangeKm<80){AlertText.Text="⛽ AUTONOMIA BAIXA • planeje abastecimento";AlertText.Foreground=FindResource("Yellow") as System.Windows.Media.Brush;}
        else if(_fuelingCandidate){AlertText.Text="⛽ ABASTECIMENTO DETECTADO • aguardando estabilização";AlertText.Foreground=FindResource("Green") as System.Windows.Media.Brush;}
        else{AlertText.Text="Nenhum alerta operacional ativo";AlertText.Foreground=FindResource("Green") as System.Windows.Media.Brush;}
        FuelAutoText.Text=_fuelingCandidate?"Abastecimento automático: detectando uma operação":$"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";
    }
    private void FuelButton_Click(object sender,RoutedEventArgs e){ShowFuelOverviewModal();}
    private void StopsButton_Click(object sender,RoutedEventArgs e){ShowOperationalModal("stop");}
    private void OccurrenceButton_Click(object sender,RoutedEventArgs e){ShowOperationalModal("occurrence");}
    private void DocumentsButton_Click(object sender,RoutedEventArgs e){ShowOperationalModal("document");}
    private void ShowDocumentsHistory(){ShowOperationalModal("document");}
    private void SummaryButton_Click(object sender,RoutedEventArgs e){ShowTripsOperationsCenter();}
    private void HomeButton_Click(object sender,RoutedEventArgs e)=>StatusText.Text="Tablet TransPoli • painel principal";
    private void UpdateOpsCounters(){if(OpsCounterText!=null)OpsCounterText.Text=$"⛽ {_refuelings.Count} abastecimentos  •  🛑 {_stops.Count} paradas  •  ⚠ {_occurrences.Count} ocorrências  •  📄 {_documents.Count} documentos";}
    private bool TrySaveOperations()
    {
        if (string.IsNullOrWhiteSpace(SecureTokenStore.ReadUserId()) || string.IsNullOrWhiteSpace(_operationsPath))
            return false;
        var fileSaved = false;
        try
        {
            var json = JsonSerializer.Serialize(new OperationsState
            {
                NextRefuelingNumber = _nextRefuelingNumber, Refuelings = _refuelings, Stops = _stops, Occurrences = _occurrences, Documents = _documents, PoliPassRecords = _poliPassRecords, PendingRefuelEventId = _pendingRefuelEventId, PendingRefuelDetectedAtUtc = _pendingRefuelDetectedAtUtc, PendingRefuelLiters = _pendingRefuelLiters, PendingRefuelFuelBefore = _fuelBefore, PendingRefuelFuelAfter = _fuelAfter, PendingRefuelOdometerKm = _fuelOdometer, PendingRefuelTruckId = CanonicalTruckIdentity(_pendingRefuelTelemetry), PendingRefuelTruckBrand = _pendingRefuelTelemetry?.TruckBrand ?? "", PendingRefuelTruckModel = _pendingRefuelTelemetry?.TruckModel ?? "", PendingRefuelLicensePlate = _pendingRefuelTelemetry?.LicensePlate ?? ""
            }, new JsonSerializerOptions { WriteIndented = true });
            var tempPath = _operationsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _operationsPath, true);
            fileSaved = true;
        }
        catch (Exception ex) { App.WriteUiCrashLog("Operations.SaveFile", ex); }

        try
        {
            var store = LocalData.Current;
            if (store is null) return fileSaved;
            var repo = new LocalOperationsRepository(store.Db);
            foreach (var item in _refuelings) repo.UpsertRefueling(item, item.TripId);
            foreach (var item in _stops) repo.UpsertOperationalEvent(item.Id, "stop", item.Type, item.Note, item.TripKey, item.SessionKey, item.TripId, "", item.TruckId, item.StartedAtUtc, item.OdometerKm, item.Manual);
            foreach (var item in _occurrences) repo.UpsertOperationalEvent(item.Id, "occurrence", item.Type, item.Details, item.SessionKey, "", item.TripId, "", item.TruckId, item.RecordedAtUtc, item.OdometerKm, true);
            foreach (var item in _documents) repo.UpsertOperationalEvent(item.Id, "document", item.Status, "", item.Reference, item.CargoKey, item.TripId, item.Driver, item.Truck, item.RecordedAtUtc, 0, false);
        }
        catch (Exception ex) { App.WriteUiCrashLog("Operations.SaveDatabase", ex); }
        return fileSaved;
    }

    private void SaveOperations() => _ = TrySaveOperations();

    private static string CanonicalTruckIdentity(TelemetrySnapshot? data)
    {
        if(data is null) return "";
        if(!string.IsNullOrWhiteSpace(data.TruckId)) return data.TruckId.Trim();
        if(!string.IsNullOrWhiteSpace(data.LicensePlate)) return data.LicensePlate.Trim().ToUpperInvariant();
        return "";
    }
    private void LoadOperations(){try{if(string.IsNullOrWhiteSpace(_operationsPath)||!File.Exists(_operationsPath))return;var state=JsonSerializer.Deserialize<OperationsState>(File.ReadAllText(_operationsPath));if(state is null)return;_refuelings.AddRange(state.Refuelings??new());
        var usedNumbers=new HashSet<long>(_refuelings.Where(x=>x.Number>0).Select(x=>x.Number));
        long legacyNumber=1;
        foreach(var item in _refuelings.OrderBy(x=>x.RecordedAtUtc)){if(item.Number<=0){while(usedNumbers.Contains(legacyNumber))legacyNumber++;item.Number=legacyNumber;usedNumbers.Add(legacyNumber);}if(string.IsNullOrWhiteSpace(item.Reference))item.Reference=$"AB-{item.Number:000000}";}
        _nextRefuelingNumber=Math.Max(state.NextRefuelingNumber,_refuelings.Count==0?1:_refuelings.Max(x=>x.Number)+1);
        _pendingRefuelEventId=string.IsNullOrWhiteSpace(state.PendingRefuelEventId)?null:state.PendingRefuelEventId;
        _pendingRefuelDetectedAtUtc=state.PendingRefuelDetectedAtUtc;
        _pendingRefuelLiters=Math.Max(0,state.PendingRefuelLiters);
        if(_pendingRefuelEventId is not null && _pendingRefuelLiters>0)
        {
            _fuelBefore=state.PendingRefuelFuelBefore;
            _fuelAfter=state.PendingRefuelFuelAfter;
            _fuelOdometer=state.PendingRefuelOdometerKm;
            _pendingRefuelTelemetry=new TelemetrySnapshot
            {
                FuelLiters=state.PendingRefuelFuelAfter,
                OdometerKm=state.PendingRefuelOdometerKm,
                TruckId=state.PendingRefuelTruckId,
                TruckBrand=state.PendingRefuelTruckBrand,
                TruckModel=state.PendingRefuelTruckModel,
                LicensePlate=state.PendingRefuelLicensePlate
            };
        }
        _stops.AddRange(state.Stops??new());_occurrences.AddRange(state.Occurrences??new());_documents.AddRange(state.Documents??new());_poliPassRecords.AddRange((state.PoliPassRecords??new())
             .Where(x=>x.EventId>0)
            .GroupBy(x=>$"{x.EventId}:{x.SourceAmount:0.00}:{Math.Round(x.OdometerKm,1):0.0}")
            .Select(g=>g.OrderBy(x=>x.RecordedAtUtc).First()));
        SaveOperations();}catch(Exception ex){App.WriteUiCrashLog("Operations.Load",ex);}}
    private static Window CreateListWindow(string title,string subtitle){var w=new Window{Title=title,Width=650,Height=520,MinWidth=520,MinHeight=380,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=(System.Windows.Media.Brush)Application.Current.FindResource("Bg"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};var root=new StackPanel();root.Children.Add(new TextBlock{Text=title,FontSize=22,FontWeight=FontWeights.Bold,Margin=new Thickness(18,18,18,4)});root.Children.Add(new TextBlock{Text=subtitle,FontSize=11,Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Muted"),Margin=new Thickness(18,0,18,10),TextWrapping=TextWrapping.Wrap});w.Content=root;return w;}
    private static TextBlock Line(string text,double size)=>new(){Text=text,FontSize=size,Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text"),Margin=new Thickness(0,0,0,10),TextWrapping=TextWrapping.Wrap};
    private static string? PromptText(string title,string prompt,string initial){var w=new Window{Title=title,Width=460,Height=210,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=(System.Windows.Media.Brush)Application.Current.FindResource("Bg"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};var root=new StackPanel{Margin=new Thickness(18)};root.Children.Add(new TextBlock{Text=prompt,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});var input=new TextBox{Text=initial,FontSize=15,Padding=new Thickness(8),Background=(System.Windows.Media.Brush)Application.Current.FindResource("Panel2"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};root.Children.Add(input);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};string? result=null;var cancel=new Button{Content="Cancelar",Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,8,0)};var ok=new Button{Content="Registrar",Padding=new Thickness(14,7,14,7)};cancel.Click+=(_,_)=>w.DialogResult=false;ok.Click+=(_,_)=>{result=input.Text.Trim();w.DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(ok);root.Children.Add(buttons);w.Content=root;w.ShowDialog();return string.IsNullOrWhiteSpace(result)?null:result;}
    private static string? Choose(string title,IEnumerable<string> options){var w=new Window{Title=title,Width=460,Height=430,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=(System.Windows.Media.Brush)Application.Current.FindResource("Bg"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};var root=new StackPanel{Margin=new Thickness(18)};string? result=null;foreach(var option in options){var b=new Button{Content=option,Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,0,0,7),HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=(_,_)=>{result=option;w.DialogResult=true;};root.Children.Add(b);}var cancel=new Button{Content="Cancelar",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};cancel.Click+=(_,_)=>w.DialogResult=false;root.Children.Add(cancel);w.Content=root;w.ShowDialog();return result;}
}

public sealed class OperationsState{public long NextRefuelingNumber{get;set;}=1;public List<RefuelingRecord>? Refuelings{get;set;}public List<StopRecord>? Stops{get;set;}public List<OccurrenceRecord>? Occurrences{get;set;}public List<DocumentRecord>? Documents{get;set;}public List<PoliPassRecord>? PoliPassRecords{get;set;}public string? PendingRefuelEventId{get;set;}public DateTime PendingRefuelDetectedAtUtc{get;set;}public float PendingRefuelLiters{get;set;}public float PendingRefuelFuelBefore{get;set;}public float PendingRefuelFuelAfter{get;set;}public float PendingRefuelOdometerKm{get;set;}public string PendingRefuelTruckId{get;set;}="";public string PendingRefuelTruckBrand{get;set;}="";public string PendingRefuelTruckModel{get;set;}="";public string PendingRefuelLicensePlate{get;set;}="" ;}
public sealed class RefuelingRecord{public string Id{get;set;}="";public long Number{get;set;}public string Reference{get;set;}="";public DateTime RecordedAtUtc{get;set;}public string Station{get;set;}="";public string Location{get;set;}="";public float Liters{get;set;}public decimal PricePerLiter{get;set;}public decimal TotalCost{get;set;}public float FuelBefore{get;set;}public float FuelAfter{get;set;}public float OdometerKm{get;set;}public string Truck{get;set;}="";public string LicensePlate{get;set;}="";public string? TripId{get;set;}public string SessionKey{get;set;}="";public string TruckId{get;set;}="";}
public sealed class StopRecord{public string Id{get;set;}="";public string Type{get;set;}="";public string Note{get;set;}="";public DateTime StartedAtUtc{get;set;}public DateTime? EndedAtUtc{get;set;}public float OdometerKm{get;set;}public string TripKey{get;set;}="";public bool Manual{get;set;}public string? TripId{get;set;}public string SessionKey{get;set;}="";public string TruckId{get;set;}="";}
public sealed class OccurrenceRecord{public string Id{get;set;}="";public string Type{get;set;}="";public string Details{get;set;}="";public DateTime RecordedAtUtc{get;set;}public float OdometerKm{get;set;}public string? TripId{get;set;}public string SessionKey{get;set;}="";public string TruckId{get;set;}="";}