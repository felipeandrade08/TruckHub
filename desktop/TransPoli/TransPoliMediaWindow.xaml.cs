using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using TagLibSharp2.Core;
using TransPoli.Audio;

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
    private sealed class MediaSettings
    {
        public string StreamUrl { get; set; } = "";
        public double Volume { get; set; } = 70;
        public string Preset { get; set; } = "NORMAL";
        public double[] Eq { get; set; } = new double[5];
        public double CabinIntensity { get; set; } = 100;
        public double SubIntensity { get; set; } = 100;
        public double AmbienceIntensity { get; set; } = 100;
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransPoli", "media-settings.json");

    public TransPoliMediaWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeComponent();
        _cabinAudio.LevelsChanged += (left, right) => { _vuLeft = left; _vuRight = right; };
        _mediaUiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _mediaUiTimer.Tick += (_, _) => RefreshMediaUi();
        _mediaUiTimer.Start();
        LoadHotkeySettings();
        LoadSettings();
        Closed += (_, _) => { try { _mediaUiTimer.Stop(); Player.Stop(); Player.Source = null; _cabinAudio.Dispose(); } catch { } };
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
            "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Information);
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
            foreach (var item in PresetBox.Items.OfType<ComboBoxItem>())
                if (string.Equals(item.Content?.ToString(), s.Preset, StringComparison.OrdinalIgnoreCase))
                    PresetBox.SelectedItem = item;
        }
        catch { }
        finally { _loadingSettings = false; ApplyAudioControls(); }
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
            var preset = (PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NORMAL";
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new MediaSettings
            {
                StreamUrl = StreamUrlBox.Text.Trim(),
                Volume = VolumeSlider.Value,
                Preset = preset,
                Eq = new[] { Eq65.Value, Eq145.Value, Eq850.Value, Eq3800.Value, Eq10500.Value },
                CabinIntensity = CabinIntensity.Value,
                SubIntensity = SubIntensity.Value,
                AmbienceIntensity = AmbienceIntensity.Value
            }));
        }
        catch { }
    }

    private void PlaySource(Uri source, string title, string kind)
    {
        try
        {
            _usingCabinEngine = false;
            _cabinAudio.Stop();
            Player.Stop();
            Player.Source = source;
            Player.Volume = VolumeSlider.Value / 100d;
            Player.Play();
            NowPlayingText.Text = title;
            SourceText.Text = kind;
            StatusText.Text = "CONECTANDO";
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
            Title = "Escolher música",
            Filter = "Áudio compatível|*.mp3;*.wav;*.wma;*.m4a;*.aac|Todos os arquivos|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Player.Stop();
            Player.Source = null;
            _usingCabinEngine = true;
            _cabinAudio.SetVolume(VolumeSlider.Value);
            _cabinAudio.SetPreset((PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NORMAL");
            _cabinAudio.OpenFile(dialog.FileName);
            ApplyTrackMetadata(dialog.FileName);
            TrackMetaText.Text = $"{Path.GetExtension(dialog.FileName).TrimStart('.').ToUpperInvariant()} • {FormatDuration(_cabinAudio.Duration)}";
            SourceText.Text = "ARQUIVO LOCAL • CABIN AUDIO DSP";
            StatusText.Text = "TOCANDO • DSP";
        }
        catch (Exception ex)
        {
            _usingCabinEngine = false;
            StatusText.Text = "ERRO DSP";
            MessageBox.Show("Não foi possível reproduzir este arquivo pelo Cabin Audio.\n\n" + ex.Message, "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e) { if (_usingCabinEngine) _cabinAudio.Play(); else Player.Play(); StatusText.Text = _usingCabinEngine ? "TOCANDO • DSP" : "TOCANDO"; }
    private void Pause_Click(object sender, RoutedEventArgs e) { if (_usingCabinEngine) _cabinAudio.Pause(); else Player.Pause(); StatusText.Text = "PAUSADO"; }
    private void Stop_Click(object sender, RoutedEventArgs e) { if (_usingCabinEngine) _cabinAudio.Stop(); else Player.Stop(); StatusText.Text = "PARADO"; }
    private void Close_Click(object sender, RoutedEventArgs e) { SaveSettings(); Close(); }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText is null || Player is null) return;
        VolumeText.Text = $"{e.NewValue:0}%";
        Player.Volume = e.NewValue / 100d;
        _cabinAudio.SetVolume(e.NewValue);
        SaveSettings();
    }

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CabinInfoText is null) return;
        var preset = (PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NORMAL";
        CabinInfoText.Text = preset switch
        {
            "CABINE" => "CABINE • DSP ativo: grave controlado, médios presentes e agudos suavizados",
            "SUBWOOFER" => "SUBWOOFER • DSP ativo: reforço forte de 70/160 Hz com proteção contra clipping",
            "NOTURNO" => "NOTURNO • DSP ativo: grave moderado e agudos reduzidos",
            _ => "NORMAL • áudio sem processamento adicional"
        };
        _cabinAudio.SetPreset(preset);
        SaveSettings();
    }

    public void UpdateCabinEnvironment(TelemetrySnapshot? data)
    {
        if (data is null)
        {
            _cabinAudio.SetEnvironment(false, false, 0, 0);
            return;
        }
        _cabinAudio.SetEnvironment(data.Connected, data.EngineEnabled, data.SpeedKph, data.Rpm);
    }

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