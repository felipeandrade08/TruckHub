using System.Windows.Controls;

namespace TransPoli;

public partial class MainWindow
{
    // Compatibilidade com módulos legados que ainda atualizam indicadores
    // removidos da composição visual atual. Os controles continuam disponíveis
    // para preservar o fluxo sem substituir o XAML principal.
    internal readonly TextBlock TripCargoText = new();
    private readonly TextBlock TripValueText = new();
    private readonly TextBlock TripProgressText2 = new();
    private readonly TextBlock TripDistanceLiveText2 = new();
    private readonly TextBlock TripRemainingText2 = new();
    private readonly TextBlock TripStartText = new();
    private readonly TextBlock TripEtaText = new();
    private readonly TextBlock TripEstimateNoteText = new();
    private readonly Border TripProgressFill2 = new();
    private readonly TextBlock TripTruckText2 = new();
}