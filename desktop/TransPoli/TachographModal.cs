using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// Tacógrafo digital do TransPoli. O visual imita um tacógrafo físico
/// (mostrador em LCD + botões + saída de papel), mas por trás ele reaproveita
/// a mesma lista <see cref="_stops"/> e o mesmo <see cref="SaveOperations"/>
/// já usados pelo resto do app (ver TransPoliOperations.cs) — ou seja, os
/// registros de direção/descanso ficam salvos junto com o restante da
/// operação, sem criar um arquivo/paralelo novo.
/// </summary>
public partial class MainWindow
{
    private const string TachDriving = "DIRECAO";
    private const string TachRest = "DESCANSO";
    private const string TachMeal = "REFEICAO";
    private const string TachWait = "ESPERA";

    private DispatcherTimer? _tachTimer;
    private TextBlock? _tachClockText;
    private TextBlock? _tachSpeedText;
    private TextBlock? _tachOdoText;
    private TextBlock? _tachStatusText;
    private TextBlock? _tachStatusSinceText;
    private TextBlock? _tachPaperText;
    private Border? _tachPaperBorder;
    private StopRecord? _tachActive;

    private void TachographButton_Click(object sender, RoutedEventArgs e)
    {
        if (EnsureModalHost() == null) return;
        // Se já havia um período em aberto de uma sessão anterior do app,
        // reconecta nele em vez de perder o histórico.
        _tachActive ??= _stops.FirstOrDefault(s => s.EndedAtUtc == null);
        ShowModalContent("tachograph", BuildTachographCard());
        StartTachClock();
    }

    private UIElement BuildTachographCard()
    {
        var shell = new Grid { Width = StandardModalWidth, Height = StandardModalHeight, MinWidth = StandardModalWidth, MinHeight = StandardModalHeight, MaxWidth = StandardModalWidth, MaxHeight = StandardModalHeight };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Cabeçalho padrão, igual aos demais modais.
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "📟 TACÓGRAFO DIGITAL", FontSize = 23, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
        titles.Children.Add(new TextBlock { Text = "Registro de direção e descanso da jornada", FontSize = 11, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 4, 0, 0) });
        header.Children.Add(titles);
        var close = new Button { Content = "✕", Tag = ModalActionTag, Style = FindResource("TabletButton") as Style, Width = 48, Height = 44, VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, e) => { e.Handled = true; CloseOperationalModal(); };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        shell.Children.Add(header);

