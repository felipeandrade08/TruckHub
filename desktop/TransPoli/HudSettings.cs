using System;
using System.IO;
using System.Text.Json;

namespace TransPoli;

public sealed class HudSettings
{
    public bool Enabled { get; set; } = true;
    public bool ShowSpeed { get; set; } = true;
    public bool ShowRpm { get; set; } = true;
    public bool ShowRange { get; set; } = true;
    public bool ShowOdometer { get; set; } = true;
    public bool ShowTripKm { get; set; } = true;
    public bool ShowRoute { get; set; } = true;
    public bool ShowCompanies { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool ShowCargo { get; set; } = true;
    public bool ShowProfit { get; set; } = true;
    public bool ShowExpenses { get; set; } = true;
    public bool ShowTripState { get; set; } = true;
    public bool ShowEta { get; set; } = true;
    public bool ShowFuel { get; set; } = true;
    public bool ShowGear { get; set; } = true;
    public bool ShowAlerts { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public string LayoutMode { get; set; } = "Completa";
    public bool ShowConnection { get; set; } = true;
    public bool ShowScale { get; set; } = true;
    public string Position { get; set; } = "Topo";
    public double Opacity { get; set; } = 0.90;
    public double Scale { get; set; } = 1.0;
    // Posição livre normalizada (0..1) para funcionar em qualquer resolução.
    public bool UseCustomPosition { get; set; } = false;
    public double CustomX { get; set; } = 0.5;
    public double CustomY { get; set; } = 0.08;
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli", "hud-settings.json");
    private static string ProfileFilePath(string game) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli", $"hud-settings-{NormalizeGame(game).ToLowerInvariant()}.json");
    private static string NormalizeGame(string? game) => string.Equals(game, "ATS", StringComparison.OrdinalIgnoreCase) ? "ATS" : "ETS2";

    public static HudSettings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(FilePath)) ?? new HudSettings(); } catch { }
        return new HudSettings();
    }

    public static HudSettings LoadForGame(string game)
    {
        var profilePath = ProfileFilePath(game);
        try
        {
            if (File.Exists(profilePath))
                return JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(profilePath)) ?? new HudSettings();

            // Migra as preferências antigas para o primeiro perfil acessado,
            // preservando a configuração existente do usuário.
            if (File.Exists(FilePath))
            {
                var legacy = JsonSerializer.Deserialize<HudSettings>(File.ReadAllText(FilePath)) ?? new HudSettings();
                legacy.SaveForGame(game);
                return legacy;
            }
        }
        catch { }
        return new HudSettings();
    }

    public void SaveForGame(string game)
    {
        var profilePath = ProfileFilePath(game);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);
            File.WriteAllText(profilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }
}