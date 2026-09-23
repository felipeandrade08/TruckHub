using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

/// <summary>
/// 💰 BANCO DO MOTORISTA
///
/// Reúne, em uma tela só, tudo que antes estava pela metade:
/// saldo, livro-caixa, tarifas por carga, empréstimo com parcelas e a
/// simulação da viagem em andamento (com os litros vindos da telemetria).
/// </summary>
public partial class MainWindow
{
    private static readonly CultureInfo Brl = CultureInfo.GetCultureInfo("pt-BR");
    private string _bankTab = "saldo";
    private string _bankLedgerFilter = "todos";

    // Nome antigo mantido: o botão "💰 ECONOMIA" do tablet chama por aqui.
    internal void ShowEconomyModal() => ShowBankModal();

    internal async void ShowBankModal(string? tab = null)
    {
        if (!string.IsNullOrWhiteSpace(tab)) _bankTab = tab!;
        if (EnsureModalHost() == null) return;

        ShowModalContent("bank", BuildModalLoading("CARREGANDO BANCO DO MOTORISTA..."));

        try
        {
            var data = LoadBankDataLocal();
            await LoadCompanyLoanDataAsync(data);
            ShowModalContent("bank", BuildModalCard(
                "💰 BANCO DO MOTORISTA",
                BuildBankBody(data),
                $"Dados locais • {DateTime.Now:dd/MM/yyyy HH:mm}"));
        }
        catch (Exception ex)
        {
            ShowModalContent("bank", BuildModalCard("BANCO DO MOTORISTA",
                ModalLine($"Não foi possível carregar o banco local.\n\n{ex.Message}", 13)));
        }
    }

    /* ----------------------------- DADOS ----------------------------- */

