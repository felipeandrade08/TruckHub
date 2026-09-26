using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly TransPoliCargoOperations _cargoOperations = new();
}

public enum CargoLifecycle
{
    AGUARDANDO_CARGA, DOCUMENTACAO, CARGA_RECEBIDA, CONFERIDA, LIBERADA, PRONTO_PARA_SAIR,
    EM_VIAGEM, PARADO, RETOMANDO, ENTREGA, CARGA_ENTREGUE, FINALIZADA, CAMINHAO_BLOQUEADO
}

public sealed class CargoTimelineEntry
{
    public DateTime AtUtc { get; set; }
    public CargoLifecycle Lifecycle { get; set; }
    public string Details { get; set; } = string.Empty;
}

public sealed class CargoOperationState
{
    public CargoLifecycle Lifecycle { get; set; } = CargoLifecycle.AGUARDANDO_CARGA;
    public string Route { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public float SpeedKph { get; set; }
    public bool CargoLoaded { get; set; }
    public bool TripActive { get; set; }
    public bool Updated { get; set; }
    public DateTime LastUpdateUtc { get; set; }
    public DateTime LastTransitionUtc { get; set; }
}

public sealed class CargoPersistence
{
    public CargoOperationState State { get; set; } = new();
    public List<CargoTimelineEntry> Timeline { get; set; } = new();
}

public sealed class DocumentRecord
{
    public string Id { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
    public DateTime? StampedAtUtc { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string CargoKey { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string Driver { get; set; } = string.Empty;
    public string Truck { get; set; } = string.Empty;
    public string TruckBrand { get; set; } = string.Empty;
    public string TruckModel { get; set; } = string.Empty;
    public string LicensePlate { get; set; } = string.Empty;
    public float CargoMassKg { get; set; }
    public float OdometerKm { get; set; }
    public float PlannedDistanceKm { get; set; }
    public decimal CargoValueBrl { get; set; }
    public float CargoDamage { get; set; }
    public string SourceCity { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public string SourceCompany { get; set; } = string.Empty;
    public string DestinationCompany { get; set; } = string.Empty;
}

public sealed class TransPoliCargoOperations
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<CargoTimelineEntry> _timeline = new();
    private readonly HashSet<Button> _wired = new();
    private readonly string? _path;
    private CargoOperationState _state = new();
    private bool _hooked;
    private bool _wasActive;
    private bool _wasStopped;

    public TransPoliCargoOperations()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        var owner = SecureTokenStore.ReadUserId();
        _path = string.IsNullOrWhiteSpace(owner)
            ? null
            : Path.Combine(folder, $"transpoli-cargo-operation-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(owner))).ToLowerInvariant()[..16]}.json");
        if (_path != null) Load();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Application.Current?.Dispatcher.BeginInvoke(new Action(Hook), DispatcherPriority.Loaded);
    }

    public void Dispose()
    {
        _timer.Stop();
        _wired.Clear();
        _timeline.Clear();
    }

    private void Hook()
    {
        if (_hooked) return;
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null) return;
        _hooked = true;
        main.Loaded += (_, _) => Wire(main);
        main.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F8) { Open(main); e.Handled = true; } };
        Wire(main);
    }

    private void Wire(MainWindow main)
    {
        // A abertura é centralizada no DocumentModal/class handler. Não adicionamos
        // um segundo Click handler aqui, evitando a tela modal + janela externa duplicada.
        foreach (var b in FindButtons(main))
        {
            var text = b.Content?.ToString() ?? string.Empty;
            if (text.Contains("DOCUMENTOS", StringComparison.OrdinalIgnoreCase) || text.Contains("CARGA", StringComparison.OrdinalIgnoreCase))
                _wired.Add(b);
        }
    }

