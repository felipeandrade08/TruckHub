using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    // V15 é um observador LOCAL do estado operacional. A API não é uma fonte de
    // polling: TripSession/documentos/outbox já carregam o estado necessário.
    // Dois segundos continuam úteis para a UI sem consumir requests HTTP.
    private readonly DispatcherTimer _v15InvoiceFlowTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _v15InvoiceFlowStarted;
    private static readonly bool V15InvoiceFlowRegistered = RegisterV15InvoiceFlow();
    private string? _v15InvoicePromptTripId;
    private bool _v15InvoiceCheckBusy;

    private static bool RegisterV15InvoiceFlow()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V15InvoiceFlowLoaded), true);
        return true;
    }

    private static void V15InvoiceFlowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v15InvoiceFlowStarted) return;
        window._v15InvoiceFlowStarted = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window._v15InvoiceFlowTimer.Tick += async (_, _) => await window.PollV15InvoiceFlowAsync();
            window._v15InvoiceFlowTimer.Start();
            _ = window.PollV15InvoiceFlowAsync();
        }), DispatcherPriority.ContextIdle);
    }

    private async Task PollV15InvoiceFlowAsync()
    {
        if (_v15InvoiceCheckBusy) return;
        _v15InvoiceCheckBusy = true;
        try
        {
            // O carimbo é persistido antes de entrar na outbox. Portanto a UI deve
            // confiar no documento local, inclusive offline, e nunca consultar
            // /me/events a cada tick para descobrir o que ela mesma acabou de gravar.
            var operationTripId = string.IsNullOrWhiteSpace(_operationTripId) ? _localTripId : _operationTripId;
            var document = !string.IsNullOrWhiteSpace(_operationInvoiceId)
                ? _documents.Find(x => string.Equals(x.Id, _operationInvoiceId, StringComparison.OrdinalIgnoreCase))
                : !string.IsNullOrWhiteSpace(operationTripId)
                    ? _documents.FindLast(x => string.Equals(x.TripId, operationTripId, StringComparison.OrdinalIgnoreCase))
                    : null;

            var stamped = string.Equals(document?.Status, "Carimbado", StringComparison.OrdinalIgnoreCase);
            if (_tripActive || _tripDocumentPending)
            {
                if (stamped)
                {
                    TripStatusText.Text = "VIAGEM EM ANDAMENTO";
                    return;
                }

                TripStatusText.Text = "AGUARDANDO CARIMBO DA NOTA";
                StatusText.Text = "TransPoli • carimbe a nota fiscal para liberar a viagem.";

                var promptId = document?.Id ?? operationTripId ?? _serverTripId ?? "pending-trip";
                if (string.Equals(_v15InvoicePromptTripId, promptId, StringComparison.OrdinalIgnoreCase)) return;
                _v15InvoicePromptTripId = promptId;
                _invoiceTelemetry = LastTelemetry;
                ShowRealisticInvoiceModal();
                return;
            }

            _v15InvoicePromptTripId = null;
            if (DateTime.UtcNow - _lastTripFinishedAtUtc < TimeSpan.FromSeconds(12))
            {
                TripStatusText.Text = "CARGA ENTREGUE";
                TripLiveText.Text = "MONITORAMENTO ATIVO";
                TripProgressText.Text = "100%";
                TripRemainingText.Text = "0 km restantes";
                TripTruckText.Margin = new Thickness(Math.Max(-10, TripProgressFill.ActualWidth - 10), 0, 0, 0);
            }
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("V15Invoice.LocalFlow", ex);
        }
        finally
        {
            _v15InvoiceCheckBusy = false;
        }

        await Task.CompletedTask;
    }
}
