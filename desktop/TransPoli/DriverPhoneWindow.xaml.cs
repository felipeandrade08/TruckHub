using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;

namespace TransPoli;

public partial class DriverPhoneWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive)
    {
        PhoneConnectionText.Text = data.Connected ? "●  ETS2 CONECTADO" : "●  ETS2 OFFLINE";
        PhoneConnectionText.Foreground = new System.Windows.Media.SolidColorBrush(
            data.Connected ? System.Windows.Media.Color.FromRgb(78,229,155) : System.Windows.Media.Color.FromRgb(146,155,167));
        PhoneTripText.Text = tripActive ? "VIAGEM EM ANDAMENTO" : (data.OnJob ? "CONTRATO ETS2 DETECTADO" : "SEM VIAGEM ATIVA");
        var origin = string.IsNullOrWhiteSpace(data.SourceCity) ? "—" : data.SourceCity;
        var destination = string.IsNullOrWhiteSpace(data.DestinationCity) ? "—" : data.DestinationCity;
        PhoneRouteText.Text = data.OnJob ? $"{origin} → {destination}" : "Aguardando contrato";
        PhoneSpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";
    }

    private void App_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var app = b.Tag?.ToString() ?? "TransPoli";
        AppTitle.Text = app.ToUpperInvariant();
        AppStatus.Text = app switch { "Mensagens" => "Central de mensagens TransPoli", "Banco" => "Banco do motorista", "Documentos" => "Documentos da operação", "Viagens" => "Histórico de viagens", "Ranking" => "Ranking do motorista", "Alertas" => "Central de alertas", "Perfil" => "Perfil do motorista", "Garagem" => "Garagem TransPoli", _ => "Configurações do celular" };
        AppDescription.Text = "Este módulo está ligado à estrutura operacional do TransPoli. Dados fictícios não serão exibidos; os próximos passos conectam esta tela às fontes locais reais do desktop.";
        AppPanel.Visibility = Visibility.Visible;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => AppPanel.Visibility = Visibility.Collapsed;

    public DriverPhoneWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm");
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
    }
}