using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace TransPoli;

// Tela inicial do tablet: alterna o card "Operação atual" entre viagem ativa e estado sem viagem.
// Sem viagem, o código existente deixa a origem como "—"; nesse caso mostramos uma mensagem clara
// com atalho para o mercado, em vez de traços soltos e barra de progresso vazia.
public partial class MainWindow
{
    private bool _tabletHomeInitialized;

    private void InitTabletHome()
    {
        if (_tabletHomeInitialized) return;
        _tabletHomeInitialized = true;
        try
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            descriptor?.AddValueChanged(TripOriginText, (_, _) => ApplyTripIdleLayout());
        }
        catch { /* sem o observador o card continua funcionando; só não alterna sozinho */ }
        ApplyTripIdleLayout();
    }

    private void ApplyTripIdleLayout()
    {
        try
        {
            var origin = (TripOriginText.Text ?? "").Trim();
            var idle = origin.Length == 0 || origin == "—";
            var active = idle ? Visibility.Collapsed : Visibility.Visible;

            TripEmptyState.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            TripRouteRow.Visibility = active;
            TripFooterRow.Visibility = active;
            TripProgressTrack.Visibility = active;
        }
        catch { /* layout da tela inicial nunca pode derrubar a atualização de telemetria */ }
    }
}
