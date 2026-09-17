using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// O motorista não aceita ofertas. A tarifa exibida é vinculada à carga
/// atualmente engatada e identificada pela telemetria do ETS2/ATS.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _v15CargoBindingTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _v15CargoBindingStarted;

    private void StartV15CargoBinding()
    {
        if (_v15CargoBindingStarted) return;
        _v15CargoBindingStarted = true;
        _v15CargoBindingTimer.Tick += async (_, _) => await RefreshV15CargoBindingAsync();
        _v15CargoBindingTimer.Start();
        _ = RefreshV15CargoBindingAsync();
    }

    private async Task RefreshV15CargoBindingAsync()
    {
        try
        {
            var telemetry = await LoadV15TelemetryAsync();
            var cargo = telemetry?.Cargo?.Trim();
            if (string.IsNullOrWhiteSpace(cargo) || _v15CleanupCargo == null || _v15CleanupRate == null) return;

            var match = _v15CleanupOffers.FirstOrDefault(item => string.Equals(item.Cargo, cargo, StringComparison.OrdinalIgnoreCase));
            _v15CleanupCargo.Text = $"📦 {cargo}";
            _v15CleanupRate.Text = match == default
                ? "Tarifa: aguardando cadastro no mercado"
                : $"R$ {match.Rate:0.00}/km";
        }
        catch { }
    }
}