    private BankData LoadBankDataLocal()
    {
        var data = new BankData();
        var store = LocalData.Current ?? throw new InvalidOperationException("Banco local ainda não foi inicializado.");

        var economy = new LocalEconomyRepository(store.Db);
        var summary = economy.GetSummary();
        data.Balance = summary.Balance;
        data.TotalCredits = summary.Credits;
        data.TotalDebits = summary.Debits;
        data.TripCount = summary.TripCount;

        data.PendingSyncCount = economy.GetPendingSyncCount();
        data.SyncStatus = data.PendingSyncCount > 0
            ? "PENDENTE DE SINCRONIZAÇÃO"
            : string.IsNullOrWhiteSpace(SecureTokenStore.Read())
                ? "BANCO LOCAL"
                : "SINCRONIZADO";

        var localLoan = economy.GetActiveLoan();
        if (localLoan is not null)
        {
            data.HasLoan = true;
            data.LoanPrincipal = localLoan.Principal;
            data.LoanRemaining = localLoan.Remaining;
            data.LoanPct = localLoan.RepaymentPct;
            data.LoanInstallmentsTotal = localLoan.InstallmentsTotal;
            data.LoanInstallmentsPaid = localLoan.InstallmentsPaid;
            data.LoanInstallmentMin = localLoan.InstallmentMin;
            data.LoanInterestMonthly = localLoan.InterestMonthlyPct;
            data.LoanTotalPayable = localLoan.TotalPayable;
        }

        foreach (var entry in economy.GetRecent(500))
        {
            data.Ledger.Add(new LedgerEntry
            {
                Type = entry.Type,
                TripId = entry.TripId,
                Description = entry.Description,
                Amount = entry.Amount,
                BalanceAfter = 0,
                CreatedAt = entry.OccurredAtUtc
            });
        }

        data.Ledger.Sort((a,b) => b.CreatedAt.CompareTo(a.CreatedAt));

        // O livro-caixa usa o mesmo ledger local do extrato, incluindo
        // receitas reconciliadas de viagens antigas. Assim, extrato, saldo e caixa
        // não apresentam fontes diferentes para a mesma movimentação.
        var cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (var group in data.Ledger.Where(x => x.CreatedAt >= cutoff).GroupBy(x => x.CreatedAt.ToLocalTime().Date).OrderByDescending(x => x.Key))
        {
            var credits = group.Where(x => x.Amount > 0).Sum(x => x.Amount);
            var debits = -group.Where(x => x.Amount < 0).Sum(x => x.Amount);
            data.CashbookDays.Add(new CashbookDay
            {
                Day = group.Key,
                Credits = credits,
                Debits = debits,
                Result = credits - debits,
                Movements = group.Count()
            });
        }

        data.StatsTrips = summary.TripCount;
        data.StatsFuelLiters = GetLocalDecimal(store.Db,
            "SELECT COALESCE(SUM(fuel_consumed_l),0) FROM trip WHERE status='finished';");
        data.StatsDistanceKm = GetLocalDecimal(store.Db,
            "SELECT COALESCE(SUM(distance_km),0) FROM trip WHERE status='finished';");

        // Receita = somente fretes de viagens concluídas.
        // Créditos de empréstimos ou outras entradas não entram em Receita/KM.
        data.StatsRevenue = GetLocalDecimal(store.Db,
            "SELECT COALESCE(SUM(income_gross),0) FROM trip WHERE status='finished' AND income_gross > 0;");
        data.StatsExpenses = summary.Debits;
        data.StatsProfit = data.StatsRevenue - data.StatsExpenses;
        data.StatsAverageKmPerLiter = data.StatsFuelLiters > 0
            ? data.StatsDistanceKm / data.StatsFuelLiters : 0;
        data.StatsRevenuePerKm = data.StatsDistanceKm > 0
            ? data.StatsRevenue / data.StatsDistanceKm : 0;
        data.StatsCostPerKm = data.StatsDistanceKm > 0
            ? data.StatsExpenses / data.StatsDistanceKm : 0;
        data.StatsProfitPerKm = data.StatsDistanceKm > 0
            ? data.StatsProfit / data.StatsDistanceKm : 0;

        using (var tripCmd = store.Db.Connection.CreateCommand())
        {
            tripCmd.CommandText = @"
SELECT t.id,t.cargo_name,t.source_city,t.destination_city,
       COALESCE(t.distance_km,0),COALESCE(t.rate_per_km,0),
       COALESCE(t.income_gross,0),COALESCE(t.expense_total,0),
       COALESCE(t.net_value,0),t.finished_at_utc
FROM trip t
WHERE t.status='finished'
ORDER BY t.finished_at_utc DESC
LIMIT 30;";
            using var tr = tripCmd.ExecuteReader();
            while (tr.Read())
            {
                var tripId = tr.GetString(0);
                data.TripHistory.Add(new TripFinancialEntry
                {
                    Id = tripId,
                    Cargo = tr.IsDBNull(1) ? "Carga" : tr.GetString(1),
                    Origin = tr.IsDBNull(2) ? "" : tr.GetString(2),
                    Destination = tr.IsDBNull(3) ? "" : tr.GetString(3),
                    DistanceKm = tr.GetDouble(4),
                    RatePerKm = tr.GetDecimal(5),
                    Gross = tr.GetDecimal(6),
                    Expenses = tr.GetDecimal(7),
                    Net = tr.GetDecimal(8),
                    FinishedAtUtc = tr.IsDBNull(9) ? DateTime.MinValue :
                        DateTime.Parse(tr.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                });
            }
        }

        foreach (var trip in data.TripHistory)
        {
            trip.Fuel = GetLocalDecimal(store.Db,
                "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE trip_id=@id AND type='fuel_expense';",
                ("@id", trip.Id));
            trip.Maintenance = GetLocalDecimal(store.Db,
                "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE trip_id=@id AND type='maintenance_expense';",
                ("@id", trip.Id));
            trip.LoanInstallment = GetLocalDecimal(store.Db,
                "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE trip_id=@id AND type='loan_installment';",
                ("@id", trip.Id));
            trip.Net = trip.Gross - trip.Fuel - trip.Maintenance - trip.LoanInstallment -
                       GetLocalDecimal(store.Db,
                           "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE trip_id=@id AND amount < 0 AND type NOT IN ('fuel_expense','maintenance_expense','loan_installment');",
                           ("@id", trip.Id));
        }

        data.ActiveTripId = GetLocalString(store.Db,
            "SELECT id FROM trip WHERE status='active' ORDER BY started_at_utc DESC LIMIT 1;") ?? "";
        if (!string.IsNullOrWhiteSpace(data.ActiveTripId))
        {
            data.ActiveCargo = GetLocalString(store.Db,
                "SELECT COALESCE(cargo_name,'Carga') FROM trip WHERE id=@id;",
                ("@id", data.ActiveTripId)) ?? "Carga";
            data.PreviewDistance = GetLocalDecimal(store.Db,
                "SELECT MAX(0, COALESCE(distance_km,0)) FROM trip WHERE id=@id;",
                ("@id", data.ActiveTripId));
            data.PreviewRate = GetLocalDecimal(store.Db,
                "SELECT COALESCE(rate_per_km,0) FROM trip WHERE id=@id;",
                ("@id", data.ActiveTripId));
            data.PreviewKmRevenue = data.PreviewDistance * data.PreviewRate;
            data.PreviewFuelLiters = GetLocalDecimal(store.Db,
                "SELECT COALESCE(fuel_consumed_l,0) FROM trip WHERE id=@id;",
                ("@id", data.ActiveTripId));
            data.PreviewFuelCost = GetLocalDecimal(store.Db,
                "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE trip_id=@id AND type='fuel_expense';",
                ("@id", data.ActiveTripId));
            data.PreviewMaintenance = GetLocalDecimal(store.Db,
                "SELECT COALESCE(-SUM(amount),0) FROM economy_transaction WHERE trip_id=@id AND type='maintenance_expense';",
                ("@id", data.ActiveTripId));
            data.PreviewGross = data.PreviewKmRevenue;
            data.PreviewNet = GetLocalDecimal(store.Db,
                "SELECT COALESCE(SUM(amount),0) FROM economy_transaction WHERE trip_id=@id;",
                ("@id", data.ActiveTripId));
            data.HasPreview = true;
            data.PreviewConsumption = data.PreviewDistance > 0 && data.PreviewFuelLiters > 0
                ? data.PreviewFuelLiters / data.PreviewDistance : 0;
        }

        var rates = new LocalTripRepository(store.Db);
        rates.ResolveRatePerKm("Carvão");
        using (var c = store.Db.Connection.CreateCommand())
        {
            c.CommandText = "SELECT name,rate_per_km FROM cargo WHERE active=1 AND source='local' ORDER BY name;";
            using var r = c.ExecuteReader();
            while (r.Read())
                data.Rates.Add(new CargoRate { Name = r.GetString(0), RatePerKm = r.GetDecimal(1) });
        }

        return data;
    }

    private static decimal GetLocalDecimal(TransPoliDb db, string sql, params (string Name, object Value)[] parameters)
    {
        using var c = db.Connection.CreateCommand();
        c.CommandText = sql;
        foreach (var p in parameters) c.Parameters.AddWithValue(p.Name, p.Value);
        return Convert.ToDecimal(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static string? GetLocalString(TransPoliDb db, string sql, params (string Name, object Value)[] parameters)
    {
        using var c = db.Connection.CreateCommand();
        c.CommandText = sql;
        foreach (var p in parameters) c.Parameters.AddWithValue(p.Name, p.Value);
        var value = c.ExecuteScalar();
        return value is null || value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private async Task<HttpResponseMessage> SendBankRequestAsync(
        HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, $"{ApiBaseUrl}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await _http.SendAsync(request);
    }

    private async Task LoadCompanyLoanDataAsync(BankData data)
    {
        var token=SecureTokenStore.Read(); if(string.IsNullOrWhiteSpace(token))return;
        try
        {
            using var response=await SendBankRequestAsync(HttpMethod.Get,"/me/company-loans",token!);
            if(!response.IsSuccessStatusCode)return;
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(!doc.RootElement.TryGetProperty("loans",out var loans)||loans.ValueKind!=JsonValueKind.Array)return;
            var latest=loans.EnumerateArray().FirstOrDefault();
            if(latest.ValueKind==JsonValueKind.Undefined)return;
            static decimal D(JsonElement e,string n)=>e.TryGetProperty(n,out var v)&&decimal.TryParse(v.ToString(),NumberStyles.Any,CultureInfo.InvariantCulture,out var x)?x:0m;
            static string S(JsonElement e,string n)=>e.TryGetProperty(n,out var v)?v.ToString():"";
            data.HasCompanyLoan=true;data.CompanyLoanStatus=S(latest,"status");data.CompanyLoanPrincipal=D(latest,"principal");data.CompanyLoanTotal=D(latest,"total_due");data.CompanyLoanPaid=D(latest,"paid_amount");data.CompanyLoanInterest=D(latest,"interest_rate");data.CompanyLoanPct=D(latest,"repayment_percent");
        } catch { }
    }

    /* ------------------------------ UI ------------------------------- */

    private UIElement BuildBankBodyLegacy(BankData data)
    {
        var panel = new StackPanel();

        // Saldo em destaque
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "SALDO DISPONÍVEL",
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush
        });
        header.Children.Add(new TextBlock
        {
            Text = Money(data.Balance),
            FontSize = 38,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource(data.Balance >= 0 ? "Green" : "Yellow") as Brush,
            Margin = new Thickness(0, 2, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Entradas {Money(data.TotalCredits)}   •   Saídas {Money(data.TotalDebits)}   •   {data.TripCount} viagens pagas",
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(ModalPanel(header));

        // Abas
        panel.Children.Add(BuildBankTabs());

        panel.Children.Add(_bankTab switch
        {
            "caixa" => BuildCashbookTab(data),
            "tarifas" => BuildRatesTab(data),
            "emprestimo" => BuildLoanTab(data),
            "estatisticas" => BuildStatisticsTab(data),
            "viagem" => BuildTripTab(data),
            _ => BuildBalanceTab(data)
        });

        var refresh = ModalButton("↻ ATUALIZAR");
        refresh.Click += (_, e) =>
        {
            e.Handled = true;
            InvalidateBankCache();
            ShowBankModal();
        };
        panel.Children.Add(refresh);

        return panel;
    }

    private UIElement BuildBankTabs()
    {
        var tabs = new[]
        {
            ("saldo", "EXTRATO"),
            ("caixa", "LIVRO-CAIXA"),
            ("viagem", "VIAGEM"),
            ("tarifas", "TARIFAS"),
            ("emprestimo", "CRÉDITO"),
            ("estatisticas", "ESTATÍSTICAS")
        };

        var grid = new UniformGrid { Columns = tabs.Length, Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (key, label) in tabs)
        {
            var active = _bankTab == key;
            var button = new Button
            {
                Content = label,
                Tag = ModalActionTag,
                Style = FindResource("TabletButton") as Style,
                Margin = new Thickness(2),
                Padding = new Thickness(4, 9, 4, 9),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Opacity = active ? 1.0 : 0.55
            };
            var target = key;
            button.Click += (_, e) => { e.Handled = true; ShowBankModal(target); };
            grid.Children.Add(button);
        }
        return grid;
    }

    /* --------------------------- ABA EXTRATO ------------------------- */

    private UIElement BuildBalanceTab(BankData data)
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalSectionTitle("EXTRATO", "PIX E PAGAMENTOS"));
        panel.Children.Add(ModalLine("Conta operacional do motorista • movimentações registradas localmente", 11));

        if (data.Ledger.Count == 0)
        {
            panel.Children.Add(ModalLine(
                "Nenhuma movimentação ainda. Finalize uma viagem para receber o primeiro frete.", 13));
            return panel;
        }

        panel.Children.Add(BuildLedgerFilters());

        var filtered = data.Ledger.FindAll(entry => _bankLedgerFilter switch
        {
            "receitas" => entry.Amount > 0 && entry.Type == "trip_income",
            "despesas" => entry.Amount < 0,
            "viagens" => entry.Type == "trip_income",
            "emprestimos" => entry.Type.StartsWith("loan_", StringComparison.OrdinalIgnoreCase),
            _ => true
        });

        if (filtered.Count == 0)
        {
            panel.Children.Add(ModalLine("Nenhuma movimentação encontrada neste filtro.", 12));
            return panel;
        }

        var box = new StackPanel();
        foreach (var entry in filtered.GetRange(0, Math.Min(25, filtered.Count)))
        {
            var positive = entry.Amount >= 0;
            box.Children.Add(ModalValueRow(
                $"{EntryIcon(entry.Type)} {EntryLabel(entry.Type)}\n{Narrative(entry)}\n{entry.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}",
                (positive ? "+" : "") + Money(entry.Amount),
                positive ? "Green" : "Yellow"));
        }
        panel.Children.Add(ModalPanel(box));
        return panel;
    }

    private UIElement BuildLedgerFilters()
    {
        var filters = new[]
        {
            ("todos", "TODOS"),
            ("receitas", "RECEITAS"),
            ("despesas", "DESPESAS"),
            ("viagens", "VIAGENS"),
            ("emprestimos", "EMPRÉSTIMOS")
        };
        var grid = new UniformGrid { Columns = filters.Length, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (key, label) in filters)
        {
            var button = new Button
            {
                Content = label,
                Tag = ModalActionTag,
                Style = FindResource("TabletButton") as Style,
                Margin = new Thickness(2),
                Padding = new Thickness(3, 7, 3, 7),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Opacity = _bankLedgerFilter == key ? 1.0 : 0.5
            };
            var target = key;
            button.Click += (_, e) => { e.Handled = true; _bankLedgerFilter = target; ShowBankModal("saldo"); };
            grid.Children.Add(button);
        }
        return grid;
    }

    /* ------------------------- ABA LIVRO-CAIXA ----------------------- */

    private UIElement BuildCashbookTab(BankData data)
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalSectionTitle("LIVRO-CAIXA", "ÚLTIMOS 30 DIAS"));

        if (data.CashbookDays.Count == 0)
        {
            panel.Children.Add(ModalLine("Nenhum lançamento no período.", 13));
            return panel;
        }

        decimal credits = 0, debits = 0;
        foreach (var day in data.CashbookDays) { credits += day.Credits; debits += day.Debits; }

        var totals = new StackPanel();
        totals.Children.Add(ModalValueRow("Total de entradas", Money(credits), "Green"));
        totals.Children.Add(ModalValueRow("Total de saídas", Money(debits), "Yellow"));
        totals.Children.Add(ModalValueRow("Resultado do período", Money(credits - debits),
            credits - debits >= 0 ? "Green" : "Yellow"));
        panel.Children.Add(ModalPanel(totals));

        panel.Children.Add(ModalLabel("POR DIA"));
        var box = new StackPanel();
        foreach (var day in data.CashbookDays)
        {
            box.Children.Add(ModalValueRow(
                $"{day.Day.ToLocalTime():dd/MM/yyyy} • {day.Movements} lançamentos\nentradas {Money(day.Credits)} / saídas {Money(day.Debits)}",
                Money(day.Result),
                day.Result >= 0 ? "Green" : "Yellow"));
        }
        panel.Children.Add(ModalPanel(box));
        return panel;
    }

    /* ---------------------------- ABA VIAGEM ------------------------- */

    private UIElement BuildTripTab(BankData data)
    {
        var panel = new StackPanel();

        if (data.HasPreview)
        {
            panel.Children.Add(ModalLabel($"VIAGEM ATUAL • {data.ActiveCargo}"));

            var revenue = new StackPanel();
            revenue.Children.Add(new TextBlock
            {
                Text = "COMO O FRETE ESTÁ SENDO CALCULADO",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Muted") as Brush,
                Margin = new Thickness(0, 0, 0, 6)
            });
            revenue.Children.Add(ModalValueRow(
                $"🚚 Pagamento por km ({data.PreviewDistance:0.0} km × {Money(data.PreviewRate)}/km)",
                Money(data.PreviewKmRevenue), "Green"));
            revenue.Children.Add(ModalValueRow("⚖️ Adicional por peso excedente",
                Money(data.PreviewWeightSurcharge), "Green"));
            revenue.Children.Add(ModalValueRow(
                $"⭐ Bônus de eficiência ({data.PreviewConsumption:0.000} L/km)",
                Money(data.PreviewEfficiencyBonus),
                data.PreviewEfficiencyBonus > 0 ? "Green" : "Muted"));
            revenue.Children.Add(ModalValueRow(
                data.PreviewCleanDelivery ? "⭐ Bônus de entrega sem avaria" : "⭐ Bônus sem avaria (perdido)",
                Money(data.PreviewCleanBonus),
                data.PreviewCleanBonus > 0 ? "Green" : "Muted"));
            if (data.PreviewDamagePenalty > 0)
                revenue.Children.Add(ModalValueRow("⚠ Penalidade por avaria",
                    "-" + Money(data.PreviewDamagePenalty), "Yellow"));
            revenue.Children.Add(ModalValueRow("RECEITA BRUTA", Money(data.PreviewGross), "Green"));
            panel.Children.Add(ModalPanel(revenue));

            var costs = new StackPanel();
            costs.Children.Add(new TextBlock
            {
                Text = "CUSTOS AUTOMÁTICOS",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Muted") as Brush,
                Margin = new Thickness(0, 0, 0, 6)
            });
            costs.Children.Add(ModalValueRow(
                $"⛽ Combustível ({data.PreviewFuelLiters:0.0} L da telemetria)",
                "-" + Money(data.PreviewFuelCost), "Yellow"));
            costs.Children.Add(ModalValueRow("🛠️ Manutenção", "-" + Money(data.PreviewMaintenance), "Yellow"));
            costs.Children.Add(ModalValueRow("RESULTADO LÍQUIDO", Money(data.PreviewNet),
                data.PreviewNet >= 0 ? "Green" : "Yellow"));
            panel.Children.Add(ModalPanel(costs));

            if (data.PreviewMarginApplied)
            {
                panel.Children.Add(ModalPanel(new TextBlock
                {
                    Text = $"📊 MARGEM MÍNIMA DE SEGURANÇA APLICADA\n\nA tarifa da carga pagaria menos que o custo operacional. O TransPoli elevou o frete para {Money(data.PreviewRevenueFloor)}, garantindo {data.MinimumMargin:0.##}% acima do custo.",
                    FontSize = 12,
                    Foreground = FindResource("Green") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }));
            }

            if (data.HasLoan)
            {
                var estimate = Math.Max(0, data.PreviewNet) * data.LoanPct / 100m;
                panel.Children.Add(ModalPanel(new TextBlock
                {
                    Text = $"📉 Ao finalizar, {data.LoanPct:0.##}% da receita líquida (cerca de {Money(estimate)}) será descontada automaticamente para o empréstimo.",
                    FontSize = 12,
                    Foreground = FindResource("Muted") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }));
            }
        }
        else
        {
            panel.Children.Add(ModalLabel("VIAGEM ATUAL"));
            panel.Children.Add(ModalLine(
                "Nenhuma viagem ativa no momento.", 12));
        }

        panel.Children.Add(ModalLabel("DETALHAMENTO DAS ÚLTIMAS VIAGENS"));

        if (data.TripHistory.Count == 0)
        {
            panel.Children.Add(ModalLine(
                "Nenhuma viagem finalizada com dados financeiros locais.", 12));
            return panel;
        }

        foreach (var trip in data.TripHistory)
        {
            var card = new StackPanel();
            card.Children.Add(ModalValueRow(
                $"🚛 {trip.Cargo}\n{trip.Origin} → {trip.Destination}",
                Money(trip.Net),
                trip.Net >= 0 ? "Green" : "Yellow"));
            card.Children.Add(ModalValueRow(
                $"{trip.DistanceKm:0.0} km × {Money(trip.RatePerKm)}/km",
                $"Bruto {Money(trip.Gross)}"));
            card.Children.Add(ModalValueRow(
                $"⛽ Combustível   •   🔧 Manutenção   •   🏦 Empréstimo",
                $"{Money(trip.Fuel)}   •   {Money(trip.Maintenance)}   •   {Money(trip.LoanInstallment)}",
                "Muted"));
            card.Children.Add(ModalValueRow(
                $"Finalizada {trip.FinishedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
                $"Líquido {Money(trip.Net)}",
                trip.Net >= 0 ? "Green" : "Yellow"));
            panel.Children.Add(ModalPanel(card));
        }

        return panel;
    }

    /* --------------------------- ABA TARIFAS ------------------------- */

    private UIElement BuildRatesTab(BankData data)
    {
        var panel = new StackPanel();

        panel.Children.Add(ModalLabel("TARIFAS POR TIPO DE CARGA"));
        if (data.Rates.Count == 0) panel.Children.Add(ModalLine("Tabela de tarifas indisponível.", 13));
        else
        {
            var box = new StackPanel();
            foreach (var rate in data.Rates)
                box.Children.Add(ModalValueRow(rate.Name, $"{Money(rate.RatePerKm)}/km", "Green"));
            panel.Children.Add(ModalPanel(box));
        }

        panel.Children.Add(ModalLabel("PARÂMETROS OPERACIONAIS"));
        var box2 = new StackPanel();
        box2.Children.Add(ModalValueRow("⛽ Diesel", $"{Money(data.FuelPrice)}/L"));
        box2.Children.Add(ModalValueRow("🛠️ Manutenção", $"{Money(data.MaintenancePerKm)}/km"));
        box2.Children.Add(ModalValueRow("⚖️ Adicional por peso",
            $"{data.WeightSurcharge:0.0000} /t·km acima de {data.FreeWeightTons:0} t"));
        box2.Children.Add(ModalValueRow("📊 Margem mínima de segurança", $"{data.MinimumMargin:0.##}%"));
        box2.Children.Add(ModalValueRow("⭐ Bônus de eficiência",
            $"{data.EfficiencyBonusPct:0.##}% até {data.EfficiencyTarget:0.00} L/km"));
        box2.Children.Add(ModalValueRow("⭐ Bônus sem avaria", $"{data.CleanBonusPct:0.##}%"));
        box2.Children.Add(ModalValueRow("⚠ Penalidade por avaria", $"até {data.DamagePenaltyPct:0.##}%"));
        panel.Children.Add(ModalPanel(box2));

        return panel;
    }

    /* -------------------------- ABA EMPRÉSTIMO ----------------------- */

    private UIElement BuildStatisticsTab(BankData data)
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalLabel("ESTATÍSTICAS GERAIS"));
        var overview = new StackPanel();
        overview.Children.Add(ModalValueRow("Viagens concluídas", data.StatsTrips.ToString("N0"), "Green"));
        overview.Children.Add(ModalValueRow("Distância total", $"{data.StatsDistanceKm:N1} km"));
        overview.Children.Add(ModalValueRow("Receita bruta", Money(data.StatsRevenue), "Green"));
        overview.Children.Add(ModalValueRow("Despesas", Money(data.StatsExpenses), "Yellow"));
        overview.Children.Add(ModalValueRow("Lucro operacional", Money(data.StatsProfit), data.StatsProfit >= 0 ? "Green" : "Yellow"));
        panel.Children.Add(ModalPanel(overview));

