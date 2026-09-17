using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

    private void OpenHistory()
    {
        Show("HISTÓRICO DE VIAGENS");
        if (_body is null || _main is null) return;
        _body.Children.Clear();
        var history = ReadLocalHistory(_main);
        if (history.Count == 0)
        {
            _body.Children.Add(Card("Nenhuma viagem finalizada foi registrada neste computador ainda."));
            return;
        }
        _body.Children.Add(new TextBlock { Text = $"{history.Count} viagem(ns) registrada(s)", FontSize = 10, Foreground = Brush("#AAB6C3"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var item in history)
        {
            var distance = item.DistanceKm;
            var consumption = distance > 0.5f ? $" • {item.FuelUsedL / distance * 100:0.0} L/100 km" : "";
            _body.Children.Add(Card($"{item.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} → {item.FinishedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}\n{item.Route}\n{item.Cargo}\n{distance:0.0} km • {item.FuelUsedL:0.0} L{consumption}"));
        }
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

    private async Task<bool> SaveNoteAsync(string title, string content)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(content)) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/notes");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new { title = string.IsNullOrWhiteSpace(title) ? "Nota" : title.Trim(), content = content.Trim() }), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task<List<NoteDto>> LoadNotesAsync()
    {
        var list = new List<NoteDto>();
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return list;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/notes?limit=50");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return list;
            var root = JsonSerializer.Deserialize<NotesResponse>(await res.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return root?.Notes ?? list;
        }
        catch { return list; }
    }

    private async Task<bool> DeleteNoteAsync(string id)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(id)) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBaseUrl}/me/notes/{id}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _http.SendAsync(req);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
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

    private Border Card(string text) => new() { Background = Brush("#101C29"), BorderBrush = Brush("#1D3B57"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8), Child = new TextBlock { Text = text, FontSize = 12, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap } };
    private Button Button(string text, double fontSize) => new() { Content = text, FontSize = fontSize, Padding = new Thickness(8, 8, 8, 8), Margin = new Thickness(0, 3, 0, 3), HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brush("#15263A"), Foreground = Brushes.White, BorderBrush = Brush("#294761") };
    private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private static Grid? FindTabletScreenGrid(DependencyObject root){foreach(var border in FindVisualChildren<Border>(root)){if(Math.Abs(border.CornerRadius.TopLeft-8)<0.1&&border.Child is Grid grid&&border.ActualWidth>200)return grid;}return null;}
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T:DependencyObject{if(root is null)yield break;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;foreach(var nested in FindVisualChildren<T>(child))yield return nested;}}

    private sealed class NotesResponse { public bool Ok { get; set; } public List<NoteDto>? Notes { get; set; } }
    private sealed class NoteDto { public string Id { get; set; } = ""; public string Title { get; set; } = "Nota"; public string Content { get; set; } = ""; public DateTime CreatedAt { get; set; } public DateTime UpdatedAt { get; set; } }
}
