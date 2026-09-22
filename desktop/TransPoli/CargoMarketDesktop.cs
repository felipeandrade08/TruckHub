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

namespace TransPoli;

public partial class MainWindow
{
    private static readonly TimeSpan CargoMarketCacheLifetime = TimeSpan.FromSeconds(20);
    private string? _cargoMarketCacheJson;
    private DateTime _cargoMarketCacheAtUtc;
    private string? _lastDiscoveredCargo;

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
        catch
        {
            // A descoberta do catálogo nunca pode interromper a viagem.
        }
    }

    internal async void ShowCargoMarketModal()
    {
        var layer = EnsureModalHost();
        if (layer == null) return;

        ShowModalContent("cargo-market", BuildModalLoading("CARREGANDO CATÁLOGO..."));
        var panel = await BuildCargoMarketPanelAsync();
        ShowModalContent("cargo-market", BuildModalCard(
            "📦 MERCADO DE CARGAS",
            panel,
            "Catálogo de tarifas por KM • leitura automática da carga pelo ETS2/ATS"));
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
        catch { }

        var live = LastTelemetry;
        string? localActiveTripId = null;
        try { localActiveTripId = GetLocalActiveTripId(); } catch { }
        if (_tripActive && live is not null)
        {
            var liveDistance = Math.Max(0f, live.OdometerKm - _tripStartOdometer);
            var liveRate = _localTripRatePerKm > 0 ? _localTripRatePerKm : 6.00;
            var liveGross = liveDistance * liveRate;
            var liveCard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 84, 217, 155)),
                BorderBrush = FindResource("Green") as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(13),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var liveStack = new StackPanel();
            liveStack.Children.Add(new TextBlock
            {
                Text = "● VIAGEM ATUAL • AO VIVO",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Green") as Brush
            });
            liveStack.Children.Add(new TextBlock
            {
                Text = $"{live.SourceCity ?? "Origem"} → {live.DestinationCity ?? "Destino"}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("Text") as Brush,
                Margin = new Thickness(0, 4, 0, 0)
            });
            liveStack.Children.Add(new TextBlock
            {
                Text = $"{(string.IsNullOrWhiteSpace(live.Cargo) ? "Carga não informada" : live.Cargo)} • {liveDistance:0.0} km percorridos • R$ {liveRate:0.00}/km • bruto estimado R$ {liveGross:0.00}",
                FontSize = 10,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            liveStack.Children.Add(new TextBlock
            {
                Text = $"Velocidade {Math.Abs(live.SpeedKph):0} km/h • combustível {live.FuelLiters:0.0} L • autonomia {live.FuelRangeKm:0} km",
                FontSize = 9,
                Foreground = FindResource("Muted") as Brush,
                Margin = new Thickness(0, 3, 0, 0)
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
                await ManualFinishCurrentTripAsync();
                ShowTripCenterModal();
            };
            liveStack.Children.Add(finishButton);
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
                await ManualFinishCurrentTripAsync();
                ShowTripCenterModal();
            };
            panel.Children.Add(recoveredFinish);
        }

        panel.Children.Add(ModalLabel("HISTÓRICO LOCAL DE VIAGENS"));

        if (LocalData.Current is not { } store)
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Banco local do TransPoli ainda não está disponível.",
                FontSize = 11,
                Foreground = FindResource("Yellow") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
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
                var localTrips = new LocalTripRepository(store.Db);
                // Se o ETS2 já mudou para outra rota/carga, qualquer viagem local
                // anterior com dados diferentes não pode continuar como "ativa".
                localTrips.FinishMismatchedActiveTrips(currentTelemetry);
                if (!HasActiveJob(currentTelemetry))
                    localTrips.FinishOrphanedActiveTrips(currentTelemetry);
            }

            using var command = store.Db.Connection.CreateCommand();
            command.CommandText = @"
SELECT
    t.id,t.cargo_name,t.source_city,t.destination_city,t.status,
    t.started_at_utc,t.finished_at_utc,t.distance_km,t.rate_per_km,
    t.income_gross,t.expense_total,t.net_value,t.server_id
