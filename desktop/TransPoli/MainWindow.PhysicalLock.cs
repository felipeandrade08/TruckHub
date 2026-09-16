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
    private int _physicalBrakeAttempts;

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

            // The parking brake is the physical safety mechanism. Only toggle
            // it when telemetry confirms that its state differs from the
            // logical TruckHub lock state. Never toggle merely because speed
            // is non-zero, otherwise a moving locked truck could turn the
            // brake back OFF.
            var brakeNeedsChange = locked ? !data.ParkingBrake : data.ParkingBrake;
            if (!brakeNeedsChange)
            {
                _physicalBrakeAttempts = 0;
                _lastPhysicalLockState = locked;
                return;
            }

            if (_lastPhysicalLockState != locked)
            {
                _physicalBrakeAttempts = 0;
                _lastPhysicalLockState = locked;
                _lastPhysicalBrakeCommand = DateTime.MinValue;
            }

            // Telemetry can take a few frames to reflect the key press. Retry
            // at most three times instead of endlessly toggling the brake.
            if (DateTime.UtcNow - _lastPhysicalBrakeCommand < TimeSpan.FromSeconds(1)) return;
            if (_physicalBrakeAttempts >= 3) return;
            if (!ApplyParkingBrakeKey()) return;

            _physicalBrakeAttempts++;
            _lastPhysicalBrakeCommand = DateTime.UtcNow;
        }
        catch
        {
            // A temporary connector/game-window failure must never crash the
            // main tablet loop.
        }
        finally
        {
            _physicalLockBusy = false;
        }
    }

    private bool ApplyParkingBrakeKey()
    {
        var gameWindow = FindTruckGameWindow();
        if (gameWindow == IntPtr.Zero) return false;

        var previousWindow = GetForegroundWindow();
        try
        {
            SetForegroundWindow(gameWindow);
            System.Threading.Thread.Sleep(35);
            keybd_event(ParkingBrakeKey, 0, 0, UIntPtr.Zero);
            keybd_event(ParkingBrakeKey, 0, 2, UIntPtr.Zero);
            System.Threading.Thread.Sleep(35);
            return true;
        }
        finally
        {
            if (previousWindow != IntPtr.Zero && previousWindow != gameWindow)
                SetForegroundWindow(previousWindow);
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
