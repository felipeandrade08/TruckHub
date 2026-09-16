using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TransPoli;

internal sealed class ConnectorSupervisor : IDisposable
{
    private Process? _process;
    private bool _startedByTransPoli;
    private bool _stopping;
    private DateTime _lastStartAttemptUtc = DateTime.MinValue;

    public bool IsRunning => IsConnectorRunning();

    public async Task<bool> StartAsync()
    {
        _stopping = false;

        if (IsConnectorRunning())
            return true;

        if (DateTime.UtcNow - _lastStartAttemptUtc < TimeSpan.FromSeconds(2))
            return false;

        var exe = Path.Combine(AppContext.BaseDirectory, "TransPoliConnector.exe");
        if (!File.Exists(exe))
            return false;

        _lastStartAttemptUtc = DateTime.UtcNow;

        try
        {
            _process?.Dispose();
            _process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            _startedByTransPoli = _process != null;
            await Task.Delay(500);
            return IsConnectorRunning();
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnsureRunningAsync()
    {
        if (_stopping)
            return false;

        if (IsConnectorRunning())
            return true;

        return await StartAsync();
    }

    public void Dispose()
    {
        _stopping = true;

        if (!_startedByTransPoli)
            return;

        try
        {
            foreach (var process in Process.GetProcessesByName("TransPoliConnector"))
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch { }

        _process?.Dispose();
        _process = null;
    }

    private static bool IsConnectorRunning()
    {
        try
        {
            return Process.GetProcessesByName("TransPoliConnector").Any();
        }
        catch
        {
            return false;
        }
    }
}
