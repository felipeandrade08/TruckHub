using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// FASE B — monitora a ponte local de telemetria e recupera automaticamente
/// uma sessão que estava conectada e perdeu o fluxo do ETS2.
/// Não substitui o ConnectorSupervisor: complementa a supervisão de processo
/// detectando também o caso em que o processo continua vivo, mas a telemetria
/// deixa de atualizar.
/// </summary>
public partial class MainWindow
{
    private static readonly DispatcherTimer TelemetryResilienceTimer = CreateTelemetryResilienceTimer();
    private static readonly HttpClient TelemetryResilienceHttp = new() { Timeout = TimeSpan.FromSeconds(2) };
    private bool _telemetryWasConnected;
    private int _telemetryDropTicks;
    private DateTime _lastTelemetryRecoveryUtc = DateTime.MinValue;

    private static DispatcherTimer CreateTelemetryResilienceTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            if (Application.Current?.MainWindow is MainWindow window)
                await window.MonitorTelemetryResilienceAsync();
        };
        timer.Start();
        return timer;
    }

    private async Task MonitorTelemetryResilienceAsync()
    {
        try
        {
            using var response = await TelemetryResilienceHttp.GetAsync(TelemetryUrl);
            if (!response.IsSuccessStatusCode)
            {
                RegisterTelemetryDrop();
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var connected = root.TryGetProperty("Connected", out var connectedUpper)
                ? connectedUpper.GetBoolean()
                : root.TryGetProperty("connected", out var connectedLower) && connectedLower.GetBoolean();

            if (connected)
            {
                _telemetryWasConnected = true;
                _telemetryDropTicks = 0;
                return;
            }

            // Só tenta recuperar automaticamente depois que uma sessão real
            // já estava conectada. Assim, iniciar o TruckHub sem o ETS2 aberto
            // não cria um ciclo de reinicialização desnecessário.
            if (_telemetryWasConnected)
                RegisterTelemetryDrop();
        }
        catch
        {
            if (_telemetryWasConnected)
                RegisterTelemetryDrop();
        }
    }

    private async void RegisterTelemetryDrop()
    {
        _telemetryDropTicks++;
        if (_telemetryDropTicks < 3)
            return;

        if (DateTime.UtcNow - _lastTelemetryRecoveryUtc < TimeSpan.FromSeconds(10))
            return;

        _lastTelemetryRecoveryUtc = DateTime.UtcNow;
        _telemetryDropTicks = 0;
        _telemetryWasConnected = false;

        try
        {
            var recovered = await _connector.RestartAsync();
            if (recovered)
                StatusText.Text = "ETS2 • telemetria reconectada automaticamente";
        }
        catch { }
    }
}
