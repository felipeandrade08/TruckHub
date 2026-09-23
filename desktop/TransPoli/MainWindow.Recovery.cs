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
        var closures=new LocalTripClosureRepository(store.Db);
        var pending=closures.GetPending();
        if(pending.Count==0) return false;
        foreach(var item in pending)
        {
            try
            {
                var trips=new LocalTripRepository(store.Db);
                var distance=Math.Max(0d,data.OdometerKm-item.StartOdometer);
                var fuelUsed=Math.Max(0d,item.StartFuel-data.FuelLiters);
                var gross=JourneyEconomyCalculator.CalculateGross(distance,JourneyEconomyCalculator.SanitizeRate(item.RatePerKm));
                if(!item.LocalSettled)
                {
                    trips.FinishTrip(item.TripId,data,distance,fuelUsed,gross,string.IsNullOrWhiteSpace(item.Reason)?"recovery_fechamento":item.Reason);
                    closures.Mark(item.TripId,"local_settled_at_utc");
                }
                if(!item.HealthCaptured)
                {
                    trips.AppendTruckHealth(item.TruckId,item.TripId,data);
                    closures.Mark(item.TripId,"health_captured_at_utc");
                }
                if(!item.TachographClosed)
                {
                    ArchiveCurrentTachograph();
                    closures.Mark(item.TripId,"tachograph_closed_at_utc");
                }
                if(!item.RemoteQueued)
                {
                    if(!string.IsNullOrWhiteSpace(item.ServerId))
                        await FinishServerTrip(item.ServerId,item.TripId,(float)distance,(float)fuelUsed,data);
                    else
                        _serverSync.QueueTripFinish(item.TripId,new { distanceKm=distance,fuelUsedL=fuelUsed,cargoDamage=Math.Clamp(data.CargoDamage,0f,1f),cargoMassKg=Math.Max(0f,data.CargoMassKg) });
                    closures.Mark(item.TripId,"remote_queued_at_utc");
                }
                trips.RefreshFinancialSummary(item.TripId);
                _tripLifecycle.ApplyFinancialSummary(trips.GetFinancialSummary(item.TripId));
                new LocalTripLogbookRepository(store.Db).Consolidate(item.TripId, _tripLifecycle.Current.SessionKey);
                _tripLifecycle.MarkFinished(data,"Fechamento recuperado após reinicialização.");
                closures.Complete(item.TripId);
                if(string.Equals(_localTripId,item.TripId,StringComparison.OrdinalIgnoreCase)) ClearSessionState();
                StatusText.Text="TransPoli • fechamento pendente recuperado e concluído";
            }
            catch(Exception ex)
            {
                closures.Fail(item.TripId,ex.Message);
                StatusText.Text="TransPoli • fechamento pendente preservado para nova tentativa";
                return true;
            }
        }
        return true;
    }

    private async Task TryRecoverActiveTrip()
    {
        if (_recoveryBusy || DateTime.UtcNow - _lastRecoveryAtUtc < TimeSpan.FromSeconds(15)) return;
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) return;

        _recoveryBusy = true;
        _lastRecoveryAtUtc = DateTime.UtcNow;
        try
        {
            using var telemetryResponse = await _http.GetAsync(TelemetryUrl);
            if (!telemetryResponse.IsSuccessStatusCode) return;
            await using var telemetryStream = await telemetryResponse.Content.ReadAsStreamAsync();
            var data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(telemetryStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data is null || !data.Connected) return;

            if (await ResumePendingTripClosuresAsync(data)) return;

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
                    // Só recuperamos um contrato do servidor quando o ETS2 confirma
                    // que existe uma carga/trabalho ativo. Isso impede que uma viagem
                    // antiga deixada como active no servidor seja ressuscitada na tela.
                    activeTrip = trip;
                    break;
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
                        ClearSessionState();
                        _tripStartedAtUtc = DateTime.UtcNow;
                        _tripStartOdometer = data.OdometerKm;
                        _tripStartFuel = data.FuelLiters;
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
                    _localTripId = new LocalTripRepository(localStore.Db).FindActiveTripIdByServerId(tripId);
                var startOdo = (float)ReadNumber(tripElement, "start_odometer_km");
                var startFuel = (float)ReadNumber(tripElement, "start_fuel_l");
                var completedDistance = Math.Max(0f, data.OdometerKm - startOdo);
                var fuelUsed = Math.Max(0f, startFuel - data.FuelLiters);
                await FinishServerTrip(tripId, null, completedDistance, fuelUsed, data);
                _tripLifecycle.MarkFinished(data, "Entrega detectada durante recuperação da sessão.");
                return;
            }
            _serverTripId = tripId;
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
            SaveSessionState();
            _tripLifecycle.Observe(data, _tripActive, _tripDocumentPending);
            await SendTelemetrySample(data, true);
        }
        catch { }
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
        catch
        {
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
