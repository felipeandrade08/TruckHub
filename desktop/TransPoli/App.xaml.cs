using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace TransPoli;

public partial class App : Application
{
    private const string SingleInstanceMutexName=@"Local\TransPoli.Desktop.SingleInstance";
    private Mutex? _singleInstanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            _singleInstanceMutex=new Mutex(true,SingleInstanceMutexName,out var isFirstInstance);
            if(!isFirstInstance)
            {
                WriteLifecycleLog("Startup", "Instância secundária ignorada; TransPoli já está em execução.");
                Shutdown(0);
                return;
            }
        }
        catch(Exception ex)
        {
            WriteCrashLog("SingleInstance",ex);
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
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

    internal static void WriteLifecycleLog(string source,string message)
    {
        try
        {
            var dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransPoli");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir,"lifecycle.log"),$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source} • {message}\r\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex=null;
        base.OnExit(e);
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

}