        // "Chassi" escuro do aparelho físico.
        var device = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 16, 0, 0)
        };
        Grid.SetRow(device, 1);
        var deviceGrid = new Grid();
        deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        deviceGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // --- Lado esquerdo: tela LCD + botões físicos ---
        var left = new StackPanel();

        var lcd = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x1A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x5A, 0x2A)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12, 16, 12)
        };
        var lcdBrush = new SolidColorBrush(Color.FromRgb(0xC7, 0xE8, 0x6A));
        var lcdStack = new StackPanel();
        _tachClockText = new TextBlock { Text = "00:00", FontFamily = new FontFamily("Consolas"), FontSize = 34, FontWeight = FontWeights.Bold, Foreground = lcdBrush };
        var row1 = new Grid();
        row1.ColumnDefinitions.Add(new ColumnDefinition());
        row1.ColumnDefinitions.Add(new ColumnDefinition());
        _tachSpeedText = new TextBlock { Text = "0 km/h", FontFamily = new FontFamily("Consolas"), FontSize = 16, Foreground = lcdBrush };
        _tachOdoText = new TextBlock { Text = "0.0 km", FontFamily = new FontFamily("Consolas"), FontSize = 16, Foreground = lcdBrush, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(_tachOdoText, 1);
        row1.Children.Add(_tachSpeedText);
        row1.Children.Add(_tachOdoText);
        lcdStack.Children.Add(_tachClockText);
        lcdStack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(80, 199, 232, 106)), Margin = new Thickness(0, 8, 0, 8) });
        lcdStack.Children.Add(row1);
        lcd.Child = lcdStack;
        left.Children.Add(lcd);

        _tachStatusText = new TextBlock { Text = "SEM REGISTRO ATIVO", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(2, 14, 0, 0) };
        _tachStatusSinceText = new TextBlock { Text = "Selecione um status para começar a registrar.", FontSize = 10, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(2, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
        left.Children.Add(_tachStatusText);
        left.Children.Add(_tachStatusSinceText);

        var buttons = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(TachStatusButton("1 • DIREÇÃO", TachDriving));
        buttons.Children.Add(TachStatusButton("2 • DESCANSO", TachRest));
        buttons.Children.Add(TachStatusButton("⏏ REFEIÇÃO", TachMeal));
        buttons.Children.Add(TachStatusButton("⏏ ESPERA", TachWait));
        left.Children.Add(buttons);

        var stopButton = new Button { Content = "◼ ENCERRAR REGISTRO ATUAL", Tag = ModalActionTag, Style = FindResource("TabletButton") as Style, Margin = new Thickness(0, 8, 0, 0) };
        stopButton.Click += (_, __) => TachSetStatus(null);
        left.Children.Add(stopButton);

        // --- Lado direito: saída de papel ---
        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = "SAÍDA DE PAPEL", Style = FindResource("Label") as Style });
        _tachPaperBorder = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 8, 0, 8),
            Padding = new Thickness(14, 16, 14, 16),
            Height = 360
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _tachPaperText = new TextBlock
        {
            Text = "— aguardando impressão —",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        };
        scroll.Content = _tachPaperText;
        _tachPaperBorder.Child = scroll;
        right.Children.Add(_tachPaperBorder);

        var printButton = new Button { Content = "🖨 IMPRIMIR RESUMO DO DIA", Tag = ModalActionTag, Style = FindResource("TabletButton") as Style };
        printButton.Click += (_, __) => TachPrint();
        right.Children.Add(printButton);

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        deviceGrid.Children.Add(left);
        deviceGrid.Children.Add(right);
        device.Child = deviceGrid;
        shell.Children.Add(device);

        UpdateTachStatusDisplay();
        return shell;
    }

    private Button TachStatusButton(string label, string status)
    {
        var button = new Button
        {
            Content = label,
            Tag = ModalActionTag,
            Style = FindResource("TabletButton") as Style,
            Margin = new Thickness(4),
            Height = 46
        };
        button.Click += (_, __) => TachSetStatus(status);
        return button;
    }

    /// <summary>Fecha o período atual (se houver) e abre um novo com o status escolhido.</summary>
    private void TachSetStatus(string? status)
    {
        var now = DateTime.UtcNow;
        var odometer = LastTelemetry?.OdometerKm ?? _lastOdometer;

        if (_tachActive != null)
        {
            _tachActive.EndedAtUtc = now;
        }

        if (status == null)
        {
            _tachActive = null;
        }
        else
        {
            _tachActive = new StopRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = status,
                Note = "Registrado pelo tacógrafo digital",
                StartedAtUtc = now,
                OdometerKm = odometer
            };
            _stops.Add(_tachActive);
        }

        SaveOperations();
        UpdateOpsCounters();
        UpdateTachStatusDisplay();
    }

    private void UpdateTachStatusDisplay()
    {
        if (_tachStatusText == null || _tachStatusSinceText == null) return;
        if (_tachActive == null)
        {
            _tachStatusText.Text = "SEM REGISTRO ATIVO";
            _tachStatusText.Foreground = FindResource("Muted") as Brush;
            _tachStatusSinceText.Text = "Selecione um status para começar a registrar.";
            return;
        }
        var label = _tachActive.Type switch
        {
            TachDriving => "EM DIREÇÃO",
            TachRest => "EM DESCANSO",
            TachMeal => "EM REFEIÇÃO",
            TachWait => "EM ESPERA",
            _ => _tachActive.Type
        };
        _tachStatusText.Text = label;
        _tachStatusText.Foreground = FindResource(_tachActive.Type == TachDriving ? "Green" : "Yellow") as Brush;
        _tachStatusSinceText.Text = $"Desde {_tachActive.StartedAtUtc.ToLocalTime():HH:mm} • {_tachActive.OdometerKm:0.0} km";
    }

    private void StartTachClock()
    {
        if (_tachTimer == null)
        {
            _tachTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tachTimer.Tick += (_, __) => UpdateTachClock();
        }
        UpdateTachClock();
        _tachTimer.Start();
    }

    internal void StopTachClock() => _tachTimer?.Stop();

    private void UpdateTachClock()
    {
        if (_tachClockText == null) return;
        var data = LastTelemetry;
        _tachClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        _tachSpeedText!.Text = $"{Math.Abs(data?.SpeedKph ?? 0):0} km/h";
        _tachOdoText!.Text = $"{(data?.OdometerKm ?? _lastOdometer):0.0} km";
    }

    /// <summary>Monta o "recibo" do dia com os registros de hoje, no formato de fita de papel.</summary>
    private void TachPrint()
    {
        if (_tachPaperText == null) return;
        var today = DateTime.UtcNow.Date;
        var records = _stops.Where(s => s.StartedAtUtc.ToLocalTime().Date == DateTime.Now.Date)
                             .OrderBy(s => s.StartedAtUtc)
                             .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("     TRANSPOLI • TACÓGRAFO");
        sb.AppendLine($"     {DateTime.Now:dd/MM/yyyy}");
        sb.AppendLine("----------------------------");
        if (records.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Nenhum registro hoje.");
        }
        else
        {
            foreach (var record in records)
            {
                var end = record.EndedAtUtc?.ToLocalTime();
                var duration = (record.EndedAtUtc ?? DateTime.UtcNow) - record.StartedAtUtc;
                sb.AppendLine($"{record.Type,-10} {record.StartedAtUtc.ToLocalTime():HH:mm} - {(end.HasValue ? end.Value.ToString("HH:mm") : "...")}");
                sb.AppendLine($"  duração: {duration:hh\\:mm}   km: {record.OdometerKm:0.0}");
                sb.AppendLine("- - - - - - - - - - - - - -");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"     total de registros: {records.Count}");
        _tachPaperText.Text = sb.ToString();
    }
}
