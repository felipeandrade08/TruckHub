using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TransPoli.Audio;

internal sealed class CabinAudioEngine : IDisposable
{
    private AudioFileReader? _reader;
    private WaveOutEvent? _output;
    private CabinEqSampleProvider? _eq;
    private VolumeSampleProvider? _volume;
    private float _volumeValue = 0.70f;

    public string Preset { get; private set; } = "NORMAL";

    public void OpenFile(string path)
    {
        Stop();
        DisposePipeline();
        _reader = new AudioFileReader(path);
        _eq = new CabinEqSampleProvider(_reader);
        _eq.SetPreset(Preset);
        _volume = new VolumeSampleProvider(_eq) { Volume = _volumeValue };
        _output = new WaveOutEvent();
        _output.Init(_volume);
        _output.Play();
    }

    public void Play()
    {
        if (_reader is null || _output is null) return;
        if (_reader.Position >= _reader.Length) _reader.Position = 0;
        _output.Play();
    }

    public void Pause() => _output?.Pause();

    public void Stop()
    {
        if (_output is not null) _output.Stop();
        if (_reader is not null) _reader.Position = 0;
    }

    public void SetVolume(double percent)
    {
        _volumeValue = (float)Math.Clamp(percent / 100d, 0d, 1d);
        if (_volume is not null) _volume.Volume = _volumeValue;
    }

    public void SetPreset(string preset)
    {
        Preset = string.IsNullOrWhiteSpace(preset) ? "NORMAL" : preset.ToUpperInvariant();
        _eq?.SetPreset(Preset);
    }

    private void DisposePipeline()
    {
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        _eq = null;
        _volume = null;
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
        DisposePipeline();
    }
}

internal sealed class CabinEqSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly object _gate = new();
    private BiQuadFilter[][] _filters = Array.Empty<BiQuadFilter[]>();
    private int _channelCursor;

    public CabinEqSampleProvider(ISampleProvider source)
    {
        _source = source;
        BuildFilters("NORMAL");
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void SetPreset(string preset)
    {
        lock (_gate) BuildFilters(preset);
    }

    private void BuildFilters(string preset)
    {
        var gains = preset switch
        {
            "CABINE" => new[] { 3.0f, 2.0f, 0.5f, -1.5f, -2.0f },
            "SUBWOOFER" => new[] { 6.0f, 4.0f, 0.0f, -1.0f, -1.5f },
            "NOTURNO" => new[] { 1.0f, 0.5f, 0.0f, -1.0f, -2.5f },
            _ => new[] { 0f, 0f, 0f, 0f, 0f }
        };
        var frequencies = new[] { 70f, 160f, 800f, 3500f, 10000f };
        var channels = Math.Max(1, WaveFormat.Channels);
        _filters = new BiQuadFilter[channels][];
        for (var ch = 0; ch < channels; ch++)
        {
            _filters[ch] = new BiQuadFilter[frequencies.Length];
            for (var band = 0; band < frequencies.Length; band++)
                _filters[ch][band] = BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, frequencies[band], 0.9f, gains[band]);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        lock (_gate)
        {
            var channels = Math.Max(1, WaveFormat.Channels);
            for (var n = 0; n < read; n++)
            {
                var channel = _channelCursor;
                _channelCursor = (_channelCursor + 1) % channels;
                var sample = buffer[offset + n];
                foreach (var filter in _filters[channel]) sample = filter.Transform(sample);
                // Headroom/soft limiter simples para impedir clipping dos boosts de grave.
                buffer[offset + n] = MathF.Tanh(sample * 0.92f);
            }
        }
        return read;
    }
}