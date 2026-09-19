using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private const string TachFuel = "ABASTECIMENTO";

    private DispatcherTimer? _tachTimer;
    private TextBlock? _tachClockText;
    private TextBlock? _tachSpeedText;
    private TextBlock? _tachOdoText;
    private TextBlock? _tachStatusText;
    private TextBlock? _tachStatusSinceText;
    private TextBlock? _tachPaperText;
    private Border? _tachPaperBorder;
    private TextBlock? _tachSessionText;
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
        buttons.Children.Add(TachStatusButton("3 • REFEIÇÃO", TachMeal));
        buttons.Children.Add(TachStatusButton("4 • ESPERA", TachWait));
        buttons.Children.Add(TachStatusButton("5 • ABASTECIMENTO", TachFuel));
        left.Children.Add(buttons);
        _tachSessionText = new TextBlock { Text = "JORNADA • em andamento", FontSize = 10, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(2, 8, 0, 0) };
        left.Children.Add(_tachSessionText);

        var stopButton = new Button { Content = "◼ ENCERRAR REGISTRO ATUAL", Tag = ModalActionTag, Style = FindResource("TabletButton") as Style, Margin = new Thickness(0, 8, 0, 0) };
        stopButton.Click += (_, __) => TachSetStatus(null);
        left.Children.Add(stopButton);

        // --- Lado direito: saída de papel ---
        var right = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = "SAÍDA DE PAPEL", Style = FindResource("Label") as Style });
        var paperSlot = new Border
        {
            Height = 18,
            Background = new SolidColorBrush(Color.FromRgb(0x08, 0x0A, 0x0D)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x42, 0x4A)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 6, 0, 2),
            Padding = new Thickness(18, 2, 18, 2)
        };
        paperSlot.Child = new Border { Height = 4, Background = FindResource("GoldBright") as Brush, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(paperSlot);
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

        var printButton = new Button { Content = "⏏  EJETAR PAPEL • IMPRIMIR ROTEIRO", Tag = ModalActionTag, Style = FindResource("TabletButton") as Style };
        printButton.Click += async (_, e) => { e.Handled = true; await TachPrintAsync(); };
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

    /// <summary>
    /// Mantém o tacógrafo sincronizado com a telemetria sem exigir interação manual.
    /// O motorista ainda pode escolher DESCANSO/REFEIÇÃO/ESPERA manualmente pelo modal.
    /// </summary>
    private void UpdateAutomaticTachographStatus(TelemetrySnapshot data)
    {
        var speed = Math.Abs(data.SpeedKph);
        var status = data.GamePaused
            ? TachWait
            : data.RefuelActive
                ? TachFuel
                : speed > 0.5f
                    ? TachDriving
                    : TachWait;

        if (_tachActive?.Type == status) return;

        // Descanso/refeição escolhidos manualmente permanecem ativos enquanto
        // o caminhão estiver parado. Ao voltar a rodar, a telemetria retoma
        // automaticamente o estado DIREÇÃO.
        if ((_tachActive?.Type == TachRest || _tachActive?.Type == TachMeal) && speed <= 0.5f && !data.GamePaused)
            return;

        var previous = _tachActive?.Type;
        TachSetStatus(status);

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
            TachFuel => "EM ABASTECIMENTO",
            _ => _tachActive.Type
        };
        _tachStatusText.Text = label;
        _tachStatusText.Foreground = FindResource(_tachActive.Type == TachDriving ? "Green" : "Yellow") as Brush;
        _tachStatusSinceText.Text = $"Desde {_tachActive.StartedAtUtc.ToLocalTime():HH:mm} • {_tachActive.OdometerKm:0.0} km";
        if (_tachSessionText != null) _tachSessionText.Text = $"JORNADA • {_stops.Count(s => s.StartedAtUtc.ToLocalTime().Date == DateTime.Now.Date)} atividades hoje";
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

    /// <summary>Finaliza o registro atual, imprime todas as atividades da jornada e anima o papel térmico.</summary>
    private async Task TachPrintAsync()
    {
        if (_tachPaperText == null) return;
        var now = DateTime.UtcNow;
        if (_tachActive != null)
        {
            _tachActive.EndedAtUtc = now;
            _tachActive = null;
        }

        var records = _stops.Where(x => x.StartedAtUtc.ToLocalTime().Date == DateTime.Now.Date)
            .OrderBy(x => x.StartedAtUtc).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("       TRANSPOLI");
        sb.AppendLine("    TACÓGRAFO DIGITAL");
        sb.AppendLine("----------------------------");
        sb.AppendLine($"DATA: {DateTime.Now:dd/MM/yyyy HH:mm}");
        sb.AppendLine($"ATIVIDADES: {records.Count}");
        sb.AppendLine("----------------------------");
        sb.AppendLine("ROTEIRO DA VIAGEM");
        sb.AppendLine($"ORIGEM: {_tripRouteOrigin ?? "—"}");
        sb.AppendLine($"DESTINO: {_tripRouteDestination ?? "—"}");
        sb.AppendLine($"CARGA: {_tripCargo ?? "—"}");
        sb.AppendLine($"VALOR: {(_tripCargoValue.HasValue ? _tripCargoValue.Value.ToString("C2", CultureInfo.GetCultureInfo("pt-BR")) : "—")}");
        sb.AppendLine($"CAMINHÃO: {LastTelemetry?.TruckBrand ?? "—"} {LastTelemetry?.TruckModel ?? ""}".Trim());
        sb.AppendLine("----------------------------");

        if (records.Count == 0)
        {
            sb.AppendLine("NENHUM REGISTRO HOJE.");
        }
        else
        {
            foreach (var record in records)
            {
                var end = record.EndedAtUtc ?? now;
                var duration = end - record.StartedAtUtc;
                sb.AppendLine(TachLabel(record.Type));
                sb.AppendLine($"{record.StartedAtUtc.ToLocalTime():HH:mm} - {end.ToLocalTime():HH:mm}");
                sb.AppendLine($"DURACAO {FormatTachDuration(duration)}");
                sb.AppendLine($"ODOMETRO {record.OdometerKm:0.0} km");
                sb.AppendLine("----------------------------");
            }
        }

        sb.AppendLine("REGISTRO ENCERRADO");
        _tachPaperText.Text = sb.ToString();
        SaveOperations();
        UpdateOpsCounters();
        UpdateTachStatusDisplay();
        await AnimateTachPaperAsync();
    }

    private static string TachLabel(string type) => type switch
    {
        TachDriving => "DIRECAO",
        TachRest => "DESCANSO",
        TachMeal => "REFEICAO",
        TachWait => "ESPERA",
        TachFuel => "ABASTECIMENTO",
        _ => type
    };

    private static string FormatTachDuration(TimeSpan value)
    {
        var minutes = Math.Max(0, (int)Math.Round(value.TotalMinutes));
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private async Task AnimateTachPaperAsync()
    {
        if (_tachPaperBorder == null) return;
        var transform = new TranslateTransform(0, -22);
        _tachPaperBorder.RenderTransform = transform;
        _tachPaperBorder.Opacity = 0.1;
        for (var i = 0; i < 10; i++)
        {
            transform.Y += 2.2;
            _tachPaperBorder.Opacity = Math.Min(1, _tachPaperBorder.Opacity + 0.1);
            await Task.Delay(25);
        }
        _tachPaperBorder.RenderTransform = Transform.Identity;
        _tachPaperBorder.Opacity = 1;
    }

}