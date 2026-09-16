using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private readonly HttpClient _opsHttp = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _opsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<RefuelingRecord> _refuelings = new();
    private readonly List<StopRecord> _stops = new();
    private readonly List<OccurrenceRecord> _occurrences = new();
    private readonly List<DocumentRecord> _documents = new();
    private string _operationsPath = string.Empty;
    private float? _lastFuelLiters;
    private float _lastOdometer;
    private bool _fuelingCandidate;
    private float _fuelBefore;
    private float _fuelAfter;
    private float _fuelOdometer;
    private DateTime _fuelStartedAt;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        InitTransPoliOperations();
    }

    private void InitTransPoliOperations()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        _operationsPath = Path.Combine(folder, "transpoli-operations.json");
        LoadOperations();
        _opsTimer.Tick += async (_, _) => await PollOperationalTelemetry();
        _opsTimer.Start();
        UpdateOpsCounters();
    }

    private async Task PollOperationalTelemetry()
    {
        try
        {
            using var response = await _opsHttp.GetAsync("http://127.0.0.1:17877/telemetry");
            if (!response.IsSuccessStatusCode) return;
            await using var stream = await response.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;
            DetectAutomaticRefueling(data);
            _lastOdometer = data.OdometerKm;
            UpdateOperationsAlert(data);
        }
        catch { }
    }

    private void DetectAutomaticRefueling(TelemetrySnapshot data)
    {
        var fuel = data.FuelLiters;
        var stopped = Math.Abs(data.SpeedKph) < 0.5f;
        var increase = _lastFuelLiters.HasValue ? fuel - _lastFuelLiters.Value : 0f;
        if (!_fuelingCandidate && stopped && increase >= 4f)
        {
            _fuelingCandidate = true;
            _fuelBefore = _lastFuelLiters ?? Math.Max(0, fuel - increase);
            _fuelStartedAt = DateTime.UtcNow;
            _fuelOdometer = data.OdometerKm;
        }
        if (_fuelingCandidate && fuel >= _fuelBefore + 2f)
        {
            _fuelAfter = fuel;
            var liters = _fuelAfter - _fuelBefore;
            if (liters >= 3f)
            {
                var duplicate = _refuelings.Any(x => Math.Abs(x.FuelAfter - _fuelAfter) < 0.2f && Math.Abs(x.OdometerKm - _fuelOdometer) < 0.5f && DateTime.UtcNow - x.RecordedAtUtc < TimeSpan.FromMinutes(2));
                if (!duplicate)
                {
                    Dispatcher.Invoke(() => RegisterDetectedRefueling(data, liters));
                    _fuelingCandidate = false;
                }
            }
        }
        if (!stopped && _fuelingCandidate && increase < 0.5f) _fuelingCandidate = false;
        _lastFuelLiters = fuel;
    }

    private void RegisterDetectedRefueling(TelemetrySnapshot data, float liters)
    {
        var station = PromptText("ABASTECIMENTO DETECTADO", $"Foram detectados {liters:0.0} L abastecidos automaticamente.\n\nInforme o nome do posto:", "Posto");
        if (station is null) return;
        var location = PromptText("LOCAL DO ABASTECIMENTO", "Informe a cidade/localização do posto:", data.DestinationCity ?? data.SourceCity ?? "");
        if (location is null) return;
        _refuelings.Add(new RefuelingRecord { Id = Guid.NewGuid().ToString("N"), RecordedAtUtc = DateTime.UtcNow, Station = station, Location = location, Liters = liters, FuelBefore = _fuelBefore, FuelAfter = _fuelAfter, OdometerKm = _fuelOdometer, Truck = $"{data.TruckBrand} {data.TruckModel}".Trim(), LicensePlate = data.LicensePlate ?? "" });
        SaveOperations(); UpdateOpsCounters();
        StatusText.Text = $"TransPoli • abastecimento registrado • {liters:0.0} L • {station}";
    }

    private void UpdateOperationsAlert(TelemetrySnapshot data)
    {
        if (data.FuelRangeKm > 0 && data.FuelRangeKm < 80)
        {
            AlertText.Text = "⛽ AUTONOMIA BAIXA • planeje abastecimento";
            AlertText.Foreground = FindResource("Yellow") as System.Windows.Media.Brush;
        }
        else if (_fuelingCandidate)
        {
            AlertText.Text = "⛽ ABASTECIMENTO DETECTADO • registrando operação";
            AlertText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        }
        FuelAutoText.Text = _fuelingCandidate ? "Abastecimento automático: operação detectada" : $"Abastecimento automático: monitorando • {data.FuelLiters:0.0} L";
    }

    private void FuelButton_Click(object sender, RoutedEventArgs e)
    {
        var w = CreateListWindow("⛽ ABASTECIMENTOS TRANS POLI", "A telemetria detecta aumento de combustível com o veículo parado; o motorista informa apenas posto e local.");
        var panel = new StackPanel { Margin = new Thickness(18) };
        foreach (var x in _refuelings.OrderByDescending(x => x.RecordedAtUtc).Take(20)) panel.Children.Add(Line($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Liters:0.0} L • {x.Station} • {x.Location}\nOdômetro {x.OdometerKm:0.0} km • {x.FuelBefore:0.0} → {x.FuelAfter:0.0} L", 11));
        if (!_refuelings.Any()) panel.Children.Add(Line("Nenhum abastecimento registrado ainda.", 12));
        w.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.ShowDialog();
    }

    private void StopsButton_Click(object sender, RoutedEventArgs e)
    {
        var type = Choose("🛑 REGISTRAR PARADA", new[] { "Descanso", "Abastecimento", "Refeição", "Manutenção", "Carga/Descarga", "Documentação", "Trânsito", "Outro" });
        if (type is null) return;
        var note = PromptText("DETALHE DA PARADA", "Observação (opcional):", "") ?? "";
        _stops.Add(new StopRecord { Id = Guid.NewGuid().ToString("N"), Type = type, Note = note, StartedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer });
        SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • parada registrada • {type}";
    }

    private void OccurrenceButton_Click(object sender, RoutedEventArgs e)
    {
        var type = Choose("⚠ REGISTRAR OCORRÊNCIA", new[] { "Acidente", "Avaria", "Problema mecânico", "Problema com carga", "Atraso", "Observação" });
        if (type is null) return;
        var details = PromptText("DETALHE DA OCORRÊNCIA", "Descreva o ocorrido:", "");
        if (details is null) return;
        _occurrences.Add(new OccurrenceRecord { Id = Guid.NewGuid().ToString("N"), Type = type, Details = details, RecordedAtUtc = DateTime.UtcNow, OdometerKm = _lastOdometer });
        SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • ocorrência registrada • {type}";
    }

    private void DocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        var type = Choose("📄 DOCUMENTO / NOTA", new[] { "Aguardando nota", "Recebido", "Conferido", "Carimbado", "Despachado" });
        if (type is null) return;
        var reference = PromptText("REFERÊNCIA", "Número da nota, documento ou observação:", "");
        if (reference is null) return;
        _documents.Add(new DocumentRecord { Id = Guid.NewGuid().ToString("N"), Status = type, Reference = reference, RecordedAtUtc = DateTime.UtcNow });
        SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • documento atualizado • {type}";
        ShowDocumentsHistory();
    }

    private void ShowDocumentsHistory()
    {
        var w = CreateListWindow("📄 HISTÓRICO DE DOCUMENTOS", "Fluxo operacional da documentação da carga.");
        var panel = new StackPanel { Margin = new Thickness(18) };
        foreach (var x in _documents.OrderByDescending(x => x.RecordedAtUtc).Take(30)) panel.Children.Add(Line($"{x.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {x.Status}\n{x.Reference}", 11));
        if (!_documents.Any()) panel.Children.Add(Line("Nenhum documento registrado.", 12));
        w.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        w.ShowDialog();
    }

    private void SummaryButton_Click(object sender, RoutedEventArgs e)
    {
        var distance = _tripActive ? Math.Max(0, _lastOdometer - _tripStartOdometer) : 0;
        var fuelUsed = _tripActive ? Math.Max(0, _tripStartFuel - (_lastFuelLiters ?? _tripStartFuel)) : 0;
        var text = $"RESUMO OPERACIONAL TRANS POLI\n\nViagem ativa: {(_tripActive ? "SIM" : "NÃO")}\nDistância registrada: {distance:0.0} km\nCombustível consumido: {fuelUsed:0.0} L\nAbastecimentos: {_refuelings.Count}\nParadas: {_stops.Count}\nOcorrências: {_occurrences.Count}\nDocumentos: {_documents.Count}\n\nÚltimo odômetro: {_lastOdometer:0.0} km";
        MessageBox.Show(text, "Resumo da viagem • TransPoli", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => StatusText.Text = "Tablet TransPoli • painel principal";

    private void UpdateOpsCounters()
    {
        if (OpsCounterText != null) OpsCounterText.Text = $"⛽ {_refuelings.Count} abastecimentos  •  🛑 {_stops.Count} paradas  •  ⚠ {_occurrences.Count} ocorrências  •  📄 {_documents.Count} documentos";
    }

    private void SaveOperations()
    {
        try { File.WriteAllText(_operationsPath, JsonSerializer.Serialize(new OperationsState { Refuelings = _refuelings, Stops = _stops, Occurrences = _occurrences, Documents = _documents }, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    private void LoadOperations()
    {
        try
        {
            if (!File.Exists(_operationsPath)) return;
            var state = JsonSerializer.Deserialize<OperationsState>(File.ReadAllText(_operationsPath));
            if (state is null) return;
            _refuelings.AddRange(state.Refuelings ?? new()); _stops.AddRange(state.Stops ?? new()); _occurrences.AddRange(state.Occurrences ?? new()); _documents.AddRange(state.Documents ?? new());
        }
        catch { }
    }

    private static Window CreateListWindow(string title, string subtitle)
    {
        var w = new Window { Title = title, Width = 650, Height = 520, MinWidth = 520, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Bg"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text") };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(18,18,18,4) });
        root.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(18,0,18,10), TextWrapping = TextWrapping.Wrap });
        w.Content = root; return w;
    }

    private static TextBlock Line(string text, double size) => new() { Text = text, FontSize = size, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text"), Margin = new Thickness(0,0,0,10), TextWrapping = TextWrapping.Wrap };

    private static string? PromptText(string title, string prompt, string initial)
    {
        var w = new Window { Title = title, Width = 460, Height = 210, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Bg"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text") };
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,10) });
        var input = new TextBox { Text = initial, FontSize = 15, Padding = new Thickness(8), Background = (System.Windows.Media.Brush)Application.Current.FindResource("Panel2"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text") };
        root.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,15,0,0) };
        string? result = null;
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(14,7,14,7), Margin = new Thickness(0,0,8,0) };
        var ok = new Button { Content = "Registrar", Padding = new Thickness(14,7,14,7) };
        cancel.Click += (_, _) => w.DialogResult = false; ok.Click += (_, _) => { result = input.Text.Trim(); w.DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(ok); root.Children.Add(buttons); w.Content = root; w.ShowDialog(); return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string? Choose(string title, IEnumerable<string> options)
    {
        var w = new Window { Title = title, Width = 460, Height = 430, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = (System.Windows.Media.Brush)Application.Current.FindResource("Bg"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text") };
        var root = new StackPanel { Margin = new Thickness(18) }; string? result = null;
        foreach (var option in options) { var b = new Button { Content = option, Padding = new Thickness(12,9,12,9), Margin = new Thickness(0,0,0,7), HorizontalContentAlignment = HorizontalAlignment.Left }; b.Click += (_, _) => { result = option; w.DialogResult = true; }; root.Children.Add(b); }
        var cancel = new Button { Content = "Cancelar", Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,8,0,0) }; cancel.Click += (_, _) => w.DialogResult = false; root.Children.Add(cancel); w.Content = root; w.ShowDialog(); return result;
    }
}

public sealed class OperationsState
{
    public List<RefuelingRecord>? Refuelings { get; set; }
    public List<StopRecord>? Stops { get; set; }
    public List<OccurrenceRecord>? Occurrences { get; set; }
    public List<DocumentRecord>? Documents { get; set; }
}

public sealed class RefuelingRecord
{
    public string Id { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public string Station { get; set; } = "";
    public string Location { get; set; } = "";
    public float Liters { get; set; }
    public float FuelBefore { get; set; }
    public float FuelAfter { get; set; }
    public float OdometerKm { get; set; }
    public string Truck { get; set; } = "";
    public string LicensePlate { get; set; } = "";
}

public sealed class StopRecord
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public float OdometerKm { get; set; }
}

public sealed class OccurrenceRecord
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public float OdometerKm { get; set; }
}
