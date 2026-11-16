using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

// Dock inferior: poucos botões; os que agrupam assuntos abrem um menu flutuante no mesmo padrão visual.
public partial class MainWindow
{
    private void DockOperation_Click(object sender, RoutedEventArgs e) => ShowDockMenu(sender,
        ("route", "Viagens", SummaryButton_Click),
        ("clock", "Jornada / tacógrafo", TachographButton_Click),
        ("doc", "Documentos", DocumentsButton_Click),
        ("fuel", "Abastecer", FuelButton_Click));

    private void DockFleet_Click(object sender, RoutedEventArgs e) => ShowDockMenu(sender,
        ("warehouse", "Garagem", GarageButtonV15_Click),
        ("truck", "Meu caminhão", MyTruckButton_Click));

    private void DockDriver_Click(object sender, RoutedEventArgs e) => ShowDockMenu(sender,
        ("account", "Meu perfil", MyProfileButton_Click),
        ("trophy", "Ranking", RankingButton_Click));

    private void ShowDockMenu(object sender, params (string Icon, string Text, RoutedEventHandler Handler)[] items)
    {
        if (sender is not FrameworkElement anchor) return;
        var stack = new StackPanel { MinWidth = 240 };
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Top,
            VerticalOffset = -10,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };
        foreach (var item in items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = TpIcons.Create(item.Icon, null, 20);
            icon.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = item.Text, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = row,
                Style = FindResource("TpSecondaryButton") as Style,
                Height = 50,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(16, 0, 16, 0),
                Margin = new Thickness(0, 3, 0, 3)
            };
            icon.SetBinding(TpIcon.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            var handler = item.Handler;
            button.Click += (_, args) => { popup.IsOpen = false; handler(anchor, args); };
            stack.Children.Add(button);
        }
        popup.Child = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(10),
            Background = FindResource("TpSurface") as Brush,
            BorderBrush = FindResource("TpStrokeStrong") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.45 },
            Child = stack
        };
        popup.IsOpen = true;
    }
}
