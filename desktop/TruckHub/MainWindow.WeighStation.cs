using System;

namespace TruckHub;

// Balança: registro do histórico (independe da HUD estar visível) e jogo ativo para os textos da interface.
public partial class MainWindow
{
    private bool _wsLastFined;
    private long _wsLastFineAmount;

    private void ProcessWeighStation(TelemetrySnapshot data)
    {
        try
        {
            TpGame.Update(data.Game);
            if (!data.Connected) { _wsLastFined = false; return; }

            var fineEdge = data.Fined && !_wsLastFined;
            var amountChanged = data.FineAmount > 0 && data.FineAmount != _wsLastFineAmount;
            if ((fineEdge || amountChanged) && data.FineAmount > 0 && WeighStation.IsWeighOffence(data.FineOffence))
                WeighStation.Register(data);   // ignora duplicata automaticamente

            _wsLastFined = data.Fined;
            if (data.FineAmount > 0) _wsLastFineAmount = data.FineAmount;
        }
        catch { /* a balança nunca pode derrubar o ciclo de telemetria */ }
    }
}
