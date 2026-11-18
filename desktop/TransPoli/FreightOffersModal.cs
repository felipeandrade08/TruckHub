using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TransPoli.GameSave;

namespace TransPoli;

/// <summary>
/// Cargas disponíveis: lê as ofertas que o próprio ETS2 mantém no save (job_offer_data) e mostra
/// ordenadas por ganho estimado pela tarifa TruckHub. Somente leitura: aceitar a carga continua
/// sendo feito no jogo, no mercado de fretes.
/// </summary>
public partial class MainWindow
{
    private string _freightSort = "net";      // income | perkm | short | urgent
    private string _freightFilter = "";
    private bool _freightOnlyUnlocked;
    private static readonly CultureInfo FreightCulture = new("pt-BR");

    internal async void ShowFreightOffersModal()
    {
        if (EnsureModalHost() == null) return;
        ShowModalContent("freight-offers", BuildModalLoading("LENDO CARGAS DO SAVE..."));
        UIElement body;
        try { body = await BuildFreightOffersAsync(); }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("Freight.Offers", ex);
            body = ModalStatePanel("CARGAS DISPONÍVEIS", "Não foi possível ler o save",
                "O ETS2 precisa salvar em formato legível. No console do jogo (tecla ~) use: g_save_format 2, salve o jogo e tente de novo.", "Yellow");
        }
        ShowModalContent("freight-offers", BuildModalCard("CARGAS DISPONÍVEIS", body, "Ofertas lidas do save do ETS2 • tarifa TruckHub estimada"));
    }

    private async System.Threading.Tasks.Task<UIElement> BuildFreightOffersAsync()
    {
        var panel = new StackPanel();
        var save = await _gameSaveIntegration.RefreshAsync();
        var offers = save?.JobOffers ?? Array.Empty<SaveJobOffer>();

        panel.Children.Add(ModalHero("LOGÍSTICA", "Mercado de fretes do seu save",
            "Ofertas reais das empresas do ETS2, ordenadas pelo ganho estimado com a tarifa por km da TruckHub.",
            offers.Count == 0 ? "SEM OFERTAS" : $"{offers.Count:N0} OFERTAS", "GoldBright"));

        if (offers.Count == 0)
        {
            panel.Children.Add(ModalStatePanel("NENHUMA OFERTA ENCONTRADA",
                save is null ? "Save não encontrado ou ilegível" : "O save não trouxe ofertas",
                "Salve o jogo no ETS2 e tente de novo. Se o save estiver criptografado, use g_save_format 2 no console do jogo e salve novamente.", "Yellow"));
            return panel;
        }

        // Tarifa estimada por tipo de carga (cache: um acesso ao banco por carga).
        var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double Rate(string cargo)
        {
            if (rates.TryGetValue(cargo, out var r)) return r;
            try { r = LocalData.Current is { } store ? new LocalTripRepository(store.Db).ResolveRatePerKm(cargo) : 0; }
            catch { r = 0; }
            return rates[cargo] = r;
        }

        // Barra de ordenação + busca
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (id, label) in new[] { ("net", "MELHOR LUCRO"), ("income", "MAIOR FRETE"), ("perkm", "MELHOR R$/KM"), ("short", "MAIS CURTAS"), ("urgent", "URGENTES") })
        {
            var on = _freightSort == id;
            var chip = new Button
            {
                Content = label, Tag = ModalActionTag,
                Style = FindResource(on ? "TpPrimaryButton" : "TpSecondaryButton") as Style,
                Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), FontSize = 11
            };
            chip.Click += (_, e) => { e.Handled = true; _freightSort = id; ShowFreightOffersModal(); };
            bar.Children.Add(chip);
        }
        var lockChip = new Button
        {
            Content = _freightOnlyUnlocked ? "SÓ LIBERADAS ✓" : "SÓ LIBERADAS", Tag = ModalActionTag,
            Style = FindResource(_freightOnlyUnlocked ? "TpPrimaryButton" : "TpSecondaryButton") as Style,
            Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), FontSize = 11,
            ToolTip = "Mostra só as cargas compatíveis com o seu nível de motorista"
        };
        lockChip.Click += (_, e) => { e.Handled = true; _freightOnlyUnlocked = !_freightOnlyUnlocked; ShowFreightOffersModal(); };
        bar.Children.Add(lockChip);
        var search = new TextBox
        {
            Text = _freightFilter, MinWidth = 220, Height = 34, Padding = new Thickness(10, 6, 10, 6), FontSize = 12,
            Background = FindResource("TpSurfaceSoft") as Brush, Foreground = FindResource("Text") as Brush,
            BorderBrush = FindResource("TpStrokeStrong") as Brush, ToolTip = "Filtrar por cidade, empresa ou carga • Enter para aplicar"
        };
        search.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true; _freightFilter = search.Text.Trim(); ShowFreightOffersModal();
        };
        bar.Children.Add(search);
        panel.Children.Add(bar);

        var driverLevel = 1;
        try
        {
            var bank = LoadBankDataLocal();
            driverLevel = DriverProgression.From(DriverProgression.ComputeXp(bank.TripCount, bank.StatsDistanceKm, bank.TripHistory.Sum(t => t.Gross), WeighStation.ViolationCount)).Level;
        }
        catch { }
        var q = _freightFilter;
        var rows = offers
            .Where(o => q.Length == 0
                || o.OriginCity.Contains(q, StringComparison.OrdinalIgnoreCase) || o.TargetCity.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.OriginCompany.Contains(q, StringComparison.OrdinalIgnoreCase) || o.TargetCompany.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.Cargo.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(o => !_freightOnlyUnlocked || FreightEstimator.RequiredLevel(o.DistanceKm, o.Urgency) <= driverLevel)
            .Select(o => (Offer: o, Rate: Rate(o.Cargo), Est: FreightEstimator.Estimate(o.DistanceKm, Rate(o.Cargo))))
            .ToList();

        rows = _freightSort switch
        {
            "perkm" => rows.OrderByDescending(x => x.Rate).ThenByDescending(x => x.Est.Net).ToList(),
            "income" => rows.OrderByDescending(x => x.Est.Gross).ToList(),
            "short" => rows.OrderBy(x => x.Offer.DistanceKm).ToList(),
            "urgent" => rows.OrderByDescending(x => x.Offer.Urgency).ThenByDescending(x => x.Est.Net).ToList(),
            _ => rows.OrderByDescending(x => x.Est.Net).ToList()
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"{rows.Count:N0} oferta(s)" + (q.Length > 0 ? $" para \"{q}\"" : "") + " • mostrando as 40 primeiras",
            FontSize = 11, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 0, 0, 8)
        });

        foreach (var (o, rate, est) in rows.Take(40))
        {
            var card = new Border
            {
                Background = FindResource("TpSurfaceSoft") as Brush, BorderBrush = FindResource("TpStroke") as Brush,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 8)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = o.Cargo.ToUpperInvariant(), FontSize = 15, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
            left.Children.Add(new TextBlock
            {
                Text = $"{o.OriginCity} → {o.TargetCity}", FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("GoldBright") as Brush, Margin = new Thickness(0, 3, 0, 0)
            });
            var meta = $"{o.DistanceKm:N0} km";
            if (o.OriginCompany.Length > 0) meta += $"  •  {o.OriginCompany} → {o.TargetCompany}";
            if (o.TrailerVariant.Length > 0) meta += $"  •  {o.TrailerVariant}";
            if (o.Urgency > 0) meta += o.Urgency >= 2 ? "  •  MUITO URGENTE" : "  •  URGENTE";
            if (o.ExpiresInGameMinutes > 0) meta += $"  •  expira em {o.ExpiresInGameMinutes / 60:0}h de jogo";
            if (o.FerryTime > 0) meta += "  •  inclui balsa/trem";
            var need = FreightEstimator.RequiredLevel(o.DistanceKm, o.Urgency);
            var locked = need > driverLevel;
            if (need > 1) meta += locked ? $"  •  🔒 exige nível {need} (você: {driverLevel})" : $"  •  liberada (nível {need}+)";
            if (rate > 0) meta += $"  •  custo est. {est.OperatingCost.ToString("C", FreightCulture)}  •  margem {est.MarginPct:0.#}%" + (est.FloorApplied ? "  •  piso de receita" : "");
            left.Children.Add(new TextBlock { Text = meta, FontSize = 10, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(left);

            var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock
            {
                Text = rate > 0 ? est.Net.ToString("C", FreightCulture) : "—", FontSize = 18, FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Bahnschrift, Segoe UI"), Foreground = FindResource("Green") as Brush, HorizontalAlignment = HorizontalAlignment.Right
            });
            right.Children.Add(new TextBlock
            {
                Text = rate > 0 ? $"lucro est. • frete {est.Gross.ToString("C", FreightCulture)}" : "tarifa indisponível", FontSize = 10,
                Foreground = FindResource("Muted") as Brush, HorizontalAlignment = HorizontalAlignment.Right
            });
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            card.Child = grid;
            panel.Children.Add(card);
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Lucro estimado = frete (distância × tarifa do tipo de carga, com piso de receita e bônus de entrega sem dano) menos combustível e manutenção padrão da TruckHub. Dano de carga acima de 1% reduz o frete em 15% × a fração de dano; o fechamento oficial pode diferir. Níveis exigidos: >500 km nível 3, >800 km nível 5, >1.200 km nível 10, urgente nível 3, muito urgente nível 6 (recomendação: o jogo não impede aceitar). Para pegar a carga, aceite-a no mercado de fretes do ETS2.",
            FontSize = 10, Foreground = FindResource("Muted") as Brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0)
        });
        return panel;
    }
}
