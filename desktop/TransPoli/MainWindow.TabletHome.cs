using System.ComponentModel;
using System.Globalization;
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

    // Escritório: cartões de gestão do motorista (saldo, viagens, empréstimo, documentos).
    private static readonly CultureInfo OfficeCulture = new("pt-BR");

    private void UpdateOfficeBalance(decimal balance)
    {
        try
        {
            OfficeBalanceText.Text = balance.ToString("C", OfficeCulture);
            OfficeBalanceText.Foreground = balance < 0 ? (System.Windows.Media.Brush)FindResource("Red") : (System.Windows.Media.Brush)FindResource("Green");
        }
        catch { /* cartão de saldo nunca derruba a atualização */ }
    }

    private void UpdateOfficeTiles(BankData bank, int documents, int stamped, bool officialSession)
    {
        try
        {
            if (!officialSession)
            {
                UpdateOfficeBalance(bank.Balance);
                OfficeBalanceHintText.Text = "Banco local";
            }
            else OfficeBalanceHintText.Text = "Conta oficial da empresa";
            OfficeTripsText.Text = bank.TripCount.ToString(OfficeCulture);
            OfficeKmText.Text = $"{(double)bank.StatsDistanceKm:N0} km rodados";
            var debt = bank.HasCompanyLoan ? bank.CompanyLoanTotal - bank.CompanyLoanPaid : (bank.HasLoan ? bank.LoanRemaining : 0m);
            if (debt > 0m)
            {
                OfficeLoanText.Text = debt.ToString("C", OfficeCulture);
                OfficeLoanHintText.Text = bank.HasLoan && !bank.HasCompanyLoan
                    ? $"Parcela mín. {bank.LoanInstallmentMin.ToString("C", OfficeCulture)} • {bank.LoanInstallmentsPaid}/{bank.LoanInstallmentsTotal}"
                    : "Empréstimo da empresa em aberto";
            }
            else
            {
                OfficeLoanText.Text = "Sem dívida";
                OfficeLoanHintText.Text = "Nenhuma parcela aberta";
            }
            OfficeDocsText.Text = documents.ToString(OfficeCulture);
            OfficeDocsHintText.Text = $"{stamped} carimbados";
        }
        catch { /* idem */ }
    }
}
