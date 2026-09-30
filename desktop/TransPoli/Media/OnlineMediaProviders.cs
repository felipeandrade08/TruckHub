using System.Net.Http.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TransPoli.Media;

public sealed class YouTubeMediaProvider : IOnlineMediaProvider
{
    private const string ApiBaseUrl = "https://truckhub.felipe-pessoall2026.workers.dev";
    private readonly HttpClient _http;
    private readonly Dictionary<string,(DateTime At,IReadOnlyList<OnlineMediaSearchResult> Items)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public YouTubeMediaProvider(HttpClient http, OnlineMediaSettings settings) { _http=http; }
    public string Name => "YOUTUBE";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecureTokenStore.Read());
    public bool CanApplyCabinDsp => false;

    public async Task<IReadOnlyList<OnlineMediaSearchResult>> SearchAsync(string query, CancellationToken cancellationToken=default)
    {
        query=(query??"").Trim();
        if (query.Length<2) return Array.Empty<OnlineMediaSearchResult>();
        if (_cache.TryGetValue(query,out var hit) && DateTime.UtcNow-hit.At<TimeSpan.FromMinutes(15)) return hit.Items;
        var token=SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Entre na sua conta TransPoli para pesquisar no YouTube.");
        using var request=new HttpRequestMessage(HttpMethod.Get,ApiBaseUrl+"/me/media/youtube/search?q="+Uri.EscapeDataString(query));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await _http.SendAsync(request,cancellationToken);
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if(!response.IsSuccessStatusCode)
        {
            var message=doc.RootElement.TryGetProperty("error",out var error)?error.GetString():null;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)?"Não foi possível pesquisar no YouTube agora.":message);
        }
        var list=new List<OnlineMediaSearchResult>();
        if(doc.RootElement.TryGetProperty("items",out var items)&&items.ValueKind==JsonValueKind.Array)
        foreach(var item in items.EnumerateArray())
        {
            string Read(string name)=>item.TryGetProperty(name,out var value)?value.GetString()??"":"";
            var id=Read("id"); if(string.IsNullOrWhiteSpace(id))continue;
            var duration=item.TryGetProperty("durationSeconds",out var durationValue)&&durationValue.TryGetDouble(out var seconds)?seconds:0;
            list.Add(new("YOUTUBE",id,System.Net.WebUtility.HtmlDecode(Read("title")),System.Net.WebUtility.HtmlDecode(Read("artist")),Read("artwork"),Read("playbackId"),duration));
        }
        _cache[query]=(DateTime.UtcNow,list);
        return list;
    }
}

public sealed class SpotifyMediaProvider : IOnlineMediaProvider
{
    private readonly HttpClient _http;
    private readonly OnlineMediaSettings _settings;
    private string _accessToken="";

    public SpotifyMediaProvider(HttpClient http, OnlineMediaSettings settings){_http=http;_settings=settings;}
    public string Name=>"SPOTIFY";
    public bool IsConfigured=>_settings.SpotifyConfigured;
    public bool CanApplyCabinDsp=>false;
    public bool IsAuthenticated=>!string.IsNullOrWhiteSpace(_accessToken);
    public void SetAccessToken(string token)=>_accessToken=token??"";

    public async Task PlayAsync(string uri,CancellationToken cancellationToken=default)
    {
        using var req=new HttpRequestMessage(HttpMethod.Put,"https://api.spotify.com/v1/me/player/play");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_accessToken);
        req.Content=JsonContent.Create(new { uris=new[]{uri} });
        using var response=await _http.SendAsync(req,cancellationToken);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("Nenhum dispositivo Spotify ativo.");
        response.EnsureSuccessStatusCode();
    }

    public Task ResumeAsync(CancellationToken cancellationToken=default)=>PlayerCommandAsync(HttpMethod.Put,"https://api.spotify.com/v1/me/player/play",cancellationToken);
    public Task PauseAsync(CancellationToken cancellationToken=default)=>PlayerCommandAsync(HttpMethod.Put,"https://api.spotify.com/v1/me/player/pause",cancellationToken);
    public Task NextAsync(CancellationToken cancellationToken=default)=>PlayerCommandAsync(HttpMethod.Post,"https://api.spotify.com/v1/me/player/next",cancellationToken);
    public Task PreviousAsync(CancellationToken cancellationToken=default)=>PlayerCommandAsync(HttpMethod.Post,"https://api.spotify.com/v1/me/player/previous",cancellationToken);

    public async Task SetVolumeAsync(int volume,CancellationToken cancellationToken=default)
        => await PlayerCommandAsync(HttpMethod.Put,"https://api.spotify.com/v1/me/player/volume?volume_percent="+Math.Clamp(volume,0,100),cancellationToken);

    public async Task<OnlineMediaSearchResult?> GetCurrentAsync(CancellationToken cancellationToken=default)
    {
        using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.spotify.com/v1/me/player");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_accessToken);
        using var response=await _http.SendAsync(req,cancellationToken);
        if(response.StatusCode==System.Net.HttpStatusCode.NoContent)return null;
        response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if(!doc.RootElement.TryGetProperty("item",out var track)||track.ValueKind==JsonValueKind.Null)return null;
        var artists=track.GetProperty("artists").EnumerateArray().Select(x=>x.GetProperty("name").GetString()).Where(x=>!string.IsNullOrWhiteSpace(x));
        var artwork=""; var images=track.GetProperty("album").GetProperty("images"); if(images.GetArrayLength()>0)artwork=images[0].GetProperty("url").GetString()??"";
        return new("SPOTIFY",track.GetProperty("id").GetString()??"",track.GetProperty("name").GetString()??"",string.Join(", ",artists),artwork,track.GetProperty("uri").GetString()??"");
    }

    private async Task PlayerCommandAsync(HttpMethod method,string url,CancellationToken cancellationToken)
    {
        using var req=new HttpRequestMessage(method,url);
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_accessToken);
        using var response=await _http.SendAsync(req,cancellationToken);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound) throw new InvalidOperationException("Nenhum dispositivo Spotify ativo.");
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<OnlineMediaSearchResult>> SearchAsync(string query,CancellationToken cancellationToken=default)
    {
        query=(query??"").Trim();
        if(!IsConfigured||!IsAuthenticated||query.Length<2) return Array.Empty<OnlineMediaSearchResult>();
        using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.spotify.com/v1/search?type=track&limit=8&q="+Uri.EscapeDataString(query));
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_accessToken);
        using var response=await _http.SendAsync(req,cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var list=new List<OnlineMediaSearchResult>();
        foreach(var track in doc.RootElement.GetProperty("tracks").GetProperty("items").EnumerateArray())
        {
            var artists=track.GetProperty("artists").EnumerateArray().Select(x=>x.GetProperty("name").GetString()).Where(x=>!string.IsNullOrWhiteSpace(x));
            var artwork="";
            var images=track.GetProperty("album").GetProperty("images");
            if(images.GetArrayLength()>0) artwork=images[0].GetProperty("url").GetString()??"";
            list.Add(new("SPOTIFY",track.GetProperty("id").GetString()??"",track.GetProperty("name").GetString()??"",string.Join(", ",artists),artwork,track.GetProperty("uri").GetString()??""));
        }
        return list;
    }
}
