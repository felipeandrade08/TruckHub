using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

/// <summary>
/// Mantém o painel rápido dedicado às funções do computador de bordo.
/// Atualização e Sobre ficam no cabeçalho, onde são funções de sistema.
/// </summary>
internal static class SystemActionsIntegration
{
    private static readonly bool Registered = Register();

    private static bool Register()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded), true);
        return true;
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window) return;

        var quick = FindByButtonText(window, "⭳ ATUALIZAR APP");
        var about = FindByButtonText(window, "ℹ SOBRE");
        if (quick != null) quick.Visibility = Visibility.Collapsed;
        if (about != null) about.Visibility = Visibility.Collapsed;

        if (FindByTag(window, "system-actions") != null) return;

        var title = FindTextBlock(window, "TRANSPOLI");
        if (title == null) return;
        var titlePanel = FindAncestor<StackPanel>(title);
        if (titlePanel == null) return;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Tag = "system-actions",
            Margin = new Thickness(0, 8, 0, 0)
        };

        actions.Children.Add(CreateButton("⭳ ATUALIZAR APP", "system-update", window.UpdateButton_Click));
        actions.Children.Add(CreateButton("ℹ SOBRE", "system-about", window.AboutButton_Click));
        titlePanel.Children.Add(actions);
    }

    private static Button CreateButton(string text, string tag, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = text,
            Tag = tag,
            Style = Application.Current.FindResource("TabletButton") as Style,
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 10,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Click += click;
        return button;
    }

    private static Button? FindByButtonText(DependencyObject root, string text)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Button button && string.Equals(button.Content?.ToString(), text, StringComparison.Ordinal))
                return button;
            var found = FindByButtonText(child, text);
            if (found != null) return found;
        }
        return null;
    }

    private static TextBlock? FindTextBlock(DependencyObject root, string text)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock block && string.Equals(block.Text, text, StringComparison.Ordinal))
                return block;
            var found = FindTextBlock(child, text);
            if (found != null) return found;
        }
        return null;
    }

    private static DependencyObject? FindByTag(DependencyObject root, string tag)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && string.Equals(element.Tag?.ToString(), tag, StringComparison.Ordinal))
                return child;
            var found = FindByTag(child, tag);
            if (found != null) return found;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current != null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is T typed) return typed;
        }
        return null;
    }
}
