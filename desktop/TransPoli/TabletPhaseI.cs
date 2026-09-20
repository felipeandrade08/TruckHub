using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Fase I — Notas + Histórico.
/// Mantém os dois módulos dentro da tela física do tablet, sem novo atalho.
/// Notas usam persistência no servidor; histórico usa os registros de viagens
/// já capturados pelo computador de bordo local.
/// </summary>
public partial class MainWindow
{
    private readonly TabletPhaseI _phaseI = new();
}

public sealed class TabletPhaseI
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _hookTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<Button> _wired = new();
    private bool _hooked;
    private MainWindow? _main;
    private Grid? _screen;
    private Border? _panel;
    private StackPanel? _body;
    private TextBlock? _title;
    private TextBlock? _status;

    public TabletPhaseI()
    {
        _hookTimer.Tick += (_, _) => Hook();
        _hookTimer.Start();
        Application.Current?.Dispatcher.BeginInvoke(new Action(Hook), DispatcherPriority.Loaded);
    }

    private void Hook()
    {
        if (_main is null) _main = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
        if (_main is null) return;
        _screen ??= FindTabletScreenGrid(_main);
        if (_screen is null) return;
        if (!_hooked) BuildHost();
        foreach (var button in FindVisualChildren<Button>(_screen))
        {
            if (_wired.Contains(button)) continue;
            var text = button.Content?.ToString() ?? "";
            if (text.Contains("NOTAS", StringComparison.OrdinalIgnoreCase))
            {
                _wired.Add(button);
                button.Click += async (_, _) => await OpenNotesAsync();
            }
            else if (text.Contains("HIST", StringComparison.OrdinalIgnoreCase))
            {
                _wired.Add(button);
                button.Click += (_, _) => OpenHistory();
            }
        }
    }

    private void BuildHost()
    {
        if (_screen is null) return;
        _hooked = true;
        _panel = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(16),
            Background = Brush("#0A1828"),
            BorderBrush = Brush("#1D3B57"),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _title = new TextBlock { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        header.Children.Add(_title);
        var close = Button("← VOLTAR", 10);
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1); header.Children.Add(close);
        root.Children.Add(header);
        _body = new StackPanel();
        Grid.SetRow(_body, 1); root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _panel.Child = root;
        Panel.SetZIndex(_panel, 2000);
        _screen.Children.Add(_panel);
    }

    private async Task OpenNotesAsync()
    {
        Show("NOTAS DO MOTORISTA");
        if (_body is null) return;
        _body.Children.Clear();
        var title = new TextBox { Text = "", FontSize = 13, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8), Background = Brush("#101C29"), Foreground = Brushes.White, BorderBrush = Brush("#294761") };
        title.ToolTip = "Título da nota";
        var content = new TextBox { FontSize = 13, Padding = new Thickness(10), MinHeight = 110, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#101C29"), Foreground = Brushes.White, BorderBrush = Brush("#294761") };
        content.ToolTip = "Escreva sua anotação...";
        var save = Button("＋ SALVAR NOTA", 10);
        var status = new TextBlock { FontSize = 11, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 8, 0, 10), TextWrapping = TextWrapping.Wrap };
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            status.Text = "Salvando...";
            var ok = await SaveNoteAsync(title.Text, content.Text);
            status.Text = ok ? "Nota salva." : "Não foi possível salvar a nota. Verifique a conexão.";
            save.IsEnabled = true;
            if (ok) { title.Clear(); content.Clear(); await RenderNotesAsync(); }
        };
        _body.Children.Add(new TextBlock { Text = "TÍTULO", FontSize = 9, Foreground = Brush("#AAB6C3") });
        _body.Children.Add(title);
        _body.Children.Add(new TextBlock { Text = "ANOTAÇÃO", FontSize = 9, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 4, 0, 4) });
        _body.Children.Add(content);
        _body.Children.Add(save);
        _body.Children.Add(status);
        await RenderNotesAsync();
    }

    private async Task RenderNotesAsync()
    {
        if (_body is null) return;
        var notes = await LoadNotesAsync();
        _body.Children.Add(new TextBlock { Text = "NOTAS SALVAS", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 6, 0, 8) });
        if (notes.Count == 0)
        {
            _body.Children.Add(Card("Ainda não há notas salvas."));
            return;
        }
        foreach (var note in notes)
        {
            var card = new Border { Background = Brush("#101C29"), BorderBrush = Brush("#1D3B57"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = note.Title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            stack.Children.Add(new TextBlock { Text = note.Content, FontSize = 12, Foreground = Brush("#D4DCE5"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 6) });
            stack.Children.Add(new TextBlock { Text = note.UpdatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), FontSize = 9, Foreground = Brush("#7F91A3") });
            var del = Button("EXCLUIR", 9);
            del.HorizontalAlignment = HorizontalAlignment.Left;
            del.Margin = new Thickness(0, 7, 0, 0);
            del.Click += async (_, _) =>
            {
                del.IsEnabled = false;
                if (await DeleteNoteAsync(note.Id)) await OpenNotesAsync(); else del.IsEnabled = true;
            };
            stack.Children.Add(del);
            card.Child = stack;
            _body.Children.Add(card);
        }
    }

    private int _historyPage = 1;
    private const int HistoryPageSize = 8;

    private async void OpenHistory()
    {
        Show("HISTÓRICO DE VIAGENS");
        await LoadServerHistoryAsync(1);
    }

    private async Task LoadServerHistoryAsync(int page)
    {
        if (_body is null) return;
        _historyPage = Math.Max(1, page);
        _body.Children.Clear();

        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            _body.Children.Add(Card("Sessão não encontrada. Entre no TransPoli para consultar o histórico sincronizado."));
            return;
        }

        var search = new TextBox
        {
            Text = "",
            FontSize = 11,
            Padding = new Thickness(9),
            Margin = new Thickness(0, 0, 0, 6),
            Background = Brush("#101C29"),
            Foreground = Brushes.White,
            BorderBrush = Brush("#294761"),
            ToolTip = "Filtrar por carga, origem, destino ou caminhão"
        };
        var statusRow = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
        var finished = Button("FINALIZADAS", 9);
        var active = Button("ATIVAS", 9);
        var all = Button("TODAS", 9);
        statusRow.Children.Add(finished); statusRow.Children.Add(active); statusRow.Children.Add(all);
        _body.Children.Add(new TextBlock { Text = "BUSCAR VIAGEM", FontSize = 9, Foreground = Brush("#AAB6C3") });
        _body.Children.Add(search);
        _body.Children.Add(statusRow);

        var status = "finished";
        var result = await FetchHistoryAsync(token, status, search.Text, _historyPage);
        if (result is null)
        {
            _body.Children.Add(Card("Não foi possível carregar o histórico do servidor."));
            return;
        }

        _body.Children.Add(new TextBlock
        {
            Text = $"{result.Total} viagem(ns) encontrada(s) • página {result.Page}/{Math.Max(1, result.Pages)}",
            FontSize = 10,
            Foreground = Brush("#AAB6C3"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (result.Trips.Count == 0)
        {
            _body.Children.Add(Card("Nenhuma viagem encontrada com os filtros atuais."));
        }
        else
        {
            foreach (var item in result.Trips)
            {
                var distance = item.DistanceKm;
                var consumption = distance > 0.5 && item.FuelUsedL > 0
                    ? $" • {item.FuelUsedL / distance * 100:0.0} L/100 km"
                    : "";
                var revenue = item.CargoValueBrl.HasValue ? $" • receita R$ {item.CargoValueBrl.Value:N2}" : "";
                var resultText = item.ResultBrl.HasValue ? $" • resultado R$ {item.ResultBrl.Value:N2}" : "";
                var card = Card(
                    $"{item.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} • {item.Status.ToUpperInvariant()}\n" +
                    $"{item.Origin ?? "Origem"} → {item.Destination ?? "Destino"}\n" +
                    $"{item.Cargo ?? "Carga não informada"}\n" +
                    $"{distance:0.0} km • {item.FuelUsedL:0.0} L{consumption}{revenue}{resultText}");
                _body.Children.Add(card);
            }
        }

        var pager = new UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 0) };
        var previous = Button("← ANTERIOR", 9);
        var next = Button("PRÓXIMA →", 9);
        previous.IsEnabled = result.Page > 1;
        next.IsEnabled = result.Page < Math.Max(1, result.Pages);
        previous.Click += async (_, _) => await LoadServerHistoryAsync(result.Page - 1);
        next.Click += async (_, _) => await LoadServerHistoryAsync(result.Page + 1);
        pager.Children.Add(previous); pager.Children.Add(next);
        _body.Children.Add(pager);

        finished.Click += async (_, _) => await LoadHistoryStatusAsync("finished", search.Text);
        active.Click += async (_, _) => await LoadHistoryStatusAsync("active", search.Text);
        all.Click += async (_, _) => await LoadHistoryStatusAsync("", search.Text);
        search.KeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                await LoadHistoryStatusAsync("finished", search.Text);
            }
        };
    }

    private async Task LoadHistoryStatusAsync(string status, string search)
    {
        if (_body is null) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        _body.Children.Clear();
        var result = await FetchHistoryAsync(token, status, search, 1);
        if (result is null) { _body.Children.Add(Card("Não foi possível carregar o histórico.")); return; }
        _body.Children.Add(new TextBlock { Text = $"{result.Total} viagem(ns) • filtro {(string.IsNullOrWhiteSpace(status) ? "todas" : status)}", FontSize = 10, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var item in result.Trips)
        {
            var consumption = item.DistanceKm > 0.5 && item.FuelUsedL > 0 ? $" • {item.FuelUsedL / item.DistanceKm * 100:0.0} L/100 km" : "";
            var revenue = item.CargoValueBrl.HasValue ? $" • R$ {item.CargoValueBrl.Value:N2}" : "";
            var resultText = item.ResultBrl.HasValue ? $" • resultado R$ {item.ResultBrl.Value:N2}" : "";
            _body.Children.Add(Card($"{item.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} • {item.Status.ToUpperInvariant()}\n{item.Origin ?? "Origem"} → {item.Destination ?? "Destino"}\n{item.Cargo ?? "Carga não informada"}\n{item.DistanceKm:0.0} km • {item.FuelUsedL:0.0} L{consumption}{revenue}{resultText}"));
        }
        var pager = new UniformGrid { Columns = 2 };
        var previous = Button("← ANTERIOR", 9); var next = Button("PRÓXIMA →", 9);
        previous.IsEnabled = result.Page > 1; next.IsEnabled = result.Page < Math.Max(1, result.Pages);
        previous.Click += async (_, _) => { var x = await FetchHistoryAsync(token, status, search, result.Page - 1); await RenderHistoryPageAsync(x, status, search, token); };
        next.Click += async (_, _) => { var x = await FetchHistoryAsync(token, status, search, result.Page + 1); await RenderHistoryPageAsync(x, status, search, token); };
        pager.Children.Add(previous); pager.Children.Add(next); _body.Children.Add(pager);
    }

    private async Task RenderHistoryPageAsync(HistoryResponse? result, string status, string search, string token)
    {
        if (_body is null) return;
        _body.Children.Clear();
        if (result is null) { _body.Children.Add(Card("Não foi possível carregar o histórico.")); return; }
        _body.Children.Add(new TextBlock { Text = $"{result.Total} viagem(ns) • página {result.Page}/{Math.Max(1, result.Pages)}", FontSize = 10, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var item in result.Trips)
        {
            var consumption = item.DistanceKm > 0.5 && item.FuelUsedL > 0 ? $" • {item.FuelUsedL / item.DistanceKm * 100:0.0} L/100 km" : "";
            _body.Children.Add(Card($"{item.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} • {item.Status.ToUpperInvariant()}\n{item.Origin ?? "Origem"} → {item.Destination ?? "Destino"}\n{item.Cargo ?? "Carga não informada"}\n{item.DistanceKm:0.0} km • {item.FuelUsedL:0.0} L{consumption} • despesas R$ {item.ExpensesBrl:N2}" +
                (item.ResultBrl.HasValue ? $" • resultado R$ {item.ResultBrl.Value:N2}" : "")));
        }
        var pager = new UniformGrid { Columns = 2 };
        var previous = Button("← ANTERIOR", 9); var next = Button("PRÓXIMA →", 9);
        previous.IsEnabled = result.Page > 1; next.IsEnabled = result.Page < Math.Max(1, result.Pages);
        previous.Click += async (_, _) => await RenderHistoryPageAsync(await FetchHistoryAsync(token, status, search, result.Page - 1), status, search, token);
        next.Click += async (_, _) => await RenderHistoryPageAsync(await FetchHistoryAsync(token, status, search, result.Page + 1), status, search, token);
        pager.Children.Add(previous); pager.Children.Add(next); _body.Children.Add(pager);
    }

    private async Task<HistoryResponse?> FetchHistoryAsync(string token, string status, string search, int page)
    {
        try
        {
            if (LocalData.Current is not { } store) return null;
            var pageSize = HistoryPageSize;
            var normalizedStatus = (status ?? string.Empty).Trim().ToLowerInvariant();
            var term = (search ?? string.Empty).Trim();
            using var count = store.Db.Connection.CreateCommand();
            count.CommandText = @"SELECT COUNT(*) FROM trip WHERE (@status='' OR LOWER(status)=@status) AND (@search='' OR cargo_name LIKE @like OR source_city LIKE @like OR destination_city LIKE @like OR truck_id LIKE @like);";
            count.Parameters.AddWithValue("@status", normalizedStatus);
            count.Parameters.AddWithValue("@search", term);
            count.Parameters.AddWithValue("@like", $"%{term}%");
            var total = Convert.ToInt32(count.ExecuteScalar() ?? 0);
            var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
            var safePage = Math.Min(Math.Max(1, page), pages);
            using var cmd = store.Db.Connection.CreateCommand();
            cmd.CommandText = @"SELECT id,cargo_name,source_city,destination_city,started_at_utc,finished_at_utc,distance_km,fuel_consumed_l,status,income_gross,expense_total,net_value,truck_id FROM trip WHERE (@status='' OR LOWER(status)=@status) AND (@search='' OR cargo_name LIKE @like OR source_city LIKE @like OR destination_city LIKE @like OR truck_id LIKE @like) ORDER BY COALESCE(finished_at_utc,started_at_utc) DESC LIMIT @limit OFFSET @offset;";
            cmd.Parameters.AddWithValue("@status", normalizedStatus);
            cmd.Parameters.AddWithValue("@search", term);
            cmd.Parameters.AddWithValue("@like", $"%{term}%");
            cmd.Parameters.AddWithValue("@limit", pageSize);
            cmd.Parameters.AddWithValue("@offset", (safePage - 1) * pageSize);
            var response = new HistoryResponse { Ok = true, Pagination = new HistoryPagination { Page = safePage, Limit = pageSize, Total = total, Pages = pages } };
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime.TryParse(reader.IsDBNull(4) ? null : reader.GetString(4), out var started);
                DateTime? finished = null;
                if (!reader.IsDBNull(5) && DateTime.TryParse(reader.GetString(5), out var finishedValue)) finished = finishedValue;
                response.Trips.Add(new HistoryTripDto
                {
                    Id = reader.GetString(0), Cargo = reader.GetString(1), Origin = reader.GetString(2), Destination = reader.GetString(3),
                    StartedAt = started, FinishedAt = finished, DistanceKm = reader.GetDouble(6), FuelUsedL = reader.GetDouble(7), Status = reader.GetString(8),
                    CargoValueBrl = reader.IsDBNull(9) ? null : reader.GetDouble(9), ExpensesBrl = reader.IsDBNull(10) ? 0 : reader.GetDouble(10),
                    ResultBrl = reader.IsDBNull(11) ? null : reader.GetDouble(11), TruckName = reader.IsDBNull(12) ? null : reader.GetString(12)
                });
            }
            return response;
        }
        catch { return null; }
    }

    private void Show(string title)
    {
        if (_panel is null || _title is null) return;
        _title.Text = title;
        _panel.Visibility = Visibility.Visible;
    }

    private void Close()
    {
        if (_panel is not null) _panel.Visibility = Visibility.Collapsed;
    }

    private Task<bool> SaveNoteAsync(string title, string content)
    {
        try
        {
            if (LocalData.Current is not { } store || string.IsNullOrWhiteSpace(content)) return Task.FromResult(false);
            var now = DateTime.UtcNow.ToString("O");
            using var cmd = store.Db.Connection.CreateCommand();
            cmd.CommandText = "INSERT INTO driver_note(id,title,content,created_at_utc,updated_at_utc) VALUES(@id,@title,@content,@now,@now);";
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@title", string.IsNullOrWhiteSpace(title) ? "Nota" : title.Trim());
            cmd.Parameters.AddWithValue("@content", content.Trim());
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    private Task<List<NoteDto>> LoadNotesAsync()
    {
        var list = new List<NoteDto>();
        try
        {
            if (LocalData.Current is not { } store) return Task.FromResult(list);
            using var cmd = store.Db.Connection.CreateCommand();
            cmd.CommandText = "SELECT id,title,content,created_at_utc,updated_at_utc FROM driver_note ORDER BY updated_at_utc DESC LIMIT 50;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                DateTime.TryParse(reader.GetString(3), out var created); DateTime.TryParse(reader.GetString(4), out var updated);
                list.Add(new NoteDto { Id=reader.GetString(0), Title=reader.GetString(1), Content=reader.GetString(2), CreatedAt=created, UpdatedAt=updated });
            }
        }
        catch { }
        return Task.FromResult(list);
    }

    private Task<bool> DeleteNoteAsync(string id)
    {
        try
        {
            if (LocalData.Current is not { } store || string.IsNullOrWhiteSpace(id)) return Task.FromResult(false);
            using var cmd = store.Db.Connection.CreateCommand(); cmd.CommandText="DELETE FROM driver_note WHERE id=@id;"; cmd.Parameters.AddWithValue("@id",id); cmd.ExecuteNonQuery(); return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    private static List<TripHistoryRecord> ReadLocalHistory(MainWindow main)
    {
        try
        {
            var field = main.GetType().GetField("_operationsCenter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var center = field?.GetValue(main);
            var historyField = center?.GetType().GetField("_history", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (historyField?.GetValue(center) is IEnumerable source) return source.Cast<object>().OfType<TripHistoryRecord>().OrderByDescending(x => x.FinishedAtUtc).Take(100).ToList();
        }
        catch { }
        return new List<TripHistoryRecord>();
    }

    private sealed class HistoryResponse
    {
        public bool Ok { get; set; }
        public List<HistoryTripDto> Trips { get; set; } = new();
        public HistoryPagination Pagination { get; set; } = new();
        public int Page => Pagination.Page;
        public int Total => Pagination.Total;
        public int Pages => Pagination.Pages;
    }

    private sealed class HistoryPagination
    {
        public int Page { get; set; } = 1;
        public int Limit { get; set; } = HistoryPageSize;
        public int Total { get; set; }
        public int Pages { get; set; }
    }

    private sealed class HistoryTripDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("cargo")] public string? Cargo { get; set; }
        [JsonPropertyName("origin")] public string? Origin { get; set; }
        [JsonPropertyName("destination")] public string? Destination { get; set; }
        [JsonPropertyName("started_at")] public DateTime StartedAt { get; set; }
        [JsonPropertyName("finished_at")] public DateTime? FinishedAt { get; set; }
        [JsonPropertyName("distance_km")] public double DistanceKm { get; set; }
        [JsonPropertyName("fuel_used_l")] public double FuelUsedL { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("cargo_value_brl")] public double? CargoValueBrl { get; set; }
        [JsonPropertyName("expenses_brl")] public double ExpensesBrl { get; set; }
        [JsonPropertyName("result_brl")] public double? ResultBrl { get; set; }
        [JsonPropertyName("truck_name")] public string? TruckName { get; set; }
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("license_plate")] public string? LicensePlate { get; set; }
    }

    private Border Card(string text) => new() { Background = Brush("#101C29"), BorderBrush = Brush("#1D3B57"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap } };
    private Button Button(string text, double fontSize) => new() { Content = text, FontSize = fontSize, Padding = new Thickness(8, 8, 8, 8), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush("#15263A"), Foreground = Brushes.White, BorderBrush = Brush("#294761") };
    private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private static Grid? FindTabletScreenGrid(DependencyObject root){foreach(var border in FindVisualChildren<Border>(root)){if(Math.Abs(border.CornerRadius.TopLeft-8)<0.1&&border.Child is Grid grid&&border.ActualWidth>200)return grid;}return null;}
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{if(root is null)yield break;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var nested in FindVisualChildren<T>(child))yield return nested;}}

    private sealed class NotesResponse { public bool Ok { get; set; } public List<NoteDto>? Notes { get; set; } }
    private sealed class NoteDto { public string Id { get; set; } = ""; public string Title { get; set; } = "Nota"; public string Content { get; set; } = ""; public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; } }
}
