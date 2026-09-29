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
    private float[] _manualEq = new float[5];
    private double _cabinIntensity = 100;
    private double _subIntensity = 100;
    private double _ambienceIntensity = 100;
    private (bool connected, bool engineEnabled, double speedKph, double rpm) _environment;
    public event Action<float, float>? LevelsChanged;
    public event Action? TrackEnded;

    public string Preset { get; private set; } = "NORMAL";

    public void OpenFile(string path)
    {
        Stop();
        DisposePipeline();
        _reader = new AudioFileReader(path);
        _eq = new CabinEqSampleProvider(_reader);
        _eq.LevelsChanged += (left, right) => LevelsChanged?.Invoke(left, right);
        _eq.SetPreset(Preset);
        _eq.SetManualEq(_manualEq);
        _eq.SetEffectIntensity(_cabinIntensity, _subIntensity, _ambienceIntensity);
        _eq.SetEnvironment(_environment.connected, _environment.engineEnabled, _environment.speedKph, _environment.rpm);
        _volume = new VolumeSampleProvider(_eq) { Volume = _volumeValue };
        _output = new WaveOutEvent();
        _output.PlaybackStopped += Output_PlaybackStopped;
        _output.Init(_volume);
        _output.Play();
    }

    private void Output_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is null && _reader is not null && _reader.Position >= _reader.Length)
            TrackEnded?.Invoke();
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
    {
        _environment = (connected, engineEnabled, speedKph, rpm);
        _eq?.SetEnvironment(connected, engineEnabled, speedKph, rpm);
    }

    public void SetManualEq(double bass, double lowMid, double mid, double presence, double treble)
    {
        _manualEq = new[] { (float)bass, (float)lowMid, (float)mid, (float)presence, (float)treble };
        _eq?.SetManualEq(_manualEq);
    }

    public void SetEffectIntensity(double cabinPercent, double subPercent, double ambiencePercent)
    {
        _cabinIntensity = cabinPercent;
        _subIntensity = subPercent;
        _ambienceIntensity = ambiencePercent;
        _eq?.SetEffectIntensity(cabinPercent, subPercent, ambiencePercent);
    }

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public void Seek(TimeSpan position)
    {
        if (_reader is null) return;
        _reader.CurrentTime = position < TimeSpan.Zero ? TimeSpan.Zero : position > _reader.TotalTime ? _reader.TotalTime : position;
    }

    public void Unload()
    {
        Stop();
        DisposePipeline();
    }

    private void DisposePipeline()
    {
        if (_output is not null) _output.PlaybackStopped -= Output_PlaybackStopped;
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
    private float[] _compressorEnvelopes = Array.Empty<float>();
    private float _environmentGain = 1f;
    private float _roomMix;
    private float[][] _delayLines = Array.Empty<float[]>();
    private int[] _delayPositions = Array.Empty<int>();
    private float[] _manualGains = new float[5];
    private float _cabinIntensity = 1f;
    private float _subIntensity = 1f;
    private float _ambienceIntensity = 1f;
    private string _preset = "NORMAL";
    public event Action<float, float>? LevelsChanged;

    public CabinEqSampleProvider(ISampleProvider source)
    {
        _source = source;
        BuildFilters("NORMAL");
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void SetPreset(string preset)
    {
        lock (_gate)
        {
            _preset = string.IsNullOrWhiteSpace(preset) ? "NORMAL" : preset.ToUpperInvariant();
            BuildFilters(_preset);
        }
    }

    public void SetManualEq(float[] gains)
    {
        if (gains.Length != 5) return;
        lock (_gate)
        {
            _manualGains = gains.Select(x => Math.Clamp(x, -12f, 12f)).ToArray();
            BuildFilters(_preset);
        }
    }

    public void SetEffectIntensity(double cabinPercent, double subPercent, double ambiencePercent)
    {
        lock (_gate)
        {
            _cabinIntensity = (float)Math.Clamp(cabinPercent / 100d, 0d, 1.5d);
            _subIntensity = (float)Math.Clamp(subPercent / 100d, 0d, 1.5d);
            _ambienceIntensity = (float)Math.Clamp(ambiencePercent / 100d, 0d, 1.5d);
            BuildFilters(_preset);
        }
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
            _roomMix = (float)((0.055 + 0.035 * speed) * _ambienceIntensity);
        }
    }

    private void BuildFilters(string preset)
    {
        var baseGains = preset switch
        {
            "CABINE" => new[] { 6.0f, 4.5f, 2.0f, -3.0f, -5.0f }.Select(x => x * _cabinIntensity).ToArray(),
            "SUBWOOFER" => new[] { 11.0f, 8.0f, -1.5f, -3.0f, -4.0f }.Select(x => x * _subIntensity).ToArray(),
            "NOTURNO" => new[] { 2.0f, 1.0f, -1.0f, -4.0f, -7.0f },
            _ => new[] { 0f, 0f, 0f, 0f, 0f }
        };
        var gains = baseGains.Select((x, i) => Math.Clamp(x + _manualGains[i], -12f, 12f)).ToArray();
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
            _compressorEnvelopes = new float[channels];
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
            var peaks = new float[Math.Min(2, channels)];
            var manualActive = _manualGains.Any(x => Math.Abs(x) > 0.01f);
            for (var n = 0; n < read; n++)
            {
                var channel = _channelCursor;
                _channelCursor = (_channelCursor + 1) % channels;
                var sample = buffer[offset + n];
                // NORMAL é bypass real para a comparação A/B ser audível e honesta.
                if (_preamp == 1f && _drive == 1f && !manualActive)
                {
                    buffer[offset + n] = sample;
                    if (channel < peaks.Length) peaks[channel] = Math.Max(peaks[channel], Math.Abs(sample));
                    continue;
                }
                sample *= _preamp;
                foreach (var filter in _filters[channel]) sample = filter.Transform(sample);

                // Compressor feed-forward com envelope suavizado.
                var absolute = MathF.Abs(sample);
                var attack = 0.22f;
                var release = 0.012f;
                var envelope = _compressorEnvelopes.Length > channel ? _compressorEnvelopes[channel] : 0f;
                envelope += (absolute - envelope) * (absolute > envelope ? attack : release);
                if (_compressorEnvelopes.Length > channel) _compressorEnvelopes[channel] = envelope;
                var threshold = 0.48f;
                var ratio = 4.0f;
                if (envelope > threshold)
                {
                    var target = threshold + (envelope - threshold) / ratio;
                    sample *= target / Math.Max(envelope, 0.0001f);
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
                if (channel < peaks.Length) peaks[channel] = Math.Max(peaks[channel], Math.Abs(buffer[offset + n]));
            }
            if (peaks.Length > 0) LevelsChanged?.Invoke(peaks[0], peaks.Length > 1 ? peaks[1] : peaks[0]);
        }
        return read;
    }
}