using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Prevents the legacy 10-second auto-finish path from winning the race before
/// the V1.0.15 reconciler can close the server trip and settle the economy.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _v15FinishGuardTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _v15FinishGuardStarted;

    private static readonly bool V15FinishGuardRegistration = RegisterV15FinishGuard();

    private static bool RegisterV15FinishGuard()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V15FinishGuardLoaded), true);
        return true;
    }

    private static void V15FinishGuardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v15FinishGuardStarted) return;
        window._v15FinishGuardStarted = true;
        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            window._v15FinishGuardTimer.Tick += async (_, _) => await window.RunV15FinishGuardAsync();
            window._v15FinishGuardTimer.Start();
        }), DispatcherPriority.ContextIdle);
    }

    private async Task RunV15FinishGuardAsync()
    {
        if (!_tripActive || string.IsNullOrWhiteSpace(_serverTripId)) return;
        try
        {
            using var response = await _v15Http.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is not null && data.Connected && (data.JobDelivered || data.JobFinished))
                _jobMissingTicks = 0;
        }
        catch { }
    }
}
