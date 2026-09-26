using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TransPoli;

public partial class MainWindow
{
    internal void ShowTripsOperationsCenter()
    {
        if (EnsureModalHost() == null) return;
        try
        {
            var data = LoadBankDataLocal();
            var panel = new StackPanel();
            panel.Children.Add(ModalHero(
                "CENTRAL DE VIAGENS",
                "Histórico operacional integrado",
                "Abra uma viagem para consultar operação, financeiro, documentos e timeline usando o mesmo TripId.",
                $"{data.TripHistory.Count} VIAGEM(NS)",
                "GoldBright"));

            if (!string.IsNullOrWhiteSpace(data.ActiveTripId))
            {
                panel.Children.Add(ModalStatusStrip("● VIAGEM ATIVA • DADOS OPERACIONAIS LOCAIS", "Yellow"));
                var active = ModalButton("ABRIR VIAGEM ATUAL");
                var activeId = data.ActiveTripId;
                active.Click += (_, e) => { e.Handled = true; ShowTripOperationsCenter(activeId); };
                panel.Children.Add(active);
            }

            panel.Children.Add(ModalLabel("VIAGENS FINALIZADAS"));
            if (data.TripHistory.Count == 0)
            {
                panel.Children.Add(ModalStatePanel(
                    "HISTÓRICO",
                    "Nenhuma viagem consolidada",
                    "As viagens concluídas aparecerão aqui depois do fechamento operacional.",
                    "Muted"));
            }
            else
            {
                foreach (var trip in data.TripHistory)
                {
                    var card = new StackPanel();
                    card.Children.Add(ModalValueRow(
                        $"{trip.Cargo} • {trip.Origin} → {trip.Destination}",
                        $"{trip.DistanceKm:0.0} km"));
                    card.Children.Add(ModalValueRow(
                        $"Finalizada {trip.FinishedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}",
                        Money(trip.Net),
                        trip.Net >= 0 ? "Green" : "Yellow"));
                    var open = ModalButton("ABRIR CENTRAL DA VIAGEM");
                    var localId = trip.Id;
                    var serverId = trip.ServerId;
                    open.Click += (_, e) => { e.Handled = true; ShowTripOperationsCenter(localId, serverId); };
                    card.Children.Add(open);
                    panel.Children.Add(ModalPanel(card));
                }
            }

            ShowModalContent("trips-center", BuildModalCard(
                "CENTRAL DE VIAGENS",
                panel,
                "Dados locais da operação • acerto oficial disponível no Banco"));
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("TripCenter.List", ex);
            ShowModalContent("trips-center", BuildModalCard("CENTRAL DE VIAGENS",
                ModalStatePanel("VIAGENS", "Histórico temporariamente indisponível",
                    "Nenhum dado foi alterado. Tente abrir novamente.", "Yellow")));
        }
    }

