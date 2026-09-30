using System.Net.Http;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using TagLibSharp2.Core;
using TransPoli.Audio;
using TransPoli.Media;

namespace TransPoli;

public partial class TransPoliMediaWindow : Window
{
    private readonly CabinAudioEngine _cabinAudio = new();
    private bool _usingCabinEngine;
    private readonly MainWindow _mainWindow;
    private readonly DispatcherTimer _mediaUiTimer;
    private float _vuLeft;
    private float _vuRight;
    private bool _loadingSettings;
    private readonly List<string> _playlist = new();
    private readonly List<string> _radioFavorites = new();
    private int _playlistIndex = -1;
    private int _queueRecoveryAttempts;
    private bool _cameraBaselineReady;
    private float _baselineHeadX, _baselineHeadY, _baselineHeadZ;
    private bool _externalPerspective;
    private int _externalCameraTicks;
    private int _cabinCameraTicks;
    private string _mediaPage = "now";
    private bool _syncingDriveControls;
    private readonly HttpClient _onlineHttp = new() { Timeout = TimeSpan.FromSeconds(12) };
    private OnlineMediaSettings _onlineSettings = OnlineMediaSettings.Load();
    private SpotifyMediaProvider? _spotifyProvider;
    private YouTubeMediaProvider? _youtubeProvider;
    private readonly List<OnlineMediaSearchResult> _onlineResults = new();
    private string _activeMediaProvider = "LOCAL";
    private bool _youtubePlaying;
    private bool _youtubeBridgeAttached;
    private bool _youtubeEndAdvancePending;
    private sealed class MediaSettings
    {
        public string StreamUrl { get; set; } = "";
        public double Volume { get; set; } = 70;
        public string Preset { get; set; } = "NORMAL";
        public double[] Eq { get; set; } = new double[5];
        public double CabinIntensity { get; set; } = 100;
        public double SubIntensity { get; set; } = 100;
        public double AmbienceIntensity { get; set; } = 100;
        public List<string> Playlist { get; set; } = new();
        public List<string> RadioFavorites { get; set; } = new();
        public bool Shuffle { get; set; }
        public bool Repeat { get; set; }
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransPoli", "media-settings.json");

    public TransPoliMediaWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        _cabinAudio.LevelsChanged += (left, right) => { _vuLeft = left; _vuRight = right; };
        _cabinAudio.TrackEnded += () => Dispatcher.BeginInvoke(() => AdvanceAfterTrackEnd());
        _mediaUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _mediaUiTimer.Tick += (_, _) => RefreshMediaUi();
        _mediaUiTimer.Start();
        LoadHotkeySettings();
        LoadSettings();
        SoundLabPanel.Visibility = Visibility.Collapsed;
        SoundLabColumn.Width = new GridLength(0);
        InitializeOnlineMedia();
        _ = RestoreSpotifySessionAsync();
        SyncDriveControls();
        ShowMediaPage("now");
        Closed += (_, _) =>
        {
            try
            {
                _mediaUiTimer.Stop();
                if (_youtubeBridgeAttached && YouTubeWebView.CoreWebView2 is not null) YouTubeWebView.CoreWebView2.WebMessageReceived -= YouTubeWebMessageReceived;
                _youtubeBridgeAttached = false;
                Player.Stop(); Player.Source = null; _cabinAudio.Dispose();
            }
            catch { }
        };
    }

    private void RefreshMediaUi()
    {
        VuLeft.Value = Math.Clamp(_vuLeft, 0f, 1f);
        VuRight.Value = Math.Clamp(_vuRight, 0f, 1f);
        _vuLeft *= 0.72f;
        _vuRight *= 0.72f;
        if (!_usingCabinEngine) return;
        var position = _cabinAudio.Position;
        var duration = _cabinAudio.Duration;
        TrackProgress.Maximum = Math.Max(1, duration.TotalSeconds);
        TrackProgress.Value = Math.Clamp(position.TotalSeconds, 0, TrackProgress.Maximum);
        TrackTimeText.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
        MediaSessionState.Update(current => current with { PositionSeconds = position.TotalSeconds, DurationSeconds = duration.TotalSeconds });
    }

    private void TrackProgress_Seek(object sender, MouseButtonEventArgs e)
    {
        if (!_usingCabinEngine || TrackProgress.Maximum <= 0) return;
        _cabinAudio.Seek(TimeSpan.FromSeconds(TrackProgress.Value));
        RefreshMediaUi();
    }

