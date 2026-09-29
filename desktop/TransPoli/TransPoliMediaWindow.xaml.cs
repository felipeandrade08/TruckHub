using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TransPoli.Audio;

namespace TransPoli;

public partial class TransPoliMediaWindow : Window
{
    private readonly CabinAudioEngine _cabinAudio = new();
    private bool _usingCabinEngine;
    private sealed class MediaSettings
    {
        public string StreamUrl { get; set; } = "";
        public double Volume { get; set; } = 70;
        public string Preset { get; set; } = "NORMAL";
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransPoli", "media-settings.json");

    public TransPoliMediaWindow()
    {
        InitializeComponent();
        LoadSettings();
        Closed += (_, _) => { try { Player.Stop(); Player.Source = null; _cabinAudio.Dispose(); } catch { } };
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = JsonSerializer.Deserialize<MediaSettings>(File.ReadAllText(SettingsPath));
            if (s is null) return;
            StreamUrlBox.Text = s.StreamUrl;
            VolumeSlider.Value = Math.Clamp(s.Volume, 0, 100);
            foreach (var item in PresetBox.Items.OfType<ComboBoxItem>())
                if (string.Equals(item.Content?.ToString(), s.Preset, StringComparison.OrdinalIgnoreCase))
                    PresetBox.SelectedItem = item;
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var preset = (PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NORMAL";
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new MediaSettings
            {
                StreamUrl = StreamUrlBox.Text.Trim(),
                Volume = VolumeSlider.Value,
                Preset = preset
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
            NowPlayingText.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
            SourceText.Text = "ARQUIVO LOCAL • CABIN AUDIO DSP";
            StatusText.Text = "TOCANDO • DSP";
        }
        catch (Exception ex)
        {
            _usingCabinEngine = false;
            StatusText.Text = "ERRO DSP";
            MessageBox.Show("Não foi possível reproduzir este arquivo pelo Cabin Audio.\\n\\n" + ex.Message, "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            "CABINE" => "CABINE • perfil preparado para grave controlado, agudos suaves e ambiência curta",
            "SUBWOOFER" => "SUBWOOFER • perfil preparado para reforço de baixa frequência e compressor",
            "NOTURNO" => "NOTURNO • perfil preparado para dinâmica reduzida e graves moderados",
            _ => "NORMAL • áudio sem processamento adicional"
        };
        _cabinAudio.SetPreset(preset);
        SaveSettings();
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e) => StatusText.Text = "TOCANDO";

    private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        StatusText.Text = "FONTE INDISPONÍVEL";
        MessageBox.Show("O Windows não conseguiu reproduzir esta fonte. Algumas rádios usam codecs ou playlists que exigem um engine de áudio dedicado.\n\n" + e.ErrorException?.Message,
            "TransPoli Media", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}