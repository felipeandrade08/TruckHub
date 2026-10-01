using System;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using TransPoli.Media;

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
    private int _rankingTrips;
    private double _rankingKm;
    private readonly List<PhoneNotificationItem> _notifications = new();
    private string _profileSession = "PERFIL LOCAL";
    private string _profileDriverName = "";
    private bool _officialSession;
    private bool _officialEconomyLoaded;
    private bool _officialTripsLoaded;
    private bool _officialDocumentsLoaded;
    private bool _documentGatePending;
    public event EventHandler? StampCurrentInvoiceRequested;
    private PhoneDocumentItem? _openDocument;
    private Button? _stampButton;
    public event Action<decimal,string,string>? CompleteRefuelRequested;
    private Button? _refuelConfirmButton;
    private PhoneTollItem? _openTollReceipt;
    private float _pendingRefuelLiters;
    private bool _pendingRefuel;
    private RoadCombinationSnapshot _combination = RoadCombinationSnapshot.Empty;
    private readonly List<PhoneTollItem> _tolls = new();
    private readonly List<PhoneRefuelItem> _refuels = new();
    private System.Windows.Point? _shadeDragStart;
    private readonly DispatcherTimer _islandTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private string _lastIslandKey = "";
    private string _homeActionApp = "";
    private MainWindow? _mainWindow;
    private TextBlock? _soundDriveTrack;
    private TextBlock? _soundDriveArtist;
    private TextBlock? _soundDriveState;
    private ProgressBar? _soundDriveProgress;
    private TextBlock? _soundDriveTime;
    private TextBox? _soundDriveSearchBox;
    private readonly List<TransPoliMediaWindow.PhoneOnlineTrack> _soundDriveOnlineResults = new();
    private bool _soundDriveSearching;
    private string _soundDriveSearchQuery = "";
    private string _soundDriveSearchError = "";
    private bool _soundDriveAdvancedAudio;

    public DriverPhoneWindow()
    {
        InitializeComponent();
        MediaSessionState.Changed += OnMediaSessionChanged;
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        _islandTimer.Tick += (_, _) => { _islandTimer.Stop(); DynamicIslandText.Visibility=Visibility.Collapsed; DynamicIsland.Width=104; };
        Closed += (_, _) => { _clock.Stop(); _islandTimer.Stop(); MediaSessionState.Changed -= OnMediaSessionChanged; };
    }

    internal void AttachMainWindow(MainWindow mainWindow) => _mainWindow = mainWindow;

    private void OnMediaSessionChanged(MediaNowPlaying state)
    {
        Dispatcher.Invoke(() =>
        {
            if (_soundDriveTrack is not null) _soundDriveTrack.Text = string.IsNullOrWhiteSpace(state.Title) ? "Nenhuma música tocando" : state.Title;
            if (_soundDriveArtist is not null) _soundDriveArtist.Text = string.IsNullOrWhiteSpace(state.Artist) ? state.Source : state.Artist;
            if (_soundDriveState is not null) _soundDriveState.Text = string.IsNullOrWhiteSpace(state.Title) ? "SOUNDDRIVE • PRONTO" : $"{(state.IsPlaying ? "TOCANDO" : "PAUSADO")} • {state.Source} • {state.Volume:0}%";
            if (_soundDriveProgress is not null) { _soundDriveProgress.Maximum = Math.Max(1, state.DurationSeconds); _soundDriveProgress.Value = Math.Clamp(state.PositionSeconds, 0, _soundDriveProgress.Maximum); }
            if (_soundDriveTime is not null) _soundDriveTime.Text = state.DurationSeconds > 0 ? $"{FormatMediaTime(state.PositionSeconds)}   /   {FormatMediaTime(state.DurationSeconds)}" : state.Source;
        });
    }

    private void BuildSoundDrive()
    {
        AddHero("SOUNDDRIVE", "Sua música na estrada");
        var state = MediaSessionState.Current;

        var source = string.IsNullOrWhiteSpace(state.Source) ? "PRONTO" : state.Source.Replace("FILA LOCAL", "LOCAL", StringComparison.OrdinalIgnoreCase);
        var now = new StackPanel { Margin = new Thickness(2, 2, 2, 2) };
        now.Children.Add(new TextBlock { Text = "AGORA TOCANDO", Foreground = Brush("#D6A52A"), FontSize = 9, FontWeight = FontWeights.Bold });
        _soundDriveTrack = new TextBlock { Text = string.IsNullOrWhiteSpace(state.Title) ? "Escolha uma música" : state.Title, Foreground = Brush("#F7F8FA"), FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 3) };
        _soundDriveArtist = new TextBlock { Text = string.IsNullOrWhiteSpace(state.Artist) ? "YouTube ou biblioteca local" : state.Artist, Foreground = Brush("#AEB7C1"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
        _soundDriveState = new TextBlock { Text = string.IsNullOrWhiteSpace(state.Title) ? "SOUNDDRIVE • PRONTO" : $"{(state.IsPlaying ? "TOCANDO" : "PAUSADO")} • {source} • {state.Volume:0}%", Foreground = Brush("#FFE08A"), FontSize = 9, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) };
        now.Children.Add(_soundDriveTrack); now.Children.Add(_soundDriveArtist); now.Children.Add(_soundDriveState);
        _soundDriveProgress = new ProgressBar { Height = 3, Minimum = 0, Maximum = Math.Max(1, state.DurationSeconds), Value = Math.Clamp(state.PositionSeconds, 0, Math.Max(1, state.DurationSeconds)), Margin = new Thickness(0, 12, 0, 5), IsHitTestVisible = false };
        _soundDriveTime = new TextBlock { Text = state.DurationSeconds > 0 ? $"{FormatMediaTime(state.PositionSeconds)}   /   {FormatMediaTime(state.DurationSeconds)}" : source, Foreground = Brush("#7F8994"), FontSize = 8, HorizontalAlignment = HorizontalAlignment.Right };
        now.Children.Add(_soundDriveProgress); now.Children.Add(_soundDriveTime);
        AppContent.Children.Add(Card(now));

        var controls = new Grid { Margin = new Thickness(0, 10, 0, 5) };
        for (var i = 0; i < 3; i++) controls.ColumnDefinitions.Add(new ColumnDefinition());
        Button Cmd(string glyph, string tip, int col, Action action)
        {
            var button = new Button { Content = glyph, ToolTip = tip, Height = 48, Margin = new Thickness(3), Background = Brush(col == 1 ? "#D6A52A" : "#141A20"), Foreground = Brush(col == 1 ? "#07090C" : "#F7F8FA"), BorderBrush = Brush(col == 1 ? "#D6A52A" : "#303B46"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = col == 1 ? 21 : 18, FontWeight = FontWeights.Bold };
            button.Click += (_, _) => action(); Grid.SetColumn(button, col); controls.Children.Add(button); return button;
        }
        Cmd("\uE892", "Anterior", 0, () => _mainWindow?.MediaController.MediaPrevious());
        Cmd(state.IsPlaying ? "\uE769" : "\uE768", "Play / Pause", 1, () => _mainWindow?.MediaController.MediaPlayPause());
        Cmd("\uE893", "Próxima", 2, () => _mainWindow?.MediaController.MediaNext());
        AppContent.Children.Add(controls);

        var volume = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        volume.ColumnDefinitions.Add(new ColumnDefinition()); volume.ColumnDefinitions.Add(new ColumnDefinition());
        Button Vol(string glyph, string label, int col, double delta)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
            panel.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = panel, Height = 38, Margin = new Thickness(3), Background = Brush("#141A20"), Foreground = Brush("#F7F8FA"), BorderBrush = Brush("#303B46"), BorderThickness = new Thickness(1) };
            button.Click += (_, _) => _mainWindow?.MediaController.MediaAdjustVolume(delta); Grid.SetColumn(button, col); volume.Children.Add(button); return button;
        }
        Vol("\uE992", "MENOS", 0, -5); Vol("\uE995", "MAIS", 1, 5); AppContent.Children.Add(volume);

        AddSection("PESQUISAR NO YOUTUBE");
        var searchGrid = new Grid { Margin = new Thickness(0, 4, 0, 7) };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition()); searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        _soundDriveSearchBox = new TextBox { Text = _soundDriveSearchQuery, Height = 42, Padding = new Thickness(12, 10, 8, 8), Background = Brush("#10161C"), Foreground = Brush("#F7F8FA"), BorderBrush = Brush("#303B46"), BorderThickness = new Thickness(1), FontSize = 10, ToolTip = "Pesquisar no YouTube" };
        _soundDriveSearchBox.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await SearchSoundDriveYouTubeAsync(); };
        searchGrid.Children.Add(_soundDriveSearchBox);
        var searchButton = new Button { Content = "\uE721", Width = 42, Height = 42, Margin = new Thickness(6, 0, 0, 0), Background = Brush("#D6A52A"), Foreground = Brush("#07090C"), BorderBrush = Brush("#D6A52A"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15, ToolTip = "Pesquisar" };
        searchButton.Click += async (_, _) => await SearchSoundDriveYouTubeAsync(); Grid.SetColumn(searchButton, 1); searchGrid.Children.Add(searchButton); AppContent.Children.Add(searchGrid);
        if (_soundDriveSearching) AddState("Pesquisando no YouTube…", "A consulta é feita somente agora, sem polling em segundo plano.");
        if (!_soundDriveSearching && !string.IsNullOrWhiteSpace(_soundDriveSearchError)) AddState("YouTube indisponível", _soundDriveSearchError);
        foreach (var result in _soundDriveOnlineResults.Take(8))
        {
            var resultRow = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); resultRow.ColumnDefinitions.Add(new ColumnDefinition()); resultRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (Uri.TryCreate(result.Artwork, UriKind.Absolute, out var artworkUri))
            {
                try { var image = new Image { Width = 50, Height = 50, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 8, 0) }; image.Source = new BitmapImage(artworkUri); resultRow.Children.Add(image); } catch { }
            }
            var resultMeta = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            resultMeta.Children.Add(new TextBlock { Text = result.Title, Foreground = Brush("#F7F8FA"), FontSize = 10.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 32 });
            var duration = result.DurationSeconds > 0 ? $" • {FormatMediaTime(result.DurationSeconds)}" : "";
            resultMeta.Children.Add(new TextBlock { Text = result.Artist + duration, Foreground = Brush("#929BA7"), FontSize = 8.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(resultMeta, 1); resultRow.Children.Add(resultMeta);
            var playOnline = new Button { Content = "\uE768", Width = 38, Height = 34, Margin = new Thickness(8, 0, 0, 0), Background = Brush("#141A20"), Foreground = Brush("#FFE08A"), BorderBrush = Brush("#80631B"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, ToolTip = "Tocar no YouTube" };
            playOnline.Click += async (_, _) => { if (_mainWindow is null) return; await _mainWindow.MediaController.MediaPlayOnlineAsync(result.Index); BuildSoundDrivePageAgain(); }; Grid.SetColumn(playOnline, 2); resultRow.Children.Add(playOnline); AppContent.Children.Add(Card(resultRow));
        }
        if (!_soundDriveSearching && _soundDriveOnlineResults.Count == 0) AddState("YouTube pronto para pesquisar", "Digite uma música ou artista. A credencial fica protegida no servidor TransPoli.");

        AddSection("BIBLIOTECA LOCAL");
        var libraryActions = new Grid { Margin = new Thickness(0, 3, 0, 7) };
        libraryActions.ColumnDefinitions.Add(new ColumnDefinition()); libraryActions.ColumnDefinitions.Add(new ColumnDefinition());
        Button LibraryAction(string text, int col, Action action)
        {
            var button = new Button { Content = text, Height = 38, Margin = new Thickness(3), Background = Brush(col == 0 ? "#D6A52A" : "#141A20"), Foreground = Brush(col == 0 ? "#07090C" : "#F7F8FA"), BorderBrush = Brush(col == 0 ? "#D6A52A" : "#303B46"), BorderThickness = new Thickness(1), FontSize = 9, FontWeight = FontWeights.Bold };
            button.Click += (_, _) => action(); Grid.SetColumn(button, col); libraryActions.Children.Add(button); return button;
        }
        LibraryAction("+ ADICIONAR", 0, () => { if (_mainWindow?.MediaController.MediaAddLocalFiles() == true) BuildSoundDrivePageAgain(); });
        LibraryAction("ATUALIZAR", 1, BuildSoundDrivePageAgain);
        AppContent.Children.Add(libraryActions);

        var tracks = _mainWindow?.MediaController.GetPhoneLibrary() ?? Array.Empty<TransPoliMediaWindow.PhoneLibraryTrack>();
        if (tracks.Count == 0) AddState("Sua biblioteca está vazia", "Adicione músicas do computador. Elas continuam tocando pelo motor SoundDrive com Cabin Audio.");
        foreach (var track in tracks.Take(12))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var meta = new StackPanel();
            meta.Children.Add(new TextBlock { Text = track.Title, Foreground = Brush("#F7F8FA"), FontSize = 11, FontWeight = track.IsCurrent ? FontWeights.Bold : FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            meta.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(track.Artist) ? $"LOCAL • {track.Detail}" : $"{track.Artist} • {track.Detail}", Foreground = Brush(track.IsCurrent ? "#FFE08A" : "#929BA7"), FontSize = 8.5, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(meta);
            var play = new Button { Content = "\uE768", Width = 38, Height = 34, Margin = new Thickness(8, 0, 0, 0), Background = Brush(track.IsCurrent ? "#D6A52A" : "#141A20"), Foreground = Brush(track.IsCurrent ? "#07090C" : "#F7F8FA"), BorderBrush = Brush("#80631B"), BorderThickness = new Thickness(1), FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, ToolTip = "Tocar" };
            play.Click += (_, _) => { _mainWindow?.MediaController.MediaPlayLocal(track.Index); BuildSoundDrivePageAgain(); };
            Grid.SetColumn(play, 1); row.Children.Add(play);
            AppContent.Children.Add(Card(row));
        }
        if (tracks.Count > 12) AddState($"+ {tracks.Count - 12} músicas na fila", "A visualização completa da biblioteca será refinada junto da busca e fila do SoundDrive.");

        AddSection("SOM DA CABINE");
        var cabinState=_mainWindow?.MediaController.GetPhoneCabinAudioState();
        var presetRow=new UniformGrid { Columns=4, Margin=new Thickness(0,4,0,6) };
        foreach(var preset in new[]{("ORIGINAL","NORMAL"),("CABINE","CABINE"),("GRAVE+","SUBWOOFER"),("NOTURNO","NOTURNO")})
        {
            var active=string.Equals(cabinState?.Preset,preset.Item2,StringComparison.OrdinalIgnoreCase);
            var button=new Button { Content=preset.Item1, Height=34, Margin=new Thickness(2), Background=Brush(active?"#D6A52A":"#141A20"), Foreground=Brush(active?"#07090C":"#D7DEE6"), BorderBrush=Brush(active?"#D6A52A":"#303B46"), BorderThickness=new Thickness(1), FontSize=8.5, FontWeight=FontWeights.Bold, Tag=preset.Item2 };
            button.Click+=(_,_)=>{ _mainWindow?.MediaController.MediaApplyCabinPreset((string)button.Tag); BuildSoundDrivePageAgain(); }; presetRow.Children.Add(button);
        }
        AppContent.Children.Add(presetRow);
        AddState("Cabin Audio", state.Source.Contains("LOCAL",StringComparison.OrdinalIgnoreCase) ? "DSP real ativo para arquivos locais. O perfil escolhido é aplicado pelo mesmo motor SoundDrive." : "Perfis ficam prontos para mídia local. YouTube mantém o áudio do player oficial, sem DSP simulado.");
        var advancedToggle=new Button { Content=_soundDriveAdvancedAudio?"OCULTAR AJUSTES":"AJUSTAR SOM", Height=34, Margin=new Thickness(0,3,0,7), Background=Brush("#10161C"), Foreground=Brush("#FFE08A"), BorderBrush=Brush("#80631B"), BorderThickness=new Thickness(1), FontSize=9, FontWeight=FontWeights.Bold };
        advancedToggle.Click+=(_,_)=>{_soundDriveAdvancedAudio=!_soundDriveAdvancedAudio;BuildSoundDrivePageAgain();}; AppContent.Children.Add(advancedToggle);
        if(_soundDriveAdvancedAudio && cabinState is not null)
        {
            AddCabinAdjuster("CABINE",cabinState.Cabin,-10,10,(a)=>_mainWindow?.MediaController.MediaAdjustCabinAudio(a,0,0));
            AddCabinAdjuster("GRAVE",cabinState.Bass,-10,10,(a)=>_mainWindow?.MediaController.MediaAdjustCabinAudio(0,a,0));
            AddCabinAdjuster("AMBIÊNCIA",cabinState.Ambience,-10,10,(a)=>_mainWindow?.MediaController.MediaAdjustCabinAudio(0,0,a));
        }
    }

    private void AddCabinAdjuster(string label,double value,double minus,double plus,Action<double> adjust)
    {
        var row=new Grid { Margin=new Thickness(0,2,0,5) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(76) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(42) });
        row.Children.Add(new TextBlock { Text=$"{label}  {value:0}%", Foreground=Brush("#AEB7C1"), FontSize=8.5, VerticalAlignment=VerticalAlignment.Center });
        var less=new Button { Content="−", Height=30, Margin=new Thickness(2), Background=Brush("#141A20"), Foreground=Brush("#F7F8FA"), BorderBrush=Brush("#303B46"), BorderThickness=new Thickness(1) }; less.Click+=(_,_)=>{adjust(minus);BuildSoundDrivePageAgain();}; Grid.SetColumn(less,1); row.Children.Add(less);
        var bar=new ProgressBar { Minimum=0,Maximum=150,Value=Math.Clamp(value,0,150),Height=4,Margin=new Thickness(8,0,8,0),IsHitTestVisible=false,VerticalAlignment=VerticalAlignment.Center }; Grid.SetColumn(bar,2); row.Children.Add(bar);
        var more=new Button { Content="+", Height=30, Margin=new Thickness(2), Background=Brush("#141A20"), Foreground=Brush("#F7F8FA"), BorderBrush=Brush("#303B46"), BorderThickness=new Thickness(1) }; more.Click+=(_,_)=>{adjust(plus);BuildSoundDrivePageAgain();}; Grid.SetColumn(more,3); row.Children.Add(more); AppContent.Children.Add(row);
    }

    private static string FormatMediaTime(double seconds)
    {
        if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)seconds=0;
        var time=TimeSpan.FromSeconds(seconds);
        return time.TotalHours>=1?time.ToString(@"h\\:mm\\:ss"):time.ToString(@"m\\:ss");
    }

    private async Task SearchSoundDriveYouTubeAsync()
    {
        if (_soundDriveSearching) return;
        if (_mainWindow is null)
        {
            _soundDriveSearchError="O SoundDrive não está vinculado ao controlador principal do TransPoli.";
            BuildSoundDrivePageAgain();
            return;
        }
        var query=_soundDriveSearchBox?.Text?.Trim()??"";
        if(query.Length<2){ AddState("Digite pelo menos 2 caracteres", "Pesquise pelo nome da música, artista ou canal."); return; }
        _soundDriveSearchQuery=query; _soundDriveSearchError="";
        _soundDriveSearching=true; BuildSoundDrivePageAgain();
        try
        {
            using var searchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var results=await _mainWindow.MediaController.MediaSearchYouTubeAsync(query, searchTimeout.Token);
            _soundDriveOnlineResults.Clear(); _soundDriveOnlineResults.AddRange(results); _soundDriveSearchError=results.Count==0?"Nenhum resultado encontrado para esta pesquisa.":"";
        }
        catch(OperationCanceledException)
        {
            _soundDriveOnlineResults.Clear();
            _soundDriveSearchError="A pesquisa demorou mais de 15 segundos. Verifique a conexão com a API TransPoli e tente novamente.";
        }
        catch(Exception ex)
        {
            _soundDriveOnlineResults.Clear();
            _soundDriveSearchError=ex.Message;
        }
        finally { _soundDriveSearching=false; BuildSoundDrivePageAgain(); }
    }

    private void BuildSoundDrivePageAgain()
    {
        if (AppPanel.Visibility != Visibility.Visible || !string.Equals(AppTitle.Text, "SOUNDDRIVE", StringComparison.OrdinalIgnoreCase)) return;
        AppContent.Children.Clear();
        BuildSoundDrive();
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
        PhoneSpeedText.Text = tripActive ? "EM ROTA" : hasJob ? "PRÉ-VIAGEM" : data.Connected ? "DISPONÍVEL" : "OFFLINE";
        PhoneProgressText.Text = hasJob ? $"{_distanceKm:0} km • {_remainingKm:0} km restantes" : "—";
        var total = _distanceKm + _remainingKm;
        var progress = total > 0 ? Math.Clamp(_distanceKm / total, 0f, 1f) : 0f;
        PhoneProgressBar.Width = 324 * progress;
        PhoneTripDot.Fill = Brush(tripActive ? "#4EE59B" : hasJob ? "#FFE08A" : "#697480");
    }

    public void UpdateRoadCombination(RoadCombinationSnapshot snapshot) => _combination = snapshot ?? RoadCombinationSnapshot.Empty;

    public void UpdateTollHistory(IEnumerable<PhoneTollItem>? items) { _tolls.Clear(); if(items!=null) _tolls.AddRange(items.Take(30)); }
    public void UpdateRefuelHistory(IEnumerable<PhoneRefuelItem>? items) { _refuels.Clear(); if(items!=null) _refuels.AddRange(items.Take(30)); }

    public void UpdateRefuelPrompt(bool pending, float liters)
    {
        _pendingRefuel=pending; _pendingRefuelLiters=Math.Max(0,liters);
        RefreshHomeAction();
    }

    public void UpdateOperationalSummary(decimal balance, int tripCount, double totalKm, int documentCount, int stampedDocumentCount, int? rankingPosition)
    {
        _balance=balance; _tripCount=tripCount; _totalKm=totalKm; _documentCount=documentCount; _stampedDocumentCount=stampedDocumentCount; _rankingPosition=rankingPosition;
    }

    public void UpdateOperationalCounters(int tripCount, double totalKm, int documentCount, int stampedDocumentCount)
    {
        _tripCount=tripCount; _totalKm=totalKm; _documentCount=documentCount; _stampedDocumentCount=stampedDocumentCount;
    }

    public void UpdateBankHistory(IEnumerable<PhoneLedgerItem> items)
    {
        _ledger.Clear(); _ledger.AddRange(items.Take(20));
    }

    public void UpdateOfficialBank(decimal balance, IEnumerable<PhoneLedgerItem> items)
    {
        _officialSession = true;
        _officialEconomyLoaded = true;
        _balance = balance;
        _ledger.Clear();
        _ledger.AddRange(items.Take(20));
        if (string.Equals(AppTitle.Text, "BANCO", StringComparison.OrdinalIgnoreCase))
            OpenApp("Banco");
    }

    public void UpdateDocumentHistory(IEnumerable<PhoneDocumentItem> items)
    {
        _documents.Clear(); _documents.AddRange(items.Take(20));
    }

    public void UpdateDocumentGate(bool pending)
    {
        _documentGatePending=pending;
        RefreshHomeAction();
    }

    public void UpdateTripHistory(IEnumerable<PhoneTripItem> items)
    {
        _trips.Clear(); _trips.AddRange(items.Take(20));
    }

    public void UpdateRankingSummary(int? position, decimal revenue, decimal rate, int trips, double km)
    {
        _rankingPosition=position; _rankingRevenue=revenue; _rankingRate=rate;
        _rankingTrips=Math.Max(0,trips); _rankingKm=Math.Max(0,km);
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

    public void SetRefuelResult(bool success,string message)
    {
        if(_refuelConfirmButton is null)return;
        _refuelConfirmButton.Content=message;
        _refuelConfirmButton.IsEnabled=!success;
        _refuelConfirmButton.Background=Brush(success?"#1E5B45":"#7A5520");
        if(success){_pendingRefuel=false;_pendingRefuelLiters=0;}
    }

    public void SetStampResult(bool success, string message)
    {
        if(_stampButton is null) return;
        _stampButton.Content=message;
        _stampButton.IsEnabled=!success;
        _stampButton.Background=Brush(success?"#1E5B45":"#D6A52A");
    }

    public void UpdateDataSourceState(bool officialSession, bool economyLoaded, bool tripsLoaded, bool documentsLoaded)
    {
        _officialSession=officialSession;
        _officialEconomyLoaded=economyLoaded;
        _officialTripsLoaded=tripsLoaded;
        _officialDocumentsLoaded=documentsLoaded;
    }

    public void UpdateProfile(string session, string truck, string plate, string driverName)
    {
        _profileSession=string.IsNullOrWhiteSpace(session)?"PERFIL LOCAL":session;
        var driver=string.IsNullOrWhiteSpace(driverName)?"":driverName.Trim();
        _profileDriverName=driver;
        PhoneGreetingText.Text=string.IsNullOrWhiteSpace(driver)?"Boa viagem!":$"Boa viagem, {driver}!";
    }

    private void RefreshHomeAction()
    {
        if(HomeActionButton is null)return;
        if(_documentGatePending)
        {
            _homeActionApp="Documentos";
            HomeActionButton.Content="●  DANFE PENDENTE  •  CARIMBAR E DESPACHAR";
            HomeActionButton.BorderBrush=Brush("#FF6262");
            HomeActionButton.Height=38;HomeActionButton.Visibility=Visibility.Visible;
        }
        else if(_pendingRefuel && _pendingRefuelLiters>0)
        {
            _homeActionApp="Abastecimentos";
            HomeActionButton.Content=$"●  ABASTECIMENTO {_pendingRefuelLiters:0.0} L  •  CONFIRMAR";
            HomeActionButton.BorderBrush=Brush("#FFE08A");
            HomeActionButton.Height=38;HomeActionButton.Visibility=Visibility.Visible;
        }
        else
        {
            _homeActionApp="";HomeActionButton.Height=0;HomeActionButton.Visibility=Visibility.Collapsed;
        }
    }

    private void HomeActionButton_Click(object sender,RoutedEventArgs e)
    {
        if(string.IsNullOrWhiteSpace(_homeActionApp))return;
        OpenApp(_homeActionApp);
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
        var target=NotificationTarget(item);
        if(!string.IsNullOrWhiteSpace(target))
        {
            shell.Cursor=System.Windows.Input.Cursors.Hand;
            shell.ToolTip=$"Abrir {target}";
            shell.MouseLeftButtonUp+=(_,__)=>{NotificationShade.Visibility=Visibility.Collapsed;OpenApp(target);};
            s.Children.Add(new TextBlock{Text=$"TOQUE PARA ABRIR {target.ToUpperInvariant()}",Foreground=Brush(color),FontSize=8,FontWeight=FontWeights.Bold,Margin=new Thickness(0,7,0,0)});
        }
        g.Children.Add(s);shell.Child=g;NotificationShadeContent.Children.Add(shell);
    }

    private static string NotificationTarget(PhoneNotificationItem item)
    {
        var title=item.Title??"";
        if(title.Contains("Carimbo",StringComparison.OrdinalIgnoreCase)||title.Contains("DANFE",StringComparison.OrdinalIgnoreCase)||title.Contains("nota",StringComparison.OrdinalIgnoreCase))return "Documentos";
        if(title.Contains("Abastecimento",StringComparison.OrdinalIgnoreCase)||title.Contains("Combustível",StringComparison.OrdinalIgnoreCase))return "Abastecimentos";
        if(title.Contains("Sincron",StringComparison.OrdinalIgnoreCase))return "Ajustes";
        if(title.Contains("Manutenção",StringComparison.OrdinalIgnoreCase))return "Ocorrências";
        return "";
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

    internal void OpenSoundDrive() => OpenApp("SoundDrive");

    private void OpenApp(string app)
    {
        AppTitle.Text=app.ToUpperInvariant(); AppContent.Children.Clear(); ApplyAppIdentity(app);
        switch(app)
        {
            case "SoundDrive":
                BuildSoundDrive();
                break;
            case "Mensagens":
                AddHero("MENSAGENS","Comunicação TransPoli");
                AddState("Canal ainda não conectado","Ainda não existe uma fonte real de mensagens entre motorista e central. Esta tela permanece somente leitura até existir um serviço oficial de comunicação.");
                AddSection("COMUNICAÇÃO OPERACIONAL");
                AddState("Ocorrências não são mensagens","Alertas de viagem, DANFE, abastecimento, manutenção e sincronização ficam na Central de Ocorrências para não misturar eventos automáticos com conversas humanas.");
                break;
            case "Ocorrências":
            case "Alertas":
                AddHero("OCORRÊNCIAS","Atenção, ação e acompanhamento");
                AddRow("ETS2",_telemetry?.Connected==true?"CONECTADO":"OFFLINE",_telemetry?.Connected==true);
                AddRow("Viagem",_tripActive?"EM ANDAMENTO":(_telemetry?.OnJob==true?"CONTRATO DETECTADO":"SEM VIAGEM"),_tripActive);
                AddSection("EVENTOS ATIVOS");
                if(_notifications.Count==0) AddState("Tudo em ordem","Não existem ocorrências operacionais ativas.");
                foreach(var item in _notifications) AddNotification(item);
                break;
            case "Banco":
                AddHero("BANCO TRANSPOLI","Saldo e extrato da conta TransPoli");
                AddSourceState(_officialEconomyLoaded?"OFICIAL • SERVIDOR":_officialSession?"SNAPSHOT OFICIAL INDISPONÍVEL":"LOCAL • SEM SESSÃO",_officialEconomyLoaded,_officialSession && !_officialEconomyLoaded?"O último saldo oficial não foi carregado; o celular não substitui por um saldo local paralelo.":"A mesma fonte econômica do Banco TransPoli é usada aqui.");
                AddBig(_officialSession&&!_officialEconomyLoaded?"—":_balance.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),"SALDO DISPONÍVEL");
                AddMetricPair("VIAGENS LIQUIDADAS",_tripCount.ToString(),"KM CONSOLIDADOS",$"{_totalKm:N0}");
                AddSection("ÚLTIMAS MOVIMENTAÇÕES");
                if(_ledger.Count==0) AddState("Extrato vazio","Ainda não existem movimentações financeiras registradas.");
                foreach(var item in _ledger) AddTransaction(item);
                break;
            case "Documentos":
                AddHero("DOCUMENTOS","Arquivo de notas da operação");
                if(_openDocument is { } doc)
                {
                    AddSection("DANFE • RESUMO NO CELULAR");
                    AddBig(string.IsNullOrWhiteSpace(doc.Reference)?"NF SEM NÚMERO":doc.Reference,doc.Stamped?"DOCUMENTO CARIMBADO":"DOCUMENTO EMITIDO");
                    AddRow("Carga",doc.Cargo,!string.IsNullOrWhiteSpace(doc.Cargo));
                    AddRow("Rota",doc.Route,!string.IsNullOrWhiteSpace(doc.Route));
                    AddRow("Emissão",doc.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),true);
                    AddState("Identidade preservada","Este resumo usa o mesmo documento da operação; o celular não cria outra DANFE.");
                    var backDoc=new Button{Content="VOLTAR AO ARQUIVO",Height=40,Margin=new Thickness(0,0,0,10),Background=Brush("#141A20"),Foreground=Brush("#67B7FF"),BorderBrush=Brush("#355A73"),BorderThickness=new Thickness(1),FontWeight=FontWeights.Bold};
                    backDoc.Click+=(_,__)=>{_openDocument=null;OpenApp("Documentos");};AppContent.Children.Add(backDoc);
                    break;
                }
                AddSourceState(_officialDocumentsLoaded?"OFICIAL + PENDÊNCIAS LOCAIS":_officialSession?"LOCAL PENDENTE / CACHE":"ARQUIVO LOCAL",_officialDocumentsLoaded,!_officialDocumentsLoaded&&_officialSession?"Documentos locais pendentes permanecem acessíveis enquanto o snapshot oficial não atualiza.":"DANFE e comprovantes preservam a identidade da operação.");
                AddBig(_documentCount.ToString(),"NOTAS REGISTRADAS"); AddMetricPair("CARIMBADAS",_stampedDocumentCount.ToString(),"PENDENTES",Math.Max(0,_documentCount-_stampedDocumentCount).ToString());
                if(_documentGatePending)
                {
                    AddState("AÇÃO NECESSÁRIA • DANFE PENDENTE","Nova carga detectada. A viagem permanece bloqueada até o despacho documental.");
                    AddRow("Carga",Value(_telemetry?.Cargo),!string.IsNullOrWhiteSpace(_telemetry?.Cargo));
                    AddRow("Rota",$"{Value(_telemetry?.SourceCity)} → {Value(_telemetry?.DestinationCity)}",!string.IsNullOrWhiteSpace(_telemetry?.DestinationCity));
                    AddState("LIBERAÇÃO OBRIGATÓRIA","Mantenha o caminhão parado e carimbe a nota atual. O mesmo documento será preservado na viagem e no arquivo.");
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
                AddHero("VIAGENS","Operação atual e histórico consolidado");
                AddSourceState(_officialTripsLoaded?"HISTÓRICO OFICIAL":_officialSession?"HISTÓRICO OFICIAL AGUARDANDO":"HISTÓRICO LOCAL",_officialTripsLoaded,_officialSession&&!_officialTripsLoaded?"A viagem atual continua local-first; o histórico oficial não é substituído por outra fonte enquanto estiver indisponível.":"A viagem atual usa a mesma TripSession do computador de bordo.");
                AddSection("VIAGEM ATUAL");
                AddBig(_tripActive?$"{_distanceKm:0} km":"—","PERCORRIDOS");
                AddRow("Rota",$"{Value(_telemetry?.SourceCity)} → {Value(_telemetry?.DestinationCity)}",_tripActive);
                AddRow("Carga",Value(_telemetry?.Cargo),_tripActive); AddRow("Percorrido",$"{_distanceKm:0.0} km",_tripActive); AddRow("Restante",$"{_remainingKm:0.0} km",_tripActive);
                AddSection("ÚLTIMAS CONCLUÍDAS");
                if(_trips.Count==0) AddState("Sem viagens concluídas",_officialSession&&!_officialTripsLoaded?"Aguardando o histórico oficial TransPoli; nenhuma viagem local será somada como se já estivesse consolidada.":"As entregas liquidadas no TransPoli aparecerão aqui.");
                foreach(var item in _trips) AddTrip(item);
                break;
            case "Ranking":
                AddHero("RANKING","Desempenho do motorista");
                AddSourceState(_officialSession?"RANKING OFICIAL • SERVIDOR":"RANKING AGUARDANDO SESSÃO",_officialSession,_officialSession?"A posição e os indicadores pertencem ao ranking centralizado do TransPoli.":"O celular não calcula uma posição local paralela.");
                AddBig(_rankingPosition.HasValue && _rankingPosition>0?$"#{_rankingPosition}":"—","POSIÇÃO ATUAL");
                AddMetricPair("VIAGENS",_officialSession?_rankingTrips.ToString():"N/D","KM",_officialSession?$"{_rankingKm:N0}":"N/D");
                AddMetricPair("R$/KM",_officialSession?$"R$ {_rankingRate:N2}":"N/D","TOTAL RECEBIDO",_officialSession?_rankingRevenue.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")):"N/D");
                break;
            case "Perfil":
                AddHero("PERFIL DO MOTORISTA","Identidade operacional");
                AddBig(_profileSession,"VÍNCULO / SESSÃO");
                AddSection("IDENTIDADE PROFISSIONAL");
                AddRow("Motorista",string.IsNullOrWhiteSpace(_profileDriverName)?"Identidade aguardando sincronização":_profileDriverName,!string.IsNullOrWhiteSpace(_profileDriverName));
                AddSection("DESEMPENHO CONSOLIDADO");
                AddRow("Viagens",_officialTripsLoaded?_tripCount.ToString():"N/D",_officialTripsLoaded); AddRow("KM consolidado",_officialTripsLoaded?$"{_totalKm:N0} km":"N/D",_officialTripsLoaded); AddRow("Ranking",_rankingPosition.HasValue?$"#{_rankingPosition}":"Aguardando ranking oficial",_rankingPosition.HasValue);
                break;
            case "PoliPass":
                AddHero("POLIPASS","Passagens e comprovantes vinculados à operação");
                if(_openTollReceipt is { } receipt)
                {
                    AddSection("COMPROVANTE");
                    AddBig(receipt.Amount?.ToString("C2",CultureInfo.GetCultureInfo("pt-BR"))??"—","VALOR DA PASSAGEM");
                    AddRow("Status",receipt.Paid?"REGISTRADO":"PENDENTE",receipt.Paid);
                    AddRow("Data / hora",receipt.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),true);
                    AddRow("Conjunto",receipt.AxlesText,receipt.Paid);
                    AddState($"POLIPASS • PP-{receipt.EventId:0000000000}","Passagem persistida no TransPoli. A consolidação oficial da conta continua pertencendo ao Banco/servidor.");
                    var closeReceipt=new Button{Content="VOLTAR ÀS PASSAGENS",Height=40,Margin=new Thickness(0,0,0,10),Background=Brush("#141A20"),Foreground=Brush("#FFE08A"),BorderBrush=Brush("#80631B"),BorderThickness=new Thickness(1),FontWeight=FontWeights.Bold};
                    closeReceipt.Click+=(_,__)=>{_openTollReceipt=null;OpenApp("PoliPass");};AppContent.Children.Add(closeReceipt);
                    break;
                }
                AddSection("ÚLTIMAS PASSAGENS");
                if(_tolls.Count==0) AddState("Nenhuma passagem registrada","As passagens reais detectadas pelo ETS2 aparecerão aqui sem inventar praça, cidade ou valor.");
                foreach (var toll in _tolls.Take(8))
                {
                    var amountText = toll.Paid
                        ? toll.Amount!.Value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"))
                        : "VALOR PENDENTE";
                    var b = new Button
                    {
                        Content = $"{(toll.Paid ? "✓ REGISTRADO" : "PENDENTE")}  •  {amountText}  •  {toll.When.ToLocalTime():dd/MM HH:mm}  •  {toll.AxlesText}" + (toll.Paid ? "  •  VER COMPROVANTE" : ""),
                        MinHeight = 46,
                        Margin = new Thickness(0, 0, 0, 6),
                        Padding = new Thickness(10,6,10,6),
                        Background = Brush("#141A20"),
                        Foreground = Brush("#F7F8FA"),
                        BorderBrush = Brush("#27313B"),
                        BorderThickness = new Thickness(1),
                        Tag = toll.EventId
                    };
                    b.IsEnabled = toll.Paid;
                    if (toll.Paid) b.Click += (_, __) => {_openTollReceipt=toll;OpenApp("PoliPass");};
                    AppContent.Children.Add(b);
                }
                AddSection("CONJUNTO ATUAL");
                if(!_combination.Connected) AddState("Telemetria indisponível","O histórico acima continua disponível; conecte o ETS2 apenas para atualizar o conjunto rodoviário atual.");
                else
                {
                    AddRow("Total de eixos",_combination.TotalAxleCount.HasValue?$"{_combination.TotalAxleCount.Value} eixos":"em análise",_combination.TotalAxleCount.HasValue);
                    AddRow("Caminhão",$"{Value(_combination.TruckBrand)} {Value(_combination.TruckModel)}".Trim(),true);
                    AddRow("Placa",Value(_combination.TruckPlate),!string.IsNullOrWhiteSpace(_combination.TruckPlate));
                }
                break;
            case "Abastecimentos":
            case "Abastecimento":
                AddHero("ABASTECIMENTOS","Registro comercial após detecção física");
                AddSourceState("TELEMETRIA LOCAL → OUTBOX → BANCO",false,"Os litros vêm do ETS2. Posto e preço são confirmados uma vez e seguem pelo fluxo oficial, sem débito paralelo.");
                if(!_pendingRefuel || _pendingRefuelLiters<=0) AddState("Nenhum abastecimento pendente","Quando a telemetria detectar combustível adicionado, a confirmação aparecerá aqui.");
                else
                {
                    AddBig($"{_pendingRefuelLiters:0.0} L","LITROS DETECTADOS PELA TELEMETRIA");
                    AddState("Dados comerciais pendentes","Informe preço por litro, posto e cidade. O volume permanece bloqueado na leitura real da telemetria.");
                    AddSection("CONFIRMAR NO CELULAR");
                    var price=PhoneInput("Preço por litro • ex.: 6,19");
                    var station=PhoneInput("Nome do posto");
                    var suggestedCity=_telemetry?.DestinationCity ?? _telemetry?.SourceCity ?? "";
                    var city=PhoneInput("Cidade",suggestedCity);
                    AppContent.Children.Add(price); AppContent.Children.Add(station); AppContent.Children.Add(city);
                    var total=new TextBlock{Text=$"TOTAL • {_pendingRefuelLiters:0.0} L × preço informado",Foreground=Brush("#FFE08A"),FontSize=11,FontWeight=FontWeights.Bold,Margin=new Thickness(2,2,0,10)};
                    AppContent.Children.Add(total);
                    price.TextChanged+=(_,__)=>{if(TryPhoneMoney(price.Text,out var p)&&p>0)total.Text=$"TOTAL • {((decimal)_pendingRefuelLiters*p).ToString("C2",CultureInfo.GetCultureInfo("pt-BR"))}";else total.Text=$"TOTAL • {_pendingRefuelLiters:0.0} L × preço informado";};
                    var b=new Button{Content="CONFIRMAR ABASTECIMENTO",Height=46,Background=Brush("#1E5B45"),Foreground=Brush("#F7F8FA"),BorderThickness=new Thickness(0),FontWeight=FontWeights.Bold}; _refuelConfirmButton=b;
                    b.Click+=(_,__)=>
                    {
                        if(!TryPhoneMoney(price.Text,out var p)||p<=0){AddInlineError("Informe um preço por litro válido.");return;}
                        if(string.IsNullOrWhiteSpace(station.Text)){AddInlineError("Informe o nome do posto.");return;}
                        if(string.IsNullOrWhiteSpace(city.Text)){AddInlineError("Informe a cidade do abastecimento.");return;}
                        b.IsEnabled=false;b.Content="SALVANDO ABASTECIMENTO...";
                        CompleteRefuelRequested?.Invoke(p,station.Text.Trim(),city.Text.Trim());
                    };
                    AppContent.Children.Add(b);
                }
                AddSection("HISTÓRICO DE ABASTECIMENTOS");
                if(_refuels.Count==0) AddState("Nenhum recibo registrado","Os abastecimentos confirmados aparecerão aqui usando o mesmo registro local ligado à viagem e ao Banco.");
                foreach(var item in _refuels.Take(12)) AddRefuelReceipt(item);
                break;
            case "Balança":
                AddHero("BALANÇA TRANSPOLI","Leitura técnica da carga e do conjunto");
                if(_telemetry?.Connected!=true){ AddState("Aguardando telemetria","Conecte o ETS2 para ler as massas reais disponíveis."); break; }
                var cargoKg=Math.Max(0f,_telemetry.CargoMassKg);
                var unitKg=Math.Max(0f,_telemetry.UnitMassKg);
                AddBig(cargoKg>0?$"{cargoKg/1000f:0.00} t":"—","PESO DA CARGA • TELEMETRIA");
                AddRow("Carga",Value(_telemetry.Cargo),cargoKg>0);
                AddRow("Massa da unidade",unitKg>0?$"{unitKg/1000f:0.00} t":"não informada pelo ETS2",unitKg>0);
                AddSection("COMPOSIÇÃO RODOVIÁRIA");
                AddRow("Caminhão",$"{Value(_combination.TruckBrand)} {Value(_combination.TruckModel)}".Trim(),true);
                AddRow("Eixos do caminhão",_combination.TruckAxleCount.HasValue?_combination.TruckAxleCount.Value.ToString():"em análise",_combination.TruckAxleCount.HasValue);
                foreach(var trailer in _combination.Trailers){var n=string.Join(" ",new[]{trailer.Brand,trailer.Name}.Where(x=>!string.IsNullOrWhiteSpace(x))).Trim();if(string.IsNullOrWhiteSpace(n))n=string.IsNullOrWhiteSpace(trailer.BodyType)?$"Reboque {trailer.Index+1}":trailer.BodyType;AddRow($"Reboque {trailer.Index+1}",$"{n} • {(trailer.AxleCount.HasValue?$"{trailer.AxleCount.Value} eixos":"eixos em análise")}",trailer.AxleCount.HasValue);}
                AddRow("Total de eixos",_combination.TotalAxleCount.HasValue?$"{_combination.TotalAxleCount.Value} eixos":"em análise",_combination.TotalAxleCount.HasValue);
                AddSection("LEITURA");
                AddState("Peso confirmado",cargoKg>0?$"{cargoKg:N0} kg de carga informados diretamente pela telemetria do ETS2.":"O ETS2 não informou peso de carga neste momento.");
                if(unitKg>0) AddState("Massa da unidade",$"{unitKg:N0} kg recebidos no campo de massa da unidade da telemetria.");
                AddState("Peso bruto do conjunto","Só será exibido como peso bruto quando houver dados suficientes. O TransPoli não inventa tara de caminhão ou reboque.");
                break;
            case "Garagem":
                AddHero("GARAGEM","Consulta do veículo e vínculo operacional");
                if(_telemetry?.Connected!=true)
                {
                    AddState("Veículo não detectado","Conecte o ETS2 para consultar o veículo atualmente em uso. A telemetria não cadastra frota oficial.");
                }
                else
                {
                    AddRow("Veículo detectado",$"{Value(_telemetry.TruckBrand)} {Value(_telemetry.TruckModel)}".Trim(),true);
                    AddRow("Placa",Value(_telemetry.LicensePlate),!string.IsNullOrWhiteSpace(_telemetry.LicensePlate));
                    AddState("Frota oficial","O celular apenas consulta o contexto detectado. Inclusão e autorização de frota continuam pertencendo à fonte oficial TransPoli.");
                }
                break;
            default:
                AddHero("AJUSTES","Estado e integração do celular TransPoli");
                AddRow("ETS2",_telemetry?.Connected==true?"CONECTADO":"OFFLINE",_telemetry?.Connected==true);
                AddRow("Conta",_officialSession?"SESSÃO OFICIAL":"MODO LOCAL",_officialSession);
                AddRow("Banco",_officialEconomyLoaded?"SNAPSHOT OFICIAL":"AGUARDANDO / LOCAL",_officialEconomyLoaded);
                AddRow("Viagens",_officialTripsLoaded?"HISTÓRICO OFICIAL":"AGUARDANDO / LOCAL",_officialTripsLoaded);
                AddRow("Documentos",_officialDocumentsLoaded?"OFICIAL + LOCAL":"CACHE / LOCAL",_officialDocumentsLoaded);
                AddSection("ATALHOS DO COMPUTADOR");
                AddRow("Celular","F9",true); AddRow("Computador de bordo","F10",true); AddRow("HUD","F11",true);
                AddState("Sincronização","O celular não cria uma segunda fonte de dados. Informações operacionais ficam locais e os registros oficiais usam o mesmo servidor/outbox do TransPoli.");
                break;
        }
        AppPanel.Visibility=Visibility.Visible;
        AnimateApp(true);
    }

    private void ApplyAppIdentity(string app)
    {
        var accent=app switch
        {
            "Banco"=>"#4EE59B","Documentos"=>"#67B7FF","Viagens"=>"#FFE08A","Ranking"=>"#D7B85A",
            "Ocorrências"=>_notifications.Any(x=>x.Priority==2)?"#FF6262":"#FFE08A","Alertas"=>_notifications.Any(x=>x.Priority==2)?"#FF6262":"#FFE08A","Perfil"=>"#9BC7FF",
            "Garagem"=>"#B5C0CB","Balança"=>"#67D7E8","Mensagens"=>"#8FA8FF","SoundDrive"=>"#FFE08A","PoliPass"=>"#F2BE2D","Abastecimentos"=>"#62D8A5","Abastecimento"=>"#62D8A5","Ajustes"=>"#B9C1C9",_=>"#929BA7"
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
        Cell($"{item.DistanceKm:N0} km",0);Cell($"R$ {item.RatePerKm:N2}/km",1,true);Cell(item.Amount.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),2);s.Children.Add(g);
        s.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,7,0,0)});AppContent.Children.Add(Card(s));
    }
    private void AddSection(string title)
    {
        var g=new Grid{Margin=new Thickness(2,15,0,8)};
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});g.ColumnDefinitions.Add(new ColumnDefinition());
        var t=new TextBlock{Text=title,Foreground=Brush("#AAB3BC"),FontSize=9,FontWeight=FontWeights.Bold};
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
    private void AddRefuelReceipt(PhoneRefuelItem item)
    {
        var s=new StackPanel();
        var h=new Grid();h.ColumnDefinitions.Add(new ColumnDefinition());h.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        h.Children.Add(new TextBlock{Text=string.IsNullOrWhiteSpace(item.Reference)?"ABASTECIMENTO":item.Reference,Foreground=Brush("#F7F8FA"),FontSize=11,FontWeight=FontWeights.Bold});
        var amount=new TextBlock{Text=item.Total.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),Foreground=Brush("#62D8A5"),FontSize=11,FontWeight=FontWeights.Bold};Grid.SetColumn(amount,1);h.Children.Add(amount);s.Children.Add(h);
        s.Children.Add(new TextBlock{Text=$"{item.Liters:0.0} L • {item.PricePerLiter.ToString("C2",CultureInfo.GetCultureInfo("pt-BR"))}/L",Foreground=Brush("#F7F8FA"),FontSize=10,Margin=new Thickness(0,5,0,0)});
        s.Children.Add(new TextBlock{Text=$"{(string.IsNullOrWhiteSpace(item.Station)?"Posto não informado":item.Station)} • {(string.IsNullOrWhiteSpace(item.City)?"Cidade não informada":item.City)}",Foreground=Brush("#929BA7"),FontSize=9,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap});
        s.Children.Add(new TextBlock{Text=item.When.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),Foreground=Brush("#929BA7"),FontSize=8,Margin=new Thickness(0,5,0,0)});
        AppContent.Children.Add(Card(s));
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
        if(item.Paid) view.Click+=(_,__)=>{_openTollReceipt=item;OpenApp("PoliPass");}; s.Children.Add(view); AppContent.Children.Add(Card(s));
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
        view.Click+=(_,__)=>{_openDocument=item;OpenApp("Documentos");};
        s.Children.Add(view); AppContent.Children.Add(Card(s));
    }
    private void AddHero(string title,string sub)
    {
        var shell=new Border{Background=Brush("#111820"),BorderBrush=AppPanel.BorderBrush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(24),Padding=new Thickness(17),Margin=new Thickness(0,7,0,16)};
        var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var s=new StackPanel();
        s.Children.Add(new TextBlock{Text=title,Foreground=AppTitle.Foreground,FontSize=10,FontWeight=FontWeights.Bold});
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
    private TextBox PhoneInput(string hint,string initial="")
    {
        return new TextBox{Text=initial,ToolTip=hint,Height=42,Margin=new Thickness(0,0,0,8),Padding=new Thickness(12,8,12,8),Background=Brush("#10171E"),Foreground=Brush("#F7F8FA"),BorderBrush=Brush("#303B46"),BorderThickness=new Thickness(1),FontSize=11};
    }
    private void AddInlineError(string message)
    {
        AppContent.Children.Add(new TextBlock{Text=message,Foreground=Brush("#FF6262"),FontSize=10,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,0,0,8)});
    }
    private static bool TryPhoneMoney(string text,out decimal value)=>decimal.TryParse(text.Trim().Replace('.',','),NumberStyles.Number,CultureInfo.GetCultureInfo("pt-BR"),out value)||decimal.TryParse(text.Trim().Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out value);

    private void AddSourceState(string label,bool official,string detail)
    {
        var s=new StackPanel();
        s.Children.Add(new TextBlock{Text=label,Foreground=Brush(official?"#4EE59B":"#FFE08A"),FontSize=9,FontWeight=FontWeights.Bold});
        s.Children.Add(new TextBlock{Text=detail,Foreground=Brush("#929BA7"),FontSize=9,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)});
        AppContent.Children.Add(Card(s));
    }
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
public sealed record PhoneTripItem(string Cargo, string Origin, string Destination, double DistanceKm, decimal RatePerKm, decimal Amount, DateTime When);
public sealed record PhoneNotificationItem(string Title, string Message, int Priority, DateTime When);
public sealed record PhoneRefuelItem(string Reference, DateTime When, float Liters, decimal PricePerLiter, decimal Total, string Station, string City);
public sealed record PhoneTollItem(long EventId, decimal? Amount, DateTime When, string AxlesText)
{
    public bool Paid => Amount.HasValue && Amount.Value > 0;
}
