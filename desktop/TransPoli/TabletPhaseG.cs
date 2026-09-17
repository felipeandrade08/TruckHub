using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TransPoli;

/// <summary>
/// Fase G: camada de navegação do tablet físico.
/// Mantém o dashboard/telemetria existente e coloca os módulos dentro da própria tela,
/// sem criar janelas gigantes e sem registrar outro atalho de teclado.
/// </summary>
public partial class MainWindow
{
    private static bool _phaseGRegistered;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler(InitializePhaseGTablet));
    }

    private static void InitializePhaseGTablet(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._phaseGReady) return;
        window._phaseGReady = true;
        window.BuildPhaseGTabletNavigation();
    }

    private bool _phaseGReady;
    private Grid? _phaseGOverlay;
    private Border? _phaseGContent;
    private TextBlock? _phaseGTitle;
    private TextBlock? _phaseGDescription;

    private void BuildPhaseGTabletNavigation()
    {
        var screen = FindTabletScreenGrid(this);
        if (screen is null) return;

        HideLegacyNavigation(screen);

        _phaseGOverlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 4, 10, 17)),
            Visibility = Visibility.Visible
        };
        Panel.SetZIndex(_phaseGOverlay, 1000);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        _phaseGOverlay.Children.Add(root);

        _phaseGContent = new Border
        {
            Margin = new Thickness(18, 18, 18, 10),
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("#1D3B57"),
            Background = Brush("#0A1828"),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_phaseGContent, 0);

        var contentStack = new StackPanel();
        var back = CreateButton("← VOLTAR AO PAINEL", 11);
        back.Click += (_, _) => ClosePhaseGModule();
        contentStack.Children.Add(back);

        _phaseGTitle = new TextBlock
        {
            Foreground = Brush("#FFFFFF"),
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 18, 0, 6)
        };
        contentStack.Children.Add(_phaseGTitle);

        _phaseGDescription = new TextBlock
        {
            Foreground = Brush("#AAB6C3"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };
        contentStack.Children.Add(_phaseGDescription);
        _phaseGContent.Child = contentStack;
        root.Children.Add(_phaseGContent);

        var navBorder = new Border
        {
            Margin = new Thickness(10, 4, 10, 10),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(18),
            Background = Brush("#101C29"),
            BorderBrush = Brush("#1D3B57"),
            BorderThickness = new Thickness(1)
        };
        Grid.SetRow(navBorder, 1);

        var nav = new UniformGrid { Rows = 2, Columns = 5 };
        foreach (var item in PhaseGModules)
        {
            var button = CreateButton(item.Label, 9);
            button.ToolTip = item.Title;
            button.Click += (_, _) => OpenPhaseGModule(item);
            nav.Children.Add(button);
        }
        navBorder.Child = nav;
        root.Children.Add(navBorder);
        _phaseGOverlay.Children.Add(new Rectangle { Fill = Brushes.Transparent, IsHitTestVisible = false });

        screen.Children.Add(_phaseGOverlay);
    }

    private void OpenPhaseGModule(PhaseGModule module)
    {
        if (_phaseGContent is null || _phaseGTitle is null || _phaseGDescription is null) return;
        _phaseGTitle.Text = module.Title;
        _phaseGDescription.Text = module.Description;
        _phaseGContent.Visibility = Visibility.Visible;
    }

    private void ClosePhaseGModule()
    {
        if (_phaseGContent is not null) _phaseGContent.Visibility = Visibility.Collapsed;
    }

    private Button CreateButton(string text, double fontSize)
    {
        return new Button
        {
            Content = text,
            Style = TryFindResource("TabletButton") as Style,
            FontSize = fontSize,
            Padding = new Thickness(5, 8, 5, 8),
            Margin = new Thickness(2)
        };
    }

    private static Brush Brush(string resource)
    {
        if (Application.Current?.TryFindResource(resource) is Brush brush) return brush;
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(resource));
    }

    private static void HideLegacyNavigation(Panel screen)
    {
        foreach (var button in FindVisualChildren<Button>(screen))
        {
            var text = button.Content?.ToString() ?? string.Empty;
            if (text.Contains("INÍCIO", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("VIAGEM", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ABAST.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("PARADAS", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("OCORR.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("DOCS", StringComparison.OrdinalIgnoreCase))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static Grid? FindTabletScreenGrid(DependencyObject root)
    {
        foreach (var border in FindVisualChildren<Border>(root))
        {
            if (Math.Abs(border.CornerRadius.TopLeft - 8) < 0.1 && border.Child is Grid grid && border.ActualWidth > 200)
                return grid;
        }
        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dependencyObject) where T : DependencyObject
    {
        if (dependencyObject is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(dependencyObject); i++)
        {
            var child = VisualTreeHelper.GetChild(dependencyObject, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private sealed record PhaseGModule(string Label, string Title, string Description);

    private static readonly IReadOnlyList<PhaseGModule> PhaseGModules = new[]
    {
        new PhaseGModule("⌂\nCENTRAL", "Central de Operações", "Visão operacional do veículo e da viagem: conexão ETS2/ATS, velocidade, combustível, odômetro, carga, rota e progresso. O painel principal continua sendo atualizado pela telemetria em tempo real."),
        new PhaseGModule("🚛\nGARAGEM", "Garagem", "Área dedicada ao caminhão atual: marca, modelo, placa, odômetro, combustível, estado e vínculo do veículo ao motorista. A autorização continua sendo validada pelo servidor."),
        new PhaseGModule("💰\nBANCO", "Banco", "Resumo financeiro do motorista: saldo, entradas, despesas e resultado das operações. Os lançamentos existentes permanecem como fonte dos dados econômicos."),
        new PhaseGModule("▣\nVIAGEM", "Viagem", "Acompanhamento da viagem atual com origem, destino, carga, peso, distância planejada, distância percorrida, tempo e status operacional."),
        new PhaseGModule("⛽\nABAST.", "Abastecimento", "Abastecimento detectado pela telemetria. Os litros vêm do jogo; o motorista informa apenas preço por litro e posto para concluir o lançamento."),
        new PhaseGModule("📝\nNOTAS", "Notas", "Espaço reservado no tablet para anotações operacionais do motorista. A navegação está pronta sem criar uma janela externa; o armazenamento específico será integrado na fase correspondente."),
        new PhaseGModule("📊\nESTAT.", "Estatísticas", "Visão preparada para indicadores como quilômetros, viagens, carga transportada, combustível, consumo médio, gastos e resultado."),
        new PhaseGModule("🔧\nMANUT.", "Manutenção", "Acesso preparado para acompanhar desgaste e serviços do caminhão. Os dados de desgaste continuam vindo da telemetria e da garagem."),
        new PhaseGModule("🏦\nEMPR.", "Empréstimos", "Área preparada para financiamentos, parcelas e descontos automáticos. A estrutura permanece dentro do tablet físico e não abre uma tela gigante."),
        new PhaseGModule("🧾\nHIST.", "Histórico", "Área preparada para consultar viagens e registros operacionais anteriores, mantendo o histórico dentro da experiência do tablet.")
    };
}
