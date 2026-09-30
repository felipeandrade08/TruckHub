using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransPoli.Media;

public sealed class SpotifyPkceAuthenticator
{
    private readonly HttpClient _http;
    private readonly OnlineMediaSettings _settings;
    public SpotifyPkceAuthenticator(HttpClient http,OnlineMediaSettings settings){_http=http;_settings=settings;}

    public async Task<string> AuthenticateAsync(CancellationToken cancellationToken=default)
    {
        if(!_settings.SpotifyConfigured) throw new InvalidOperationException("Configure o Spotify Client ID primeiro.");
        var verifier=Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state=Base64Url(RandomNumberGenerator.GetBytes(24));
        var redirect=_settings.SpotifyRedirectUri;
        var scopes="user-read-playback-state user-modify-playback-state streaming";
        var auth="https://accounts.spotify.com/authorize?response_type=code&client_id="+Uri.EscapeDataString(_settings.SpotifyClientId)+
                 "&redirect_uri="+Uri.EscapeDataString(redirect)+"&scope="+Uri.EscapeDataString(scopes)+
                 "&code_challenge_method=S256&code_challenge="+Uri.EscapeDataString(challenge)+"&state="+Uri.EscapeDataString(state);
        using var listener=new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();
        Process.Start(new ProcessStartInfo(auth){UseShellExecute=true});
        using var registration=cancellationToken.Register(()=>{try{listener.Stop();}catch{}});
        var context=await listener.GetContextAsync();
        var returnedState=context.Request.QueryString["state"];
        var code=context.Request.QueryString["code"];
        var error=context.Request.QueryString["error"];
        var body=string.IsNullOrWhiteSpace(error)?"<html><body style='font-family:sans-serif;background:#0b1016;color:#fff'><h2>TransPoli SoundDrive conectado.</h2><p>Você pode voltar ao aplicativo.</p></body></html>":"<html><body>Spotify não autorizado.</body></html>";
        var bytes=Encoding.UTF8.GetBytes(body); context.Response.ContentType="text/html; charset=utf-8"; context.Response.ContentLength64=bytes.Length; await context.Response.OutputStream.WriteAsync(bytes,cancellationToken); context.Response.Close();
        if(!string.Equals(returnedState,state,StringComparison.Ordinal)||string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Autorização Spotify inválida ou cancelada.");
        using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",_settings.SpotifyClientId},{"grant_type","authorization_code"},{"code",code},{"redirect_uri",redirect},{"code_verifier",verifier}});
        using var tokenResponse=await _http.PostAsync("https://accounts.spotify.com/api/token",form,cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var doc=JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
        return doc.RootElement.GetProperty("access_token").GetString()??throw new InvalidOperationException("Spotify não retornou access token.");
    }

    private static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}
