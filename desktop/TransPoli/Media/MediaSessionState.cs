namespace TransPoli.Media;

public sealed record MediaNowPlaying(
    string Title,
    string Artist,
    string Source,
    double Volume,
    bool IsPlaying,
    string Perspective,
    string Artwork = "",
    double PositionSeconds = 0,
    double DurationSeconds = 0);

public static class MediaSessionState
{
    private static readonly object Gate = new();
    private static MediaNowPlaying _current = new("", "", "", 70, false, "CABIN");

    public static event Action<MediaNowPlaying>? Changed;

    public static MediaNowPlaying Current
    {
        get { lock (Gate) return _current; }
    }

    public static void Publish(MediaNowPlaying value)
    {
        lock (Gate) _current = value;
        Changed?.Invoke(value);
    }

    public static void Update(Func<MediaNowPlaying, MediaNowPlaying> update)
    {
        MediaNowPlaying value;
        lock (Gate) { _current = update(_current); value = _current; }
        Changed?.Invoke(value);
    }
}
