using System;
using System.Data;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

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
        if(value.ValueKind==JsonValueKind.Array && value.GetArrayLength()>0){
            foreach(var p in value[0].EnumerateObject()) table.Columns.Add(p.Name,typeof(string));
            foreach(var item in value.EnumerateArray()){
                var row=table.NewRow();
                foreach(var p in item.EnumerateObject()) row[p.Name]=p.Value.ValueKind==JsonValueKind.Null?"":p.Value.ToString();
                table.Rows.Add(row);
            }
        }
        grid.ItemsSource=table.DefaultView;
    }
}