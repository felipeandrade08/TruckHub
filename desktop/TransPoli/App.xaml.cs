using System;
using System.IO;
using System.Windows;

namespace TransPoli;

public partial class App : Application
{
    private LicenseHeartbeat? _licenseHeartbeat;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            _licenseHeartbeat = new LicenseHeartbeat();
            _licenseHeartbeat.Start();

            var activation = new ActivationWindow();
            MainWindow = activation;
            activation.Show();
            activation.Activate();
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
            Shutdown(1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            WriteCrashLog("UnhandledException", ex);
    }

    internal static void WriteUiCrashLog(string source, Exception ex)
    {
        WriteCrashLog(source, ex);
    }

    private static void WriteCrashLog(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TransPoli");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "startup-crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n------------------------------\r\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _licenseHeartbeat?.Dispose();
        _licenseHeartbeat = null;
        base.OnExit(e);
    }
}
