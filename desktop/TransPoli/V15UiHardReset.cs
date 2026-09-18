using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

// V1.0.15 build validation
namespace TransPoli;

/// <summary>V1.0.15: garante que controles legados não permaneçam visíveis no cockpit.</summary>
public partial class MainWindow
{
    private static readonly bool V15HardResetRegistered = RegisterV15HardReset();
    private bool _v15HardResetApplied;

    private static bool RegisterV15HardReset()
    {
        // DESATIVADO durante a estabilização do cockpit.
        // Este patch altera a árvore visual em ContextIdle e pode deixar a janela
        // viva/visível com a composição WPF vazia.
        return false;
    }

    private static void OnV15HardResetLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow w || w._v15HardResetApplied) return;
        w._v15HardResetApplied = true;
        w.Dispatcher.BeginInvoke(new Action(w.ApplyV15HardReset), DispatcherPriority.ContextIdle);
    }

    private void ApplyV15HardReset()
    {
        foreach (var element in DescendantsHardReset(this).ToList())
        {
            if (element is TextBlock text)
            {
                var value = text.Text ?? string.Empty;
                if (value.Contains("1.0.14", StringComparison.OrdinalIgnoreCase) || value.Contains("V1.0.14", StringComparison.OrdinalIgnoreCase))
                    text.Text = value.Replace("V1.0.14", "V1.0.15", StringComparison.OrdinalIgnoreCase).Replace("1.0.14", "1.0.15", StringComparison.OrdinalIgnoreCase);
                if (text.Name is "VersionText" or "AppVersionText" or "BuildVersionText") text.Text = "V1.0.15";
                if (value.Contains("ocorrências", StringComparison.OrdinalIgnoreCase) || value.Contains("paradas", StringComparison.OrdinalIgnoreCase)) text.Visibility = Visibility.Collapsed;
            }
            else if (element is Button button && IsRemovedLegacyAction(button.Content?.ToString()))
            {
                button.Visibility = Visibility.Collapsed;
                button.IsEnabled = false;
            }
        }

        foreach (var text in DescendantsHardReset(this).OfType<TextBlock>())
            if (text.Name == "OpsCounterText") text.Visibility = Visibility.Collapsed;

        RemoveLegacyAlertPanel();
    }

    private void RemoveLegacyAlertPanel()
    {
        var alertText = DescendantsHardReset(this).OfType<TextBlock>().FirstOrDefault(x => x.Name == "AlertText");
        if (alertText == null) return;
        var parent = FindParent<Panel>(alertText);
        if (parent == null) return;
        var heading = parent.Children.OfType<TextBlock>().FirstOrDefault(x => string.Equals(x.Text, "ALERTAS", StringComparison.OrdinalIgnoreCase));
        if (heading != null) heading.Text = "MERCADO DE CARGAS";
        alertText.Text = "📦 Mercado de cargas";
        alertText.Foreground = FindResource("Text") as Brush ?? alertText.Foreground;
        alertText.TextWrapping = TextWrapping.Wrap;
    }

    private static bool IsRemovedLegacyAction(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim();
        return s.Contains("PARADAS", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("OCORR", StringComparison.OrdinalIgnoreCase) ||
               s.Equals("DOCS", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("DOCUMENTOS", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("ALERTAS", StringComparison.OrdinalIgnoreCase);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current != null)
        {
            if (current is T result) return result;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static IEnumerable<DependencyObject> DescendantsHardReset(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in DescendantsHardReset(child)) yield return descendant;
        }
    }
}
