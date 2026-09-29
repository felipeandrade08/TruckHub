using System.IO;
using System.Text.Json;

namespace TransPoli;

public sealed class MediaHotkeySettings
{
    public int FunctionKey { get; set; } = 12;
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransPoli", "media-hotkey.json");

    public static MediaHotkeySettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<MediaHotkeySettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }

    public uint VirtualKey => FunctionKey is >= 1 and <= 12 ? (uint)(0x6F + FunctionKey) : 0x7B;
    public uint Modifiers => (Ctrl ? 0x0002u : 0u) | (Alt ? 0x0001u : 0u) | (Shift ? 0x0004u : 0u);
    public string Display => $"{(Ctrl ? "Ctrl+" : "")}{(Alt ? "Alt+" : "")}{(Shift ? "Shift+" : "")}F{FunctionKey}";
}