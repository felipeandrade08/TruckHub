using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    // Compatibilidade com módulos legados que ainda atualizam indicadores
    // removidos da composição visual atual. Os controles continuam disponíveis
    // para preservar o fluxo sem substituir o XAML principal.
    internal readonly TextBlock TripCargoText = new();
    private readonly TextBlock TripValueText = new();
    private readonly TextBlock TripProgressText2 = new();
    private readonly TextBlock TripDistanceLiveText2 = new();
    private readonly TextBlock TripRemainingText2 = new();
    private readonly TextBlock TripStartText = new();
    private readonly TextBlock TripEtaText = new();
    private readonly TextBlock TripEstimateNoteText = new();
    private readonly Border TripProgressFill2 = new();
    private readonly TextBlock TripTruckText2 = new();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
}