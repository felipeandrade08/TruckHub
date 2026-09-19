        if(!_tripActive && HasActiveJob(data))
        {
            if(!string.IsNullOrWhiteSpace(data.SourceCity)) _tripRouteOrigin=data.SourceCity;
            if(!string.IsNullOrWhiteSpace(data.DestinationCity)) _tripRouteDestination=data.DestinationCity;
            if(!string.IsNullOrWhiteSpace(data.SourceCompany)) _tripRouteOriginCompany=data.SourceCompany;
            if(!string.IsNullOrWhiteSpace(data.DestinationCompany)) _tripRouteDestinationCompany=data.DestinationCompany;
            if(!string.IsNullOrWhiteSpace(data.Cargo))
        {
            _tripCargo=data.Cargo;
            _ = DiscoverCargoMarketAsync(data.Cargo);
        }
            if(data.CargoValueBrl.HasValue) _tripCargoValue=data.CargoValueBrl;
        }
        if(!string.IsNullOrWhiteSpace(data.SourceCity)) _tripRouteOrigin=data.SourceCity;
        if(!string.IsNullOrWhiteSpace(data.DestinationCity)) _tripRouteDestination=data.DestinationCity;
        if(!string.IsNullOrWhiteSpace(data.SourceCompany)) _tripRouteOriginCompany=data.SourceCompany;
        if(!string.IsNullOrWhiteSpace(data.DestinationCompany)) _tripRouteDestinationCompany=data.DestinationCompany;
        if(!string.IsNullOrWhiteSpace(data.Cargo)) _tripCargo=data.Cargo;
        if(data.CargoValueBrl.HasValue) _tripCargoValue=data.CargoValueBrl;
        TripOriginText.Text=string.IsNullOrWhiteSpace(_tripRouteOrigin)?"Origem não informada":_tripRouteOrigin;
        TripDestinationText.Text=string.IsNullOrWhiteSpace(_tripRouteDestination)?"Destino não informado":_tripRouteDestination;
        TripOriginCompanyText.Text=string.IsNullOrWhiteSpace(_tripRouteOriginCompany)?"Empresa de origem —":_tripRouteOriginCompany;
        TripDestinationCompanyText.Text=string.IsNullOrWhiteSpace(_tripRouteDestinationCompany)?"Empresa de destino —":_tripRouteDestinationCompany;
        TripCargoText.Text=string.IsNullOrWhiteSpace(_tripCargo)?"Nenhuma carga ativa":_tripCargo;
        TripValueText.Text=_tripCargoValue.HasValue?$"R$ {_tripCargoValue.Value:N0}":"—";
        if(!_tripActive&&HasActiveJob(data)&&data.CargoLoaded)
        {
            TripLiveText.Text = "MONITORAMENTO ATIVO";
            TripLiveText.Foreground = FindResource("Green") as System.Windows.Media.Brush;
        }
    }

    private float GetTripPlannedDistanceKm(TelemetrySnapshot data,float distance)
    {
        if(_tripPlannedDistanceKm>0) return _tripPlannedDistanceKm;
        if(data.PlannedDistanceKm>0) _tripPlannedDistanceKm=data.PlannedDistanceKm;
        else if(data.RouteDistanceKm>0) _tripPlannedDistanceKm=Math.Max(1f,distance+data.RouteDistanceKm);
        return _tripPlannedDistanceKm;
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