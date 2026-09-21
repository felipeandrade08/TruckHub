using System;
using System.Windows;
using System.Windows.Controls;

namespace TransPoli;

public partial class DirectorTruckEditorWindow : Window
{
    public string SelectedUserId => (DriverBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    public string TruckName => TruckNameBox.Text.Trim();
    public string Brand => BrandBox.Text.Trim();
    public string Model => ModelBox.Text.Trim();
    public string LicensePlate => PlateBox.Text.Trim();

    public DirectorTruckEditorWindow(string? selectedUserId, string? truckName, string? brand, string? model, string? plate)
    {
        InitializeComponent();
        TruckNameBox.Text = truckName ?? "";
        BrandBox.Text = brand ?? "";
        ModelBox.Text = model ?? "";
        PlateBox.Text = plate ?? "";
        if (!string.IsNullOrWhiteSpace(selectedUserId)) DriverBox.SelectedValue = selectedUserId;
    }

    public void SetDrivers(System.Text.Json.JsonElement drivers)
    {
        DriverBox.Items.Clear();
        if (drivers.ValueKind != System.Text.Json.JsonValueKind.Array) return;
        foreach (var d in drivers.EnumerateArray())
        {
            var item = new ComboBoxItem { Content = $"{JsonString(d,"name","Motorista")} • {JsonString(d,"email","")}", Tag = JsonString(d,"id","") };
            DriverBox.Items.Add(item);
        }
        if (DriverBox.Items.Count > 0 && DriverBox.SelectedIndex < 0) DriverBox.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedUserId) || (string.IsNullOrWhiteSpace(TruckName) && string.IsNullOrWhiteSpace(Brand) && string.IsNullOrWhiteSpace(Model) && string.IsNullOrWhiteSpace(LicensePlate)))
        { MessageBox.Show("Selecione o motorista e informe os dados do caminhão.", "TransPoli", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private static string JsonString(System.Text.Json.JsonElement v,string p,string f)=>v.TryGetProperty(p,out var x)?x.ToString():f;
}
