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

    internal async void ShowTripOperationsCenter(string localTripId, string? serverTripId = null)
    {
        if (string.IsNullOrWhiteSpace(localTripId) || EnsureModalHost() == null) return;

        try
        {
            var store = LocalData.Current ?? throw new InvalidOperationException("Banco local ainda não foi inicializado.");
            var logbook = new LocalTripLogbookRepository(store.Db);
            var trip = logbook.Get(localTripId);
            var timeline = logbook.GetTimeline(localTripId);
            var refuelings = logbook.GetRefuelings(localTripId);
            var maintenance = logbook.GetMaintenance(localTripId);
            var tolls = logbook.GetTolls(localTripId);
            var sync = logbook.GetSyncDetail(localTripId);
            var closure = new LocalTripClosureRepository(store.Db).GetStatus(localTripId);
            var finance = new LocalTripRepository(store.Db).GetFinancialSummary(localTripId);
            var officialSettlement = await GetOfficialTripSettlementAsync(localTripId, serverTripId);
            var documents = _documents
                .Where(x => string.Equals(x.TripId, localTripId, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(serverTripId)
                        && string.Equals(x.TripId, serverTripId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.RecordedAtUtc)
                .ToList();
            var stampedDocument = documents.FirstOrDefault(x => string.Equals(x.Status, "Carimbado", StringComparison.OrdinalIgnoreCase));
            var primaryDocument = stampedDocument ?? documents.FirstOrDefault();
            var driverName = primaryDocument?.Driver ?? "";
            var truckName = primaryDocument is null ? "" : string.Join(" ", new[] { primaryDocument.TruckBrand, primaryDocument.TruckModel }
                .Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
            if (string.IsNullOrWhiteSpace(truckName)) truckName = primaryDocument?.Truck ?? "";
            if (string.IsNullOrWhiteSpace(truckName)) truckName = trip?.TruckId ?? closure.TruckId;

            var isCurrentActiveTrip = _tripActive && string.Equals(_localTripId, localTripId, StringComparison.OrdinalIgnoreCase);
            var dossierStatus = !string.IsNullOrWhiteSpace(closure.LastError)
                ? "COM PENDÊNCIA"
                : officialSettlement is not null && sync.Pending == 0
                    ? "LIQUIDADA"
                    : sync.Pending > 0
                        ? "AGUARDANDO SYNC"
                        : isCurrentActiveTrip || (trip is not null && !trip.FinishedAt.HasValue)
                            ? "EM ANDAMENTO"
                            : closure.Completed
                                ? "FECHADA • AGUARDANDO ACERTO"
                                : "REGISTRADA";
            var dossierTone = dossierStatus == "LIQUIDADA" ? "Green"
                : dossierStatus == "COM PENDÊNCIA" || dossierStatus == "AGUARDANDO SYNC" ? "Yellow"
                : "GoldBright";

            var panel = new StackPanel();
            panel.Children.Add(ModalHero(
                "CENTRAL DA VIAGEM",
                trip?.Cargo ?? "Viagem consolidada",
                "Uma única visão da TripSession: operação, financeiro, documentos e linha do tempo.",
                dossierStatus,
                dossierTone));
            panel.Children.Add(ModalStatusStrip($"● {dossierStatus}", dossierTone));

            panel.Children.Add(ModalLabel("IDENTIDADE DA OPERAÇÃO"));
            var identity = new StackPanel();
            identity.Children.Add(ModalValueRow("Motorista", string.IsNullOrWhiteSpace(driverName) ? "Não informado no arquivo da viagem" : driverName));
            identity.Children.Add(ModalValueRow("Caminhão", string.IsNullOrWhiteSpace(truckName) ? "Não informado no arquivo da viagem" : truckName));
            if (!string.IsNullOrWhiteSpace(primaryDocument?.LicensePlate))
                identity.Children.Add(ModalValueRow("Placa", primaryDocument.LicensePlate));
            if (!string.IsNullOrWhiteSpace(serverTripId))
                identity.Children.Add(ModalValueRow("TripId servidor", serverTripId));
            panel.Children.Add(ModalPanel(identity));

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

            panel.Children.Add(ModalLabel("ACERTO DA VIAGEM"));
            if (officialSettlement is not null)
            {
                panel.Children.Add(ModalStatusStrip("✓ ACERTO OFICIAL CONSOLIDADO NO SERVIDOR", "Green"));
                var settlementBox = new StackPanel();
                settlementBox.Children.Add(ModalValueRow($"Parte do motorista • {officialSettlement.DriverSharePct:0.##}%", Money(officialSettlement.DriverGross), "Green"));
                settlementBox.Children.Add(ModalValueRow("Parte da empresa", Money(officialSettlement.CompanyShare), "Muted"));
                if (officialSettlement.CompanyExpenses > 0)
                    settlementBox.Children.Add(ModalValueRow("Despesas assumidas pela empresa", Money(officialSettlement.CompanyExpenses), "Muted"));
                if (officialSettlement.DriverExpenses > 0)
                    settlementBox.Children.Add(ModalValueRow("Despesas do motorista", "-" + Money(officialSettlement.DriverExpenses), "Yellow"));
                if (officialSettlement.LoanPayment > 0)
                    settlementBox.Children.Add(ModalValueRow("Parcela de empréstimo", "-" + Money(officialSettlement.LoanPayment), "Yellow"));
                settlementBox.Children.Add(ModalValueRow("LÍQUIDO OFICIAL DO MOTORISTA", Money(officialSettlement.DriverNet), officialSettlement.DriverNet >= 0 ? "Green" : "Yellow"));
                panel.Children.Add(ModalPanel(settlementBox));
            }
            else
            {
                panel.Children.Add(ModalStatePanel("ACERTO",
                    sync.Pending > 0 ? "Aguardando sincronização/consolidação" : "Acerto oficial ainda não disponível",
                    "Os valores operacionais locais permanecem separados do saldo oficial até o servidor consolidar esta viagem.",
                    sync.Pending > 0 ? "Yellow" : "Muted"));
            }

            panel.Children.Add(ModalLabel("SITUAÇÃO DE SINCRONIZAÇÃO"));
            panel.Children.Add(ModalStatusStrip(
                sync.Pending == 0
                    ? "✓ SEM PENDÊNCIAS LOCAIS DESTA VIAGEM"
                    : $"● {sync.Pending} ITEM(NS) DESTA VIAGEM AGUARDANDO SINCRONIZAÇÃO",
                sync.Pending == 0 ? "Green" : "Yellow"));
            if (sync.Pending > 0)
            {
                var syncBox = new StackPanel();
                syncBox.Children.Add(ModalValueRow("Tentativas acumuladas", sync.Attempts.ToString()));
                syncBox.Children.Add(ModalValueRow("Última tentativa",
                    sync.LastAttemptAt.HasValue ? sync.LastAttemptAt.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "Ainda não enviada"));
                panel.Children.Add(ModalPanel(syncBox));
            }

            panel.Children.Add(ModalLabel("POLIPASS / PEDÁGIOS"));
            if (tolls.Count == 0)
                panel.Children.Add(ModalLine("Nenhum débito de pedágio vinculado a esta viagem.", 12));
            foreach (var toll in tolls)
            {
                var tollBox = new StackPanel();
                tollBox.Children.Add(ModalValueRow(
                    $"Pedágio • {toll.At.ToLocalTime():dd/MM/yyyy HH:mm}",
                    "-" + Money((decimal)toll.Amount), "Yellow"));
                tollBox.Children.Add(ModalLine(string.IsNullOrWhiteSpace(toll.Description) ? "PoliPass" : toll.Description, 11));
                var receipt = _poliPassRecords
                    .Where(x => Math.Abs((double)x.Amount - toll.Amount) < 0.01
                        && Math.Abs((x.RecordedAtUtc - toll.At).TotalMinutes) <= 5)
                    .OrderBy(x => Math.Abs((x.RecordedAtUtc - toll.At).TotalSeconds))
                    .FirstOrDefault();
                if (receipt is not null)
                {
                    var openReceipt = ModalButton("ABRIR COMPROVANTE POLIPASS");
                    openReceipt.Click += (_, e) => { e.Handled = true; ShowPoliPassReceipt(receipt); };
                    tollBox.Children.Add(openReceipt);
                }
                panel.Children.Add(ModalPanel(tollBox));
            }

            panel.Children.Add(ModalLabel("ABASTECIMENTOS"));
            if (refuelings.Count == 0)
                panel.Children.Add(ModalLine("Nenhum abastecimento vinculado a esta viagem.", 12));
            foreach (var fuel in refuelings)
            {
                var fuelBox = new StackPanel();
                fuelBox.Children.Add(ModalValueRow(
                    $"{fuel.Station} • {fuel.Location}",
                    Money((decimal)fuel.TotalCost), "Yellow"));
                fuelBox.Children.Add(ModalValueRow(
                    $"{fuel.Liters:0.0} L × {Money((decimal)fuel.PricePerLiter)}/L",
                    $"{fuel.OdometerKm:0.0} km"));
                fuelBox.Children.Add(ModalLine($"Comprovante {fuel.Id} • {fuel.At.ToLocalTime():dd/MM/yyyy HH:mm}", 11));
                panel.Children.Add(ModalPanel(fuelBox));
            }

            panel.Children.Add(ModalLabel("MANUTENÇÃO"));
            if (maintenance.Count == 0)
                panel.Children.Add(ModalLine("Nenhuma manutenção vinculada a esta viagem.", 12));
            foreach (var item in maintenance)
            {
                var maintenanceBox = new StackPanel();
                maintenanceBox.Children.Add(ModalValueRow(
                    $"{item.Type} • {item.Component}",
                    Money((decimal)item.Cost), "Yellow"));
                if (!string.IsNullOrWhiteSpace(item.Description))
                    maintenanceBox.Children.Add(ModalLine(item.Description, 11));
                maintenanceBox.Children.Add(ModalLine(
                    $"{item.At.ToLocalTime():dd/MM/yyyy HH:mm} • {item.OdometerKm:0.0} km", 11));
                panel.Children.Add(ModalPanel(maintenanceBox));
            }

            panel.Children.Add(ModalLabel("DANFE / CARIMBO"));
            if (primaryDocument is null)
            {
                panel.Children.Add(ModalStatePanel("DANFE","Nenhum documento arquivado nesta viagem",
                    "O prontuário não associa documentos de outras viagens como fallback.","Muted"));
            }
            else
            {
                var danfe = new StackPanel();
                danfe.Children.Add(ModalValueRow("Documento", string.IsNullOrWhiteSpace(primaryDocument.Reference) ? primaryDocument.Id : primaryDocument.Reference));
                danfe.Children.Add(ModalValueRow("Status", string.Equals(primaryDocument.Status,"Carimbado",StringComparison.OrdinalIgnoreCase) ? "CARIMBADA" : primaryDocument.Status,
                    string.Equals(primaryDocument.Status,"Carimbado",StringComparison.OrdinalIgnoreCase) ? "Green" : "Yellow"));
                danfe.Children.Add(ModalValueRow("Carimbo",
                    primaryDocument.StampedAtUtc.HasValue ? primaryDocument.StampedAtUtc.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "Não registrado"));
                panel.Children.Add(ModalPanel(danfe));
            }

            panel.Children.Add(ModalLabel("TACÓGRAFO"));
            var tachBox = new StackPanel();
            tachBox.Children.Add(ModalValueRow("Fechamento",
                closure.TachographClosed ? "ARQUIVADO" : "SEM CONFIRMAÇÃO DE FECHAMENTO",
                closure.TachographClosed ? "Green" : "Yellow"));
            var tach = ModalButton("ABRIR TICKET / JORNADA ARQUIVADA");
            var tachSession = closure.SessionKey;
            tach.Click += (_, e) => { e.Handled = true; ShowArchivedTachographForTrip(localTripId, tachSession); };
            tachBox.Children.Add(tach);
            panel.Children.Add(ModalPanel(tachBox));

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
