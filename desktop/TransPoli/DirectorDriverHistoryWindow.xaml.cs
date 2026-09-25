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
            SetGrid(TripsGrid,doc.RootElement.GetProperty("trips"));
            SetGrid(EventsGrid,doc.RootElement.GetProperty("events"));
            StatusText.Text="Histórico operacional real da TransPoli.";
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
        if(name=="status")return raw.ToLowerInvariant() switch{"active"=>"ATIVO","finished"=>"CONCLUÍDA","cancelled"=>"CANCELADA","blocked"=>"BLOQUEADO",_=>raw.ToUpperInvariant()};
        return raw;
    }
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
    private void DragWindow(object sender,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed)DragMove();}

}