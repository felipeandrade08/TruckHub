using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
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
    private readonly List<PhoneLedgerItem> _ledger = new();
    private readonly List<PhoneDocumentItem> _documents = new();
    private readonly List<PhoneTripItem> _trips = new();
    private decimal _rankingRevenue;
    private decimal _rankingRate;
    private readonly List<PhoneNotificationItem> _notifications = new();
    private string _profileSession = "PERFIL LOCAL";
    private string _profileTruck = "—";
    private string _profilePlate = "—";

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
        var total = _distanceKm + _remainingKm;
        var progress = total > 0 ? Math.Clamp(_distanceKm / total, 0f, 1f) : 0f;
        PhoneProgressBar.Width = 324 * progress;
        PhoneTripDot.Fill = Brush(tripActive ? "#4EE59B" : hasJob ? "#FFE08A" : "#697480");
    }

    public void UpdateOperationalSummary(decimal balance, int tripCount, double totalKm, int documentCount, int stampedDocumentCount, int? rankingPosition)
    {
        _balance=balance; _tripCount=tripCount; _totalKm=totalKm; _documentCount=documentCount; _stampedDocumentCount=stampedDocumentCount; _rankingPosition=rankingPosition;
    }

    public void UpdateBankHistory(IEnumerable<PhoneLedgerItem> items)
    {
        _ledger.Clear(); _ledger.AddRange(items.Take(20));
    }

    public void UpdateDocumentHistory(IEnumerable<PhoneDocumentItem> items)
    {
        _documents.Clear(); _documents.AddRange(items.Take(20));
        _documentCount=_documents.Count; _stampedDocumentCount=_documents.Count(x=>x.Stamped);
    }

    public void UpdateTripHistory(IEnumerable<PhoneTripItem> items)
    {
        _trips.Clear(); _trips.AddRange(items.Take(20));
    }

    public void UpdateRankingSummary(int? position, decimal revenue, decimal rate)
    {
        _rankingPosition=position; _rankingRevenue=revenue; _rankingRate=rate;
    }

    public void UpdateNotifications(IEnumerable<PhoneNotificationItem> items)
    {
        _notifications.Clear(); _notifications.AddRange(items.OrderByDescending(x=>x.Priority).ThenByDescending(x=>x.When).Take(20));
        var critical=_notifications.Count(x=>x.Priority==2); var attention=_notifications.Count(x=>x.Priority==1);
        AlertsButton.Content=_notifications.Count>0?$"●  ALERTAS  {_notifications.Count}\nOperação":"●  ALERTAS\nOperação";
        AlertsButton.Foreground=Brush(critical>0?"#FF6262":attention>0?"#FFE08A":"#F7F8FA");
        MessagesButton.Content="●  MENSAGENS\nCentral";
    }

    public void UpdateProfile(string session, string truck, string plate)
    {
        _profileSession=string.IsNullOrWhiteSpace(session)?"PERFIL LOCAL":session;
        _profileTruck=Value(truck); _profilePlate=Value(plate);
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var app=b.Tag?.ToString() ?? "TransPoli"; AppTitle.Text=app.ToUpperInvariant(); AppContent.Children.Clear(); ApplyAppIdentity(app);
        switch(app)
        {
            case "Mensagens":
                AddHero("MENSAGENS","Comunicação TransPoli");
                AddState("Nenhuma conversa registrada","O desktop ainda não possui uma fonte real de mensagens entre motorista e central. O celular não cria conversas fictícias.");
                break;
            case "Alertas":
                AddHero("ALERTAS","Central operacional");
                AddRow("ETS2",_telemetry?.Connected==true?"CONECTADO":"OFFLINE",_telemetry?.Connected==true);
                AddRow("Viagem",_tripActive?"EM ANDAMENTO":(_telemetry?.OnJob==true?"CONTRATO DETECTADO":"SEM VIAGEM"),_tripActive);
                AddSection("EVENTOS ATIVOS");
                if(_notifications.Count==0) AddState("Tudo em ordem","Não existem alertas operacionais ativos.");
                foreach(var item in _notifications) AddNotification(item);
                break;
            case "Banco":
                AddHero("BANCO TRANSPOLI","Saldo e extrato local");
                AddBig(_balance.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),"SALDO");
                AddRow("Viagens liquidadas",_tripCount.ToString(),true); AddRow("Quilometragem",$"{_totalKm:N0} km",true);
                AddSection("ÚLTIMAS MOVIMENTAÇÕES");
                if(_ledger.Count==0) AddState("Extrato vazio","Ainda não existem movimentações financeiras registradas.");
                foreach(var item in _ledger) AddTransaction(item);
                break;
            case "Documentos":
                AddHero("DOCUMENTOS","Arquivo de notas da operação");
                AddBig(_documentCount.ToString(),"NOTAS REGISTRADAS"); AddRow("Carimbadas",_stampedDocumentCount.ToString(),_stampedDocumentCount>0); AddRow("Pendentes",Math.Max(0,_documentCount-_stampedDocumentCount).ToString(),_documentCount==_stampedDocumentCount);
                AddSection("HISTÓRICO");
                if(_documents.Count==0) AddState("Nenhuma nota registrada","As notas emitidas no computador de bordo aparecerão aqui.");
                foreach(var item in _documents) AddDocument(item);
                break;
            case "Viagens":
                AddHero("VIAGENS","Operação e histórico");
                AddSection("VIAGEM ATUAL");
                AddRow("Rota",$"{Value(_telemetry?.SourceCity)} → {Value(_telemetry?.DestinationCity)}",_tripActive);
                AddRow("Carga",Value(_telemetry?.Cargo),_tripActive); AddRow("Percorrido",$"{_distanceKm:0.0} km",_tripActive); AddRow("Restante",$"{_remainingKm:0.0} km",_tripActive);
                AddSection("ÚLTIMAS CONCLUÍDAS");
                if(_trips.Count==0) AddState("Sem viagens concluídas","As entregas liquidadas no TransPoli aparecerão aqui.");
                foreach(var item in _trips) AddTrip(item);
                break;
            case "Ranking":
                AddHero("RANKING","Desempenho do motorista");
                AddBig(_rankingPosition.HasValue && _rankingPosition>0?$"#{_rankingPosition}":"LOCAL","POSIÇÃO");
                AddRow("Viagens",_tripCount.ToString(),true); AddRow("KM",$"{_totalKm:N0} km",true); AddRow("R$/km",$"R$ {_rankingRate:N2}",true); AddRow("Total recebido",_rankingRevenue.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),true);
                break;
            case "Perfil":
                AddHero("PERFIL DO MOTORISTA","Identidade operacional");
                AddBig(_profileSession,"SESSÃO");
                AddRow("Caminhão",_profileTruck,_telemetry?.Connected==true); AddRow("Placa",_profilePlate,!string.IsNullOrWhiteSpace(_profilePlate)&&_profilePlate!="—");
                AddRow("Viagens",_tripCount.ToString(),true); AddRow("KM consolidado",$"{_totalKm:N0} km",true); AddRow("Ranking",_rankingPosition.HasValue?$"#{_rankingPosition}":"LOCAL",true);
                break;
            case "Garagem": AddHero("GARAGEM","Veículo em uso"); AddRow("Caminhão",$"{Value(_telemetry?.TruckBrand)} {Value(_telemetry?.TruckModel)}".Trim(),_telemetry?.Connected==true); AddRow("Odômetro",$"{_telemetry?.OdometerKm ?? 0:0.0} km",true); break;
            default: AddHero("AJUSTES","Celular TransPoli"); AddRow("Atalho","F9",true); AddRow("HUD","F11",true); AddRow("Tablet","F10",true); break;
        }
        AppPanel.Visibility=Visibility.Visible;
    }

    private void ApplyAppIdentity(string app)
    {
        var accent=app switch
        {
            "Banco"=>"#4EE59B","Documentos"=>"#67B7FF","Viagens"=>"#FFE08A","Ranking"=>"#D7B85A",
            "Alertas"=>_notifications.Any(x=>x.Priority==2)?"#FF6262":"#FFE08A","Perfil"=>"#9BC7FF",
            "Garagem"=>"#B5C0CB","Mensagens"=>"#8FA8FF",_=>"#929BA7"
        };
        AppTitle.Foreground=Brush(accent);
        AppPanel.BorderBrush=Brush(accent);
    }

    private void Back_Click(object sender,RoutedEventArgs e)
    {
        AppPanel.Visibility=Visibility.Collapsed;
        AppPanel.BorderBrush=Brush("#303B46");
        AppTitle.Foreground=Brush("#F7F8FA");
    }
    private void AddNotification(PhoneNotificationItem item)
    {
        var s=new StackPanel(); var label=item.Priority==2?"CRÍTICO":item.Priority==1?"ATENÇÃO":"INFO"; var color=item.Priority==2?"#FF6262":item.Priority==1?"#FFE08A":"#4EE59B";
        s.Children.Add(new TextBlock{Text=$"{label} • {item.Title}",Foreground=Brush(color),FontSize=11,FontWeight=FontWeights.Bold});
        s.Children.Add(new TextBlock{Text=item.Message,Foreground=Brush("#F7F8FA"),FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
        s.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM HH:mm"),Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,5,0,0)});AppContent.Children.Add(Card(s));
    }
    private void AddTrip(PhoneTripItem item)
    {
        var s=new StackPanel(); s.Children.Add(new TextBlock{Text=item.Cargo,Foreground=Brush("#F7F8FA"),FontSize=12,FontWeight=FontWeights.Bold,TextWrapping=TextWrapping.Wrap});
        s.Children.Add(new TextBlock{Text=$"{item.Origin} → {item.Destination}",Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,4,0,7),TextWrapping=TextWrapping.Wrap});
        var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition());
        void Cell(string text,int col,bool gold=false){var t=new TextBlock{Text=text,Foreground=Brush(gold?"#FFE08A":"#F7F8FA"),FontSize=9,FontWeight=FontWeights.SemiBold,TextAlignment=col==2?TextAlignment.Right:TextAlignment.Left};Grid.SetColumn(t,col);g.Children.Add(t);}
        Cell($"{item.DistanceKm:N0} km",0);Cell($"R$ {item.RatePerKm:N2}/km",1,true);Cell(item.Gross.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),2);s.Children.Add(g);
        s.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,7,0,0)});AppContent.Children.Add(Card(s));
    }
    private void AddSection(string title)=>AppContent.Children.Add(new TextBlock{Text=title,Foreground=Brush("#929BA7"),FontSize=9,FontWeight=FontWeights.Bold,Margin=new Thickness(2,12,0,7)});
    private void AddTransaction(PhoneLedgerItem item)
    {
        var g=new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var left=new StackPanel(); left.Children.Add(new TextBlock{Text=item.Description,Foreground=Brush("#F7F8FA"),FontSize=11,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap}); left.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM • HH:mm"),Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,3,0,0)}); g.Children.Add(left);
        var amount=new TextBlock{Text=item.Amount.ToString("+ R$ #,##0.00;- R$ #,##0.00;R$ 0.00",CultureInfo.GetCultureInfo("pt-BR")),Foreground=Brush(item.Amount>=0?"#4EE59B":"#FF6262"),FontSize=11,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(amount,1); g.Children.Add(amount); AppContent.Children.Add(Card(g));
    }
    private void AddDocument(PhoneDocumentItem item)
    {
        var s=new StackPanel(); var h=new Grid(); h.ColumnDefinitions.Add(new ColumnDefinition());h.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        h.Children.Add(new TextBlock{Text=string.IsNullOrWhiteSpace(item.Reference)?"NF sem número":item.Reference,Foreground=Brush("#F7F8FA"),FontSize=12,FontWeight=FontWeights.Bold});
        var st=new TextBlock{Text=item.Stamped?"CARIMBADA":"EMITIDA",Foreground=Brush(item.Stamped?"#4EE59B":"#FFE08A"),FontSize=9,FontWeight=FontWeights.Bold};Grid.SetColumn(st,1);h.Children.Add(st);s.Children.Add(h);
        s.Children.Add(new TextBlock{Text=item.Cargo,Foreground=Brush("#F7F8FA"),FontSize=10,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap});s.Children.Add(new TextBlock{Text=item.Route,Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap});s.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,5,0,0)});AppContent.Children.Add(Card(s));
    }
    private void AddHero(string title,string sub)
    {
        var shell=new Border{Background=Brush("#0E141A"),BorderBrush=AppPanel.BorderBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(17),Padding=new Thickness(14),Margin=new Thickness(0,7,0,13)};
        var s=new StackPanel();s.Children.Add(new TextBlock{Text=title,Foreground=AppTitle.Foreground,FontSize=10,FontWeight=FontWeights.Bold});s.Children.Add(new TextBlock{Text=sub,Foreground=Brush("#F7F8FA"),FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap});shell.Child=s;AppContent.Children.Add(shell);
    }
    private void AddBig(string value,string label){var s=new StackPanel();s.Children.Add(new TextBlock{Text=value,Foreground=Brush("#F7F8FA"),FontSize=26,FontWeight=FontWeights.Bold});s.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=9});AppContent.Children.Add(Card(s));}
    private void AddRow(string label,string value,bool ok){var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});g.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=10});var v=new TextBlock{Text=value,Foreground=Brush(ok?"#4EE59B":"#FFE08A"),FontSize=11,FontWeight=FontWeights.SemiBold};Grid.SetColumn(v,1);g.Children.Add(v);AppContent.Children.Add(Card(g));}
    private void AddState(string title,string body){var s=new StackPanel();s.Children.Add(new TextBlock{Text=title,Foreground=Brush("#F7F8FA"),FontSize=13,FontWeight=FontWeights.SemiBold});s.Children.Add(new TextBlock{Text=body,Foreground=Brush("#929BA7"),FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});AppContent.Children.Add(Card(s));}
    private static Border Card(UIElement child)=>new(){Background=Brush("#10161D"),BorderBrush=Brush("#27313B"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(13),Padding=new Thickness(12),Margin=new Thickness(0,0,0,8),Child=child};
    private static SolidColorBrush Brush(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static string Value(string? value)=>string.IsNullOrWhiteSpace(value)?"—":value;
}

public sealed record PhoneLedgerItem(string Description, decimal Amount, DateTime When);
public sealed record PhoneDocumentItem(string Reference, string Cargo, string Route, bool Stamped, DateTime When);
public sealed record PhoneTripItem(string Cargo, string Origin, string Destination, double DistanceKm, decimal RatePerKm, decimal Gross, DateTime When);
public sealed record PhoneNotificationItem(string Title, string Message, int Priority, DateTime When);