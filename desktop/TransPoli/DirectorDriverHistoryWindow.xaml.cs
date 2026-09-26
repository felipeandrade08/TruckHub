using System;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace TransPoli;

public partial class DirectorDriverHistoryWindow : Window
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _token;
    private readonly string _id;

    public DirectorDriverHistoryWindow(string token,string id,string name)
    {
        InitializeComponent();
        _token=token; _id=id; TitleText.Text=$"Histórico • {name}";
        Loaded += async (_,_)=>await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,ApiBaseUrl+"/director/drivers/"+_id+"/history");
            req.Headers.TryAddWithoutValidation("Authorization","Bearer "+_token);
            using var res=await _http.SendAsync(req);
            var json=await res.Content.ReadAsStringAsync();
            if(!res.IsSuccessStatusCode){StatusText.Text="Não foi possível carregar o histórico.";return;}
            using var doc=JsonDocument.Parse(json);
            var trips=doc.RootElement.TryGetProperty("trips",out var tripItems)?tripItems:default;
            var events=doc.RootElement.TryGetProperty("events",out var eventItems)?eventItems:default;
            SetGrid(TripsGrid,trips);
            SetGrid(EventsGrid,events);
            var tripCount=trips.ValueKind==JsonValueKind.Array?trips.GetArrayLength():0;
            var eventCount=events.ValueKind==JsonValueKind.Array?events.GetArrayLength():0;
            StatusText.Text=tripCount==0&&eventCount==0?"Nenhum histórico operacional registrado para este motorista.":$"{tripCount} viagem(ns) • {eventCount} evento(s) registrados.";
        }catch(Exception ex){StatusText.Text="Erro ao carregar histórico.";App.WriteUiCrashLog("DirectorDriverHistory.Load",ex);}
    }

    private static void SetGrid(System.Windows.Controls.DataGrid grid,JsonElement value)
    {
        var table=new DataTable();
        if(value.ValueKind==JsonValueKind.Array && value.GetArrayLength()>0)
        {
            var props=value[0].EnumerateObject().Select(p=>p.Name).Where(p=>p is not "id" and not "user_id" and not "driver_id" and not "truck_id").ToArray();
            foreach(var name in props)table.Columns.Add(FriendlyHeader(name),typeof(string));
            foreach(var item in value.EnumerateArray())
            {
                var row=table.NewRow();
                for(var i=0;i<props.Length;i++) row[i]=item.TryGetProperty(props[i],out var v)?FormatValue(props[i],v):"";
                table.Rows.Add(row);
            }
        }
        grid.ItemsSource=table.DefaultView;
    }

    private static string FriendlyHeader(string name)=>name switch
    {
        "cargo"=>"Carga","origin"=>"Origem","destination"=>"Destino","started_at"=>"Início","finished_at"=>"Fim",
        "distance_km"=>"KM","fuel_used_l"=>"Combustível","status"=>"Status","event_type"=>"Evento","event_at"=>"Data",
        "created_at"=>"Data","truck_name"=>"Caminhão","amount"=>"Valor","description"=>"Descrição","type"=>"Tipo",
        _=>name.Replace("_"," ").ToUpperInvariant()
    };

    private static string FormatValue(string name,JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Null)return "";
        var raw=value.ToString();
        if(name.EndsWith("_at")&&DateTime.TryParse(raw,out var dt))return dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        if(name=="status")return raw.ToLowerInvariant() switch{"active"=>"ATIVO","finished"=>"CONCLUÍDA","cancelled"=>"CANCELADA","blocked"=>"BLOQUEADO","pending"=>"PENDENTE",_=>raw.ToUpperInvariant()};
        if(name=="amount"&&double.TryParse(raw,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var money))return $"R$ {money:N2}";
        if(name=="distance_km"&&double.TryParse(raw,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var km))return $"{km:N1} km";
        if(name=="fuel_used_l"&&double.TryParse(raw,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var fuel))return $"{fuel:N1} L";
        return raw;
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void DragWindow(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)DragMove();}

}