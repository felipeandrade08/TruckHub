using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// V1.0.14 stability layer.
/// Centralizes the desktop refresh loop, prevents telemetry/network calls from
/// overlapping, keeps the operational alert stable, and makes Phase G module
/// buttons route to the real tablet modules instead of the old placeholder
/// description handler.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _v14CycleTimer;
    private bool _v14CycleBusy;
    private bool _v14AlertGuard;
    private string _v14LastStableAlert = string.Empty;
    private bool _v14Initialized;

    private static readonly bool V14StabilityRegistered = RegisterV14Stability();

    private static bool RegisterV14Stability()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(V14Loaded), true);
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(V14PhaseGClick), true);
        return true;
    }

    private static void V14Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v14Initialized) return;
        window._v14Initialized = true;
        window._timer.Stop();
        window._opsTimer.Interval = TimeSpan.FromSeconds(2);
        window.AttachV14AlertGuard();
        window.TagPhaseGButtons();

        window._v14CycleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        window._v14CycleTimer.Tick += async (_, _) => await window.RunV14CycleAsync();
        window._v14CycleTimer.Start();
        _ = window.RunV14CycleAsync();

        window.Dispatcher.BeginInvoke(new Action(() =>
        {
            try { OperationsCenterPhaseH.Register(window); } catch { }
            window.TagPhaseGButtons();
        }), DispatcherPriority.Loaded);
    }

    private async Task RunV14CycleAsync()
    {
        if (_v14CycleBusy) return;
        _v14CycleBusy = true;
        try
        {
            await _connector.EnsureRunningAsync();
            await RefreshTelemetry();
        }
        catch { }
        finally
        {
            _v14CycleBusy = false;
        }
    }

    private void AttachV14AlertGuard()
    {
        if (AlertText is null) return;
        AlertText.TextChanged -= V14AlertTextChanged;
        AlertText.TextChanged += V14AlertTextChanged;
        _v14LastStableAlert = AlertText.Text ?? string.Empty;
    }

    private void V14AlertTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_v14AlertGuard || sender is not TextBlock block) return;
        var value = block.Text ?? string.Empty;
        if (string.Equals(value, "Nenhum alerta operacional ativo", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(_v14LastStableAlert) && !string.Equals(_v14LastStableAlert, value, StringComparison.Ordinal))
            {
                _v14AlertGuard = true;
                block.Text = _v14LastStableAlert;
                _v14AlertGuard = false;
            }
            return;
        }
        _v14LastStableAlert = value;
    }

    private void TagPhaseGButtons()
    {
        foreach (var button in FindVisualChildren<Button>(this))
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (text.Contains("CENTRAL", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("GARAGEM", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("BANCO", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("VIAGEM", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ABAST.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("NOTAS", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ESTAT.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("MANUT.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("EMPR.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("HIST.", StringComparison.OrdinalIgnoreCase))
            {
                if (button.Tag is null) button.Tag = "phaseg-v14";
            }
        }
    }

    private static void V14PhaseGClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button || !Equals(button.Tag, "phaseg-v14")) return;
        if (Window.GetWindow(button) is not MainWindow window) return;

        var text = button.Content?.ToString() ?? string.Empty;
        e.Handled = true;

        if (text.Contains("GARAGEM", StringComparison.OrdinalIgnoreCase)) { window.ShowGarageTabletModal(); return; }
        if (text.Contains("BANCO", StringComparison.OrdinalIgnoreCase)) { window.ShowBankModal(); return; }
        if (text.Contains("VIAGEM", StringComparison.OrdinalIgnoreCase)) { window.ShowOperationalModal("cargo"); return; }
        if (text.Contains("ABAST.", StringComparison.OrdinalIgnoreCase)) { window.ShowOperationalModal("fuel"); return; }
        if (text.Contains("NOTAS", StringComparison.OrdinalIgnoreCase)) { InvokePhasePrivate(window, "_phaseI", "OpenNotesAsync"); return; }
        if (text.Contains("HIST.", StringComparison.OrdinalIgnoreCase)) { InvokePhasePrivate(window, "_phaseI", "OpenHistory"); return; }
        if (text.Contains("ESTAT.", StringComparison.OrdinalIgnoreCase)) { InvokePhasePrivate(window, "_phaseJ", "OpenAsync"); return; }
        if (text.Contains("MANUT.", StringComparison.OrdinalIgnoreCase)) { window.ShowOperationalModal("summary"); return; }
        if (text.Contains("EMPR.", StringComparison.OrdinalIgnoreCase)) { window.ShowBankModal(); return; }

        if (text.Contains("CENTRAL", StringComparison.OrdinalIgnoreCase))
        {
            window.ClosePhaseGModule();
            try
            {
                OperationsCenterPhaseH.Register(window);
                button.Tag = "phaseh-v14";
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                button.Tag = "phaseg-v14";
            }
            catch { button.Tag = "phaseg-v14"; }
        }
    }

    private static void InvokePhasePrivate(MainWindow window, string fieldName, string methodName)
    {
        try
        {
            var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var module = field?.GetValue(window);
            var method = module?.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method is null) return;
            var result = method.Invoke(module, null);
            if (result is Task task) _ = task;
        }
        catch { }
    }
}
