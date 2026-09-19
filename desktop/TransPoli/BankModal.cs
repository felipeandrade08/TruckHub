using System;
using System.Collections.Generic;
using System.Globalization;
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

    // Cache curto para troca de abas instantânea e para evitar repetir as
    // mesmas consultas quando o motorista navega dentro do Banco.
    private static readonly TimeSpan BankCacheLifetime = TimeSpan.FromSeconds(12);
    private BankData? _bankCache;
    private string? _bankCacheToken;
    private DateTime _bankCacheAtUtc;

    // Nome antigo mantido: o botão "💰 ECONOMIA" do tablet chama por aqui.
    internal void ShowEconomyModal() => ShowBankModal();

    internal async void ShowBankModal(string? tab = null)
    {
        if (!string.IsNullOrWhiteSpace(tab)) _bankTab = tab!;
        if (EnsureModalHost() == null) return;

        ShowModalContent("bank", BuildModalLoading("💰 CARREGANDO BANCO DO MOTORISTA..."));

        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            ShowModalContent("bank", BuildModalCard("💰 BANCO DO MOTORISTA",
                ModalLine("Sessão do motorista não encontrada. Ative o computador de bordo novamente para acessar o banco.", 14)));
            return;
        }

        BankData data;
        try
        {
            var now = DateTime.UtcNow;
            if (_bankCache != null &&
                string.Equals(_bankCacheToken, token, StringComparison.Ordinal) &&
                now - _bankCacheAtUtc < BankCacheLifetime)
            {
                data = _bankCache;
            }
            else
            {
                data = await LoadBankDataAsync(token!);
                _bankCache = data;
                _bankCacheToken = token;
                _bankCacheAtUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            ShowModalContent("bank", BuildModalCard("💰 BANCO DO MOTORISTA",
                ModalLine($"Não foi possível falar com o banco agora.\n\n{ex.Message}", 13)));
            return;
        }

        ShowModalContent("bank", BuildModalCard(
            "💰 BANCO DO MOTORISTA",
            BuildBankBody(data),
            $"Saldo atualizado • {DateTime.Now:dd/MM/yyyy HH:mm}"));
    }

    /* ----------------------------- DADOS ----------------------------- */

    private async Task<BankData> LoadBankDataAsync(string token)
    {
        var data = new BankData();

        // As quatro fontes independentes + a lista de viagens não precisam
        // esperar umas pelas outras. Antes eram cinco requisições em cadeia.
        // Agora elas começam juntas e só a prévia da viagem fica dependente
        // do ID da viagem ativa.
        var economyTask = SendBankRequestAsync(HttpMethod.Get, "/me/economy", token);
        var cashbookTask = SendBankRequestAsync(HttpMethod.Get, "/me/economy/cashbook?days=30", token);
        var ratesTask = SendBankRequestAsync(HttpMethod.Get, "/me/economy/rates", token);
        var tripsTask = SendBankRequestAsync(HttpMethod.Get, "/me/trips", token);
        var statisticsTask = SendBankRequestAsync(HttpMethod.Get, "/me/statistics?period=all", token);

        await Task.WhenAll(economyTask, cashbookTask, ratesTask, tripsTask, statisticsTask);

        using (var response = await economyTask)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"O banco respondeu {(int)response.StatusCode}.");

            var root = J.Parse(await response.Content.ReadAsStringAsync());
            data.Balance = J.Dec(J.Prop(root, "account"), "balanceBrl");
            data.TotalCredits = J.Dec(J.Prop(root, "totals"), "creditsBrl");
            data.TotalDebits = J.Dec(J.Prop(root, "totals"), "debitsBrl");
            data.TripCount = J.Int(J.Prop(root, "totals"), "trips");

            var loan = J.Prop(root, "loan");
            if (loan is JsonElement l && l.ValueKind == JsonValueKind.Object)
            {
                data.HasLoan = true;
                data.LoanPrincipal = J.Dec(loan, "principal_brl");
                data.LoanRemaining = J.Dec(loan, "remaining_brl");
                data.LoanPct = J.Dec(loan, "repayment_pct");
                data.LoanInstallmentsTotal = J.Int(loan, "installments_total", 10);
                data.LoanInstallmentsPaid = J.Int(loan, "installments_paid");
                data.LoanInstallmentMin = J.Dec(loan, "installment_min_brl");
            }

            foreach (var entry in J.Array(root, "ledger"))
            {
                data.Ledger.Add(new LedgerEntry
                {
                    Type = J.Str(entry, "entry_type"),
                    Description = J.Str(entry, "description"),
                    Amount = J.Dec(entry, "amount_brl"),
                    BalanceAfter = J.Dec(entry, "balance_after_brl"),
                    CreatedAt = J.Date(entry, "created_at") ?? DateTime.UtcNow
                });
            }
        }

        using (var response = await cashbookTask)
        {
            if (response.IsSuccessStatusCode)
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                foreach (var day in J.Array(root, "daily"))
                {
                    data.CashbookDays.Add(new CashbookDay
                    {
                        Day = J.Date(day, "day") ?? DateTime.UtcNow,
                        Credits = J.Dec(day, "credits"),
                        Debits = J.Dec(day, "debits"),
                        Result = J.Dec(day, "result"),
                        Movements = J.Int(day, "movements")
                    });
                }
            }
        }

        using (var response = await ratesTask)
        {
            if (response.IsSuccessStatusCode)
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                var s = J.Prop(root, "settings");
                data.FuelPrice = J.Dec(s, "fuelPriceBrl", 5.98m);
                data.MinimumMargin = J.Dec(s, "minimumMarginPct", 20m);
                data.MaintenancePerKm = J.Dec(s, "maintenanceBrlKm", 0.42m);
                data.EfficiencyBonusPct = J.Dec(s, "efficiencyBonusPct", 5m);
                data.EfficiencyTarget = J.Dec(s, "efficiencyTargetLKm", 0.45m);
                data.CleanBonusPct = J.Dec(s, "cleanDeliveryBonusPct", 5m);
                data.DamagePenaltyPct = J.Dec(s, "damagePenaltyPct", 15m);
                data.WeightSurcharge = J.Dec(s, "weightSurchargeBrlTonKm", 0.015m);
                data.FreeWeightTons = J.Dec(s, "freeWeightTons", 20m);

                foreach (var rate in J.Array(root, "rates"))
                {
                    data.Rates.Add(new CargoRate
                    {
                        Name = J.Str(rate, "display_name", "Carga"),
                        RatePerKm = J.Dec(rate, "rate_brl_km")
                    });
                }
            }
        }

        using (var response = await tripsTask)
        {
            if (response.IsSuccessStatusCode)
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                foreach (var trip in J.Array(root, "trips"))
                {
                    if (!string.Equals(J.Str(trip, "status"), "active", StringComparison.OrdinalIgnoreCase)) continue;
                    data.ActiveTripId = J.Str(trip, "id");
                    data.ActiveCargo = J.Str(trip, "cargo", "Carga");
                    break;
                }
            }
        }

        using (var response = await statisticsTask)
        {
            if (response.IsSuccessStatusCode)
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                var st = J.Prop(root, "statistics");
                data.StatsTrips = J.Int(st, "trips");
                data.StatsDistanceKm = J.Dec(st, "distanceKm");
                data.StatsRevenue = J.Dec(st, "revenueBrl");
                data.StatsExpenses = J.Dec(st, "expensesBrl");
                data.StatsProfit = J.Dec(st, "profitBrl");
                data.StatsFuelLiters = J.Dec(st, "fuelLiters");
                data.StatsAverageKmPerLiter = J.Dec(st, "averageKmPerLiter");
                data.StatsRevenuePerKm = J.Dec(st, "revenuePerKm");
                data.StatsCostPerKm = J.Dec(st, "costPerKm");
                data.StatsProfitPerKm = J.Dec(st, "profitPerKm");
                data.StatsCleanDeliveries = J.Int(st, "cleanDeliveries");
                data.StatsDamagedDeliveries = J.Int(st, "damagedDeliveries");
            }
        }

        // Esta é a única chamada que depende de um dado obtido acima.
        if (!string.IsNullOrWhiteSpace(data.ActiveTripId))
        {
            using var response = await SendBankRequestAsync(
                HttpMethod.Get, $"/me/trips/{data.ActiveTripId}/economy-preview", token);

            if (response.IsSuccessStatusCode)
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                var p = J.Prop(root, "preview");
                data.HasPreview = true;
                data.PreviewDistance = J.Dec(p, "distanceKm");
                data.PreviewFuelLiters = J.Dec(p, "fuelLiters");
                data.PreviewRate = J.Dec(p, "rateBrlKm");
                data.PreviewKmRevenue = J.Dec(p, "kmRevenue");
                data.PreviewWeightSurcharge = J.Dec(p, "weightSurcharge");
                data.PreviewFuelCost = J.Dec(p, "fuelCost");
                data.PreviewMaintenance = J.Dec(p, "maintenanceCost");
                data.PreviewEfficiencyBonus = J.Dec(p, "efficiencyBonus");
                data.PreviewCleanBonus = J.Dec(p, "cleanDeliveryBonus");
                data.PreviewDamagePenalty = J.Dec(p, "damagePenalty");
                data.PreviewGross = J.Dec(p, "grossRevenue");
                data.PreviewNet = J.Dec(p, "netBeforeLoan");
                data.PreviewMarginApplied = J.Bool(p, "marginApplied");
                data.PreviewRevenueFloor = J.Dec(p, "revenueFloor");
                data.PreviewConsumption = J.Dec(p, "consumptionLKm");
                data.PreviewCleanDelivery = J.Bool(p, "cleanDelivery", true);
            }
        }

        return data;
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

    /* ------------------------------ UI ------------------------------- */

    private UIElement BuildBankBodyLegacy(BankData data)
    {
        var panel = new StackPanel();

        // Saldo em destaque
        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "SALDO DISPONÍVEL",
            FontSize = 10,
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
            FontSize = 11,
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
                FontSize = 10,
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
        panel.Children.Add(ModalLabel("MOVIMENTAÇÕES RECENTES"));

        if (data.Ledger.Count == 0)
        {
            panel.Children.Add(ModalLine(
                "Nenhuma movimentação ainda. Finalize uma viagem para receber o primeiro frete.", 13));
            return panel;
        }

        var box = new StackPanel();
        foreach (var entry in data.Ledger.GetRange(0, Math.Min(25, data.Ledger.Count)))
        {
            var positive = entry.Amount >= 0;
            box.Children.Add(ModalValueRow(
                $"{entry.CreatedAt.ToLocalTime():dd/MM HH:mm} • {EntryLabel(entry.Type)}\n{entry.Description}",
                (positive ? "+" : "") + Money(entry.Amount),
                positive ? "Green" : "Yellow"));
        }
        panel.Children.Add(ModalPanel(box));
        return panel;
    }

    /* ------------------------- ABA LIVRO-CAIXA ----------------------- */

    private UIElement BuildCashbookTab(BankData data)
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalLabel("📒 LIVRO-CAIXA • ÚLTIMOS 30 DIAS"));

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

        if (!data.HasPreview)
        {
            panel.Children.Add(ModalLabel("VIAGEM ATUAL"));
            panel.Children.Add(ModalLine(
                "Nenhuma viagem ativa. Pegue uma carga no ETS2 e o TransPoli abre a viagem sozinho.", 13));
            return panel;
        }

        panel.Children.Add(ModalLabel($"SIMULAÇÃO • {data.ActiveCargo}"));

        var revenue = new StackPanel();
        revenue.Children.Add(new TextBlock
        {
            Text = "COMO O FRETE ESTÁ SENDO CALCULADO",
            FontSize = 10,
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
            FontSize = 10,
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

        return panel;
    }

    /* --------------------------- ABA TARIFAS ------------------------- */

    private UIElement BuildRatesTab(BankData data)
    {
        var panel = new StackPanel();

        panel.Children.Add(ModalLabel("📦 TARIFAS POR TIPO DE CARGA"));
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
        panel.Children.Add(ModalLabel("📊 ESTATÍSTICAS GERAIS"));
        var overview = new StackPanel();
        overview.Children.Add(ModalValueRow("Viagens concluídas", data.StatsTrips.ToString("N0"), "Green"));
        overview.Children.Add(ModalValueRow("Distância total", $"{data.StatsDistanceKm:N1} km"));
        overview.Children.Add(ModalValueRow("Receita bruta", Money(data.StatsRevenue), "Green"));
        overview.Children.Add(ModalValueRow("Despesas", Money(data.StatsExpenses), "Yellow"));
        overview.Children.Add(ModalValueRow("Lucro operacional", Money(data.StatsProfit), data.StatsProfit >= 0 ? "Green" : "Yellow"));
        panel.Children.Add(ModalPanel(overview));

        var efficiency = new StackPanel();
        efficiency.Children.Add(ModalLabel("⚙️ EFICIÊNCIA"));
        efficiency.Children.Add(ModalValueRow("Consumo médio", data.StatsAverageKmPerLiter > 0 ? $"{data.StatsAverageKmPerLiter:0.00} km/L" : "—"));
        efficiency.Children.Add(ModalValueRow("Receita por km", data.StatsRevenuePerKm > 0 ? $"{Money(data.StatsRevenuePerKm)}/km" : "—"));
        efficiency.Children.Add(ModalValueRow("Custo por km", data.StatsCostPerKm > 0 ? $"{Money(data.StatsCostPerKm)}/km" : "—"));
        efficiency.Children.Add(ModalValueRow("Lucro por km", data.StatsProfitPerKm != 0 ? $"{Money(data.StatsProfitPerKm)}/km" : "—", data.StatsProfitPerKm >= 0 ? "Green" : "Yellow"));
        efficiency.Children.Add(ModalValueRow("Combustível utilizado", $"{data.StatsFuelLiters:N1} L"));
        panel.Children.Add(ModalPanel(efficiency));

        var delivery = new StackPanel();
        delivery.Children.Add(ModalLabel("📦 QUALIDADE DAS ENTREGAS"));
        delivery.Children.Add(ModalValueRow("Sem avaria", data.StatsCleanDeliveries.ToString("N0"), "Green"));
        delivery.Children.Add(ModalValueRow("Com avaria", data.StatsDamagedDeliveries.ToString("N0"), data.StatsDamagedDeliveries > 0 ? "Yellow" : "Green"));
        panel.Children.Add(ModalPanel(delivery));
        return panel;
    }

    private UIElement BuildLoanTab(BankData data)
    {
        var panel = new StackPanel();
        panel.Children.Add(ModalLabel("💳 CRÉDITO DO MOTORISTA"));

        if (data.HasLoan)
        {
            var paidPct = data.LoanPrincipal > 0
                ? (data.LoanPrincipal - data.LoanRemaining) / data.LoanPrincipal * 100m
                : 0m;

            var box = new StackPanel();
            box.Children.Add(ModalValueRow("Valor contratado", Money(data.LoanPrincipal)));
            box.Children.Add(ModalValueRow("Saldo devedor", Money(data.LoanRemaining), "Yellow"));
            box.Children.Add(ModalValueRow("Já quitado", $"{paidPct:0.#}%", "Green"));
            box.Children.Add(ModalValueRow("Parcelas",
                $"{data.LoanInstallmentsPaid} de {data.LoanInstallmentsTotal}"));
            box.Children.Add(ModalValueRow("Parcela mínima por viagem", Money(data.LoanInstallmentMin)));
            box.Children.Add(ModalValueRow("Desconto automático",
                $"{data.LoanPct:0.##}% da receita líquida"));
            panel.Children.Add(ModalPanel(box));

            panel.Children.Add(ModalLine(
                "📉 A cada viagem finalizada, a parcela é descontada sozinha da receita líquida antes de entrar no saldo.", 12));

            var settle = ModalButton($"✓ QUITAR AGORA ({Money(data.LoanRemaining)})");
            settle.IsEnabled = data.Balance >= data.LoanRemaining;
            settle.Opacity = settle.IsEnabled ? 1.0 : 0.45;
            settle.Click += async (_, e) => { e.Handled = true; await SettleLoanAsync(); };
            panel.Children.Add(settle);

            if (!settle.IsEnabled)
                panel.Children.Add(ModalLine(
                    $"Saldo insuficiente para quitação antecipada. Faltam {Money(data.LoanRemaining - data.Balance)}.", 11));
        }
        else
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Empréstimo inicial para começar a operar. O valor entra no saldo na hora e é descontado automaticamente das próximas viagens, sem juros.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));

            var loan5 = ModalButton("💳 SOLICITAR R$ 5.000  •  10 parcelas");
            loan5.Click += async (_, e) => { e.Handled = true; await RequestLoanAsync(5000, 10); };
            panel.Children.Add(loan5);

            var loan10 = ModalButton("💳 SOLICITAR R$ 10.000  •  10 parcelas");
            loan10.Click += async (_, e) => { e.Handled = true; await RequestLoanAsync(10000, 10); };
            panel.Children.Add(loan10);

            panel.Children.Add(ModalLine(
                "O desconto é de 20% da receita líquida de cada viagem, respeitando a parcela mínima.", 11));
        }

        return panel;
    }

    private void InvalidateBankCache()
    {
        _bankCache = null;
        _bankCacheToken = null;
        _bankCacheAtUtc = default;
    }

    private async Task RequestLoanAsync(decimal amount, int installments)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            using var response = await SendBankRequestAsync(HttpMethod.Post, "/me/economy/loan", token!,
                new { principalBrl = amount, repaymentPct = 20, installments });

            if (response.IsSuccessStatusCode)
            {
                StatusText.Text = $"TransPoli • empréstimo de {Money(amount)} liberado";
            }
            else
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                StatusText.Text = "TransPoli • " + J.Str(root, "error", "não foi possível liberar o empréstimo");
            }
        }
        catch
        {
            StatusText.Text = "TransPoli • falha de comunicação com o banco";
        }
        InvalidateBankCache();
        ShowBankModal("emprestimo");
    }

    private async Task SettleLoanAsync()
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            using var response = await SendBankRequestAsync(HttpMethod.Post, "/me/economy/loan/settle", token!, new { });
            if (response.IsSuccessStatusCode) StatusText.Text = "TransPoli • empréstimo quitado";
            else
            {
                var root = J.Parse(await response.Content.ReadAsStringAsync());
                StatusText.Text = "TransPoli • " + J.Str(root, "error", "não foi possível quitar");
            }
        }
        catch
        {
            StatusText.Text = "TransPoli • falha de comunicação com o banco";
        }
        InvalidateBankCache();
        ShowBankModal("emprestimo");
    }

    private static string Money(decimal value) => value.ToString("C2", Brl);

    private static string EntryLabel(string type) => type switch
    {
        "trip_income" => "FRETE RECEBIDO",
        "trip_expenses" => "CUSTOS DA VIAGEM",
        "loan_credit" => "EMPRÉSTIMO LIBERADO",
        "loan_payment" => "PARCELA DO EMPRÉSTIMO",
        "loan_settlement" => "QUITAÇÃO",
        _ => type.ToUpperInvariant()
    };

    /* ---------------------------- MODELOS ---------------------------- */

    private sealed class BankData
    {
        public decimal Balance { get; set; }
        public decimal TotalCredits { get; set; }
        public decimal TotalDebits { get; set; }
        public int TripCount { get; set; }

        public bool HasLoan { get; set; }
        public decimal LoanPrincipal { get; set; }
        public decimal LoanRemaining { get; set; }
        public decimal LoanPct { get; set; }
        public int LoanInstallmentsTotal { get; set; } = 10;
        public int LoanInstallmentsPaid { get; set; }
        public decimal LoanInstallmentMin { get; set; }

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
    }

    private sealed class LedgerEntry
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime CreatedAt { get; set; }
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
