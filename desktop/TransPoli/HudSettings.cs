using System;
using System.IO;
using System.Text.Json;

namespace TransPoli;

public sealed class HudSettings
{
    public bool Enabled { get; set; } = true;
    public bool ShowSpeed { get; set; } = true;
    public bool ShowOdometer { get; set; } = true;
    public bool ShowTripKm { get; set; } = true;
    public bool ShowRoute { get; set; } = true;
    public bool ShowCompanies { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool ShowCargo { get; set; } = true;
    public bool ShowProfit { get; set; } = true;
    public bool ShowExpenses { get; set; } = true;
    public string Position { get; set; } = "Centro superior";
    public double Opacity { get; set; } = 0.85;
    public double Scale { get; set; } = 1.0;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli", "hud-settings.json");
    public static HudSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(FilePath)) ?? new HudSettings(); } catch { }
        return new HudSettings();
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
}