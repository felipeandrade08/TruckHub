namespace TransPoli;

/// <summary>
/// Compatibility marker for the former runtime feature injector.
/// Navigation is now declared explicitly in MainWindow.xaml so the tablet no longer
/// depends on VisualTree text matching, duplicated buttons, or periodic UI discovery.
/// </summary>
public partial class MainWindow
{
    private static readonly bool _tabletFeatureIntegration = TabletFeatureIntegration.Register();
}

internal static class TabletFeatureIntegration
{
    internal static bool Register() => true;
}
