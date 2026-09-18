using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    private static readonly DispatcherTimer _tripProgressTimer = CreateTripProgressTimer();
    private static DispatcherTimer CreateTripProgressTimer(){var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};timer.Tick+=async(_,_)=>{if(Application.Current?.MainWindow is MainWindow window)await window.RefreshTripProgressAsync();};timer.Start();return timer;}

    private async Task RefreshTripProgressAsync()
    {
        try
        {
            using var response=await _http.GetAsync(TelemetryUrl);if(!response.IsSuccessStatusCode)return;
            await using var stream=await response.Content.ReadAsStreamAsync();var data=await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});if(data is null||!data.Connected)return;
            UpdateTripRouteHeader(data);
            if(!_tripActive&&HasActiveJob(data)&&data.CargoLoaded)await RecoverTripForProgressAsync(data);
            if(!_tripActive){ResetTripProgressUi();return;}
            var elapsed=DateTime.UtcNow-_tripStartedAtUtc;var distance=Math.Max(0f,data.OdometerKm-_tripStartOdometer);var planned=data.PlannedDistanceKm>0?data.PlannedDistanceKm:data.RouteDistanceKm>0?distance+data.RouteDistanceKm:0;var remaining=data.RouteDistanceKm>0?data.RouteDistanceKm:planned>0?Math.Max(0f,planned-distance):0;var progress=planned>0?Math.Clamp(distance/planned,0f,1f):0;
            TripProgressText.Text=planned>0?$"{progress*100:0}%":"—";TripDistanceLiveText.Text=planned>0?$"{distance:0.0} / {planned:0} km":"— / — km";TripRemainingText.Text=planned>0||remaining>0?$"{remaining:0.0} km restantes":"distância restante indisponível";TripStartText.Text=_tripStartedAtUtc.ToLocalTime().ToString("HH:mm");TripDrivingTimeText.Text=FormatDuration(elapsed);
            TripLiveText.Text=data.GamePaused?"JOGO PAUSADO":data.RefuelActive?"ABASTECENDO":Math.Abs(data.SpeedKph)<0.5f?"VEÍCULO PARADO":"EM CONDUÇÃO";
            TripLiveText.Foreground=data.GamePaused?(System.Windows.Media.Brush)FindResource("GoldBright"):data.RefuelActive?(System.Windows.Media.Brush)FindResource("GoldBright"):Math.Abs(data.SpeedKph)<0.5f?(System.Windows.Media.Brush)FindResource("TextMuted"):(System.Windows.Media.Brush)FindResource("Green");
            if(TripProgressFill.Parent is Grid progressGrid&&progressGrid.ActualWidth>0){TripProgressFill.Width=progressGrid.ActualWidth*progress;TripTruckText.Margin=new Thickness(Math.Max(-10,TripProgressFill.Width-10),0,0,0);}
            var averageSpeed=elapsed.TotalHours>0.008&&distance>0.5f?distance/(float)elapsed.TotalHours:Math.Abs(data.SpeedKph);
            if(remaining<=0.1f&&planned>0){TripArrivalText.Text="Destino alcançado";TripEtaText.Text="0 min";TripEstimateNoteText.Text="Distância planejada concluída.";return;}
            if(averageSpeed<5f||remaining<=0){TripArrivalText.Text="Calculando…";TripEtaText.Text=averageSpeed<5f?"aguardando movimento":"—";TripEstimateNoteText.Text="Aguardando movimento real para estabilizar a estimativa.";return;}
            var etaSeconds=remaining/averageSpeed*3600d;var arrival=DateTime.UtcNow.AddSeconds(etaSeconds).ToLocalTime();TripArrivalText.Text=$"{arrival:HH:mm} • {arrival:dd/MM}";TripEtaText.Text=FormatTripEta(etaSeconds);TripEstimateNoteText.Text=$"ETA real: média de {averageSpeed:0.0} km/h.";
        }catch{}
    }

    private void UpdateTripRouteHeader(TelemetrySnapshot data)
    {
        TripOriginText.Text=string.IsNullOrWhiteSpace(data.SourceCity)?"Origem não informada":data.SourceCity;
        TripDestinationText.Text=string.IsNullOrWhiteSpace(data.DestinationCity)?"Destino não informado":data.DestinationCity;
        TripOriginCompanyText.Text=string.IsNullOrWhiteSpace(data.SourceCompany)?"Empresa de origem —":data.SourceCompany;
        TripDestinationCompanyText.Text=string.IsNullOrWhiteSpace(data.DestinationCompany)?"Empresa de destino —":data.DestinationCompany;
        TripCargoText.Text=string.IsNullOrWhiteSpace(data.Cargo)?"Nenhuma carga ativa":data.Cargo;
        TripValueText.Text=data.CargoValueBrl.HasValue?$"R$ {data.CargoValueBrl.Value:N0}":"—";
        if(!_tripActive&&HasActiveJob(data)&&data.CargoLoaded)
        {
            TripLiveText.Text="PRONTO PARA SAÍDA";
            TripLiveText.Foreground=(System.Windows.Media.Brush)FindResource("GoldBright");
        }
    }

    private async Task RecoverTripForProgressAsync(TelemetrySnapshot data)
    {
        try
        {
            var token=SecureTokenStore.Read();if(string.IsNullOrWhiteSpace(token))return;
            using var request=new HttpRequestMessage(HttpMethod.Get,$"{ApiBaseUrl}/me/trips");request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");request.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");using var response=await _http.SendAsync(request);if(!response.IsSuccessStatusCode)return;
            using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());if(!doc.RootElement.TryGetProperty("trips",out var trips))return;
            foreach(var trip in trips.EnumerateArray())
            {
                if(!trip.TryGetProperty("status",out var status)||!string.Equals(status.GetString(),"active",StringComparison.OrdinalIgnoreCase))continue;
                var cargo=trip.TryGetProperty("cargo",out var c)?c.GetString():null;if(!string.IsNullOrWhiteSpace(data.Cargo)&&!string.Equals(cargo,data.Cargo,StringComparison.OrdinalIgnoreCase))continue;
                if(!trip.TryGetProperty("id",out var idEl))continue;var id=idEl.GetString();if(string.IsNullOrWhiteSpace(id))continue;
                _serverTripId=id;_tripActive=true;_tripStartedAtUtc=trip.TryGetProperty("started_at",out var st)&&DateTime.TryParse(st.GetString(),null,System.Globalization.DateTimeStyles.AdjustToUniversal,out var parsed)?parsed.ToUniversalTime():DateTime.UtcNow;
                var distance=0f;
                try{using var pReq=new HttpRequestMessage(HttpMethod.Get,$"{ApiBaseUrl}/me/trips/{id}/economy-preview");pReq.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");pReq.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");using var pResp=await _http.SendAsync(pReq);if(pResp.IsSuccessStatusCode){using var pDoc=JsonDocument.Parse(await pResp.Content.ReadAsStringAsync());if(pDoc.RootElement.TryGetProperty("preview",out var p)&&p.TryGetProperty("distanceKm",out var d))distance=Math.Max(0,d.GetSingle());}}catch{}
                _tripStartOdometer=Math.Max(0,data.OdometerKm-distance);_tripStartFuel=data.FuelLiters;_jobMissingTicks=0;TripStatusText.Text="VIAGEM EM ANDAMENTO • TELEMETRIA RECUPERADA";TripRouteText.Text=BuildRoute(data);TripCargoText.Text=string.IsNullOrWhiteSpace(data.Cargo)?"Carga não informada":$"Carga: {data.Cargo}";return;
            }
        }catch{}
    }

    private void ResetTripProgressUi(){TripProgressText.Text="0%";TripProgressFill.Width=0;TripTruckText.Margin=new Thickness(-9,0,0,0);TripDistanceLiveText.Text="0 / 0 km";TripRemainingText.Text="— km restantes";TripStartText.Text="—";TripArrivalText.Text="Aguardando saída";TripEtaText.Text="—";TripEstimateNoteText.Text="A estimativa será calculada assim que a viagem começar.";TripDrivingTimeText.Text="00:00:00";TripLiveText.Text="AGUARDANDO";TripLiveText.Foreground=(System.Windows.Media.Brush)FindResource("TextMuted");TripValueText.Text="—";}
    private static string FormatTripEta(double seconds){var totalMinutes=Math.Max(0,(int)Math.Round(seconds/60d));var hours=totalMinutes/60;var minutes=totalMinutes%60;return hours>0?$"{hours}h {minutes:00}min":$"{minutes}min";}
}
