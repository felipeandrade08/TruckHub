using System.ComponentModel;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TransPoli;

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
            SetPending(PendDocsText, unstamped > 0 ? $"{unstamped} aguardando" : "Em dia", unstamped > 0 ? gold : green);
            SetPending(PendLoanText, debt > 0m && bank.HasLoan ? bank.LoanInstallmentMin.ToString("C", OfficeCulture) : "Sem parcelas", debt > 0m && bank.HasLoan ? gold : green);
            var fines = WeighStation.ViolationCount;
            SetPending(PendWeighText, fines > 0 ? $"{fines} registrada(s)" : "Sem multas", fines > 0 ? red : green);
            SetPending(PendTripText, _tripActive ? "Em andamento" : "Sem viagem", _tripActive ? green : muted);

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
}
