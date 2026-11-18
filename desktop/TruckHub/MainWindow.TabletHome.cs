using System.ComponentModel;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TruckHub;

// Tela inicial do tablet: alterna o card "Operação atual" entre viagem ativa e estado sem viagem.
// Sem viagem, o código existente deixa a origem como "—"; nesse caso mostramos uma mensagem clara
// com atalho para o mercado, em vez de traços soltos e barra de progresso vazia.
public partial class MainWindow
{
    private bool _tabletHomeInitialized;

    private void InitTabletHome()
    {
        if (_tabletHomeInitialized) return;
        _tabletHomeInitialized = true;
        try
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            descriptor?.AddValueChanged(TripOriginText, (_, _) => ApplyTripIdleLayout());
        }
        catch { /* sem o observador o card continua funcionando; só não alterna sozinho */ }
        ApplyTripIdleLayout();
        try
        {
            // Cantos arredondados reais: sem o clip, os fundos retos das camadas internas aparecem nos cantos.
            TabletScreen.SizeChanged += (_, _) => ClipTabletScreen();
            ClipTabletScreen();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => SyncStatusBar();
            timer.Start();
            SyncStatusBar();
        }
        catch { /* acabamento visual: nunca impede o app de abrir */ }
    }

    private void ClipTabletScreen()
    {
        if (TabletScreen.ActualWidth <= 0 || TabletScreen.ActualHeight <= 0) return;
        TabletScreen.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, TabletScreen.ActualWidth, TabletScreen.ActualHeight), 42, 42);
    }

    // Os textos de Wi-Fi e bateria continuam sendo atualizados pelo código existente; aqui só espelhamos em ícones.
    private void SyncStatusBar()
    {
        try
        {
            WifiIcon.Fill = WifiStatusText.Foreground;
            var text = (BatteryPercentText.Text ?? "").Replace("%", "").Trim();
            var known = int.TryParse(text, out var percent);
            BatteryFill.Width = known ? Math.Clamp(percent, 0, 100) / 100.0 * 22.0 : 0;
            BatteryFill.Background = known
                ? (System.Windows.Media.Brush)FindResource(percent <= 20 ? "Red" : percent <= 40 ? "GoldBright" : "Green")
                : (System.Windows.Media.Brush)FindResource("TextMuted");
            BatteryBoltIcon.Visibility = (BatteryModeText.Text ?? "").Contains("EXTERNA") ? Visibility.Visible : Visibility.Collapsed;
            WeekEmptyText.Visibility = WeekTotalText.Text.Contains("0,00") ? Visibility.Visible : Visibility.Collapsed;
        }
        catch { }
    }

    private void ApplyTripIdleLayout()
    {
        try
        {
            var origin = (TripOriginText.Text ?? "").Trim();
            var idle = origin.Length == 0 || origin == "—";
            var active = idle ? Visibility.Collapsed : Visibility.Visible;

            TripEmptyState.Visibility = idle ? Visibility.Visible : Visibility.Collapsed;
            TripRouteRow.Visibility = active;
            TripFooterRow.Visibility = active;
            TripProgressTrack.Visibility = active;
        }
        catch { /* layout da tela inicial nunca pode derrubar a atualização de telemetria */ }
    }

    // Escritório: perfil, saldo, ganhos da semana, pendências e diário de hoje.
    private static readonly CultureInfo OfficeCulture = new("pt-BR");
    private const double WeekBarMax = 120;

    private void UpdateOfficeBalance(decimal balance)
    {
        try
        {
            OfficeBalanceText.Text = balance.ToString("C", OfficeCulture);
            OfficeBalanceText.Foreground = (System.Windows.Media.Brush)FindResource(balance < 0 ? "Red" : "Green");
        }
        catch { /* cartão de saldo nunca derruba a atualização */ }
    }

    private void UpdateOfficeTiles(BankData bank, int documents, int stamped, bool officialSession)
    {
        try
        {
            var muted = (System.Windows.Media.Brush)FindResource("TextMuted");
            var green = (System.Windows.Media.Brush)FindResource("Green");
            var red = (System.Windows.Media.Brush)FindResource("Red");
            var gold = (System.Windows.Media.Brush)FindResource("GoldBright");
            var warn = (System.Windows.Media.Brush)FindResource("Warn");

            // Perfil
            var name = string.IsNullOrWhiteSpace(_phoneOfficialDriverName) ? "Motorista" : _phoneOfficialDriverName.Trim();
            OfficeDriverText.Text = name;
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            OfficeInitialsText.Text = parts.Length == 0 ? "TP"
                : (parts.Length == 1 ? parts[0][..1] : parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
            OfficeTripsText.Text = bank.TripCount.ToString(OfficeCulture);
            OfficeKmText.Text = $"{(double)bank.StatsDistanceKm:N0} km rodados";
            var rank = (DashboardRankingPositionText.Text ?? "").Trim();
            OfficeRankText.Text = string.IsNullOrEmpty(rank) ? "#—" : rank;

            // Progressão (XP/nível) calculada do histórico real
            var grossTotal = bank.TripHistory.Sum(t => t.Gross);
            var prog = DriverProgression.From(DriverProgression.ComputeXp(bank.TripCount, bank.StatsDistanceKm, grossTotal, WeighStation.ViolationCount));
            OfficeLevelText.Text = $"NÍVEL {prog.Level} • {prog.Title.ToUpperInvariant()}";
            OfficeXpText.Text = prog.MaxLevel ? $"{prog.Xp:N0} XP • NÍVEL MÁXIMO" : $"{prog.XpIntoLevel:N0} / {prog.XpForNext:N0} XP";
            OfficeXpBar.Width = OfficeXpTrack.ActualWidth > 0 ? OfficeXpTrack.ActualWidth * prog.Progress : 0;
            _driverPhone?.UpdateProgression(OfficeLevelText.Text, OfficeXpText.Text, prog.Progress);

            // Saldo
            if (!officialSession)
            {
                UpdateOfficeBalance(bank.Balance);
                OfficeBalanceHintText.Text = "Banco local";
            }
            else OfficeBalanceHintText.Text = "Conta oficial da empresa";

            var today = DateTime.Today;
            var trips = bank.TripHistory
                .Select(t => (Day: t.FinishedAtUtc == DateTime.MinValue ? DateTime.MinValue : t.FinishedAtUtc.ToLocalTime().Date, t.Gross, t.DistanceKm))
                .Where(t => t.Day != DateTime.MinValue)
                .ToList();
            var todayTrips = trips.Where(t => t.Day == today).ToList();
            var todayGross = todayTrips.Sum(t => t.Gross);
            OfficeTodayText.Text = (todayGross >= 0 ? "+ " : "- ") + Math.Abs(todayGross).ToString("C", OfficeCulture);

            var debt = bank.HasCompanyLoan ? bank.CompanyLoanTotal - bank.CompanyLoanPaid : (bank.HasLoan ? bank.LoanRemaining : 0m);
            if (debt < 0m) debt = 0m;
            OfficeLoanText.Text = debt.ToString("C", OfficeCulture);
            OfficeLoanText.Foreground = debt > 0m ? red : muted;
            OfficeLoanHintText.Text = debt <= 0m ? "Nenhuma parcela aberta"
                : bank.HasLoan && !bank.HasCompanyLoan
                    ? $"Parcela mín. {bank.LoanInstallmentMin.ToString("C", OfficeCulture)} • {bank.LoanInstallmentsPaid}/{bank.LoanInstallmentsTotal}"
                    : "Empréstimo da empresa em aberto";

            // Ganhos da semana
            var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).ToArray();
            var sums = days.Select(d => trips.Where(t => t.Day == d).Sum(t => t.Gross)).ToArray();
            var max = sums.Max();
            var bars = new[] { WeekBar0, WeekBar1, WeekBar2, WeekBar3, WeekBar4, WeekBar5, WeekBar6 };
            var labels = new[] { WeekDay0, WeekDay1, WeekDay2, WeekDay3, WeekDay4, WeekDay5, WeekDay6 };
            var idle = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x24, 0x31, 0x40));
            for (var i = 0; i < 7; i++)
            {
                bars[i].Height = max > 0m ? Math.Max(6, (double)(sums[i] / max) * WeekBarMax) : 6;
                bars[i].Background = i == 6 ? gold : idle;
                var label = days[i].ToString("ddd", OfficeCulture).TrimEnd('.');
                labels[i].Text = i == 6 ? "Hoje" : char.ToUpperInvariant(label[0]) + label[1..];
            }
            WeekTotalText.Text = sums.Sum().ToString("C", OfficeCulture);

            // Pendências
            var unstamped = Math.Max(0, documents - stamped);
            SetPending(PendDocsText, unstamped > 0 ? $"{unstamped} aguardando" : "Em dia", unstamped > 0 ? warn : green);
            SetPending(PendLoanText, debt > 0m && bank.HasLoan ? bank.LoanInstallmentMin.ToString("C", OfficeCulture) : "Sem parcelas", debt > 0m && bank.HasLoan ? warn : green);
            var fines = WeighStation.ViolationCount;
            SetPending(PendWeighText, fines > 0 ? $"{fines} registrada(s)" : "Sem multas", fines > 0 ? red : green);
            SetPending(PendTripText, _tripActive ? "Em andamento" : "Sem viagem", _tripActive ? green : muted);

            // Custo por km e margem dos últimos 7 dias (combustível + manutenção + pedágios / bruto)
            var weekStart = today.AddDays(-6);
            var weekEntries = bank.TripHistory
                .Where(t => t.FinishedAtUtc != DateTime.MinValue && t.FinishedAtUtc.ToLocalTime().Date >= weekStart)
                .ToList();
            var weekKm = weekEntries.Sum(t => t.DistanceKm);
            var weekGross = weekEntries.Sum(t => t.Gross);
            var weekCost = weekEntries.Sum(t => t.Fuel + t.Maintenance + t.Tolls + t.OtherExpenses);
            if (weekKm > 1 && weekGross > 0m)
            {
                DiaryCostKmText.Text = (weekCost / (decimal)weekKm).ToString("C", OfficeCulture);
                var margin = (weekGross - weekCost) / weekGross * 100m;
                DiaryMarginText.Text = $"{margin:0}%";
                DiaryMarginText.Foreground = margin >= 25m ? green : margin >= 10m ? warn : red;
            }
            else
            {
                DiaryCostKmText.Text = "—";
                DiaryMarginText.Text = "—";
                DiaryMarginText.Foreground = muted;
            }

            // Diário de hoje
            DiaryTripsText.Text = todayTrips.Count.ToString(OfficeCulture);
            var todayKm = todayTrips.Sum(t => t.DistanceKm);
            DiaryKmText.Text = $"{todayKm:N0}";
            var track = DiaryBar.Parent as System.Windows.FrameworkElement;
            DiaryBar.Width = track is { ActualWidth: > 0 } ? track.ActualWidth * Math.Min(1.0, todayKm / 600.0) : 0;
        }
        catch { /* idem */ }
    }

    private static void SetPending(TextBlock block, string text, System.Windows.Media.Brush brush)
    {
        block.Text = text;
        block.Foreground = brush;
    }

    // Pendências viram atalhos para a tela que resolve cada uma.
    private void PendingRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            var args = new RoutedEventArgs();
            switch((sender as FrameworkElement)?.Tag as string)
            {
                case "docs": DocumentsButton_Click(sender, args); break;
                case "loan": BankButtonV15_Click(sender, args); break;
                case "weigh": SummaryButton_Click(sender, args); break;
                case "trip": TachographButton_Click(sender, args); break;
            }
        }
        catch (Exception ex) { App.WriteUiCrashLog("PendingRow_Click", ex); }
    }
}
