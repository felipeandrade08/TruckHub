using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private string _operationsPath = string.Empty;
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
    private bool _refuelDialogOpen;\n    private bool _lastRefuelPayed;

    protected override void OnInitialized(EventArgs e){base.OnInitialized(e);InitTransPoliOperations();}
    private void InitTransPoliOperations(){var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransPoli");Directory.CreateDirectory(folder);_operationsPath=Path.Combine(folder,"transpoli-operations.json");LoadOperations();_opsTimer.Tick+=async (_,_)=>await PollOperationalTelemetry();_opsTimer.Start();UpdateOpsCounters();}
    private async Task PollOperationalTelemetry(){try{using var response=await _opsHttp.GetAsync(TelemetryUrl);if(!response.IsSuccessStatusCode)return;await using var stream=await response.Content.ReadAsStreamAsync();var data=await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});if(data is null||!data.Connected)return;\n        if(data.RefuelPayed&&!_lastRefuelPayed&&data.RefuelAmountLiters>=0.5f&&!_refuelDialogOpen){_fuelBefore=Math.Max(0,data.FuelLiters-data.RefuelAmountLiters);_fuelAfter=data.FuelLiters;_fuelOdometer=data.OdometerKm;_pendingRefuelTelemetry=data;_pendingRefuelLiters=data.RefuelAmountLiters;_refuelDialogOpen=true;_fuelingCandidate=false;Dispatcher.BeginInvoke(new Action(()=>{try{ShowFuelPaymentModalC();}finally{_refuelDialogOpen=false;}}),DispatcherPriority.Normal);}\n        _lastRefuelPayed=data.RefuelPayed;\n        if(!data.RefuelPayed) DetectAutomaticRefueling(data);_lastOdometer=data.OdometerKm;UpdateOperationsAlert(data);}catch{}}
    private void DetectAutomaticRefueling(TelemetrySnapshot data){var now=DateTime.UtcNow;var fuel=data.FuelLiters;var stopped=Math.Abs(data.SpeedKph)<0.5f;if(!_refuelBaselineInitialized){_refuelBaselineFuel=fuel;_refuelBaselineInitialized=true;_lastFuelLiters=fuel;return;}var increaseFromBaseline=fuel-_refuelBaselineFuel;if(!_fuelingCandidate){if(stopped&&!_refuelDialogOpen&&increaseFromBaseline>=4f&&now-_lastRefuelDetectedAt>=TimeSpan.FromSeconds(30)){_fuelingCandidate=true;_fuelBefore=_refuelBaselineFuel;_fuelPeak=fuel;_fuelOdometer=data.OdometerKm;_fuelStableTicks=0;}}else{if(!stopped){_refuelBaselineFuel=fuel;ResetFuelingCandidate();}else if(fuel>_fuelPeak+0.2f){_fuelPeak=fuel;_fuelStableTicks=0;}else _fuelStableTicks++;var liters=_fuelPeak-_fuelBefore;if(_fuelStableTicks>=3&&liters>=3f&&!_refuelDialogOpen){_fuelAfter=_fuelPeak;_fuelingCandidate=false;_fuelStableTicks=0;_lastRefuelDetectedAt=now;_refuelBaselineFuel=_fuelPeak;_refuelDialogOpen=true;Dispatcher.BeginInvoke(new Action(()=>{try{RegisterDetectedRefueling(data,liters);}finally{_refuelDialogOpen=false;}}),DispatcherPriority.Normal);}}_lastFuelLiters=fuel;}
    private void ResetFuelingCandidate(){_fuelingCandidate=false;_fuelStableTicks=0;_fuelPeak=0;}
    private void RegisterDetectedRefueling(TelemetrySnapshot data,float liters){_pendingRefuelTelemetry=data;_pendingRefuelLiters=liters;ShowFuelPaymentModalC();}
    private void UpdateOperationsAlert(TelemetrySnapshot data){
        if(_garageUnauthorized){AlertText.Text=string.IsNullOrWhiteSpace(_garageMessage)?"🔒 CAMINHÃO NÃO AUTORIZADO NA GARAGEM":_garageMessage;AlertText.Foreground=FindResource("Yellow") as System.Windows.Media.Brush;FuelAutoText.Text=$"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";return;}
        if(_truckLocked){AlertText.Text=data.EngineEnabled?"Caminhão ligado • desbloqueio necessário":"🔒 CAMINHÃO BLOQUEADO • DESBLOQUEIO NECESSÁRIO";AlertText.Foreground=FindResource("Yellow") as System.Windows.Media.Brush;FuelAutoText.Text=$"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";return;}
        if(data.FuelRangeKm>0&&data.FuelRangeKm<80){AlertText.Text="⛽ AUTONOMIA BAIXA • planeje abastecimento";AlertText.Foreground=FindResource("Yellow") as System.Windows.Media.Brush;}
        else if(_fuelingCandidate){AlertText.Text="⛽ ABASTECIMENTO DETECTADO • aguardando estabilização";AlertText.Foreground=FindResource("Green") as System.Windows.Media.Brush;}
        else{AlertText.Text="Nenhum alerta operacional ativo";AlertText.Foreground=FindResource("Green") as System.Windows.Media.Brush;}
        FuelAutoText.Text=_fuelingCandidate?"Abastecimento automático: detectando uma operação":$"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";
    }
    private void FuelButton_Click(object sender,RoutedEventArgs e){ShowFuelPaymentModalC();}
    private void StopsButton_Click(object sender,RoutedEventArgs e){ShowOperationalModal("stop");}
    private void OccurrenceButton_Click(object sender,RoutedEventArgs e){ShowOperationalModal("occurrence");}
    private void DocumentsButton_Click(object sender,RoutedEventArgs e){ShowOperationalModal("document");}
    private void ShowDocumentsHistory(){ShowOperationalModal("document");}
    private void SummaryButton_Click(object sender,RoutedEventArgs e){ShowTripCenterModal();}
    private void HomeButton_Click(object sender,RoutedEventArgs e)=>StatusText.Text="Tablet TransPoli • painel principal";
    private void UpdateOpsCounters(){if(OpsCounterText!=null)OpsCounterText.Text=$"⛽ {_refuelings.Count} abastecimentos  •  🛑 {_stops.Count} paradas  •  ⚠ {_occurrences.Count} ocorrências  •  📄 {_documents.Count} documentos";}
    private void SaveOperations(){try{File.WriteAllText(_operationsPath,JsonSerializer.Serialize(new OperationsState{Refuelings=_refuelings,Stops=_stops,Occurrences=_occurrences,Documents=_documents},new JsonSerializerOptions{WriteIndented=true}));}catch{}}
    private void LoadOperations(){try{if(!File.Exists(_operationsPath))return;var state=JsonSerializer.Deserialize<OperationsState>(File.ReadAllText(_operationsPath));if(state is null)return;_refuelings.AddRange(state.Refuelings??new());_stops.AddRange(state.Stops??new());_occurrences.AddRange(state.Occurrences??new());_documents.AddRange(state.Documents??new());}catch{}}
    private static Window CreateListWindow(string title,string subtitle){var w=new Window{Title=title,Width=650,Height=520,MinWidth=520,MinHeight=380,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=(System.Windows.Media.Brush)Application.Current.FindResource("Bg"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};var root=new StackPanel();root.Children.Add(new TextBlock{Text=title,FontSize=22,FontWeight=FontWeights.Bold,Margin=new Thickness(18,18,18,4)});root.Children.Add(new TextBlock{Text=subtitle,FontSize=11,Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Muted"),Margin=new Thickness(18,0,18,10),TextWrapping=TextWrapping.Wrap});w.Content=root;return w;}
    private static TextBlock Line(string text,double size)=>new(){Text=text,FontSize=size,Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text"),Margin=new Thickness(0,0,0,10),TextWrapping=TextWrapping.Wrap};
    private static string? PromptText(string title,string prompt,string initial){var w=new Window{Title=title,Width=460,Height=210,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=(System.Windows.Media.Brush)Application.Current.FindResource("Bg"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};var root=new StackPanel{Margin=new Thickness(18)};root.Children.Add(new TextBlock{Text=prompt,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,10)});var input=new TextBox{Text=initial,FontSize=15,Padding=new Thickness(8),Background=(System.Windows.Media.Brush)Application.Current.FindResource("Panel2"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};root.Children.Add(input);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,15,0,0)};string? result=null;var cancel=new Button{Content="Cancelar",Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,0,8,0)};var ok=new Button{Content="Registrar",Padding=new Thickness(14,7,14,7)};cancel.Click+=(_,_)=>w.DialogResult=false;ok.Click+=(_,_)=>{result=input.Text.Trim();w.DialogResult=true;};buttons.Children.Add(cancel);buttons.Children.Add(ok);root.Children.Add(buttons);w.Content=root;w.ShowDialog();return string.IsNullOrWhiteSpace(result)?null:result;}
    private static string? Choose(string title,IEnumerable<string> options){var w=new Window{Title=title,Width=460,Height=430,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=(System.Windows.Media.Brush)Application.Current.FindResource("Bg"),Foreground=(System.Windows.Media.Brush)Application.Current.FindResource("Text")};var root=new StackPanel{Margin=new Thickness(18)};string? result=null;foreach(var option in options){var b=new Button{Content=option,Padding=new Thickness(12,9,12,9),Margin=new Thickness(0,0,0,7),HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=(_,_)=>{result=option;w.DialogResult=true;};root.Children.Add(b);}var cancel=new Button{Content="Cancelar",Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,8,0,0)};cancel.Click+=(_,_)=>w.DialogResult=false;root.Children.Add(cancel);w.Content=root;w.ShowDialog();return result;}
}

public sealed class OperationsState{public List<RefuelingRecord>? Refuelings{get;set;}public List<StopRecord>? Stops{get;set;}public List<OccurrenceRecord>? Occurrences{get;set;}public List<DocumentRecord>? Documents{get;set;}}
public sealed class RefuelingRecord{public string Id{get;set;}="";public DateTime RecordedAtUtc{get;set;}public string Station{get;set;}="";public string Location{get;set;}="";public float Liters{get;set;}public float FuelBefore{get;set;}public float FuelAfter{get;set;}public float OdometerKm{get;set;}public string Truck{get;set;}="";public string LicensePlate{get;set;}="";}
public sealed class StopRecord{public string Id{get;set;}="";public string Type{get;set;}="";public string Note{get;set;}="";public DateTime StartedAtUtc{get;set;}public DateTime? EndedAtUtc{get;set;}public float OdometerKm{get;set;}}
public sealed class OccurrenceRecord{public string Id{get;set;}="";public string Type{get;set;}="";public string Details{get;set;}="";public DateTime RecordedAtUtc{get;set;}public float OdometerKm{get;set;}}