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

    public void SetEnvironment(bool connected, bool engineEnabled, double speedKph, double rpm)
        => _eq?.SetEnvironment(connected, engineEnabled, speedKph, rpm);

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
    private float _preamp = 1f;
    private float _drive = 1f;
    private float _compressorEnvelope;
    private float _environmentGain = 1f;
    private float _roomMix;
    private float[][] _delayLines = Array.Empty<float[]>();
    private int[] _delayPositions = Array.Empty<int>();

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

    public void SetEnvironment(bool connected, bool engineEnabled, double speedKph, double rpm)
    {
        lock (_gate)
        {
            if (!connected)
            {
                _environmentGain = 1f;
                _roomMix = 0f;
                return;
            }
            var speed = Math.Clamp(Math.Abs(speedKph) / 90d, 0d, 1d);
            var engine = engineEnabled ? Math.Clamp(rpm / 1800d, 0d, 1d) : 0d;
            // Pequena compensação de mascaramento do motor/rodagem, sem pumping por tick.
            _environmentGain = (float)(1.0 + 0.045 * speed + 0.025 * engine);
            _roomMix = (float)(0.055 + 0.035 * speed);
        }
    }

    private void BuildFilters(string preset)
    {
        var gains = preset switch
        {
            "CABINE" => new[] { 6.0f, 4.5f, 2.0f, -3.0f, -5.0f },
            "SUBWOOFER" => new[] { 11.0f, 8.0f, -1.5f, -3.0f, -4.0f },
            "NOTURNO" => new[] { 2.0f, 1.0f, -1.0f, -4.0f, -7.0f },
            _ => new[] { 0f, 0f, 0f, 0f, 0f }
        };
        (_preamp, _drive) = preset switch
        {
            "CABINE" => (0.72f, 1.18f),
            "SUBWOOFER" => (0.52f, 1.35f),
            "NOTURNO" => (0.78f, 1.05f),
            _ => (1.0f, 1.0f)
        };
        var frequencies = new[] { 65f, 145f, 850f, 3800f, 10500f };
        var channels = Math.Max(1, WaveFormat.Channels);
        _filters = new BiQuadFilter[channels][];
        var delaySamples = Math.Max(1, (int)(WaveFormat.SampleRate * 0.017));
        if (_delayLines.Length != channels || _delayLines.Any(x => x.Length != delaySamples))
        {
            _delayLines = Enumerable.Range(0, channels).Select(_ => new float[delaySamples]).ToArray();
            _delayPositions = new int[channels];
        }
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
                // NORMAL é bypass real para a comparação A/B ser audível e honesta.
                if (_preamp == 1f && _drive == 1f)
                {
                    buffer[offset + n] = sample;
                    continue;
                }
                sample *= _preamp;
                foreach (var filter in _filters[channel]) sample = filter.Transform(sample);

                // Compressor feed-forward com envelope suavizado.
                var absolute = MathF.Abs(sample);
                var attack = 0.22f;
                var release = 0.012f;
                _compressorEnvelope += (absolute - _compressorEnvelope) * (absolute > _compressorEnvelope ? attack : release);
                var threshold = 0.48f;
                var ratio = 4.0f;
                if (_compressorEnvelope > threshold)
                {
                    var target = threshold + (_compressorEnvelope - threshold) / ratio;
                    sample *= target / Math.Max(_compressorEnvelope, 0.0001f);
                }

                // Reflexão curta (~17 ms): sensação de superfícies próximas, não reverb de salão.
                if (_roomMix > 0f && _delayLines.Length > channel)
                {
                    var pos = _delayPositions[channel];
                    var delayed = _delayLines[channel][pos];
                    _delayLines[channel][pos] = sample + delayed * 0.16f;
                    _delayPositions[channel] = (pos + 1) % _delayLines[channel].Length;
                    sample = sample * (1f - _roomMix) + delayed * _roomMix;
                }

                sample *= _environmentGain;
                // Limiter final independente do compressor.
                var limited = MathF.Tanh(sample * _drive);
                buffer[offset + n] = Math.Clamp(limited / MathF.Tanh(_drive), -0.985f, 0.985f);
            }
        }
        return read;
    }
}