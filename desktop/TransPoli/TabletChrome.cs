using System.Windows;
using System.Windows.Input;

namespace TransPoli;

// Moldura do tablet: como as janelas usam WindowStyle="None",
// estes handlers cuidam de arrastar / minimizar / maximizar / fechar.
public partial class MainWindow
{
    private void TabletDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) { ToggleTabletMaximize(); return; }
        try { DragMove(); } catch { }
    }

    private void ToggleTabletMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void TabletMinimize_Click(object sender, RoutedEventArgs e)\n    {\n        ShowInTaskbar = true;\n        Hide();\n    }

    private void TabletMaximize_Click(object sender, RoutedEventArgs e) => ToggleTabletMaximize();

    private void TabletClose_Click(object sender, RoutedEventArgs e) => Close();
}

public partial class ActivationWindow
{
    private void TabletDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); } catch { }
    }

    private void TabletMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void TabletClose_Click(object sender, RoutedEventArgs e) => Close();
}

public partial class MainWindow
{
    // Tela "Sobre": versão instalada e créditos.
    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        MessageBox.Show(
            $"TransPoli • Computador de Bordo\nVersão {version}\n\n" +
            "Criado por Felipe Andrade\n© 2026 Felipe Andrade\n\n" +
            "As atualizações são verificadas automaticamente ao abrir o app e a cada 6 horas.",
            "Sobre o TransPoli", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
