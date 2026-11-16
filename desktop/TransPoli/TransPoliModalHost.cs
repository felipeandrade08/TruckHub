using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace TransPoli;

/// <summary>
/// Antes existiam quatro cópias da mesma lógica de criação do overlay
/// (documentos, economia, garagem e nota). Cada uma reconstruía o host e
/// as telas brigavam entre si. Agora existe um único host.
/// </summary>
public partial class MainWindow
{
    // Tamanho único para TODOS os modais do TransPoli.
    // O host nunca redimensiona depois que o modal fica visível.
    // Acompanha o tamanho da janela (com folga para a moldura) e nunca passa de 1400 x 840.
    private double StandardModalWidth => Math.Clamp((ActualWidth > 0 ? ActualWidth : 1520) - 150, 900, 1300);
    private double StandardModalHeight => Math.Clamp((ActualHeight > 0 ? ActualHeight : 940) - 140, 560, 800);
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
            Background = new SolidColorBrush(Color.FromArgb(232, 6, 10, 15)),
            Margin = new Thickness(20, 18, 20, 18),
            Padding = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
            Visibility = Visibility.Collapsed
        };
        // Toque/clique fora do cartão fecha o painel (menos nas telas que exigem decisão).
        _documentModalLayer.MouseLeftButtonDown += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, _documentModalLayer)) return;
            if (_documentModalKind is "trip-gate" or "polipass-receipt") return;
            CloseOperationalModal();
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
            element.Width = StandardModalWidth;
            element.Height = StandardModalHeight;
            element.MinWidth = StandardModalWidth;
            element.MinHeight = StandardModalHeight;
            element.MaxWidth = StandardModalWidth;
            element.MaxHeight = StandardModalHeight;
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.VerticalAlignment = VerticalAlignment.Center;
        }
        layer.Child = content;
        layer.Visibility = Visibility.Visible;
        try
        {
            // Entrada suave: surge e sobe alguns pixels (sem animar tamanho, para não reabrir o problema de "abre grande e encolhe").
            if (content is FrameworkElement card)
            {
                var slide = new TranslateTransform(0, 16);
                card.RenderTransform = slide;
                card.Opacity = 0;
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                var span = TimeSpan.FromMilliseconds(190);
                card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, span) { EasingFunction = ease });
                slide.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, span) { EasingFunction = ease });
            }
        }
        catch { /* animação é só acabamento */ }
        // Força o WPF a recalcular o layout agora, no mesmo frame, em vez de
        // esperar o próximo ciclo de renderização. Sem isso, janelas sem
        // moldura (WindowStyle="None" + AllowsTransparency="True") podem
        // desenhar um primeiro frame no tamanho "cru" (preenchendo o
        // dashboard inteiro) antes de aplicar o tamanho fixo do card —
        // é esse frame extra que dava a impressão de "abre grande e encolhe".
        layer.UpdateLayout();
    }

    /// <summary>Abre qualquer tela usando exatamente o mesmo cartão visual dos modais nativos.</summary>
    internal void ShowStandardModal(string kind, string title, UIElement body, string? subtitle = null)
    {
        _documentModalKind = kind;
        ShowModalContent(kind, BuildModalCard(title, body, subtitle));
    }

    /// <summary>Cartão padrão do tablet: título, botão de fechar e corpo rolável.</summary>
    private Border BuildModalCard(string title, UIElement body, string? subtitle = null)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Margin = new Thickness(0, 0, 0, 14);

        // Ícone do assunto em um bloco dourado, como o app de tablet da prévia.
        var iconTile = new Border
        {
            Width = 58,
            Height = 58,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xE7, 0xB8, 0x4A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xE7, 0xB8, 0x4A)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = TpIcons.Create(ModalKindIcon(_documentModalKind), FindResource("TpGoldBright") as Brush, 28)
        };
        header.Children.Add(iconTile);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "TRANSPOLI OS  •  " + (_documentModalKind ?? "SISTEMA").Replace("-", " ").ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("TpGoldBright") as Brush,
            Margin = new Thickness(0, 0, 0, 3)
        });
        titles.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 27,
            FontWeight = FontWeights.Bold,
            FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
            Foreground = FindResource("TpText") as Brush,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            titles.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 13,
                Foreground = FindResource("TpMuted") as Brush,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);

        var close = new Button
        {
            Tag = ModalActionTag,
            Style = FindResource("TpSecondaryButton") as Style,
            Width = 48,
            Height = 48,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Fechar painel"
        };
        var closeIcon = TpIcons.Create("close", null, 20);
        closeIcon.SetBinding(TpIcon.FillProperty, new Binding("Foreground") { Source = close });
        close.Content = closeIcon;
        close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        root.Children.Add(header);

        // Divisor fino entre o cabeçalho e o conteúdo.
        var divider = new Border { Height = 1, Background = FindResource("TpStroke") as Brush, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0) };
        root.Children.Add(divider);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(2, 0, 8, 10),
            Content = body
        };
        // Degradê no rodapé: indica que há mais conteúdo abaixo (some quando a rolagem chega ao fim).
        var surfaceColor = (FindResource("TpSurface") as SolidColorBrush)?.Color ?? Color.FromRgb(15, 20, 27);
        var fade = new Border
        {
            Height = 46,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Background = new LinearGradientBrush(
                Color.FromArgb(0, surfaceColor.R, surfaceColor.G, surfaceColor.B), surfaceColor, 90)
        };
        scroll.ScrollChanged += (_, _) =>
            fade.Visibility = scroll.ScrollableHeight - scroll.VerticalOffset > 6 ? Visibility.Visible : Visibility.Collapsed;
        var scrollHost = new Grid();
        scrollHost.Children.Add(scroll);
        scrollHost.Children.Add(fade);
        Grid.SetRow(scrollHost, 1);
        root.Children.Add(scrollHost);

        return new Border
        {
            Width = StandardModalWidth,
            Height = StandardModalHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = FindResource("TpSurface") as Brush,
            BorderBrush = FindResource("TpStrokeStrong") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(30),
            Padding = new Thickness(30, 26, 30, 26),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 0,
                Opacity = 0.6
            },
            Child = root
        };
    }

    /// <summary>Estado de carregamento enquanto a API responde.</summary>
    private UIElement BuildModalLoading(string message) => new Border
    {
        Width = StandardModalWidth,
        Height = StandardModalHeight,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Background = FindResource("TpCanvas") as Brush,
        BorderBrush = FindResource("TpStroke") as Brush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(24),
        Padding = new Thickness(34),
        Child = new TextBlock
        {
            Text = message,
            Foreground = FindResource("TpText") as Brush,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        }
    };

    /// <summary>Estado operacional padronizado para vazio, offline e erro.</summary>
    private Border ModalStatePanel(string eyebrow, string title, string detail, string accentResource = "Muted")
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = eyebrow.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(accentResource) as Brush
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("TpText") as Brush,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 13,
            Foreground = FindResource("TpMuted") as Brush,
            Margin = new Thickness(0, 7, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        return new Border
        {
            Background = accentResource == "Muted" ? ModalSurfaceGradient() : ModalTint(accentResource, 0x16),
            BorderBrush = accentResource == "Muted" ? FindResource("Stroke") as Brush : ModalTint(accentResource, 0x66),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(24, 20, 24, 20),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack
        };
    }

    internal void CloseOperationalModal()
    {
        if (_documentModalLayer == null) return;
        if (_documentModalKind == "tachograph") StopTachClock();
        if (_documentModalKind == "trip-gate")
        {
            _tripGateModalOpen = false;
            _tripGateNextPromptUtc = DateTime.UtcNow.AddSeconds(5);
        }
        _documentModalLayer.Child = null;
        _documentModalLayer.Visibility = Visibility.Collapsed;
        _documentModalKind = null;
        // O conteúdo original continua montado no host — remover daqui
        // causava tela preta ao reabrir qualquer modal.
    }

    /* ---------- blocos visuais reutilizados pelos modais ---------- */

    private static readonly FontFamily ModalNumberFont = new("Bahnschrift, Segoe UI");

    /// <summary>Versão translúcida de um pincel de recurso (fundos tingidos de status e destaque).</summary>
    private Brush ModalTint(string resource, byte alpha)
    {
        if (TryFindResource(resource) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }
        return new SolidColorBrush(Color.FromArgb(alpha, 0x8B, 0x99, 0xA7));
    }

    private Brush ModalSurfaceGradient() => new LinearGradientBrush(
        Color.FromRgb(0x1A, 0x25, 0x30), Color.FromRgb(0x13, 0x1C, 0x25), 90);

    private Button ModalButton(string text) => new()
    {
        Content = text,
        Tag = ModalActionTag,
        Style = FindResource("TpPrimaryButton") as Style,
        MinHeight = 50,
        Margin = new Thickness(0, 14, 0, 0),
        Padding = new Thickness(22, 13, 22, 13),
        FontSize = 14,
        FontWeight = FontWeights.Bold,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private TextBlock ModalLabel(string text) => new()
    {
        Text = text,
        Style = FindResource("TpSectionLabel") as Style,
        Margin = new Thickness(0, 18, 0, 8)
    };

    private static TextBlock ModalLine(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = (Brush)Application.Current.FindResource("TpText"),
        Margin = new Thickness(0, 0, 0, 10),
        TextWrapping = TextWrapping.Wrap
    };

    /// <summary>Linha de valor: faixa arredondada com rótulo à esquerda e número em destaque à direita.</summary>
    private Border ModalValueRow(string label, string value, string? accentResource = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 14,
            Foreground = FindResource("TpMuted") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

        var amount = new TextBlock
        {
            Text = value,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            FontFamily = ModalNumberFont,
            Foreground = FindResource(accentResource ?? "TpText") as Brush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0)
        };
        Grid.SetColumn(amount, 1);
        grid.Children.Add(amount);

        return new Border
        {
            Padding = new Thickness(16, 11, 16, 11),
            Margin = new Thickness(0, 0, 0, 6),
            Background = FindResource("TpSurfaceRaised") as Brush,
            BorderBrush = FindResource("TpStroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = grid
        };
    }

    private Border ModalPanel(UIElement child) => new()
    {
        Background = ModalSurfaceGradient(),
        BorderBrush = FindResource("TpStroke") as Brush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(22),
        Padding = new Thickness(22),
        Margin = new Thickness(0, 0, 0, 14),
        Child = child
    };

    /// <summary>Indicador compacto: rótulo pequeno em caixa-alta e valor grande em fonte numérica.</summary>
    private Border MiniCard(string label, string value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindResource("Muted") as Brush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            FontFamily = ModalNumberFont,
            Foreground = FindResource("TpText") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        return new Border
        {
            Background = ModalSurfaceGradient(),
            BorderBrush = FindResource("TpStroke") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14, 18, 14),
            Margin = new Thickness(4),
            Child = panel
        };
    }

    // Blocos premium compartilhados: dão aos módulos a mesma linguagem visual
    // sem duplicar cores, espaçamentos e tipografia em cada tela.
    private Border ModalHero(string eyebrow, string title, string detail, string? value = null, string accentResource = "GoldBright")
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = eyebrow.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(accentResource) as Brush
        });
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            FontFamily = ModalNumberFont,
            Foreground = FindResource("TpText") as Brush,
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(value))
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 40,
                FontWeight = FontWeights.Bold,
                FontFamily = ModalNumberFont,
                Foreground = FindResource(accentResource) as Brush,
                Margin = new Thickness(0, 8, 0, 0)
            });
        stack.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 14,
            Foreground = FindResource("TpMuted") as Brush,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        // Faixa de destaque lateral na cor do assunto + fundo levemente tingido.
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(new Border
        {
            Width = 5,
            CornerRadius = new CornerRadius(3),
            Background = FindResource(accentResource) as Brush,
            Margin = new Thickness(0, 2, 20, 2)
        });
        Grid.SetColumn(stack, 1);
        layout.Children.Add(stack);

        return new Border
        {
            Background = new LinearGradientBrush(
                ((ModalTint(accentResource, 0x24) as SolidColorBrush)?.Color) ?? Color.FromRgb(0x20, 0x2A, 0x35),
                Color.FromRgb(0x13, 0x1C, 0x25), 0),
            BorderBrush = ModalTint(accentResource, 0x55),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(26, 22, 26, 22),
            Margin = new Thickness(0, 0, 0, 16),
            Child = layout
        };
    }

    private Border ModalStatusStrip(string text, string accentResource = "Green")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = FindResource(accentResource) as Brush,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(accentResource) as Brush,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        });
        return new Border
        {
            Background = ModalTint(accentResource, 0x1A),
            BorderBrush = ModalTint(accentResource, 0x66),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 11, 16, 11),
            Margin = new Thickness(0, 0, 0, 12),
            Child = row
        };
    }

    private TextBlock ModalSectionTitle(string title, string? subtitle = null)
    {
        return new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(subtitle) ? title.ToUpperInvariant() : $"{title.ToUpperInvariant()}  •  {subtitle}",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("TpGoldBright") as Brush,
            Margin = new Thickness(0, 24, 0, 10),
            TextWrapping = TextWrapping.Wrap
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

    private static string ModalKindIcon(string? kind)
    {
        var k = (kind ?? "").ToLowerInvariant();
        if (k.Contains("bank") || k.Contains("economy")) return "bank";
        if (k.Contains("garage") || k.Contains("fleet")) return "warehouse";
        if (k.Contains("tach")) return "clock";
        if (k.Contains("fuel")) return "fuel";
        if (k.Contains("truck")) return "truck";
        if (k.Contains("cargo") || k.Contains("market")) return "cargo";
        if (k.Contains("invoice") || k.Contains("document") || k.Contains("receipt")) return "doc";
        if (k.Contains("rank")) return "trophy";
        if (k.Contains("profile")) return "account";
        if (k.Contains("trip") || k.Contains("route")) return "route";
        if (k.Contains("maint")) return "cog";
        return TpIcons.Has("cog") ? "cog" : "truck";
    }
}
