using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private Grid? _documentModalHost;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(InterceptDocumentButton), true);
    }

    private static void InterceptDocumentButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button || button.Content is not string text) return;
        var normalized = text.Replace("\n", " ").Trim();
        if (!normalized.Contains("DOC", StringComparison.OrdinalIgnoreCase)) return;
        var window = Window.GetWindow(button) as MainWindow;
        if (window is null) return;
        e.Handled = true;
        window.ShowDocumentsInsideTablet();
    }

    private void ShowDocumentsInsideTablet()
    {
        if (_documentModalHost != null) return;
        if (Content is not UIElement original) return;
        var host = new Grid();
        Content = host;
        host.Children.Add(original);
        _documentModalHost = host;

        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)), Padding = new Thickness(70, 100) };
        var card = new Border { Background = FindResource("Bg") as Brush, BorderBrush = FindResource("Panel2") as Brush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(24), Padding = new Thickness(24) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = "📄 DOCUMENTOS", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
        var close = new Button { Content = "✕", Style = FindResource("TabletButton") as Style, Width = 48, Height = 44 };
        close.Click += (_, _) => CloseDocumentsInsideTablet();
        Grid.SetColumn(close, 1); header.Children.Add(close); root.Children.Add(header);

        var subtitle = new TextBlock { Text = "Gerencie documentos sem abrir outra janela ou aba.", FontSize = 11, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 6, 0, 14) };
        Grid.SetRow(subtitle, 1); root.Children.Add(subtitle);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "STATUS", Style = FindResource("Label") as Style, Margin = new Thickness(0, 0, 0, 7) });
        var status = new ComboBox { FontSize = 14, Padding = new Thickness(8), ItemsSource = new[] { "Aguardando nota", "Recebido", "Conferido", "Carimbado", "Despachado" }, SelectedIndex = 0, Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        content.Children.Add(status);
        content.Children.Add(new TextBlock { Text = "NÚMERO / REFERÊNCIA", Style = FindResource("Label") as Style, Margin = new Thickness(0, 14, 0, 7) });
        var reference = new TextBox { FontSize = 14, Padding = new Thickness(10), Background = FindResource("Panel2") as Brush, Foreground = FindResource("Text") as Brush };
        content.Children.Add(reference);
        var save = new Button { Content = "✓ REGISTRAR DOCUMENTO", Style = FindResource("TabletButton") as Style, Margin = new Thickness(0, 14, 0, 14), Padding = new Thickness(12) };
        content.Children.Add(save);
        content.Children.Add(new TextBlock { Text = "HISTÓRICO", Style = FindResource("Label") as Style, Margin = new Thickness(0, 4, 0, 8) });
        var history = new StackPanel();
        foreach (var item in _documents.OrderByDescending(x => x.RecordedAtUtc).Take(20)) history.Children.Add(new TextBlock { Text = $"{item.RecordedAtUtc.ToLocalTime():dd/MM HH:mm} • {item.Status}\n{item.Reference}", FontSize = 11, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap });
        if (!_documents.Any()) history.Children.Add(new TextBlock { Text = "Nenhum documento registrado ainda.", FontSize = 12, Foreground = FindResource("Muted") as Brush });
        content.Children.Add(new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 400 });
        Grid.SetRow(content, 2); root.Children.Add(content);

        save.Click += (_, _) =>
        {
            var value = reference.Text.Trim();
            if (string.IsNullOrWhiteSpace(value)) { reference.Focus(); return; }
            var selected = status.SelectedItem?.ToString() ?? "Aguardando nota";
            _documents.Add(new DocumentRecord { Id = Guid.NewGuid().ToString("N"), Status = selected, Reference = value, RecordedAtUtc = DateTime.UtcNow });
            SaveOperations(); UpdateOpsCounters(); StatusText.Text = $"TransPoli • documento atualizado • {selected}";
            CloseDocumentsInsideTablet();
        };

        card.Child = root; dim.Child = card; host.Children.Add(dim);
    }

    private void CloseDocumentsInsideTablet()
    {
        if (_documentModalHost == null) return;
        var host = _documentModalHost;
        if (host.Children.Count > 1) host.Children.RemoveAt(1);
        _documentModalHost = null;
    }
}
