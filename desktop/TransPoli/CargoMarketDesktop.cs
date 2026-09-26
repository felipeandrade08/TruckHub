using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly TimeSpan CargoMarketCacheSafetyLifetime = TimeSpan.FromMinutes(65);
    private string? _cargoMarketCacheJson;
    private DateTime _cargoMarketCacheAtUtc;
    private string? _lastDiscoveredCargo;
    private DispatcherTimer? _cargoMarketCountdownTimer;
    private TextBlock? _cargoMarketCountdownText;
    private DateTime _cargoMarketNextRefreshUtc;

    private async Task DiscoverCargoMarketAsync(string cargo)
    {
        var name = cargo?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (string.Equals(_lastDiscoveredCargo, name, StringComparison.OrdinalIgnoreCase)) return;


        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/cargo-market/discover");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { cargo = name }),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            _lastDiscoveredCargo = name;
            _cargoMarketCacheJson = null;
            _cargoMarketCacheAtUtc = DateTime.MinValue;
        }
        catch (Exception ex)
        {
            // A descoberta do catálogo nunca pode interromper a viagem, mas a falha
            // precisa ficar observável para não mascarar catálogo/tarifa desatualizados.
            App.WriteUiCrashLog("CargoMarket.Discover", ex);
        }
    }

    internal async void ShowCargoMarketModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("cargo-market", BuildModalLoading("CARREGANDO CATÁLOGO..."));
        var panel = await BuildCargoMarketPanelAsync();
        ShowModalContent("cargo-market", BuildModalCard(
            "📦 CENTRAL DE FRETES TRANSPOLI",
            panel,
            "Somente cargas reais detectadas no ETS2 • tarifas TransPoli atualizadas a cada 59 minutos"));
    }

    internal async void ShowTripCenterModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("trip-center", BuildModalLoading("CARREGANDO VIAGENS E CONTRATOS..."));
        _invoiceTelemetry = await LoadCurrentTelemetryAsync();
        var panel = await BuildTripHistoryPanelAsync();
        ShowModalContent("trip-center", BuildModalCard("🚛 VIAGENS E CONTRATOS", panel,
            "Centro de viagem local-first • viagem atual • histórico • contrato • resultado"));
    }

    private Task<UIElement> BuildTripHistoryPanelAsync()
    {
        var panel = new StackPanel();

        // LOCAL-FIRST: o histórico operacional é lido diretamente do SQLite do jogador.
        // A API não é necessária para abrir esta tela e não é usada como fonte de verdade.
        try
        {
            var current = BuildCargoModal();
            panel.Children.Add(current);
        }
        catch (Exception ex) { App.WriteUiCrashLog("Trips.BuildCurrentContract", ex); }

        var live = LastTelemetry;
        string? localActiveTripId = null;
        try { localActiveTripId = GetLocalActiveTripId(); }
        catch (Exception ex) { App.WriteUiCrashLog("Trips.GetLocalActiveTrip", ex); }
        if (_tripActive && live is not null)
        {
            var liveDistance = Math.Max(0f, live.OdometerKm - _tripStartOdometer);
            var liveRate = JourneyEconomyCalculator.SanitizeRate(_localTripRatePerKm);
            var liveGross = liveDistance * liveRate;
            var liveCard = new Border
            {
                Background = FindResource("Panel2") as Brush,
                BorderBrush = FindResource("Green") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 14)
            };
            var liveStack = new StackPanel();
            liveStack.Children.Add(new TextBlock
            {
                Text = "● VIAGEM ATUAL • AO VIVO",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Green") as Brush
            });
            liveStack.Children.Add(new TextBlock
            {
                Text = $"{(!string.IsNullOrWhiteSpace(_tripRouteOrigin) ? _tripRouteOrigin : live.SourceCity) ?? "Origem"} → {(!string.IsNullOrWhiteSpace(_tripRouteDestination) ? _tripRouteDestination : live.DestinationCity) ?? "Destino"}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Text") as Brush,
                Margin = new Thickness(0, 4, 0, 0)
            });
            liveStack.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(_tripCargo) ? _tripCargo.ToUpperInvariant() : string.IsNullOrWhiteSpace(live.Cargo) ? "CARGA NÃO INFORMADA" : live.Cargo.ToUpperInvariant(),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("GoldBright") as Brush,
                Margin = new Thickness(0, 5, 0, 0)
            });
            var liveMetrics = new Grid { Margin = new Thickness(0, 10, 0, 2) };
            for (var i = 0; i < 4; i++) liveMetrics.ColumnDefinitions.Add(new ColumnDefinition());
            AddTripHistoryMetric(liveMetrics, 0, "PERCORRIDO", $"{liveDistance:0.0} km");
            AddTripHistoryMetric(liveMetrics, 1, "TARIFA", $"R$ {liveRate:0.00}/km");
            AddTripHistoryMetric(liveMetrics, 2, "BRUTO EST.", $"R$ {liveGross:0.00}");
            AddTripHistoryMetric(liveMetrics, 3, "VELOCIDADE", $"{Math.Abs(live.SpeedKph):0} km/h");
            liveStack.Children.Add(liveMetrics);
            liveStack.Children.Add(new TextBlock
            {
                Text = $"COMBUSTÍVEL {live.FuelLiters:0.0} L   •   AUTONOMIA {live.FuelRangeKm:0} km",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("Muted") as Brush,
                Margin = new Thickness(0, 6, 0, 0)
            });

            // Fallback manual: se a telemetria não sinalizar a entrega corretamente,
            // o motorista pode encerrar a viagem por aqui e limpar o estado ao vivo.
            var finishButton = new Button
            {
                Content = "✓ FINALIZAR VIAGEM MANUALMENTE",
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = ModalActionTag,
                ToolTip = "Encerra a viagem atual, fecha o contrato e zera a Viagem Atual ao Vivo"
            };
            finishButton.Click += async (_, e) =>
            {
                e.Handled = true;
                if (_tripFinishBusy) return;
                finishButton.IsEnabled = false;
                try
                {
                    StatusText.Text = "TransPoli • finalização manual solicitada...";
                    CloseOperationalModal();
                    await ManualFinishCurrentTripAsync();
                }
                finally
                {
                    finishButton.IsEnabled = true;
                }
            };
            liveStack.Children.Add(finishButton);

            var resetButton = new Button
            {
                Content = "↻ DESCARTAR / RESETAR VIAGEM TRAVADA",
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(8, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = ModalActionTag,
                ToolTip = "Recuperação: limpa a viagem ativa sem pagamento, ranking ou entrega concluída"
            };
            resetButton.Click += (_, e) =>
            {
                e.Handled = true;
                CloseOperationalModal();
                ResetCurrentTripForRecovery();
            };
            liveStack.Children.Add(resetButton);
            liveCard.Child = liveStack;
            panel.Children.Add(liveCard);
        }

        // A sessão da janela pode ter sido perdida após reinício/erro de telemetria,
        // mas a viagem ainda pode estar ativa no SQLite. Nesse caso o botão continua
        // disponível para o motorista encerrar a viagem sem depender de _tripActive.
        if (!_tripActive && !string.IsNullOrWhiteSpace(localActiveTripId))
        {
            var recoveredFinish = new Button
            {
                Content = "✓ FINALIZAR VIAGEM MANUALMENTE",
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = ModalActionTag,
                ToolTip = "Finaliza a viagem ativa salva localmente e limpa a Viagem Atual ao Vivo"
            };
            recoveredFinish.Click += async (_, e) =>
            {
                e.Handled = true;
                if (_tripFinishBusy) return;
                recoveredFinish.IsEnabled = false;
                try
                {
                    StatusText.Text = "TransPoli • finalização manual recuperada solicitada...";
                    CloseOperationalModal();
                    await ManualFinishCurrentTripAsync();
                }
                finally
                {
                    recoveredFinish.IsEnabled = true;
                }
            };
            panel.Children.Add(recoveredFinish);
        }

        panel.Children.Add(ModalSectionTitle("HISTÓRICO LOCAL DE VIAGENS", "OPERAÇÕES CONSOLIDADAS"));
        if (!_tripActive && string.IsNullOrWhiteSpace(localActiveTripId)) panel.Children.Add(ModalStatusStrip("● NENHUMA VIAGEM ATIVA • CENTRAL PRONTA PARA A PRÓXIMA OPERAÇÃO","Green"));

        if (LocalData.Current is not { } store)
        {
            panel.Children.Add(ModalStatePanel(
                "ARMAZENAMENTO LOCAL",
                "Histórico inicializando",
                "A telemetria atual continua funcionando. O banco local de viagens ainda está sendo preparado; reabra esta central em alguns instantes.",
                "Yellow"));
            return Task.FromResult<UIElement>(panel);
        }

        try
        {
            // Se o ETS2 já não possui trabalho ativo e a sessão do TransPoli também
            // não está em viagem, uma viagem local antiga não pode continuar aparecendo
            // como "EM ANDAMENTO". Isso corrige sessões encerradas antes de o fechamento
            // local ser persistido (por exemplo, após fechar/reabrir o aplicativo).
            if (!_tripActive && LastTelemetry is { } currentTelemetry)
            {
                // A Central de Viagens é uma tela de leitura. Ela não pode transformar
                // uma viagem ativa em "finished" apenas porque carga/rota mudaram ou o
                // ETS2 momentaneamente não reportou job. Encerramento pertence ao
                // pipeline durável de trip_closure/recovery.
            }

            using var command = store.Db.Connection.CreateCommand();
            command.CommandText = @"
SELECT
    t.id,t.cargo_name,t.source_city,t.destination_city,t.status,
    t.started_at_utc,t.finished_at_utc,t.distance_km,t.rate_per_km,
    t.income_gross,t.expense_total,t.net_value,t.server_id
FROM trip t
WHERE t.owner_user_id=@owner
ORDER BY t.started_at_utc DESC
LIMIT 50;";
            command.Parameters.AddWithValue("@owner", SecureTokenStore.ReadUserId() ?? "");

            using var reader = command.ExecuteReader();
            var count = 0;

            while (reader.Read())
            {
                count++;

                var cargo = reader.IsDBNull(1) ? "Carga" : reader.GetString(1);
                var origin = reader.IsDBNull(2) ? "Origem não informada" : reader.GetString(2);
                var destination = reader.IsDBNull(3) ? "Destino não informado" : reader.GetString(3);
                var status = reader.IsDBNull(4) ? "unknown" : reader.GetString(4);
                var started = reader.IsDBNull(5) ? null : reader.GetString(5);
                var finished = reader.IsDBNull(6) ? null : reader.GetString(6);
                var distance = reader.IsDBNull(7) ? 0d : Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture);
                var rate = reader.IsDBNull(8) ? 0d : Convert.ToDouble(reader.GetValue(8), CultureInfo.InvariantCulture);
                var gross = reader.IsDBNull(9) ? 0d : Convert.ToDouble(reader.GetValue(9), CultureInfo.InvariantCulture);
                var expenses = reader.IsDBNull(10) ? 0d : Convert.ToDouble(reader.GetValue(10), CultureInfo.InvariantCulture);
                var net = reader.IsDBNull(11) ? 0d : Convert.ToDouble(reader.GetValue(11), CultureInfo.InvariantCulture);

                var statusText = status == "active" ? "EM ANDAMENTO" :
                    status == "finished" ? "CONCLUÍDA" : status.ToUpperInvariant();

                var statusBrush = status == "active"
                    ? FindResource("GoldBright") as Brush
                    : status == "finished"
                        ? FindResource("Green") as Brush
                        : FindResource("Muted") as Brush;

                var card = new Border
                {
                    Background = FindResource("Panel") as Brush,
                    BorderBrush = FindResource("Stroke") as Brush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 11)
                };

                var stack = new StackPanel();
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var title = new StackPanel();
                title.Children.Add(new TextBlock
                {
                    Text = cargo,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Text") as Brush
                });
                title.Children.Add(new TextBlock
                {
                    Text = $"{origin} → {destination}",
                    FontSize = 12,
                    Foreground = FindResource("Muted") as Brush,
                    Margin = new Thickness(0, 3, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(title, 0);
                header.Children.Add(title);

                var badge = new Border
                {
                    BorderBrush = statusBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(8, 4, 8, 4),
                    VerticalAlignment = VerticalAlignment.Top
                };
                badge.Child = new TextBlock
                {
                    Text = statusText,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = statusBrush
                };
                Grid.SetColumn(badge, 1);
                header.Children.Add(badge);
                stack.Children.Add(header);

                var contractText = status == "finished" ? "CONTRATO ENTREGUE" :
                    status == "active" ? "CONTRATO ATIVO" : "CONTRATO LOCAL";
                stack.Children.Add(new TextBlock
                {
                    Text = $"{contractText}  •  {distance:0.0} km  •  R$ {rate:0.00}/km",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("GoldBright") as Brush,
                    Margin = new Thickness(0, 7, 0, 0)
                });

                var financial = new Grid { Margin = new Thickness(0, 8, 0, 0) };
                for (var i = 0; i < 3; i++) financial.ColumnDefinitions.Add(new ColumnDefinition());
                AddTripHistoryMetric(financial, 0, "BRUTO", gross > 0 ? $"R$ {gross:N2}" : "—");
                AddTripHistoryMetric(financial, 1, "DESPESAS", expenses > 0 ? $"R$ {expenses:N2}" : "R$ 0,00");
                AddTripHistoryMetric(financial, 2, "LÍQUIDO", $"R$ {net:N2}");
                stack.Children.Add(financial);

                var when = string.IsNullOrWhiteSpace(finished) ? started : finished;
                if (DateTime.TryParse(when, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = $"Registrada em {dt.ToLocalTime():dd/MM/yyyy HH:mm}",
                        FontSize = 12,
                        Foreground = FindResource("Muted") as Brush,
                        Margin = new Thickness(0, 7, 0, 0)
                    });
                }

                card.Child = stack;
                panel.Children.Add(card);
            }

            if (count == 0)
            {
                panel.Children.Add(ModalStatePanel(
                    "HISTÓRICO LOCAL",
                    "Nenhuma viagem finalizada ainda",
                    "Quando uma operação for concluída, ela aparecerá aqui com rota, carga, distância e resultado. Esta área continua disponível sem conexão com o servidor.",
                    "Muted"));
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"FONTE: BANCO LOCAL • {count} viagem(ns) carregada(s) • funciona offline",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Green") as Brush,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(ModalStatePanel(
                "FALHA DE LEITURA",
                "Histórico local temporariamente indisponível",
                $"O TransPoli não conseguiu ler o banco de viagens agora. Detalhe técnico: {ex.Message}",
                "Yellow"));
        }

        return Task.FromResult<UIElement>(panel);
    }

    private void AddTripHistoryMetric(Grid grid, int column, string label, string value)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        box.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Muted") as Brush
        });
        box.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("Text") as Brush,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(box, column);
        grid.Children.Add(box);
    }

    private async Task DeliverCargoContractAsync(string? contractId)
    {
        if (string.IsNullOrWhiteSpace(contractId)) return;

        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show("Faça login para entregar o contrato.", "Mercado de Cargas", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{ApiBaseUrl}/me/cargo-market/contracts/{Uri.EscapeDataString(contractId)}/deliver");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                MessageBox.Show($"Não foi possível entregar o contrato.\nHTTP {(int)response.StatusCode}", "Mercado de Cargas", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _cargoMarketCacheJson = null;
            _cargoMarketCacheAtUtc = DateTime.MinValue;

            ShowTripCenterModal();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao entregar contrato: {ex.Message}", "Mercado de Cargas", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<UIElement> BuildCargoMarketPanelAsync()
    {
        var panel = new StackPanel();

        TelemetrySnapshot? telemetry = null;
        try { telemetry = await LoadCurrentTelemetryAsync(); }
        catch (Exception ex) { App.WriteUiCrashLog("CargoMarket.LoadTelemetry", ex); }

        var detectedCargo = telemetry != null && telemetry.Connected && !string.IsNullOrWhiteSpace(telemetry.Cargo) ? telemetry.Cargo : "AGUARDANDO CARGA";
        panel.Children.Add(ModalHero("MERCADO DE CARGAS TRANSPOLI", "Central de cotações do ETS2", "Somente cargas realmente detectadas pelo ETS2. O TransPoli não cria nem aceita fretes fictícios; ele registra a carga real e congela a tarifa vigente quando a viagem começa.", detectedCargo, telemetry != null && telemetry.Connected ? "GoldBright" : "Yellow"));
        panel.Children.Add(ModalStatusStrip(telemetry != null && telemetry.Connected ? "● ETS2 CONECTADO • DETECÇÃO AUTOMÁTICA DE CARGAS ATIVA • CICLO DE PREÇOS: 59 MIN" : "● ETS2 DESCONECTADO • O CATÁLOGO CONTINUA VISÍVEL, MAS NOVAS CARGAS DEPENDEM DA TELEMETRIA", telemetry != null && telemetry.Connected ? "Green" : "Yellow"));

        var intro = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(34, 212, 166, 60)),
            BorderBrush = FindResource("StrokeGold") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var introStack = new StackPanel();
        introStack.Children.Add(new TextBlock
        {
            Text = "FLUXO OPERACIONAL  /  ETS2 → TRANSPOLI",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GoldBright") as Brush
        });
        introStack.Children.Add(new TextBlock
        {
            Text = "Aceite o trabalho dentro do ETS2. Quando a telemetria confirmar a carga, o TransPoli registra automaticamente o frete e aplica a cotação vigente.",
            FontSize = 13,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        introStack.Children.Add(new TextBlock
        {
            Text = "Cargas novas entram automaticamente no catálogo. As cotações variam entre R$ 12,00 e R$ 22,00/km a cada ciclo de 59 minutos. Ao iniciar uma viagem real, a tarifa daquele contrato fica congelada até a entrega.",
            FontSize = 12,
            Foreground = FindResource("Muted") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        intro.Child = introStack;
        panel.Children.Add(intro);

        if (telemetry != null && telemetry.Connected)
        {
            var current = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            current.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            current.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(new TextBlock { Text = "CARGA DETECTADA AGORA", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
            left.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(telemetry.Cargo) ? "Nenhuma carga detectada" : telemetry.Cargo,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Text") as Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            left.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(telemetry.Cargo)
                    ? "Aguardando carga do ETS2"
                    : $"{telemetry.SourceCity ?? "Origem"} → {telemetry.DestinationCity ?? "Destino"}",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                Margin = new Thickness(0, 3, 0, 0)
            });
            Grid.SetColumn(left, 0);
            current.Children.Add(left);

            var status = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(42, 20, 180, 100)),
                BorderBrush = FindResource("Green") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusStack = new StackPanel();
            statusStack.Children.Add(new TextBlock { Text = "TELEMETRIA", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Green") as Brush });
            statusStack.Children.Add(new TextBlock
            {
                Text = "CONECTADA",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Text") as Brush,
                Margin = new Thickness(0, 2, 0, 0)
            });
            status.Child = statusStack;
            Grid.SetColumn(status, 1);
            current.Children.Add(status);
            panel.Children.Add(ModalPanel(current));
        }

        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token))
        {
            panel.Children.Add(ModalStatePanel(
                "SESSÃO INDISPONÍVEL",
                "Tarifas temporariamente bloqueadas",
                "A telemetria pode continuar funcionando, mas o catálogo de cotações precisa de uma sessão ativa do motorista para carregar contratos e tarifas.",
                "Yellow"));
            return panel;
        }

        if (telemetry == null || !telemetry.Connected)
        {
            panel.Children.Add(ModalStatePanel(
                "MODO OFFLINE",
                "Catálogo em modo de consulta",
                "As cotações já conhecidas podem continuar visíveis, mas novas cargas e contratos só serão detectados quando o ETS2 restabelecer a telemetria.",
                "Yellow"));
        }
        else if (string.IsNullOrWhiteSpace(telemetry.Cargo))
        {
            panel.Children.Add(ModalStatePanel(
                "ETS2 ONLINE",
                "Aguardando uma carga real",
                "Aceite um trabalho dentro do ETS2. O TransPoli identificará a carga, rota e tarifa automaticamente sem criar fretes fictícios.",
                "Green"));
        }

        panel.Children.Add(ModalSectionTitle("VIAGENS REAIS DETECTADAS", "CONTRATOS ETS2"));

        try
        {
            using var contractRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/cargo-market/contracts");
            contractRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            contractRequest.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var contractResponse = await _http.SendAsync(contractRequest);
            var contractJson = await contractResponse.Content.ReadAsStringAsync();

            if (contractResponse.IsSuccessStatusCode)
            {
                using var contractDoc = JsonDocument.Parse(contractJson);
                var contractRoot = contractDoc.RootElement;
                var contracts = contractRoot.TryGetProperty("contracts", out var contractsElement) && contractsElement.ValueKind == JsonValueKind.Array
                    ? contractsElement.EnumerateArray().ToList()
                    : new List<JsonElement>();

                if (contracts.Count == 0)
                {
                    panel.Children.Add(ModalPanel(new TextBlock
                    {
                        Text = "Nenhum contrato registrado. A carga detectada pelo ETS2 pode ser vinculada a um contrato nesta central.",
                        FontSize = 12,
                        Foreground = FindResource("Muted") as Brush,
                        TextWrapping = TextWrapping.Wrap
                    }));
                }
                else
                {
                    foreach (var contract in contracts.Take(8))
                    {
                        var cargo = GetString(contract, "cargo") ?? "Carga";
                        var status = GetString(contract, "status") ?? "accepted";
                        var rate = GetDecimal(contract, "rate_brl_km");
                        var origin = GetString(contract, "origin") ?? "Origem pendente";
                        var destination = GetString(contract, "destination") ?? "Destino pendente";
                        var distance = GetDecimal(contract, "distance_km");
                        var statusText = status == "active" ? "ATIVO" : status == "delivered" ? "ENTREGUE" : status == "cancelled" ? "CANCELADO" : "ACEITO";
                        var statusBrush = status == "delivered" ? FindResource("Green") as Brush : status == "cancelled" ? FindResource("Yellow") as Brush : FindResource("GoldBright") as Brush;

                        var contractGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                        contractGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
                        contractGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
                        contractGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                        var info = new StackPanel();
                        info.Children.Add(new TextBlock { Text = cargo, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
                        info.Children.Add(new TextBlock { Text = $"{origin} → {destination}", FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
                        info.Children.Add(new TextBlock { Text = distance > 0 ? $"{distance:0.0} km • R$ {rate:0.00}/km" : $"R$ {rate:0.00}/km • distância aguardando ETS2", FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 2, 0, 0) });
                        Grid.SetColumn(info, 0);
                        contractGrid.Children.Add(info);

                        var statusBadge = new Border { BorderBrush = statusBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                        statusBadge.Child = new TextBlock { Text = statusText, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = statusBrush };
                        Grid.SetColumn(statusBadge, 1);
                        contractGrid.Children.Add(statusBadge);

                        if (status == "accepted" || status == "active")
                        {
                            var realStatus = new TextBlock { Text = "ETS2", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Green") as Brush, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                            Grid.SetColumn(realStatus, 2);
                            contractGrid.Children.Add(realStatus);
                        }

                        panel.Children.Add(ModalPanel(contractGrid));
                    }
                }
            }
        }
        catch
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Não foi possível carregar os contratos agora. O catálogo continua disponível.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        try
        {
            // Garante que a carga mostrada pelo ETS2 seja cadastrada antes de
            // carregar o catálogo. Assim a tela nunca fica presa no catálogo antigo.
            if (telemetry != null && telemetry.Connected && !string.IsNullOrWhiteSpace(telemetry.Cargo))
                await DiscoverCargoMarketAsync(telemetry.Cargo);

            string json;
            var cacheStillInMarketCycle =
                !string.IsNullOrWhiteSpace(_cargoMarketCacheJson) &&
                ((_cargoMarketNextRefreshUtc != default && DateTime.UtcNow < _cargoMarketNextRefreshUtc) ||
                 (_cargoMarketNextRefreshUtc == default &&
                  DateTime.UtcNow - _cargoMarketCacheAtUtc < CargoMarketCacheSafetyLifetime));
            if (cacheStillInMarketCycle)
            {
                json = _cargoMarketCacheJson!;
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/cargo-market");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
                using var response = await _http.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    panel.Children.Add(ModalPanel(new TextBlock
                    {
                        Text = TryApiError(json, "Não foi possível carregar o Mercado de Cargas."),
                        FontSize = 12,
                        Foreground = FindResource("Yellow") as Brush,
                        TextWrapping = TextWrapping.Wrap
                    }));
                    return panel;
                }
                _cargoMarketCacheJson = json;
                _cargoMarketCacheAtUtc = DateTime.UtcNow;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var offers = root.TryGetProperty("offers", out var offersElement) && offersElement.ValueKind == JsonValueKind.Array
                ? offersElement.EnumerateArray().ToList()
                : new List<JsonElement>();

            var policy = root.TryGetProperty("policy", out var policyElement) ? policyElement : default;
            var minimum = Math.Max(12m, GetDecimal(policy, "minimumBrlKm"));
            var maximum = Math.Max(22m, GetDecimal(policy, "maximumBrlKm"));
            var cycleMinutes = GetInt(policy, "cycleMinutes");
            var nextRefreshText = GetString(policy, "nextRefreshAt");
            if (DateTime.TryParse(nextRefreshText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var nextRefresh))
            {
                _cargoMarketNextRefreshUtc = nextRefresh.ToUniversalTime();
                StartCargoMarketCountdown();
            }

            var stats = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            AddMarketStat(stats, 0, "CARGAS NO CATÁLOGO", offers.Count.ToString(CultureInfo.InvariantCulture));
            AddMarketStat(stats, 1, "FAIXA DE TARIFA", $"R$ {minimum:0.00}–{maximum:0.00}/km");
            AddMarketStat(stats, 2, "PRÓXIMA COTAÇÃO", cycleMinutes > 0 ? $"{cycleMinutes} MIN" : "59 MIN");
            panel.Children.Add(BuildCargoMarketCountdownCard());
            panel.Children.Add(stats);

            if (offers.Count == 0)
            {
                panel.Children.Add(ModalStatePanel(
                    "CATÁLOGO VAZIO",
                    "Nenhuma carga catalogada ainda",
                    "Assim que uma viagem real do ETS2 informar uma carga nova, o TransPoli fará o cadastro automaticamente e ela passará a participar das próximas cotações.",
                    "Muted"));
                return panel;
            }

            panel.Children.Add(ModalSectionTitle("MELHORES COTAÇÕES AGORA", $"{offers.Count} CARGAS • SEM ACEITE FICTÍCIO"));
            foreach (var offer in offers)
            {
                var cargo = GetString(offer, "display_name") ?? "Carga geral";
                var rate = Math.Clamp(GetDecimal(offer, "rate_brl_km"), 12m, 22m);
                var discoveries = GetInt(offer, "discovered_count");
                var statusKey = GetString(offer, "market_status")?.ToLowerInvariant();
                var trend = GetString(offer, "trend")?.ToLowerInvariant();
                var previousRate = Math.Clamp(GetDecimal(offer, "previous_rate_brl_km"), 12m, 22m);
                var statusText = statusKey == "high" ? "TARIFA ALTA" : statusKey == "low" ? "TARIFA BAIXA" : "TARIFA NORMAL";
                var statusBrush = statusKey == "high"
                    ? FindResource("Green") as Brush
                    : statusKey == "low"
                        ? FindResource("Yellow") as Brush
                        : FindResource("GoldBright") as Brush;

                var card = new Grid { Margin = new Thickness(2, 1, 2, 1) };
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(new TextBlock
                {
                    Text = cargo,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FindResource("Text") as Brush,
                    TextWrapping = TextWrapping.Wrap
                });
                name.Children.Add(new TextBlock
                {
                    Text = $"DETECTADA NO ETS2 • {discoveries} registro{(discoveries == 1 ? "" : "s")}",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Muted") as Brush,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                Grid.SetColumn(name, 0);
                card.Children.Add(name);

                var rateBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                rateBlock.Children.Add(new TextBlock
                {
                    Text = "COTAÇÃO / KM",
                    FontSize = 11,
                    Foreground = FindResource("Muted") as Brush
                });
                rateBlock.Children.Add(new TextBlock
                {
                    Text = $"R$ {rate:0.00}",
                    FontSize = 21,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("GoldBright") as Brush
                });
                rateBlock.Children.Add(new TextBlock { Text = trend == "up" ? $"↑ antes R$ {previousRate:0.00}" : trend == "down" ? $"↓ antes R$ {previousRate:0.00}" : "— estável", FontSize = 12, Foreground = FindResource(trend == "up" ? "Green" : trend == "down" ? "Yellow" : "Muted") as Brush });
                Grid.SetColumn(rateBlock, 1);
                card.Children.Add(rateBlock);

                var badge = new Border
                {
                    BorderBrush = statusBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 6, 10, 6),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = statusText,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = statusBrush
                };
                Grid.SetColumn(badge, 2);
                card.Children.Add(badge);

                panel.Children.Add(ModalPanel(card));
            }

            var note = new TextBlock
            {
                Text = "COTAÇÃO OPERACIONAL • O ciclo atualiza a cada 59 minutos. Quando uma viagem real começa no ETS2, a tarifa daquele contrato fica congelada até a entrega.",
                FontSize = 12,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(note);
        }
        catch
        {
            panel.Children.Add(ModalStatePanel(
                "COMUNICAÇÃO INDISPONÍVEL",
                "Mercado de Cargas temporariamente offline",
                "Não foi possível atualizar as cotações agora. A telemetria e as viagens locais continuam funcionando; tente abrir o catálogo novamente mais tarde.",
                "Yellow"));
        }

        return panel;
    }

    private UIElement BuildCargoMarketCountdownCard()
    {
        var border = new Border
        {
            Background = FindResource("Panel2") as Brush,
            BorderBrush = FindResource("GoldBright") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "COTAÇÃO DINÂMICA TRANSPOLI", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("GoldBright") as Brush });
        left.Children.Add(new TextBlock { Text = "Os valores do catálogo serão recalculados automaticamente.", FontSize = 12, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 2, 0, 0) });
        row.Children.Add(left);
        _cargoMarketCountdownText = new TextBlock { Text = "59:00", FontFamily = new FontFamily("Consolas"), FontSize = 20, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_cargoMarketCountdownText, 1);
        row.Children.Add(_cargoMarketCountdownText);
        border.Child = row;
        UpdateCargoMarketCountdown();
        return border;
    }

    private void StartCargoMarketCountdown()
    {
        _cargoMarketCountdownTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _cargoMarketCountdownTimer.Tick -= CargoMarketCountdownTick;
        _cargoMarketCountdownTimer.Tick += CargoMarketCountdownTick;
        if (!_cargoMarketCountdownTimer.IsEnabled) _cargoMarketCountdownTimer.Start();
    }

    private void CargoMarketCountdownTick(object? sender, EventArgs e)
    {
        UpdateCargoMarketCountdown();
        if (_cargoMarketNextRefreshUtc != default && DateTime.UtcNow >= _cargoMarketNextRefreshUtc)
        {
            // Nunca reabra/reconstrua o modal a partir do timer. Isso causava
            // um loop visual de abrir/fechar quando o backend ainda devolvia
            // o mesmo nextRefreshAt por alguns segundos após a virada do ciclo.
            _cargoMarketCacheJson = null;
            _cargoMarketCacheAtUtc = DateTime.MinValue;
            _cargoMarketNextRefreshUtc = default;
            _cargoMarketCountdownTimer?.Stop();
            if (_cargoMarketCountdownText != null)
                _cargoMarketCountdownText.Text = "ATUALIZAR";
        }
    }

    private void UpdateCargoMarketCountdown()
    {
        if (_cargoMarketCountdownText == null || _cargoMarketNextRefreshUtc == default) return;
        var remaining = _cargoMarketNextRefreshUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        _cargoMarketCountdownText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
    }

    private void AddMarketStat(Grid grid, int column, string label, string value)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(42, 9, 14, 20)),
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(14),
            Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
        border.Child = stack;
        Grid.SetColumn(border, column);
        grid.Children.Add(border);
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private static decimal GetDecimal(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static string TryApiError(string json, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString() ?? fallback;
        }
        catch { }
        return fallback;
    }

    private static int GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}