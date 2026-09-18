using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly bool _tabletFrameIntegration = TabletFrameIntegration.Register();
}

/// <summary>
/// Coloca o cockpit existente dentro de uma moldura de tablet inspirada
/// diretamente no hardware enviado para o projeto: bezel preto, aro metálico,
/// câmera frontal, alto-falante e botões laterais.
/// </summary>
internal static class TabletFrameIntegration
{
    internal static bool Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        return true;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;
        if (window.Content is not Grid root) return;
        if (Equals(root.Tag, "truckhub-tablet-frame")) return;

        var shell = root.Children.OfType<Border>().FirstOrDefault(b => b.Name == "TabletScreen");
        if (shell == null) return;

        root.Tag = "truckhub-tablet-frame";

        window.Width = 1120;
        window.Height = 800;
        window.MinWidth = 980;
        window.MinHeight = 700;

        // O dashboard fica como uma camada NORMAL do WPF.
        // Não usamos Viewbox nem nenhuma camada sobre a tela, pois isso
        // podia transformar a moldura preta em uma sobreposição do cockpit.
        root.Children.Remove(shell);

        var tabletBody = new Border
        {
            Margin = new Thickness(7),
            CornerRadius = new CornerRadius(55),
            Background = new SolidColorBrush(Color.FromRgb(4, 6, 8)),
            BorderBrush = BuildMetalBrush(),
            BorderThickness = new Thickness(3),
            Effect = new DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 5,
                Opacity = 0.55,
                Color = Colors.Black
            },
            IsHitTestVisible = false
        };
        Panel.SetZIndex(tabletBody, 0);
        root.Children.Add(tabletBody);

        shell.Width = 980;
        shell.Height = 700;
        shell.Margin = new Thickness(0);
        shell.HorizontalAlignment = HorizontalAlignment.Center;
        shell.VerticalAlignment = VerticalAlignment.Center;
        shell.CornerRadius = new CornerRadius(22);
        shell.BorderThickness = new Thickness(0);
        shell.BorderBrush = Brushes.Transparent;
        shell.IsHitTestVisible = true;
        Panel.SetZIndex(shell, 20);
        root.Children.Add(shell);

        // Elementos decorativos ficam fora da área útil e nunca cobrem o dashboard.
        AddTopHandle(root);
        AddCamera(root);
        AddSpeaker(root);
        AddSideButtons(root);
    }

    private static LinearGradientBrush BuildMetalBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(62, 67, 73), 0),
                new GradientStop(Color.FromRgb(224, 228, 232), 0.18),
                new GradientStop(Color.FromRgb(112, 118, 125), 0.42),
                new GradientStop(Color.FromRgb(236, 239, 242), 0.72),
                new GradientStop(Color.FromRgb(61, 66, 72), 1)
            }
        };
    }

    private static void AddTopHandle(Grid root)
    {
        var handle = new Border
        {
            Width = 180,
            Height = 14,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(8, 9, 11)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 53, 59)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false
        };
        Panel.SetZIndex(handle, 80);
        root.Children.Add(handle);
    }

    private static void AddCamera(Grid root)
    {
        var camera = new Grid
        {
            Width = 34,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 55, 0, 0),
            IsHitTestVisible = false
        };
        camera.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(15, 17, 21)),
            Stroke = new SolidColorBrush(Color.FromRgb(84, 90, 98)),
            StrokeThickness = 3
        });
        camera.Children.Add(new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new RadialGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(103, 183, 255), 0),
                    new GradientStop(Color.FromRgb(25, 52, 80), 0.55),
                    new GradientStop(Color.FromRgb(3, 5, 8), 1)
                }
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Panel.SetZIndex(camera, 90);
        root.Children.Add(camera);
    }

    private static void AddSpeaker(Grid root)
    {
        var speaker = new Border
        {
            Width = 170,
            Height = 13,
            CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(24, 27, 31), 0),
                    new GradientStop(Color.FromRgb(6, 8, 10), 0.5),
                    new GradientStop(Color.FromRgb(31, 34, 38), 1)
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 74, 81)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 34),
            IsHitTestVisible = false
        };
        Panel.SetZIndex(speaker, 90);
        root.Children.Add(speaker);
    }

    private static void AddSideButtons(Grid root)
    {
        var leftTop = CreateSideButton(12, 112);
        leftTop.HorizontalAlignment = HorizontalAlignment.Left;
        leftTop.VerticalAlignment = VerticalAlignment.Center;
        leftTop.Margin = new Thickness(0, -155, 0, 0);
        root.Children.Add(leftTop);

        var leftBottom = CreateSideButton(12, 92);
        leftBottom.HorizontalAlignment = HorizontalAlignment.Left;
        leftBottom.VerticalAlignment = VerticalAlignment.Center;
        leftBottom.Margin = new Thickness(0, 70, 0, 0);
        root.Children.Add(leftBottom);

        var right = CreateSideButton(13, 150);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        right.VerticalAlignment = VerticalAlignment.Center;
        right.Margin = new Thickness(0, 0, 0, 0);
        root.Children.Add(right);
    }

    private static Border CreateSideButton(double width, double height)
    {
        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(26, 29, 33), 0),
                    new GradientStop(Color.FromRgb(79, 84, 90), 0.55),
                    new GradientStop(Color.FromRgb(18, 21, 25), 1)
                }
            },
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 67, 74)),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false
        };
    }
}
