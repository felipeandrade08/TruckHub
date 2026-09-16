using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private const byte ParkingBrakeKey = 0x20;
    private DispatcherTimer? _physicalLockTimer;
    private bool? _lastPhysicalLockState;
    private DateTime _lastPhysicalBrakeCommand = DateTime.MinValue;
    private bool _physicalLockBusy;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _physicalLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _physicalLockTimer.Tick += async (_, _) => await EnforcePhysicalLockAsync();
        _physicalLockTimer.Start();
    }

    private async Task EnforcePhysicalLockAsync()
    {
        if (_physicalLockBusy) return;
        _physicalLockBusy = true;
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(900) };
            using var response = await client.GetAsync("http://127.0.0.1:17877/telemetry");
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;

            var locked = _truckLocked;
            var brakeNeedsChange = locked ? !data.ParkingBrake : data.ParkingBrake;
            var movingWhileLocked = locked && Math.Abs(data.SpeedKph) > 0.5f;
            if (!brakeNeedsChange && !movingWhileLocked) return;
            if (_lastPhysicalLockState == locked && DateTime.UtcNow - _lastPhysicalBrakeCommand < TimeSpan.FromSeconds(1)) return;

            await Dispatcher.InvokeAsync(ApplyParkingBrakeKey);
            _lastPhysicalLockState = locked;
            _lastPhysicalBrakeCommand = DateTime.UtcNow;
        }
        catch { }
        finally { _physicalLockBusy = false; }
    }

    private void ApplyParkingBrakeKey()
    {
        var previousWindow = GetForegroundWindow();
        var gameWindow = FindTruckGameWindow();
        if (gameWindow == IntPtr.Zero) return;
        try
        {
            SetForegroundWindow(gameWindow);
            System.Threading.Thread.Sleep(35);
            keybd_event(ParkingBrakeKey, 0, 0, UIntPtr.Zero);
            keybd_event(ParkingBrakeKey, 0, 2, UIntPtr.Zero);
            System.Threading.Thread.Sleep(35);
        }
        finally
        {
            if (previousWindow != IntPtr.Zero && previousWindow != gameWindow) SetForegroundWindow(previousWindow);
        }
    }

    private static IntPtr FindTruckGameWindow()
    {
        foreach (var name in new[] { "eurotrucks2", "amtrucks" })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
