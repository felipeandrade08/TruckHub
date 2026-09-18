using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

/// <summary>
/// Antes existiam quatro cópias da mesma lógica de criação do overlay
/// (documentos, economia, garagem e nota). Cada uma reconstruía o host e
/// as telas brigavam entre si. Agora existe um único host.
/// </summary>
public partial class MainWindow
{
    private Grid? _documentModalHost;
    private UIElement? _documentModalOriginalContent;
    private Border? _documentModalLayer;
    private string? _documentModalKind;

    /// <summary>Marcador usado nos botões criados dentro de um modal.</summary>
    internal const string ModalActionTag = "modal-action";

    /// <summary>Cria o overlay uma única vez e devolve a camada pronta.</summary>
    private Border? EnsureModalHost()
    {
        if (_documentModalLayer != null) return _documentModalLayer;
        if (Content is not UIElement original) return null;

        _documentModalOriginalContent = original;
        var host = new Grid();
        Content = host;
        host.Children.Add(original);
        _documentModalHost = host;

        _documentModalLayer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 0, 0, 0)),
            Margin = new Thickness(105, 100, 110, 120),
            Padding = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
            Visibility = Visibility.Collapsed
        };
        host.Children.Add(_documentModalLayer);
        return _documentModalLayer;
    }

    // Mantido por compatibilidade com o código que chamava este nome.
    private void EnsureEconomyModalHost() => EnsureModalHost();

    /// <summary>Exibe um conteúdo qualquer dentro do overlay.</summary>
    private void ShowModalContent(string kind, UIElement content)
    {
        var layer = EnsureModalHost();
        if (layer == null) return;
        _documentModalKind = kind;
        // Define o tamanho antes de tornar a camada visível para eliminar o efeito
        // de abrir grande e encolher depois.
        if (content is FrameworkElement element)
        {
            element.Width = 900;
            element.Height = 600;
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.VerticalAlignment = VerticalAlignment.Center;
        }
        layer.Child = content;
        layer.Visibility = Visibility.Visible;
        // Força o WPF a recalcular o layout agora, no mesmo frame, em vez de
        // esperar o próximo ciclo de renderização. Sem isso, janelas sem
        // moldura (WindowStyle="None" + AllowsTransparency="True") podem
        // desenhar um primeiro frame no tamanho "cru" (preenchendo o
        // dashboard inteiro) antes de aplicar o tamanho fixo do card —
        // é esse frame extra que dava a impressão de "abre grande e encolhe".
        layer.UpdateLayout();
    }

    /// <summary>Cartão padrão do tablet: título, botão de fechar e corpo rolável.</summary>
    private Border BuildModalCard(string title, UIElement body, string? subtitle = null)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = new StackPanel();
        titles.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            titles.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = FindResource("Muted") as Brush,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
        header.Children.Add(titles);

        var close = new Button
        {
            Content = "✕",
            Tag = ModalActionTag,
            Style = FindResource("TabletButton") as Style,
            Width = 48,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Top
        };
        close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 16, 0, 0),
            Content = body
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        return new Border
        {
            Width = 900,
            Height = 600,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = FindResource("Bg") as Brush,
            BorderBrush = FindResource("StrokeStrong") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(24),
            Child = root
        };
    }

    /// <summary>Estado de carregamento enquanto a API responde.</summary>
    private UIElement BuildModalLoading(string message) => new Border
    {
        Width = 900,
        Height = 600,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Background = FindResource("Bg") as Brush,
        BorderBrush = FindResource("Panel2") as Brush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(24),
        Padding = new Thickness(34),
        Child = new TextBlock
        {
            Text = message,
            Foreground = FindResource("Text") as Brush,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        }
    };

    internal void CloseOperationalModal()
    {
        if (_documentModalLayer == null) return;
        _documentModalLayer.Child = null;
        _documentModalLayer.Visibility = Visibility.Collapsed;
        _documentModalKind = null;
        // O conteúdo original continua montado no host — remover daqui
        // causava tela preta ao reabrir qualquer modal.
    }

    /* ---------- blocos visuais reutilizados pelos modais ---------- */

    private Button ModalButton(string text) => new()
    {
        Content = text,
        Tag = ModalActionTag,
        Style = FindResource("TabletButton") as Style,
        Margin = new Thickness(0, 12, 0, 0),
        Padding = new Thickness(12, 11, 12, 11),
        HorizontalContentAlignment = HorizontalAlignment.Center
    };

    private TextBlock ModalLabel(string text) => new()
    {
        Text = text,
        Style = FindResource("Label") as Style,
        Margin = new Thickness(0, 16, 0, 8)
    };

    private static TextBlock ModalLine(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = (Brush)Application.Current.FindResource("Text"),
        Margin = new Thickness(0, 0, 0, 10),
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>Linha de valor com rótulo à esquerda e número à direita.</summary>
    private Border ModalValueRow(string label, string value, string? accentResource = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var amount = new TextBlock
        {
            Text = value,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(accentResource ?? "Text") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);

        return new Border
        {
            Padding = new Thickness(0, 7, 0, 7),
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private Border ModalPanel(UIElement child) => new()
    {
        Background = FindResource("Panel2") as Brush,
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 10),
        Child = child
    };

    private Border MiniCard(string label, string value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontSize = 9, Foreground = FindResource("Muted") as Brush });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        return new Border
        {
            Background = FindResource("Panel2") as Brush,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Margin = new Thickness(4),
            Child = panel
        };
    }

    private Border ModalCard(string label1, string value1, string label2, string value2)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var first = MiniCard(label1, value1);
        var second = MiniCard(label2, value2);
        Grid.SetColumn(second, 1);
        grid.Children.Add(first);
        grid.Children.Add(second);
        return new Border { Child = grid, Margin = new Thickness(0, 0, 0, 6) };
    }
}