    private void Eq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Eq65Text is null) return;
        Eq65Text.Text = $"{Eq65.Value:+0;-0;0} dB";
        Eq145Text.Text = $"{Eq145.Value:+0;-0;0} dB";
        Eq850Text.Text = $"{Eq850.Value:+0;-0;0} dB";
        Eq3800Text.Text = $"{Eq3800.Value:+0;-0;0} dB";
        Eq10500Text.Text = $"{Eq10500.Value:+0;-0;0} dB";
        _cabinAudio.SetManualEq(Eq65.Value, Eq145.Value, Eq850.Value, Eq3800.Value, Eq10500.Value);
        SaveSettings();
    }

    private void Intensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CabinIntensity is null || SubIntensity is null || AmbienceIntensity is null) return;
        _cabinAudio.SetEffectIntensity(CabinIntensity.Value, SubIntensity.Value, AmbienceIntensity.Value);
        SaveSettings();
    }

    private void ResetEq_Click(object sender, RoutedEventArgs e)
    {
        _loadingSettings = true;
        Eq65.Value = Eq145.Value = Eq850.Value = Eq3800.Value = Eq10500.Value = 0;
        CabinIntensity.Value = SubIntensity.Value = AmbienceIntensity.Value = 100;
        _loadingSettings = false;
        ApplyAudioControls();
        SaveSettings();
    }

    private void LoadHotkeySettings()
    {
        foreach (var key in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 12 }) HotkeyBox.Items.Add($"F{key}");
        var hotkey = MediaHotkeySettings.Load();
        HotkeyBox.SelectedItem = $"F{hotkey.FunctionKey}";
        HotkeyCtrl.IsChecked = hotkey.Ctrl;
        HotkeyAlt.IsChecked = hotkey.Alt;
        HotkeyShift.IsChecked = hotkey.Shift;
        HotkeyStatusText.Text = $"ATUAL • {hotkey.Display}";
    }

    private void SaveHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeyBox.SelectedItem is not string keyText || !int.TryParse(keyText.TrimStart('F'), out var functionKey)) return;
        var candidate = new MediaHotkeySettings
        {
            FunctionKey = functionKey,
            Ctrl = HotkeyCtrl.IsChecked == true,
            Alt = HotkeyAlt.IsChecked == true,
            Shift = HotkeyShift.IsChecked == true
        };
        if (_mainWindow.ApplyMediaHotkey(candidate))
        {
            HotkeyStatusText.Text = $"SALVO • {candidate.Display}";
            return;
        }
        HotkeyStatusText.Text = "INDISPONÍVEL • escolha outra combinação";
        MessageBox.Show("Essa combinação já está sendo usada pelo Windows ou por outro aplicativo. O atalho anterior foi mantido.",
            "SoundDrive", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<MediaSettings>(File.ReadAllText(SettingsPath));
            if (s is null) return;
            StreamUrlBox.Text = s.StreamUrl;
            VolumeSlider.Value = Math.Clamp(s.Volume, 0, 100);
            if (s.Eq?.Length == 5) { Eq65.Value=s.Eq[0]; Eq145.Value=s.Eq[1]; Eq850.Value=s.Eq[2]; Eq3800.Value=s.Eq[3]; Eq10500.Value=s.Eq[4]; }
            CabinIntensity.Value=Math.Clamp(s.CabinIntensity,0,150); SubIntensity.Value=Math.Clamp(s.SubIntensity,0,150); AmbienceIntensity.Value=Math.Clamp(s.AmbienceIntensity,0,150);
            _playlist.Clear();
            _playlist.AddRange((s.Playlist ?? new()).Where(File.Exists));
            _radioFavorites.Clear();
            _radioFavorites.AddRange((s.RadioFavorites ?? new()).Where(url => Uri.TryCreate(url, UriKind.Absolute, out _)));
            ShuffleToggle.IsChecked = s.Shuffle;
            RepeatToggle.IsChecked = s.Repeat;
            foreach (var item in PresetBox.Items.OfType<ComboBoxItem>())
                if (string.Equals(item.Tag?.ToString() ?? item.Content?.ToString(), s.Preset, StringComparison.OrdinalIgnoreCase))
                    PresetBox.SelectedItem = item;
        }
        catch { }
        finally { _loadingSettings = false; ApplyAudioControls(); RefreshMediaLists(); }
    }

    private string GetSelectedPreset()
        => (PresetBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()
           ?? (PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
           ?? "NORMAL";

    private void ToggleSoundLab_Click(object sender, RoutedEventArgs e)
    {
        ShowMediaPage(SoundLabPanel.Visibility == Visibility.Visible ? "now" : "cabin");
    }

    private void MediaNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page) ShowMediaPage(page);
    }

    private void ShowMediaPage(string page)
    {
        _mediaPage = page;
        NowPlayingPage.Visibility = page == "now" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = page == "library" ? Visibility.Visible : Visibility.Collapsed;
        RadioPage.Visibility = page == "radio" ? Visibility.Visible : Visibility.Collapsed;
        OnlinePage.Visibility = page == "online" ? Visibility.Visible : Visibility.Collapsed;
        var cabin = page == "cabin";
        SoundLabPanel.Visibility = cabin ? Visibility.Visible : Visibility.Collapsed;
        SoundLabColumn.Width = cabin ? new GridLength(410) : new GridLength(0);
        NowPlayingNav.Style = (Style)FindResource(page == "now" ? "NavActiveButton" : "NavButton");
        LibraryNav.Style = (Style)FindResource(page == "library" ? "NavActiveButton" : "NavButton");
        OnlineNav.Style = (Style)FindResource(page == "online" ? "NavActiveButton" : "NavButton");
        RadioNav.Style = (Style)FindResource(page == "radio" ? "NavActiveButton" : "NavButton");
        CabinNav.Style = (Style)FindResource(page == "cabin" ? "NavActiveButton" : "NavButton");
    }

    private void InitializeOnlineMedia()
    {
        _spotifyProvider = new SpotifyMediaProvider(_onlineHttp, _onlineSettings);
        _youtubeProvider = new YouTubeMediaProvider(_onlineHttp, _onlineSettings);
        SpotifyClientIdBox.Text = _onlineSettings.SpotifyClientId;
        RefreshOnlineStatus();
    }

    private async Task RestoreSpotifySessionAsync()
    {
        if (!_onlineSettings.SpotifyConfigured || string.IsNullOrWhiteSpace(_onlineSettings.SpotifyRefreshToken)) return;
        try
        {
            var auth = new SpotifyPkceAuthenticator(_onlineHttp, _onlineSettings);
            var token = await auth.RefreshAsync(_onlineSettings.SpotifyRefreshToken);
            _spotifyProvider ??= new SpotifyMediaProvider(_onlineHttp, _onlineSettings);
            _spotifyProvider.SetAccessToken(token.AccessToken);
            _onlineSettings.SpotifyRefreshToken = token.RefreshToken;
            _onlineSettings.Save();
            RefreshOnlineStatus();
        }
        catch { SpotifyOnlineStatus.Text = "SPOTIFY • RECONECTE A CONTA"; }
    }

    private void RefreshOnlineStatus()
    {
        SpotifyOnlineStatus.Text = !_onlineSettings.SpotifyConfigured ? "SPOTIFY • NÃO CONFIGURADO" : _spotifyProvider?.IsAuthenticated == true ? "SPOTIFY • CONECTADO" : "SPOTIFY • PRONTO PARA CONECTAR";
        YouTubeOnlineStatus.Text = _youtubeProvider?.IsConfigured == true ? "YOUTUBE • TRANSPOLI PRONTO" : "YOUTUBE • ENTRE NA CONTA TRANSPOLI";
    }

    private void SaveOnlineSettings_Click(object sender, RoutedEventArgs e)
    {
        _onlineSettings.SpotifyClientId = SpotifyClientIdBox.Text.Trim();
        _onlineSettings.Save();
        InitializeOnlineMedia();
        StatusText.Text = "ONLINE • CONFIGURAÇÃO SALVA";
    }

    private async void ConnectSpotify_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "SPOTIFY • AUTORIZANDO";
            var auth = new SpotifyPkceAuthenticator(_onlineHttp, _onlineSettings);
            var token = await auth.AuthenticateAsync();
            _spotifyProvider ??= new SpotifyMediaProvider(_onlineHttp, _onlineSettings);
            _spotifyProvider.SetAccessToken(token.AccessToken);
            _onlineSettings.SpotifyRefreshToken = token.RefreshToken;
            _onlineSettings.Save();
            RefreshOnlineStatus();
            StatusText.Text = "SPOTIFY • CONECTADO";
        }
        catch (Exception ex) { StatusText.Text = "SPOTIFY • " + ex.Message; }
    }

    private async void OnlineSearch_Click(object sender, RoutedEventArgs e)
    {
        var query = OnlineSearchBox.Text.Trim();
        if (query.Length < 2) { StatusText.Text = "ONLINE • DIGITE AO MENOS 2 CARACTERES"; return; }
        try
        {
            StatusText.Text = "ONLINE • BUSCANDO";
            var tasks = new List<Task<IReadOnlyList<OnlineMediaSearchResult>>>();
            if (_spotifyProvider?.IsAuthenticated == true) tasks.Add(_spotifyProvider.SearchAsync(query));
            if (_youtubeProvider?.IsConfigured == true) tasks.Add(_youtubeProvider.SearchAsync(query));
            var groups = tasks.Count == 0 ? Array.Empty<IReadOnlyList<OnlineMediaSearchResult>>() : await Task.WhenAll(tasks);
            _onlineResults.Clear();
            _onlineResults.AddRange(groups.SelectMany(x => x));
            OnlineResultsBox.Items.Clear();
            foreach (var item in _onlineResults)
                OnlineResultsBox.Items.Add($"{item.Provider}  •  {item.Title} — {item.Artist}");
            StatusText.Text = _onlineResults.Count == 0 ? "ONLINE • NENHUM RESULTADO / CONFIGURE UM SERVIÇO" : $"ONLINE • {_onlineResults.Count} RESULTADOS";
        }
        catch (Exception ex) { StatusText.Text = "ONLINE • FALHA • " + ex.Message; }
    }

    private async void OnlineResults_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var index = OnlineResultsBox.SelectedIndex;
        if (index < 0 || index >= _onlineResults.Count) return;
        var item = _onlineResults[index];
        NowPlayingText.Text = item.Title;
        ArtistText.Text = item.Artist;
        SourceText.Text = item.Provider + " • PLAYER OFICIAL";
        TrackMetaText.Text = item.Provider == "SPOTIFY" ? "Spotify Connect • DSP externo" : "YouTube • player incorporado";
        if (item.Provider == "SPOTIFY")
        {
            try
            {
                if (_spotifyProvider?.IsAuthenticated != true) { StatusText.Text = "SPOTIFY • CONECTE SUA CONTA"; return; }
                await _spotifyProvider.PlayAsync(item.PlaybackReference);
                _activeMediaProvider = "SPOTIFY";
                YouTubePlayerPanel.Visibility = Visibility.Collapsed;
                StatusText.Text = "TOCANDO • SPOTIFY";
                MediaSessionState.Publish(new MediaNowPlaying(item.Title,item.Artist,"SPOTIFY",VolumeSlider.Value,true,_externalPerspective?"OPEN AIR":"CABIN",item.Artwork));
            }
            catch (Exception ex) { StatusText.Text = "SPOTIFY • " + ex.Message; }
            return;
        }
        if (item.Provider == "YOUTUBE")
        {
            try
            {
                await EnsureYouTubePlayerAsync(item.Id);
                _activeMediaProvider = "YOUTUBE";
                _youtubePlaying = true;
                YouTubePlayerPanel.Visibility = Visibility.Visible;
                StatusText.Text = "TOCANDO • YOUTUBE";
                MediaSessionState.Publish(new MediaNowPlaying(item.Title,item.Artist,"YOUTUBE",VolumeSlider.Value,true,_externalPerspective?"OPEN AIR":"CABIN",item.Artwork,0,item.DurationSeconds));
            }
            catch (Exception ex) { StatusText.Text = "YOUTUBE • " + ex.Message; }
        }
    }

    private async Task EnsureYouTubePlayerAsync(string videoId)
    {
        await YouTubeWebView.EnsureCoreWebView2Async();
        if (!_youtubeBridgeAttached && YouTubeWebView.CoreWebView2 is not null)
        {
            YouTubeWebView.CoreWebView2.WebMessageReceived += YouTubeWebMessageReceived;
            _youtubeBridgeAttached = true;
        }
        var id = System.Text.Json.JsonSerializer.Serialize(videoId);
        var html = $@"<!doctype html><html><head><meta name='viewport' content='width=device-width,height=device-height,initial-scale=1'><style>html,body,#player{{width:100%;height:100%;margin:0;background:#080b0f;overflow:hidden}}</style></head><body><div id='player'></div><script src='https://www.youtube.com/iframe_api'></script><script>var player,timer;function send(){{if(!player||!window.chrome?.webview)return;try{{chrome.webview.postMessage(JSON.stringify({{kind:'state',state:player.getPlayerState(),position:player.getCurrentTime()||0,duration:player.getDuration()||0,volume:player.getVolume()||0}}));}}catch(e){{}}}}function onYouTubeIframeAPIReady(){{player=new YT.Player('player',{{width:'100%',height:'100%',videoId:{id},playerVars:{{'playsinline':1,'controls':1,'enablejsapi':1}},events:{{'onReady':function(e){{e.target.setVolume({VolumeSlider.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)});e.target.playVideo();clearInterval(timer);timer=setInterval(send,1000);send();}},'onStateChange':function(){{send();}}}}}});}}function tp(c,v){{if(!player)return;if(c==='play')player.playVideo();if(c==='pause')player.pauseVideo();if(c==='volume')player.setVolume(v);send();}}</script></body></html>";
        YouTubeWebView.NavigateToString(html);
    }

    private void YouTubeWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc=JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root=doc.RootElement;
            if(!root.TryGetProperty("kind",out var kind)||kind.GetString()!="state")return;
            var state=root.TryGetProperty("state",out var stateValue)?stateValue.GetInt32():-1;
            var position=root.TryGetProperty("position",out var positionValue)?positionValue.GetDouble():0;
            var duration=root.TryGetProperty("duration",out var durationValue)?durationValue.GetDouble():0;
            var volume=root.TryGetProperty("volume",out var volumeValue)?volumeValue.GetDouble():VolumeSlider.Value;
            _youtubePlaying=state==1;
            if(state!=0)_youtubeEndAdvancePending=false;
            MediaSessionState.Update(current=>current with { IsPlaying=_youtubePlaying, PositionSeconds=Math.Max(0,position), DurationSeconds=Math.Max(0,duration), Volume=Math.Clamp(volume,0,100) });
            if(state==0&&!_youtubeEndAdvancePending){_youtubeEndAdvancePending=true;_=PlayAdjacentOnlineAsync(1);}
        }
        catch { }
    }

    private async Task YouTubeCommandAsync(string command, double value=0)
    {
        if (YouTubeWebView.CoreWebView2 is null) return;
        await YouTubeWebView.ExecuteScriptAsync($"tp('{command}',{value.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    }

    private void SyncDriveControls()
    {
        if (DriveVolumeSlider is null) return;
        _syncingDriveControls = true;
        DriveVolumeSlider.Value = VolumeSlider.Value;
        DriveVolumeText.Text = $"{VolumeSlider.Value:0}%";
        UpdateQuickPresetState();
        _syncingDriveControls = false;
    }

    private void DriveVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DriveVolumeText is null) return;
        DriveVolumeText.Text = $"{e.NewValue:0}%";
        if (_syncingDriveControls || VolumeSlider is null) return;
        _syncingDriveControls = true;
        VolumeSlider.Value = e.NewValue;
        _syncingDriveControls = false;
        Player.Volume = e.NewValue / 100d;
        _cabinAudio.SetVolume(e.NewValue);
        PublishMediaSession();
        SaveSettings();
    }

    private void QuickPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string preset) return;
        foreach (var item in PresetBox.Items.OfType<ComboBoxItem>())
            if (string.Equals(item.Tag?.ToString(), preset, StringComparison.OrdinalIgnoreCase))
            {
                PresetBox.SelectedItem = item;
                break;
            }
        UpdateQuickPresetState();
    }

    private void UpdateQuickPresetState()
    {
        if (QuickOriginal is null) return;
        var preset = GetSelectedPreset();
        foreach (var button in new[] { QuickOriginal, QuickCabin, QuickBass, QuickNight })
            button.Style = (Style)FindResource(string.Equals(button.Tag?.ToString(), preset, StringComparison.OrdinalIgnoreCase) ? "NavActiveButton" : "NavButton");
    }

    private void PublishMediaSession(bool? playing = null)
    {
        MediaSessionState.Publish(new MediaNowPlaying(
            NowPlayingText?.Text ?? "",
            ArtistText?.Text ?? "",
            SourceText?.Text ?? "",
            VolumeSlider?.Value ?? 70,
            playing ?? (StatusText?.Text?.StartsWith("TOCANDO", StringComparison.OrdinalIgnoreCase) == true),
            _externalPerspective ? "OPEN AIR" : "CABIN"));
    }

    private void ApplyAudioControls()
    {
        _cabinAudio.SetManualEq(Eq65.Value, Eq145.Value, Eq850.Value, Eq3800.Value, Eq10500.Value);
        _cabinAudio.SetEffectIntensity(CabinIntensity.Value, SubIntensity.Value, AmbienceIntensity.Value);
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var preset = GetSelectedPreset();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new MediaSettings
            {
                StreamUrl = StreamUrlBox.Text.Trim(),
                Volume = VolumeSlider.Value,
                Preset = preset,
                Eq = new[] { Eq65.Value, Eq145.Value, Eq850.Value, Eq3800.Value, Eq10500.Value },
                CabinIntensity = CabinIntensity.Value,
                SubIntensity = SubIntensity.Value,
                AmbienceIntensity = AmbienceIntensity.Value,
                Playlist = _playlist.ToList(),
                RadioFavorites = _radioFavorites.ToList(),
                Shuffle = ShuffleToggle.IsChecked == true,
                Repeat = RepeatToggle.IsChecked == true
            }));
        }
        catch { }
    }

    private void PlaySource(Uri source, string title, string kind)
    {
        try
        {
            _usingCabinEngine = false;
            _cabinAudio.Unload();
            Player.Stop();
            Player.Source = source;
            Player.Volume = VolumeSlider.Value / 100d;
            Player.Play();
            NowPlayingText.Text = title;
            SourceText.Text = kind;
            _activeMediaProvider = "RADIO";
            StatusText.Text = "CONECTANDO";
            PublishMediaSession(true);
        }
        catch (Exception ex)
        {
            StatusText.Text = "ERRO";
            MessageBox.Show("Não foi possível abrir esta fonte.\n\n" + ex.Message, "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PlayStream_Click(object sender, RoutedEventArgs e)
    {
        var raw = StreamUrlBox.Text.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("Informe um endereço HTTP/HTTPS válido de rádio online.", "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SaveSettings();
        PlaySource(uri, "Rádio online", "STREAM • INTERNET");
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Adicionar músicas à fila",
            Multiselect = true,
            Filter = "Áudio compatível|*.mp3;*.wav;*.wma;*.m4a;*.aac|Todos os arquivos|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames)
            if (!_playlist.Contains(path, StringComparer.OrdinalIgnoreCase)) _playlist.Add(path);
        RefreshMediaLists();
        SaveSettings();
        if (_playlistIndex < 0 && _playlist.Count > 0) PlayPlaylistIndex(0);
    }

    private void PlayPlaylistIndex(int index)
    {
        if (_playlist.Count == 0) return;
        index = Math.Clamp(index, 0, _playlist.Count - 1);
        var path = _playlist[index];
        try
        {
            Player.Stop(); Player.Source = null;
            _usingCabinEngine = true;
            _playlistIndex = index;
            _cabinAudio.SetVolume(VolumeSlider.Value);
            _cabinAudio.SetPreset(GetSelectedPreset());
            ApplyAudioControls();
            _cabinAudio.OpenFile(path);
            ApplyTrackMetadata(path);
            TrackMetaText.Text = $"{Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} • {FormatDuration(_cabinAudio.Duration)}";
            SourceText.Text = $"FILA LOCAL • {index + 1}/{_playlist.Count} • CABIN AUDIO DSP";
            _activeMediaProvider = "LOCAL";
            StatusText.Text = "TOCANDO • DSP";
            DspStateText.Text = "DSP • ATIVO";
            PublishMediaSession(true);
            PlaylistBox.SelectedIndex = index;
            _queueRecoveryAttempts = 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = "FAIXA INDISPONÍVEL";
            DspStateText.Text = "DSP • AGUARDANDO";
            if (_playlist.Count > 1 && _queueRecoveryAttempts < _playlist.Count - 1)
            {
                _queueRecoveryAttempts++;
                var next = (index + 1) % _playlist.Count;
                PlayPlaylistIndex(next);
                return;
            }
            _queueRecoveryAttempts = 0;
            MessageBox.Show("Nenhuma faixa válida pôde ser reproduzida nesta sequência.\n\nÚltimo erro: " + ex.Message, "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;
        PlayPlaylistIndex(_playlistIndex <= 0 ? _playlist.Count - 1 : _playlistIndex - 1);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;
        if (ShuffleToggle.IsChecked == true && _playlist.Count > 1)
        {
            var next = Random.Shared.Next(_playlist.Count - 1);
            if (next >= _playlistIndex) next++;
            PlayPlaylistIndex(next);
            return;
        }
        PlayPlaylistIndex((_playlistIndex + 1 + _playlist.Count) % _playlist.Count);
    }

    private void AdvanceAfterTrackEnd()
    {
        if (_playlist.Count == 0) return;
        if (RepeatToggle.IsChecked == true && _playlistIndex >= 0) { PlayPlaylistIndex(_playlistIndex); return; }
        if (ShuffleToggle.IsChecked == true && _playlist.Count > 1)
        {
            var next = Random.Shared.Next(_playlist.Count - 1);
            if (next >= _playlistIndex) next++;
            PlayPlaylistIndex(next);
            return;
        }
        if (_playlistIndex >= _playlist.Count - 1)
        {
            StatusText.Text = "FILA FINALIZADA";
            return;
        }
        PlayPlaylistIndex(_playlistIndex + 1);
    }

    private void PlaylistBox_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistBox.SelectedIndex < 0) return;
        if (PlaylistBox.Tag is List<int> indexes && PlaylistBox.SelectedIndex < indexes.Count) PlayPlaylistIndex(indexes[PlaylistBox.SelectedIndex]);
    }

    private void RemoveTrack_Click(object sender, RoutedEventArgs e)
    {
        var selected = PlaylistBox.SelectedIndex;
        if (selected < 0) return;
        var index = PlaylistBox.Tag is List<int> indexes && selected < indexes.Count ? indexes[selected] : selected;
        if (index < 0 || index >= _playlist.Count) return;
        var removingCurrent = index == _playlistIndex;
        _playlist.RemoveAt(index);
        if (_playlist.Count == 0)
        {
            _playlistIndex = -1;
            if (removingCurrent) { _cabinAudio.Unload(); _usingCabinEngine = false; StatusText.Text = "FILA VAZIA"; }
        }
        else if (removingCurrent) PlayPlaylistIndex(Math.Min(index, _playlist.Count - 1));
        else if (index < _playlistIndex) _playlistIndex--;
        RefreshMediaLists();
        SaveSettings();
    }

    private void ClearPlaylist_Click(object sender, RoutedEventArgs e)
    {
        _cabinAudio.Unload();
        _usingCabinEngine = false;
        _playlist.Clear();
        _playlistIndex = -1;
        RefreshMediaLists();
        StatusText.Text = "FILA VAZIA";
        NowPlayingText.Text = "Nenhuma faixa selecionada";
        ArtistText.Text = "Adicione músicas para começar";
        DspStateText.Text = "DSP • PRONTO";
        SaveSettings();
    }

    private void RemoveRadioFavorite_Click(object sender, RoutedEventArgs e)
    {
        var index = RadioFavoritesBox.SelectedIndex;
        if (index < 0 || RadioFavoritesBox.SelectedItem is not string selectedUrl) return;
        _radioFavorites.RemoveAll(x => string.Equals(x, selectedUrl, StringComparison.OrdinalIgnoreCase));
        RefreshMediaLists();
        SaveSettings();
    }

    private void PlaybackModeChanged(object sender, RoutedEventArgs e) => SaveSettings();

    private void FavoriteRadio_Click(object sender, RoutedEventArgs e)
    {
        var url = StreamUrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        if (!_radioFavorites.Contains(url, StringComparer.OrdinalIgnoreCase)) _radioFavorites.Add(url);
        RefreshMediaLists();
        SaveSettings();
    }

    private void RadioFavoritesBox_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RadioFavoritesBox.SelectedItem is not string url) return;
        StreamUrlBox.Text = url;
        PlayStream_Click(sender, new RoutedEventArgs());
    }

    private void RefreshMediaLists()
    {
        var query = SearchBox?.Text?.Trim() ?? "";
        SearchHint.Visibility = string.IsNullOrWhiteSpace(query) ? Visibility.Visible : Visibility.Collapsed;
        var queueItems = _playlist.Select((path, index) => new { Path = path, Index = index, Label = BuildQueueLabel(path, index) })
            .Where(x => string.IsNullOrWhiteSpace(query) || x.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        PlaylistBox.ItemsSource = null;
        PlaylistBox.ItemsSource = queueItems.Select(x => x.Label).ToList();
        PlaylistBox.Tag = queueItems.Select(x => x.Index).ToList();
        QueueCountText.Text = string.IsNullOrWhiteSpace(query)
            ? (_playlist.Count == 1 ? "1 FAIXA" : $"{_playlist.Count} FAIXAS")
            : $"{queueItems.Count} ENCONTRADAS";
        PlaylistEmptyState.Visibility = queueItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var radios = _radioFavorites.Where(url => string.IsNullOrWhiteSpace(query) || url.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        RadioFavoritesBox.ItemsSource = null;
        RadioFavoritesBox.ItemsSource = radios;
        RadioFavoritesBox.Tag = radios;
        RadioEmptyState.Visibility = radios.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string BuildQueueLabel(string path, int index)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        var artist = "";
        try
        {
            var result = MediaFile.Read(path);
            if (result.IsSuccess && result.Tag is not null)
            {
                if (!string.IsNullOrWhiteSpace(result.Tag.Title)) title = result.Tag.Title;
                if (!string.IsNullOrWhiteSpace(result.Tag.Artist)) artist = result.Tag.Artist;
            }
        }
        catch { }
        return $"{index + 1:00}  •  {title}" + (string.IsNullOrWhiteSpace(artist) ? "" : $" — {artist}");
    }

    public sealed record PhoneCabinAudioState(string Preset, double Cabin, double Bass, double Ambience);

    public PhoneCabinAudioState GetPhoneCabinAudioState()
        => new(GetSelectedPreset(),CabinIntensity.Value,SubIntensity.Value,AmbienceIntensity.Value);

    public void MediaApplyCabinPreset(string preset)
    {
        preset=(preset??"").Trim().ToUpperInvariant();
        foreach(var item in PresetBox.Items.OfType<ComboBoxItem>())
            if(string.Equals(item.Tag?.ToString(),preset,StringComparison.OrdinalIgnoreCase)){PresetBox.SelectedItem=item;break;}
        UpdateQuickPresetState(); SaveSettings();
    }

    public void MediaAdjustCabinAudio(double cabinDelta,double bassDelta,double ambienceDelta)
    {
        CabinIntensity.Value=Math.Clamp(CabinIntensity.Value+cabinDelta,0,150);
        SubIntensity.Value=Math.Clamp(SubIntensity.Value+bassDelta,0,150);
        AmbienceIntensity.Value=Math.Clamp(AmbienceIntensity.Value+ambienceDelta,0,150);
        ApplyAudioControls(); SaveSettings();
    }

    public sealed record PhoneOnlineTrack(int Index, string Title, string Artist, string Artwork, double DurationSeconds);

    public async Task<IReadOnlyList<PhoneOnlineTrack>> MediaSearchYouTubeAsync(string query, CancellationToken cancellationToken=default)
    {
        query=(query??"").Trim();
        if(query.Length<2)return Array.Empty<PhoneOnlineTrack>();
        _youtubeProvider ??= new YouTubeMediaProvider(_onlineHttp,_onlineSettings);
        var items=await _youtubeProvider.SearchAsync(query,cancellationToken);
        _onlineResults.RemoveAll(x=>string.Equals(x.Provider,"YOUTUBE",StringComparison.OrdinalIgnoreCase));
        _onlineResults.AddRange(items);
        return _onlineResults.Select((item,index)=>new { item,index })
            .Where(x=>string.Equals(x.item.Provider,"YOUTUBE",StringComparison.OrdinalIgnoreCase))
            .Select(x=>new PhoneOnlineTrack(x.index,x.item.Title,x.item.Artist,x.item.Artwork,x.item.DurationSeconds)).ToList();
    }

    public async Task MediaPlayOnlineAsync(int index)
    {
        if(index<0||index>=_onlineResults.Count)return;
        var item=_onlineResults[index];
        if(!string.Equals(item.Provider,"YOUTUBE",StringComparison.OrdinalIgnoreCase))return;
        NowPlayingText.Text=item.Title; ArtistText.Text=item.Artist; SourceText.Text="YOUTUBE • PLAYER OFICIAL"; TrackMetaText.Text="YouTube • player incorporado";
        await EnsureYouTubePlayerAsync(item.Id);
        _activeMediaProvider="YOUTUBE"; _youtubePlaying=true; _youtubeEndAdvancePending=false; YouTubePlayerPanel.Visibility=Visibility.Visible; StatusText.Text="TOCANDO • YOUTUBE";
        MediaSessionState.Publish(new MediaNowPlaying(item.Title,item.Artist,"YOUTUBE",VolumeSlider.Value,true,_externalPerspective?"OPEN AIR":"CABIN",item.Artwork,0,item.DurationSeconds));
    }

    public sealed record PhoneLibraryTrack(int Index, string Title, string Artist, string Detail, bool IsCurrent);

    public IReadOnlyList<PhoneLibraryTrack> GetPhoneLibrary()
    {
        return _playlist.Select((path, index) =>
        {
            var title = Path.GetFileNameWithoutExtension(path);
            var artist = "";
            try
            {
                var result = MediaFile.Read(path);
                if (result.IsSuccess && result.Tag is not null)
                {
                    if (!string.IsNullOrWhiteSpace(result.Tag.Title)) title = result.Tag.Title;
                    if (!string.IsNullOrWhiteSpace(result.Tag.Artist)) artist = result.Tag.Artist;
                }
            }
            catch { }
            return new PhoneLibraryTrack(index, title, artist, Path.GetExtension(path).TrimStart('.').ToUpperInvariant(), index == _playlistIndex);
        }).ToList();
    }

    public bool MediaAddLocalFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Adicionar músicas ao SoundDrive",
            Multiselect = true,
            Filter = "Áudio compatível|*.mp3;*.wav;*.wma;*.m4a;*.aac|Todos os arquivos|*.*"
        };
        if (dialog.ShowDialog() != true) return false;
        foreach (var path in dialog.FileNames)
            if (!_playlist.Contains(path, StringComparer.OrdinalIgnoreCase)) _playlist.Add(path);
        RefreshMediaLists();
        SaveSettings();
        return true;
    }

    public void MediaPlayLocal(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;
        PlayPlaylistIndex(index);
    }

    public async void MediaPlayPause()
    {
        try
        {
            if (_activeMediaProvider == "SPOTIFY" && _spotifyProvider?.IsAuthenticated == true)
            {
                if (MediaSessionState.Current.IsPlaying) await _spotifyProvider.PauseAsync();
                else await _spotifyProvider.ResumeAsync();
                MediaSessionState.Update(x => x with { IsPlaying = !x.IsPlaying });
                return;
            }
            if (_activeMediaProvider == "YOUTUBE")
            {
                _youtubePlaying = !_youtubePlaying;
                await YouTubeCommandAsync(_youtubePlaying ? "play" : "pause");
                MediaSessionState.Update(x => x with { IsPlaying = _youtubePlaying });
                return;
            }
            var current = MediaSessionState.Current;
            if (current.IsPlaying) Pause_Click(this, new RoutedEventArgs());
            else Play_Click(this, new RoutedEventArgs());
        }
        catch (Exception ex) { StatusText.Text = "SOUNDDRIVE • " + ex.Message; }
    }

    public async void MediaNext()
    {
        try
        {
            if (_activeMediaProvider == "SPOTIFY" && _spotifyProvider?.IsAuthenticated == true) { await _spotifyProvider.NextAsync(); await RefreshSpotifyNowPlayingAsync(); StatusText.Text="SPOTIFY • PRÓXIMA"; return; }
            if (_activeMediaProvider == "YOUTUBE") { await PlayAdjacentOnlineAsync(1); return; }
            Next_Click(this,new RoutedEventArgs());
        }
        catch(Exception ex){StatusText.Text="SOUNDDRIVE • "+ex.Message;}
    }

    public async void MediaPrevious()
    {
        try
        {
            if (_activeMediaProvider == "SPOTIFY" && _spotifyProvider?.IsAuthenticated == true) { await _spotifyProvider.PreviousAsync(); await RefreshSpotifyNowPlayingAsync(); StatusText.Text="SPOTIFY • ANTERIOR"; return; }
            if (_activeMediaProvider == "YOUTUBE") { await PlayAdjacentOnlineAsync(-1); return; }
            Previous_Click(this,new RoutedEventArgs());
        }
        catch(Exception ex){StatusText.Text="SOUNDDRIVE • "+ex.Message;}
    }

    public async void MediaAdjustVolume(double delta)
    {
        var value=Math.Clamp(VolumeSlider.Value+delta,0,100);
        VolumeSlider.Value=value;
        try
        {
            if(_activeMediaProvider=="SPOTIFY"&&_spotifyProvider?.IsAuthenticated==true) await _spotifyProvider.SetVolumeAsync((int)Math.Round(value));
            else if(_activeMediaProvider=="YOUTUBE") await YouTubeCommandAsync("volume",value);
        }
        catch(Exception ex){StatusText.Text="VOLUME • "+ex.Message;}
    }

    private async Task RefreshSpotifyNowPlayingAsync()
    {
        if(_spotifyProvider is null)return;
        await Task.Delay(250);
        var item=await _spotifyProvider.GetCurrentAsync();
        if(item is null)return;
        NowPlayingText.Text=item.Title; ArtistText.Text=item.Artist; SourceText.Text="SPOTIFY • CONNECT";
        MediaSessionState.Publish(new MediaNowPlaying(item.Title,item.Artist,"SPOTIFY",VolumeSlider.Value,true,_externalPerspective?"OPEN AIR":"CABIN",item.Artwork));
    }

    private async Task PlayAdjacentOnlineAsync(int direction)
    {
        var current=MediaSessionState.Current;
        var candidates=_onlineResults.Where(x=>x.Provider=="YOUTUBE").ToList();
        if(candidates.Count==0)return;
        var index=candidates.FindIndex(x=>x.Title==current.Title&&x.Artist==current.Artist);
        if(index<0)index=0;
        var target=index+Math.Sign(direction);
        if(target<0){target=0;}
        if(target>=candidates.Count)
        {
            _youtubePlaying=false;
            _youtubeEndAdvancePending=false;
            StatusText.Text="YOUTUBE • FIM DA LISTA";
            MediaSessionState.Update(x=>x with { IsPlaying=false, PositionSeconds=x.DurationSeconds });
            return;
        }
        var item=candidates[target];
        NowPlayingText.Text=item.Title; ArtistText.Text=item.Artist; SourceText.Text="YOUTUBE • PLAYER OFICIAL";
        await EnsureYouTubePlayerAsync(item.Id);
        _activeMediaProvider="YOUTUBE"; _youtubePlaying=true; _youtubeEndAdvancePending=false; YouTubePlayerPanel.Visibility=Visibility.Visible;
        MediaSessionState.Publish(new MediaNowPlaying(item.Title,item.Artist,"YOUTUBE",VolumeSlider.Value,true,_externalPerspective?"OPEN AIR":"CABIN",item.Artwork,0,item.DurationSeconds));
    }

    private void Play_Click(object sender, RoutedEventArgs e) { if (_usingCabinEngine) _cabinAudio.Play(); else Player.Play(); StatusText.Text = _usingCabinEngine ? "TOCANDO • DSP" : "TOCANDO"; PublishMediaSession(true); }
    private void Pause_Click(object sender, RoutedEventArgs e) { if (_usingCabinEngine) _cabinAudio.Pause(); else Player.Pause(); StatusText.Text = "PAUSADO"; PublishMediaSession(false); }
    private void Stop_Click(object sender, RoutedEventArgs e) { if (_usingCabinEngine) _cabinAudio.Stop(); else Player.Stop(); StatusText.Text = "PARADO"; PublishMediaSession(false); }
    private void Close_Click(object sender, RoutedEventArgs e) { SaveSettings(); Close(); }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is null || Player is null) return;
        VolumeText.Text = $"{e.NewValue:0}%";
        if (!_syncingDriveControls && DriveVolumeSlider is not null)
        {
            _syncingDriveControls = true;
            DriveVolumeSlider.Value = e.NewValue;
            DriveVolumeText.Text = $"{e.NewValue:0}%";
            _syncingDriveControls = false;
        }
        Player.Volume = e.NewValue / 100d;
        _cabinAudio.SetVolume(e.NewValue);
        if (_activeMediaProvider == "YOUTUBE") _ = YouTubeCommandAsync("volume", e.NewValue);
        MediaSessionState.Update(current => current with { Volume = e.NewValue });
        SaveSettings();
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CabinInfoText is null) return;
        var preset = GetSelectedPreset();
        CabinInfoText.Text = preset switch
        {
            "CABINE" => "CABINE • DSP ativo: grave controlado, médios presentes e agudos suavizados",
            "SUBWOOFER" => "SUBWOOFER • DSP ativo: reforço forte de 70/160 Hz com proteção contra clipping",
            "NOTURNO" => "NOTURNO • DSP ativo: grave moderado e agudos reduzidos",
            _ => "NORMAL • áudio sem processamento adicional"
        };
        _cabinAudio.SetPreset(preset);
        UpdateQuickPresetState();
        SaveSettings();
    }

    public void UpdateCabinEnvironment(TelemetrySnapshot? data)
    {
        if (data is null)
        {
            _cabinAudio.SetEnvironment(false, false, 0, 0);
            TelemetryAudioText.Text = "TELEMETRIA • OFFLINE";
            return;
        }
        _cabinAudio.SetEnvironment(data.Connected, data.EngineEnabled, data.SpeedKph, data.Rpm);
        UpdateCameraPerspective(data);
        TelemetryAudioText.Text = data.Connected
            ? $"TELEMETRIA • {(data.EngineEnabled ? "MOTOR" : "IGNIÇÃO")} • {Math.Abs(data.SpeedKph):0} KM/H"
            : "TELEMETRIA • OFFLINE";
    }

    private void UpdateCameraPerspective(TelemetrySnapshot data)
    {
        if (!data.Connected) { _cameraBaselineReady = false; return; }
        if (!_cameraBaselineReady)
        {
            _baselineHeadX = data.HeadOffsetX; _baselineHeadY = data.HeadOffsetY; _baselineHeadZ = data.HeadOffsetZ;
            _cameraBaselineReady = true;
            _externalPerspective = false;
        }
        var dx = data.HeadOffsetX - _baselineHeadX;
        var dy = data.HeadOffsetY - _baselineHeadY;
        var dz = data.HeadOffsetZ - _baselineHeadZ;
        var displacement = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        var rotation = Math.Max(Math.Abs(data.HeadOffsetRotationX), Math.Max(Math.Abs(data.HeadOffsetRotationY), Math.Abs(data.HeadOffsetRotationZ)));
        // Histerese + confirmação por ticks evita alternância perto do limite.
        var enterExternal = displacement > 2.35 || rotation > 1.20;
        var returnCabin = displacement < 1.65 && rotation < 0.82;
        if (!_externalPerspective)
        {
            _externalCameraTicks = enterExternal ? _externalCameraTicks + 1 : 0;
            if (_externalCameraTicks < 2) return;
            _externalPerspective = true;
            _externalCameraTicks = 0;
        }
        else
        {
            _cabinCameraTicks = returnCabin ? _cabinCameraTicks + 1 : 0;
            if (_cabinCameraTicks < 2) return;
            _externalPerspective = false;
            _cabinCameraTicks = 0;
        }
        CameraAudioText.Text = _externalPerspective ? "PERSPECTIVA • EXTERNA" : "PERSPECTIVA • CABINE";
        DrivePerspectiveText.Text = _externalPerspective ? "OPEN AIR" : "CABIN";
        DrivePerspectiveDetail.Text = _externalPerspective ? "Campo aberto • reflexão de cabine reduzida" : "Som otimizado para dentro da cabine";
        _cabinAudio.SetCameraPerspective(_externalPerspective);
        PublishMediaSession();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshMediaLists();

    private void ApplyTrackMetadata(string path)
    {
        NowPlayingText.Text = Path.GetFileNameWithoutExtension(path);
        ArtistText.Text = "Sem artista informado";
        CoverImage.Source = null;
        CoverPlaceholder.Visibility = Visibility.Visible;
        try
        {
            var result = MediaFile.Read(path);
            if (!result.IsSuccess || result.Tag is null) return;
            var tag = result.Tag;
            if (!string.IsNullOrWhiteSpace(tag.Title)) NowPlayingText.Text = tag.Title;
            if (!string.IsNullOrWhiteSpace(tag.Artist)) ArtistText.Text = tag.Artist;
            var picture = tag.Pictures?.FirstOrDefault();
            if (picture is null) return;
            var bytes = picture.PictureData.ToArray();
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            CoverImage.Source = bitmap;
            CoverPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            // Metadados/capa nunca impedem a reprodução do áudio.
        }
    }

    private static string FormatDuration(TimeSpan value)
        => value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private void Player_MediaOpened(object sender, RoutedEventArgs e) => StatusText.Text = "TOCANDO";

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StatusText.Text = "FONTE INDISPONÍVEL";
        MessageBox.Show("O Windows não conseguiu reproduzir esta fonte. Algumas rádios usam codecs ou playlists que exigem um engine de áudio dedicado.\n\n" + e.ErrorException?.Message,
            "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}