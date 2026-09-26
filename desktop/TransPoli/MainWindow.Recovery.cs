using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TransPoli;

public partial class MainWindow
{
    private bool _recoveryBusy;
    private DateTime _lastRecoveryAtUtc = DateTime.MinValue;


    private async Task<bool> ResumePendingTripClosuresAsync(TelemetrySnapshot data)
    {
        if(LocalData.Current is not { } store) return false;
        var ownerUserId=SecureTokenStore.ReadUserId();
        if(string.IsNullOrWhiteSpace(ownerUserId)) return false;
        var closures=new LocalTripClosureRepository(store.Db);
        var pending=closures.GetPending(ownerUserId);
        if(pending.Count==0) return false;
        foreach(var item in pending)
        {
            try
            {
                // A closure pode já estar concluída e existir aqui apenas porque a
                // remoção da TripSession falhou no processo anterior. Nesse caso não
                // repetimos financeiro, tacógrafo, saúde nem outbox.
                if(closures.IsMarked(item.TripId,"completed_at_utc"))
                {
                    if(string.Equals(_localTripId,item.TripId,StringComparison.OrdinalIgnoreCase))
                    {
                        if(!ClearSessionState())
                        {
                            StatusText.Text="TransPoli • fechamento concluído • limpeza da sessão ainda pendente";
                            return true;
                        }
                        StatusText.Text="TransPoli • fechamento concluído • sessão recuperada e limpa";
                    }
                    continue;
                }
                var trips=new LocalTripRepository(store.Db);
                // Todos os números abaixo pertencem ao snapshot imutável da viagem.
                // 'data' só confirma que o app está conectado; nunca recalcula a viagem encerrada.
                var frozen=new TelemetrySnapshot
                {
                    OdometerKm=(float)item.FinalOdometer,
                    FuelLiters=(float)item.FinalFuel,
                    CargoDamage=(float)item.CargoDamage,
                    CargoMassKg=(float)item.CargoMassKg,
                    WearEngine=(float)item.WearEngine,
                    WearTransmission=(float)item.WearTransmission,
                    WearCabin=(float)item.WearCabin,
                    WearChassis=(float)item.WearChassis,
                    WearWheels=(float)item.WearWheels,
                    TruckId=item.TruckId
                };
                if(!item.LocalSettled)
                {
                    trips.FinishTrip(item.TripId,frozen,item.DistanceKm,item.FuelConsumedL,item.GrossValue,string.IsNullOrWhiteSpace(item.Reason)?"recovery_fechamento":item.Reason);
                    if(!closures.Mark(item.TripId,"local_settled_at_utc"))
                        throw new InvalidOperationException("Liquidação local concluída, mas checkpoint não persistiu.");
                }
                if(!item.HealthCaptured)
                {
                    trips.AppendTruckHealth(item.TruckId,item.TripId,frozen);
                    if(!closures.Mark(item.TripId,"health_captured_at_utc"))
                        throw new InvalidOperationException("Saúde final capturada, mas checkpoint não persistiu.");
                }

                // Recovery reproduz apenas o fechamento operacional da viagem.
                // Empréstimos pertencem ao livro-caixa oficial do servidor e não
                // podem ser cobrados por estado legado do SQLite deste computador.

                if(!item.TachographClosed)
                {
                    if(!ArchiveTachographForTrip(item.TripId, item.SessionKey))
                        throw new InvalidOperationException("Arquivo do tacógrafo não pôde ser persistido.");
                    if(!closures.Mark(item.TripId,"tachograph_closed_at_utc"))
                        throw new InvalidOperationException("Tacógrafo arquivado, mas checkpoint não persistiu.");
                }
                if(!item.RemoteQueued)
                {
                    // Nunca tenta enviar/encerrar uma closure criada por outra conta.
                    // GetPending já filtra por owner; esta checagem mantém a fronteira
                    // explícita caso o repositório/migração mude no futuro.
                    if(!string.Equals(item.OwnerUserId,ownerUserId,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Fechamento pertence a outra identidade autenticada.");
                    // Recovery usa o mesmo caminho determinístico do fechamento normal.
                    // Nunca fazemos HTTP direto aqui: trip-finish-<localTripId> é a única
                    // operação remota e o outbox resolve o server_id persistido antes do envio.
                    var remoteDurable=_serverSync.QueueTripFinish(item.TripId,new
                        { distanceKm=item.DistanceKm,fuelUsedL=item.FuelConsumedL,cargoDamage=item.CargoDamage,cargoMassKg=item.CargoMassKg })
                        && new LocalSyncQueueRepository(store.Db).HasPendingTripFinish(item.TripId,ownerUserId);
                    if(!remoteDurable)
                        throw new InvalidOperationException("Finalização remota ainda não foi confirmada nem persistida na fila local.");
                    if(!closures.Mark(item.TripId,"remote_queued_at_utc"))
                        throw new InvalidOperationException("Finalização remota durável, mas checkpoint não persistiu.");
                }
                trips.RefreshFinancialSummary(item.TripId);
                new LocalTripLogbookRepository(store.Db).Consolidate(item.TripId,item.SessionKey);
                if(!closures.Complete(item.TripId))
                    throw new InvalidOperationException("Checkpoint de fechamento ainda não está completo.");
                if(string.Equals(_localTripId,item.TripId,StringComparison.OrdinalIgnoreCase))
                {
                    // O recovery só limpa a memória se este checkpoint ainda for a
                    // TripSession carregada. Um fechamento antigo nunca pode apagar
                    // a identidade de uma operação mais nova.
                    if(!ClearSessionState())
                        throw new InvalidOperationException("Fechamento concluído, mas a TripSession persistida ainda não pôde ser removida.");
                }
                InvalidatePhoneOfficialCache(economy: true, trips: true, documents: true);
                _lastOfficialRankingRefreshUtc = DateTime.MinValue;
                TripStatusText.Text="VIAGEM RECUPERADA E FINALIZADA";
                TripDistanceText.Text=$"{item.DistanceKm:0.0} km";
                StatusText.Text="TransPoli • fechamento congelado recuperado e concluído";
            }
            catch(Exception ex)
            {
                App.WriteUiCrashLog("TripRecovery.PendingClosure", ex);
                closures.Fail(item.TripId,ex.Message);
                TripStatusText.Text="FECHAMENTO PENDENTE • viagem preservada";
                StatusText.Text="TransPoli • fechamento congelado preservado para nova tentativa";
                return true;
            }
        }
        return true;
    }

    private async Task TryRecoverActiveTrip()
    {
        if (_recoveryBusy || DateTime.UtcNow - _lastRecoveryAtUtc < TimeSpan.FromMinutes(5)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;
        var ownerUserId = SecureTokenStore.ReadUserId();
        if (string.IsNullOrWhiteSpace(ownerUserId)) return;

        _recoveryBusy = true;
        _lastRecoveryAtUtc = DateTime.UtcNow;
        try
        {
            // O loop principal já acabou de ler a telemetria. Recovery não deve
            // duplicar essa requisição local nem criar um segundo snapshot divergente.
            var data = LastTelemetry;
            if (data is null || !data.Connected) return;

            if (await ResumePendingTripClosuresAsync(data)) return;

            // Sem job real do ETS2 e sem fechamento pendente não existe viagem
            // legítima para recuperar. Evita consultar /me/trips com o motorista ocioso.
            if (!_tripActive && !HasActiveJob(data)) return;

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("trips", out var trips) || trips.ValueKind != JsonValueKind.Array) return;
            JsonElement? activeTrip = null;
            foreach (var trip in trips.EnumerateArray())
            {
                if (!IsOpenTrip(trip)) continue;
                if (_tripActive && !string.IsNullOrWhiteSpace(_serverTripId) &&
                    string.Equals(ReadString(trip, "id"), _serverTripId, StringComparison.OrdinalIgnoreCase))
                {
                    activeTrip = trip;
                    break;
                }
                if (!_tripActive && HasActiveJob(data) && TripMatchesTelemetry(trip, data))
                {
                    var candidateServerId = ReadString(trip, "id");
                    var candidateHasLocalIdentity = false;
                    if (!string.IsNullOrWhiteSpace(candidateServerId) && LocalData.Current is { } candidateStore)
                        candidateHasLocalIdentity = !string.IsNullOrWhiteSpace(
                            new LocalTripRepository(candidateStore.Db).FindActiveTripIdByServerId(candidateServerId, SecureTokenStore.ReadUserId() ?? ""));

                    // Carga/origem/destino servem somente para descobrir um candidato.
                    // A retomada automática exige também a identidade local já persistida;
                    // sem ela, o candidato não recebe autorização documental nem substitui
                    // uma operação nova que coincidentemente tenha a mesma rota/carga.
                    if (candidateHasLocalIdentity)
                    {
                        activeTrip = trip;
                        break;
                    }
                }
            }

            // A sessão antiga não pode bloquear uma viagem nova. Se o servidor não
            // encontrar mais o contrato salvo, limpamos somente o estado transitório
            // e deixamos o ETS2 iniciar a próxima viagem normalmente.
            if (activeTrip is null)
            {
                if (_tripActive && HasActiveJob(data))
                {
                    var sessionMatches = Same(_tripCargo, data.Cargo) &&
                                         Same(_tripRouteOrigin, data.SourceCity) &&
                                         Same(_tripRouteDestination, data.DestinationCity);
                    if (!sessionMatches)
                    {
                        // Um contrato ausente no servidor não autoriza apagar uma
                        // TripSession local ativa. O job novo não pode herdar nem
                        // substituir silenciosamente a identidade da viagem anterior.
                        StatusText.Text = "TransPoli • TripSession local preservada • contrato remoto não encontrado";
                    }
                }
                return;
            }
            var tripElement = activeTrip.Value;
            if (!tripElement.TryGetProperty("id", out var idElement)) return;
            var tripId = idElement.GetString(); if (string.IsNullOrWhiteSpace(tripId)) return;
            // Entrega/finalização já detectada pelo ETS2: não ressuscitar a viagem como ativa.
            // Se uma viagem ficou aberta por falha de sincronização, tenta liquidá-la agora.
            if (data.JobDelivered || data.JobFinished)
            {
                if (string.IsNullOrWhiteSpace(_localTripId) && LocalData.Current is { } localStore)
                    _localTripId = new LocalTripRepository(localStore.Db).FindActiveTripIdByServerId(tripId, SecureTokenStore.ReadUserId() ?? "");

                // Se existe a contraparte local, nunca liquidamos este encerramento pelo
                // atalho antigo do servidor. Reconstituímos a sessão e usamos exatamente
                // o mesmo pipeline idempotente/snapshotado de FinishAutomaticTrip.
                if (!string.IsNullOrWhiteSpace(_localTripId))
                {
                    _serverTripId = tripId;
                    _tripActive = true;
                    _tripStartedAtUtc = ReadDateTime(tripElement, "started_at") ?? _tripStartedAtUtc;
                    var startOdo = (float)ReadNumber(tripElement, "start_odometer_km");
                    var startFuel = (float)ReadNumber(tripElement, "start_fuel_l");
                    if (_tripStartOdometer <= 0 && startOdo > 0) _tripStartOdometer = startOdo;
                    if (_tripStartFuel <= 0 && startFuel > 0) _tripStartFuel = startFuel;
                    await FinishAutomaticTrip(data);
                    return;
                }

                // Sem viagem local não existe TripSession que possa ser liquidada no
                // banco TransPoli. Apenas sincronizamos o contrato remoto, sem criar
                // economia local nem reutilizar identidade de outra sessão.
                // Sem identidade local não existe operação idempotente que possamos
                // liquidar com segurança. Não fabricamos uma TripSession nem fazemos
                // fechamento HTTP avulso; preservamos o contrato remoto para reconciliação.
                StatusText.Text = "TransPoli • entrega remota encontrada sem TripSession local • reconciliação preservada";
                return;
            }
            _serverTripId = tripId;
            if (LocalData.Current is { } recoveryStore)
            {
                var recoveredLocalTripId = new LocalTripRepository(recoveryStore.Db).FindActiveTripIdByServerId(tripId, ownerUserId);
                if (!string.IsNullOrWhiteSpace(recoveredLocalTripId))
                {
                    _localTripId = recoveredLocalTripId;
                    _operationTripId = recoveredLocalTripId;
                }
            }

            var recoveredDocument = _documents
                .Where(x => string.Equals(x.TripId, tripId, StringComparison.OrdinalIgnoreCase)
                         || (!string.IsNullOrWhiteSpace(_operationTripId)
                             && string.Equals(x.TripId, _operationTripId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.RecordedAtUtc)
                .FirstOrDefault();
            if (recoveredDocument is not null)
            {
                _operationInvoiceId = recoveredDocument.Id;
                if (string.IsNullOrWhiteSpace(_operationTripId) && !string.IsNullOrWhiteSpace(recoveredDocument.TripId))
                    _operationTripId = recoveredDocument.TripId;
            }

            _tripActive = true;
            _tripStartedAtUtc = ReadDateTime(tripElement, "started_at") ?? DateTime.UtcNow;

            var serverStartOdometer = ReadNumber(tripElement, "start_odometer_km");
            var serverStartFuel = ReadNumber(tripElement, "start_fuel_l");
            var serverPlannedDistance = ReadNumber(tripElement, "planned_distance_km");
            if (serverPlannedDistance > 0) _tripPlannedDistanceKm = (float)serverPlannedDistance;

            // Prioridade: marco salvo localmente > marco registrado no servidor > amostra atual.
            // Uma reconexão do ETS2 nunca transforma o progresso já percorrido em 0%.
            if (_tripStartOdometer <= 0)
                _tripStartOdometer = serverStartOdometer > 0 ? (float)serverStartOdometer : data.OdometerKm;
            if (_tripStartFuel <= 0)
                _tripStartFuel = serverStartFuel > 0 ? (float)serverStartFuel : data.FuelLiters;

            await RestoreTripBaseline(tripId, token, data);
            TripStatusText.Text = "VIAGEM RECUPERADA AUTOMATICAMENTE"; TripRouteText.Text = BuildRoute(data); TripCargoText.Text = string.IsNullOrWhiteSpace(data.Cargo) ? "Carga não informada" : $"Carga: {data.Cargo}";
            var distance = Math.Max(0f, data.OdometerKm - _tripStartOdometer);
            var planned = _tripPlannedDistanceKm > 0 ? _tripPlannedDistanceKm : (tripElement.TryGetProperty("planned_distance_km", out var plannedElement) ? (float)ReadNumber(tripElement, "planned_distance_km") : data.PlannedDistanceKm);
            var progress = planned > 0 ? Math.Clamp(distance / planned, 0f, 1f) : 0f;
            var remaining = planned > 0 ? Math.Max(0f, planned - distance) : 0f;
            TripDistanceText.Text = distance > 0.1f ? $"{distance:0.0} km" : "Em andamento";
            TripProgressText.Text = planned > 0 ? $"{progress * 100:0}%" : "—";
            TripProgressText2.Text = TripProgressText.Text;
            TripDistanceLiveText.Text = planned > 0 ? $"{distance:0.0} / {planned:0} km" : "— / — km";
            TripDistanceLiveText2.Text = TripDistanceLiveText.Text;
            TripRemainingText.Text = planned > 0 ? $"{remaining:0.0} km restantes" : "distância restante indisponível";
            TripRemainingText2.Text = TripRemainingText.Text;
            TripDurationText.Text = FormatDuration(DateTime.UtcNow - _tripStartedAtUtc);
            StatusText.Text = "ETS2 conectado • viagem recuperada após reinício";
            if (!TrySaveSessionState())
            {
                _truckLocked = true;
                StatusText.Text = "TransPoli • viagem recuperada, mas a TripSession não pôde ser persistida";
                return;
            }
            _tripLifecycle.Observe(data, _tripActive, _tripDocumentPending);
            // A sessão recuperada já será incluída no próximo checkpoint normal.
            // Recovery não cria um upload extra apenas por reconstruir memória/UI.
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("TripRecovery.ActiveTrip", ex);
            _truckLocked = true;
            StatusText.Text = $"TransPoli • recuperação preservada • {ex.GetType().Name}";
        }
        finally { _recoveryBusy = false; }
    }

    private async Task RestoreTripBaseline(string tripId, string token, TelemetrySnapshot current)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}/me/trips/{tripId}/telemetry");
            request.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (_tripStartOdometer <= 0 &&
                document.RootElement.TryGetProperty("telemetryStartOdometer", out var startOdo) &&
                startOdo.ValueKind == JsonValueKind.Number &&
                startOdo.TryGetSingle(out var serverStartOdo) &&
                serverStartOdo > 0)
                _tripStartOdometer = serverStartOdo;

            if (_tripStartFuel <= 0 &&
                document.RootElement.TryGetProperty("telemetryStartFuel", out var startFuel) &&
                startFuel.ValueKind == JsonValueKind.Number &&
                startFuel.TryGetSingle(out var serverFuel) &&
                serverFuel > 0)
                _tripStartFuel = serverFuel;

            if (_tripStartOdometer <= 0) _tripStartOdometer = current.OdometerKm;
            if (_tripStartFuel <= 0) _tripStartFuel = current.FuelLiters;
        }
        catch (Exception ex)
        {
            App.WriteUiCrashLog("TripRecovery.RestoreBaseline", ex);
            if (_tripStartOdometer <= 0) _tripStartOdometer = current.OdometerKm;
            if (_tripStartFuel <= 0) _tripStartFuel = current.FuelLiters;
        }
    }

    private static double ReadNumber(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return double.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
    private static bool IsOpenTrip(JsonElement trip)
    {
        if (trip.TryGetProperty("finished_at", out var finished) && finished.ValueKind != JsonValueKind.Null && finished.ValueKind != JsonValueKind.Undefined) return false;
        if (trip.TryGetProperty("status", out var status)) { var value = status.GetString(); if (!string.IsNullOrWhiteSpace(value) && !value.Equals("active", StringComparison.OrdinalIgnoreCase)) return false; }
        return true;
    }
    private static bool TripMatchesTelemetry(JsonElement trip, TelemetrySnapshot data)
    {
        var cargo = ReadString(trip, "cargo"); var destination = ReadString(trip, "destination"); var origin = ReadString(trip, "origin");
        if (!string.IsNullOrWhiteSpace(cargo) && !string.IsNullOrWhiteSpace(data.Cargo) && !Same(cargo, data.Cargo)) return false;
        if (!string.IsNullOrWhiteSpace(destination) && !string.IsNullOrWhiteSpace(data.DestinationCity) && !Same(destination, data.DestinationCity)) return false;
        if (!string.IsNullOrWhiteSpace(origin) && !string.IsNullOrWhiteSpace(data.SourceCity) && !Same(origin, data.SourceCity)) return false;
        return true;
    }
    private static string? ReadString(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTime? ReadDateTime(JsonElement element, string property) { var value = ReadString(element, property); return DateTime.TryParse(value, out var result) ? result.ToUniversalTime() : null; }
    private static bool Same(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