    private void Tick()
    {
        var main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main is null) return;
        Wire(main);
        var active = GetField(main, "_tripActive", false);
        var locked = GetField(main, "_truckLocked", false);
        var speed = ParseSpeed(main.SpeedText?.Text);
        var cargoText = Clean(main.CargoText?.Text);
        var route = Clean(main.TripRouteText?.Text);
        var cargoLoaded = active || (!string.IsNullOrWhiteSpace(cargoText) && !cargoText.Contains("Nenhuma", StringComparison.OrdinalIgnoreCase));
        if (active && !_wasActive) Transition(CargoLifecycle.EM_VIAGEM, "Viagem iniciada automaticamente pela telemetria.", main);
        if (active && speed < 0.5f && !_wasStopped) { _wasStopped = true; Transition(CargoLifecycle.PARADO, "Veículo parado durante a viagem.", main); }
        else if (active && speed >= 1.0f && _wasStopped) { _wasStopped = false; Transition(CargoLifecycle.RETOMANDO, "Movimento retomado após parada.", main); Transition(CargoLifecycle.EM_VIAGEM, "Viagem retomada.", main); }
        if (!active && _wasActive)
        {
            if (!cargoLoaded) { Transition(CargoLifecycle.CARGA_ENTREGUE, "A carga deixou de estar carregada; entrega detectada.", main); Transition(CargoLifecycle.FINALIZADA, "Operação de carga finalizada.", main); }
            else Transition(CargoLifecycle.PRONTO_PARA_SAIR, "Viagem encerrada enquanto a carga permanece carregada.", main);
        }
        if (locked && !active) SetStateIfDifferent(CargoLifecycle.CAMINHAO_BLOQUEADO, "Caminhão bloqueado pelo computador de bordo.");
        else if (!active && !cargoLoaded) SetStateIfDifferent(CargoLifecycle.AGUARDANDO_CARGA, "Aguardando nova carga.");
        else if (!active && cargoLoaded && (_state.Lifecycle == CargoLifecycle.AGUARDANDO_CARGA || _state.Lifecycle == CargoLifecycle.CARGA_ENTREGUE || _state.Lifecycle == CargoLifecycle.FINALIZADA)) SetStateIfDifferent(CargoLifecycle.CARGA_RECEBIDA, "Carga identificada no computador de bordo.");
        _state.Route = route; _state.Cargo = cargoText; _state.SpeedKph = speed; _state.CargoLoaded = cargoLoaded; _state.TripActive = active; _state.Updated = true; _state.LastUpdateUtc = DateTime.UtcNow; Save(); _wasActive = active;
    }

    public void Open(MainWindow main)
    {
        main.ShowOperationalModal("cargo");
    }

    private UIElement Cycle(MainWindow main)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        foreach (var s in new[] { CargoLifecycle.AGUARDANDO_CARGA, CargoLifecycle.DOCUMENTACAO, CargoLifecycle.CARGA_RECEBIDA, CargoLifecycle.CONFERIDA, CargoLifecycle.LIBERADA, CargoLifecycle.PRONTO_PARA_SAIR, CargoLifecycle.EM_VIAGEM, CargoLifecycle.PARADO, CargoLifecycle.RETOMANDO, CargoLifecycle.ENTREGA, CargoLifecycle.CARGA_ENTREGUE, CargoLifecycle.FINALIZADA })
        {
            var selected = s == _state.Lifecycle;
            panel.Children.Add(new TextBlock { Text = (selected ? "● " : "○ ") + Label(s), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        }
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Documents(MainWindow main)
    {
        var docs = GetField(main, "_documents", new List<DocumentRecord>());
        var panel = new StackPanel { Margin = new Thickness(10) };
        foreach (var d in docs.OrderByDescending(x => x.RecordedAtUtc).Take(50)) Row(panel, main, "📄 " + d.Status, $"{d.RecordedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss} • {d.Reference}");
        if (docs.Count == 0) Row(panel, main, "DOCUMENTOS", "Nenhum documento registrado.");
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Timeline(MainWindow main)
    {
        var panel = new StackPanel { Margin = new Thickness(10) };
        foreach (var x in _timeline.OrderByDescending(x => x.AtUtc).Take(150)) Row(panel, main, $"{x.AtUtc.ToLocalTime():dd/MM HH:mm:ss} • {Label(x.Lifecycle)}", x.Details);
        if (_timeline.Count == 0) Row(panel, main, "LINHA DO TEMPO", "Nenhuma transição registrada ainda.");
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void Transition(CargoLifecycle next, string details, MainWindow main)
    {
        if (_state.Lifecycle == next && _timeline.Count > 0 && (DateTime.UtcNow - _timeline[0].AtUtc).TotalSeconds < 3) return;
        _state.Lifecycle = next; _state.LastTransitionUtc = DateTime.UtcNow;
        var entry = new CargoTimelineEntry { AtUtc = DateTime.UtcNow, Lifecycle = next, Details = details };
        _timeline.Insert(0, entry);
        try { if (LocalData.Current is { } store) new LocalOperationsRepository(store.Db).AppendCargoTimeline(entry); } catch (Exception ex) { App.WriteUiCrashLog("CargoOperations.AppendTimeline", ex); }
        if (_timeline.Count > 300) _timeline.RemoveRange(300, _timeline.Count - 300);
        if (main.StatusText != null) main.StatusText.Text = "TransPoli • " + Label(next);
        Save();
    }

    private void SetStateIfDifferent(CargoLifecycle next, string details)
    {
        if (_state.Lifecycle == next) return;
        _state.Lifecycle = next; _state.LastTransitionUtc = DateTime.UtcNow;
        _timeline.Insert(0, new CargoTimelineEntry { AtUtc = DateTime.UtcNow, Lifecycle = next, Details = details });
        if (_timeline.Count > 300) _timeline.RemoveRange(300, _timeline.Count - 300);
        try { if (LocalData.Current is { } store) new LocalOperationsRepository(store.Db).UpsertCargo(_state); } catch (Exception ex) { App.WriteUiCrashLog("CargoOperations.UpsertState", ex); }
    }

    private void Load()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_path) || !File.Exists(_path)) { _state = new CargoOperationState(); return; }
            var data = JsonSerializer.Deserialize<CargoPersistence>(File.ReadAllText(_path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _state = data?.State ?? new CargoOperationState();
            _timeline.Clear(); if (data?.Timeline != null) _timeline.AddRange(data.Timeline);
        }
        catch (Exception ex) { App.WriteUiCrashLog("CargoOperations.Load", ex); _state = new CargoOperationState(); _timeline.Clear(); }
    }

    private void Save()
    {
        try { if (!string.IsNullOrWhiteSpace(_path)) File.WriteAllText(_path, JsonSerializer.Serialize(new CargoPersistence { State = _state, Timeline = _timeline }, new JsonSerializerOptions { WriteIndented = true })); } catch (Exception ex) { App.WriteUiCrashLog("CargoOperations.SaveFile", ex); }
        try { if (LocalData.Current is { } store) new LocalOperationsRepository(store.Db).UpsertCargo(_state); } catch (Exception ex) { App.WriteUiCrashLog("CargoOperations.SaveDatabase", ex); }
    }

    private static T GetField<T>(object target, string name, T fallback)
    {
        var value = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);
        return value is T typed ? typed : fallback;
    }

    private static float ParseSpeed(string? text)
    {
        if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return Math.Abs(value);
        if (float.TryParse(text, out value)) return Math.Abs(value);
        return 0;
    }

    private static string Clean(string? text) => string.IsNullOrWhiteSpace(text) ? "" : text.Trim();

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        if (root == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button b) yield return b;
            foreach (var nested in FindButtons(child)) yield return nested;
        }
    }

    private static void Row(Panel panel, MainWindow main, string title, string details)
    {
        var b = new Border { Background = Brush(main, "Panel"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 7) };
        var p = new StackPanel(); p.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold });
        p.Children.Add(new TextBlock { Text = details, FontSize = 11, Foreground = Brush(main, "Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        b.Child = p; panel.Children.Add(b);
    }

    private static SolidColorBrush Brush(MainWindow main, string key) => main.FindResource(key) as SolidColorBrush ?? Brushes.White;

    private static string Label(CargoLifecycle s) => s switch
    {
        CargoLifecycle.AGUARDANDO_CARGA => "AGUARDANDO CARGA", CargoLifecycle.DOCUMENTACAO => "DOCUMENTAÇÃO", CargoLifecycle.CARGA_RECEBIDA => "CARGA RECEBIDA", CargoLifecycle.CONFERIDA => "CONFERIDA", CargoLifecycle.LIBERADA => "LIBERADA", CargoLifecycle.PRONTO_PARA_SAIR => "PRONTO PARA SAIR", CargoLifecycle.EM_VIAGEM => "EM VIAGEM", CargoLifecycle.PARADO => "PARADO", CargoLifecycle.RETOMANDO => "RETOMANDO", CargoLifecycle.ENTREGA => "ENTREGA", CargoLifecycle.CARGA_ENTREGUE => "CARGA ENTREGUE", CargoLifecycle.FINALIZADA => "FINALIZADA", CargoLifecycle.CAMINHAO_BLOQUEADO => "CAMINHÃO BLOQUEADO", _ => s.ToString()
    };
}
