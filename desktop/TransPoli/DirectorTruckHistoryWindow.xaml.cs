using System;
using System.Data;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TransPoli;

public partial class DirectorTruckHistoryWindow : Window
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _token;
    private readonly string _truckId;

    public DirectorTruckHistoryWindow(string token, string truckId, string truckName)
    {
        InitializeComponent();
        _token = token;
        _truckId = truckId;
        TitleText.Text = string.IsNullOrWhiteSpace(truckName) ? "Histórico do caminhão" : truckName;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + "/director/trucks/" + _truckId + "/history");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) { MessageBox.Show(ApiMessage(json, "Não foi possível carregar o histórico."), "TransPoli", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if(root.TryGetProperty("truck",out var truck))
            {
                DriverText.Text=JsonString(truck,"driver","—");
                OdometerText.Text=$"{JsonNumber(truck,"current_odometer_km"):N1} km";
                WearText.Text=$"{JsonNumber(truck,"wear_pct"):N1}%";
                StateText.Text=JsonString(truck,"operational_state","normal").ToUpperInvariant();
            }
            if(root.TryGetProperty("trips",out var trips)) SetGrid(TripsGrid,trips,new[]{("Carga","cargo"),("Origem","origin"),("Destino","destination"),("Início","started_at"),("Fim","finished_at"),("KM","distance_km"),("Combustível","fuel_used_l"),("Valor","cargo_value_brl"),("Status","status")});
            if(root.TryGetProperty("maintenance",out var maintenance))
            {
                SetGrid(MaintenanceGrid,maintenance,new[]{("Serviço","service_type"),("Componente","component"),("Descrição","description"),("Custo","cost_brl"),("Odômetro","odometer_km"),("Data","created_at")});
                var count=maintenance.ValueKind==JsonValueKind.Array?maintenance.GetArrayLength():0;
                MaintenanceText.Text=count==0?"Nenhuma manutenção registrada para este caminhão.":$"{count} registro(s) de manutenção encontrados. A última manutenção é atualizada automaticamente pela operação da frota.";
            }
        }
        catch(Exception ex)
        {
            App.WriteUiCrashLog("DirectorTruckHistoryWindow.Load",ex);
            MessageBox.Show("Não foi possível carregar o histórico do caminhão.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }

    private static void SetGrid(System.Windows.Controls.DataGrid grid, JsonElement value, (string Header,string Property)[] columns)
    {
        var table=new DataTable();
        foreach(var col in columns)table.Columns.Add(col.Header,typeof(string));
        if(value.ValueKind==JsonValueKind.Array) foreach(var item in value.EnumerateArray())
        {
            var row=table.NewRow();
            for(var i=0;i<columns.Length;i++) row[i]=item.ValueKind==JsonValueKind.Object&&item.TryGetProperty(columns[i].Property,out var p)?p.ToString():"";
            table.Rows.Add(row);
        }
        grid.ItemsSource=table.DefaultView;
    }

    private static double JsonNumber(JsonElement value,string property)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(property,out var p)&&TryNumber(p,out var n)?n:0;
    private static bool TryNumber(JsonElement v,out double n){if(v.ValueKind==JsonValueKind.Number)return v.TryGetDouble(out n);if(v.ValueKind==JsonValueKind.String)return double.TryParse(v.GetString(),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out n)||double.TryParse(v.GetString(),out n);n=0;return false;}
    private static string JsonString(JsonElement value,string property,string fallback)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(property,out var p)&&p.ValueKind!=JsonValueKind.Null?p.GetString()??fallback:fallback;
    private static string ApiMessage(string json,string fallback){try{using var doc=JsonDocument.Parse(json);return doc.RootElement.TryGetProperty("error",out var p)?p.GetString()??fallback:fallback;}catch{return fallback;}}
    private void Close_Click(object sender,RoutedEventArgs e)=>Close();
}