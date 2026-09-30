using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace TransPoli.Media;

public sealed class OnlineMediaSettings
{
    public string SpotifyClientId { get; set; } = "";
    public string SpotifyRedirectUri { get; set; } = "http://127.0.0.1:43821/spotify/callback/";
    public string ProtectedYouTubeApiKey { get; set; } = "";
    public string ProtectedSpotifyRefreshToken { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string YouTubeApiKey { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string SpotifyRefreshToken { get; set; } = "";

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
            var settings = JsonSerializer.Deserialize<OnlineMediaSettings>(File.ReadAllText(FilePath)) ?? new();
            settings.YouTubeApiKey = Unprotect(settings.ProtectedYouTubeApiKey);
            settings.SpotifyRefreshToken = Unprotect(settings.ProtectedSpotifyRefreshToken);
            return settings;
        }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        ProtectedYouTubeApiKey = Protect(YouTubeApiKey);
        ProtectedSpotifyRefreshToken = Protect(SpotifyRefreshToken);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
        }
        catch { return ""; }
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
