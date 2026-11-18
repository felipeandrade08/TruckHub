using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    /// <summary>
    /// Reafirma o bloqueio enquanto a nota estiver pendente. O pedido de bloqueio antes só era feito uma vez,
    /// no instante em que a carga era detectada: reabrir o app (estado documental restaurado) deixava o caminhão
    /// livre sem carimbo. Agora a cada ciclo: nota pendente = bloqueado; sem pendência = liberado.
    /// </summary>
    private void EnforceDocumentVehicleLock()
    {
        try
        {
            var requested = _vehicleAuthorization.Snapshot.LockRequested;
            if (_tripDocumentPending && !_tripActive)
            {
                if (!requested) _vehicleAuthorization.RequestLock("DANFE pendente", "trip-document");
            }
            else if (requested && string.Equals(_vehicleAuthorization.CurrentSource, "trip-document", StringComparison.Ordinal))
            {
                _vehicleAuthorization.Authorize("document-cleared");
            }
        }
        catch { }
    }

    private void AxlesButton_Click(object sender, RoutedEventArgs e) => ShowAxlesModal();

    private void ShowAxlesModal()
    {
        if (EnsureModalHost() == null) return;
        var data = LastTelemetry;
        var panel = new StackPanel();
        var connected = data is { Connected: true };
        var pluginOk = _vehicleAuthorization.PluginReachable;

        panel.Children.Add(ModalHero("EIXOS", "Eixos levantáveis",
            "Levanta ou abaixa o eixo do caminhão e do reboque pelo plugin do jogo. O comando é o mesmo do botão do cockpit.",
            connected ? "JOGO CONECTADO" : "SEM JOGO", connected ? "Green" : "Yellow"));

        panel.Children.Add(AxleCard("CAMINHÃO", data?.LiftAxle == true, connected && pluginOk, false));
        panel.Children.Add(AxleCard("REBOQUE", data?.TrailerLiftAxle == true, connected && pluginOk, true));

        panel.Children.Add(ModalLine(!pluginOk
            ? "Controle indisponível: o canal com o plugin do jogo não abriu."
            : !connected ? "Abra o jogo e entre no caminhão para usar."
            : "Eixo levantado reduz o desgaste de pneus e o pedágio por eixo. O jogo só aceita levantar o eixo quando as condições do veículo permitem (carga/velocidade).", 11));
        ShowModalContent("axles", BuildModalCard("EIXOS", panel, "Plugin TruckHub • caminhão e reboque"));
    }

    private UIElement AxleCard(string title, bool lifted, bool enabled, bool trailer)
    {
        var box = new StackPanel();
        box.Children.Add(ModalValueRow(title, lifted ? "LEVANTADO" : "ABAIXADO", lifted ? "GoldBright" : "Green"));
        var button = ModalButton(lifted ? $"ABAIXAR EIXO • {title}" : $"LEVANTAR EIXO • {title}");
        button.IsEnabled = enabled;
        button.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!_vehicleAuthorization.PulseLiftAxle(trailer))
            {
                StatusText.Text = "TruckHub • não foi possível enviar o comando de eixo";
                return;
            }
            await Task.Delay(900);   // dá tempo do jogo trocar o estado e a telemetria chegar
            ShowAxlesModal();
        };
        box.Children.Add(button);
        return ModalPanel(box);
    }
}
