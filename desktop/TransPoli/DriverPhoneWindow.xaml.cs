using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public partial class DriverPhoneWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private TelemetrySnapshot? _telemetry;
    private bool _tripActive;
    private float _distanceKm;
    private float _remainingKm;
    private decimal _balance;
    private int _tripCount;
    private double _totalKm;
    private int _documentCount;
    private int _stampedDocumentCount;
    private int? _rankingPosition;

    public DriverPhoneWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
    }

    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive, float distanceKm = 0, float remainingKm = 0)
    {
        _telemetry = data; _tripActive = tripActive; _distanceKm = Math.Max(0, distanceKm); _remainingKm = Math.Max(0, remainingKm);
        PhoneConnectionText.Text = data.Connected ? "●  ETS2 CONECTADO" : "●  ETS2 OFFLINE";
        PhoneConnectionText.Foreground = Brush(data.Connected ? "#4EE59B" : "#929BA7");
        var hasJob = data.OnJob || data.CargoLoaded || !string.IsNullOrWhiteSpace(data.Cargo);
        PhoneTripText.Text = tripActive ? "VIAGEM EM ANDAMENTO" : hasJob ? "CONTRATO ETS2 DETECTADO" : "SEM VIAGEM ATIVA";
        PhoneRouteText.Text = hasJob ? $"{Value(data.SourceCity)} → {Value(data.DestinationCity)}" : "Aguardando contrato";
        PhoneCargoText.Text = $"Carga: {Value(data.Cargo)}";
        PhoneSpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";
        PhoneProgressText.Text = hasJob ? $"{_distanceKm:0} km • {_remainingKm:0} km restantes" : "—";
    }

    public void UpdateOperationalSummary(decimal balance, int tripCount, double totalKm, int documentCount, int stampedDocumentCount, int? rankingPosition)
    {
        _balance=balance; _tripCount=tripCount; _totalKm=totalKm; _documentCount=documentCount; _stampedDocumentCount=stampedDocumentCount; _rankingPosition=rankingPosition;
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var app=b.Tag?.ToString() ?? "TransPoli"; AppTitle.Text=app.ToUpperInvariant(); AppContent.Children.Clear();
        switch(app)
        {
            case "Mensagens": AddHero("CENTRAL TRANSPOLI","Comunicação operacional"); AddState("Nenhuma mensagem local pendente","As mensagens aparecerão aqui somente quando houver uma ocorrência real registrada pelo TransPoli."); break;
            case "Alertas": AddHero("ALERTAS","Situação da operação"); AddRow("ETS2",_telemetry?.Connected==true?"CONECTADO":"OFFLINE",_telemetry?.Connected==true); AddRow("Viagem",_tripActive?"EM ANDAMENTO":(_telemetry?.OnJob==true?"CONTRATO DETECTADO":"SEM VIAGEM"),_tripActive); break;
            case "Banco": AddHero("BANCO TRANSPOLI","Resumo financeiro real"); AddBig(_balance.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),"SALDO"); AddRow("Viagens liquidadas",_tripCount.ToString(),true); AddRow("Quilometragem consolidada",$"{_totalKm:N0} km",true); break;
            case "Documentos": AddHero("DOCUMENTOS","Arquivo operacional"); AddBig(_documentCount.ToString(),"NOTAS REGISTRADAS"); AddRow("Carimbadas",_stampedDocumentCount.ToString(),_stampedDocumentCount>0); AddRow("Pendentes",Math.Max(0,_documentCount-_stampedDocumentCount).ToString(),_documentCount==_stampedDocumentCount); break;
            case "Viagens": AddHero("VIAGEM ATUAL","Telemetria ETS2"); AddRow("Rota",$"{Value(_telemetry?.SourceCity)} → {Value(_telemetry?.DestinationCity)}",_tripActive); AddRow("Carga",Value(_telemetry?.Cargo),_tripActive); AddRow("Percorrido",$"{_distanceKm:0.0} km",_tripActive); AddRow("Restante",$"{_remainingKm:0.0} km",_tripActive); break;
            case "Ranking": AddHero("RANKING","Desempenho do motorista"); AddBig(_rankingPosition.HasValue?$"#{_rankingPosition}":"—","POSIÇÃO CONHECIDA"); AddRow("Viagens",_tripCount.ToString(),true); AddRow("KM",$"{_totalKm:N0} km",true); break;
            case "Perfil": AddHero("PERFIL DO MOTORISTA","Sessão TransPoli"); AddState("Perfil conectado ao computador de bordo","Dados pessoais continuam protegidos no aplicativo principal; o celular funciona como extensão operacional."); break;
            case "Garagem": AddHero("GARAGEM","Veículo em uso"); AddRow("Caminhão",$"{Value(_telemetry?.TruckBrand)} {Value(_telemetry?.TruckModel)}".Trim(),_telemetry?.Connected==true); AddRow("Odômetro",$"{_telemetry?.OdometerKm ?? 0:0.0} km",true); break;
            default: AddHero("AJUSTES","Celular TransPoli"); AddRow("Atalho","F9",true); AddRow("HUD","F11",true); AddRow("Tablet","F10",true); break;
        }
        AppPanel.Visibility=Visibility.Visible;
    }

    private void Back_Click(object sender,RoutedEventArgs e)=>AppPanel.Visibility=Visibility.Collapsed;
    private void AddHero(string title,string sub){AppContent.Children.Add(new TextBlock{Text=title,Foreground=Brush("#FFE08A"),FontSize=11,FontWeight=FontWeights.Bold,Margin=new Thickness(0,8,0,4)});AppContent.Children.Add(new TextBlock{Text=sub,Foreground=Brush("#F7F8FA"),FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,14)});}
    private void AddBig(string value,string label){var s=new StackPanel();s.Children.Add(new TextBlock{Text=value,Foreground=Brush("#F7F8FA"),FontSize=26,FontWeight=FontWeights.Bold});s.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=9});AppContent.Children.Add(Card(s));}
    private void AddRow(string label,string value,bool ok){var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});g.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=10});var v=new TextBlock{Text=value,Foreground=Brush(ok?"#4EE59B":"#FFE08A"),FontSize=11,FontWeight=FontWeights.SemiBold};Grid.SetColumn(v,1);g.Children.Add(v);AppContent.Children.Add(Card(g));}
    private void AddState(string title,string body){var s=new StackPanel();s.Children.Add(new TextBlock{Text=title,Foreground=Brush("#F7F8FA"),FontSize=13,FontWeight=FontWeights.SemiBold});s.Children.Add(new TextBlock{Text=body,Foreground=Brush("#929BA7"),FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});AppContent.Children.Add(Card(s));}
    private static Border Card(UIElement child)=>new(){Background=Brush("#10161D"),BorderBrush=Brush("#27313B"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(12),Margin=new Thickness(0,0,0,8),Child=child};
    private static SolidColorBrush Brush(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static string Value(string? value)=>string.IsNullOrWhiteSpace(value)?"—":value;
}