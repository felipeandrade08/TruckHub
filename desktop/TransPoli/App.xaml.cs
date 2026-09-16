using System.Windows;

namespace TransPoli;

public partial class App : Application
{
    private LicenseHeartbeat? _licenseHeartbeat;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _licenseHeartbeat = new LicenseHeartbeat();
        _licenseHeartbeat.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _licenseHeartbeat?.Dispose();
        _licenseHeartbeat = null;
        base.OnExit(e);
    }
}