FROM trip t
ORDER BY t.started_at_utc DESC
LIMIT 50;";

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
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 9)
                };

                var stack = new StackPanel();
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var title = new StackPanel();
                title.Children.Add(new TextBlock
                {
                    Text = cargo,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Text") as Brush
                });
                title.Children.Add(new TextBlock
                {
                    Text = $"{origin} → {destination}",
                    FontSize = 9,
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
                    FontSize = 8,
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
                    FontSize = 9,
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
                        FontSize = 9,
                        Foreground = FindResource("Muted") as Brush,
                        Margin = new Thickness(0, 7, 0, 0)
                    });
                }

                card.Child = stack;
                panel.Children.Add(card);
            }

            if (count == 0)
            {
                panel.Children.Add(ModalPanel(new TextBlock
                {
                    Text = "Nenhuma viagem registrada no banco local ainda.",
                    FontSize = 11,
                    Foreground = FindResource("Muted") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }));
            }
            else
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"FONTE: BANCO LOCAL • {count} viagem(ns) carregada(s) • funciona offline",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Green") as Brush,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = $"Não foi possível ler o histórico local: {ex.Message}",
                FontSize = 11,
                Foreground = FindResource("Yellow") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        return Task.FromResult<UIElement>(panel);
    }

    private void AddTripHistoryMetric(Grid grid, int column, string label, string value)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        box.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 8,
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
        try { telemetry = await LoadCurrentTelemetryAsync(); } catch { }

        var intro = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(34, 212, 166, 60)),
            BorderBrush = FindResource("StrokeGold") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var introStack = new StackPanel();
        introStack.Children.Add(new TextBlock
        {
            Text = "CATÁLOGO TRANSPOLI",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("GoldBright") as Brush
        });
        introStack.Children.Add(new TextBlock
        {
            Text = "A carga não é escolhida neste painel. O ETS2 informa a carga da viagem e o TransPoli consulta automaticamente o catálogo para encontrar a tarifa.",
            FontSize = 12,
            Foreground = FindResource("Text") as Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        introStack.Children.Add(new TextBlock
        {
            Text = "Se uma carga ainda não existir, ela é cadastrada automaticamente e recebe uma tarifa fixa entre R$ 5,00 e R$ 12,00/km. Depois da descoberta, o valor não fica oscilando.",
            FontSize = 11,
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
            left.Children.Add(new TextBlock { Text = "CARGA DETECTADA AGORA", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
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
                FontSize = 10,
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
            statusStack.Children.Add(new TextBlock { Text = "TELEMETRIA", FontSize = 8, FontWeight = FontWeights.Bold, Foreground = FindResource("Green") as Brush });
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
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Sessão do motorista não encontrada. O catálogo precisa de uma sessão ativa para carregar as tarifas.",
                FontSize = 12,
                Foreground = FindResource("Yellow") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
            return panel;
        }

        panel.Children.Add(ModalLabel("MEUS CONTRATOS"));

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
                        FontSize = 11,
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
                        info.Children.Add(new TextBlock { Text = $"{origin} → {destination}", FontSize = 9, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
                        info.Children.Add(new TextBlock { Text = distance > 0 ? $"{distance:0.0} km • R$ {rate:0.00}/km" : $"R$ {rate:0.00}/km • distância aguardando ETS2", FontSize = 9, Foreground = FindResource("Muted") as Brush, Margin = new Thickness(0, 2, 0, 0) });
                        Grid.SetColumn(info, 0);
                        contractGrid.Children.Add(info);

                        var statusBadge = new Border { BorderBrush = statusBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
                        statusBadge.Child = new TextBlock { Text = statusText, FontSize = 8, FontWeight = FontWeights.Bold, Foreground = statusBrush };
                        Grid.SetColumn(statusBadge, 1);
                        contractGrid.Children.Add(statusBadge);

                        if (status == "accepted" || status == "active")
                        {
                            var deliverButton = new Button { Content = "ENTREGAR", Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(8, 0, 0, 0), Tag = GetString(contract, "id"), ToolTip = "Marcar o contrato como entregue" };
                            deliverButton.Click += async (_, _) => await DeliverCargoContractAsync((string?)deliverButton.Tag);
                            Grid.SetColumn(deliverButton, 2);
                            contractGrid.Children.Add(deliverButton);
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
                FontSize = 11,
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
            if (!string.IsNullOrWhiteSpace(_cargoMarketCacheJson) &&
                DateTime.UtcNow - _cargoMarketCacheAtUtc < CargoMarketCacheLifetime)
            {
                json = _cargoMarketCacheJson;
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
            var minimum = GetDecimal(policy, "minimumBrlKm");
            var maximum = GetDecimal(policy, "maximumBrlKm");

            var stats = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            stats.ColumnDefinitions.Add(new ColumnDefinition());
            AddMarketStat(stats, 0, "CARGAS NO CATÁLOGO", offers.Count.ToString(CultureInfo.InvariantCulture));
            AddMarketStat(stats, 1, "FAIXA DE TARIFA", $"R$ {minimum:0.00}–{maximum:0.00}/km");
            AddMarketStat(stats, 2, "MODELO", "FIXO APÓS DESCOBERTA");
            panel.Children.Add(stats);

            if (offers.Count == 0)
            {
                panel.Children.Add(ModalPanel(new TextBlock
                {
                    Text = "Nenhuma carga foi catalogada ainda. Assim que uma viagem informar uma carga nova, o TransPoli fará o cadastro automaticamente.",
                    FontSize = 12,
                    Foreground = FindResource("Muted") as Brush,
                    TextWrapping = TextWrapping.Wrap
                }));
                return panel;
            }

            panel.Children.Add(ModalLabel($"CATÁLOGO DE TARIFAS • {offers.Count} CARGAS"));

            foreach (var offer in offers)
            {
                var cargo = GetString(offer, "display_name") ?? "Carga geral";
                var rate = GetDecimal(offer, "rate_brl_km");
                var discoveries = GetInt(offer, "discovered_count");
                var statusKey = GetString(offer, "market_status")?.ToLowerInvariant();
                var statusText = statusKey == "high" ? "TARIFA ALTA" : statusKey == "low" ? "TARIFA BAIXA" : "TARIFA NORMAL";
                var statusBrush = statusKey == "high"
                    ? FindResource("Green") as Brush
                    : statusKey == "low"
                        ? FindResource("Yellow") as Brush
                        : FindResource("GoldBright") as Brush;

                var card = new Grid { Margin = new Thickness(0, 0, 0, 9) };
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(new TextBlock
                {
                    Text = cargo,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Text") as Brush,
                    TextWrapping = TextWrapping.Wrap
                });
                name.Children.Add(new TextBlock
                {
                    Text = $"CATALOGADA • {discoveries} descoberta{(discoveries == 1 ? "" : "s")}",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("Muted") as Brush,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                Grid.SetColumn(name, 0);
                card.Children.Add(name);

                var rateBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                rateBlock.Children.Add(new TextBlock
                {
                    Text = "TARIFA POR KM",
                    FontSize = 8,
                    Foreground = FindResource("Muted") as Brush
                });
                rateBlock.Children.Add(new TextBlock
                {
                    Text = $"R$ {rate:0.00}",
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindResource("GoldBright") as Brush
                });
                Grid.SetColumn(rateBlock, 1);
                card.Children.Add(rateBlock);

                var badge = new Border
                {
                    BorderBrush = statusBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(9, 5, 9, 5),
                    VerticalAlignment = VerticalAlignment.Center
                };
                badge.Child = new TextBlock
                {
                    Text = statusText,
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = statusBrush
                };
                Grid.SetColumn(badge, 2);
                card.Children.Add(badge);

                panel.Children.Add(ModalPanel(card));
            }

            var note = new TextBlock
            {
                Text = "ℹ O valor acima é o valor armazenado no catálogo. A cada nova viagem, o TransPoli usa a tarifa já cadastrada para a carga correspondente.",
                FontSize = 10,
                Foreground = FindResource("Muted") as Brush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            panel.Children.Add(note);
        }
        catch
        {
            panel.Children.Add(ModalPanel(new TextBlock
            {
                Text = "Erro de comunicação com o Mercado de Cargas. Tente abrir o catálogo novamente.",
                FontSize = 12,
                Foreground = FindResource("Yellow") as Brush,
                TextWrapping = TextWrapping.Wrap
            }));
        }

        return panel;
    }

    private void AddMarketStat(Grid grid, int column, string label, string value)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(42, 9, 14, 20)),
            BorderBrush = FindResource("Panel2") as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10),
            Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 7.5, FontWeight = FontWeights.Bold, Foreground = FindResource("Muted") as Brush });
        stack.Children.Add(new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
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