    internal void ShowTripOperationsCenter(string localTripId, string? serverTripId = null)
    {
        if (string.IsNullOrWhiteSpace(localTripId) || EnsureModalHost() == null) return;

        try
        {
            var store = LocalData.Current ?? throw new InvalidOperationException("Banco local ainda não foi inicializado.");
            var logbook = new LocalTripLogbookRepository(store.Db);
            var trip = logbook.Get(localTripId);
            var timeline = logbook.GetTimeline(localTripId);
            var finance = new LocalTripRepository(store.Db).GetFinancialSummary(localTripId);
            var documents = _documents
                .Where(x => string.Equals(x.TripId, localTripId, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(serverTripId)
                        && string.Equals(x.TripId, serverTripId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.RecordedAtUtc)
                .ToList();

            var panel = new StackPanel();
            panel.Children.Add(ModalHero(
                "CENTRAL DA VIAGEM",
                trip?.Cargo ?? "Viagem consolidada",
                "Uma única visão da TripSession: operação, financeiro, documentos e linha do tempo.",
                trip?.Status ?? "REGISTRADA",
                trip?.Status == "FINALIZADA" ? "Green" : "Yellow"));

            if (trip is not null)
            {
                var summary = new StackPanel();
                summary.Children.Add(ModalValueRow("Rota", string.IsNullOrWhiteSpace(trip.Route) ? "Não informada" : trip.Route));
                summary.Children.Add(ModalValueRow("Caminhão", string.IsNullOrWhiteSpace(trip.TruckId) ? "Não informado" : trip.TruckId));
                summary.Children.Add(ModalValueRow("Distância", $"{trip.DistanceKm:0.0} km"));
                summary.Children.Add(ModalValueRow("Combustível consumido", $"{trip.FuelLiters:0.0} L"));
                summary.Children.Add(ModalValueRow("Receita operacional local", Money((decimal)finance.Income), "Green"));
                summary.Children.Add(ModalValueRow("Despesas vinculadas", "-" + Money((decimal)finance.Expenses), "Yellow"));
                summary.Children.Add(ModalValueRow("Resultado operacional local", Money((decimal)finance.Net), finance.Net >= 0 ? "Green" : "Yellow"));
                panel.Children.Add(ModalPanel(summary));
            }
            else
            {
                panel.Children.Add(ModalStatePanel("VIAGEM", "Resumo consolidado ainda não disponível",
                    "O TripId existe, mas o logbook local ainda não possui um snapshot consolidado desta viagem.", "Yellow"));
            }

            panel.Children.Add(ModalLabel("DOCUMENTOS VINCULADOS"));
            var docBox = new StackPanel();
            docBox.Children.Add(ModalValueRow("Documentos desta viagem", documents.Count.ToString()));
            docBox.Children.Add(ModalValueRow("Carimbados", documents.Count(x => string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase)).ToString()));
            panel.Children.Add(ModalPanel(docBox));

            var docs = ModalButton("ABRIR DOCUMENTOS DESTA VIAGEM");
            docs.Click += (_, e) =>
            {
                e.Handled = true;
                ShowTripDocuments(localTripId, serverTripId);
            };
            panel.Children.Add(docs);

            var bank = ModalButton("ABRIR ACERTO NO BANCO");
            bank.Click += (_, e) =>
            {
                e.Handled = true;
                ShowBankModal("viagem");
            };
            panel.Children.Add(bank);

            panel.Children.Add(ModalLabel("LINHA DO TEMPO OPERACIONAL"));
            if (timeline.Count == 0)
            {
                panel.Children.Add(ModalStatePanel("TIMELINE", "Nenhum evento operacional arquivado",
                    "Abastecimentos, manutenções e ocorrências vinculados a esta viagem aparecerão aqui.", "Muted"));
            }
            else
            {
                foreach (var item in timeline.OrderByDescending(x => x.At).Take(40))
                {
                    var row = new StackPanel();
                    row.Children.Add(ModalValueRow(
                        $"{item.Type} • {item.At.ToLocalTime():dd/MM HH:mm}",
                        $"{item.OdometerKm:0.0} km"));
                    if (!string.IsNullOrWhiteSpace(item.Status))
                        row.Children.Add(ModalLine(item.Status, 11));
                    if (!string.IsNullOrWhiteSpace(item.Details))
                        row.Children.Add(ModalLine(item.Details, 11));
                    panel.Children.Add(ModalPanel(row));
                }
            }

            ShowModalContent("trip-center", BuildModalCard(
                "CENTRAL DE VIAGENS",
                panel,
                $"TripId local • {localTripId}"));
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("TripCenter.Open", ex);
            ShowModalContent("trip-center", BuildModalCard("CENTRAL DE VIAGENS",
                ModalStatePanel("VIAGEM", "Não foi possível montar a central desta viagem",
                    "Os dados permanecem preservados. Tente abrir novamente.", "Yellow")));
        }
    }
}
