namespace TransPoli;

/// <summary>
/// Legacy Phase I compatibility shell.
/// The former VisualTree scanner and parallel blue host were retired after
/// trip history moved to Central de Viagens. No periodic UI polling remains.
/// </summary>
public partial class MainWindow
{
    private readonly TabletPhaseI _phaseI = new();
}

public sealed class TabletPhaseI : System.IDisposable
{
    public void Dispose() { }
}
