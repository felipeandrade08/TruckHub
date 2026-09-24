using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Runtime.InteropServices;
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
    private bool _documentGatePending;
    public event EventHandler? StampCurrentInvoiceRequested;
    public event Action<string>? InvoiceViewRequested;
    private Button? _stampButton;
    public event EventHandler? CompleteRefuelRequested;
    public event Action<long>? PoliPassReceiptRequested;
    private float _pendingRefuelLiters;
    private bool _pendingRefuel;
    private RoadCombinationSnapshot _combination = RoadCombinationSnapshot.Empty;
    private readonly List<PhoneTollItem> _tolls = new();
    private System.Windows.Point? _shadeDragStart;
    private readonly DispatcherTimer _islandTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _lastIslandKey = "";

    public DriverPhoneWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        _islandTimer.Tick += (_, _) => { _islandTimer.Stop(); DynamicIslandText.Visibility=Visibility.Collapsed; DynamicIsland.Width=104; };
        Closed += (_, _) => { _clock.Stop(); _islandTimer.Stop(); };
    }

    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive, float distanceKm = 0, float remainingKm = 0)
    {
        _telemetry = data; _tripActive = tripActive; _distanceKm = Math.Max(0, distanceKm); _remainingKm = Math.Max(0, remainingKm);
        PhoneConnectionText.Text = data.Connected ? "ETS2 CONECTADO" : "ETS2 OFFLINE";
        ToolTip = data.Connected ? "ETS2 conectado ao computador de bordo" : "ETS2 offline";
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

    public void UpdateRoadCombination(RoadCombinationSnapshot snapshot) => _combination = snapshot ?? RoadCombinationSnapshot.Empty;

    public void UpdateTollHistory(IEnumerable<PhoneTollItem>? items) { _tolls.Clear(); if(items!=null) _tolls.AddRange(items.Take(30)); }

    public void UpdateRefuelPrompt(bool pending, float liters)
    {
        _pendingRefuel=pending; _pendingRefuelLiters=Math.Max(0,liters);
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
    }

    public void UpdateDocumentGate(bool pending)
    {
        _documentGatePending=pending;
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
        AlertsBadge.Visibility=_notifications.Count>0?Visibility.Visible:Visibility.Collapsed;
        AlertsCountText.Text=_notifications.Count.ToString(CultureInfo.InvariantCulture);
        AlertsButton.Foreground=Brush(critical>0?"#FF6262":attention>0?"#FFE08A":"#F7F8FA");
        if(_notifications.Count>0) { var top=_notifications[0]; ShowIslandEvent($"{top.Priority}|{top.When.ToUniversalTime():O}|{top.Title}|{top.Message}",top.Title); }
    }

    public void ShowIslandEvent(string text) => ShowIslandEvent(text,text);

    private void ShowIslandEvent(string key,string text)
    {
        if(string.IsNullOrWhiteSpace(key)||string.IsNullOrWhiteSpace(text)) return;
        key=key.Trim();
        if(string.Equals(key,_lastIslandKey,StringComparison.Ordinal)) return;
        _lastIslandKey=key; DynamicIslandText.Text=text.Trim().ToUpperInvariant(); DynamicIslandText.Visibility=Visibility.Visible; DynamicIsland.Width=196; _islandTimer.Stop(); _islandTimer.Start();
    }

    public void SetStampResult(bool success, string message)
    {
        if(_stampButton is null) return;
        _stampButton.Content=message;
        _stampButton.IsEnabled=!success;
        _stampButton.Background=Brush(success?"#1E5B45":"#D6A52A");
    }

    public void UpdateProfile(string session, string truck, string plate)
    {
        _profileSession=string.IsNullOrWhiteSpace(session)?"PERFIL LOCAL":session;
        _profileTruck=Value(truck); _profilePlate=Value(plate);
        var driver=Environment.UserName;
        PhoneGreetingText.Text=string.IsNullOrWhiteSpace(driver)?"Boa viagem!":$"Boa viagem, {driver}!";
    }

    private void PhoneStatusBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if(e.ChangedButton!=System.Windows.Input.MouseButton.Left) return;
        _shadeDragStart=e.GetPosition(this); PhoneStatusBar.CaptureMouse(); e.Handled=true;
    }
    private void PhoneStatusBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if(!_shadeDragStart.HasValue || e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed) return;
        var p=e.GetPosition(this); if(p.Y-_shadeDragStart.Value.Y>=34){PhoneStatusBar.ReleaseMouseCapture();_shadeDragStart=null;ShowNotificationShade();}
    }
    private void PhoneStatusBar_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if(!_shadeDragStart.HasValue) return; var p=e.GetPosition(this); var delta=p.Y-_shadeDragStart.Value.Y; PhoneStatusBar.ReleaseMouseCapture(); _shadeDragStart=null;
        if(delta>=12 || e.ClickCount==1) ShowNotificationShade(); e.Handled=true;
    }

    private void CloseNotificationShade_Click(object sender,RoutedEventArgs e)=>NotificationShade.Visibility=Visibility.Collapsed;

    private void ShowNotificationShade()
    {
        NotificationShadeContent.Children.Clear();
        var now=new TextBlock{Text=DateTime.Now.ToString("dddd, dd MMMM • HH:mm",CultureInfo.GetCultureInfo("pt-BR")),Foreground=Brush("#F7F8FA"),FontSize=18,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)};
        NotificationShadeContent.Children.Add(now);
        if(_notifications.Count==0){NotificationShadeContent.Children.Add(Card(new TextBlock{Text="Tudo em ordem • nenhuma notificação operacional ativa.",Foreground=Brush("#AEB7C1"),FontSize=11}));NotificationShade.Visibility=Visibility.Visible;return;}
        AddShadeGroup("CRÍTICAS",_notifications.Where(x=>x.Priority==2),"#FF6262");
        AddShadeGroup("ATENÇÃO",_notifications.Where(x=>x.Priority==1),"#FFE08A");
        AddShadeGroup("INFORMATIVAS",_notifications.Where(x=>x.Priority<=0),"#67B7FF");
        NotificationShade.Visibility=Visibility.Visible;
    }

    private void AddShadeGroup(string title,IEnumerable<PhoneNotificationItem> items,string color)
    {
        var list=items.ToList(); if(list.Count==0)return;
        var header=new Grid{Margin=new Thickness(1,10,1,7)};header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        header.Children.Add(new TextBlock{Text=title,Foreground=Brush(color),FontSize=9,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center});
        var count=new Border{Background=Brush("#141A20"),CornerRadius=new CornerRadius(9),Padding=new Thickness(7,2,7,2)};count.Child=new TextBlock{Text=list.Count.ToString(CultureInfo.InvariantCulture),Foreground=Brush(color),FontSize=8,FontWeight=FontWeights.Bold};Grid.SetColumn(count,1);header.Children.Add(count);NotificationShadeContent.Children.Add(header);
        foreach(var item in list) AddShadeNotification(item,color);
    }

    private void AddShadeNotification(PhoneNotificationItem item,string color)
    {
        var shell=new Border{Background=Brush("#E6141A20"),BorderBrush=Brush("#27313B"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(12),Margin=new Thickness(0,0,0,7)};
        var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(4)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        var rail=new Border{Background=Brush(color),CornerRadius=new CornerRadius(2),Margin=new Thickness(0,1,0,1)};g.Children.Add(rail);
        var s=new StackPanel{Margin=new Thickness(11,0,0,0)};Grid.SetColumn(s,1);
        var h=new Grid();h.ColumnDefinitions.Add(new ColumnDefinition());h.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        h.Children.Add(new TextBlock{Text=item.Title,Foreground=Brush("#F7F8FA"),FontSize=11,FontWeight=FontWeights.Bold});
        var tm=new TextBlock{Text=item.When.ToLocalTime().ToString("HH:mm"),Foreground=Brush("#7E8994"),FontSize=8};Grid.SetColumn(tm,1);h.Children.Add(tm);s.Children.Add(h);
        s.Children.Add(new TextBlock{Text=item.Message,Foreground=Brush("#B9C1C9"),FontSize=9,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,5,0,0)});
        g.Children.Add(s);shell.Child=g;NotificationShadeContent.Children.Add(shell);
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        OpenApp(b.Tag?.ToString() ?? "TransPoli");
    }

    private void Dock_Click(object sender, RoutedEventArgs e)
    {
        if(sender is not Button b) return;
        var app=b.Tag?.ToString() ?? "Home";
        if(app=="Home"){ CloseApp(); return; }
        OpenApp(app);
    }

    private void OpenApp(string app)
    {
        AppTitle.Text=app.ToUpperInvariant(); AppContent.Children.Clear(); ApplyAppIdentity(app);
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
                AddBig(_balance.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),"SALDO DISPONÍVEL");
                AddMetricPair("VIAGENS LIQUIDADAS",_tripCount.ToString(),"KM CONSOLIDADOS",$"{_totalKm:N0}");
                AddSection("ÚLTIMAS MOVIMENTAÇÕES");
                if(_ledger.Count==0) AddState("Extrato vazio","Ainda não existem movimentações financeiras registradas.");
                foreach(var item in _ledger) AddTransaction(item);
                break;
            case "Documentos":
                AddHero("DOCUMENTOS","Arquivo de notas da operação");
                AddBig(_documentCount.ToString(),"NOTAS REGISTRADAS"); AddMetricPair("CARIMBADAS",_stampedDocumentCount.ToString(),"PENDENTES",Math.Max(0,_documentCount-_stampedDocumentCount).ToString());
                if(_documentGatePending)
                {
                    AddState("LIBERAÇÃO OBRIGATÓRIA","A carga foi detectada. O freio de estacionamento permanece aplicado até o carimbo da nota atual.");
                    var stamp=new Button{Content="CARIMBAR NOTA E LIBERAR VIAGEM",Height=46,Margin=new Thickness(0,0,0,10),Background=Brush("#D6A52A"),Foreground=Brush("#07090C"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold,Cursor=System.Windows.Input.Cursors.Hand};
                    _stampButton=stamp;
                    stamp.Click+=(_,__)=>{stamp.IsEnabled=false;stamp.Content="PROCESSANDO CARIMBO...";StampCurrentInvoiceRequested?.Invoke(this,EventArgs.Empty);};
                    AppContent.Children.Add(stamp);
                }
                AddSection("NOTAS DA CARGA • DANFE");
                if(_documents.Count==0) AddState("Nenhuma nota registrada","As DANFEs emitidas pelo computador de bordo aparecerão aqui e poderão ser reabertas após o carimbo.");
                foreach(var item in _documents) AddDocument(item);
                AddSection("COMPROVANTES POLIPASS");
                if(_tolls.Count==0) AddState("Nenhum comprovante PoliPass","As passagens detectadas pelo ETS2 aparecerão aqui com acesso ao comprovante do conjunto.");
                foreach(var toll in _tolls.Take(12)) AddPoliPassDocument(toll);
                break;
            case "Viagens":
                AddHero("VIAGENS","Operação e histórico");
                AddSection("VIAGEM ATUAL");
                AddBig(_tripActive?$"{_distanceKm:0} km":"—","PERCORRIDOS");
                AddRow("Rota",$"{Value(_telemetry?.SourceCity)} → {Value(_telemetry?.DestinationCity)}",_tripActive);
                AddRow("Carga",Value(_telemetry?.Cargo),_tripActive); AddRow("Percorrido",$"{_distanceKm:0.0} km",_tripActive); AddRow("Restante",$"{_remainingKm:0.0} km",_tripActive);
                AddSection("ÚLTIMAS CONCLUÍDAS");
                if(_trips.Count==0) AddState("Sem viagens concluídas","As entregas liquidadas no TransPoli aparecerão aqui.");
                foreach(var item in _trips) AddTrip(item);
                break;
            case "Ranking":
                AddHero("RANKING","Desempenho do motorista");
                AddBig(_rankingPosition.HasValue && _rankingPosition>0?$"#{_rankingPosition}":"LOCAL","POSIÇÃO ATUAL");
                AddMetricPair("VIAGENS",_tripCount.ToString(),"KM",$"{_totalKm:N0}");
                AddMetricPair("R$/KM",$"R$ {_rankingRate:N2}","TOTAL RECEBIDO",_rankingRevenue.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")));
                break;
            case "Perfil":
                AddHero("PERFIL DO MOTORISTA","Identidade operacional");
                AddBig(_profileSession,"SESSÃO");
                AddRow("Caminhão",_profileTruck,_telemetry?.Connected==true); AddRow("Placa",_profilePlate,!string.IsNullOrWhiteSpace(_profilePlate)&&_profilePlate!="—");
                AddRow("Viagens",_tripCount.ToString(),true); AddRow("KM consolidado",$"{_totalKm:N0} km",true); AddRow("Ranking",_rankingPosition.HasValue?$"#{_rankingPosition}":"LOCAL",true);
                break;
            case "PoliPass":
                AddHero("POLIPASS","Pedágio inteligente TransPoli");
                if(!_combination.Connected) AddState("Aguardando telemetria","Conecte o ETS2 para identificar o conjunto rodoviário.");
                else { AddBig(_combination.TotalAxleCount.HasValue?$"{_combination.TotalAxleCount} eixos":"Eixos em análise","CONJUNTO ATUAL"); AddState(_combination.HasTrailer?$"{_combination.Trailers.Count} reboque(s) acoplado(s)":"Sem reboque acoplado",$"Carga: {_combination.CargoMassKg/1000f:0.0} t • Caminhão: {_combination.TruckBrand} {_combination.TruckModel}"); }
                if(_tolls.Count==0) AddState("Nenhuma passagem registrada","As próximas passagens detectadas pela telemetria aparecerão aqui.");
                foreach (var toll in _tolls.Take(8))
                {
                    var amountText = toll.Paid
                        ? toll.Amount!.Value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"))
                        : "AGUARDANDO PAGAMENTO";
                    var b = new Button
                    {
                        Content = $"PASSAGEM • {(toll.Paid ? "✓ PAGO" : "PENDENTE")} • {amountText}  •  {toll.When.ToLocalTime():dd/MM HH:mm}  •  {(toll.Paid ? "VER COMPROVANTE" : "AGUARDANDO")}",
                        Height = 42,
                        Margin = new Thickness(0, 0, 0, 6),
                        Background = Brush("#141A20"),
                        Foreground = Brush("#F7F8FA"),
                        BorderBrush = Brush("#27313B"),
                        BorderThickness = new Thickness(1),
                        Tag = toll.EventId
                    };
                    b.IsEnabled = toll.Paid;
                    if (toll.Paid) b.Click += (_, __) => PoliPassReceiptRequested?.Invoke((long)b.Tag);
                    AppContent.Children.Add(b);
                }
                break;
            case "Abastecimento":
                AddHero("ABASTECIMENTO","Confirmação rápida pelo celular");
                if(!_pendingRefuel || _pendingRefuelLiters<=0) AddState("Nenhum abastecimento pendente","Quando a telemetria detectar combustível adicionado, a confirmação aparecerá aqui.");
                else { AddBig($"{_pendingRefuelLiters:0.0} L","LITROS DETECTADOS PELA TELEMETRIA"); AddState("Dados comerciais pendentes","Use a confirmação de abastecimento para informar somente posto e preço, sem alterar os litros detectados."); var b=new Button{Content="ABRIR CONFIRMAÇÃO DE ABASTECIMENTO",Height=46,Background=Brush("#1E5B45"),Foreground=Brush("#F7F8FA"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold}; b.Click+=(_,__)=>CompleteRefuelRequested?.Invoke(this,EventArgs.Empty); AppContent.Children.Add(b); }
                break;
            case "Garagem": AddHero("GARAGEM","Veículo em uso"); AddRow("Caminhão",$"{Value(_telemetry?.TruckBrand)} {Value(_telemetry?.TruckModel)}".Trim(),_telemetry?.Connected==true); AddRow("Odômetro",$"{_telemetry?.OdometerKm ?? 0:0.0} km",true); break;
            default: AddHero("AJUSTES","Celular TransPoli"); AddRow("Atalho","F9",true); AddRow("HUD","F11",true); AddRow("Tablet","F10",true); break;
        }
        AppPanel.Visibility=Visibility.Visible;
        AnimateApp(true);
    }

    private void ApplyAppIdentity(string app)
    {
        var accent=app switch
        {
            "Banco"=>"#4EE59B","Documentos"=>"#67B7FF","Viagens"=>"#FFE08A","Ranking"=>"#D7B85A",
            "Alertas"=>_notifications.Any(x=>x.Priority==2)?"#FF6262":"#FFE08A","Perfil"=>"#9BC7FF",
            "Garagem"=>"#B5C0CB","Mensagens"=>"#8FA8FF","PoliPass"=>"#F2BE2D","Abastecimento"=>"#62D8A5","Ajustes"=>"#B9C1C9",_=>"#929BA7"
        };
        AppTitle.Foreground=Brush(accent);
        AppPanel.BorderBrush=Brush(accent);
    }

    private void Back_Click(object sender,RoutedEventArgs e)=>CloseApp();

    private void CloseApp()
    {
        AnimateApp(false);
        AppPanel.Visibility=Visibility.Collapsed;
        AppPanel.BorderBrush=Brush("#303B46");
        AppTitle.Foreground=Brush("#F7F8FA");
    }

    private void AnimateApp(bool opening)
    {
        AppPanel.RenderTransformOrigin=new Point(.5,.5);
        var group=AppPanel.RenderTransform as TransformGroup;
        if(group is null){group=new TransformGroup();group.Children.Add(new ScaleTransform(1,1));group.Children.Add(new TranslateTransform());AppPanel.RenderTransform=group;}
        var scale=(ScaleTransform)group.Children[0];
        var translate=(TranslateTransform)group.Children[1];
        var duration=TimeSpan.FromMilliseconds(140);
        if(opening)
        {
            AppPanel.Opacity=0;
            scale.ScaleX=.97; scale.ScaleY=.97; translate.Y=10;
            AppPanel.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,duration));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.97,1,duration));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.985,1,duration));
        }
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
    private void AddSection(string title)
    {
        var g=new Grid{Margin=new Thickness(2,15,0,8)};
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});g.ColumnDefinitions.Add(new ColumnDefinition());
        var t=new TextBlock{Text=title,Foreground=Brush("#AAB3BC"),FontSize=9,FontWeight=FontWeights.Bold,CharacterSpacing=65};
        g.Children.Add(t);
        var line=new Border{Height=1,Background=Brush("#27313B"),Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(line,1);g.Children.Add(line);
        AppContent.Children.Add(g);
    }
    private void AddTransaction(PhoneLedgerItem item)
    {
        var g=new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var left=new StackPanel(); left.Children.Add(new TextBlock{Text=item.Description,Foreground=Brush("#F7F8FA"),FontSize=11,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap}); left.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM • HH:mm"),Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,3,0,0)}); g.Children.Add(left);
        var amount=new TextBlock{Text=item.Amount.ToString("+ R$ #,##0.00;- R$ #,##0.00;R$ 0.00",CultureInfo.GetCultureInfo("pt-BR")),Foreground=Brush(item.Amount>=0?"#4EE59B":"#FF6262"),FontSize=11,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(amount,1); g.Children.Add(amount); AppContent.Children.Add(Card(g));
    }
    private void AddPoliPassDocument(PhoneTollItem item)
    {
        var s=new StackPanel();
        var h=new Grid(); h.ColumnDefinitions.Add(new ColumnDefinition()); h.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        h.Children.Add(new TextBlock{Text=$"POLIPASS • PP-{item.EventId:0000000000}",Foreground=Brush("#F7F8FA"),FontSize=11,FontWeight=FontWeights.Bold});
        var paid=new TextBlock{Text=item.Paid?"✓ PAGO":"PENDENTE",Foreground=Brush(item.Paid?"#4EE59B":"#FFE08A"),FontSize=9,FontWeight=FontWeights.Bold}; Grid.SetColumn(paid,1); h.Children.Add(paid); s.Children.Add(h);
        s.Children.Add(new TextBlock{Text=item.AxlesText,Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,5,0,0)});
        s.Children.Add(new TextBlock{Text=$"{item.When.ToLocalTime():dd/MM/yyyy HH:mm} • {(item.Amount.HasValue ? item.Amount.Value.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")) : "SEM TARIFA TRANSPOLI")}",Foreground=Brush("#F7F8FA"),FontSize=10,Margin=new Thickness(0,4,0,0)});
        var view=new Button{Content=item.Paid?"VER COMPROVANTE":"AGUARDANDO PAGAMENTO",Height=38,Margin=new Thickness(0,9,0,0),Background=Brush("#141A20"),Foreground=Brush(item.Paid?"#FFE08A":"#929BA7"),BorderBrush=Brush(item.Paid?"#80631B":"#303B46"),BorderThickness=new Thickness(1),FontWeight=FontWeights.Bold,Cursor=item.Paid?System.Windows.Input.Cursors.Hand:System.Windows.Input.Cursors.Arrow,Tag=item.EventId,IsEnabled=item.Paid};
        if(item.Paid) view.Click+=(_,__)=>PoliPassReceiptRequested?.Invoke(item.EventId); s.Children.Add(view); AppContent.Children.Add(Card(s));
    }

    private void AddDocument(PhoneDocumentItem item)
    {
        var s=new StackPanel(); var h=new Grid(); h.ColumnDefinitions.Add(new ColumnDefinition());h.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        h.Children.Add(new TextBlock{Text=string.IsNullOrWhiteSpace(item.Reference)?"NF sem número":item.Reference,Foreground=Brush("#F7F8FA"),FontSize=12,FontWeight=FontWeights.Bold});
        var st=new TextBlock{Text=item.Stamped?"CARIMBADA":"EMITIDA",Foreground=Brush(item.Stamped?"#4EE59B":"#FFE08A"),FontSize=9,FontWeight=FontWeights.Bold};Grid.SetColumn(st,1);h.Children.Add(st);s.Children.Add(h);
        s.Children.Add(new TextBlock{Text=item.Cargo,Foreground=Brush("#F7F8FA"),FontSize=10,Margin=new Thickness(0,5,0,0),TextWrapping=TextWrapping.Wrap});
        s.Children.Add(new TextBlock{Text=item.Route,Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap});
        s.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,5,0,0)});
        var view=new Button{Content=item.Stamped?"VISUALIZAR NOTA CARIMBADA":"VISUALIZAR DANFE",Height=38,Margin=new Thickness(0,9,0,0),Background=Brush("#141A20"),Foreground=Brush("#FFE08A"),BorderBrush=Brush("#80631B"),BorderThickness=new Thickness(1),FontWeight=FontWeights.Bold,Cursor=System.Windows.Input.Cursors.Hand,Tag=item.Reference};
        view.Click+=(_,__)=>InvoiceViewRequested?.Invoke(item.Reference ?? "");
        s.Children.Add(view); AppContent.Children.Add(Card(s));
    }
    private void AddHero(string title,string sub)
    {
        var shell=new Border{Background=Brush("#111820"),BorderBrush=AppPanel.BorderBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(24),Padding=new Thickness(17),Margin=new Thickness(0,7,0,16)};
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var s=new StackPanel();
        s.Children.Add(new TextBlock{Text=title,Foreground=AppTitle.Foreground,FontSize=10,FontWeight=FontWeights.Bold,CharacterSpacing=80});
        s.Children.Add(new TextBlock{Text=sub,Foreground=Brush("#F7F8FA"),FontSize=21,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,6,12,0),TextWrapping=TextWrapping.Wrap});
        grid.Children.Add(s);
        var mark=new Border{Width=42,Height=42,CornerRadius=new CornerRadius(14),Background=Brush("#0A0E12"),BorderBrush=AppTitle.Foreground,BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Center};
        mark.Child=new TextBlock{Text="TP",Foreground=AppTitle.Foreground,FontSize=11,FontWeight=FontWeights.ExtraBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        Grid.SetColumn(mark,1);grid.Children.Add(mark);shell.Child=grid;AppContent.Children.Add(shell);
    }
    private void AddMetricPair(string label1,string value1,string label2,string value2)
    {
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
        StackPanel Metric(string label,string value){var s=new StackPanel();s.Children.Add(new TextBlock{Text=value,Foreground=Brush("#F7F8FA"),FontSize=17,FontWeight=FontWeights.Bold});s.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,3,0,0)});return s;}
        grid.Children.Add(Metric(label1,value1));var right=Metric(label2,value2);Grid.SetColumn(right,1);grid.Children.Add(right);AppContent.Children.Add(Card(grid));
    }
    private void AddBig(string value,string label){var s=new StackPanel();s.Children.Add(new TextBlock{Text=value,Foreground=Brush("#F7F8FA"),FontSize=26,FontWeight=FontWeights.Bold});s.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=9});AppContent.Children.Add(Card(s));}
    private void AddRow(string label,string value,bool ok){var g=new Grid();g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});g.Children.Add(new TextBlock{Text=label,Foreground=Brush("#929BA7"),FontSize=10});var v=new TextBlock{Text=value,Foreground=Brush(ok?"#4EE59B":"#FFE08A"),FontSize=11,FontWeight=FontWeights.SemiBold};Grid.SetColumn(v,1);g.Children.Add(v);AppContent.Children.Add(Card(g));}
    private void AddState(string title,string body){var s=new StackPanel();s.Children.Add(new TextBlock{Text=title,Foreground=Brush("#F7F8FA"),FontSize=13,FontWeight=FontWeights.SemiBold});s.Children.Add(new TextBlock{Text=body,Foreground=Brush("#929BA7"),FontSize=10,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)});AppContent.Children.Add(Card(s));}
    private static Border Card(UIElement child)=>new(){Background=Brush("#0E151C"),BorderBrush=Brush("#27313B"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(20),Padding=new Thickness(15),Margin=new Thickness(0,0,0,11),Child=child};
    private static SolidColorBrush Brush(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    private static string Value(string? value)=>string.IsNullOrWhiteSpace(value)?"—":value;
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    private void PhoneWindow_Loaded(object sender, RoutedEventArgs e) => ApplyRoundedWindowRegion();

    private void PhoneWindow_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyRoundedWindowRegion();

    private void ApplyRoundedWindowRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var radius = (int)Math.Round(120 * Math.Min(dpi.DpiScaleX, dpi.DpiScaleY));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        SetWindowRgn(hwnd, region, true);
    }

}

public sealed record PhoneLedgerItem(string Description, decimal Amount, DateTime When);
public sealed record PhoneDocumentItem(string Reference, string Cargo, string Route, bool Stamped, DateTime When);
public sealed record PhoneTripItem(string Cargo, string Origin, string Destination, double DistanceKm, decimal RatePerKm, decimal Gross, DateTime When);
public sealed record PhoneNotificationItem(string Title, string Message, int Priority, DateTime When);
public sealed record PhoneTollItem(long EventId, decimal? Amount, DateTime When, string AxlesText)
{
    public bool Paid => Amount.HasValue && Amount.Value > 0;
}