        var efficiency = new StackPanel();
        efficiency.Children.Add(ModalLabel("EFICIÊNCIA"));
        efficiency.Children.Add(ModalValueRow("Consumo médio", data.StatsAverageKmPerLiter > 0 ? $"{data.StatsAverageKmPerLiter:0.00} km/L" : "—"));
        efficiency.Children.Add(ModalValueRow("Receita por km", data.StatsRevenuePerKm > 0 ? $"{Money(data.StatsRevenuePerKm)}/km" : "—"));
        efficiency.Children.Add(ModalValueRow("Custo por km", data.StatsCostPerKm > 0 ? $"{Money(data.StatsCostPerKm)}/km" : "—"));
        efficiency.Children.Add(ModalValueRow("Lucro por km", data.StatsProfitPerKm != 0 ? $"{Money(data.StatsProfitPerKm)}/km" : "—", data.StatsProfitPerKm >= 0 ? "Green" : "Yellow"));
        efficiency.Children.Add(ModalValueRow("Combustível utilizado", $"{data.StatsFuelLiters:N1} L"));
        panel.Children.Add(ModalPanel(efficiency));

        var delivery = new StackPanel();
        delivery.Children.Add(ModalLabel("QUALIDADE DAS ENTREGAS"));
        delivery.Children.Add(ModalValueRow("Sem avaria", data.StatsCleanDeliveries.ToString("N0"), "Green"));
        delivery.Children.Add(ModalValueRow("Com avaria", data.StatsDamagedDeliveries.ToString("N0"), data.StatsDamagedDeliveries > 0 ? "Yellow" : "Green"));
        panel.Children.Add(ModalPanel(delivery));
        return panel;
    }

    private UIElement BuildLoanTab(BankData data)
    {
        var panel=new StackPanel(); panel.Children.Add(ModalLabel("CRÉDITO EMPRESARIAL TRANSPOLI"));
        if(data.HasCompanyLoan)
        {
            var remaining=Math.Max(0,data.CompanyLoanTotal-data.CompanyLoanPaid);
            var status=data.CompanyLoanStatus switch{"pending"=>"AGUARDANDO DIRETORIA","approved"=>"APROVADO","active"=>"ATIVO","paid"=>"QUITADO","rejected"=>"REJEITADO",_=>data.CompanyLoanStatus.ToUpperInvariant()};
            var box=new StackPanel();
            box.Children.Add(ModalValueRow("Status",status,status=="ATIVO"?"Green":status=="REJEITADO"?"Yellow":"Text"));
            box.Children.Add(ModalValueRow("Valor solicitado",Money(data.CompanyLoanPrincipal)));
            box.Children.Add(ModalValueRow("Juros do contrato",$"{data.CompanyLoanInterest:0.##}%"));
            box.Children.Add(ModalValueRow("Total devido",Money(data.CompanyLoanTotal),"Yellow"));
            box.Children.Add(ModalValueRow("Total pago",Money(data.CompanyLoanPaid),"Green"));
            box.Children.Add(ModalValueRow("Saldo devedor",Money(remaining),remaining>0?"Yellow":"Green"));
            box.Children.Add(ModalValueRow("Desconto por viagem",$"{data.CompanyLoanPct:0.##}% do resultado elegível"));
            panel.Children.Add(ModalPanel(box));
            panel.Children.Add(ModalLine(status=="AGUARDANDO DIRETORIA"?"Sua solicitação foi enviada. A Diretoria precisa aprovar antes de qualquer crédito entrar na conta.":status=="ATIVO"?"O pagamento é automático no fechamento imutável das viagens e retorna ao Banco da Empresa.":status=="QUITADO"?"Contrato encerrado. Nenhum desconto adicional será aplicado.":status=="REJEITADO"?"A solicitação não foi aprovada pela Diretoria.":"Contrato empresarial registrado.",12));
            return panel;
        }
        panel.Children.Add(ModalPanel(new TextBlock{Text="O crédito é financiado pelo Banco da Empresa. A solicitação vai para a Diretoria e só após aprovação o valor é debitado da empresa e creditado na sua conta.",FontSize=12,Foreground=FindResource("Muted") as Brush,TextWrapping=TextWrapping.Wrap}));
        foreach(var amount in new[]{5000m,10000m,20000m,50000m})
        {
            var value=amount;var button=ModalButton($"SOLICITAR {Money(value)} À DIRETORIA");
            button.Click+=async(_,e)=>{e.Handled=true;await RequestCompanyLoanAsync(value);};panel.Children.Add(button);
        }
        panel.Children.Add(ModalLine("Juros e percentual de desconto são definidos pela política financeira vigente da empresa. O ETS2 não fornece nem altera este dinheiro.",11));
        return panel;
    }

    private async Task RequestCompanyLoanAsync(decimal amount)
    {
        var token=SecureTokenStore.Read();
        if(string.IsNullOrWhiteSpace(token)){MessageBox.Show("Conecte sua sessão TransPoli para solicitar crédito empresarial.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        try
        {
            using var response=await SendBankRequestAsync(HttpMethod.Post,"/me/company-loans",token!,new{principal=amount});
            var json=await response.Content.ReadAsStringAsync();
            if(!response.IsSuccessStatusCode){MessageBox.Show("Não foi possível enviar a solicitação. "+json,"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);return;}
            StatusText.Text=$"TransPoli • solicitação de {Money(amount)} enviada à Diretoria";
        }catch(Exception ex){StatusText.Text="TransPoli • falha ao solicitar crédito • "+ex.Message;}
        ShowBankModal("emprestimo");
    }

    private void InvalidateBankCache()
    {
        _bankLedgerFilter = "todos";
    }

    private static string Money(decimal value) => value.ToString("C2", Brl);

    private static string EntryLabel(string type) => type switch
    {
        "trip_income" => "PIX RECEBIDO • VIAGEM",
        "fuel_expense" => "PIX ENVIADO • COMBUSTÍVEL",
        "maintenance_expense" => "PIX ENVIADO • MANUTENÇÃO",
        "trip_expenses" => "PIX ENVIADO • DESPESAS DA VIAGEM",
        "loan_credit" => "CRÉDITO • EMPRÉSTIMO",
        "loan_installment" => "PIX ENVIADO • PARCELA DO EMPRÉSTIMO",
        "loan_payment" => "PIX ENVIADO • PARCELA DO EMPRÉSTIMO",
        "loan_settlement" => "PIX ENVIADO • QUITAÇÃO",
        _ => type.ToUpperInvariant()
    };

    private static string EntryIcon(string type) => type switch
    {
        "trip_income" => "↙",
        "fuel_expense" => "↗",
        "maintenance_expense" => "↗",
        "loan_installment" => "↗",
        "loan_payment" => "↗",
        "loan_settlement" => "↗",
        "loan_credit" => "↙",
        _ => "•"
    };

    private static string Narrative(LedgerEntry entry)
    {
        if (entry.Type == "trip_income")
            return string.IsNullOrWhiteSpace(entry.Description)
                ? "Você recebeu um Pix referente a uma viagem."
                : entry.Description;

        if (entry.Type == "fuel_expense")
            return "Você enviou um Pix para abastecimento • " + entry.Description.Replace("Abastecimento • ", "");

        if (entry.Type == "maintenance_expense")
            return "Você enviou um Pix para manutenção • " + entry.Description.Replace("Manutenção • ", "");

        if (entry.Type == "loan_installment" || entry.Type == "loan_payment")
            return "Você enviou um Pix para pagamento da parcela • " + entry.Description;

        if (entry.Type == "loan_settlement")
            return "Você enviou um Pix para quitar o empréstimo • " + entry.Description;

        if (entry.Type == "loan_credit")
            return "Você recebeu o crédito do empréstimo • " + entry.Description;

        return entry.Description; 
    }

    /* ---------------------------- MODELOS ---------------------------- */

    private sealed class BankData
    {
        public decimal Balance { get; set; }
        public decimal TotalCredits { get; set; }
        public decimal TotalDebits { get; set; }
        public int TripCount { get; set; }
        public string SyncStatus { get; set; } = "BANCO LOCAL";
        public int PendingSyncCount { get; set; }

        public bool HasCompanyLoan { get; set; }
        public string CompanyLoanStatus { get; set; } = "";
        public decimal CompanyLoanPrincipal { get; set; }
        public decimal CompanyLoanTotal { get; set; }
        public decimal CompanyLoanPaid { get; set; }
        public decimal CompanyLoanInterest { get; set; }
        public decimal CompanyLoanPct { get; set; }

        public bool HasLoan { get; set; }
        public decimal LoanPrincipal { get; set; }
        public decimal LoanRemaining { get; set; }
        public decimal LoanPct { get; set; }
        public int LoanInstallmentsTotal { get; set; } = 10;
        public int LoanInstallmentsPaid { get; set; }
        public decimal LoanInstallmentMin { get; set; }
        public decimal LoanInterestMonthly { get; set; }
        public decimal LoanTotalPayable { get; set; }

        public decimal FuelPrice { get; set; } = 5.98m;
        public decimal MinimumMargin { get; set; } = 20m;
        public decimal MaintenancePerKm { get; set; } = 0.42m;
        public decimal EfficiencyBonusPct { get; set; } = 5m;
        public decimal EfficiencyTarget { get; set; } = 0.45m;
        public decimal CleanBonusPct { get; set; } = 5m;
        public decimal DamagePenaltyPct { get; set; } = 15m;
        public decimal WeightSurcharge { get; set; } = 0.015m;
        public decimal FreeWeightTons { get; set; } = 20m;

        public string ActiveTripId { get; set; } = "";
        public string ActiveCargo { get; set; } = "Carga";
        public bool HasPreview { get; set; }
        public decimal PreviewDistance { get; set; }
        public decimal PreviewFuelLiters { get; set; }
        public decimal PreviewRate { get; set; }
        public decimal PreviewKmRevenue { get; set; }
        public decimal PreviewWeightSurcharge { get; set; }
        public decimal PreviewFuelCost { get; set; }
        public decimal PreviewMaintenance { get; set; }
        public decimal PreviewEfficiencyBonus { get; set; }
        public decimal PreviewCleanBonus { get; set; }
        public decimal PreviewDamagePenalty { get; set; }
        public decimal PreviewGross { get; set; }
        public decimal PreviewNet { get; set; }
        public decimal PreviewRevenueFloor { get; set; }
        public decimal PreviewConsumption { get; set; }
        public bool PreviewMarginApplied { get; set; }
        public bool PreviewCleanDelivery { get; set; } = true;

        public int StatsTrips { get; set; }
        public decimal StatsDistanceKm { get; set; }
        public decimal StatsRevenue { get; set; }
        public decimal StatsExpenses { get; set; }
        public decimal StatsProfit { get; set; }
        public decimal StatsFuelLiters { get; set; }
        public decimal StatsAverageKmPerLiter { get; set; }
        public decimal StatsRevenuePerKm { get; set; }
        public decimal StatsCostPerKm { get; set; }
        public decimal StatsProfitPerKm { get; set; }
        public int StatsCleanDeliveries { get; set; }
        public int StatsDamagedDeliveries { get; set; }

        public List<LedgerEntry> Ledger { get; } = new();
        public List<CashbookDay> CashbookDays { get; } = new();
        public List<CargoRate> Rates { get; } = new();
        public List<TripFinancialEntry> TripHistory { get; } = new();
    }

    private sealed class LedgerEntry
    {
        public string Type { get; set; } = "";
        public string? TripId { get; set; }
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class TripFinancialEntry
    {
        public string Id { get; set; } = "";
        public string Cargo { get; set; } = "Carga";
        public string Origin { get; set; } = "";
        public string Destination { get; set; } = "";
        public double DistanceKm { get; set; }
        public decimal RatePerKm { get; set; }
        public decimal Gross { get; set; }
        public decimal Fuel { get; set; }
        public decimal Maintenance { get; set; }
        public decimal LoanInstallment { get; set; }
        public decimal Expenses { get; set; }
        public decimal Net { get; set; }
        public DateTime FinishedAtUtc { get; set; }
    }

    private sealed class CashbookDay
    {
        public DateTime Day { get; set; }
        public decimal Credits { get; set; }
        public decimal Debits { get; set; }
        public decimal Result { get; set; }
        public int Movements { get; set; }
    }

    private sealed class CargoRate
    {
        public string Name { get; set; } = "";
        public decimal RatePerKm { get; set; }
    }
}
