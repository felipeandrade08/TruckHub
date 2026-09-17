using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace TransPoli;

internal sealed class ConnectorSupervisor : IDisposable
{
    private static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromMilliseconds(900) };
    private const string HealthUrl = "http://127.0.0.1:17877/health";
    private Process? _process;
    private bool _startedByTransPoli;
    private bool _stopping;
    private DateTime _lastStartAttemptUtc = DateTime.MinValue;
    private DateTime _lastRestartUtc = DateTime.MinValue;

    public bool IsRunning => IsConnectorRunning();

    public async Task<bool> StartAsync()
    {
        _stopping = false;

        if (IsConnectorRunning())
            return await IsHealthyAsync();

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
            return IsConnectorRunning() && await IsHealthyAsync();
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

        if (!IsConnectorRunning())
            return await StartAsync();

        if (await IsHealthyAsync())
            return true;

        if (DateTime.UtcNow - _lastRestartUtc < TimeSpan.FromSeconds(5))
            return false;

        return await RestartAsync();
    }

    public async Task<bool> RestartAsync()
    {
        if (_stopping || DateTime.UtcNow - _lastRestartUtc < TimeSpan.FromSeconds(5))
            return false;

        _lastRestartUtc = DateTime.UtcNow;
        StopOwnedConnector();
        await Task.Delay(250);
        return await StartAsync();
    }

    private static async Task<bool> IsHealthyAsync()
    {
        try
        {
            using var response = await HealthClient.GetAsync(HealthUrl);
            if (!response.IsSuccessStatusCode)
                return false;
            var body = await response.Content.ReadAsStringAsync();
            return body.Contains("\"ok\":true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stopping = true;
        StopOwnedConnector();
    }

    private void StopOwnedConnector()
    {
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
        _startedByTransPoli = false;
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
