using System.Text.Json;

namespace TransPoli.Media;

public sealed class OnlineMediaSettings
{
    public string SpotifyClientId { get; set; } = "";
    public string SpotifyRedirectUri { get; set; } = "http://127.0.0.1:43821/spotify/callback/";
    public string YouTubeApiKey { get; set; } = "";

    public bool SpotifyConfigured => !string.IsNullOrWhiteSpace(SpotifyClientId);
    public bool YouTubeConfigured => !string.IsNullOrWhiteSpace(YouTubeApiKey);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransPoli", "online-media-settings.json");

    public static OnlineMediaSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<OnlineMediaSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed record OnlineMediaSearchResult(
    string Provider,
    string Id,
    string Title,
    string Artist,
    string Artwork,
    string PlaybackReference);

public interface IOnlineMediaProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    bool CanApplyCabinDsp { get; }
    Task<IReadOnlyList<OnlineMediaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}
