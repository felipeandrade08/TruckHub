using System;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;

namespace TransPoli;

public partial class DriverPhoneWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
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